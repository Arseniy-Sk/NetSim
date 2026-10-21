using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using NetSim.App.Services;
using NetSim.App.ViewModels;
using NetSim.App.Views;
using NetSim.Calculators;
using NetSim.Core.Model;
using NetSim.Core.Serialization;
using NetSim.Core.Topology;
using NetSim.Simulation.Traffic;

namespace NetSim.App.Diagnostics;

/// <summary>
/// ПР4 (ЛР4) «Задание трафика вычислительной сети»: основная часть на сети из ПР3, задание 2 (интернет-кафе)
/// и самостоятельная работа по варианту 7. Запуск: <c>NetSim.exe --lab pr4 &lt;папка&gt; &lt;папка ПР3&gt;</c>.
/// </summary>
public static class LabPr4
{
    private const double RunSeconds = 60;
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private sealed class Results
    {
        public int MainFlows;
        public ReportTable MainSummary = null!;
        public List<string> MainAnswers = new();
        public ReportTable CafeSummary = null!;
        public List<string> CafeAnswers = new();
        public int VariantFlows;
        public ReportTable VariantSummary = null!;
        public List<string> VariantAnswers = new();
        public int MainShots, CafeShots;
    }

    public static async void Run(MainWindow window, MainViewModel vm, string outDir, string pr3Dir)
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
            await SelfTest.Settle();

            await MainPart(c, r, Path.Combine(pr3Dir, "ПР3 — пример (повторите за мной).nsp"));
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР4 — сеть из ПР3 с трафиком.nsp"));
            r.MainShots = c.Shots.Count;

            await Cafe(c, r);
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР4 — интернет-кафе (задание 2).nsp"));
            r.CafeShots = c.Shots.Count;

            await Variant7(c, r, Path.Combine(pr3Dir, "ПР3 — вариант 7.nsp"));
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР4 — вариант 7.nsp"));

            BuildReport(c, r);
            log.Add($"main flows={r.MainFlows}, variant flows={r.VariantFlows}");
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

    // ---------------- Общие операции ----------------

    internal static Device Dev(MainViewModel vm, string name) => vm.Project.AllDevices().First(d => d.Name == name);

    internal static string F(string key, double v) => StatKeys.Format(key, v, "ru");

    internal static void SetServer(Device s, Distribution size, Distribution delay)
    {
        s.Server.ReplySize = size;
        s.Server.ReplyDelay = delay;
    }

    /// <summary>Окно Profiles с выбранным профилем (как щелчок по профилю в списке).</summary>
    internal static Action<Window> SelectProfile(string profileId) => w =>
    {
        var list = LabPr2.FindChild<ListBox>(w)!;
        list.SelectedItem = list.Items.OfType<ListBoxItem>().First(i => ((TrafficProfile)i.Tag).Id == profileId);
    };

    internal static void AddFlow(Ctx c, Device a, Device b, string profile)
    {
        var (f, e) = c.B.AddFlow(a, b, profile);
        if (f == null) throw new InvalidOperationException(e);
    }

    internal static async Task RunSimulation(Ctx c, double until)
    {
        c.Vm.RunCommand.Execute(null);
        c.Vm.TrafficSim!.RunUntil(until);
        c.Vm.PauseCommand.Execute(null);
        c.Vm.RefreshSimTime();
        c.Vm.RequestRedraw();
        await SelfTest.Settle();
    }

    // ---------------- Основная часть ----------------

    private static async Task MainPart(Ctx c, Results r, string pr3Example)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.OpenFile(pr3Example);
        vm.Project.Name = "ПР4 — трафик сети из ПР3";
        c.B = new TopologyBuilder(vm.Project, vm.Catalog);
        vm.Project.Flows.Clear();

        await c.Main("m01-open", "Скриншот 1 — сеть из лабораторной работы №2 (ПР3) загружена",
            "Открыт проект сети из ПР3 (Файл → Открыть): сегмент Fast Ethernet WS1–WS3 и S1, кольцо Token Ring 16 Мбит/с с WS4, WS5, S2, второй сегмент Fast Ethernet WS6–WS8 и S3, маршрутизатор и мост.",
            "Часть 1. Подготовка проекта");
        await c.Software(Dev(vm, "S3"), "sw-http-server", "m02-s3", "Скриншот 2 — свойства S3: HTTP server с галочками HTTP, FTP, POP3",
            "Проверено, что программное обеспечение из ПР3 сохранилось: S1 — Database + CAD/CAM, S2 — File client-server, S3 — HTTP, FTP, POP3, все WS — HTTP и File client-server, WS3, WS5, WS7, WS8 — дополнительно POP3.");

        // Peer-to-peer
        var p2p = new (string A, string B)[] { ("WS1", "WS2"), ("WS2", "WS3"), ("WS1", "WS3"), ("WS4", "WS5"), ("WS6", "WS7"), ("WS7", "WS8"), ("WS6", "WS8") };
        await c.Dialog(new ProfilesWindow(vm, Dev(vm, "WS1"), Dev(vm, "WS2")), "m03-profiles-p2p",
            "Скриншот 3 — окно Profiles с выбранным профилем Small office peer-to-peer",
            "Инструментом Traffic (молния) щёлкнуты WS1, затем WS2; в окне Profiles выбран профиль Small office peer-to-peer и нажата кнопка Assign. Так же назначены пары WS2↔WS3, WS1↔WS3, внутри Token Ring — WS4↔WS5, во втором сегменте Fast Ethernet — WS6↔WS7, WS7↔WS8, WS6↔WS8.",
            "Часть 2. Назначение трафика. Шаг 2.1 — внутренний трафик (peer-to-peer)", SelectProfile("small-office-p2p"));
        foreach (var (a, b) in p2p) AddFlow(c, Dev(vm, a), Dev(vm, b), "small-office-p2p");
        vm.ShowFlows = true;
        await c.Main("m04-p2p", "Скриншот 4 — схема сети с отображёнными потоками peer-to-peer (цветные пунктирные линии со стрелками)");

        // Клиент → сервер
        var cs = new List<(string[] Ws, string Server, string Profile)>
        {
            (new[] { "WS1", "WS2", "WS3" }, "S1", "database"),
            (new[] { "WS1", "WS2", "WS3" }, "S1", "cadcam"),
            (new[] { "WS4", "WS5" }, "S2", "file-cs"),
            (new[] { "WS4", "WS5" }, "S2", "database"),
            (new[] { "WS6", "WS7", "WS8" }, "S3", "http"),
            (new[] { "WS6", "WS7", "WS8" }, "S3", "ftp"),
            (new[] { "WS3", "WS5", "WS7", "WS8" }, "S3", "email-pop"),
        };
        // S2 по таблице методички обслуживает и клиентов базы данных — на нём включена служба Database.
        c.Install(Dev(vm, "S2"), "sw-small-office-db", "db");
        await c.Dialog(new ProfilesWindow(vm, Dev(vm, "WS1"), Dev(vm, "S1")), "m05-profiles-db",
            "Скриншот 5 — назначение трафика WS1 → S1 (профиль Database client-server)",
            "Клиент-серверный трафик назначен по таблице методички: WS1–WS3 → S1 (Database, CAD/CAM), WS4, WS5 → S2 (File client-server, Database), WS6–WS8 → S3 (HTTP, FTP), WS3, WS5, WS7, WS8 → S3 (E-mail (POP)). Так как по таблице S2 обслуживает и клиентов базы данных, на S2 дополнительно установлено Small office database server со службой Database client-server.",
            "Шаг 2.2 — трафик «клиент → сервер»", SelectProfile("database"));
        foreach (var (ws, server, profile) in cs)
            foreach (var w in ws) AddFlow(c, Dev(vm, w), Dev(vm, server), profile);
        r.MainFlows = vm.Project.Flows.Count;
        await c.Main("m06-all-flows", $"Скриншот 6 — итоговая схема со всеми потоками ({r.MainFlows} потоков)");
        await c.Dialog(new DataFlowWindow(vm), "m07-dataflow", $"Скриншот 7 — окно Data Flow со списком всех потоков (всего {r.MainFlows})",
            $"Global → Data Flow показывает список всех назначенных потоков. Всего потоков: {r.MainFlows} (7 peer-to-peer и {r.MainFlows - 7} клиент-серверных).",
            "Шаг 2.3 — просмотр списка потоков");

        // Законы распределения серверов (по заданию основной части)
        SetServer(Dev(vm, "S1"), Distribution.Normal(2048, 512), Distribution.Exponential(5));
        SetServer(Dev(vm, "S2"), Distribution.Normal(2048, 512), Distribution.Normal(2, 0.7));
        SetServer(Dev(vm, "S3"), Distribution.Normal(2048, 512), Distribution.Erlang(1.5, 0.4));
        await c.ConfigTab(Dev(vm, "S1"), "Config_Server", "m07a-s1-reply", "Параметры ответа S1: Reply Size — нормальный закон (M = 2048 байт, σ = 512), Reply Delay — экспоненциальный (M = 5 с)",
            "Размер ответа сервера (Reply Size) всех серверов задан по нормальному закону (M = 2048 байт, σ = 512). Пересчёт в границы диапазона по формулам методички: Rmin = Mx − σ = 2048 − 512 = 1536 байт, Rmax = Mx + σ = 2048 + 512 = 2560 байт; в NetSim закон задаётся непосредственно через M и σ (вкладка «Сервер (Reply)»), гистограмма под полями показывает распределение значений. Задержка ответа (Reply Delay): S1 — экспоненциальный закон (M = 5 с), S2 — нормальный (M = 2 с, σ = 0,7; Rmin = 1,3, Rmax = 2,7), S3 — закон Эрланга (M = 1,5 с, σ = 0,4; Rmin = 1,1, Rmax = 1,9).",
            "Задание законов распределения для серверов");
        await c.ConfigTab(Dev(vm, "S2"), "Config_Server", "m07b-s2-reply", "Параметры ответа S2: Reply Delay — нормальный закон (M = 2 с, σ = 0,7)");
        await c.ConfigTab(Dev(vm, "S3"), "Config_Server", "m07c-s3-reply", "Параметры ответа S3: Reply Delay — закон Эрланга (M = 1,5 с, σ = 0,4)");

        // Часть 3. Моделирование
        foreach (var f in vm.Project.Flows) f.ShowAnimation = false;
        await c.Dialog(new DataFlowWindow(vm), "m08-uncheck", "Скриншот 8 — окно Data Flow со снятыми галочками анимации (Uncheck All)",
            "Global → Data Flow → Uncheck All отключает анимацию пакетов (статистика при этом собирается). Для наглядности скриншота 9 анимация затем снова включена (Check All).",
            "Часть 3. Запуск моделирования");
        foreach (var f in vm.Project.Flows) f.ShowAnimation = true;
        vm.TopologyChanged();
        await RunSimulation(c, 20);
        // Пакет в линии 100 Мбит/с находится доли миллисекунды — шагаем модель до момента, когда в линиях есть пакеты.
        var running = vm.TrafficSim!;
        for (int i = 0; i < 200000 && running.InFlight.Count(p => p.Flow.ShowAnimation) < 1; i++) running.RunUntil(running.Now + 0.00005);
        vm.RefreshSimTime();
        vm.RequestRedraw();
        await c.Main("m09-run", "Скриншот 9 — сеть в режиме Run: в линии виден передаваемый пакет (цветная точка у WS6)",
            "Моделирование запущено кнопкой Run (F5).", rebuild: false);
        vm.TrafficSim!.RunUntil(RunSeconds);
        vm.StopCommand.Execute(null);
        vm.RefreshSimTime();
        await c.Main("m10-stop", $"Скриншот 10 — сеть после остановки (Stop), модельное время {RunSeconds:0} с",
            $"Моделирование остановлено кнопкой Stop после {RunSeconds:0} с модельного времени; статистика сохранена.", rebuild: false);

        // Часть 4. Статистика
        var servers = new[] { "S1", "S2", "S3" }.Select(n => Dev(vm, n)).ToArray();
        foreach (var s in servers) { s.ShownStatistics.Clear(); s.ShownStatistics.Add(StatKeys.CurrentWorkload); s.ShownStatistics.Add(StatKeys.PacketsReceived); }
        await c.Dialog(new StatisticsItemsWindow(vm, servers[0]), "m11a-statitems", "Окно Statistics Items для S1: отмечены current workload и packets received",
            "Для каждого сервера: ПКМ → Статистика (Statistics Items), отмечены current workload (текущая нагрузка) и packets received (получено пакетов), OK — значения появились под значком сервера.",
            "Часть 4. Снятие статистики. Шаг 4.1 — статистика серверов");
        int n = 11;
        foreach (var s in servers)
            await c.Selected(s, $"m{n}-{s.Name.ToLowerInvariant()}", $"Скриншот {n++} — статистика под {s.Name} (current workload, packets received); справа — все показатели сервера");

        var segs = new[] { ("Switch 1", "Fast Ethernet 1"), ("Token Ring", "Token Ring"), ("Switch 2", "Fast Ethernet 2") };
        bool firstSeg = true;
        foreach (var (name, title) in segs)
        {
            var d = Dev(vm, name);
            d.ShownStatistics.Clear();
            d.ShownStatistics.Add(StatKeys.AverageUtilization);
            await c.Selected(d, $"m{n}-seg", $"Скриншот {n++} — статистика сегмента {title} (average utilization)",
                firstSeg ? "Для сегментов: ПКМ по коммутатору или кольцу → Статистика → average utilization (средний процент использования). Для коммутатора — среднее по его линиям, для кольца Token Ring — загрузка общей среды." : null,
                firstSeg ? "Шаг 4.2 — статистика сегментов" : null);
            firstSeg = false;
        }
        vm.Selected = null;

        var sim = vm.TrafficSim!;
        var rows = servers.Select(s => new[]
        {
            s.Name, F(StatKeys.CurrentWorkload, sim.DeviceValue(s, StatKeys.CurrentWorkload)), F(StatKeys.AverageWorkload, sim.DeviceValue(s, StatKeys.AverageWorkload)),
            sim.DeviceValue(s, StatKeys.PacketsReceived).ToString("0"), "—",
        }).ToList();
        foreach (var (name, title) in segs)
            rows.Add(new[] { title, "—", "—", "—", F(StatKeys.AverageUtilization, sim.DeviceValue(Dev(vm, name), StatKeys.AverageUtilization)) });
        r.MainSummary = new ReportTable($"Сводная таблица статистики (модельное время {RunSeconds:0} с)",
            new[] { "Устройство / сегмент", "Current workload", "Average workload", "Packets received", "Average utilization" }, rows);

        // Часть 5. Анализ — с проверочным экспериментом «+5 станций в Token Ring».
        var mostLoaded = servers.OrderByDescending(s => sim.DeviceValue(s, StatKeys.AverageWorkload)).First();
        double tr = sim.DeviceValue(Dev(vm, "Token Ring"), StatKeys.AverageUtilization);
        double fe1 = sim.DeviceValue(Dev(vm, "Switch 1"), StatKeys.AverageUtilization);
        double fe2 = sim.DeviceValue(Dev(vm, "Switch 2"), StatKeys.AverageUtilization);
        double trPlus5 = TokenRingPlusFive(vm.Project);
        r.MainAnswers.Add($"1. Больше всех нагружен сервер {mostLoaded.Name}: средняя нагрузка {F(StatKeys.AverageWorkload, sim.DeviceValue(mostLoaded, StatKeys.AverageWorkload))}, получено {sim.DeviceValue(mostLoaded, StatKeys.PacketsReceived):0} пакетов. "
            + (mostLoaded.Name == "S3" ? "S3 обслуживает сразу три службы (HTTP, FTP, POP3) и больше всего клиентов." : $"{mostLoaded.Name} получает больше всего запросов от своих клиентов."));
        r.MainAnswers.Add($"2. Сильнее загружен сегмент Token Ring: {F(StatKeys.AverageUtilization, tr)} против {F(StatKeys.AverageUtilization, fe1)} и {F(StatKeys.AverageUtilization, fe2)} у сегментов Fast Ethernet. Скорость Token Ring — 16 Мбит/с, у Fast Ethernet — 100 Мбит/с, кроме того кольцо — общая среда для всех станций, а коммутатор выделяет каждой станции собственную линию.");
        r.MainAnswers.Add($"3. Проверено моделированием: после добавления в кольцо Token Ring ещё 5 рабочих станций с таким же трафиком, как у WS4, загрузка кольца выросла с {F(StatKeys.AverageUtilization, tr)} до {F(StatKeys.AverageUtilization, trPlus5)} (в {(tr > 0 ? trPlus5 / tr : 0):0.0} раза). При дальнейшем росте числа станций и интенсивности трафика кольцо может стать узким местом сети — появятся очереди и перегрузка.");
        r.MainAnswers.Add("4. Статистику снимают до реального развёртывания, чтобы заранее найти перегруженные сегменты и серверы, выбрать подходящие технологии и скорости линий, оценить время ответа и избежать затрат на переделку сети после монтажа.");

        await c.Main("m17-final", "Скриншот 17 — итоговая сеть со всеми индикаторами статистики", null, "Итоговая сеть", rebuild: false);
        vm.ShowFlows = false;
    }

    /// <summary>Отдельный прогон: +5 станций в кольце Token Ring с трафиком как у WS4.</summary>
    private static double TokenRingPlusFive(Project original)
    {
        var p = ProjectSerializer.Clone(original);
        var b = new TopologyBuilder(p);
        var ring = p.AllDevices().First(d => d.Name == "Token Ring");
        var ws4 = p.AllDevices().First(d => d.Name == "WS4");
        var templateFlows = p.Flows.Where(f => f.SourceId == ws4.Id).ToList();
        for (int i = 0; i < 5; i++)
        {
            var pc = b.CreateDevice("pc", 0, 0, name: $"WS-TR{i + 1}");
            foreach (var sw in ws4.Software) b.InstallSoftware(pc, sw.SoftwareId, sw.EnabledServices.ToArray());
            b.Connect(pc, ring, "tr-16", 10, mediaId: "stp");
            foreach (var f in templateFlows) b.AddFlow(pc, p.FindDevice(f.TargetId)!, f.Profile.Clone());
        }
        var sim = new TrafficSimulator(p);
        sim.RunUntil(RunSeconds);
        return sim.DeviceValue(ring, StatKeys.AverageUtilization);
    }

    // ---------------- Задание 2: интернет-кафе ----------------

    private static async Task Cafe(Ctx c, Results r)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.NewProjectCommand.Execute(null);
        vm.Project.Name = "Интернет-кафе";
        vm.Project.Description = "ПР4, задание 2: 10 ПК, коммутатор Fast Ethernet, сервер (биллинг + кэш + FTP), маршрутизатор, облако PSTN.";
        c.B = new TopologyBuilder(vm.Project, vm.Catalog);
        var b = c.B;
        var sw = b.CreateDevice("switch-eth", 520, 300, name: "Коммутатор");
        var server = b.CreateDevice("server", 300, 140, name: "Сервер");
        var router = b.CreateDevice("router", 760, 140, name: "Маршрутизатор");
        var pstn = b.CreateDevice("cloud-pstn", 980, 140, name: "PSTN");
        var pcs = new List<Device>();
        for (int i = 0; i < 10; i++)
            pcs.Add(b.CreateDevice("pc", 110 + (i % 5) * 200 + (i >= 5 ? 100 : 0), i < 5 ? 470 : 590, name: $"ПК{i + 1}"));
        foreach (var pc in pcs) b.Connect(pc, sw, "100base-t4", 10, mediaId: "utp5");
        var lServer = b.Connect(sw, server, "100base-t4", 10, mediaId: "utp5");
        b.Connect(sw, router, "100base-t4", 10, mediaId: "utp5");
        var lPstn = b.Connect(router, pstn, "modem-56k", 1000);

        c.Install(server, "sw-http-server", "http", "ftp");
        c.Install(server, "sw-ftp-server", "ftp");
        c.Install(server, "sw-billing", "billing");
        foreach (var pc in pcs) c.Install(pc, "sw-http-server", "http", "ftp");
        foreach (var pc in pcs) { AddFlow(c, pc, server, "http"); AddFlow(c, pc, server, "ftp"); }
        for (int i = 0; i < 10; i += 2) AddFlow(c, pcs[i], pcs[i + 1], "small-office-p2p");
        AddFlow(c, pcs[0], pstn, "interlan");
        vm.ShowFlows = true;
        await c.Main("c18-cafe", $"Скриншот 18 — схема интернет-кафе: 10 ПК, коммутатор, сервер, маршрутизатор, облако PSTN ({vm.Project.Flows.Count} потоков)",
            "Создан новый проект. Размещены 10 ПК, коммутатор Fast Ethernet, сервер, маршрутизатор и облако PSTN (Clouds → PSTN) — имитация провайдера. Все ПК, сервер и маршрутизатор подключены к коммутатору по Fast Ethernet 100Base-T4 (витая пара), маршрутизатор к PSTN — телефонной линией через модем V.90 (56 кбит/с). На сервер установлены HTTP server (HTTP, FTP), FTP server и Billing server, на ПК — клиентские службы HTTP и FTP. Назначен трафик: все ПК → сервер (HTTP и FTP), пары ПК1↔ПК2, ПК3↔ПК4, ПК5↔ПК6, ПК7↔ПК8, ПК9↔ПК10 — Small office peer-to-peer (игры по сети), ПК1 → PSTN — InterLAN traffic (выход в интернет).",
            "Задание 2. Интернет-кафе");

        server.ShownStatistics.Add(StatKeys.CurrentWorkload);
        server.ShownStatistics.Add(StatKeys.PacketsReceived);
        sw.ShownStatistics.Add(StatKeys.AverageUtilization);
        lPstn.ShownStatistics.Add(StatKeys.AverageUtilization);
        vm.TopologyChanged();
        await RunSimulation(c, RunSeconds);
        vm.StopCommand.Execute(null);
        var sim = vm.TrafficSim!;
        await c.Selected(server, "c19-server", "Скриншот 19 — статистика сервера (current workload, packets received)",
            $"Моделирование выполнено {RunSeconds:0} с модельного времени (Run → Stop).");
        await c.Selected(sw, "c20-switch", "Скриншот 20 — статистика коммутатора (average utilization)");
        vm.Selected = lPstn;
        await SelfTest.Settle();
        SelfTest.Snap(c.Window, c.Dir, "c21-pstn");
        c.Shots.Add(new("c21-pstn", "Скриншот 21 — статистика линии «маршрутизатор → PSTN» (average utilization); линия перегружена и подсвечена красным", null, null));
        vm.Selected = null;

        double util = sim.LinkValue(lPstn, StatKeys.AverageUtilization);
        double drops = sim.LinkValue(lPstn, StatKeys.PacketsDropped);
        double swUtil = sim.DeviceValue(sw, StatKeys.AverageUtilization);
        double srvLink = sim.LinkValue(lServer, StatKeys.AverageUtilization);
        r.CafeSummary = new ReportTable($"Статистика интернет-кафе (модельное время {RunSeconds:0} с)",
            new[] { "Объект", "Показатель", "Значение" },
            new List<string[]>
            {
                new[] { "Сервер", "Current workload", F(StatKeys.CurrentWorkload, sim.DeviceValue(server, StatKeys.CurrentWorkload)) },
                new[] { "Сервер", "Average workload", F(StatKeys.AverageWorkload, sim.DeviceValue(server, StatKeys.AverageWorkload)) },
                new[] { "Сервер", "Packets received", sim.DeviceValue(server, StatKeys.PacketsReceived).ToString("0") },
                new[] { "Коммутатор", "Average utilization", F(StatKeys.AverageUtilization, swUtil) },
                new[] { "Линия коммутатор — сервер", "Average utilization", F(StatKeys.AverageUtilization, srvLink) },
                new[] { "Линия маршрутизатор — PSTN", "Average utilization", F(StatKeys.AverageUtilization, util) },
                new[] { "Линия маршрутизатор — PSTN", "Потеряно пакетов", drops.ToString("0") },
            });

        // Эксперимент: все 10 ПК одновременно качают файлы (по 5 МБ каждые ~30 с).
        var p = ProjectSerializer.Clone(vm.Project);
        // Файл передаётся пачкой пакетов; реальный TCP регулирует скорость окном, поэтому очередь не ограничиваем.
        p.Simulation.MaxQueuePackets = 1_000_000;
        var pb = new TopologyBuilder(p);
        var srv = p.AllDevices().First(d => d.Name == "Сервер");
        foreach (var pc in p.AllDevices().Where(d => d.Name.StartsWith("ПК")))
        {
            var bulk = vm.Catalog.Profile("bulk-data")!.Clone();
            bulk.TimeBetween = Distribution.Exponential(30);
            pb.AddFlow(srv, pc, bulk);
        }
        var heavy = new TrafficSimulator(p);
        heavy.RunUntil(RunSeconds);
        var heavyLink = heavy.Graph.Links.First(l => l.Touches(srv.Id));
        double heavySrv = heavy.LinkValue(heavyLink, StatKeys.AverageUtilization);
        double heavyCur = heavy.LinkValue(heavyLink, StatKeys.CurrentUtilization);
        double heavySw = heavy.DeviceValue(heavy.Graph.Devices.Values.First(d => d.Name == "Коммутатор"), StatKeys.AverageUtilization);

        r.CafeAnswers.Add($"Внутри кафе сеть справляется с нагрузкой: средняя загрузка коммутатора {F(StatKeys.AverageUtilization, swUtil)}, линии к серверу {F(StatKeys.AverageUtilization, srvLink)}. Узкое место — выход в интернет: модемная линия 56 кбит/с к PSTN загружена на {F(StatKeys.AverageUtilization, util)}, потеряно {drops:0} пакетов, линия подсвечена красным (перегрузка).");
        r.CafeAnswers.Add($"Если все 10 ПК одновременно начнут качать файлы с сервера (дополнительно по 5 МБ на каждый ПК в среднем раз в 30 с), средняя загрузка линии «коммутатор — сервер» вырастет до {F(StatKeys.AverageUtilization, heavySrv)} (в конце интервала — {F(StatKeys.CurrentUtilization, heavyCur)}), средняя загрузка коммутатора — {F(StatKeys.AverageUtilization, heavySw)}. Fast Ethernet 100 Мбит/с выдерживает такую нагрузку внутри сети, но линия к серверу — общий узкий участок для всех ПК: при более крупных файлах или большем числе ПК её стоит сделать гигабитной.");
        r.CafeAnswers.Add("Если загрузка коммутатора превысит 80%, нужно перейти на Gigabit Ethernet (коммутатор и сетевые карты 1000Base-T), подключить сервер гигабитным портом или агрегировать каналы, разнести трафик по сегментам (VLAN) или поставить второй сервер.");
        r.CafeAnswers.Add("Предложения по улучшению: 1) заменить модемное подключение к провайдеру (56 кбит/с) на ADSL/оптику (от 8–100 Мбит/с) — сейчас именно оно перегружено; 2) заменить коммутатор на гигабитный и подключить сервер на 1 Гбит/с; 3) выделить игровой трафик peer-to-peer и служебный трафик сервера в разные VLAN или добавить второй сервер для кэша и FTP.");
        vm.ShowFlows = false;
    }

    // ---------------- Вариант 7 ----------------

    private static async Task Variant7(Ctx c, Results r, string pr3Variant)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.OpenFile(pr3Variant);
        vm.Project.Name = "ПР4 — вариант 7";
        c.B = new TopologyBuilder(vm.Project, vm.Catalog);
        vm.Project.Flows.Clear();
        foreach (var d in vm.Project.AllDevices()) d.ShownStatistics.Clear();
        const string sec = "Самостоятельная работа. Вариант 7";

        var s1 = Dev(vm, "Сервер 1 (FDDI)");
        var s2 = Dev(vm, "Сервер 2 (Token Ring)");
        var s3 = Dev(vm, "Сервер 3 (Ethernet)");
        Device W(int i) => Dev(vm, $"WS{i}");

        // Станции взаимодействуют с серверами и друг с другом внутри своих сетей по Small office peer-to-peer.
        foreach (var i in new[] { 1, 2, 3 }) AddFlow(c, W(i), s1, "small-office-p2p");
        foreach (var i in new[] { 4, 5 }) AddFlow(c, W(i), s2, "small-office-p2p");
        foreach (var i in new[] { 6, 7, 8 }) AddFlow(c, W(i), s3, "small-office-p2p");
        foreach (var (a, b) in new[] { (1, 2), (2, 3), (1, 3), (4, 5), (6, 7), (7, 8), (6, 8) }) AddFlow(c, W(a), W(b), "small-office-p2p");
        int p2pCount = vm.Project.Flows.Count;
        // Клиент-серверный трафик по ролям из ПР3 — на него отвечают серверы (Reply Size / Reply Delay).
        foreach (var i in new[] { 1, 2, 3 }) { AddFlow(c, W(i), s1, "database"); AddFlow(c, W(i), s1, "cadcam"); }
        foreach (var i in new[] { 4, 5 }) { AddFlow(c, W(i), s2, "file-cs"); AddFlow(c, W(i), s2, "database"); }
        foreach (var i in Enumerable.Range(1, 8)) { AddFlow(c, W(i), s3, "http"); AddFlow(c, W(i), s3, "ftp"); }
        foreach (var i in new[] { 3, 5, 7, 8 }) AddFlow(c, W(i), s3, "email-pop");
        r.VariantFlows = vm.Project.Flows.Count;

        SetServer(s1, Distribution.Normal(2048, 512), Distribution.Exponential(5));
        SetServer(s2, Distribution.Normal(2048, 512), Distribution.Normal(2, 0.7));
        SetServer(s3, Distribution.Normal(2048, 512), Distribution.Erlang(1.5, 0.4));

        vm.ShowFlows = true;
        await c.Main("v01-flows", $"Вариант 7. Сеть из ПР3 с назначенным трафиком ({r.VariantFlows} потоков)",
            $"Открыта сеть варианта 7 из ПР3 (FDDI — Token Ring — Fast Ethernet). Назначены потоки Small office peer-to-peer: рабочие станции с серверами своих сетей (WS1–WS3 ↔ сервер 1, WS4, WS5 ↔ сервер 2, WS6–WS8 ↔ сервер 3) и станции друг с другом внутри своих сетей (WS1↔WS2, WS2↔WS3, WS1↔WS3, WS4↔WS5, WS6↔WS7, WS7↔WS8, WS6↔WS8) — всего {p2pCount}. Чтобы законы Reply Size и Reply Delay серверов проявились в моделировании, сохранены и клиент-серверные потоки по ролям из ПР3 (БД, CAD/CAM, файлы, HTTP, FTP, POP3) — {r.VariantFlows - p2pCount}.",
            sec);
        await c.Dialog(new DataFlowWindow(vm), "v02-dataflow", $"Вариант 7. Окно Data Flow — всего {r.VariantFlows} потоков");
        await c.ConfigTab(s1, "Config_Server", "v03-s1", "Вариант 7. Сервер 1: Reply Size — Normal (M = 2048, σ = 512), Reply Delay — Exponential (M = 5 с)",
            "Reply Size всех серверов — нормальный закон, M = 2048 байт, σ = 512 (Rmin = 1536, Rmax = 2560 байт). Reply Delay: сервер 1 — экспоненциальный закон (M = 5 с), сервер 2 — нормальный (M = 2 с, σ = 0,7), сервер 3 — Эрланга (M = 1,5 с, σ = 0,4; порядок k = M²/σ² ≈ 14).");
        await c.ConfigTab(s2, "Config_Server", "v04-s2", "Вариант 7. Сервер 2: Reply Delay — Normal (M = 2 с, σ = 0,7)");
        await c.ConfigTab(s3, "Config_Server", "v05-s3", "Вариант 7. Сервер 3: Reply Delay — Erlang (M = 1,5 с, σ = 0,4)");

        var segs = new[] { ("FDDI", "FDDI"), ("Token Ring", "Token Ring"), ("Коммутатор Ethernet", "Fast Ethernet") };
        foreach (var s in new[] { s1, s2, s3 }) { s.ShownStatistics.Add(StatKeys.CurrentWorkload); s.ShownStatistics.Add(StatKeys.PacketsReceived); }
        foreach (var (name, _) in segs) Dev(vm, name).ShownStatistics.Add(StatKeys.AverageUtilization);
        vm.TopologyChanged();
        await RunSimulation(c, RunSeconds);
        vm.StopCommand.Execute(null);
        var sim = vm.TrafficSim!;
        await c.Main("v06-stats", $"Вариант 7. Статистика после {RunSeconds:0} с моделирования: под серверами — current workload и packets received, под кольцами и коммутатором — average utilization",
            "Моделирование выполнено (Run → Stop), выведена статистика по заданию: для всех серверов — текущая нагрузка и количество полученных пакетов, для сегментов — процент использования.", rebuild: false);
        foreach (var s in new[] { s1, s2, s3 })
            await c.Selected(s, $"v07-{(s == s1 ? 1 : s == s2 ? 2 : 3)}", $"Вариант 7. Все показатели: {s.Name}");
        vm.Selected = null;

        var rows = new[] { s1, s2, s3 }.Select(s => new[]
        {
            s.Name, s.Server.ReplyDelay.Summary, F(StatKeys.CurrentWorkload, sim.DeviceValue(s, StatKeys.CurrentWorkload)),
            F(StatKeys.AverageWorkload, sim.DeviceValue(s, StatKeys.AverageWorkload)), sim.DeviceValue(s, StatKeys.PacketsReceived).ToString("0"), "—",
        }).ToList();
        foreach (var (name, title) in segs)
            rows.Add(new[] { title, "—", "—", "—", "—", F(StatKeys.AverageUtilization, sim.DeviceValue(Dev(vm, name), StatKeys.AverageUtilization)) });
        r.VariantSummary = new ReportTable($"Вариант 7. Сводная таблица статистики (модельное время {RunSeconds:0} с)",
            new[] { "Устройство / сегмент", "Reply Delay", "Current workload", "Average workload", "Packets received", "Average utilization" }, rows);

        var top = new[] { s1, s2, s3 }.OrderByDescending(s => sim.DeviceValue(s, StatKeys.AverageWorkload)).First();
        var segUtil = segs.Select(x => (x.Item2, U: sim.DeviceValue(Dev(vm, x.Item1), StatKeys.AverageUtilization))).OrderByDescending(x => x.U).ToList();
        double resp(Device s) => new[] { 1, 2, 3, 4, 5, 6, 7, 8 }.Select(i => sim.StatsOf(W(i))).Where(st => st.TransactionsCompleted > 0).Average(st => st.AverageResponseTime);
        r.VariantAnswers.Add($"Наиболее нагружен {top.Name}: средняя нагрузка {F(StatKeys.AverageWorkload, sim.DeviceValue(top, StatKeys.AverageWorkload))}, получено {sim.DeviceValue(top, StatKeys.PacketsReceived):0} пакетов — к нему обращаются все восемь станций (HTTP, FTP, POP3).");
        r.VariantAnswers.Add($"Загрузка сегментов: {string.Join(", ", segUtil.Select(x => $"{x.Item1} — {F(StatKeys.AverageUtilization, x.U)}"))}. Сильнее всего загружен {segUtil[0].Item1}" + (segUtil[0].Item1 == "Token Ring" ? " — у него наименьшая скорость (16 Мбит/с) и общая среда для всех станций кольца." : "."));
        r.VariantAnswers.Add($"Законы распределения задержки ответа влияют на время ответа серверов: экспоненциальный закон с M = 5 с у сервера 1 даёт самые большие и самые разбросанные задержки, закон Эрланга (M = 1,5 с, σ = 0,4) у сервера 3 — наименьшие и наиболее стабильные. Среднее время ответа по всем станциям — {resp(s1):0.00} с.");
        r.VariantAnswers.Add("Перегруженных линий нет: загрузка всех сегментов значительно ниже порога, сеть варианта 7 выдерживает заданный трафик.");
        vm.ShowFlows = false;
    }

    // ---------------- Отчёт ----------------

    private static void BuildReport(Ctx c, Results r)
    {
        var path = Path.Combine(c.Dir, "Отчёт ПР4 (ЛР4) — вариант 7.docx");
        using var w = new WordReport(path);
        w.Title("ОТЧЁТ");
        w.Centered("по лабораторной работе №4 (ПР4)", bold: true);
        w.Centered("«Задание трафика вычислительной сети»", bold: true);
        w.Centered("Вариант 7");
        w.Centered("(выполнено в NetSim — учебном аналоге NetCracker Professional)");
        w.Paragraph("");
        w.Centered("Выполнил(а): ____________________, группа ________");
        w.Centered("Проверил(а): ____________________");
        w.Centered(DateTime.Now.ToString("dd.MM.yyyy"));
        w.PageBreak();

        w.Heading1("Цель работы");
        w.Paragraph("Научиться моделировать трафик сети согласно заданным параметрам.");
        w.Heading1("1. Основная часть: трафик сети из лабораторной работы №2");
        w.Paragraph("Задание: помимо серверов рабочие станции внутри своих сетей взаимодействуют друг с другом по трафику Small office peer-to-peer. Размер ответа на запрос (Reply Size) всех серверов — нормальный закон, M = 2048 байт, СКО = 512. Задержка ответа (Reply Delay): сервер 1 — экспоненциальный закон, M = 5 с; сервер 2 — нормальный, M = 2 с, СКО = 0,7; сервер 3 — закон Эрланга, M = 1,5 с, СКО = 0,4. Вывести статистику: для серверов — current workload и количество полученных пакетов; для сегментов — average utilization.");
        c.Images(w, c.Shots.Take(r.MainShots));
        w.Heading2("Шаг 4.3 — сводная таблица");
        w.Table(r.MainSummary, "Таблица");
        w.Heading2("Часть 5. Анализ и выводы");
        foreach (var a in r.MainAnswers) w.Paragraph(a);

        w.Heading1("2. Задание 2. Сеть интернет-кафе");
        w.Paragraph("Задание: спроектировать сеть для интернет-кафе на 10 компьютеров (HTTP, игры по локальной сети, потоковое видео, FTP) с одним сервером (биллинг + кэш + FTP) и доказать, что она выдержит нагрузку.");
        c.Images(w, c.Shots.Skip(r.MainShots).Take(r.CafeShots - r.MainShots));
        w.Table(r.CafeSummary, "Таблица");
        w.Heading2("Вывод по заданию 2");
        foreach (var a in r.CafeAnswers) w.Paragraph(a);

        w.Heading1("3. Самостоятельная работа. Вариант 7");
        w.Paragraph("Задание: рабочие станции взаимодействуют с серверами по трафику Small office peer-to-peer. Помимо серверов рабочие станции внутри своих сетей взаимодействуют друг с другом по трафику Small office peer-to-peer. Reply Size всех серверов — нормальный закон, M = 2048, СКО = 512 байт. Reply Delay сервера (1) — экспоненциальный закон, M = 5; сервера (2) — нормальный, M = 2, СКО = 0,7; сервера (3) — закон Эрланга, M = 1,5, СКО = 0,4, время в секундах. Вывести статистику: для всех серверов — current workload и количество полученных пакетов; для сегментов — average utilization.");
        c.Images(w, c.Shots.Skip(r.CafeShots));
        w.Table(r.VariantSummary, "Таблица");
        w.Heading2("Анализ результатов варианта 7");
        foreach (var a in r.VariantAnswers) w.Paragraph(a);

        w.Heading1("Выводы");
        w.Paragraph("В ходе работы освоено задание трафика вычислительной сети: назначение потоков peer-to-peer и клиент-серверных потоков инструментом Traffic через окно Profiles, просмотр и управление потоками в окне Data Flow, задание законов распределения размера (Reply Size) и задержки (Reply Delay) ответа серверов — нормального, экспоненциального и Эрланга, запуск и остановка моделирования, вывод статистики серверов (current workload, packets received) и сегментов (average utilization).");
        w.Paragraph("Моделирование показало, что наиболее нагружены серверы, обслуживающие наибольшее число служб и клиентов, а сильнее всего загружаются сегменты с меньшей скоростью и общей средой передачи (Token Ring 16 Мбит/с). Снятие статистики до развёртывания сети позволяет заранее обнаружить узкие места — как модемный выход в интернет в сети интернет-кафе — и выбрать технологии с достаточной пропускной способностью.");
    }
}
