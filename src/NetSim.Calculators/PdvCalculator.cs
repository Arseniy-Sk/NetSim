namespace NetSim.Calculators;

/// <summary>Табличные значения IEEE 802.3 для расчёта PDV (таблица 1 ПР8), битовые интервалы.</summary>
public sealed record PdvSegmentType(string Id, string Name, double? LeftBase, double MiddleBase, double? RightBase, double DelayPerMeter, double MaxLengthM);

public sealed record PdvSegment(string TypeId, double LengthM);

public sealed class PdvResult
{
    public double Total;
    public double Limit;
    public bool Ok;
    public List<(string Position, PdvSegmentType Type, double Length, double Value)> Items { get; } = new();
    public CalcReport Report = null!;
}

/// <summary>
/// Расчёт времени двойного оборота сигнала (Path Delay Value) для сети Ethernet 10 Мбит/с:
/// PDV = Σ (база сегмента + L · задержка на 1 м), левый — передающий, правый — принимающий.
/// PDV не должно превышать 575 битовых интервалов (минимальный кадр 64 байта + преамбула 8 байт = 576 бит).
/// </summary>
public static class PdvCalculator
{
    public const double Limit10 = 575;

    public static readonly PdvSegmentType[] Types =
    {
        new("10base-5", "10Base-5", 11.8, 46.5, 169.5, 0.0866, 500),
        new("10base-2", "10Base-2", 11.8, 46.5, 169.5, 0.1026, 185),
        new("10base-t", "10Base-T", 15.3, 42.0, 165.0, 0.113, 100),
        new("10base-fb", "10Base-FB", null, 24.0, null, 0.1, 2000),
        new("10base-fl", "10Base-FL", 12.3, 33.5, 156.5, 0.1, 2000),
        new("foirl", "FOIRL", 7.8, 29.0, 152.0, 0.1, 1000),
    };

    public static PdvSegmentType Type(string id) => Types.First(t => t.Id == id);

    public static PdvResult Calculate(IReadOnlyList<PdvSegment> path, string lang = "ru")
    {
        var r = new CalcReport(lang);
        var res = new PdvResult { Report = r, Limit = Limit10 };
        if (path.Count < 2)
        {
            r.Fail("Нужно минимум два сегмента: левый (передающий) и правый (принимающий).",
                   "At least two segments are required: left (transmitting) and right (receiving).");
            return res;
        }

        r.Heading("Расчёт PDV (время двойного оборота)", "PDV (path delay value) calculation");
        for (int i = 0; i < path.Count; i++)
        {
            var seg = path[i];
            var t = Type(seg.TypeId);
            string pos;
            double? baseValue;
            if (i == 0) { pos = r.T("лев.сегм.", "left seg."); baseValue = t.LeftBase; }
            else if (i == path.Count - 1) { pos = r.T("пр.сегм.", "right seg."); baseValue = t.RightBase; }
            else { pos = r.T($"пром.сегм.{i}", $"mid seg.{i}"); baseValue = t.MiddleBase; }

            if (baseValue == null)
            {
                r.Fail($"Сегмент {t.Name} не может быть крайним (только промежуточным — магистраль между повторителями).",
                       $"Segment {t.Name} cannot be an end segment (only intermediate — repeater backbone).");
                baseValue = t.MiddleBase;
            }
            if (seg.LengthM > t.MaxLengthM)
                r.Fail($"Длина сегмента {t.Name} {r.N(seg.LengthM)} м больше допустимой {r.N(t.MaxLengthM)} м.",
                       $"Segment {t.Name} length {r.N(seg.LengthM)} m exceeds the maximum of {r.N(t.MaxLengthM)} m.");

            var value = baseValue.Value + seg.LengthM * t.DelayPerMeter;
            res.Items.Add((pos, t, seg.LengthM, value));
            r.Formula($"PDV {pos} ({t.Name}) = {r.N(baseValue.Value)} + {r.N(seg.LengthM)} · {r.N(t.DelayPerMeter, "0.####")} = {r.N(value, "0.##")} bt");
        }

        res.Total = res.Items.Sum(x => x.Value);
        r.Formula($"PDV = {string.Join(" + ", res.Items.Select(x => r.N(x.Value, "0.##")))} = {r.N(res.Total, "0.##")} bt");

        int repeaters = path.Count - 1;
        if (repeaters > 4)
            r.Fail($"Между станциями {repeaters} повторителей — больше 4 (правило 5-4-3).",
                   $"{repeaters} repeaters between stations — more than 4 (5-4-3 rule).");

        res.Ok = res.Total <= Limit10 && r.Ok;
        if (res.Total <= Limit10)
            r.Good($"PDV = {r.N(res.Total, "0.##")} ≤ 575 bt — условие корректности сегмента Ethernet выполняется (сеть проходит по критерию времени двойного оборота).",
                   $"PDV = {r.N(res.Total, "0.##")} ≤ 575 bt — the Ethernet segment passes the round-trip delay criterion.");
        else
            r.Fail($"PDV = {r.N(res.Total, "0.##")} > 575 bt — коллизии могут не распознаваться. Сократите длину кабеля или число повторителей, разделите домен коллизий коммутатором.",
                   $"PDV = {r.N(res.Total, "0.##")} > 575 bt — collisions may go undetected. Shorten cables, reduce repeaters, or split the domain with a switch.");
        return res;
    }

    /// <summary>
    /// Fast Ethernet (100 Мбит/с): PDV ≤ 512 bt. Задержки: пара DTE (TX/FX — 100, T4 — 138),
    /// повторитель класса I — 140, класса II — 92 (T4 — 67), кабель: кат.3 — 1,14, кат.5 — 1,112, оптика — 1,0 bt/м.
    /// </summary>
    public static CalcReport CalculateFastEthernet(string dteType, int classI, int classII, double copperM, bool cat5, double fiberM, string lang = "ru")
    {
        var r = new CalcReport(lang);
        double dte = dteType == "t4" ? 138 : 100;
        double cls2 = dteType == "t4" ? 67 : 92;
        double perM = cat5 ? 1.112 : 1.14;
        double total = dte + classI * 140 + classII * cls2 + copperM * perM + fiberM * 1.0;

        r.Heading("Расчёт PDV для Fast Ethernet (100 Мбит/с)", "Fast Ethernet PDV (100 Mbit/s)");
        r.Formula($"PDV = DTE({r.N(dte)}) + {classI}·140 + {classII}·{r.N(cls2)} + {r.N(copperM)}·{r.N(perM, "0.###")} + {r.N(fiberM)}·1,0 = {r.N(total, "0.##")} bt");
        if (classI > 1 || classI + classII > 2 || (classI == 1 && classII > 0))
            r.Fail("В одном домене коллизий допускается один повторитель I класса или два II класса.",
                   "One Class I repeater or two Class II repeaters are allowed per collision domain.");
        if (total <= 512) r.Good($"PDV ≤ 512 bt — домен коллизий корректен.", "PDV ≤ 512 bt — the collision domain is valid.");
        else r.Fail("PDV > 512 bt — домен коллизий слишком велик.", "PDV > 512 bt — the collision domain is too large.");
        return r;
    }
}
