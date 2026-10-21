namespace NetSim.Protocols.Ip;

public enum EtherType { Arp, Ipv4 }

/// <summary>Кадр канального уровня.</summary>
public sealed class Frame
{
    public string SrcMac { get; init; } = "";
    public string DstMac { get; init; } = "";
    public EtherType Type { get; init; }
    public object Payload { get; init; } = null!;
    public int Bytes { get; init; } = 64;

    /// <summary>Метка VLAN 802.1Q (только на trunk-линиях; null — кадр без метки).</summary>
    public int? Vlan { get; init; }

    public Frame WithVlan(int? vlan) => new() { SrcMac = SrcMac, DstMac = DstMac, Type = Type, Payload = Payload, Bytes = Bytes + (vlan.HasValue ? 4 : 0) - (Vlan.HasValue ? 4 : 0), Vlan = vlan };

    public bool IsBroadcast => DstMac == Mac.Broadcast;

    public string Describe() => Payload switch
    {
        ArpPacket a => a.Describe(),
        IpPacket p => p.Describe(),
        _ => Type.ToString(),
    };

    public string Protocol => Payload switch
    {
        ArpPacket => "ARP",
        IpPacket { Payload: IcmpMessage } => "ICMP",
        IpPacket { Payload: DhcpMessage } => "DHCP",
        IpPacket { Payload: DnsMessage } => "DNS",
        _ => Type.ToString().ToUpperInvariant(),
    };
}

public enum ArpOp { Request, Reply }

public sealed record ArpPacket(ArpOp Op, Ipv4 SenderIp, string SenderMac, Ipv4 TargetIp, string TargetMac)
{
    public string Describe() => Op == ArpOp.Request
        ? $"ARP Who has {TargetIp}? Tell {SenderIp}"
        : $"ARP {SenderIp} is at {SenderMac}";
}

public sealed record IpPacket(Ipv4 Src, Ipv4 Dst, int Ttl, object Payload)
{
    public string Describe() => Payload switch
    {
        IcmpMessage icmp => $"ICMP {icmp.Type} {Src} → {Dst} seq={icmp.Seq} TTL={Ttl}",
        DhcpMessage d => $"DHCP {d.Op} xid={d.Xid}" + (d.YourIp.Value != 0 ? $" {d.YourIp}" : ""),
        DnsMessage n => n.IsResponse ? $"DNS response {n.Name} = {(n.Address?.ToString() ?? "NXDOMAIN")}" : $"DNS query {n.Name}",
        _ => $"IP {Src} → {Dst}",
    };
}

public enum IcmpType { EchoRequest, EchoReply, DestinationUnreachable, TimeExceeded }

public sealed record IcmpMessage(IcmpType Type, int Id, int Seq, int DataBytes = 32);

public enum DhcpOp { Discover, Offer, Request, Ack, Nak }

/// <summary>Сообщение DHCP (упрощённо: без опций и аренды).</summary>
public sealed record DhcpMessage(DhcpOp Op, int Xid, string ClientMac, Ipv4 YourIp, Ipv4 Mask, Ipv4 Router, Ipv4 Dns, Ipv4 ServerIp);

/// <summary>Сообщение DNS: запрос A-записи и ответ.</summary>
public sealed record DnsMessage(int Id, string Name, bool IsResponse, Ipv4? Address);
