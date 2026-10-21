using NetSim.Core.Model;
using NetSim.Core.Samples;
using NetSim.Core.Topology;
using NetSim.Protocols;
using NetSim.Protocols.Cli;

namespace NetSim.Tests;

public class ProtocolServicesTests
{
    private static void Ip(Device d, string iface, string ip, string mask = "255.255.255.0")
    {
        var i = d.Network.Interfaces.First(x => x.Name == iface);
        i.IpAddress = ip;
        i.SubnetMask = mask;
        i.IsUp = true;
    }

    [Fact]
    public void Router_dhcp_pool_configured_from_cli_gives_address_to_pc()
    {
        var p = SampleProjects.IpDemo();
        var sim = new ProtocolSimulator(p);
        var r1 = p.AllDevices().First(d => d.Name == "R1");
        var cli = CommandShell.For(r1, sim);
        foreach (var c in new[] { "en", "conf t", "ip dhcp excluded-address 192.168.1.1 192.168.1.20", "ip dhcp pool LAN1",
                     "network 192.168.1.0 255.255.255.0", "default-router 192.168.1.1", "dns-server 192.168.1.1", "end" })
            cli.Execute(c);

        var pc = p.AllDevices().First(d => d.Name == "PC 2");
        var pcIf = pc.Network.Interfaces[0];
        pcIf.IpAddress = ""; pcIf.SubnetMask = ""; pc.Network.DefaultGateway = "";
        var shell = CommandShell.For(pc, sim);
        var output = shell.Execute("ipconfig /renew");
        Assert.Contains(output, l => l.Contains("192.168.1.21"));
        Assert.Equal("192.168.1.21", pcIf.IpAddress);
        Assert.Equal("192.168.1.1", pc.Network.DefaultGateway);
        Assert.Contains(shell.Execute("ping 192.168.2.10"), l => l.Contains("Received = 4"));
        Assert.Contains(cli.Execute("show ip dhcp binding"), l => l.StartsWith("192.168.1.21"));
    }

    [Fact]
    public void Dns_server_resolves_names_for_ping()
    {
        var p = SampleProjects.IpDemo();
        var b = new TopologyBuilder(p);
        var sw2 = p.AllDevices().First(d => d.Name == "SW2");
        var srv = b.CreateDevice("server", 0, 0, name: "DNS");
        var l = b.Connect(sw2, srv, "100base-tx");
        Ip(srv, l.BInterface, "192.168.2.53");
        srv.Network.DefaultGateway = "192.168.2.1";
        srv.Network.DnsServerEnabled = true;
        srv.Network.DnsRecords.Add(new DnsRecord { Name = "www.college.ru", Address = "192.168.2.11" });

        var pc1 = p.AllDevices().First(d => d.Name == "PC 1");
        pc1.Network.DnsServer = "192.168.2.53";
        var sim = new ProtocolSimulator(p);
        var shell = CommandShell.For(pc1, sim);
        Assert.Contains(shell.Execute("nslookup www.college.ru"), x => x.Contains("192.168.2.11"));
        Assert.Contains(shell.Execute("ping www.college.ru"), x => x.Contains("Reply from 192.168.2.11"));
        Assert.Contains(shell.Execute("ping nosuch.host"), x => x.Contains("could not find host"));
    }

    [Fact]
    public void Tracert_lists_every_router()
    {
        var p = new Project();
        var b = new TopologyBuilder(p);
        var r1 = b.CreateDevice("router", 0, 0, name: "R1");
        var r2 = b.CreateDevice("router", 0, 0, name: "R2");
        var a = b.CreateDevice("pc", 0, 0, name: "A");
        var c = b.CreateDevice("pc", 0, 0, name: "C");
        var la = b.Connect(a, r1); var lr = b.Connect(r1, r2, "serial-v35"); var lc = b.Connect(r2, c);
        Ip(a, la.AInterface, "10.1.0.10"); a.Network.DefaultGateway = "10.1.0.1";
        Ip(r1, la.BInterface, "10.1.0.1"); Ip(r1, lr.AInterface, "10.12.0.1");
        Ip(r2, lr.BInterface, "10.12.0.2"); Ip(r2, lc.AInterface, "10.2.0.1");
        Ip(c, lc.BInterface, "10.2.0.10"); c.Network.DefaultGateway = "10.2.0.1";
        r1.Network.StaticRoutes.Add(new StaticRoute { Network = "10.2.0.0", Mask = "255.255.255.0", NextHop = "10.12.0.2" });
        r2.Network.StaticRoutes.Add(new StaticRoute { Network = "10.1.0.0", Mask = "255.255.255.0", NextHop = "10.12.0.1" });

        var sim = new ProtocolSimulator(p);
        var output = sim.TraceBlocking(a, "10.2.0.10", cisco: false);
        var hops = output.Where(l => l.TrimStart().Length > 0 && char.IsDigit(l.TrimStart()[0])).ToList();
        Assert.Equal(3, hops.Count);
        Assert.EndsWith("10.1.0.1", hops[0]);
        Assert.EndsWith("10.12.0.2", hops[1]);
        Assert.EndsWith("10.2.0.10", hops[2]);
        Assert.Contains("Trace complete.", output);
    }

    [Fact]
    public void Vlans_isolate_hosts_and_trunk_carries_them()
    {
        // Два коммутатора, соединённые trunk; PC-A и PC-C в VLAN 10, PC-B в VLAN 20 — все в одной IP-сети.
        var p = new Project();
        var b = new TopologyBuilder(p);
        var s1 = b.CreateDevice("switch-cisco-2960", 0, 0, name: "S1");
        var s2 = b.CreateDevice("switch-cisco-2960", 0, 0, name: "S2");
        var pa = b.CreateDevice("pc", 0, 0, name: "A");
        var pb = b.CreateDevice("pc", 0, 0, name: "B");
        var pcc = b.CreateDevice("pc", 0, 0, name: "C");
        var trunk = b.Connect(s1, s2);
        var la = b.Connect(s1, pa); var lb = b.Connect(s1, pb); var lc = b.Connect(s2, pcc);
        Ip(pa, la.BInterface, "10.0.0.1"); Ip(pb, lb.BInterface, "10.0.0.2"); Ip(pcc, lc.BInterface, "10.0.0.3");

        var sim = new ProtocolSimulator(p);
        var c1 = CommandShell.For(s1, sim);
        foreach (var c in new[] { "en", "conf t", "vlan 10", "name Students", "exit", "vlan 20",
                     $"int {la.AInterface}", "switchport mode access", "switchport access vlan 10",
                     $"int {lb.AInterface}", "switchport access vlan 20",
                     $"int {trunk.AInterface}", "switchport mode trunk", "end" })
            c1.Execute(c);
        var c2 = CommandShell.For(s2, sim);
        foreach (var c in new[] { "en", "conf t", $"int {lc.AInterface}", "switchport access vlan 10", $"int {trunk.BInterface}", "switchport mode trunk", "end" })
            c2.Execute(c);

        Assert.Contains(sim.PingBlocking(pa, "10.0.0.3"), l => l.Contains("Received = 4")); // VLAN 10 через trunk
        Assert.Contains(sim.PingBlocking(pa, "10.0.0.2"), l => l.Contains("Received = 0")); // разные VLAN
        Assert.Contains(c1.Execute("show vlan brief"), l => l.StartsWith("10") && l.Contains("Students"));

        c2.Execute("conf t");
        c2.Execute($"int {trunk.BInterface}");
        var dbg = c2.Execute("switchport trunk allowed vlan 20");
        Assert.Empty(dbg);
        Assert.Equal("20", s2.Network.Interfaces.First(i => i.Name == trunk.BInterface).AllowedVlans);
        sim.Rebuild();
        Assert.Contains(sim.PingBlocking(pa, "10.0.0.3"), l => l.Contains("Received = 0")); // VLAN 10 запрещена на trunk
    }
}
