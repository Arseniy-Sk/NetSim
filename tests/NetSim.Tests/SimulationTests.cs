using NetSim.Core.Model;
using NetSim.Core.Samples;
using NetSim.Core.Topology;
using NetSim.Simulation.Traffic;

namespace NetSim.Tests;

public class TrafficSimulatorTests
{
    private static (Project P, Device Pc, Device Server, Link Link) ClientServer(string std = "10base-t")
    {
        var p = new Project();
        var b = new TopologyBuilder(p);
        var pc = b.CreateDevice("pc", 0, 0);
        var srv = b.CreateDevice("server", 0, 0);
        var link = b.Connect(pc, srv, std);
        b.InstallSoftware(srv, "sw-http-server");
        var profile = DefaultProfile("http");
        profile.TransactionSize = Distribution.Constant(1000);
        profile.TimeBetween = Distribution.Constant(0.1);
        b.AddFlow(pc, srv, profile);
        srv.Server.ReplySize = Distribution.Constant(10_000);
        srv.Server.ReplyDelay = Distribution.Constant(0.01);
        return (p, pc, srv, link);
    }

    private static TrafficProfile DefaultProfile(string id) => NetSim.Core.Catalog.DeviceCatalog.Default.Profile(id)!.Clone();

    [Fact]
    public void Client_server_produces_requests_replies_and_utilization()
    {
        var (p, pc, srv, link) = ClientServer();
        var sim = new TrafficSimulator(p);
        sim.RunUntil(10);

        Assert.Empty(sim.Diagnostics);
        Assert.InRange(sim.StatsOf(srv).RequestsServed, 90, 101);
        Assert.True(sim.StatsOf(pc).TransactionsCompleted > 80);

        // 10 запросов/с: ответ 10000 байт = 7 кадров по 1500 → ≈ (10000 + 7·38)·8·10 / 10 Мбит/с ≈ 8,2 %
        var util = sim.LinkValue(link, StatKeys.AverageUtilization);
        Assert.InRange(util, 0.06, 0.10);
        Assert.True(sim.DeviceValue(srv, StatKeys.PacketsReceived) > 0);
    }

    [Fact]
    public void Same_seed_gives_same_results()
    {
        var (p1, _, s1, _) = ClientServer();
        var (p2, _, s2, _) = ClientServer();
        p1.Flows[0].Profile.TimeBetween = Distribution.Exponential(0.1);
        p2.Flows[0].Profile.TimeBetween = Distribution.Exponential(0.1);
        var a = new TrafficSimulator(p1); a.RunUntil(5);
        var b = new TrafficSimulator(p2); b.RunUntil(5);
        Assert.Equal(a.StatsOf(s1).PacketsReceived, b.StatsOf(s2).PacketsReceived);
    }

    [Fact]
    public void Overloaded_link_drops_packets()
    {
        var p = new Project();
        var b = new TopologyBuilder(p);
        var a = b.CreateDevice("pc", 0, 0);
        var c = b.CreateDevice("pc", 0, 0);
        var link = b.Connect(a, c, "modem-56k");
        var profile = DefaultProfile("bulk-data");
        profile.TimeBetween = Distribution.Constant(1);
        b.AddFlow(a, c, profile);
        var sim = new TrafficSimulator(p);
        sim.RunUntil(5);
        Assert.True(sim.LinkValue(link, StatKeys.PacketsDropped) > 0);
        Assert.True(sim.IsOverloaded(link));
    }

    [Fact]
    public void Token_ring_is_more_loaded_than_fast_ethernet_with_same_traffic()
    {
        var p = new Project();
        var b = new TopologyBuilder(p);
        var ring = b.CreateDevice("seg-token-ring", 0, 0);
        var sw = b.CreateDevice("switch-eth", 0, 0);
        var trA = b.CreateDevice("ws-token-ring", 0, 0);
        var trB = b.CreateDevice("ws-token-ring", 0, 0);
        var feA = b.CreateDevice("pc", 0, 0);
        var feB = b.CreateDevice("pc", 0, 0);
        b.Connect(trA, ring); b.Connect(trB, ring);
        b.Connect(feA, sw, "100base-tx"); b.Connect(feB, sw, "100base-tx");
        foreach (var (x, y) in new[] { (trA, trB), (feA, feB) })
        {
            var pr = DefaultProfile("lan-p2p");
            pr.TimeBetween = Distribution.Constant(0.01);
            b.AddFlow(x, y, pr);
        }
        var sim = new TrafficSimulator(p);
        sim.RunUntil(5);
        Assert.True(sim.DeviceValue(ring, StatKeys.AverageUtilization) > sim.DeviceValue(sw, StatKeys.AverageUtilization));
    }

    [Theory]
    [InlineData("techno")]
    [InlineData("hier")]
    public void Samples_simulate_without_route_errors(string id)
    {
        var p = SampleProjects.Create(id);
        var sim = new TrafficSimulator(p);
        sim.RunUntil(20);
        Assert.Empty(sim.Diagnostics);
        Assert.True(sim.ProcessedEvents > 0);
    }
}
