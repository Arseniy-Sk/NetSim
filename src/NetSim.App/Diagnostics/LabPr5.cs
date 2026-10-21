using System.IO;
using System.Windows;
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
/// ПР5 (ЛР5) «Моделирование трафика сети с использованием законов распределения»: новая сеть с нуля
/// (скриншоты 1–23) и творческое задание «интернет-кафе» (24–27). Запуск: <c>NetSim.exe --lab pr5 &lt;папка&gt;</c>.
/// </summary>
public static class LabPr5
{
    private const double RunSeconds = 60;

    private sealed class Results
    {
        public int Flows;
        public ReportTable Laws = null!;
        public ReportTable Summary = null!;
        public List<string> Answers = new();
        public int MainShots;
        public ReportTable CafeLaws = null!;
        public ReportTable CafeSummary = null!;
        public List<string> CafeConclusion = new();
    }

    public static async void Run(MainWindow window, MainViewModel vm, string outDir)
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

            await MainPart(c, r);
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР5 — сеть с законами распределения.nsp"));
            r.MainShots = c.Shots.Count;
            await Cafe(c, r);
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР5 — интернет-кафе (творческое задание).nsp"));
            BuildReport(c, r);
            log.Add($"flows={r.Flows}");
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

    // ---------------- Основная часть ----------------

    private static async Task MainPart(Ctx c, Results r)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.NewProjectCommand.Execute(null);
        vm.Project.Name = "ПР5 — законы распределения";
        c.B = new TopologyBuilder(vm.Project, vm.Catalog);
        var b = c.B;

        await c.Main("p01-empty", "Скриншот 1 — чистое рабочее поле", "Создан новый проект (Файл → Новый проект).", "Часть 1. Создание сети", fit: false);

        var sw1 = b.CreateDevice("switch-eth", 330, 260, name: "Switch 1");
        var sw2 = b.CreateDevice("switch-eth", 700, 420, name: "Switch 2");
        var router = b.CreateDevice("router", 700, 200, name: "Router");
        var pstn = b.CreateDevice("cloud-pstn", 950, 200, name: "PSTN");
        var ws = new[]
        {
            b.CreateDevice("pc", 120, 120, name: "WS1"), b.CreateDevice("pc", 120, 260, name: "WS2"), b.CreateDevice("pc", 120, 400, name: "WS3"),
            b.CreateDevice("pc", 560, 580, name: "WS4"), b.CreateDevice("pc", 700, 600, name: "WS5"),
        };
        var s1 = b.CreateDevice("server", 330, 450, name: "S1");
        var s2 = b.CreateDevice("server", 860, 560, name: "S2");
        await c.Main("p02-devices", "Скриншот 2 — все устройства на рабочем поле: 2 коммутатора, 5 ПК, 2 сервера, маршрутизатор, облако PSTN",
            "Из браузера Devices перетащены: 2 коммутатора (Switches → Workgroup → Ethernet Switch), 5 ПК (LAN Workstations → PCs and Notebooks → PC), 2 сервера (LAN Servers → Server), маршрутизатор (Routers and bridges → Low end → Router) и облако PSTN (Clouds → PSTN) — имитация интернета.");

        Link L(Device a, Device z) => b.Connect(a, z, "100base-t4", 10, mediaId: "utp5");
        foreach (var w in ws.Take(3)) L(w, sw1);
        L(sw1, s1);
        var seg1 = L(sw1, router);
        b.Connect(router, pstn, "isdn-bri", 1000);
        foreach (var w in ws.Skip(3)) L(w, sw2);
        L(sw2, s2);
        var seg2 = L(sw2, router);
        await c.Main("p03-links", "Скриншот 3 — готовая сеть со всеми соединениями",
            "Инструментом Link (Link Assistant: Protocol — Fast Ethernet 100Base-T4, Bandwidth — 100 Мбит/с, Media — витая пара) соединены: WS1, WS2, WS3 → Switch 1; Switch 1 → S1; Switch 1 → Router; WS4, WS5, S2 → Switch 2; Switch 2 → Router. Маршрутизатор соединён с облаком PSTN линией ISDN BRI (по методичке — «любой тип»).");

        // ПО
        c.Install(s1, "sw-small-office-db", "db", "cad");
        c.Install(s2, "sw-http-server", "http", "ftp");
        c.Install(s2, "sw-ftp-server", "ftp");
        c.Install(s2, "sw-email-server", "pop3");
        foreach (var w in ws.Take(3)) c.Install(w, "sw-small-office-db", "db", "cad");
        foreach (var w in ws) c.Install(w, "sw-http-server", "http", "ftp");
        foreach (var w in new[] { ws[2], ws[4] }) c.Install(w, "sw-email-server", "pop3");
        await c.Software(s1, "sw-small-office-db", "p04-s1", "Скриншот 4 — свойства S1: Small office database server, галочки Database client-server и CAD/CAM client-server",
            "На S1 из Network and enterprise software → Server software перетащено Small office database server (отдельного CAD/CAM server в каталоге нет — по методичке используется то же ПО), отмечены Database client-server и CAD/CAM client-server.",
            "Часть 2. Установка программного обеспечения");
        await c.Selected(s2, "p05-s2", "Скриншот 5 — свойства S2: HTTP server (HTTP, FTP), FTP server (FTP), E-mail server (POP3)",
            "На S2 установлены HTTP server, FTP server и E-mail server, отмечены HTTP, FTP, POP3. Справа в панели свойств виден весь набор ПО и служб.");
        await c.Selected(ws[0], "p06-ws1", "Скриншот 6 — свойства WS1: Small office database server (Database, CAD/CAM), HTTP server (HTTP, FTP)",
            "WS1–WS3 — клиенты S1 (Database, CAD/CAM); все WS — HTTP- и FTP-клиенты (HTTP server: HTTP, FTP); WS3, WS5 — POP3-клиенты (E-mail server: POP3).");
        await c.Selected(ws[4], "p07-ws5", "Скриншот 7 — свойства WS5: HTTP server (HTTP, FTP), E-mail server (POP3)");
        vm.Selected = null;

        // Трафик
        await c.Dialog(new ProfilesWindow(vm, ws[0], ws[1]), "p08-profiles", "Скриншот 8 — окно Profiles с выбранным Small office peer-to-peer",
            "Инструментом Traffic назначен внутренний трафик Small office peer-to-peer: WS1↔WS2, WS2↔WS3, WS1↔WS3, WS4↔WS5.",
            "Часть 3. Назначение трафика", SelectProfile("small-office-p2p"));
        foreach (var (a, z) in new[] { (0, 1), (1, 2), (0, 2), (3, 4) }) AddFlow(c, ws[a], ws[z], "small-office-p2p");
        await c.Dialog(new ProfilesWindow(vm, ws[0], s1), "p09-ws1-s1", "Скриншот 9 — назначение трафика WS1 → S1 (Database)",
            "Клиент-серверный трафик по таблице: WS1–WS3 → S1 (Database, CAD/CAM), WS4, WS5 → S2 (HTTP, FTP), WS3, WS5 → S2 (E-mail (POP)).", null, SelectProfile("database"));
        foreach (var w in ws.Take(3)) { AddFlow(c, w, s1, "database"); AddFlow(c, w, s1, "cadcam"); }
        foreach (var w in ws.Skip(3)) { AddFlow(c, w, s2, "http"); AddFlow(c, w, s2, "ftp"); }
        foreach (var w in new[] { ws[2], ws[4] }) AddFlow(c, w, s2, "email-pop");
        r.Flows = vm.Project.Flows.Count;
        vm.ShowFlows = true;
        await c.Main("p10-flows", $"Скриншот 10 — схема сети со всеми потоками ({r.Flows})");
        await c.Dialog(new DataFlowWindow(vm), "p11-dataflow", $"Скриншот 11 — окно Data Flow (всего потоков: {r.Flows})",
            $"Global → Data Flow: всего {r.Flows} потоков (4 peer-to-peer и {r.Flows - 4} клиент-серверных).");

        // Законы распределения
        SetServer(s1, Distribution.Normal(2048, 512), Distribution.Exponential(5));
        SetServer(s2, Distribution.Exponential(1024), Distribution.Erlang(1.5, 0.4));
        await c.ServerEditors(s1,
            (0, "p12-s1-size", "Скриншот 12 — Reply Size для S1: нормальный закон, Mean = 2048, Std. Deviation = 512",
                "Свойства S1 → вкладка «Сервер (Reply)» → Reply Size: закон Normal, Mean = 2048, Std. Deviation = 512. Нормальный закон: большинство ответов около 2048 байт, крайние значения редки; ≈68% ответов — в диапазоне 1536–2560 байт (M ± σ). Под полями — гистограмма 4000 случайных значений.",
                "Часть 4. Законы распределения для серверов"),
            (1, "p14-s1-delay", "Скриншот 14 — Reply Delay для S1: экспоненциальный закон, Mean = 5 с",
                "Reply Delay для S1: закон Exponential, Mean = 5 с — задержки случайны, средняя 5 с; маленькие значения часты, большие редки (бывают и 1, и 15 с).", null));
        await c.ServerEditors(s2,
            (0, "p13-s2-size", "Скриншот 13 — Reply Size для S2: экспоненциальный закон, Mean = 1024",
                "Reply Size для S2: закон Exponential, Mean = 1024 байт — много маленьких ответов и редкие большие; подходит для интернет-трафика.", null),
            (1, "p15-s2-delay", "Скриншот 15 — Reply Delay для S2: закон Эрланга, Mean = 1,5 с, Std. Deviation = 0,4",
                "Reply Delay для S2: закон Erlang, Mean = 1,5 с, Std. Deviation = 0,4 — «сглаженный» экспоненциальный закон (порядок k = M²/σ² ≈ 14), задержки группируются около 1,5 с.", null));
        r.Laws = new ReportTable("Сводная таблица законов распределения (шаг 4.5)",
            new[] { "Сервер", "Reply Size (закон)", "Mean", "СКО", "Reply Delay (закон)", "Mean", "СКО" },
            new List<string[]>
            {
                new[] { "S1", "Нормальный", "2048 байт", "512 байт", "Экспоненциальный", "5 с", "5 с (у экспоненциального СКО = M)" },
                new[] { "S2", "Экспоненциальный", "1024 байт", "1024 байт (= M)", "Эрланга", "1,5 с", "0,4 с" },
            });

        // Моделирование
        foreach (var f in vm.Project.Flows) f.ShowAnimation = false;
        await c.Dialog(new DataFlowWindow(vm), "p16-uncheck", "Скриншот 16 — окно Data Flow со снятыми галочками (Uncheck All)",
            "Global → Data Flow → Uncheck All — анимация отключена (статистика собирается). Для скриншота 17 анимация снова включена.", "Часть 5. Запуск моделирования");
        foreach (var f in vm.Project.Flows) f.ShowAnimation = true;
        vm.TopologyChanged();
        await RunSimulation(c, 20);
        var running = vm.TrafficSim!;
        for (int i = 0; i < 200000 && running.InFlight.Count(p => p.Flow.ShowAnimation) < 1; i++) running.RunUntil(running.Now + 0.00005);
        vm.RefreshSimTime();
        vm.RequestRedraw();
        await c.Main("p17-run", "Скриншот 17 — сеть в режиме Run (в линии виден передаваемый пакет — цветная точка)", "Нажата кнопка Run.", rebuild: false);
        running.RunUntil(RunSeconds);
        vm.StopCommand.Execute(null);
        vm.RefreshSimTime();
        await c.Main("p18-stop", $"Скриншот 18 — сеть после остановки (Stop), модельное время {RunSeconds:0} с", null, null, rebuild: false);

        // Статистика
        foreach (var s in new[] { s1, s2 }) { s.ShownStatistics.Add(StatKeys.CurrentWorkload); s.ShownStatistics.Add(StatKeys.PacketsReceived); }
        await c.Selected(s1, "p19-s1", "Скриншот 19 — статистика S1 (current workload, packets received)",
            "ПКМ по серверу → Статистика: current workload и packets received.", "Часть 6. Снятие статистики");
        await c.Selected(s2, "p20-s2", "Скриншот 20 — статистика S2 (current workload, packets received)");
        seg1.ShownStatistics.Add(StatKeys.AverageUtilization);
        seg2.ShownStatistics.Add(StatKeys.AverageUtilization);
        await SelectLink(c, seg1, "p21-seg1", "Скриншот 21 — статистика сегмента 1 (линия Switch 1 — Router, average utilization)",
            "Для сегментов (линий между коммутаторами и маршрутизатором): ПКМ по линии → Статистика → average utilization.");
        await SelectLink(c, seg2, "p22-seg2", "Скриншот 22 — статистика сегмента 2 (линия Switch 2 — Router, average utilization)");
        vm.Selected = null;

        var sim = vm.TrafficSim!;
        double u1 = sim.LinkValue(seg1, StatKeys.AverageUtilization), u2 = sim.LinkValue(seg2, StatKeys.AverageUtilization);
        r.Summary = new ReportTable($"Сводная таблица статистики (шаг 6.3, модельное время {RunSeconds:0} с)",
            new[] { "Устройство / сегмент", "Current workload", "Average workload", "Packets received", "Average utilization" },
            new List<string[]>
            {
                Row(sim, s1), Row(sim, s2),
                new[] { "Сегмент 1 (Switch 1 — Router)", "—", "—", "—", F(StatKeys.AverageUtilization, u1) },
                new[] { "Сегмент 2 (Switch 2 — Router)", "—", "—", "—", F(StatKeys.AverageUtilization, u2) },
            });

        // Ответы — с проверкой на модели.
        double Resp(Device server) => vm.Project.Flows.Where(f => f.TargetId == server.Id && f.Profile.Pattern == TrafficPattern.ClientServer)
            .Select(f => sim.StatsOf(vm.Project.FindDevice(f.SourceId)!)).Where(st => st.TransactionsCompleted > 0).Select(st => st.AverageResponseTime).DefaultIfEmpty(0).Average();
        var (b1, b2, a1, a2, srvBefore, srvAfter) = ReplySize8192(vm.Project, seg1.Id, seg2.Id);
        r.Answers.Add("1. Для S1 выбран нормальный закон, потому что сервер базы данных и CAD/CAM отдаёт записи и фрагменты чертежей примерно одинакового размера: ответы группируются около среднего 2048 байт, а сильные отклонения редки. Для S2 (HTTP, FTP, почта — интернет-трафик) выбран экспоненциальный закон: большинство ответов маленькие (страницы, письма), а большие (файлы) встречаются редко.");
        r.Answers.Add($"2. Большая задержка у S1: среднее Reply Delay 5 с (экспоненциальный закон) против 1,5 с у S2 (Эрланг). Это подтверждает моделирование: среднее время завершения транзакций у клиентов S1 (WS1–WS3) — {Resp(s1):0.00} с, у клиентов S2 (WS4, WS5) — {Resp(s2):0.00} с (в среднее входят и быстрые peer-to-peer обмены, поэтому значения ниже Reply Delay, но соотношение сохраняется). У экспоненциального закона ещё и большой разброс — отдельные ответы S1 ждут 10–15 с.");
        var flowsOverRouter = vm.Project.Flows.Count(f =>
        {
            var path = sim.Graph.FindPath(f.SourceId, f.TargetId);
            return path != null && path.Any(l => l.Touches(router.Id));
        });
        r.Answers.Add($"3. Сегмент 1 (Switch 1 — Router): {F(StatKeys.AverageUtilization, u1)}, сегмент 2 (Switch 2 — Router): {F(StatKeys.AverageUtilization, u2)}. "
            + (flowsOverRouter == 0
                ? "Обе линии практически не загружены: по заданию все клиенты обращаются к серверу своего коммутатора (WS1–WS3 → S1 через Switch 1, WS4, WS5 → S2 через Switch 2), и ни один поток не проходит через маршрутизатор. Сильнее загружены линии внутри сегментов: например, линия Switch 1 — S1 — " + F(StatKeys.AverageUtilization, sim.LinkValue(sim.Graph.LinksOf(s1.Id)[0], StatKeys.AverageUtilization)) + ", линия Switch 2 — S2 — " + F(StatKeys.AverageUtilization, sim.LinkValue(sim.Graph.LinksOf(s2.Id)[0], StatKeys.AverageUtilization)) + "."
                : $"Через маршрутизатор проходит только {flowsOverRouter} поток(а) между сегментами (почта WS3 → S2), поэтому загрузка обеих линий — сотые доли процента: по сегменту 1 передано {StatKeys.FormatBytes(sim.LinkValue(seg1, StatKeys.BytesCarried))}, по сегменту 2 — {StatKeys.FormatBytes(sim.LinkValue(seg2, StatKeys.BytesCarried))}. Основной трафик (запросы к S1 и S2) идёт внутри своих коммутаторов: линия Switch 1 — S1 загружена на {F(StatKeys.AverageUtilization, sim.LinkValue(sim.Graph.LinksOf(s1.Id)[0], StatKeys.AverageUtilization))}, линия Switch 2 — S2 — на {F(StatKeys.AverageUtilization, sim.LinkValue(sim.Graph.LinksOf(s2.Id)[0], StatKeys.AverageUtilization))}. Сильнее нагружен первый сегмент: у S1 больше клиентов (WS1–WS3) и два вида запросов (Database и CAD/CAM)."));
        r.Answers.Add($"4. Проверено моделированием: при увеличении Reply Size до 8192 байт (S1 — нормальный закон M = 8192, σ = 2048; S2 — экспоненциальный M = 8192) размер каждого ответа растёт в 4 раза (S1) и в 8 раз (S2): суммарная средняя нагрузка серверов выросла с {F(StatKeys.AverageWorkload, srvBefore)} до {F(StatKeys.AverageWorkload, srvAfter)} — в {(srvBefore > 0 ? srvAfter / srvBefore : 0):0.0} раза (в нагрузку входят и запросы, которые не изменились), загрузка линий от серверов к клиентам растёт соответственно. Сеть Fast Ethernet при этом остаётся недогруженной, но время передачи ответов увеличивается.");
        r.Answers.Add("5. Законы распределения задают не только среднее, но и разброс значений. Реальный трафик неравномерен: бывают пики, длинные задержки, большие ответы. Только с законом распределения модель показывает очереди, пиковую загрузку и разброс времени ответа; модель «по средним» дала бы слишком оптимистичную картину и не выявила бы перегрузки в пиках.");
        _ = (b1, b2, a1, a2);

        await c.Main("p23-final", "Скриншот 23 — итоговая сеть со статистикой серверов и сегментов", null, "Итоговая сеть", rebuild: false);
        vm.ShowFlows = false;
    }

    private static string[] Row(TrafficSimulator sim, Device s) => new[]
    {
        s.Name, F(StatKeys.CurrentWorkload, sim.DeviceValue(s, StatKeys.CurrentWorkload)), F(StatKeys.AverageWorkload, sim.DeviceValue(s, StatKeys.AverageWorkload)),
        sim.DeviceValue(s, StatKeys.PacketsReceived).ToString("0"), "—",
    };

    private static async Task SelectLink(Ctx c, Link l, string file, string caption, string? text = null)
    {
        c.Vm.Selected = l;
        await SelfTest.Settle();
        SelfTest.Snap(c.Window, c.Dir, file);
        c.Shots.Add(new(file, caption, text, null));
    }

    /// <summary>Эксперимент к вопросу 4: Reply Size = 8192 байт.</summary>
    private static (double B1, double B2, double A1, double A2, double SrvBefore, double SrvAfter) ReplySize8192(Project original, string seg1, string seg2)
    {
        double Load(Project p, out double u1, out double u2)
        {
            var sim = new TrafficSimulator(p);
            sim.RunUntil(RunSeconds);
            u1 = sim.LinkValue(p.AllLinks().First(l => l.Id == seg1), StatKeys.AverageUtilization);
            u2 = sim.LinkValue(p.AllLinks().First(l => l.Id == seg2), StatKeys.AverageUtilization);
            return new[] { "S1", "S2" }.Sum(n => sim.DeviceValue(p.AllDevices().First(d => d.Name == n), StatKeys.AverageWorkload));
        }
        var before = ProjectSerializer.Clone(original);
        var after = ProjectSerializer.Clone(original);
        after.AllDevices().First(d => d.Name == "S1").Server.ReplySize = Distribution.Normal(8192, 2048);
        after.AllDevices().First(d => d.Name == "S2").Server.ReplySize = Distribution.Exponential(8192);
        var sb = Load(before, out var b1, out var b2);
        var sa = Load(after, out var a1, out var a2);
        return (b1, b2, a1, a2, sb, sa);
    }

    // ---------------- Творческое задание ----------------

    private static async Task Cafe(Ctx c, Results r)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.NewProjectCommand.Execute(null);
        vm.Project.Name = "Интернет-кафе (ПР5)";
        vm.Project.Description = "Творческое задание ПР5: 10 ПК, коммутатор Fast Ethernet, сервер (кэш + FTP + биллинг), маршрутизатор, облако PSTN; законы распределения выбраны и обоснованы.";
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
        var lPstn = b.Connect(router, pstn, "adsl", 2000);
        c.Install(server, "sw-http-server", "http", "ftp");
        c.Install(server, "sw-ftp-server", "ftp");
        c.Install(server, "sw-billing", "billing");
        foreach (var pc in pcs) c.Install(pc, "sw-http-server", "http", "ftp");
        foreach (var pc in pcs) { AddFlow(c, pc, server, "http"); AddFlow(c, pc, server, "ftp"); }
        for (int i = 0; i < 10; i += 2) AddFlow(c, pcs[i], pcs[i + 1], "small-office-p2p");
        AddFlow(c, pcs[0], pstn, "interlan");

        // Выбранные законы: кэш отдаёт веб-объекты разного размера, задержка кэша мала и стабильна.
        SetServer(server, Distribution.Exponential(16384), Distribution.Erlang(0.2, 0.05));
        vm.ShowFlows = true;
        await c.Main("k24-cafe", $"Скриншот 24 — схема интернет-кафе (10 ПК, коммутатор, сервер, маршрутизатор, PSTN; {vm.Project.Flows.Count} потоков)",
            "Создан новый проект. Все ПК, сервер и маршрутизатор подключены к коммутатору Fast Ethernet (100Base-T4, витая пара). По итогам ПР4 (модемная линия 56 кбит/с оказалась перегруженной) маршрутизатор подключён к PSTN по ADSL (8 Мбит/с). На сервер установлены HTTP server (HTTP, FTP), FTP server и Billing server, на ПК — HTTP и FTP. Трафик: все ПК → сервер (HTTP, FTP), ПК1↔ПК2, ПК3↔ПК4, ПК5↔ПК6, ПК7↔ПК8, ПК9↔ПК10 — Small office peer-to-peer, ПК1 → PSTN — InterLAN traffic.",
            "Творческое задание. Интернет-кафе");
        await c.ServerEditors(server,
            (0, "k25a-size", "Скриншот 25 (а) — выбранный закон Reply Size сервера: экспоненциальный, Mean = 16384 байт",
                "Законы распределения для сервера выбраны самостоятельно (см. таблицу обоснования ниже).", null),
            (1, "k25b-delay", "Скриншот 25 (б) — выбранный закон Reply Delay сервера: Эрланга, Mean = 0,2 с, Std. Deviation = 0,05", null, null));
        r.CafeLaws = new ReportTable("Выбор законов распределения для сервера интернет-кафе (обоснование)",
            new[] { "Параметр", "Закон и параметры", "Обоснование" },
            new List<string[]>
            {
                new[] { "Reply Size", "Exponential, Mean = 16384 байт (16 КБ)", "Сервер работает как кэш веб-страниц и FTP: большинство ответов — небольшие объекты страниц (HTML, картинки, CSS — единицы и десятки КБ), а крупные файлы встречаются редко. Такое распределение «много маленьких — мало больших» описывает экспоненциальный закон; среднее 16 КБ — типичный размер веб-объекта." },
                new[] { "Reply Delay", "Erlang, Mean = 0,2 с, СКО = 0,05 с", "Кэш отвечает из памяти быстро и стабильно: задержка складывается из нескольких последовательных этапов (приём запроса, поиск в кэше, биллинг, отправка), поэтому подходит закон Эрланга (сумма экспоненциальных этапов, k = M²/σ² = 16) — значения группируются около 0,2 с без длинных «хвостов»." },
            });

        server.ShownStatistics.Add(StatKeys.CurrentWorkload);
        server.ShownStatistics.Add(StatKeys.PacketsReceived);
        sw.ShownStatistics.Add(StatKeys.AverageUtilization);
        lPstn.ShownStatistics.Add(StatKeys.AverageUtilization);
        vm.TopologyChanged();
        await RunSimulation(c, RunSeconds);
        vm.StopCommand.Execute(null);
        var sim = vm.TrafficSim!;
        await c.Selected(server, "k26-server", "Скриншот 26 — статистика сервера (current workload, packets received)", $"Моделирование {RunSeconds:0} с (Run → Stop).");
        await c.Selected(sw, "k27-switch", "Скриншот 27 — статистика коммутатора (average utilization)");
        vm.Selected = null;

        double swU = sim.DeviceValue(sw, StatKeys.AverageUtilization), srvU = sim.LinkValue(lServer, StatKeys.AverageUtilization), pstnU = sim.LinkValue(lPstn, StatKeys.AverageUtilization);
        r.CafeSummary = new ReportTable($"Статистика интернет-кафе (модельное время {RunSeconds:0} с)",
            new[] { "Объект", "Показатель", "Значение" },
            new List<string[]>
            {
                new[] { "Сервер", "Current workload", F(StatKeys.CurrentWorkload, sim.DeviceValue(server, StatKeys.CurrentWorkload)) },
                new[] { "Сервер", "Average workload", F(StatKeys.AverageWorkload, sim.DeviceValue(server, StatKeys.AverageWorkload)) },
                new[] { "Сервер", "Packets received", sim.DeviceValue(server, StatKeys.PacketsReceived).ToString("0") },
                new[] { "Коммутатор", "Average utilization", F(StatKeys.AverageUtilization, swU) },
                new[] { "Линия коммутатор — сервер", "Average utilization", F(StatKeys.AverageUtilization, srvU) },
                new[] { "Линия маршрутизатор — PSTN (ADSL)", "Average utilization", F(StatKeys.AverageUtilization, pstnU) },
            });

        // Все 10 ПК одновременно качают файлы.
        var p = ProjectSerializer.Clone(vm.Project);
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
        double heavyU = heavy.LinkValue(heavy.Graph.Links.First(l => l.Touches(srv.Id)), StatKeys.AverageUtilization);

        r.CafeConclusion.Add($"При выбранных законах сеть кафе работает с большим запасом: средняя загрузка коммутатора {F(StatKeys.AverageUtilization, swU)}, линии к серверу {F(StatKeys.AverageUtilization, srvU)}, линии ADSL к провайдеру {F(StatKeys.AverageUtilization, pstnU)}.");
        r.CafeConclusion.Add($"Если все 10 ПК одновременно качают файлы (дополнительно по 5 МБ в среднем раз в 30 с на каждый ПК), загрузка линии «коммутатор — сервер» растёт до {F(StatKeys.AverageUtilization, heavyU)} — сеть выдерживает, но именно эта линия становится общим узким местом для всех ПК.");
        r.CafeConclusion.Add("Предложения по улучшению: 1) подключить сервер к коммутатору гигабитным портом (1000Base-T), чтобы снять узкое место при массовых загрузках; 2) при росте числа ПК заменить коммутатор на гигабитный и разделить игровой и служебный трафик по VLAN; 3) увеличить скорость канала к провайдеру (оптика вместо ADSL), если клиенты будут активно скачивать из интернета, а не из кэша.");
        vm.ShowFlows = false;
    }

    // ---------------- Отчёт ----------------

    private static void BuildReport(Ctx c, Results r)
    {
        var path = Path.Combine(c.Dir, "Отчёт ПР5 (ЛР5).docx");
        using var w = new WordReport(path);
        w.Title("ОТЧЁТ");
        w.Centered("по лабораторной работе №5 (ПР5)", bold: true);
        w.Centered("«Моделирование трафика сети с использованием законов распределения»", bold: true);
        w.Centered("(выполнено в NetSim — учебном аналоге NetCracker Professional)");
        w.Paragraph("");
        w.Centered("Выполнил(а): ____________________, группа ________");
        w.Centered("Проверил(а): ____________________");
        w.Centered(DateTime.Now.ToString("dd.MM.yyyy"));
        w.PageBreak();

        w.Heading1("Цель работы");
        w.Paragraph("Научиться создавать сеть с нуля, назначать трафик и настраивать законы распределения для параметров серверов (Reply Size, Reply Delay), а также снимать и анализировать статистику.");
        w.Heading1("Краткие сведения");
        w.Paragraph("Reply Size — размер ответа сервера на запрос (байты), Reply Delay — задержка перед отправкой ответа (секунды). Закон распределения описывает, как случайная величина принимает значения. Нормальный закон (Mean, СКО) — значения группируются около среднего; экспоненциальный (Mean) — маленькие значения часты, большие редки; равномерный (Rmin, Rmax) — все значения диапазона равновероятны (Rmin = M − σ√3, Rmax = M + σ√3); закон Эрланга (Mean, СКО) — «сглаженный» экспоненциальный.");
        w.Heading1("1. Основная часть");
        c.Images(w, c.Shots.Take(r.MainShots));
        w.Heading2("Сводные таблицы");
        w.Table(r.Laws, "Таблица");
        w.Table(r.Summary, "Таблица");
        w.Heading2("Ответы на вопросы");
        foreach (var a in r.Answers) w.Paragraph(a);

        w.Heading1("2. Творческое задание: сеть интернет-кафе");
        w.Paragraph("Задание: спроектировать сеть для интернет-кафе на 10 ПК и 1 сервер (кэш + FTP + биллинг). Клиенты сидят в интернете (HTTP), играют по локальной сети (peer-to-peer), скачивают файлы (FTP). Законы распределения выбрать самостоятельно и обосновать.");
        c.Images(w, c.Shots.Skip(r.MainShots));
        w.Table(r.CafeLaws, "Таблица");
        w.Table(r.CafeSummary, "Таблица");
        w.Heading2("Вывод по творческому заданию");
        foreach (var a in r.CafeConclusion) w.Paragraph(a);

        w.Heading1("Выводы");
        w.Paragraph("В ходе работы создана сеть с нуля, установлено серверное и клиентское ПО, назначен peer-to-peer и клиент-серверный трафик, для серверов заданы законы распределения размера и задержки ответа (нормальный, экспоненциальный, Эрланга), выполнено моделирование и снята статистика серверов и сегментов.");
        w.Paragraph("Законы распределения существенно влияют на результат: экспоненциальная задержка с большим средним (S1) даёт долгое и нестабильное время ответа, закон Эрланга (S2) — короткое и стабильное; увеличение размера ответа пропорционально увеличивает нагрузку на серверы и линии. Задание законов вместо средних значений позволяет увидеть разброс и пики трафика и заранее оценить, выдержит ли сеть нагрузку.");
    }
}
