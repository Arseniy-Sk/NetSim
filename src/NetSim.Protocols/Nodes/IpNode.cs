using System.Globalization;
using NetSim.Core.Model;
using NetSim.Protocols.Ip;

namespace NetSim.Protocols.Nodes;

public sealed record RouteEntry(Ipv4 Network, Ipv4 Mask, Ipv4? NextHop, Port? OutPort, char Code)
{
    public string Format() => Code == 'C'
        ? $"C    {Network}/{Mask.PrefixLength} is directly connected, {OutPort?.Name}"
        : $"S    {Network}/{Mask.PrefixLength} [1/0] via {NextHop}";
}

/// <summary>Сеанс ping: отправленные запросы и полученные ответы.</summary>
public sealed class PingSession
{
    public Ipv4 Target { get; init; }
    public int Count { get; init; }
    public int Id { get; init; }
    public List<(int Seq, double Sent, double? Rtt, int Ttl, string? Error)> Results { get; } = new();
    public bool Completed => Results.Count >= Count && Results.All(r => r.Rtt.HasValue || r.Error != null);
    public event Action<PingSession>? Updated;
    internal void RaiseUpdated() => Updated?.Invoke(this);

    public IEnumerable<string> FormatWindows()
    {
        yield return $"Pinging {Target} with 32 bytes of data:";
        foreach (var r in Results)
        {
            if (r.Rtt is { } rtt)
            {
                var ms = rtt * 1000;
                yield return $"Reply from {Target}: bytes=32 time{(ms < 1 ? "<1" : "=" + ms.ToString("0", CultureInfo.InvariantCulture))}ms TTL={r.Ttl}";
            }
            else yield return r.Error ?? "Request timed out.";
        }
        int ok = Results.Count(r => r.Rtt.HasValue);
        yield return "";
        yield return $"Ping statistics for {Target}:";
        yield return $"    Packets: Sent = {Count}, Received = {ok}, Lost = {Count - ok} ({(Count == 0 ? 0 : (Count - ok) * 100 / Count)}% loss),";
    }

    public IEnumerable<string> FormatCisco()
    {
        yield return "Type escape sequence to abort.";
        yield return $"Sending {Count}, 100-byte ICMP Echos to {Target}, timeout is 2 seconds:";
        yield return new string(Results.Select(r => r.Rtt.HasValue ? '!' : r.Error?.Contains("unreachable") == true ? 'U' : '.').ToArray());
        int ok = Results.Count(r => r.Rtt.HasValue);
        var rtts = Results.Where(r => r.Rtt.HasValue).Select(r => r.Rtt!.Value * 1000).ToList();
        var line = $"Success rate is {(Count == 0 ? 0 : ok * 100 / Count)} percent ({ok}/{Count})";
        if (rtts.Count > 0) line += $", round-trip min/avg/max = {rtts.Min():0}/{rtts.Average():0}/{rtts.Max():0} ms";
        yield return line;
    }
}

/// <summary>Узел с IP-стеком: ARP, ICMP. Основа для компьютера и маршрутизатора.</summary>
public abstract partial class IpNode : NetNode
{
    public Dictionary<Ipv4, (string Mac, double Time)> ArpCache { get; } = new();
    private readonly Dictionary<Ipv4, List<(Port Port, IpPacket Packet)>> _pendingArp = new();
    private readonly Dictionary<(int Id, int Seq), (PingSession Session, int Index)> _outstanding = new();
    private static int _nextPingId = 1;

    protected IpNode(ProtocolSimulator sim, Device d) : base(sim, d) { }

    protected virtual int DefaultTtl => 128;

    protected bool IsMine(Ipv4 ip) => Ports.Any(p => p.Ip == ip && p.IsUp);

    public override void Receive(Port port, Frame frame)
    {
        if (!port.IsUp) return;
        if (!frame.IsBroadcast && !string.Equals(frame.DstMac, port.Mac, StringComparison.OrdinalIgnoreCase)) return;

        switch (frame.Payload)
        {
            case ArpPacket arp: HandleArp(port, arp); break;
            case IpPacket ip: HandleIp(port, ip); break;
        }
    }

    private void HandleArp(Port port, ArpPacket arp)
    {
        if (port.Ip == null) return;
        ArpCache[arp.SenderIp] = (arp.SenderMac, Sim.Engine.Now);
        if (arp.Op == ArpOp.Request && arp.TargetIp == port.Ip)
        {
            Sim.Log(this, "ARP", $"Reply: {port.Ip} is at {port.Mac}");
            Send(port, new Frame
            {
                SrcMac = port.Mac, DstMac = arp.SenderMac, Type = EtherType.Arp, Bytes = 64,
                Payload = new ArpPacket(ArpOp.Reply, port.Ip.Value, port.Mac, arp.SenderIp, arp.SenderMac),
            });
        }
        if (_pendingArp.Remove(arp.SenderIp, out var waiting))
            foreach (var (p, pkt) in waiting) SendFrame(p, arp.SenderMac, pkt);
    }

    protected virtual void HandleIp(Port port, IpPacket packet)
    {
        if (IsMine(packet.Dst) || packet.Dst == Ipv4.Broadcast || (port.Mask is { } m && packet.Dst.IsBroadcastFor(m)))
        {
            Deliver(port, packet);
            return;
        }
        Forward(port, packet);
    }

    /// <summary>Пакет адресован чужому узлу. Компьютер его отбрасывает, маршрутизатор — пересылает.</summary>
    protected virtual void Forward(Port inPort, IpPacket packet) { }

    protected void Deliver(Port port, IpPacket packet)
    {
        if (packet.Payload is DhcpMessage dhcp) { HandleDhcp(port, packet, dhcp); return; }
        if (packet.Payload is DnsMessage dns) { HandleDns(port, packet, dns); return; }
        if (packet.Payload is not IcmpMessage icmp) return;
        if (icmp.Type != IcmpType.EchoRequest && HandleTraceReply(packet, icmp)) return;
        switch (icmp.Type)
        {
            case IcmpType.EchoRequest:
                Sim.Log(this, "ICMP", $"Echo request from {packet.Src} → reply");
                SendIp(new IpPacket(packet.Dst, packet.Src, DefaultTtl, icmp with { Type = IcmpType.EchoReply }));
                break;
            case IcmpType.EchoReply:
                if (_outstanding.Remove((icmp.Id, icmp.Seq), out var o))
                {
                    var r = o.Session.Results[o.Index];
                    o.Session.Results[o.Index] = (r.Seq, r.Sent, Sim.Engine.Now - r.Sent, packet.Ttl, null);
                    Sim.Log(this, "ICMP", $"Echo reply from {packet.Src}");
                    o.Session.RaiseUpdated();
                }
                break;
            case IcmpType.DestinationUnreachable:
            case IcmpType.TimeExceeded:
                if (_outstanding.Remove((icmp.Id, icmp.Seq), out var u))
                {
                    var r = u.Session.Results[u.Index];
                    var msg = icmp.Type == IcmpType.TimeExceeded
                        ? $"Reply from {packet.Src}: TTL expired in transit."
                        : $"Reply from {packet.Src}: Destination host unreachable.";
                    u.Session.Results[u.Index] = (r.Seq, r.Sent, null, 0, msg);
                    u.Session.RaiseUpdated();
                }
                break;
        }
    }

    /// <summary>Таблица маршрутизации: подключённые сети, статические маршруты и шлюз по умолчанию.</summary>
    public virtual List<RouteEntry> RoutingTable()
    {
        var table = new List<RouteEntry>();
        foreach (var p in Ports.Where(p => p.HasIp && p.IsUp))
            table.Add(new RouteEntry(p.Ip!.Value.Network(p.Mask!.Value), p.Mask!.Value, null, p, 'C'));
        foreach (var r in Device.Network.StaticRoutes)
            if (Ipv4.TryParse(r.Network, out var n) && Ipv4.TryParse(r.Mask, out var m) && Ipv4.TryParse(r.NextHop, out var nh))
                table.Add(new RouteEntry(n.Network(m), m, nh, null, 'S'));
        if (Ipv4.TryParse(Device.Network.DefaultGateway, out var gw))
            table.Add(new RouteEntry(Ipv4.Any, Ipv4.Any, gw, null, 'S'));
        return table;
    }

    /// <summary>Выбор маршрута по самому длинному префиксу; рекурсивно находит выходной порт для next-hop.</summary>
    public (Port Port, Ipv4 NextHop)? Lookup(Ipv4 dst, int depth = 0)
    {
        if (depth > 4) return null;
        var best = RoutingTable()
            .Where(r => dst.SameSubnet(r.Network, r.Mask))
            .OrderByDescending(r => r.Mask.PrefixLength)
            .ThenBy(r => r.Code == 'C' ? 0 : 1)
            .FirstOrDefault();
        if (best == null) return null;
        if (best.OutPort != null) return (best.OutPort, dst);
        var via = Lookup(best.NextHop!.Value, depth + 1);
        return via == null ? null : (via.Value.Port, best.NextHop.Value);
    }

    public bool SendIp(IpPacket packet)
    {
        var route = Lookup(packet.Dst);
        if (route == null)
        {
            Sim.Log(this, "IP", $"No route to {packet.Dst}", dropped: true);
            return false;
        }
        var (port, nextHop) = route.Value;
        if (ArpCache.TryGetValue(nextHop, out var entry))
        {
            SendFrame(port, entry.Mac, packet);
            return true;
        }
        if (!_pendingArp.TryGetValue(nextHop, out var list))
        {
            _pendingArp[nextHop] = list = new List<(Port, IpPacket)>();
            Sim.Log(this, "ARP", $"Request: who has {nextHop}?");
            Send(port, new Frame
            {
                SrcMac = port.Mac, DstMac = Mac.Broadcast, Type = EtherType.Arp, Bytes = 64,
                Payload = new ArpPacket(ArpOp.Request, port.Ip ?? Ipv4.Any, port.Mac, nextHop, "0000.0000.0000"),
            });
            Sim.Engine.After(1.0, () => _pendingArp.Remove(nextHop));
        }
        list.Add((port, packet));
        return true;
    }

    protected void SendFrame(Port port, string dstMac, IpPacket packet)
    {
        int bytes = 18 + 20 + (packet.Payload switch { IcmpMessage icmp => 8 + icmp.DataBytes, DhcpMessage => 8 + 300, DnsMessage => 8 + 60, _ => 64 });
        Send(port, new Frame { SrcMac = port.Mac, DstMac = dstMac, Type = EtherType.Ipv4, Payload = packet, Bytes = Math.Max(64, bytes) });
    }

    public PingSession StartPing(Ipv4 target, int count, double timeoutSec)
    {
        var session = new PingSession { Target = target, Count = count, Id = _nextPingId++ };
        var src = Lookup(target)?.Port.Ip ?? Ports.Select(p => p.Ip).FirstOrDefault(i => i.HasValue) ?? Ipv4.Any;
        for (int i = 0; i < count; i++)
        {
            int seq = i + 1;
            Sim.Engine.After(i * 1.0, () =>
            {
                int index = session.Results.Count;
                session.Results.Add((seq, Sim.Engine.Now, null, 0, null));
                _outstanding[(session.Id, seq)] = (session, index);
                Sim.Log(this, "ICMP", $"Echo request to {target} seq={seq}");
                if (!SendIp(new IpPacket(src, target, DefaultTtl, new IcmpMessage(IcmpType.EchoRequest, session.Id, seq))))
                {
                    _outstanding.Remove((session.Id, seq));
                    session.Results[index] = (seq, Sim.Engine.Now, null, 0, "PING: transmit failed. General failure.");
                    session.RaiseUpdated();
                    return;
                }
                Sim.Engine.After(timeoutSec, () =>
                {
                    if (_outstanding.Remove((session.Id, seq)))
                    {
                        session.Results[index] = (seq, session.Results[index].Sent, null, 0, "Request timed out.");
                        session.RaiseUpdated();
                    }
                });
            });
        }
        return session;
    }
}

/// <summary>Компьютер, сервер, принтер: один IP-стек, шлюз по умолчанию.</summary>
public sealed class HostNode : IpNode
{
    public HostNode(ProtocolSimulator sim, Device d) : base(sim, d) { }
}

/// <summary>Маршрутизатор: пересылает IP-пакеты между сетями по таблице маршрутизации.</summary>
public sealed class RouterNode : IpNode
{
    public RouterNode(ProtocolSimulator sim, Device d) : base(sim, d) { }

    protected override int DefaultTtl => 255;

    protected override void Forward(Port inPort, IpPacket packet)
    {
        if (packet.Ttl <= 1)
        {
            Sim.Log(this, "IP", $"TTL expired for {packet.Dst}", dropped: true);
            if (packet.Payload is IcmpMessage icmp && inPort.Ip is { } myIp)
                SendIp(new IpPacket(myIp, packet.Src, DefaultTtl, icmp with { Type = IcmpType.TimeExceeded }));
            return;
        }
        if (Lookup(packet.Dst) == null)
        {
            Sim.Log(this, "IP", $"Destination {packet.Dst} unreachable", dropped: true);
            if (packet.Payload is IcmpMessage icmp && inPort.Ip is { } myIp)
                SendIp(new IpPacket(myIp, packet.Src, DefaultTtl, icmp with { Type = IcmpType.DestinationUnreachable }));
            return;
        }
        Sim.Log(this, "IP", $"Forward {packet.Src} → {packet.Dst}");
        SendIp(packet with { Ttl = packet.Ttl - 1 });
    }
}
