using NetSim.Core.Catalog;
using NetSim.Core.Model;
using NetSim.Core.Topology;
using NetSim.Protocols.Ip;
using NetSim.Protocols.Nodes;
using NetSim.Simulation.Engine;

namespace NetSim.Protocols;

/// <summary>Запись журнала событий (как список Event List в режиме Simulation у Packet Tracer).</summary>
public sealed record ProtocolEvent(double Time, string Device, string Protocol, string Info, bool Dropped = false);

/// <summary>Кадр в линии — для анимации.</summary>
public sealed class FrameInFlight
{
    public Link Link { get; init; } = null!;
    public string FromDeviceId { get; init; } = "";
    public string ToDeviceId { get; init; } = "";
    public Frame Frame { get; init; } = null!;
    public double StartTime { get; init; }
    public double EndTime { get; init; }
    public double Progress(double now) => EndTime <= StartTime ? 1 : Math.Clamp((now - StartTime) / (EndTime - StartTime), 0, 1);
}

/// <summary>
/// Режим протоколов (заготовка под функциональность Cisco Packet Tracer):
/// кадры Ethernet, ARP, IPv4, ICMP, обучение коммутаторов, статическая маршрутизация.
/// Новые протоколы (DHCP, RIP, OSPF, VLAN trunking, ACL...) добавляются как новые узлы/обработчики.
/// </summary>
public sealed class ProtocolSimulator
{
    public SimEngine Engine { get; } = new();
    public Project Project { get; }
    public DeviceCatalog Catalog { get; }
    public NetworkGraph Graph { get; private set; } = null!;

    /// <summary>Замедление для анимации: время обработки кадра на устройстве, с.</summary>
    public double ProcessingDelay { get; set; } = 1e-4;

    public List<ProtocolEvent> Events { get; } = new();
    public HashSet<FrameInFlight> InFlight { get; } = new();
    public event Action<ProtocolEvent>? EventLogged;

    private readonly Dictionary<string, NetNode> _nodes = new();

    public ProtocolSimulator(Project project, DeviceCatalog? catalog = null)
    {
        Project = project;
        Catalog = catalog ?? DeviceCatalog.Default;
        Rebuild();
    }

    /// <summary>Пересоздать узлы по текущей топологии и конфигурации (сбрасывает ARP- и MAC-таблицы).</summary>
    public void Rebuild()
    {
        Engine.Reset();
        Events.Clear();
        InFlight.Clear();
        _nodes.Clear();
        Graph = new NetworkGraph(Project, Catalog);
        foreach (var d in Graph.Devices.Values) _nodes[d.Id] = NetNode.Create(this, d);
        foreach (var link in Graph.Links)
        {
            var a = _nodes[link.AId];
            var b = _nodes[link.BId];
            var pa = a.AddPort(link, link.AInterface);
            var pb = b.AddPort(link, link.BInterface);
            pa.Peer = pb;
            pb.Peer = pa;
        }
    }

    public NetNode? Node(Device d) => _nodes.GetValueOrDefault(d.Id);

    public void Log(NetNode node, string protocol, string info, bool dropped = false)
    {
        var e = new ProtocolEvent(Engine.Now, node.Device.Name, protocol, info, dropped);
        Events.Add(e);
        if (Events.Count > 5000) Events.RemoveRange(0, 1000);
        EventLogged?.Invoke(e);
    }

    /// <summary>Передать кадр в линию: через время передачи он придёт на порт соседа.</summary>
    internal void Transmit(Port from, Frame frame)
    {
        var peer = from.Peer;
        if (peer == null || !from.IsUp || !peer.IsUp) return;
        var std = Catalog.Standard(from.Link.StandardId);
        double bw = from.Link.BandwidthBps > 0 ? from.Link.BandwidthBps : std?.BandwidthBps ?? 100e6;
        double delay = frame.Bytes * 8 / bw + from.Link.LengthM * (std?.PropagationNsPerM ?? 5) * 1e-9 + ProcessingDelay;
        var flight = new FrameInFlight
        {
            Link = from.Link, FromDeviceId = from.Node.Device.Id, ToDeviceId = peer.Node.Device.Id, Frame = frame,
            StartTime = Engine.Now, EndTime = Engine.Now + delay,
        };
        InFlight.Add(flight);
        Engine.After(delay, () =>
        {
            InFlight.Remove(flight);
            peer.Node.Receive(peer, frame);
        });
    }

    /// <summary>
    /// Выполнить ping и дождаться результата (модельное время прокручивается мгновенно).
    /// Возвращает строки вывода в стиле Windows (ПК) или Cisco IOS (маршрутизатор/коммутатор).
    /// </summary>
    public IReadOnlyList<string> PingBlocking(Device source, string target, int count = 4, double timeoutSec = 2)
    {
        var output = new List<string>();
        if (Node(source) is not IpNode node)
        {
            output.Add("% Устройство не поддерживает IP / device has no IP stack");
            return output;
        }
        var resolved = ResolveBlocking(source, target);
        if (resolved is not { } dst)
        {
            output.Add(source.Kind is DeviceKind.Router or DeviceKind.Switch
                ? $"% Unrecognized host or address, or protocol not running: {target}"
                : $"Ping request could not find host {target}. Please check the name and try again.");
            return output;
        }
        bool cisco = source.Kind is DeviceKind.Router or DeviceKind.Switch or DeviceKind.Firewall or DeviceKind.Gateway;
        // Пинг из консоли выполняется мгновенно и с реальными задержками — без замедления анимации.
        var savedDelay = ProcessingDelay;
        ProcessingDelay = 1e-4;
        PingSession session;
        try
        {
            session = node.StartPing(dst, count, timeoutSec);
            Engine.RunUntil(Engine.Now + count * (timeoutSec + 1) + 1);
        }
        finally
        {
            ProcessingDelay = savedDelay;
        }
        output.AddRange(cisco ? session.FormatCisco() : session.FormatWindows());
        return output;
    }

    /// <summary>Выполнить действие с мгновенной прокруткой модельного времени (без замедления анимации).</summary>
    private void RunFast(Action start, double seconds)
    {
        var savedDelay = ProcessingDelay;
        ProcessingDelay = 1e-4;
        try
        {
            start();
            Engine.RunUntil(Engine.Now + seconds);
        }
        finally
        {
            ProcessingDelay = savedDelay;
        }
    }

    /// <summary>Имя → IP через DNS-сервер устройства (IP-адрес возвращается как есть).</summary>
    public Ipv4? ResolveBlocking(Device source, string name)
    {
        if (Ipv4.TryParse(name, out var ip)) return ip;
        if (Node(source) is not IpNode node) return null;
        Ipv4? result = null;
        RunFast(() => node.Resolve(name, r => result = r), 4);
        return result;
    }

    /// <summary>ipconfig /renew: получить адрес по DHCP.</summary>
    public IReadOnlyList<string> DhcpRenewBlocking(Device source)
    {
        var output = new List<string>();
        if (Node(source) is not IpNode node) { output.Add("% No IP stack"); return output; }
        DhcpSession? session = null;
        RunFast(() => session = node.StartDhcp(), 6);
        if (session == null) output.Add("DHCP: нет подключённых интерфейсов / no connected interfaces.");
        else if (!session.Success) output.Add("DHCP request failed. / Не удалось получить адрес: DHCP-сервер не ответил.");
        else
        {
            output.Add($"   IPv4 Address. . . . . . . . . . . : {session.Address}");
            output.Add($"   Subnet Mask . . . . . . . . . . . : {session.Mask}");
            output.Add($"   Default Gateway . . . . . . . . . : {(session.Router.Value == 0 ? "0.0.0.0" : session.Router.ToString())}");
            output.Add($"   DNS Server. . . . . . . . . . . . : {(session.Dns.Value == 0 ? "0.0.0.0" : session.Dns.ToString())}");
            output.Add($"   DHCP Server . . . . . . . . . . . : {session.Server}");
        }
        return output;
    }

    /// <summary>tracert / traceroute.</summary>
    public IReadOnlyList<string> TraceBlocking(Device source, string target, bool cisco)
    {
        var output = new List<string>();
        if (Node(source) is not IpNode node) { output.Add("% No IP stack"); return output; }
        var dst = ResolveBlocking(source, target);
        if (dst == null) { output.Add($"Unable to resolve target system name {target}."); return output; }
        if (cisco) output.Add("Type escape sequence to abort.");
        output.Add(cisco ? $"Tracing the route to {dst}" : $"Tracing route to {dst} over a maximum of 30 hops:");
        output.Add("");
        TraceSession? session = null;
        RunFast(() => session = node.StartTrace(dst.Value, 30, 2), 30 * 2.5);
        foreach (var (ttl, hop, rtt) in session!.Hops)
        {
            var ms = rtt.HasValue ? (rtt.Value * 1000 < 1 ? "<1 ms" : $"{rtt.Value * 1000:0} ms") : "*";
            output.Add(cisco ? $"  {ttl,2}   {hop?.ToString() ?? "*"}   {ms}" : $"  {ttl,2}   {ms,7}  {ms,7}  {ms,7}   {hop?.ToString() ?? "Request timed out."}");
        }
        output.Add("");
        output.Add(session.Reached ? (cisco ? "" : "Trace complete.") : "Destination not reached.");
        return output;
    }

    /// <summary>Отправить один эхо-запрос с анимацией (инструмент «Простой PDU»).</summary>
    public PingSession? SendSimplePdu(Device source, Device target)
    {
        if (Node(source) is not IpNode node || Node(target) is not IpNode t) return null;
        var ip = t.Ports.Select(p => p.Ip).FirstOrDefault(ip => ip.HasValue);
        // Тайм-аут с запасом: в анимации каждый переход замедлен (ProcessingDelay).
        return ip == null ? null : node.StartPing(ip.Value, 1, 3 + 60 * ProcessingDelay);
    }
}
