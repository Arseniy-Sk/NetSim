using NetSim.Core.Catalog;
using NetSim.Core.Model;

namespace NetSim.Calculators;

public sealed record CoverageRow(Device AccessPoint, Device Station, Link Link, double DistanceM, int Walls, double RangeM, bool Ok);

public sealed class CoverageResult
{
    public List<CoverageRow> Rows { get; } = new();

    /// <summary>Радиус зоны покрытия каждой точки доступа без стен и через 1 стену, м.</summary>
    public Dictionary<Device, (double Clear, double OneWall)> Radius { get; } = new();

    public CalcReport Report = null!;
}

/// <summary>
/// Анализ покрытия беспроводной сети по плану помещения (ПР10):
/// расстояние от точки доступа до каждой станции, число стен на пути, допустимая дальность.
/// </summary>
public static class WirelessCoverage
{
    public static double SensitivityFor(double rateMbps)
    {
        // Берём ближайшую скорость из таблицы 3, не выше требуемой (на меньшей скорости чувствительность лучше).
        foreach (var (mbps, dbm) in WirelessCalculator.Sensitivity)
            if (rateMbps >= mbps) return dbm;
        return WirelessCalculator.Sensitivity[^1].Dbm;
    }

    public static double FrequencyFor(int channel)
    {
        try { return WirelessCalculator.ChannelFrequency(channel); }
        catch (ArgumentOutOfRangeException) { return 2437; }
    }

    public static RadioRangeInput InputFor(Device ap, Device? station, int walls, double wallLossDb)
    {
        var a = ap.Radio;
        var s = station?.Radio;
        return new RadioRangeInput
        {
            PtDbm = a.PowerDbm,
            GtDbi = a.AntennaGainDbi,
            GrDbi = s?.AntennaGainDbi ?? 2,
            LtDb = a.CableLossDb,
            LrDb = s?.CableLossDb ?? 0,
            PminDbm = SensitivityFor(Math.Min(a.RateMbps, s?.RateMbps ?? a.RateMbps)),
            FrequencyMhz = FrequencyFor(a.Channel),
            SomDb = 10,
            Walls = walls,
            WallLossDb = wallLossDb,
        };
    }

    public static CoverageResult Analyze(Level level, DeviceCatalog catalog, string lang = "ru")
    {
        var res = new CoverageResult();
        var r = new CalcReport(lang);
        res.Report = r;
        var plan = level.Plan;
        var byId = level.Devices.ToDictionary(d => d.Id);

        foreach (var ap in level.Devices.Where(d => d.Kind == DeviceKind.AccessPoint))
        {
            var clear = WirelessCalculator.Range(InputFor(ap, null, 0, plan.WallLossDb), lang).DistanceM;
            var one = WirelessCalculator.Range(InputFor(ap, null, 1, plan.WallLossDb), lang).DistanceM;
            res.Radius[ap] = (clear, one);
        }

        r.Heading("Покрытие беспроводной сети по плану", "Wireless coverage from the floor plan");
        r.Text($"Масштаб плана: {r.N(plan.PixelsPerMeter)} пикс/м, затухание в стене Zоф = {r.N(plan.WallLossDb)} дБ.",
               $"Plan scale: {r.N(plan.PixelsPerMeter)} px/m, wall loss Zof = {r.N(plan.WallLossDb)} dB.");

        foreach (var link in level.Links)
        {
            if (catalog.Standard(link.StandardId)?.Family != TechFamily.Wireless) continue;
            if (!byId.TryGetValue(link.AId, out var a) || !byId.TryGetValue(link.BId, out var b)) continue;
            var ap = a.Kind == DeviceKind.AccessPoint ? a : b.Kind == DeviceKind.AccessPoint ? b : null;
            if (ap == null) continue;
            var st = ap == a ? b : a;
            double dist = plan.DistanceM(ap.X, ap.Y, st.X, st.Y);
            int walls = plan.WallsBetween(ap.X, ap.Y, st.X, st.Y);
            var range = WirelessCalculator.Range(InputFor(ap, st, walls, plan.WallLossDb), lang).DistanceM;
            bool ok = dist <= range;
            res.Rows.Add(new CoverageRow(ap, st, link, dist, walls, range, ok));
        }

        foreach (var g in res.Rows.GroupBy(x => x.AccessPoint))
        {
            var ap = g.Key;
            var (clear, one) = res.Radius[ap];
            r.Heading($"Точка доступа «{ap.Name}» (канал {ap.Radio.Channel}, {r.N(FrequencyFor(ap.Radio.Channel))} МГц)",
                      $"Access point \"{ap.Name}\" (channel {ap.Radio.Channel}, {r.N(FrequencyFor(ap.Radio.Channel))} MHz)");
            r.Text($"Радиус без стен ≈ {r.N(clear, "0.#")} м, через одну стену ≈ {r.N(one, "0.#")} м. Станций: {g.Count()}.",
                   $"Range without walls ≈ {r.N(clear, "0.#")} m, through one wall ≈ {r.N(one, "0.#")} m. Stations: {g.Count()}.");
            var far = g.OrderByDescending(x => x.DistanceM).First();
            foreach (var row in g.OrderByDescending(x => x.DistanceM))
            {
                var line = r.T($"{row.Station.Name}: D = {r.N(row.DistanceM, "0.#")} м, стен q = {row.Walls}, допустимо {r.N(row.RangeM, "0.#")} м",
                               $"{row.Station.Name}: D = {r.N(row.DistanceM, "0.#")} m, walls q = {row.Walls}, allowed {r.N(row.RangeM, "0.#")} m");
                if (row.Ok) r.Good(line, line); else r.Fail(line, line);
            }
            r.Result(r.T($"Самый удалённый компьютер — {far.Station.Name} ({r.N(far.DistanceM, "0.#")} м).", $"Farthest computer: {far.Station.Name} ({r.N(far.DistanceM, "0.#")} m)."));
            if (g.Count() > 50)
                r.Warn("На одну точку больше 50 клиентов — рекомендуется 12…50.", "More than 50 clients per AP — 12…50 recommended.");
        }

        if (res.Rows.Count == 0)
            r.Warn("На уровне нет беспроводных линий между точкой доступа и станциями (спецификации 802.11).",
                   "No wireless links between access points and stations on this level (802.11 specifications).");
        else if (res.Rows.All(x => x.Ok))
            r.Good("Все станции в зоне уверенного приёма.", "All stations are within reliable range.");
        else
            r.Warn("Есть станции вне зоны приёма — передвиньте точку доступа, добавьте ещё одну или снизьте требуемую скорость.",
                   "Some stations are out of range — move the access point, add another, or lower the required rate.");
        return res;
    }

    /// <summary>Записать расстояния по плану в длины беспроводных линий.</summary>
    public static int ApplyLengths(CoverageResult result)
    {
        foreach (var row in result.Rows) row.Link.LengthM = Math.Round(row.DistanceM, 1);
        return result.Rows.Count;
    }
}
