namespace NetSim.Calculators;

/// <summary>Исходные данные ПР8 «Логическое проектирование локальной сети».</summary>
public sealed class LanLoadInput
{
    /// <summary>Число компьютеров в каждой группе (размер массива — число групп).</summary>
    public int[] GroupSizes { get; set; } = { 6, 6 };

    /// <summary>Средняя интенсивность трафика одного компьютера, доля от Cmax (K).</summary>
    public double K { get; set; } = 0.08;

    /// <summary>Максимальная пропускная способность базовой технологии, Мбит/с (10Base-T — 10).</summary>
    public double CmaxMbps { get; set; } = 10;

    /// <summary>Доля трафика между группами g, % от суммарного.</summary>
    public double InterGroupPercent { get; set; } = 50;

    /// <summary>Доля трафика к серверу s, % от суммарного.</summary>
    public double ServerPercent { get; set; } = 50;

    /// <summary>Допустимый коэффициент нагрузки домена коллизий Ethernet.</summary>
    public double MaxLoad { get; set; } = 0.35;

    /// <summary>Максимальное расстояние по кабелю между компьютерами в группе, м (L).</summary>
    public double DistanceInGroupM { get; set; } = 2;

    /// <summary>Максимальное расстояние по кабелю между группами, м (S).</summary>
    public double DistanceBetweenGroupsM { get; set; } = 350;
}

/// <summary>Рекомендованная спецификация для участка сети.</summary>
public sealed record SegmentRecommendation(string Segment, string StandardId, string StandardName, double CapacityMbps, double Load);

public sealed class LanLoadResult
{
    public double Ci;
    public double CSum;
    public double LoadUnstructured;
    public bool UnstructuredOk;
    public double[] GroupLoads = Array.Empty<double>();
    public double CInterGroup;
    public double CServer;
    public double InterGroupLoad;
    public double ServerLoad;
    public List<SegmentRecommendation> Recommendations { get; } = new();
    public CalcReport Report = null!;
}

/// <summary>
/// Расчёт ПР8 по формулам методички:
/// (5) Ci = K·Cmax; (6) CΣ = N·M·Ci; (7) ρн = CΣ / Cmax; (8) ρн ≤ ρEthernet = 0,35;
/// (9) Cмеж.гр = g·CΣ; (10) Cсерв = s·CΣ; (11) Cмеж.гр / Cmax ≤ 0,35.
/// </summary>
public static class LanLoadCalculator
{
    private static readonly (string Id, string Name, double Mbps, double MaxLenM, bool Fiber)[] EthernetLadder =
    {
        ("10base-t", "10Base-T", 10, 100, false),
        ("10base-fl", "10Base-FL", 10, 2000, true),
        ("100base-tx", "100Base-TX", 100, 100, false),
        ("100base-fx", "100Base-FX", 100, 2000, true),
        ("1000base-t", "1000Base-T", 1000, 100, false),
        ("1000base-lx", "1000Base-LX", 1000, 5000, true),
    };

    public static LanLoadResult Calculate(LanLoadInput input, string lang = "ru")
    {
        var r = new CalcReport(lang);
        var res = new LanLoadResult { Report = r };
        int groups = input.GroupSizes.Length;
        int total = input.GroupSizes.Sum();
        bool uniform = input.GroupSizes.Distinct().Count() == 1;
        double g = input.InterGroupPercent / 100, s = input.ServerPercent / 100;

        r.Heading("1) Трафик одного компьютера (5)", "1) Traffic of one computer (5)");
        res.Ci = input.K * input.CmaxMbps;
        r.Formula($"Ci = K · Cmax = {r.N(input.K)} · {r.N(input.CmaxMbps)} = {r.N(res.Ci)} {Mbit(r)}");

        r.Heading("2) Суммарный трафик неструктурированной сети (6)", "2) Total traffic of the flat network (6)");
        res.CSum = total * res.Ci;
        r.Formula(uniform
            ? $"CΣ = N · M · Ci = {groups} · {input.GroupSizes[0]} · {r.N(res.Ci)} = {r.N(res.CSum)} {Mbit(r)}"
            : $"CΣ = (Σ Ni) · Ci = ({string.Join(" + ", input.GroupSizes)}) · {r.N(res.Ci)} = {r.N(res.CSum)} {Mbit(r)}");

        r.Heading("3) Коэффициент нагрузки неструктурированной сети (7)", "3) Load factor of the flat network (7)");
        res.LoadUnstructured = res.CSum / input.CmaxMbps;
        r.Formula($"ρн = CΣ / Cmax = {r.N(res.CSum)} / {r.N(input.CmaxMbps)} = {r.N(res.LoadUnstructured)}");

        r.Heading("4) Проверка допустимой нагрузки домена коллизий (8)", "4) Collision domain load check (8)");
        res.UnstructuredOk = res.LoadUnstructured <= input.MaxLoad;
        r.Formula($"ρн(д.к.) = {r.N(res.LoadUnstructured)} {(res.UnstructuredOk ? "≤" : ">")} ρEthernet = {r.N(input.MaxLoad)}");
        if (res.UnstructuredOk)
            r.Good("Условие выполняется — реструктуризация не требуется.", "Condition holds — no restructuring needed.");
        else
        {
            r.Warn("Нагрузка превышена — требуется логическая реструктуризация: каждая группа выделяется в отдельный домен коллизий (группы соединяются коммутатором или мостом).",
                   "Load exceeded — logical restructuring needed: each group becomes its own collision domain (groups are joined by a switch or bridge).");
        }

        // Нагрузка доменов коллизий после разбиения на группы.
        r.Heading("Нагрузка логических сегментов (групп)", "Load of logical segments (groups)");
        res.GroupLoads = input.GroupSizes.Select(n => n * res.Ci / input.CmaxMbps).ToArray();
        for (int i = 0; i < groups; i++)
        {
            var load = res.GroupLoads[i];
            r.Formula($"ρгр{i + 1} = Ni · Ci / Cmax = {input.GroupSizes[i]} · {r.N(res.Ci)} / {r.N(input.CmaxMbps)} = {r.N(load)} {(load <= input.MaxLoad ? "≤" : ">")} {r.N(input.MaxLoad)}");
        }
        for (int i = 0; i < groups; i++)
        {
            var rec = Recommend(T(r, $"Группа {i + 1}", $"Group {i + 1}"), input.GroupSizes[i] * res.Ci, input.DistanceInGroupM, input.MaxLoad, input.CmaxMbps);
            res.Recommendations.Add(rec);
            if (res.GroupLoads[i] > input.MaxLoad)
                r.Warn($"Группа {i + 1}: нагрузка превышает допустимую при {r.N(input.CmaxMbps)} Мбит/с — нужна технология {rec.StandardName} или дальнейшее деление группы.",
                       $"Group {i + 1}: load exceeds the limit at {r.N(input.CmaxMbps)} Mbit/s — use {rec.StandardName} or split the group further.");
        }

        r.Heading("5) Межгрупповой трафик и трафик к серверу (9), (10)", "5) Inter-group and server traffic (9), (10)");
        res.CInterGroup = g * res.CSum;
        res.CServer = s * res.CSum;
        r.Formula($"Cмеж.гр. = g · CΣ = {r.N(g)} · {r.N(res.CSum)} = {r.N(res.CInterGroup)} {Mbit(r)}");
        r.Formula($"Cсерв = s · CΣ = {r.N(s)} · {r.N(res.CSum)} = {r.N(res.CServer)} {Mbit(r)}");

        r.Heading("Условие корректной работы (11)", "Correct operation condition (11)");
        res.InterGroupLoad = res.CInterGroup / input.CmaxMbps;
        res.ServerLoad = res.CServer / input.CmaxMbps;
        r.Formula($"Cмеж.гр.серв. = Cмеж.гр. / Cmax = {r.N(res.CInterGroup)} / {r.N(input.CmaxMbps)} = {r.N(res.InterGroupLoad)} {(res.InterGroupLoad <= input.MaxLoad ? "≤" : ">")} {r.N(input.MaxLoad)}");
        r.Formula($"Cсерв / Cmax = {r.N(res.CServer)} / {r.N(input.CmaxMbps)} = {r.N(res.ServerLoad)} {(res.ServerLoad <= input.MaxLoad ? "≤" : ">")} {r.N(input.MaxLoad)}");

        var backbone = Recommend(T(r, "Магистраль между группами", "Inter-group backbone"), res.CInterGroup, input.DistanceBetweenGroupsM, input.MaxLoad, input.CmaxMbps);
        var server = Recommend(T(r, "Линия к серверу", "Server link"), res.CServer, input.DistanceInGroupM, input.MaxLoad, input.CmaxMbps);
        res.Recommendations.Add(backbone);
        res.Recommendations.Add(server);

        if (res.InterGroupLoad > input.MaxLoad)
            r.Warn($"Межгрупповой канал перегружен на базовой технологии — нужна {backbone.StandardName} (ρ = {r.N(backbone.Load)}).",
                   $"Inter-group channel overloaded on the base technology — use {backbone.StandardName} (ρ = {r.N(backbone.Load)}).");
        else r.Good("Межгрупповой канал работает в допустимом режиме.", "Inter-group channel load is acceptable.");

        if (input.DistanceBetweenGroupsM > 100)
            r.Warn($"Расстояние между группами S = {r.N(input.DistanceBetweenGroupsM)} м превышает 100 м для витой пары — нужен оптоволоконный кабель ({backbone.StandardName}).",
                   $"Inter-group distance S = {r.N(input.DistanceBetweenGroupsM)} m exceeds 100 m for twisted pair — use fiber ({backbone.StandardName}).");

        r.Heading("Рекомендуемые спецификации", "Recommended specifications");
        foreach (var rec in res.Recommendations)
            r.Result($"{rec.Segment}: {rec.StandardName} ({r.N(rec.CapacityMbps)} {Mbit(r)}), ρ = {r.N(rec.Load)}");

        r.Ok = res.UnstructuredOk || res.GroupLoads.All(l => l <= input.MaxLoad);
        return res;
    }

    private static string T(CalcReport r, string ru, string en) => r.T(ru, en);
    private static string Mbit(CalcReport r) => r.T("Мбит/с", "Mbit/s");

    /// <summary>Минимальная спецификация Ethernet, при которой нагрузка не превышает допустимую и хватает длины кабеля.</summary>
    public static SegmentRecommendation Recommend(string segment, double trafficMbps, double lengthM, double maxLoad, double baseMbps)
    {
        foreach (var e in EthernetLadder)
        {
            if (e.Mbps < baseMbps) continue;
            if (lengthM > e.MaxLenM) continue;
            var load = trafficMbps / e.Mbps;
            if (load <= maxLoad) return new SegmentRecommendation(segment, e.Id, e.Name, e.Mbps, load);
        }
        var last = EthernetLadder[^1];
        return new SegmentRecommendation(segment, last.Id, last.Name, last.Mbps, trafficMbps / last.Mbps);
    }
}
