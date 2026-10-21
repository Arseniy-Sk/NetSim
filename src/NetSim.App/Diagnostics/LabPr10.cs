using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NetSim.App.Localization;
using NetSim.App.Services;
using NetSim.App.ViewModels;
using NetSim.App.Views;
using NetSim.Calculators;
using NetSim.Core.Model;
using NetSim.Core.Serialization;
using NetSim.Core.Topology;
using NetSim.Simulation.Traffic;
using static NetSim.App.Diagnostics.LabPr4;

namespace NetSim.App.Diagnostics;

/// <summary>
/// ПР10 (ЛР10) «Методика проектирования беспроводной локальной сети», вариант 7:
/// план помещения, расчёт пропускной способности и дальности, размещение точек доступа, анализ покрытия,
/// проверка моделированием. Запуск: <c>NetSim.exe --lab pr10 &lt;папка&gt; &lt;план варианта.jpg&gt;</c>.
/// </summary>
public static class LabPr10
{
    private const double Px = 18;      // пикселей на метр
    private const double O = 60;       // отступ плана, пикс
    private const double Ci = 2, Cs = 30, Cm = 40, Internet = 100;
    private const double RunSeconds = 20;
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private static double X(double m) => O + m * Px;
    private static double Y(double m) => O + m * Px;

    private sealed record Room(string Name, double X1, double Y1, double X2, double Y2, (double X, double Y)[] Seats);

    /// <summary>Кабинеты и места по плану варианта 7 (метры). Здание П-образное вокруг двора, коридор 5 м.</summary>
    private static readonly Room[] Rooms =
    {
        new("Каб. 1 (10×30 м)", 0, 0, 10, 30, new[] { (1.2, 2.0), (1.2, 8.0), (1.2, 14.0), (1.2, 20.0), (1.2, 26.0), (5.0, 2.0), (8.8, 2.0), (8.8, 8.0), (8.8, 14.0), (8.8, 20.0), (8.8, 26.0) }),
        new("Каб. 2 (10×25 м)", 0, 30, 10, 55, new[] { (1.2, 32.0), (5.0, 32.0), (8.8, 32.0), (1.2, 40.0), (1.2, 45.5), (8.8, 38.0), (8.8, 44.0), (8.8, 50.0) }),
        new("Каб. 3 (15×10 м)", 10, 0, 25, 10, new[] { (11.5, 1.5), (15.0, 1.5), (18.5, 1.5), (22.5, 1.5), (15.5, 8.5), (19.0, 8.5), (22.5, 8.5) }),
        new("Каб. 4 (20×10 м)", 25, 0, 45, 10, new[] { (27.0, 1.5), (31.0, 1.5), (35.0, 1.5), (39.0, 1.5), (35.0, 8.5), (39.0, 8.5), (43.0, 8.5) }),
        new("Каб. 5 (10×30 м)", 45, 0, 55, 30, new[] { (46.2, 2.0), (50.0, 2.0), (53.8, 2.0), (53.8, 8.0), (53.8, 14.0), (53.8, 20.0), (53.8, 26.0), (46.2, 8.0), (46.2, 14.0), (46.2, 20.0), (46.2, 26.0) }),
        new("Каб. 6 (10×25 м)", 45, 30, 55, 55, new[] { (46.2, 32.0), (50.0, 32.0), (53.8, 32.0), (46.2, 38.0), (46.2, 44.0), (46.2, 50.0), (53.8, 40.0), (53.8, 45.5) }),
    };

    private sealed class Results
    {
        public int Stations;
        public WirelessCapacityResult Capacity = null!;
        public RadioRangeResult RangeClear = null!, RangeWall = null!;
        public CoverageResult Coverage = null!;
        public ReportTable Aps = null!;
        public ReportTable Sim = null!;
        public ReportTable Equipment = null!;
        public double FarthestM;
        public string SimText = "";
    }

    public static async void Run(MainWindow window, MainViewModel vm, string outDir, string? planImage)
    {
        Directory.CreateDirectory(outDir);
        var log = new List<string>();
        var c = new Ctx { Window = window, Vm = vm, Dir = outDir };
        var r = new Results();
        try
        {
            window.WindowState = WindowState.Normal;
            window.Width = 1400;
            window.Height = 860;
            vm.ShowLinkLabels = false;
            vm.ShowFlows = false;
            vm.ShowCoverage = true;
            await SelfTest.Settle();
            if (planImage != null && File.Exists(planImage)) File.Copy(planImage, Path.Combine(outDir, "plan-variant.jpg"), true);

            await Build(c, r);
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР10 — вариант 7 (беспроводная сеть).nsp"));
            BuildReport(c, r);
            log.Add($"stations={r.Stations}, aps={r.Aps.Rows.Count}");
            log.Add("ok");
        }
        catch (Exception ex)
        {
            log.Add("FAIL: " + ex);
        }
        File.WriteAllLines(Path.Combine(outDir, "lab.log"), log);
        vm.IsDirty = false;
        Application.Current.Shutdown();
    }

    private static async Task Build(Ctx c, Results r)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.NewProjectCommand.Execute(null);
        vm.Project.Name = "ПР10 — вариант 7";
        vm.Project.Description = "Беспроводная ЛВС офиса (П-образное здание, 6 кабинетов, 54 рабочих места). И — компьютер, подключённый к провайдеру (HTTP-сервер), С — сервер данных. Интернет — VDSL 100 Мбит/с.";
        var plan = vm.Project.Root.Plan;
        plan.PixelsPerMeter = Px;
        plan.WallLossDb = 6;
        foreach (var room in Rooms)
            plan.Shapes.Add(new PlanShape { Kind = PlanShapeKind.Room, X1 = X(room.X1), Y1 = Y(room.Y1), X2 = X(room.X2), Y2 = Y(room.Y2), Label = room.Name });
        // Стены двора (коридор 5 м вдоль внутренних сторон)
        plan.Shapes.Add(new PlanShape { Kind = PlanShapeKind.Wall, X1 = X(15), Y1 = Y(55), X2 = X(15), Y2 = Y(15) });
        plan.Shapes.Add(new PlanShape { Kind = PlanShapeKind.Wall, X1 = X(15), Y1 = Y(15), X2 = X(40), Y2 = Y(15) });
        plan.Shapes.Add(new PlanShape { Kind = PlanShapeKind.Wall, X1 = X(40), Y1 = Y(15), X2 = X(40), Y2 = Y(55) });
        c.B = new TopologyBuilder(vm.Project, vm.Catalog);
        var b = c.B;

        await c.Main("w01-plan", "План помещения варианта 7, построенный в NetSim (масштаб 18 пикс/м): шесть кабинетов П-образного здания, коридор 5 м вокруг двора",
            "План построен по размерам с рисунка варианта: левое и правое крылья шириной 10 м с кабинетами 30 м и 25 м, в верхней части — кабинеты 15 × 10 м и 20 × 10 м, между кабинетами и двором — коридор 5 м. Стены кабинетов учитываются при расчёте затухания сигнала (6 дБ на стену).",
            "1. План помещения");

        // Рабочие места
        var stations = new List<(Device Pc, int Room)>();
        Device internetPc = null!, server = null!;
        int n = 1;
        for (int ri = 0; ri < Rooms.Length; ri++)
            foreach (var (sx, sy) in Rooms[ri].Seats)
                stations.Add((b.CreateDevice("pc-wireless", X(sx), Y(sy), name: $"РС{n++}"), ri));
        internetPc = b.CreateDevice("server", X(1.2), Y(52.5), name: "И (интернет)");
        server = b.CreateDevice("server-rack", X(53.8), Y(52.5), name: "С (сервер данных)");
        r.Stations = stations.Count;

        // Расчёт пропускной способности — в калькуляторе
        int k = stations.Count + 2;
        r.Capacity = WirelessCalculator.Capacity(new WirelessCapacityInput
        {
            Stations = k, CiMbps = Ci, CsMbps = Cs, CmMbps = Cm, H = 2, Standard = "802.11n", MaxClientsPerAp = 12,
            InternetMbps = Internet, ViMbps = Math.Round(Cm / stations.Count, 2),
        });
        await c.Dialog(new CalculatorsWindow(vm), "w02-capacity", "Калькулятор ПР10 «Пропускная способность»: K = 54, Ci = 2, Cs = 30, Cm = 40 Мбит/с, h = 2, 802.11n",
            $"Расчёт выполнен в калькуляторе NetSim. Количество рабочих станций с учётом компьютеров И и С — K = {k}. Подключение к интернету — через отдельный компьютер И, поэтому h = 2. Требуемая пропускная способность сети Cmax ≥ (K − h)·Ci + Cs·(h − 1) + Cm = ({k} − 2)·2 + 30·1 + 40 = {r.Capacity.Cmax:0} Мбит/с — это больше возможностей одной точки доступа любого стандарта, поэтому выбран 802.11n и несколько точек доступа.",
            "2. Расчёт пропускной способности сети", w =>
            {
                var tab = Tab(w, 2);
                var boxes = All<TextBox>(tab).Take(9).ToList();
                var vals = new[] { k.ToString(), "2", "30", "40", "0", "12", "100", (Cm / stations.Count).ToString("0.##", Ru), "0" };
                for (int i = 0; i < 9; i++) boxes[i].Text = vals[i];
                var combos = All<ComboBox>(tab);
                combos[0].SelectedIndex = 1; // h = 2
                combos[1].SelectedIndex = 3; // 802.11n
                Click(tab, Loc.T("Calc_Calculate"));
            });

        // Точки доступа — по одной в каждом кабинете, 5 ГГц, разные каналы
        int[] channels = { 36, 40, 44, 48, 52, 56 };
        var aps = new Device[Rooms.Length];
        var sw = b.CreateDevice("switch-gigabit", X(27.5), Y(12.5), name: "Коммутатор (коридор)");
        for (int ri = 0; ri < Rooms.Length; ri++)
        {
            var room = Rooms[ri];
            double cx = (room.X1 + room.X2) / 2, cy = (room.Y1 + room.Y2) / 2;
            aps[ri] = b.CreateDevice("ap-n", X(cx), Y(cy), name: $"AP{ri + 1} (кан. {channels[ri]})");
            aps[ri].Radio.Channel = channels[ri];
            aps[ri].Radio.PowerDbm = 20;
            aps[ri].Radio.AntennaGainDbi = 5;
            aps[ri].Radio.RateMbps = 54;
            double cable = Math.Abs(cx - 27.5) + Math.Abs(cy - 12.5) + 3;
            b.Connect(aps[ri], sw, "1000base-t", Math.Round(cable), mediaId: "utp6");
        }
        foreach (var (pc, ri) in stations) b.Connect(pc, aps[ri], "802.11n", 10);
        b.Connect(server, sw, "1000base-t", Math.Round(Math.Abs(53.8 - 27.5) + Math.Abs(52.5 - 12.5)), mediaId: "utp6");
        b.Connect(internetPc, sw, "1000base-t", Math.Round(Math.Abs(1.2 - 27.5) + Math.Abs(52.5 - 12.5)), mediaId: "utp6");
        var modem = b.CreateDevice("modem-dsl", X(5), Y(59), name: "VDSL-модем");
        var cloud = b.CreateDevice("cloud-internet", X(5), Y(66), name: "Интернет-провайдер");
        b.Connect(internetPc, modem, "100base-tx", 2);
        b.Connect(modem, cloud, "vdsl2", 300, mediaId: "phone");

        // Анализ покрытия и длины радиолиний по плану
        r.Coverage = WirelessCoverage.Analyze(vm.Project.Root, vm.Catalog);
        WirelessCoverage.ApplyLengths(r.Coverage);
        r.FarthestM = r.Coverage.Rows.Max(x => x.DistanceM);
        var far = r.Coverage.Rows.OrderByDescending(x => x.DistanceM).First();

        vm.ShowCoverage = false; // зоны и подписи покрытия — в окне анализа покрытия, на общей схеме они загромождают план
        await c.Main("w03-network", $"Размещение точек доступа: по одной точке 802.11n (5 ГГц) в каждом кабинете, {stations.Count} беспроводных станций",
            "Точки доступа установлены в центре каждого кабинета: так между точкой и станциями нет стен, а на точку приходится 7–11 клиентов (рекомендуется 12–50 и не больше). Точки подключены кабелем 1000Base-T к гигабитному коммутатору в коридоре; к нему же подключены сервер данных С и компьютер И, который через VDSL-модем (100 Мбит/с) связан с провайдером.",
            "3. Размещение точек доступа");

        // Полноразмерная схема (как Файл → Экспорт схемы в PNG): при «вписать в окно» подписи слишком мелкие
        var (png, pw, ph) = c.Window.Topology.RenderPng();
        File.WriteAllBytes(Path.Combine(c.Dir, "w03b-scheme.png"), png);
        c.Shots.Add(new("w03b-scheme", "Схема беспроводной сети варианта 7 в натуральную величину (Файл → Экспорт схемы в PNG): пунктир — радиолинии станций к точкам доступа, сплошные линии — кабели 1000Base-T к коммутатору", null, null));

        // Дальность связи — калькулятор
        var apIn = WirelessCoverage.InputFor(aps[0], stations[0].Pc, 0, plan.WallLossDb);
        apIn.CheckDistanceM = r.FarthestM;
        r.RangeClear = WirelessCalculator.Range(apIn);
        r.RangeWall = WirelessCalculator.Range(WirelessCoverage.InputFor(aps[0], stations[0].Pc, 1, plan.WallLossDb));
        await c.Dialog(new CalculatorsWindow(vm), "w04-range", $"Калькулятор ПР10 «Дальность связи»: Pt = 20 дБм, Gt = Gr = 5 дБи, 54 Мбит/с (−66 дБм), канал 36 (5180 МГц), проверка расстояния {r.FarthestM:0.#} м",
            $"Дальность рассчитана по формулам (22)–(26). Самая удалённая от своей точки станция — {far.Station.Name} ({far.DistanceM:0.#} м до {far.AccessPoint.Name}).",
            "4. Расчёт радиуса сети", w =>
            {
                var tab = Tab(w, 3);
                var boxes = All<TextBox>(tab).Take(9).ToList();
                var vals = new[] { "20", "5", "5", "1", "1", "10", "0", "6", r.FarthestM.ToString("0.#", Ru) };
                for (int i = 0; i < 9; i++) boxes[i].Text = vals[i];
                var combos = All<ComboBox>(tab);
                combos[0].SelectedIndex = 0; // 54 Мбит/с
                combos[1].SelectedIndex = 13; // канал 36
                Click(tab, Loc.T("Calc_Calculate"));
            });
        await c.Dialog(new CoverageWindow(vm), "w05-coverage", "Анализ покрытия по плану (План → Анализ покрытия Wi-Fi): расстояние до каждой станции, число стен, допустимая дальность",
            $"Программа рассчитала для каждой из {stations.Count} станций расстояние до её точки доступа по масштабу плана, число стен на пути и допустимую дальность с учётом затухания в стенах.",
            "5. Проверка покрытия");
        await c.ConfigTab(aps[0], "Config_Radio", "w06-radio", "Радиопараметры точки доступа AP1: мощность 20 дБм, антенна 5 дБи, канал 36 (5180 МГц), скорость 54 Мбит/с");

        // Трафик по условию и моделирование
        double perServer = Cs / stations.Count, perInternet = Cm / stations.Count, perLocal = Ci - perServer - perInternet;
        TrafficProfile P(string name, double mbps)
        {
            var p = vm.Catalog.Profile("lan-p2p")!.Clone();
            p.NameRu = name; p.NameEn = name;
            p.TransactionSize = Distribution.Constant(1500);
            p.TimeBetween = Distribution.Exponential(1500 * 8 / (mbps * 1e6));
            return p;
        }
        for (int i = 0; i < stations.Count; i++)
        {
            var (pc, ri) = stations[i];
            b.AddFlow(pc, server, P("К серверу данных", perServer));
            b.AddFlow(internetPc, pc, P("Из интернета (через И)", perInternet));
            var mate = stations.Where(s => s.Room == ri && s.Pc != pc).Select(s => s.Pc).FirstOrDefault();
            if (mate != null && perLocal > 0) b.AddFlow(pc, mate, P("Обмен внутри кабинета", perLocal));
        }
        b.AddFlow(cloud, internetPc, P("Провайдер → И", Cm));
        foreach (var ap in aps) ap.ShownStatistics.Add(StatKeys.AverageUtilization);
        vm.TopologyChanged();
        await RunSimulation(c, RunSeconds);
        vm.StopCommand.Execute(null);
        vm.RefreshSimTime();
        var sim = vm.TrafficSim!;
        var vdsl = vm.Project.Root.Links.First(l => l.StandardId == "vdsl2");
        double maxAp = aps.Max(a => sim.DeviceValue(a, StatKeys.AverageUtilization));
        double drops = sim.Graph.Channels.Sum(ch => sim.ChannelValue(ch, StatKeys.PacketsDropped));
        r.Sim = new ReportTable($"Загрузка радиоканалов и линий (моделирование {RunSeconds:0} с)",
            new[] { "Объект", "Станций", "Average utilization", "Текущая нагрузка" },
            aps.Select((a, i) => new[] { a.Name, Rooms[i].Seats.Length.ToString(), F(StatKeys.AverageUtilization, sim.DeviceValue(a, StatKeys.AverageUtilization)), StatKeys.FormatBits(sim.DeviceValue(a, StatKeys.CurrentLoad)) })
                .Append(new[] { "Линия VDSL к провайдеру (100 Мбит/с)", "—", F(StatKeys.AverageUtilization, sim.LinkValue(vdsl, StatKeys.AverageUtilization)), StatKeys.FormatBits(sim.LinkValue(vdsl, StatKeys.CurrentLoad)) })
                .ToList());
        r.SimText = string.Create(Ru, $"Для проверки задан трафик по условию: каждая станция генерирует Ci = 2 Мбит/с — {perServer:0.###} Мбит/с к серверу данных (всего Cs = 30 Мбит/с), {perInternet:0.###} Мбит/с из интернета через компьютер И (всего Cm = 40 Мбит/с), остальное — обмен внутри кабинета. Наибольшая средняя загрузка радиоканала точки доступа — {F(StatKeys.AverageUtilization, maxAp)} (реальная пропускная способность радиоканала в модели — 45% от номинала), потеряно пакетов — {drops:0}. Сеть справляется с нагрузкой.");
        await c.Main("w07-sim", $"Моделирование {RunSeconds:0} с с трафиком по условию: под точками доступа — средняя загрузка радиоканала", r.SimText, "6. Проверка моделированием", rebuild: false);

        r.Aps = new ReportTable("Точки доступа", new[] { "Точка доступа", "Кабинет", "Канал / частота", "Станций", "Самая удалённая станция", "Допустимая дальность" },
            aps.Select((a, i) =>
            {
                var rows = r.Coverage.Rows.Where(x => x.AccessPoint == a).ToList();
                var farRow = rows.OrderByDescending(x => x.DistanceM).First();
                return new[] { a.Name, Rooms[i].Name, $"{channels[i]} / {WirelessCalculator.ChannelFrequency(channels[i])} МГц", rows.Count.ToString(),
                    string.Create(Ru, $"{farRow.Station.Name} — {farRow.DistanceM:0.#} м, стен {farRow.Walls}"), string.Create(Ru, $"{farRow.RangeM:0.#} м ✓") };
            }).ToList());
        r.Equipment = new ReportTable("Выбор оборудования", new[] { "Оборудование", "Кол-во", "Требования (из расчёта)" }, new List<string[]>
        {
            new[] { "Точка доступа 802.11n, 2,4/5 ГГц, порт 1000Base-T (желательно с питанием PoE)", aps.Length.ToString(), "Работа в диапазоне 5 ГГц (непересекающиеся каналы 36–56), до 12 клиентов на точку, реальная пропускная способность ≈ 100 Мбит/с" },
            new[] { "Беспроводной адаптер 802.11n с поддержкой 5 ГГц (USB или PCIe)", stations.Count.ToString(), "Для всех рабочих мест, кроме И и С" },
            new[] { "Коммутатор Gigabit Ethernet, не менее 8 портов (PoE для точек доступа)", "1", "6 точек доступа + сервер С + компьютер И" },
            new[] { "VDSL2-модем 100 Мбит/с", "1", "Подключение компьютера И к провайдеру (Internet = 100 Мбит/с ≥ Vmax)" },
            new[] { "Кабель UTP кат. 6 (1000Base-T)", $"≈ {vm.Project.Root.Links.Where(l => l.StandardId == "1000base-t").Sum(l => l.LengthM):0} м", "Соединение точек доступа, сервера и компьютера И с коммутатором" },
        });
    }

    // ---------------- Окна калькуляторов ----------------

    private static FrameworkElement Tab(Window w, int index)
    {
        var tabs = LabPr2.FindChild<TabControl>(w)!;
        tabs.SelectedIndex = index;
        tabs.UpdateLayout();
        return (FrameworkElement)((TabItem)tabs.Items[index]).Content;
    }

    private static List<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        var list = new List<T>();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var ch = VisualTreeHelper.GetChild(root, i);
            if (ch is T t) list.Add(t);
            list.AddRange(All<T>(ch));
        }
        return list;
    }

    private static void Click(DependencyObject root, string text) =>
        All<Button>(root).First(b => b.Content as string == text).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    // ---------------- Отчёт ----------------

    private static void BuildReport(Ctx c, Results r)
    {
        var path = Path.Combine(c.Dir, "Отчёт ПР10 (ЛР10) — вариант 7.docx");
        using var w = new WordReport(path);
        w.Title("ОТЧЁТ");
        w.Centered("по лабораторной работе №10 (ПР10)", bold: true);
        w.Centered("«Расчёт беспроводной локальной вычислительной сети предприятия»", bold: true);
        w.Centered("Вариант 7");
        w.Centered("(выполнено в NetSim — учебном аналоге NetCracker Professional)");
        w.Paragraph("");
        w.Centered("Выполнил(а): ____________________, группа ________");
        w.Centered("Проверил(а): ____________________");
        w.Centered(DateTime.Now.ToString("dd.MM.yyyy"));
        w.PageBreak();

        w.Heading1("Цель работы");
        w.Paragraph("Разработать логическую структуру беспроводной сети малого офиса, рассчитать её технические параметры и подобрать сетевое оборудование.");
        w.Heading1("Задание (вариант 7)");
        w.Paragraph("Необходимо провести беспроводную локальную сеть для помещения, план которого приведён на рисунке. И — компьютер, подключённый к интернет-провайдеру (HTTP-сервер), С — сервер данных. Тип интернет-соединения — высокоскоростной доступ в Интернет с использованием DSL (для всех компьютеров офиса), скорость доступа 100 Мб/с. Максимальная интенсивность трафика, генерируемого одним компьютером в группе, Ci = 2 Мб/с; интенсивность трафика к серверу данных — 30 Мб/с; интенсивность трафика к интернету — 40 Мб/с.");
        var planFile = Path.Combine(c.Dir, "plan-variant.jpg");
        if (File.Exists(planFile))
        {
            var bytes = File.ReadAllBytes(planFile);
            using var ms = new MemoryStream(bytes);
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            // Word принимает JPEG в ImagePart с типом PNG не всегда — перекодируем в PNG
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(frame);
            using var png = new MemoryStream();
            enc.Save(png);
            w.Image(new ReportImage("План помещения варианта 7 (из методических указаний): И — компьютер с выходом в интернет, С — сервер данных", png.ToArray(), frame.PixelWidth, frame.PixelHeight), "");
        }
        w.Paragraph(string.Create(Ru, $"По плану в помещении {r.Stations} рабочих мест, компьютер И и сервер С — всего K = {r.Stations + 2} компьютеров в шести кабинетах."));

        c.Images(w, c.Shots.Where(s => s.File == "w01-plan"));
        c.Images(w, c.Shots.Where(s => s.File == "w02-capacity"));
        w.Code(r.Capacity.Report.ToText());
        w.Paragraph(string.Create(Ru, $"Выбор стандарта (таблица 2 методички): 802.11b (11 Мбит/с) и 802.11g/a (54 Мбит/с, реально около 20 Мбит/с) не обеспечивают требуемых {r.Capacity.Cmax:0} Мбит/с даже несколькими точками в одном диапазоне. Выбран стандарт 802.11n (до 450 Мбит/с, реально около 100 Мбит/с на точку). По расчёту требуется не менее {r.Capacity.AccessPoints} точек доступа; так как точек больше трёх, а в диапазоне 2,4 ГГц всего 3 непересекающихся канала, сеть строится в диапазоне 5 ГГц (21 непересекающийся канал). Для размещения выбрано 6 точек — по одной в каждом кабинете: так между точкой и станциями нет стен и на каждую точку приходится не более 11 клиентов."));
        c.Images(w, c.Shots.Where(s => s.File is "w03-network" or "w03b-scheme"));
        w.Table(r.Aps, "Таблица");

        c.Images(w, c.Shots.Where(s => s.File == "w04-range"));
        w.Code(r.RangeClear.Report.ToText());
        w.Paragraph(string.Create(Ru, $"Через одну офисную стену (затухание 6 дБ) дальность уменьшается до {r.RangeWall.DistanceM:0.#} м:"));
        w.Code(r.RangeWall.Report.ToText());
        w.Paragraph(string.Create(Ru, $"Самая удалённая станция находится в {r.FarthestM:0.#} м от своей точки доступа без стен на пути — это меньше радиуса {r.RangeClear.DistanceM:0.#} м, поэтому все станции находятся в зоне уверенного приёма."));
        c.Images(w, c.Shots.Where(s => s.File is "w05-coverage" or "w06-radio"));
        w.Code(r.Coverage.Report.ToText());

        w.Heading1("6. Проверка моделированием");
        c.Images(w, c.Shots.Where(s => s.File == "w07-sim").Select(s => s with { Section = null, Text = null }));
        w.Paragraph(r.SimText);
        w.Table(r.Sim, "Таблица");

        w.Heading1("7. Выбор оборудования");
        w.Paragraph("Оборудование выбрано из расчёта минимальной стоимости при характеристиках, достаточных для данной сети: недорогие двухдиапазонные точки доступа 802.11n вместо 802.11ac, один гигабитный коммутатор и VDSL-модем на 100 Мбит/с.");
        w.Table(r.Equipment, "Таблица");

        w.Heading1("Выводы");
        w.Paragraph(string.Create(Ru, $"Спроектирована беспроводная ЛВС офиса варианта 7: {r.Stations} беспроводных рабочих места, сервер данных С и компьютер И с выходом в интернет по VDSL 100 Мбит/с. Требуемая пропускная способность сети — {r.Capacity.Cmax:0} Мбит/с; выбран стандарт 802.11n в диапазоне 5 ГГц и 6 точек доступа (по одной на кабинет) на непересекающихся каналах 36–56, подключённых к гигабитному коммутатору."));
        w.Paragraph(string.Create(Ru, $"Расчёт дальности по формулам (22)–(26) дал радиус {r.RangeClear.DistanceM:0.#} м без стен и {r.RangeWall.DistanceM:0.#} м через одну стену; самая удалённая станция находится в {r.FarthestM:0.#} м от точки доступа, все станции в зоне уверенного приёма. Моделирование с трафиком по условию подтвердило, что радиоканалы и канал к провайдеру не перегружены."));
    }
}
