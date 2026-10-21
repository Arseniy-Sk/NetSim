using NetSim.Core.Catalog;
using NetSim.Core.Model;

namespace NetSim.Core.Topology;

/// <summary>
/// Канал передачи — то, что занимают пакеты: либо отдельная линия (порт коммутатора, маршрутизатора),
/// либо целый домен коллизий (все линии хаба, сегмента шины/кольца, радиоканал точки доступа).
/// </summary>
public sealed class Channel
{
    public int Index { get; init; }
    public bool IsShared { get; init; }

    /// <summary>Устройство, образующее общую среду (хаб, сегмент, точка доступа), для общих каналов.</summary>
    public List<Device> SharedDevices { get; } = new();
    public List<Link> Links { get; } = new();

    /// <summary>Пропускная способность канала, бит/с (для общего — минимальная из линий).</summary>
    public double BandwidthBps { get; set; }

    public int Mtu { get; set; } = 1500;

    public string DisplayName => IsShared && SharedDevices.Count > 0
        ? string.Join(" + ", SharedDevices.Select(d => d.Name))
        : Links.Count > 0 ? $"{Links[0].Id}" : $"#{Index}";
}

/// <summary>
/// «Плоское» представление проекта: все устройства всех уровней и все линии.
/// Контейнеры (город, здание) — прозрачные узлы: линии снаружи и изнутри сходятся в них.
/// </summary>
public sealed class NetworkGraph
{
    public Project Project { get; }
    public DeviceCatalog Catalog { get; }

    public IReadOnlyDictionary<string, Device> Devices => _devices;
    public IReadOnlyList<Link> Links => _links;
    public IReadOnlyList<Channel> Channels => _channels;

    private readonly Dictionary<string, Device> _devices = new();
    private readonly List<Link> _links = new();
    private readonly List<Channel> _channels = new();
    private readonly Dictionary<string, List<Link>> _adjacency = new();
    private readonly Dictionary<Link, Channel> _channelOfLink = new();

    public NetworkGraph(Project project, DeviceCatalog? catalog = null)
    {
        Project = project;
        Catalog = catalog ?? DeviceCatalog.Default;
        foreach (var d in project.AllDevices())
        {
            _devices[d.Id] = d;
            _adjacency[d.Id] = new List<Link>();
        }
        foreach (var l in project.AllLinks())
        {
            if (!_devices.ContainsKey(l.AId) || !_devices.ContainsKey(l.BId)) continue;
            _links.Add(l);
            _adjacency[l.AId].Add(l);
            _adjacency[l.BId].Add(l);
        }
        BuildChannels();
    }

    public IReadOnlyList<Link> LinksOf(string deviceId) => _adjacency.TryGetValue(deviceId, out var l) ? l : Array.Empty<Link>();

    public Channel ChannelOf(Link link) => _channelOfLink[link];

    public MediaStandard? StandardOf(Link link) => Catalog.Standard(link.StandardId);

    /// <summary>Входит ли линия в общую среду устройства (для точки доступа — только радиолинии).</summary>
    public bool IsSharedAttachment(Device device, Link link)
    {
        if (!device.Kind.IsSharedMedium()) return false;
        if (device.Kind != DeviceKind.AccessPoint) return true;
        return StandardOf(link)?.Family == TechFamily.Wireless;
    }

    private void BuildChannels()
    {
        // Объединение линий в домены коллизий: линии, подключённые к одной общей среде,
        // и цепочки хабов, соединённых между собой (повторители расширяют домен).
        var parent = new Dictionary<Link, Link>();
        Link Find(Link x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        void Union(Link a, Link b) { var ra = Find(a); var rb = Find(b); if (ra != rb) parent[ra] = rb; }

        foreach (var l in _links) parent[l] = l;
        var sharedOwners = new Dictionary<Link, HashSet<Device>>();
        foreach (var d in _devices.Values)
        {
            var attached = _adjacency[d.Id].Where(l => IsSharedAttachment(d, l)).ToList();
            for (int i = 1; i < attached.Count; i++) Union(attached[0], attached[i]);
            foreach (var l in attached)
            {
                if (!sharedOwners.TryGetValue(l, out var set)) sharedOwners[l] = set = new HashSet<Device>();
                set.Add(d);
            }
        }

        var byRoot = new Dictionary<Link, Channel>();
        foreach (var l in _links)
        {
            var root = Find(l);
            if (!byRoot.TryGetValue(root, out var ch))
            {
                ch = new Channel { Index = _channels.Count, IsShared = sharedOwners.ContainsKey(l), BandwidthBps = double.MaxValue, Mtu = int.MaxValue };
                byRoot[root] = ch;
                _channels.Add(ch);
            }
            ch.Links.Add(l);
            if (sharedOwners.TryGetValue(l, out var owners))
                foreach (var o in owners) if (!ch.SharedDevices.Contains(o)) ch.SharedDevices.Add(o);
            ch.BandwidthBps = Math.Min(ch.BandwidthBps, l.BandwidthBps > 0 ? l.BandwidthBps : StandardOf(l)?.BandwidthBps ?? 10e6);
            ch.Mtu = Math.Min(ch.Mtu, StandardOf(l)?.Mtu ?? 1500);
            _channelOfLink[l] = ch;
        }
        foreach (var ch in _channels.Where(c => c.Mtu == int.MaxValue)) ch.Mtu = 1500;
    }

    /// <summary>Может ли устройство пересылать чужие пакеты (не конечный узел).</summary>
    public static bool IsTransit(Device d) => !d.Kind.IsEndNode();

    /// <summary>
    /// Кратчайший по числу переходов маршрут между двумя устройствами.
    /// Конечные узлы (ПК, серверы, принтеры) не используются как транзитные.
    /// Возвращает последовательность линий или null, если пути нет.
    /// </summary>
    public List<Link>? FindPath(string fromId, string toId)
    {
        if (fromId == toId) return new List<Link>();
        var prev = new Dictionary<string, Link>();
        var visited = new HashSet<string> { fromId };
        var queue = new Queue<string>();
        queue.Enqueue(fromId);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var link in _adjacency[cur])
            {
                var next = link.Other(cur);
                if (!visited.Add(next)) continue;
                prev[next] = link;
                if (next == toId)
                {
                    var path = new List<Link>();
                    for (var at = toId; at != fromId;)
                    {
                        var l = prev[at];
                        path.Add(l);
                        at = l.Other(at);
                    }
                    path.Reverse();
                    return path;
                }
                if (IsTransit(_devices[next])) queue.Enqueue(next);
            }
        }
        return null;
    }

    /// <summary>
    /// Разбить маршрут на участки по каналам: несколько линий одного домена коллизий
    /// (ПК → хаб → ПК) передаются за одну передачу.
    /// </summary>
    public List<PathHop> SplitIntoHops(string fromId, List<Link> path)
    {
        var hops = new List<PathHop>();
        var at = fromId;
        foreach (var link in path)
        {
            var ch = ChannelOf(link);
            var next = link.Other(at);
            if (hops.Count > 0 && hops[^1].Channel == ch && ch.IsShared)
            {
                hops[^1].Links.Add(link);
                hops[^1].ToId = next;
            }
            else
            {
                hops.Add(new PathHop { Channel = ch, FromId = at, ToId = next, Links = { link } });
            }
            at = next;
        }
        return hops;
    }
}

public sealed class PathHop
{
    public Channel Channel { get; init; } = null!;
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";
    public List<Link> Links { get; } = new();
    public double LengthM => Links.Sum(l => l.LengthM);
}
