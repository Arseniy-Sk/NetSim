using NetSim.Core.Catalog;
using NetSim.Core.Model;

namespace NetSim.Core.Topology;

/// <summary>
/// Операции над топологией, общие для интерфейса, примеров и тестов:
/// создание устройств из каталога, соединение, установка ПО, назначение трафика.
/// </summary>
public sealed class TopologyBuilder
{
    public DeviceCatalog Catalog { get; }
    public Project Project { get; }
    public string Language { get; set; } = "ru";

    public TopologyBuilder(Project project, DeviceCatalog? catalog = null)
    {
        Project = project;
        Catalog = catalog ?? DeviceCatalog.Default;
    }

    /// <summary>Короткое базовое имя для автоматического именования (PC, Server, Switch...).</summary>
    public static string BaseName(CatalogDevice cd) => cd.Kind switch
    {
        DeviceKind.Workstation => cd.Icon == "notebook" ? "Notebook" : "PC",
        DeviceKind.Server => "Server",
        DeviceKind.Printer => "Printer",
        DeviceKind.Hub => "Hub",
        DeviceKind.Switch => "Switch",
        DeviceKind.Router => "Router",
        DeviceKind.Bridge => "Bridge",
        DeviceKind.AccessPoint => "AP",
        DeviceKind.Cloud => cd.NameEn,
        DeviceKind.Segment => cd.NameEn,
        DeviceKind.Container => cd.NameEn,
        _ => cd.NameEn,
    };

    public Device CreateDevice(string catalogId, double x, double y, Level? level = null, string? name = null)
    {
        var cd = Catalog.Device(catalogId) ?? throw new ArgumentException($"Нет модели «{catalogId}» в каталоге");
        var device = new Device
        {
            CatalogId = cd.Id,
            Kind = cd.Kind,
            Name = name ?? Project.UniqueName(BaseName(cd)),
            X = x,
            Y = y,
        };
        device.Network.Hostname = device.Name.Replace(" ", "");
        foreach (var sw in cd.PreinstalledSoftware) InstallSoftware(device, sw);
        if (cd.Kind == DeviceKind.Container)
            device.ChildLevel = new Level { Name = device.Name, Background = cd.ChildBackground ?? "plain" };
        (level ?? Project.Root).Devices.Add(device);
        return device;
    }

    /// <summary>Установить ПО (перетаскивание из группы Network and enterprise software).</summary>
    public InstalledSoftware InstallSoftware(Device device, string softwareId, params string[] services)
    {
        var cs = Catalog.SoftwareById(softwareId) ?? throw new ArgumentException($"Нет ПО «{softwareId}» в каталоге");
        var existing = device.Software.FirstOrDefault(s => s.SoftwareId == softwareId);
        if (existing == null)
        {
            existing = new InstalledSoftware { SoftwareId = softwareId };
            foreach (var s in cs.DefaultServices) existing.EnabledServices.Add(s);
            device.Software.Add(existing);
        }
        foreach (var s in services)
            if (cs.Services.Contains(s) && !existing.EnabledServices.Contains(s))
                existing.EnabledServices.Add(s);
        return existing;
    }

    /// <summary>Соединить два устройства (Link Assistant). Если спецификация не задана — подбирается автоматически.</summary>
    public Link Connect(Device a, Device b, string? standardId = null, double lengthM = 10, Level? level = null, string? mediaId = null)
    {
        var ca = Catalog.Device(a.CatalogId);
        var cb = Catalog.Device(b.CatalogId);
        var std = standardId != null ? Catalog.Standard(standardId) : Catalog.SuggestStandard(ca, cb);
        std ??= Catalog.Standards.First();

        var link = new Link
        {
            AId = a.Id,
            BId = b.Id,
            StandardId = std.Id,
            MediaId = mediaId ?? std.Media.FirstOrDefault() ?? "utp5",
            BandwidthBps = std.BandwidthBps,
            LengthM = lengthM,
        };

        level ??= FindLinkLevel(a, b);
        link.AInterface = NextInterfaceName(a, ca, level);
        link.BInterface = NextInterfaceName(b, cb, level);
        EnsureInterface(a, link.AInterface);
        EnsureInterface(b, link.BInterface);
        level.Links.Add(link);
        return link;
    }

    /// <summary>
    /// Уровень, на котором хранится линия: общий уровень обоих устройств или
    /// вложенный уровень контейнера, если одно из устройств — сам контейнер-владелец.
    /// </summary>
    public Level FindLinkLevel(Device a, Device b)
    {
        var la = Project.FindLevelOf(a);
        var lb = Project.FindLevelOf(b);
        if (la != null && la == lb) return la;
        if (a.ChildLevel != null && lb == a.ChildLevel) return lb;
        if (b.ChildLevel != null && la == b.ChildLevel) return la;
        return la ?? lb ?? Project.Root;
    }

    private string NextInterfaceName(Device device, CatalogDevice? cd, Level level)
    {
        var used = Project.AllLinks()
            .SelectMany(l => new[] { (l.AId, l.AInterface), (l.BId, l.BInterface) })
            .Where(x => x.Item1 == device.Id)
            .Select(x => x.Item2)
            .ToHashSet();
        var prefix = cd?.InterfacePrefix ?? "Port";
        int start = prefix.EndsWith('/') ? 1 : 0;
        for (int i = start; ; i++)
        {
            var name = $"{prefix}{i}";
            if (!used.Contains(name)) return name;
        }
    }

    private static void EnsureInterface(Device device, string name)
    {
        if (device.Network.Interfaces.Any(i => i.Name == name)) return;
        device.Network.Interfaces.Add(new NetInterface
        {
            Name = name,
            MacAddress = RandomMac(),
            IsUp = device.Kind != DeviceKind.Router, // как в Cisco: интерфейсы маршрутизатора выключены
        });
    }

    private static readonly Random MacRng = new();
    private static string RandomMac()
    {
        var b = new byte[6];
        lock (MacRng) MacRng.NextBytes(b);
        b[0] = (byte)((b[0] & 0xFC) | 0x02); // локально администрируемый unicast
        return string.Join("", new[] { b[0], b[1], b[2], b[3], b[4], b[5] }
            .Select(x => x.ToString("X2"))).Insert(4, ".").Insert(9, ".");
    }

    /// <summary>Назначить поток (Set traffic). Возвращает null и причину, если профиль требует службу, а её нет.</summary>
    public (TrafficFlow? Flow, string? Error) AddFlow(Device source, Device target, string profileId)
    {
        var profile = Catalog.Profile(profileId) ?? throw new ArgumentException($"Нет профиля «{profileId}»");
        return AddFlow(source, target, profile.Clone());
    }

    public (TrafficFlow? Flow, string? Error) AddFlow(Device source, Device target, TrafficProfile profile)
    {
        var error = ValidateFlow(source, target, profile);
        if (error != null) return (null, error);
        var flow = new TrafficFlow { SourceId = source.Id, TargetId = target.Id, Profile = profile };
        Project.Flows.Add(flow);
        return (flow, null);
    }

    public string? ValidateFlow(Device source, Device target, TrafficProfile profile)
    {
        bool ru = Language != "en";
        if (source == target) return ru ? "Источник и приёмник совпадают." : "Source and destination are the same.";
        if (source.IsContainer || target.IsContainer)
            return ru ? "Трафик назначается между устройствами, а не контейнерами." : "Traffic must connect devices, not containers.";
        if (profile.Pattern == TrafficPattern.ClientServer && profile.RequiredService is { } svc && !target.HasService(svc))
        {
            var svcName = Catalog.Service(svc)?.Name(Language) ?? svc;
            return ru
                ? $"На «{target.Name}» не установлено серверное ПО со службой «{svcName}». Перетащите ПО из группы Network and enterprise software на устройство и включите службу."
                : $"\"{target.Name}\" has no server software with the \"{svcName}\" service. Drag software from Network and enterprise software onto the device and enable the service.";
        }
        return null;
    }
}
