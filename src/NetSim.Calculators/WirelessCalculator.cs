namespace NetSim.Calculators;

/// <summary>Исходные данные ПР10 — пропускная способность беспроводной сети.</summary>
public sealed class WirelessCapacityInput
{
    /// <summary>Число рабочих станций K.</summary>
    public int Stations { get; set; } = 20;

    /// <summary>Интенсивность трафика одной станции Ci, Мбит/с.</summary>
    public double CiMbps { get; set; } = 2;

    /// <summary>Интенсивность трафика к серверу данных Cs, Мбит/с.</summary>
    public double CsMbps { get; set; } = 6;

    /// <summary>Интенсивность трафика к интернет-серверу Cm, Мбит/с.</summary>
    public double CmMbps { get; set; } = 6;

    /// <summary>h = 2 — интернет через отдельный компьютер (сервер), h = 1 — через точку доступа (маршрутизатор).</summary>
    public int H { get; set; } = 1;

    /// <summary>Стандарт: 802.11a/b/g/n.</summary>
    public string Standard { get; set; } = "802.11g";

    /// <summary>Реальная пропускная способность одной точки доступа, Мбит/с (0 — по стандарту).</summary>
    public double ApThroughputMbps { get; set; }

    /// <summary>Максимум одновременно работающих клиентов на точку доступа (12..50).</summary>
    public int MaxClientsPerAp { get; set; } = 30;

    /// <summary>Скорость интернет-подключения, Мбит/с.</summary>
    public double InternetMbps { get; set; } = 100;

    /// <summary>Требуемая скорость доступа в интернет на одну станцию Vi, Мбит/с.</summary>
    public double ViMbps { get; set; } = 2;

    /// <summary>Число станций, имеющих доступ в интернет (0 — все).</summary>
    public int InternetStations { get; set; }
}

public sealed record WifiStandardInfo(string Id, string Name, double NominalMbps, double RealMbps, double RangeM, string Band, int NonOverlappingChannels);

public sealed class WirelessCapacityResult
{
    public double Cmax;
    public int AccessPoints;
    public double PerApThroughput;
    public double Vmax;
    public double VfactPerStation;
    public CalcReport Report = null!;
}

public sealed class RadioRangeInput
{
    public double PtDbm { get; set; } = 20;
    public double GtDbi { get; set; } = 5;
    public double GrDbi { get; set; } = 2;

    /// <summary>Чувствительность приёмника на выбранной скорости, дБм (отрицательное значение).</summary>
    public double PminDbm { get; set; } = -71;

    public double LtDb { get; set; } = 1;
    public double LrDb { get; set; } = 1;
    public double SomDb { get; set; } = 10;

    /// <summary>Центральная частота канала, МГц.</summary>
    public double FrequencyMhz { get; set; } = 2437;

    /// <summary>Число офисных стен на пути q.</summary>
    public int Walls { get; set; }

    /// <summary>Затухание в одной стене Zоф, дБ.</summary>
    public double WallLossDb { get; set; } = 6;

    /// <summary>Расстояние до самого удалённого компьютера, м (0 — не проверять).</summary>
    public double CheckDistanceM { get; set; }
}

public sealed class RadioRangeResult
{
    public double Y;
    public double Fsl;
    public double DistanceM;
    public bool? Reachable;
    public CalcReport Report = null!;
}

/// <summary>Расчёты ПР10 «Методика проектирования беспроводной локальной сети».</summary>
public static class WirelessCalculator
{
    public static readonly WifiStandardInfo[] Standards =
    {
        new("802.11a", "802.11a", 54, 20, 100, "5", 21),
        new("802.11b", "802.11b", 11, 5, 300, "2.4", 3),
        new("802.11g", "802.11g", 54, 20, 300, "2.4", 3),
        new("802.11n", "802.11n", 450, 100, 300, "2.4/5", 3),
    };

    /// <summary>Чувствительность приёмника по скорости (таблица 3 ПР10).</summary>
    public static readonly (double Mbps, double Dbm)[] Sensitivity =
    {
        (54, -66), (48, -71), (36, -76), (24, -80), (18, -83), (12, -85), (9, -86), (6, -87),
    };

    /// <summary>Центральная частота канала Wi-Fi, МГц: 2,4 ГГц — каналы 1–13 (14 — 2484), 5 ГГц — 5000 + 5·n.</summary>
    public static double ChannelFrequency(int channel) => channel switch
    {
        >= 1 and <= 13 => 2412 + 5 * (channel - 1),
        14 => 2484,
        >= 32 and <= 177 => 5000 + 5 * channel,
        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
    };

    public static WirelessCapacityResult Capacity(WirelessCapacityInput i, string lang = "ru")
    {
        var r = new CalcReport(lang);
        var res = new WirelessCapacityResult { Report = r };
        var std = Standards.FirstOrDefault(s => s.Id == i.Standard) ?? Standards[2];
        string mb = r.T("Мбит/с", "Mbit/s");

        r.Heading("Пропускная способность беспроводной сети", "Wireless network capacity");
        r.Formula($"Cmax = K · Ci = {i.Stations} · {r.N(i.CiMbps)} = {r.N(i.Stations * i.CiMbps)} {mb}  (15)");
        res.Cmax = (i.Stations - i.H) * i.CiMbps + i.CsMbps * (i.H - 1) + i.CmMbps;
        r.Formula($"Cmax ≥ (K − h)·Ci + Cs·(h − 1) + Cm = ({i.Stations} − {i.H})·{r.N(i.CiMbps)} + {r.N(i.CsMbps)}·({i.H} − 1) + {r.N(i.CmMbps)} = {r.N(res.Cmax)} {mb}  (16)");
        r.Text(i.H == 2 ? "h = 2: подключение к интернету через отдельный компьютер (сервер)." : "h = 1: подключение к интернету через точку доступа (маршрутизатор).",
               i.H == 2 ? "h = 2: Internet via a dedicated computer (server)." : "h = 1: Internet via the access point (router).");

        res.PerApThroughput = i.ApThroughputMbps > 0 ? i.ApThroughputMbps : std.RealMbps;
        int byCapacity = (int)Math.Ceiling(res.Cmax / res.PerApThroughput);
        int byClients = (int)Math.Ceiling(i.Stations / (double)Math.Max(1, i.MaxClientsPerAp));
        res.AccessPoints = Math.Max(1, Math.Max(byCapacity, byClients));

        r.Heading("Число точек доступа (17)–(19)", "Number of access points (17)–(19)");
        r.Text($"Стандарт {std.Name}: номинально {r.N(std.NominalMbps)} {mb}, реальная пропускная способность точки ≈ {r.N(res.PerApThroughput)} {mb}.",
               $"{std.Name}: nominal {r.N(std.NominalMbps)} {mb}, real AP throughput ≈ {r.N(res.PerApThroughput)} {mb}.");
        r.Formula($"n ≥ Cmax / Cn = {r.N(res.Cmax)} / {r.N(res.PerApThroughput)} → {byCapacity}");
        r.Formula($"n ≥ K / x = {i.Stations} / {i.MaxClientsPerAp} → {byClients}");
        r.Result(r.T($"Требуется точек доступа: n = {res.AccessPoints}", $"Access points required: n = {res.AccessPoints}"));

        int perAp = (int)Math.Ceiling(i.Stations / (double)res.AccessPoints);
        double cn = perAp * i.CiMbps + i.CsMbps + i.CmMbps;
        r.Formula($"Cn ≥ Σ Ci + Cs + Cm = {perAp}·{r.N(i.CiMbps)} + {r.N(i.CsMbps)} + {r.N(i.CmMbps)} = {r.N(cn)} {mb}");
        if (cn > res.PerApThroughput)
            r.Warn($"Нагрузка на одну точку ({r.N(cn)} {mb}) выше её реальной пропускной способности — увеличьте число точек или выберите стандарт быстрее.",
                   $"Per-AP load ({r.N(cn)} {mb}) exceeds its real throughput — add access points or pick a faster standard.");

        if (res.AccessPoints > 3 && std.Band.StartsWith("2.4"))
            r.Warn("В диапазоне 2,4 ГГц только 3 непересекающихся канала — больше трёх точек в одном месте дадут соканальную интерференцию. Используйте 5 ГГц (21 канал), например 802.11a/n.",
                   "2.4 GHz has only 3 non-overlapping channels — more than three APs in one place cause co-channel interference. Use 5 GHz (21 channels), e.g. 802.11a/n.");
        else r.Good("Количество точек доступа укладывается в число непересекающихся каналов диапазона.", "The number of APs fits the band's non-overlapping channels.");

        r.Heading("Скорость доступа в интернет (20), (21)", "Internet access speed (20), (21)");
        int k = i.InternetStations > 0 ? i.InternetStations : i.Stations;
        res.Vmax = k * i.ViMbps;
        res.VfactPerStation = i.InternetMbps / k;
        r.Formula($"Vmax = K · Vi = {k} · {r.N(i.ViMbps)} = {r.N(res.Vmax)} {mb}");
        r.Formula($"Vi.факт = Vинтернет / K = {r.N(i.InternetMbps)} / {k} = {r.N(res.VfactPerStation)} {mb}");
        if (i.InternetMbps >= res.Vmax) r.Good("Скорости интернет-канала достаточно.", "The Internet link speed is sufficient.");
        else r.Warn("Скорости интернет-канала недостаточно — фактическая скорость на станцию ниже требуемой.", "The Internet link is too slow — the actual per-station speed is below the requirement.");
        return res;
    }

    /// <summary>
    /// Дальность связи: Y = Pt + Gt + Gr − Pmin − Lt − Lr (Pmin — отрицательная чувствительность, т.е. её модуль прибавляется),
    /// FSL = Y − SOM, D = 10^(FSL/20 − 33/20 − lg F) — км (F в МГц). Стены: Pt = Pt − Zоф·q.
    /// </summary>
    public static RadioRangeResult Range(RadioRangeInput i, string lang = "ru")
    {
        var r = new CalcReport(lang);
        var res = new RadioRangeResult { Report = r };

        r.Heading("Энергетический бюджет радиолинии", "Radio link budget");
        double pt = i.PtDbm;
        if (i.Walls > 0)
        {
            pt = i.PtDbm - i.WallLossDb * i.Walls;
            r.Formula($"Pt = Pt − Zоф · q = {r.N(i.PtDbm)} − {r.N(i.WallLossDb)} · {i.Walls} = {r.N(pt)} {r.T("дБм", "dBm")}  (26)");
        }
        res.Y = pt + i.GtDbi + i.GrDbi - i.PminDbm - i.LtDb - i.LrDb;
        r.Formula($"Y = Pt + Gt + Gr + |Pmin| − Lt − Lr = {r.N(pt)} + {r.N(i.GtDbi)} + {r.N(i.GrDbi)} + {r.N(Math.Abs(i.PminDbm))} − {r.N(i.LtDb)} − {r.N(i.LrDb)} = {r.N(res.Y)} {r.T("дБ", "dB")}  (23)");
        res.Fsl = res.Y - i.SomDb;
        r.Formula($"FSL = Y − SOM = {r.N(res.Y)} − {r.N(i.SomDb)} = {r.N(res.Fsl)} {r.T("дБ", "dB")}  (24)");

        double dKm = Math.Pow(10, res.Fsl / 20 - 33.0 / 20 - Math.Log10(i.FrequencyMhz));
        res.DistanceM = dKm * 1000;
        r.Formula($"D = 10^(FSL/20 − 33/20 − lg F) = 10^({r.N(res.Fsl)}/20 − 1,65 − lg {r.N(i.FrequencyMhz)}) = {r.N(dKm, "0.####")} {r.T("км", "km")} = {r.N(res.DistanceM, "0.#")} {r.T("м", "m")}  (25)");
        r.Text("В формуле (22) FSL = 33 + 20(lg F + lg D) частота F — в МГц, расстояние D — в км.",
               "In (22) FSL = 33 + 20(lg F + lg D), F is in MHz and D in km.");

        if (i.CheckDistanceM > 0)
        {
            res.Reachable = i.CheckDistanceM <= res.DistanceM;
            if (res.Reachable == true)
                r.Good($"Самый удалённый компьютер ({r.N(i.CheckDistanceM)} м) в зоне уверенного приёма.",
                       $"The farthest computer ({r.N(i.CheckDistanceM)} m) is within reliable range.");
            else
                r.Fail($"Самый удалённый компьютер ({r.N(i.CheckDistanceM)} м) дальше радиуса действия — добавьте точку доступа или перенесите её.",
                       $"The farthest computer ({r.N(i.CheckDistanceM)} m) is out of range — add or move an access point.");
        }
        return res;
    }
}
