using NetSim.Core.Catalog;
using NetSim.Core.Model;
using NetSim.Core.Samples;
using NetSim.Core.Serialization;
using NetSim.Core.Topology;

namespace NetSim.Tests;

public class CatalogTests
{
    [Fact]
    public void Default_catalog_loads_and_references_are_consistent()
    {
        var c = DeviceCatalog.Default;
        Assert.NotEmpty(c.Devices);
        Assert.NotEmpty(c.Standards);
        Assert.NotEmpty(c.Profiles);

        void Walk(IEnumerable<CatalogCategory> cats)
        {
            foreach (var cat in cats)
            {
                foreach (var item in cat.Items)
                    Assert.True(cat.IsSoftware ? c.SoftwareById(item) != null : c.Device(item) != null, $"{cat.Id}: нет элемента {item}");
                Walk(cat.Children);
            }
        }
        Walk(c.Categories);

        foreach (var d in c.Devices) Assert.NotNull(c.Standard(d.DefaultStandard));
        foreach (var s in c.Standards) Assert.All(s.Media, m => Assert.NotNull(c.Media(m)));
        foreach (var sw in c.Software) Assert.All(sw.Services, s => Assert.NotNull(c.Service(s)));
        foreach (var p in c.Profiles.Where(p => p.RequiredService != null)) Assert.NotNull(c.Service(p.RequiredService!));
    }

    [Fact]
    public void Token_ring_segment_accepts_only_token_ring()
    {
        var c = DeviceCatalog.Default;
        var ring = c.Device("seg-token-ring");
        var pc = c.Device("pc");
        var compatible = c.CompatibleStandards(pc, ring);
        Assert.All(compatible, s => Assert.Equal(TechFamily.TokenRing, s.Family));
        Assert.Equal("tr-16", c.SuggestStandard(pc, ring)!.Id);
    }
}

public class TopologyTests
{
    [Fact]
    public void Hub_links_form_one_collision_domain_switch_links_do_not()
    {
        var p = new Project();
        var b = new TopologyBuilder(p);
        var hub = b.CreateDevice("hub-eth", 0, 0);
        var sw = b.CreateDevice("switch-eth", 0, 0);
        var pcs = Enumerable.Range(0, 3).Select(_ => b.CreateDevice("pc", 0, 0)).ToList();
        var l1 = b.Connect(pcs[0], hub, "10base-t");
        var l2 = b.Connect(pcs[1], hub, "10base-t");
        var l3 = b.Connect(hub, sw, "10base-t");
        var l4 = b.Connect(sw, pcs[2], "100base-tx");

        var g = new NetworkGraph(p);
        Assert.Same(g.ChannelOf(l1), g.ChannelOf(l2));
        Assert.Same(g.ChannelOf(l1), g.ChannelOf(l3));
        Assert.NotSame(g.ChannelOf(l3), g.ChannelOf(l4));
        Assert.True(g.ChannelOf(l1).IsShared);
        Assert.False(g.ChannelOf(l4).IsShared);

        var path = g.FindPath(pcs[0].Id, pcs[2].Id)!;
        Assert.Equal(3, path.Count);
        var hops = g.SplitIntoHops(pcs[0].Id, path);
        Assert.Equal(2, hops.Count); // PC → хаб → коммутатор одной передачей, затем порт коммутатора
    }

    [Fact]
    public void End_nodes_are_not_used_as_transit()
    {
        var p = new Project();
        var b = new TopologyBuilder(p);
        var a = b.CreateDevice("pc", 0, 0);
        var mid = b.CreateDevice("pc", 0, 0);
        var c = b.CreateDevice("pc", 0, 0);
        b.Connect(a, mid);
        b.Connect(mid, c);
        Assert.Null(new NetworkGraph(p).FindPath(a.Id, c.Id));
    }

    [Fact]
    public void Container_connects_inner_and_outer_levels()
    {
        var p = SampleProjects.Hier();
        var g = new NetworkGraph(p);
        var ca = p.AllDevices().First(d => d.Name == "California PC 1");
        var ny = p.AllDevices().First(d => d.Name == "New York server");
        Assert.NotNull(g.FindPath(ca.Id, ny.Id));
    }

    [Fact]
    public void Client_server_flow_requires_service()
    {
        var p = new Project();
        var b = new TopologyBuilder(p);
        var pc = b.CreateDevice("pc", 0, 0);
        var srv = b.CreateDevice("server", 0, 0);
        var (flow, error) = b.AddFlow(pc, srv, "database");
        Assert.Null(flow);
        Assert.NotNull(error);

        b.InstallSoftware(srv, "sw-small-office-db");
        (flow, error) = b.AddFlow(pc, srv, "database");
        Assert.NotNull(flow);
        Assert.Null(error);
    }

    [Fact]
    public void Remove_device_removes_links_and_flows()
    {
        var p = SampleProjects.Techno();
        var server = p.AllDevices().First(d => d.Name == "Data server");
        p.RemoveDevice(server);
        Assert.DoesNotContain(p.AllLinks(), l => l.Touches(server.Id));
        Assert.DoesNotContain(p.Flows, f => f.TargetId == server.Id || f.SourceId == server.Id);
    }
}

public class SerializationTests
{
    [Theory]
    [InlineData("techno")]
    [InlineData("hier")]
    [InlineData("ip-demo")]
    [InlineData("wifi-office")]
    public void Samples_round_trip(string id)
    {
        var p = SampleProjects.Create(id);
        var json = ProjectSerializer.ToJson(p);
        var back = ProjectSerializer.FromJson(json);
        Assert.Equal(p.AllDevices().Count(), back.AllDevices().Count());
        Assert.Equal(p.AllLinks().Count(), back.AllLinks().Count());
        Assert.Equal(p.Flows.Count, back.Flows.Count);
        Assert.Equal(json, ProjectSerializer.ToJson(back));
    }
}

public class DistributionTests
{
    [Theory]
    [InlineData(DistributionLaw.Normal, 2048, 512)]
    [InlineData(DistributionLaw.Exponential, 5, 0)]
    [InlineData(DistributionLaw.Erlang, 1.5, 0.4)]
    public void Sample_mean_matches(DistributionLaw law, double mean, double sd)
    {
        var d = new Distribution { Law = law, Mean = mean, StdDev = sd };
        var rng = new Random(7);
        var xs = Enumerable.Range(0, 50_000).Select(_ => d.Sample(rng)).ToList();
        Assert.InRange(xs.Average(), mean * 0.97, mean * 1.03);
    }

    [Fact]
    public void Uniform_bounds_from_mean_and_std_match_lab5_example()
    {
        var (min, max) = Distribution.UniformBounds(2, 0.7);
        Assert.Equal(0.79, min, 2);
        Assert.Equal(3.21, max, 2);
    }

    [Fact]
    public void Erlang_order()
    {
        Assert.Equal(14, Distribution.ErlangOrder(1.5, 0.4));
    }
}
