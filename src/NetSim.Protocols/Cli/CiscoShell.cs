using NetSim.Core.Model;
using NetSim.Protocols.Ip;
using NetSim.Protocols.Nodes;

namespace NetSim.Protocols.Cli;

/// <summary>
/// Упрощённый Cisco IOS: режимы User EXEC (&gt;), Privileged EXEC (#), Global config, Interface config.
/// Поддерживаются сокращения (en, conf t, int fa0/1, sh ip int br).
/// </summary>
public sealed class CiscoShell : CommandShell
{
    private enum Mode { User, Privileged, Config, Interface, DhcpPool, Vlan }

    private Mode _mode = Mode.User;
    private NetInterface? _iface;
    private DhcpPool? _pool;
    private VlanInfo? _vlan;

    public CiscoShell(Device device, ProtocolSimulator sim) : base(device, sim) { }

    private string Host => string.IsNullOrWhiteSpace(Device.Network.Hostname) ? Device.Name.Replace(" ", "") : Device.Network.Hostname;

    public override string Prompt => _mode switch
    {
        Mode.User => $"{Host}>",
        Mode.Privileged => $"{Host}#",
        Mode.Config => $"{Host}(config)#",
        Mode.DhcpPool => $"{Host}(dhcp-config)#",
        Mode.Vlan => $"{Host}(config-vlan)#",
        _ => $"{Host}(config-if)#",
    };

    public override IEnumerable<string> Banner()
    {
        yield return "NetSim IOS emulation (учебная). Press RETURN to get started.";
        yield return "Подсказка: enable → configure terminal → interface fa0/0 → ip address 192.168.1.1 255.255.255.0 → no shutdown";
    }

    protected override void Run(List<string> t, string raw, List<string> o)
    {
        if (t[0] == "?" || Is(t, 0, "help", 2)) { o.AddRange(Help()); return; }

        if (_mode is Mode.Config or Mode.Interface or Mode.DhcpPool or Mode.Vlan && Is(t, 0, "do", 2))
        {
            var saved = _mode;
            _mode = Mode.Privileged;
            Run(t.Skip(1).ToList(), raw, o);
            _mode = saved;
            return;
        }
        if (Is(t, 0, "end", 3) && _mode is Mode.Config or Mode.Interface or Mode.DhcpPool or Mode.Vlan) { _mode = Mode.Privileged; _iface = null; return; }
        if (Is(t, 0, "exit", 2))
        {
            _mode = _mode switch { Mode.Interface or Mode.DhcpPool or Mode.Vlan => Mode.Config, Mode.Config => Mode.Privileged, _ => Mode.User };
            if (_mode != Mode.Interface) _iface = null;
            return;
        }

        switch (_mode)
        {
            case Mode.User: RunExec(t, o, privileged: false); break;
            case Mode.Privileged: RunExec(t, o, privileged: true); break;
            case Mode.Config: RunConfig(t, raw, o); break;
            case Mode.Interface: RunInterface(t, raw, o); break;
            case Mode.DhcpPool: RunDhcpPool(t, o); break;
            case Mode.Vlan: RunVlan(t, o); break;
        }
    }

    private void RunExec(List<string> t, List<string> o, bool privileged)
    {
        if (Is(t, 0, "enable", 2)) { _mode = Mode.Privileged; return; }
        if (Is(t, 0, "disable", 4)) { _mode = Mode.User; return; }
        if (Is(t, 0, "ping", 1)) { Ping(t, 1, o, 5); return; }
        if (Is(t, 0, "traceroute", 4) || Is(t, 0, "tracert", 6))
        {
            if (t.Count < 2) { o.Add("% Incomplete command."); return; }
            o.AddRange(Sim.TraceBlocking(Device, t[1], cisco: true));
            return;
        }
        if (Is(t, 0, "show", 2)) { Show(t, o, privileged); return; }
        if (privileged && Is(t, 0, "configure", 4))
        {
            if (t.Count == 1 || Is(t, 1, "terminal", 1))
            {
                o.Add("Enter configuration commands, one per line.  End with CNTL/Z.");
                _mode = Mode.Config;
                return;
            }
        }
        if (privileged && (Is(t, 0, "write", 2) || (Is(t, 0, "copy", 2) && t.Count >= 3)))
        {
            o.Add("Building configuration...");
            o.Add("[OK]");
            return;
        }
        Invalid(o, t[0]);
    }

    private void Show(List<string> t, List<string> o, bool privileged)
    {
        if (Is(t, 1, "running-config", 2) && privileged) { o.AddRange(RunningConfig()); return; }
        if (Is(t, 1, "ip", 2) && Is(t, 2, "interface", 3) && Is(t, 3, "brief", 2)) { o.AddRange(InterfaceBrief()); return; }
        if (Is(t, 1, "ip", 2) && Is(t, 2, "route", 2)) { o.AddRange(RouteTable()); return; }
        if (Is(t, 1, "arp", 2) || (Is(t, 1, "ip", 2) && Is(t, 2, "arp", 2)))
        {
            o.Add("Protocol  Address           Hardware Addr       Type");
            foreach (var l in ArpTable()) o.Add("Internet" + l);
            return;
        }
        if (Is(t, 1, "mac", 2) || Is(t, 1, "mac-address-table", 4)) { o.AddRange(MacTable()); return; }
        if (Is(t, 1, "interfaces", 3)) { o.AddRange(Interfaces()); return; }
        if (Is(t, 1, "vlan", 2)) { o.AddRange(VlanBrief()); return; }
        if (Is(t, 1, "ip", 2) && Is(t, 2, "dhcp", 2) && Is(t, 3, "binding", 1))
        {
            o.Add("IP address       Client-ID/Hardware address");
            if (Sim.Node(Device) is IpNode n)
                foreach (var (mac, ip) in n.DhcpBindings.OrderBy(x => x.Value)) o.Add($"{ip,-17}{mac}");
            return;
        }
        if (Is(t, 1, "hosts", 2))
        {
            foreach (var r in Device.Network.DnsRecords) o.Add($"{r.Name,-24}{r.Address}");
            return;
        }
        if (Is(t, 1, "version", 2))
        {
            o.Add("NetSim IOS Software (учебная эмуляция), Version 0.1");
            o.Add($"{Host} uptime is {Sim.Engine.Now:0} seconds");
            return;
        }
        o.Add("% Incomplete or unsupported 'show' command. Try: show running-config | ip interface brief | ip route | arp | mac address-table | interfaces | version");
    }

    private void RunConfig(List<string> t, string raw, List<string> o)
    {
        if (Is(t, 0, "hostname", 3))
        {
            if (t.Count < 2) { o.Add("% Incomplete command."); return; }
            Device.Network.Hostname = t[1];
            return;
        }
        if (Is(t, 0, "interface", 3))
        {
            var name = string.Concat(t.Skip(1));
            var iface = FindInterface(name);
            if (iface == null) { o.Add($"% Invalid interface: {name}. Interfaces appear when a cable is connected."); return; }
            _iface = iface;
            _mode = Mode.Interface;
            return;
        }
        bool no = Is(t, 0, "no", 2);
        int b = no ? 1 : 0;
        if (Is(t, b, "vlan", 1) && t.Count > b + 1 && int.TryParse(t[b + 1], out var vid) && vid is >= 1 and <= 4094)
        {
            var vlans = Device.Network.Vlans;
            var existingVlan = vlans.FirstOrDefault(v => v.Id == vid);
            if (no) { if (existingVlan != null) vlans.Remove(existingVlan); return; }
            if (existingVlan == null) vlans.Add(existingVlan = new VlanInfo { Id = vid, Name = $"VLAN{vid:0000}" });
            _vlan = existingVlan;
            _mode = Mode.Vlan;
            return;
        }
        if (Is(t, b, "ip", 2) && Is(t, b + 1, "dhcp", 2) && Is(t, b + 2, "pool", 1))
        {
            if (t.Count < b + 4) { o.Add("% Incomplete command."); return; }
            var pools = Device.Network.DhcpPools;
            var existingPool = pools.FirstOrDefault(x => x.Name.Equals(t[b + 3], StringComparison.OrdinalIgnoreCase));
            if (no) { if (existingPool != null) pools.Remove(existingPool); return; }
            if (existingPool == null) pools.Add(existingPool = new DhcpPool { Name = t[b + 3] });
            Device.Network.DhcpServerEnabled = true;
            _pool = existingPool;
            _mode = Mode.DhcpPool;
            return;
        }
        if (Is(t, b, "ip", 2) && Is(t, b + 1, "dhcp", 2) && Is(t, b + 2, "excluded-address", 2))
        {
            if (t.Count < b + 4 || !Ipv4.TryParse(t[b + 3], out var from)) { o.Add("% Invalid input detected."); return; }
            var range = t.Count > b + 4 && Ipv4.TryParse(t[b + 4], out var to) ? $"{from} {to}" : from.ToString();
            if (no) Device.Network.DhcpExcluded.Remove(range);
            else if (!Device.Network.DhcpExcluded.Contains(range)) Device.Network.DhcpExcluded.Add(range);
            return;
        }
        if (Is(t, b, "service", 3) && Is(t, b + 1, "dhcp", 2)) { Device.Network.DhcpServerEnabled = !no; return; }
        if (Is(t, b, "ip", 2) && Is(t, b + 1, "dns", 2) && Is(t, b + 2, "server", 1)) { Device.Network.DnsServerEnabled = !no; return; }
        if (Is(t, b, "ip", 2) && Is(t, b + 1, "name-server", 2))
        {
            Device.Network.DnsServer = no ? "" : t.Count > b + 2 && Ipv4.TryParse(t[b + 2], out var ns) ? ns.ToString() : Device.Network.DnsServer;
            return;
        }
        if (Is(t, b, "ip", 2) && Is(t, b + 1, "host", 2))
        {
            if (t.Count < b + 3) { o.Add("% Incomplete command."); return; }
            var recs = Device.Network.DnsRecords;
            var rec = recs.FirstOrDefault(r => r.Name.Equals(t[b + 2], StringComparison.OrdinalIgnoreCase));
            if (no) { if (rec != null) recs.Remove(rec); return; }
            if (t.Count < b + 4 || !Ipv4.TryParse(t[b + 3], out var hostIp)) { o.Add("% Invalid input detected."); return; }
            if (rec == null) recs.Add(new DnsRecord { Name = t[b + 2], Address = hostIp.ToString() });
            else rec.Address = hostIp.ToString();
            Device.Network.DnsServerEnabled = true;
            return;
        }
        if (Is(t, b, "ip", 2) && Is(t, b + 1, "route", 2))
        {
            if (t.Count < b + 5) { o.Add("% Incomplete command."); return; }
            if (!Ipv4.TryParse(t[b + 2], out var net) || !Ipv4.TryParse(t[b + 3], out var mask) || !Ipv4.IsValidMask(mask) || !Ipv4.TryParse(t[b + 4], out var nh))
            {
                o.Add("% Invalid input detected.");
                return;
            }
            var routes = Device.Network.StaticRoutes;
            var existing = routes.FirstOrDefault(r => r.Network == net.Network(mask).ToString() && r.Mask == mask.ToString() && r.NextHop == nh.ToString());
            if (no) { if (existing != null) routes.Remove(existing); }
            else if (existing == null) routes.Add(new StaticRoute { Network = net.Network(mask).ToString(), Mask = mask.ToString(), NextHop = nh.ToString() });
            return;
        }
        if (Is(t, b, "ip", 2) && Is(t, b + 1, "default-gateway", 3))
        {
            Device.Network.DefaultGateway = no ? "" : t.Count > b + 2 && Ipv4.TryParse(t[b + 2], out var gw) ? gw.ToString() : Device.Network.DefaultGateway;
            return;
        }
        Invalid(o, t[0]);
    }

    private void RunInterface(List<string> t, string raw, List<string> o)
    {
        if (_iface == null) { _mode = Mode.Config; return; }
        if (Is(t, 0, "interface", 3)) { RunConfig(t, raw, o); return; }
        bool no = Is(t, 0, "no", 2);
        int b = no ? 1 : 0;

        if (Is(t, b, "shutdown", 2))
        {
            _iface.IsUp = no;
            o.Add($"%LINK-5-CHANGED: Interface {LongName(_iface.Name)}, changed state to {(no ? "up" : "administratively down")}");
            return;
        }
        if (Is(t, b, "ip", 2) && Is(t, b + 1, "address", 2))
        {
            if (no) { _iface.IpAddress = ""; _iface.SubnetMask = ""; return; }
            if (t.Count < 4 || !Ipv4.TryParse(t[2], out var ip) || !Ipv4.TryParse(t[3], out var mask) || !Ipv4.IsValidMask(mask))
            {
                o.Add("% Invalid input detected.");
                return;
            }
            _iface.IpAddress = ip.ToString();
            _iface.SubnetMask = mask.ToString();
            return;
        }
        if (Is(t, b, "description", 3))
        {
            _iface.Description = no ? "" : string.Join(' ', t.Skip(1));
            return;
        }
        if (Is(t, b, "switchport", 3))
        {
            if (Is(t, b + 1, "access", 2) && Is(t, b + 2, "vlan", 1) && t.Count > b + 3 && int.TryParse(t[b + 3], out var vlan) && vlan is >= 1 and <= 4094)
            {
                _iface.Vlan = no ? 1 : vlan;
                return;
            }
            if (Is(t, b + 1, "mode", 2))
            {
                if (Is(t, b + 2, "trunk", 1)) _iface.Mode = no ? SwitchportMode.Access : SwitchportMode.Trunk;
                else if (Is(t, b + 2, "access", 1)) _iface.Mode = SwitchportMode.Access;
                else o.Add("% Invalid input detected.");
                return;
            }
            if (Is(t, b + 1, "trunk", 2) && Is(t, b + 2, "allowed", 2) && Is(t, b + 3, "vlan", 1))
            {
                _iface.AllowedVlans = no || t.Count <= b + 4 ? "all" : string.Join("", t.Skip(b + 4)).Replace("add", "");
                return;
            }
            o.Add("% Invalid input detected.");
            return;
        }
        Invalid(o, t[0]);
    }

    private void RunDhcpPool(List<string> t, List<string> o)
    {
        if (_pool == null) { _mode = Mode.Config; return; }
        if (Is(t, 0, "network", 3) && t.Count >= 3 && Ipv4.TryParse(t[1], out var n) && Ipv4.TryParse(t[2], out var m) && Ipv4.IsValidMask(m))
        {
            _pool.Network = n.Network(m).ToString();
            _pool.Mask = m.ToString();
            return;
        }
        if (Is(t, 0, "default-router", 3) && t.Count >= 2 && Ipv4.TryParse(t[1], out var r)) { _pool.DefaultRouter = r.ToString(); return; }
        if (Is(t, 0, "dns-server", 3) && t.Count >= 2 && Ipv4.TryParse(t[1], out var d)) { _pool.DnsServer = d.ToString(); return; }
        // Как в IOS: команда глобального режима выходит из подрежима и выполняется.
        _mode = Mode.Config;
        RunConfig(t, string.Join(' ', t), o);
    }

    private void RunVlan(List<string> t, List<string> o)
    {
        if (_vlan == null) { _mode = Mode.Config; return; }
        if (Is(t, 0, "name", 2) && t.Count >= 2) { _vlan.Name = t[1]; return; }
        _mode = Mode.Config;
        RunConfig(t, string.Join(' ', t), o);
    }

    private IEnumerable<string> VlanBrief()
    {
        yield return "VLAN Name                             Status    Ports";
        yield return "---- -------------------------------- --------- -------------------------------";
        var ids = Device.Network.Vlans.Select(v => v.Id).Append(1)
            .Concat(Device.Network.Interfaces.Where(i => i.Mode == SwitchportMode.Access).Select(i => i.Vlan)).Distinct().OrderBy(x => x);
        foreach (var id in ids)
        {
            var name = id == 1 ? "default" : Device.Network.Vlans.FirstOrDefault(v => v.Id == id)?.Name ?? $"VLAN{id:0000}";
            var ports = string.Join(", ", Device.Network.Interfaces.Where(i => i.Mode == SwitchportMode.Access && i.Vlan == id).Select(i => i.Name));
            yield return $"{id,-5}{name,-33}active    {ports}";
        }
        var trunks = Device.Network.Interfaces.Where(i => i.Mode == SwitchportMode.Trunk).ToList();
        if (trunks.Count > 0)
        {
            yield return "";
            yield return "Trunk ports: " + string.Join(", ", trunks.Select(i => $"{i.Name} (allowed {i.AllowedVlans})"));
        }
    }

    private static void Invalid(List<string> o, string token)
    {
        o.Add($"% Invalid input detected at '{token}'. Type ? for help.");
    }

    private IEnumerable<string> Help() => _mode switch
    {
        Mode.User => new[] { "  enable              Turn on privileged commands", "  ping <ip>           Send echo messages", "  show ...            Show running system information", "  exit                Exit from the EXEC" },
        Mode.Privileged => new[] { "  configure terminal  Enter configuration mode", "  show running-config | ip interface brief | ip route | arp | mac address-table | vlan brief | ip dhcp binding | hosts",
            "  ping <ip|name>", "  traceroute <ip|name>", "  write / copy run start", "  disable", "  exit" },
        Mode.Config => new[] { "  hostname <name>", "  interface <name>    e.g. interface fa0/0", "  ip route <net> <mask> <next-hop>", "  ip default-gateway <ip>",
            "  vlan <n>            (name <text>)", "  ip dhcp pool <name> (network, default-router, dns-server)", "  ip dhcp excluded-address <from> [to]",
            "  ip host <name> <ip> (DNS record)", "  ip name-server <ip>", "  no ...", "  do <exec command>", "  exit / end" },
        Mode.DhcpPool => new[] { "  network <net> <mask>", "  default-router <ip>", "  dns-server <ip>", "  exit" },
        Mode.Vlan => new[] { "  name <text>", "  exit" },
        _ => new[] { "  ip address <ip> <mask>", "  no ip address", "  shutdown / no shutdown", "  description <text>", "  switchport mode access | trunk",
            "  switchport access vlan <n>", "  switchport trunk allowed vlan <list>", "  exit / end" },
    };

    private IEnumerable<string> RunningConfig()
    {
        yield return "Building configuration...";
        yield return "!";
        yield return $"hostname {Host}";
        yield return "!";
        foreach (var i in Device.Network.Interfaces)
        {
            yield return $"interface {LongName(i.Name)}";
            if (!string.IsNullOrEmpty(i.Description)) yield return $" description {i.Description}";
            if (i.Mode == SwitchportMode.Trunk)
            {
                yield return " switchport mode trunk";
                if (!i.AllowedVlans.Equals("all", StringComparison.OrdinalIgnoreCase)) yield return $" switchport trunk allowed vlan {i.AllowedVlans}";
            }
            else if (Device.Kind == DeviceKind.Switch && i.Vlan != 1) yield return $" switchport access vlan {i.Vlan}";
            yield return string.IsNullOrEmpty(i.IpAddress) ? " no ip address" : $" ip address {i.IpAddress} {i.SubnetMask}";
            if (!i.IsUp) yield return " shutdown";
            yield return "!";
        }
        foreach (var v in Device.Network.Vlans) { yield return $"vlan {v.Id}"; yield return $" name {v.Name}"; yield return "!"; }
        foreach (var ex in Device.Network.DhcpExcluded) yield return $"ip dhcp excluded-address {ex}";
        foreach (var p in Device.Network.DhcpPools)
        {
            yield return $"ip dhcp pool {p.Name}";
            if (!string.IsNullOrEmpty(p.Network)) yield return $" network {p.Network} {p.Mask}";
            if (!string.IsNullOrEmpty(p.DefaultRouter)) yield return $" default-router {p.DefaultRouter}";
            if (!string.IsNullOrEmpty(p.DnsServer)) yield return $" dns-server {p.DnsServer}";
            yield return "!";
        }
        foreach (var r in Device.Network.DnsRecords) yield return $"ip host {r.Name} {r.Address}";
        if (!string.IsNullOrEmpty(Device.Network.DnsServer)) yield return $"ip name-server {Device.Network.DnsServer}";
        if (!string.IsNullOrEmpty(Device.Network.DefaultGateway)) yield return $"ip default-gateway {Device.Network.DefaultGateway}";
        foreach (var r in Device.Network.StaticRoutes) yield return $"ip route {r.Network} {r.Mask} {r.NextHop}";
        yield return "!";
        yield return "end";
    }

    private IEnumerable<string> InterfaceBrief()
    {
        yield return $"{"Interface",-24}{"IP-Address",-16}{"OK?",-5}{"Method",-8}{"Status",-23}Protocol";
        var node = Sim.Node(Device);
        foreach (var i in Device.Network.Interfaces)
        {
            var port = node?.Ports.FirstOrDefault(p => p.Config == i);
            bool peerUp = port?.Peer?.IsUp ?? false;
            string status = !i.IsUp ? "administratively down" : port == null ? "down" : "up";
            string proto = i.IsUp && peerUp ? "up" : "down";
            yield return $"{LongName(i.Name),-24}{(string.IsNullOrEmpty(i.IpAddress) ? "unassigned" : i.IpAddress),-16}{"YES",-5}{"manual",-8}{status,-23}{proto}";
        }
    }

    private IEnumerable<string> RouteTable()
    {
        yield return "Codes: C - connected, S - static";
        yield return "";
        if (Sim.Node(Device) is not IpNode node) { yield return "% IP routing not supported on this device"; yield break; }
        var table = node.RoutingTable();
        var gw = table.FirstOrDefault(r => r.Mask.Value == 0);
        yield return gw != null ? $"Gateway of last resort is {gw.NextHop} to network 0.0.0.0" : "Gateway of last resort is not set";
        yield return "";
        foreach (var r in table.OrderBy(r => r.Network)) yield return r.Format();
    }

    private IEnumerable<string> MacTable()
    {
        yield return "          Mac Address Table";
        yield return "-------------------------------------------";
        yield return "Vlan    Mac Address       Type        Ports";
        yield return "----    -----------       --------    -----";
        if (Sim.Node(Device) is SwitchNode sw)
            foreach (var (mac, e) in sw.MacTable.OrderBy(x => x.Key))
                yield return $"{mac.Vlan,4}    {mac.Mac,-18}DYNAMIC     {e.Port.Name}";
    }

    private IEnumerable<string> Interfaces()
    {
        foreach (var i in Device.Network.Interfaces)
        {
            yield return $"{LongName(i.Name)} is {(i.IsUp ? "up" : "administratively down")}";
            yield return $"  Hardware address is {i.MacAddress}";
            if (!string.IsNullOrEmpty(i.IpAddress)) yield return $"  Internet address is {i.IpAddress}/{(Ipv4.TryParse(i.SubnetMask, out var m) ? m.PrefixLength : 0)}";
            if (!string.IsNullOrEmpty(i.Description)) yield return $"  Description: {i.Description}";
        }
    }
}
