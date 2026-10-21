using NetSim.Core.Model;
using NetSim.Core.Samples;
using NetSim.Core.Topology;
using NetSim.Protocols;
using NetSim.Protocols.Cli;
using NetSim.Protocols.Nodes;

namespace NetSim.Tests;

public class ProtocolTests
{
    [Fact]
    public void Ping_within_subnet_and_through_router()
    {
        var p = SampleProjects.IpDemo();
        var sim = new ProtocolSimulator(p);
        var pc1 = p.AllDevices().First(d => d.Name == "PC 1");

        var local = sim.PingBlocking(pc1, "192.168.1.11");
        Assert.Contains(local, l => l.Contains("Received = 4"));

        var remote = sim.PingBlocking(pc1, "192.168.2.10");
        Assert.Contains(remote, l => l.Contains("Received = 4"));
        Assert.Contains(remote, l => l.Contains("TTL=127")); // один маршрутизатор на пути

        var sw = (SwitchNode)sim.Node(p.AllDevices().First(d => d.Name == "SW1"))!;
        Assert.NotEmpty(sw.MacTable);
    }

    [Fact]
    public void Ping_fails_without_gateway()
    {
        var p = SampleProjects.IpDemo();
        var pc1 = p.AllDevices().First(d => d.Name == "PC 1");
        pc1.Network.DefaultGateway = "";
        var sim = new ProtocolSimulator(p);
        var res = sim.PingBlocking(pc1, "192.168.2.10");
        Assert.Contains(res, l => l.Contains("Received = 0"));
    }

    [Fact]
    public void Cisco_cli_configures_router_and_pings()
    {
        var p = new Project();
        var b = new TopologyBuilder(p);
        var r = b.CreateDevice("router-cisco-1941", 0, 0, name: "R1");
        var pc = b.CreateDevice("pc", 0, 0, name: "PC");
        var link = b.Connect(r, pc, "100base-tx");
        var pcIf = pc.Network.Interfaces.First(i => i.Name == link.BInterface);
        pcIf.IpAddress = "10.0.0.2"; pcIf.SubnetMask = "255.255.255.0";

        var sim = new ProtocolSimulator(p);
        var cli = CommandShell.For(r, sim);
        Assert.Equal("R1>", cli.Prompt);
        cli.Execute("en");
        cli.Execute("conf t");
        Assert.Equal("R1(config)#", cli.Prompt);
        cli.Execute("hostname Core");
        cli.Execute($"int {link.AInterface.ToLowerInvariant()}");
        Assert.Equal("Core(config-if)#", cli.Prompt);
        cli.Execute("ip address 10.0.0.1 255.255.255.0");
        cli.Execute("no shut");
        cli.Execute("end");
        Assert.Equal("Core#", cli.Prompt);

        var brief = cli.Execute("sh ip int br");
        Assert.Contains(brief, l => l.Contains("10.0.0.1") && l.EndsWith("up"));
        var ping = cli.Execute("ping 10.0.0.2");
        Assert.Contains(ping, l => l.StartsWith("Success rate is 100 percent"));
        Assert.Contains(cli.Execute("show running-config"), l => l.Contains("ip address 10.0.0.1 255.255.255.0"));
    }

    [Fact]
    public void Static_route_between_two_routers()
    {
        var p = new Project();
        var b = new TopologyBuilder(p);
        var r1 = b.CreateDevice("router", 0, 0, name: "R1");
        var r2 = b.CreateDevice("router", 0, 0, name: "R2");
        var a = b.CreateDevice("pc", 0, 0, name: "A");
        var c = b.CreateDevice("pc", 0, 0, name: "C");
        var la = b.Connect(a, r1);
        var lr = b.Connect(r1, r2, "serial-v35");
        var lc = b.Connect(r2, c);
        void Ip(Device d, string iface, string ip) { var i = d.Network.Interfaces.First(x => x.Name == iface); i.IpAddress = ip; i.SubnetMask = "255.255.255.0"; i.IsUp = true; }
        Ip(a, la.AInterface, "10.1.0.10"); a.Network.DefaultGateway = "10.1.0.1";
        Ip(r1, la.BInterface, "10.1.0.1"); Ip(r1, lr.AInterface, "10.12.0.1");
        Ip(r2, lr.BInterface, "10.12.0.2"); Ip(r2, lc.AInterface, "10.2.0.1");
        Ip(c, lc.BInterface, "10.2.0.10"); c.Network.DefaultGateway = "10.2.0.1";

        var sim = new ProtocolSimulator(p);
        Assert.Contains(sim.PingBlocking(a, "10.2.0.10"), l => l.Contains("Received = 0"));

        r1.Network.StaticRoutes.Add(new StaticRoute { Network = "10.2.0.0", Mask = "255.255.255.0", NextHop = "10.12.0.2" });
        r2.Network.StaticRoutes.Add(new StaticRoute { Network = "10.1.0.0", Mask = "255.255.255.0", NextHop = "10.12.0.1" });
        Assert.Contains(sim.PingBlocking(a, "10.2.0.10"), l => l.Contains("Received = 4"));
    }
}
