using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace NetSim.Core.Model;

/// <summary>
/// Роль устройства. По ней движки моделирования решают, как устройство обрабатывает трафик.
/// </summary>
public enum DeviceKind
{
    Workstation,
    Server,
    Printer,
    Equipment,      // периферия, мед. оборудование, терминалы и т.п. — конечный узел
    Phone,
    Mainframe,
    Hub,            // повторитель — общая среда (домен коллизий)
    Segment,        // Generic LAN: шина (Thick/Thin Ethernet), кольцо (Token Ring, FDDI)
    AccessPoint,    // беспроводная точка доступа — общая радиосреда
    Switch,
    Bridge,
    Router,
    Gateway,
    Firewall,
    Modem,
    DsuCsu,
    Frad,
    AccessServer,
    Multiplexer,
    Pbx,
    Cloud,          // глобальные сети: PSTN, ISDN, Frame Relay, ATM, X.25, Интернет, спутник
    Container,      // уровень иерархии: страна, город, здание, этаж, комната (ПР6)
}

public static class DeviceKindExtensions
{
    /// <summary>Конечный узел — источник/приёмник трафика, не пересылает чужие пакеты.</summary>
    public static bool IsEndNode(this DeviceKind k) => k is DeviceKind.Workstation or DeviceKind.Server
        or DeviceKind.Printer or DeviceKind.Equipment or DeviceKind.Phone or DeviceKind.Mainframe;

    /// <summary>Разделяемая среда: все подключённые линии образуют один домен коллизий.</summary>
    public static bool IsSharedMedium(this DeviceKind k) => k is DeviceKind.Hub or DeviceKind.Segment or DeviceKind.AccessPoint;

    public static bool IsContainer(this DeviceKind k) => k == DeviceKind.Container;
}

/// <summary>Установленное на устройство программное обеспечение и включённые в нём службы ("Plugin Setup").</summary>
public sealed class InstalledSoftware : ObservableBase
{
    private string _softwareId = "";
    public string SoftwareId { get => _softwareId; set => Set(ref _softwareId, value); }

    /// <summary>Идентификаторы включённых служб (db, cad, file, http, ftp, pop3...).</summary>
    public ObservableCollection<string> EnabledServices { get; set; } = new();
}

/// <summary>Параметры ответа сервера (вкладка Server): Reply Size (байт) и Reply Delay (с).</summary>
public sealed class ServerSettings : ObservableBase
{
    public Distribution ReplySize { get; set; } = Distribution.Constant(2048);
    public Distribution ReplyDelay { get; set; } = Distribution.Constant(0.01);
}

/// <summary>Сетевой интерфейс (заготовка под режим протоколов, как в Packet Tracer).</summary>
public sealed class NetInterface : ObservableBase
{
    private string _name = "";
    private string _ipAddress = "";
    private string _subnetMask = "";
    private string _macAddress = "";
    private bool _isUp = true;
    private int _vlan = 1;
    private string _description = "";

    public string Name { get => _name; set => Set(ref _name, value); }
    public string IpAddress { get => _ipAddress; set => Set(ref _ipAddress, value); }
    public string SubnetMask { get => _subnetMask; set => Set(ref _subnetMask, value); }
    public string MacAddress { get => _macAddress; set => Set(ref _macAddress, value); }
    public bool IsUp { get => _isUp; set => Set(ref _isUp, value); }
    public int Vlan { get => _vlan; set => Set(ref _vlan, value); }
    public string Description { get => _description; set => Set(ref _description, value); }

    private SwitchportMode _mode;
    private string _allowedVlans = "all";

    /// <summary>Режим порта коммутатора: access (одна VLAN) или trunk (802.1Q, несколько VLAN).</summary>
    public SwitchportMode Mode { get => _mode; set => Set(ref _mode, value); }

    /// <summary>Разрешённые VLAN на trunk: "all" или список "10,20,30-40".</summary>
    public string AllowedVlans { get => _allowedVlans; set => Set(ref _allowedVlans, value); }

    public bool AllowsVlan(int vlan)
    {
        if (Mode == SwitchportMode.Access) return vlan == Vlan;
        if (string.IsNullOrWhiteSpace(AllowedVlans) || AllowedVlans.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var part in AllowedVlans.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-');
            if (range.Length == 2 && int.TryParse(range[0], out var a) && int.TryParse(range[1], out var b) && vlan >= a && vlan <= b) return true;
            if (int.TryParse(part, out var v) && v == vlan) return true;
        }
        return false;
    }
}

public enum SwitchportMode { Access, Trunk }

/// <summary>Пул DHCP-сервера (ip dhcp pool).</summary>
public sealed class DhcpPool : ObservableBase
{
    private string _name = "LAN";
    private string _network = "";
    private string _mask = "255.255.255.0";
    private string _defaultRouter = "";
    private string _dnsServer = "";
    private string _startAddress = "";
    private int _maxUsers = 100;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Network { get => _network; set => Set(ref _network, value); }
    public string Mask { get => _mask; set => Set(ref _mask, value); }
    public string DefaultRouter { get => _defaultRouter; set => Set(ref _defaultRouter, value); }
    public string DnsServer { get => _dnsServer; set => Set(ref _dnsServer, value); }

    /// <summary>Первый выдаваемый адрес (пусто — первый свободный в сети).</summary>
    public string StartAddress { get => _startAddress; set => Set(ref _startAddress, value); }

    public int MaxUsers { get => _maxUsers; set => Set(ref _maxUsers, value); }
}

/// <summary>Запись DNS-сервера: имя → IP.</summary>
public sealed class DnsRecord : ObservableBase
{
    private string _name = "";
    private string _address = "";
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Address { get => _address; set => Set(ref _address, value); }
}

/// <summary>VLAN коммутатора (vlan N / name X).</summary>
public sealed class VlanInfo : ObservableBase
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "";
}

/// <summary>Статический маршрут (ip route).</summary>
public sealed class StaticRoute : ObservableBase
{
    public string Network { get; set; } = "";
    public string Mask { get; set; } = "";
    public string NextHop { get; set; } = "";
}

/// <summary>Конфигурация уровня L3 — используется режимом протоколов.</summary>
public sealed class NetworkConfig : ObservableBase
{
    private string _hostname = "";
    private string _defaultGateway = "";

    public string Hostname { get => _hostname; set => Set(ref _hostname, value); }
    public string DefaultGateway { get => _defaultGateway; set => Set(ref _defaultGateway, value); }
    public ObservableCollection<NetInterface> Interfaces { get; set; } = new();
    public ObservableCollection<StaticRoute> StaticRoutes { get; set; } = new();

    private bool _useDhcp;
    private string _dnsServer = "";
    private bool _dhcpServerEnabled;
    private bool _dnsServerEnabled;

    /// <summary>Хост получает адрес по DHCP (IP Configuration → DHCP).</summary>
    public bool UseDhcp { get => _useDhcp; set => Set(ref _useDhcp, value); }

    /// <summary>DNS-сервер, к которому обращается хост.</summary>
    public string DnsServer { get => _dnsServer; set => Set(ref _dnsServer, value); }

    /// <summary>Служба DHCP включена (сервер или маршрутизатор).</summary>
    public bool DhcpServerEnabled { get => _dhcpServerEnabled; set => Set(ref _dhcpServerEnabled, value); }
    public ObservableCollection<DhcpPool> DhcpPools { get; set; } = new();

    /// <summary>Исключённые из выдачи адреса (ip dhcp excluded-address).</summary>
    public ObservableCollection<string> DhcpExcluded { get; set; } = new();

    /// <summary>Служба DNS включена.</summary>
    public bool DnsServerEnabled { get => _dnsServerEnabled; set => Set(ref _dnsServerEnabled, value); }
    public ObservableCollection<DnsRecord> DnsRecords { get; set; } = new();

    public ObservableCollection<VlanInfo> Vlans { get; set; } = new();
}

public sealed class Device : ObservableBase
{
    private string _name = "";
    private double _x;
    private double _y;
    private string _notes = "";

    public string Id { get; set; } = Ids.New();

    /// <summary>Идентификатор модели в каталоге оборудования.</summary>
    public string CatalogId { get; set; } = "";

    public DeviceKind Kind { get; set; }

    public string Name { get => _name; set => Set(ref _name, value); }
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public string Notes { get => _notes; set => Set(ref _notes, value); }

    public ObservableCollection<InstalledSoftware> Software { get; set; } = new();
    public ServerSettings Server { get; set; } = new();
    public NetworkConfig Network { get; set; } = new();

    /// <summary>Радиопараметры (для точек доступа и беспроводных станций, ПР10).</summary>
    public RadioSettings Radio { get; set; } = new();

    /// <summary>Ключи статистики, которые показываются под значком на схеме (диалог Statistics Items).</summary>
    public ObservableCollection<string> ShownStatistics { get; set; } = new();

    /// <summary>Вложенный уровень для контейнеров (город, здание, комната...).</summary>
    public Level? ChildLevel { get; set; }

    [JsonIgnore] public bool IsContainer => Kind.IsContainer();

    public bool HasService(string serviceId) =>
        Software.Any(s => s.EnabledServices.Contains(serviceId));
}
