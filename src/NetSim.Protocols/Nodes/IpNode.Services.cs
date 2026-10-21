using NetSim.Core.Model;
using NetSim.Protocols.Ip;

namespace NetSim.Protocols.Nodes;

/// <summary>Результат получения адреса по DHCP.</summary>
public sealed class DhcpSession
{
    public int Xid { get; init; }
    public Port Port { get; init; } = null!;
    public bool Completed { get; internal set; }
    public bool Success { get; internal set; }
    public Ipv4 Address { get; internal set; }
    public Ipv4 Mask { get; internal set; }
    public Ipv4 Router { get; internal set; }
    public Ipv4 Dns { get; internal set; }
    public Ipv4 Server { get; internal set; }
}

/// <summary>Трассировка маршрута (tracert / traceroute).</summary>
public sealed class TraceSession
{
    public Ipv4 Target { get; init; }
    public int Id { get; init; }
    public int MaxHops { get; init; } = 30;
    public List<(int Ttl, Ipv4? Hop, double? Rtt)> Hops { get; } = new();
    public bool Completed { get; internal set; }
    public bool Reached { get; internal set; }
    internal Action<int>? NextProbe { get; set; }
}

public abstract partial class IpNode
{
    private readonly Dictionary<int, DhcpSession> _dhcpClients = new();
    private readonly Dictionary<string, Ipv4> _dhcpBindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, Action<Ipv4?>> _dnsWaiting = new();
    private readonly Dictionary<int, (TraceSession Session, double Sent)> _traceProbes = new();
    private static int _nextXid = 1000;

    /// <summary>Привязки DHCP-сервера: MAC → выданный адрес (show ip dhcp binding).</summary>
    public IReadOnlyDictionary<string, Ipv4> DhcpBindings => _dhcpBindings;

    // ---------------- DHCP: клиент ----------------

    /// <summary>Запросить адрес по DHCP на первом подключённом интерфейсе (DISCOVER → OFFER → REQUEST → ACK).</summary>
    public DhcpSession? StartDhcp()
    {
        var port = Ports.FirstOrDefault(p => p.IsUp);
        if (port == null) return null;
        var session = new DhcpSession { Xid = _nextXid++, Port = port };
        _dhcpClients[session.Xid] = session;
        Sim.Log(this, "DHCP", "DISCOVER");
        SendFrame(port, Mac.Broadcast, new IpPacket(Ipv4.Any, Ipv4.Broadcast, 64,
            new DhcpMessage(DhcpOp.Discover, session.Xid, port.Mac, Ipv4.Any, Ipv4.Any, Ipv4.Any, Ipv4.Any, Ipv4.Any)));
        Sim.Engine.After(5, () =>
        {
            if (session.Completed) return;
            session.Completed = true;
            _dhcpClients.Remove(session.Xid);
            Sim.Log(this, "DHCP", "No DHCP server response", dropped: true);
        });
        return session;
    }

    /// <summary>Освободить адрес (ipconfig /release).</summary>
    public void ReleaseDhcp()
    {
        foreach (var p in Ports)
        {
            p.Config.IpAddress = "";
            p.Config.SubnetMask = "";
        }
        Device.Network.DefaultGateway = "";
        Device.Network.DnsServer = "";
    }

    private void HandleDhcp(Port port, IpPacket packet, DhcpMessage m)
    {
        switch (m.Op)
        {
            case DhcpOp.Discover:
            case DhcpOp.Request:
                ServeDhcp(port, m);
                break;
            case DhcpOp.Offer:
                if (_dhcpClients.TryGetValue(m.Xid, out var s) && !s.Completed && m.ClientMac == s.Port.Mac)
                {
                    Sim.Log(this, "DHCP", $"OFFER {m.YourIp} from {m.ServerIp} → REQUEST");
                    SendFrame(s.Port, Mac.Broadcast, new IpPacket(Ipv4.Any, Ipv4.Broadcast, 64, m with { Op = DhcpOp.Request }));
                }
                break;
            case DhcpOp.Ack:
                if (_dhcpClients.Remove(m.Xid, out var a) && m.ClientMac == a.Port.Mac)
                {
                    a.Port.Config.IpAddress = m.YourIp.ToString();
                    a.Port.Config.SubnetMask = m.Mask.ToString();
                    Device.Network.DefaultGateway = m.Router.Value == 0 ? "" : m.Router.ToString();
                    Device.Network.DnsServer = m.Dns.Value == 0 ? "" : m.Dns.ToString();
                    a.Address = m.YourIp; a.Mask = m.Mask; a.Router = m.Router; a.Dns = m.Dns; a.Server = m.ServerIp;
                    a.Success = a.Completed = true;
                    Sim.Log(this, "DHCP", $"ACK: {m.YourIp}/{m.Mask.PrefixLength}, gateway {m.Router}");
                }
                break;
            case DhcpOp.Nak:
                if (_dhcpClients.Remove(m.Xid, out var n)) { n.Completed = true; Sim.Log(this, "DHCP", "NAK", dropped: true); }
                break;
        }
    }

    // ---------------- DHCP: сервер ----------------

    private void ServeDhcp(Port port, DhcpMessage m)
    {
        var net = Device.Network;
        if (!net.DhcpServerEnabled || net.DhcpPools.Count == 0 || port.Ip is not { } myIp) return;
        var pool = net.DhcpPools.FirstOrDefault(p => Ipv4.TryParse(p.Network, out var n) && Ipv4.TryParse(p.Mask, out var mk) && myIp.SameSubnet(n, mk))
                   ?? (Device.Kind is DeviceKind.Router ? null : net.DhcpPools[0]);
        if (pool == null || !Ipv4.TryParse(pool.Mask, out var mask)) return;

        var ip = m.Op == DhcpOp.Request && m.YourIp.Value != 0 ? m.YourIp : Allocate(pool, m.ClientMac);
        if (ip == null)
        {
            Sim.Log(this, "DHCP", $"Pool {pool.Name} exhausted", dropped: true);
            return;
        }
        _dhcpBindings[m.ClientMac] = ip.Value;
        Ipv4.TryParse(pool.DefaultRouter, out var router);
        Ipv4.TryParse(pool.DnsServer, out var dns);
        var op = m.Op == DhcpOp.Discover ? DhcpOp.Offer : DhcpOp.Ack;
        Sim.Log(this, "DHCP", $"{op} {ip} → {m.ClientMac}");
        SendFrame(port, m.ClientMac, new IpPacket(myIp, Ipv4.Broadcast, 64,
            new DhcpMessage(op, m.Xid, m.ClientMac, ip.Value, mask, router, dns, myIp)));
    }

    private Ipv4? Allocate(DhcpPool pool, string clientMac)
    {
        if (_dhcpBindings.TryGetValue(clientMac, out var existing)) return existing;
        if (!Ipv4.TryParse(pool.Network, out var network) || !Ipv4.TryParse(pool.Mask, out var mask)) return null;
        network = network.Network(mask);
        var used = new HashSet<uint>(Sim.Project.AllDevices().SelectMany(d => d.Network.Interfaces)
            .Select(i => Ipv4.TryParse(i.IpAddress, out var a) ? a.Value : 0u));
        foreach (var b in _dhcpBindings.Values) used.Add(b.Value);
        foreach (var ex in Device.Network.DhcpExcluded)
        {
            var parts = ex.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && Ipv4.TryParse(parts[0], out var from))
            {
                var to = parts.Length >= 2 && Ipv4.TryParse(parts[1], out var t) ? t : from;
                for (uint v = from.Value; v <= to.Value && v - from.Value < 65536; v++) used.Add(v);
            }
        }
        if (Ipv4.TryParse(pool.DefaultRouter, out var r)) used.Add(r.Value);
        uint start = Ipv4.TryParse(pool.StartAddress, out var s) ? s.Value : network.Value + 1;
        uint broadcast = network.Value | ~mask.Value;
        int count = 0;
        for (uint v = start; v < broadcast && count < pool.MaxUsers; v++, count++)
            if (!used.Contains(v)) return new Ipv4(v);
        return null;
    }

    // ---------------- DNS ----------------

    /// <summary>Разрешить имя через DNS-сервер хоста. Колбэк получает адрес или null.</summary>
    public void Resolve(string name, Action<Ipv4?> done)
    {
        if (Ipv4.TryParse(name, out var literal)) { done(literal); return; }
        if (!Ipv4.TryParse(Device.Network.DnsServer, out var server))
        {
            done(null);
            return;
        }
        int id = _nextXid++;
        _dnsWaiting[id] = done;
        Sim.Log(this, "DNS", $"Query {name} → {server}");
        var src = Lookup(server)?.Port.Ip ?? Ipv4.Any;
        if (!SendIp(new IpPacket(src, server, DefaultTtl, new DnsMessage(id, name, false, null)))) { _dnsWaiting.Remove(id); done(null); return; }
        Sim.Engine.After(3, () => { if (_dnsWaiting.Remove(id, out var cb)) cb(null); });
    }

    private void HandleDns(Port port, IpPacket packet, DnsMessage m)
    {
        if (m.IsResponse)
        {
            if (_dnsWaiting.Remove(m.Id, out var cb)) cb(m.Address);
            return;
        }
        if (!Device.Network.DnsServerEnabled) return;
        var record = Device.Network.DnsRecords.FirstOrDefault(r => string.Equals(r.Name.TrimEnd('.'), m.Name.TrimEnd('.'), StringComparison.OrdinalIgnoreCase));
        Ipv4? answer = record != null && Ipv4.TryParse(record.Address, out var a) ? a : null;
        Sim.Log(this, "DNS", $"{m.Name} → {(answer?.ToString() ?? "NXDOMAIN")}");
        SendIp(new IpPacket(packet.Dst == Ipv4.Broadcast ? port.Ip ?? Ipv4.Any : packet.Dst, packet.Src, DefaultTtl, m with { IsResponse = true, Address = answer }));
    }

    // ---------------- Трассировка ----------------

    public TraceSession StartTrace(Ipv4 target, int maxHops = 30, double timeoutSec = 2)
    {
        var session = new TraceSession { Target = target, Id = _nextXid++, MaxHops = maxHops };
        var src = Lookup(target)?.Port.Ip ?? Ports.Select(p => p.Ip).FirstOrDefault(i => i.HasValue) ?? Ipv4.Any;

        void Probe(int ttl)
        {
            if (session.Completed) return;
            if (ttl > maxHops) { session.Completed = true; return; }
            _traceProbes[ttl + session.Id * 100] = (session, Sim.Engine.Now);
            session.Hops.Add((ttl, null, null));
            if (!SendIp(new IpPacket(src, target, ttl, new IcmpMessage(IcmpType.EchoRequest, session.Id, ttl))))
            {
                session.Completed = true;
                return;
            }
            Sim.Engine.After(timeoutSec, () =>
            {
                if (_traceProbes.Remove(ttl + session.Id * 100)) Probe(ttl + 1);
            });
        }

        session.NextProbe = Probe;
        Probe(1);
        return session;
    }

    /// <summary>Ответ на пробу трассировки: Time Exceeded от промежуточного узла или Echo Reply от цели.</summary>
    private bool HandleTraceReply(IpPacket packet, IcmpMessage icmp)
    {
        if (!_traceProbes.Remove(icmp.Seq + icmp.Id * 100, out var probe)) return false;
        var s = probe.Session;
        int idx = s.Hops.FindIndex(h => h.Ttl == icmp.Seq);
        if (idx >= 0) s.Hops[idx] = (icmp.Seq, packet.Src, Sim.Engine.Now - probe.Sent);
        if (icmp.Type == IcmpType.EchoReply || packet.Src == s.Target)
        {
            s.Reached = true;
            s.Completed = true;
        }
        else if (icmp.Type == IcmpType.DestinationUnreachable) s.Completed = true;
        else s.NextProbe?.Invoke(icmp.Seq + 1);
        return true;
    }
}
