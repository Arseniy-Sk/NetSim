using NetSim.Calculators;

namespace NetSim.Tests;

public class CalculatorTests
{
    [Fact]
    public void Lab8_variant1_load_exceeds_and_needs_restructuring()
    {
        // ПР8, вариант 1: M = 2 группы, N = 6 ПК, K = 0,08, 10Base-T, g = s = 50 %, S = 350 м.
        var res = LanLoadCalculator.Calculate(new LanLoadInput
        {
            GroupSizes = new[] { 6, 6 }, K = 0.08, CmaxMbps = 10, InterGroupPercent = 50, ServerPercent = 50,
            DistanceInGroupM = 2, DistanceBetweenGroupsM = 350,
        });
        Assert.Equal(0.8, res.Ci, 6);
        Assert.Equal(9.6, res.CSum, 6);
        Assert.Equal(0.96, res.LoadUnstructured, 6);
        Assert.False(res.UnstructuredOk);
        Assert.Equal(4.8, res.CInterGroup, 6);
        var backbone = res.Recommendations.First(r => r.Segment.Contains("Магистраль"));
        Assert.Equal("100base-fx", backbone.StandardId); // > 100 м и > 0,35 при 10 Мбит/с
        Assert.Contains("ρн = CΣ / Cmax", res.Report.ToText());
    }

    [Fact]
    public void Lab8_pdv_example_matches_method()
    {
        // Пример из ПР8: 10Base-T, L = 3 м: 15,3 + 3·0,113 = 15,64; 165 + 3·0,113 = 165,34.
        var res = PdvCalculator.Calculate(new[] { new PdvSegment("10base-t", 3), new PdvSegment("10base-t", 3) });
        Assert.Equal(15.639, res.Items[0].Value, 3);
        Assert.Equal(165.339, res.Items[1].Value, 3);
        Assert.True(res.Ok);
    }

    [Fact]
    public void Pdv_fails_for_too_long_path()
    {
        var path = new[]
        {
            new PdvSegment("10base-5", 500), new PdvSegment("10base-5", 500), new PdvSegment("10base-5", 500),
            new PdvSegment("10base-5", 500), new PdvSegment("10base-5", 500), new PdvSegment("10base-5", 500),
        };
        Assert.False(PdvCalculator.Calculate(path).Ok);
    }

    [Fact]
    public void Lab10_capacity_and_range()
    {
        var cap = WirelessCalculator.Capacity(new WirelessCapacityInput { Stations = 20, CiMbps = 2, CsMbps = 6, CmMbps = 6, H = 1, Standard = "802.11g" });
        Assert.Equal(44, cap.Cmax, 6); // (20 − 1)·2 + 6·0 + 6
        Assert.Equal(3, cap.AccessPoints);

        var range = WirelessCalculator.Range(new RadioRangeInput { PtDbm = 20, GtDbi = 5, GrDbi = 5, PminDbm = -71, LtDb = 0, LrDb = 0, SomDb = 10, FrequencyMhz = 2437 });
        Assert.Equal(101, range.Y, 6);
        Assert.Equal(91, range.Fsl, 6);
        Assert.InRange(range.DistanceM, 300, 360);

        var walls = WirelessCalculator.Range(new RadioRangeInput { PtDbm = 20, GtDbi = 5, GrDbi = 5, PminDbm = -71, LtDb = 0, LrDb = 0, Walls = 2, FrequencyMhz = 2437 });
        Assert.True(walls.DistanceM < range.DistanceM);
        Assert.Equal(2437, WirelessCalculator.ChannelFrequency(6));
        Assert.Equal(5180, WirelessCalculator.ChannelFrequency(36));
    }
}

public class FloorPlanTests
{
    [Fact]
    public void Shared_wall_between_rooms_counts_once()
    {
        var plan = new NetSim.Core.Model.FloorPlan { PixelsPerMeter = 10 };
        plan.BuildCorridorPlan(new[] { 5.0, 5.0 }, Array.Empty<double>(), 5, 0, 0, 0);
        // Из центра первой комнаты в центр второй — через одну общую стену.
        Assert.Equal(1, plan.WallsBetween(25, 25, 75, 25));
        // Внутри одной комнаты — без стен.
        Assert.Equal(0, plan.WallsBetween(10, 10, 40, 40));
        // Из комнаты наружу — одна стена.
        Assert.Equal(1, plan.WallsBetween(25, 25, 25, 80));
        Assert.Equal(5, plan.DistanceM(25, 25, 75, 25), 6);
    }

    [Fact]
    public void Wifi_office_sample_coverage()
    {
        var p = NetSim.Core.Samples.SampleProjects.WifiOffice();
        var res = WirelessCoverage.Analyze(p.Root, NetSim.Core.Catalog.DeviceCatalog.Default);
        Assert.Equal(16, res.Rows.Count);
        Assert.Contains(res.Rows, r => r.Walls >= 1);
        Assert.True(res.Radius.Values.All(v => v.Clear > v.OneWall));
        Assert.Equal(16, WirelessCoverage.ApplyLengths(res));
        Assert.All(res.Rows, r => Assert.Equal(Math.Round(r.DistanceM, 1), r.Link.LengthM));
    }
}
