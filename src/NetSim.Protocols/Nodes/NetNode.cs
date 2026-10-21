using NetSim.Core.Model;
using NetSim.Protocols.Ip;

namespace NetSim.Protocols.Nodes;

/// <summary>Порт устройства — конец линии с настройками интерфейса.</summary>
public sealed class Port
{
    public NetNode Node { get; init; } = null!;
    public Link Link { get; init; } = null!;
    public NetInterface Config { get; init; } = null!;
    public Port? Peer { get; set; }

    public string Name => Config.Name;
    public string Mac => Config.MacAddress;
    public bool IsUp => Config.IsUp;

    public Ipv4? Ip => Ipv4.TryParse(Config.IpAddress, out var ip) ? ip : null;
    public Ipv4? Mask => Ipv4.TryParse(Config.SubnetMask, out var m) && Ipv4.IsValidMask(m) ? m : null;

    public bool HasIp => Ip.HasValue && Mask.HasValue;
}

/// <summary>Базовый узел режима протоколов. Поведение зависит от роли устройства.</summary>
public abstract class NetNode
{
    public ProtocolSimulator Sim { get; }
    public Device Device { get; }
    public List<Port> Ports { get; } = new();

    protected NetNode(ProtocolSimulator sim, Device device)
    {
        Sim = sim;
        Device = device;
    }

    public static NetNode Create(ProtocolSimulator sim, Device d) => d.Kind switch
    {
        DeviceKind.Hub or DeviceKind.Segment or DeviceKind.Container => new RepeaterNode(sim, d),
        DeviceKind.Switch or DeviceKind.Bridge or DeviceKind.AccessPoint or DeviceKind.Modem or DeviceKind.DsuCsu
            or DeviceKind.Frad or DeviceKind.Multiplexer or DeviceKind.Pbx or DeviceKind.Cloud => new SwitchNode(sim, d),
        DeviceKind.Router or DeviceKind.Gateway or DeviceKind.Firewall or DeviceKind.AccessServer => new RouterNode(sim, d),
        _ => new HostNode(sim, d),
    };

    public Port AddPort(Link link, string interfaceName)
    {
        var cfg = Device.Network.Interfaces.FirstOrDefault(i => i.Name == interfaceName);
        if (cfg == null)
        {
            cfg = new NetInterface { Name = interfaceName, MacAddress = $"0000.{Ports.Count:X4}.{(uint)Device.Id.GetHashCode() & 0xFFFF:X4}" };
            Device.Network.Interfaces.Add(cfg);
        }
        if (string.IsNullOrEmpty(cfg.MacAddress))
            cfg.MacAddress = $"0200.{(uint)Device.Id.GetHashCode() & 0xFFFF:X4}.{Ports.Count:X4}";
        var port = new Port { Node = this, Link = link, Config = cfg };
        Ports.Add(port);
        return port;
    }

    /// <summary>Кадр пришёл на порт.</summary>
    public abstract void Receive(Port port, Frame frame);

    protected void Send(Port port, Frame frame)
    {
        if (!port.IsUp) return;
        Sim.Transmit(port, frame);
    }
}

/// <summary>Повторитель (хаб, сегмент шины/кольца, контейнер уровня): копирует кадр во все остальные порты.</summary>
public sealed class RepeaterNode : NetNode
{
    public RepeaterNode(ProtocolSimulator sim, Device d) : base(sim, d) { }

    public override void Receive(Port port, Frame frame)
    {
        foreach (var p in Ports)
            if (p != port) Send(p, frame);
    }
}

/// <summary>
/// Коммутатор (мост): изучает MAC-адреса отдельно в каждой VLAN, пересылает кадр только в нужный порт.
/// Порты access — одна VLAN без метки; trunk — несколько VLAN с меткой 802.1Q (native VLAN 1 без метки).
/// </summary>
public sealed class SwitchNode : NetNode
{
    public Dictionary<(int Vlan, string Mac), (Port Port, double Time)> MacTable { get; } = new();
    public const double AgingTime = 300;
    public const int NativeVlan = 1;

    public SwitchNode(ProtocolSimulator sim, Device d) : base(sim, d) { }

    private static bool IsTrunk(Port p) => p.Config.Mode == SwitchportMode.Trunk;

    public override void Receive(Port port, Frame frame)
    {
        if (!port.IsUp) return;
        int vlan = IsTrunk(port) ? frame.Vlan ?? NativeVlan : port.Config.Vlan;
        if (!port.Config.AllowsVlan(vlan)) return;
        MacTable[(vlan, frame.SrcMac)] = (port, Sim.Engine.Now);

        if (!frame.IsBroadcast && MacTable.TryGetValue((vlan, frame.DstMac), out var entry) && Sim.Engine.Now - entry.Time < AgingTime)
        {
            if (entry.Port != port) Out(entry.Port, frame, vlan);
            return;
        }
        foreach (var p in Ports)
            if (p != port && p.Config.AllowsVlan(vlan)) Out(p, frame, vlan);
    }

    private void Out(Port p, Frame frame, int vlan)
    {
        int? tag = IsTrunk(p) && vlan != NativeVlan ? vlan : null;
        Send(p, frame.Vlan == tag ? frame : frame.WithVlan(tag));
    }
}
