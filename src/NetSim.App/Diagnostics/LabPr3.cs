using System.IO;
using System.Windows;
using System.Windows.Controls;
using NetSim.App.Localization;
using NetSim.App.Services;
using NetSim.App.ViewModels;
using NetSim.App.Views;
using NetSim.Core.Model;
using NetSim.Core.Serialization;
using NetSim.Core.Topology;

namespace NetSim.App.Diagnostics;

/// <summary>
/// ПР3 (ЛР2) «Проектирование топологии и спецификации вычислительной сети»:
/// пример «повторите за мной» (скриншоты 1–17) и самостоятельная работа, вариант 7.
/// Запуск: <c>NetSim.exe --lab pr3 &lt;папка&gt;</c>.
/// </summary>
public static class LabPr3
{
    public static async void Run(MainWindow window, MainViewModel vm, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new List<string>();
        var c = new Ctx { Window = window, Vm = vm, Dir = outDir };
        try
        {
            window.WindowState = WindowState.Normal;
            window.Width = 1400;
            window.Height = 860;
            vm.ShowLinkLabels = false;
            vm.ShowFlows = false;
            await SelfTest.Settle();

            await Example(c);
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР3 — пример (повторите за мной).nsp"));
            int exampleShots = c.Shots.Count;

            var (flows, served) = await Variant7(c);
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР3 — вариант 7.nsp"));
            log.Add($"variant7: flows={flows}, served={served}");

            BuildReport(c, exampleShots, flows, served);
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

    // ---------------- Пример «повторите за мной» ----------------

    private static async Task Example(Ctx c)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.NewProjectCommand.Execute(null);
        vm.Project.Name = "ПР3 — пример";
        c.B = new TopologyBuilder(vm.Project, vm.Catalog);
        var b = c.B;
        const string sec = "Пример: сеть Fast Ethernet — Token Ring — Fast Ethernet";

        await c.Main("e01-empty", "Скриншот 1 — чистое рабочее поле",
            "Создан новый проект (Файл → Новый проект). Рабочее поле пустое.", sec, fit: false);

        var sw1 = b.CreateDevice("switch-eth", 380, 300, name: "Switch 1");
        var ws1 = b.CreateDevice("pc", 150, 180, name: "WS1");
        var ws2 = b.CreateDevice("pc", 290, 110, name: "WS2");
        var ws3 = b.CreateDevice("pc", 450, 140, name: "WS3");
        var s1 = b.CreateDevice("server", 200, 440, name: "S1");
        await c.Main("e02-devices", "Скриншот 2 — на рабочем поле коммутатор, три ПК и сервер S1",
            "Из браузера Devices перетащены: коммутатор (Switches → Workgroup → Ethernet Switch), три компьютера WS1–WS3 (LAN Workstations → PCs and Notebooks → PC) и сервер S1 (LAN Servers → Server). Устройства переименованы в панели свойств.");

        var la = new LinkAssistantWindow(vm, sw1, ws1, null) { Owner = c.Window };
        la.Show();
        await SelfTest.Settle();
        SelectStandard(la, "100base-t4");
        await SelfTest.Settle();
        SelfTest.Snap(la, c.Dir, "e03a-link");
        la.Close();
        c.Shots.Add(new("e03a-link", "Окно Link Assistant: Fast Ethernet 100Base-T4, 100 Мбит/с, витая пара",
            "Инструментом Link щёлкнуты коммутатор и WS1. В окне Link Assistant выбрана спецификация (Protocol) Fast Ethernet 100Base-T4, скорость (Bandwidth) 100 Мбит/с, среда (Media) — неэкранированная витая пара, нажата кнопка Link. Так же подключены WS2, WS3 и S1.", null));
        foreach (var d in new[] { ws1, ws2, ws3, s1 }) b.Connect(sw1, d, "100base-t4", 10, mediaId: "utp5");
        await c.Main("e03-segment", "Скриншот 3 — сегмент Fast Ethernet: WS1–WS3 и S1 подключены к коммутатору");

        var router = b.CreateDevice("router", 600, 200, name: "Router");
        var bridge = b.CreateDevice("bridge", 560, 430, name: "Bridge");
        await c.Main("e04-router-bridge", "Скриншот 4 — на поле добавлены маршрутизатор и мост",
            "Из группы Routers and bridges добавлены маршрутизатор (Low end → Router) и мост (Bridges → Bridge).");

        b.Connect(sw1, router, "100base-t4", 10, mediaId: "utp5");
        b.Connect(sw1, bridge, "100base-t4", 10, mediaId: "utp5");
        await c.Main("e05-sw-router-bridge", "Скриншот 5 — коммутатор соединён с маршрутизатором и мостом (Fast Ethernet 100Base-T4)");

        var ring = b.CreateDevice("seg-token-ring", 830, 200, name: "Token Ring");
        var ws4 = b.CreateDevice("pc", 760, 60, name: "WS4");
        var ws5 = b.CreateDevice("pc", 950, 80, name: "WS5");
        var s2 = b.CreateDevice("server", 980, 280, name: "S2");
        foreach (var d in new[] { ws4, ws5, s2 }) b.Connect(d, ring, "tr-16", 10, mediaId: "stp");
        await c.Main("e06-token-ring", "Скриншот 6 — кольцо Token Ring с WS4, WS5 и S2",
            "Из группы Generic LANs → Token Ring на поле добавлено кольцо. К нему подключены WS4, WS5 и сервер S2 — программа сама предложила спецификацию Token Ring 16 Мбит/с, так как кольцо поддерживает только Token Ring. Все станции кольца образуют общую среду (один домен).");

        b.Connect(router, ring, "tr-16", 10, mediaId: "stp");
        await c.Main("e07-router-ring", "Скриншот 7 — маршрутизатор соединён с Token Ring (16 Мбит/с, экранированная витая пара STP)");

        var sw2 = b.CreateDevice("switch-eth", 780, 480, name: "Switch 2");
        var ws6 = b.CreateDevice("pc", 640, 600, name: "WS6");
        var ws7 = b.CreateDevice("pc", 800, 630, name: "WS7");
        var ws8 = b.CreateDevice("pc", 960, 580, name: "WS8");
        var s3 = b.CreateDevice("server", 960, 430, name: "S3");
        foreach (var d in new[] { ws6, ws7, ws8, s3 }) b.Connect(sw2, d, "100base-t4", 10, mediaId: "utp5");
        b.Connect(bridge, sw2, "100base-t4", 10, mediaId: "utp5");
        await c.Main("e08-second-segment", "Скриншот 8 — второй сегмент Fast Ethernet: WS6–WS8 и S3, подключённый к мосту",
            "Добавлен второй коммутатор, к нему подключены WS6, WS7, WS8 и сервер S3; мост соединён с этим коммутатором (Fast Ethernet 100Base-T4).");
        c.Vm.ShowLinkLabels = true;
        await c.Main("e09-full", "Скриншот 9 — полная сеть (видны все устройства, соединения и спецификации линий)");
        c.Vm.ShowLinkLabels = false;

        // ПО
        c.Install(s1, "sw-small-office-db", "db", "cad");
        await c.Software(s1, "sw-small-office-db", "e10-s1", "Скриншот 10 — свойства S1: Small office database server, отмечены Database client-server и CAD/CAM client-server",
            "Из группы Network and enterprise software → Server software на значок S1 перетащено ПО Small office database server. В окне конфигурации S1 (вкладка «Программы (Plugin Setup)») отмечены службы Database client-server и CAD/CAM client-server.",
            "Установка программного обеспечения");
        foreach (var w in new[] { ws1, ws2, ws3 }) c.Install(w, "sw-small-office-db", "db", "cad");
        await c.Software(ws1, "sw-small-office-db", "e11-ws1", "Скриншот 11 — свойства WS1: те же галочки Database и CAD/CAM",
            "Такое же ПО с теми же службами установлено на клиентов S1 — WS1, WS2, WS3.");
        c.Install(s2, "sw-file-server", "file");
        await c.Software(s2, "sw-file-server", "e12-s2", "Скриншот 12 — свойства S2: File server, отмечена File client-server");
        foreach (var w in new[] { ws4, ws5 }) c.Install(w, "sw-small-office-db", "db");
        await c.Software(ws4, "sw-small-office-db", "e13-ws4", "Скриншот 13 — свойства WS4: отмечена Database client-server (клиент базы данных S2)",
            "На WS4 и WS5 установлено Small office database server с включённой службой Database client-server.");
        c.Install(s3, "sw-http-server", "http", "ftp", "pop3");
        await c.Software(s3, "sw-http-server", "e14-s3", "Скриншот 14 — свойства S3: HTTP server, отмечены HTTP, FTP, POP3");
        var all = new[] { ws1, ws2, ws3, ws4, ws5, ws6, ws7, ws8 };
        foreach (var w in all) { c.Install(w, "sw-http-server", "http"); c.Install(w, "sw-file-server", "file"); }
        await c.Selected(ws3, "e15-ws3", "Скриншот 15 — свойства WS3: установлены HTTP server (HTTP) и File server (File client-server); справа — список ПО и служб",
            "На все рабочие станции WS1–WS8 установлены HTTP server (служба HTTP — все станции являются HTTP-клиентами) и File server (File client-server — обращение к серверу за файлами).");
        foreach (var w in new[] { ws3, ws5, ws7, ws8 }) c.Install(w, "sw-email-server", "pop3");
        await c.Selected(ws5, "e16-ws5", "Скриншот 16 — свойства WS5 с дополнительной службой POP3 (E-mail server)",
            "На WS3, WS5, WS7, WS8 дополнительно установлено E-mail server со службой POP3.");

        var p = new ProfilesWindow(c.Vm, ws1, s1) { Owner = c.Window };
        p.Show();
        await SelfTest.Settle();
        SelfTest.Snap(p, c.Dir, "e17a-profiles");
        p.Close();
        b.AddFlow(ws1, s1, "database");
        c.Vm.ShowFlows = true;
        c.Vm.Selected = null;
        c.Shots.Add(new("e17a-profiles", "Окно Profiles: трафик WS1 → S1, профиль Database client-server",
            "Инструментом Traffic (молния) щёлкнуты WS1 и S1, в окне Profiles выбран профиль Database client-server и нажата кнопка Assign.", "Назначение трафика (дополнительно)"));
        await c.Main("e17-traffic", "Скриншот 17 — назначенный трафик между WS1 и S1 (пунктирная линия со стрелкой)");
        c.Vm.ShowFlows = false;
    }

    private static void SelectStandard(LinkAssistantWindow w, string id)
    {
        var list = LabPr2.FindChild<ListBox>(w)!;
        list.SelectedItem = list.Items.OfType<ListBoxItem>().First(i => (string)i.Tag == id);
    }

    // ---------------- Вариант 7 ----------------

    private static async Task<(int Flows, long Served)> Variant7(Ctx c)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.NewProjectCommand.Execute(null);
        vm.Project.Name = "ПР3 — вариант 7";
        vm.Project.Description = "FDDI-кольцо (CDDI, UTP кат. 5) с WS1–WS3 и сервером 1; через маршрутизатор — Token Ring 16 Мбит/с (WS4, WS5, сервер 2); через мост — Fast Ethernet 100 Мбит/с (WS6–WS8, сервер 3).";
        c.B = new TopologyBuilder(vm.Project, vm.Catalog);
        var b = c.B;
        const string sec = "Самостоятельная работа. Вариант 7";

        var fddi = b.CreateDevice("seg-fddi", 330, 290, name: "FDDI");
        var ws1 = b.CreateDevice("pc", 150, 150, name: "WS1");
        var ws2 = b.CreateDevice("pc", 330, 110, name: "WS2");
        var ws3 = b.CreateDevice("pc", 510, 150, name: "WS3");
        var srv1 = b.CreateDevice("server", 170, 450, name: "Сервер 1 (FDDI)");
        foreach (var d in new[] { ws1, ws2, ws3, srv1 }) b.Connect(d, fddi, "cddi", 20, mediaId: "utp5");

        var router = b.CreateDevice("router", 620, 270, name: "Маршрутизатор");
        b.Connect(fddi, router, "cddi", 20, mediaId: "utp5");
        var tr = b.CreateDevice("seg-token-ring", 860, 230, name: "Token Ring");
        b.Connect(router, tr, "tr-16", 15, mediaId: "stp");
        var ws4 = b.CreateDevice("pc", 790, 80, name: "WS4");
        var ws5 = b.CreateDevice("pc", 980, 90, name: "WS5");
        var srv2 = b.CreateDevice("server", 1010, 320, name: "Сервер 2 (Token Ring)");
        foreach (var d in new[] { ws4, ws5, srv2 }) b.Connect(d, tr, "tr-16", 10, mediaId: "stp");

        var bridge = b.CreateDevice("bridge", 450, 480, name: "Мост");
        b.Connect(fddi, bridge, "cddi", 20, mediaId: "utp5");
        var sw = b.CreateDevice("switch-eth", 680, 520, name: "Коммутатор Ethernet");
        b.Connect(bridge, sw, "100base-tx", 15, mediaId: "utp5");
        var ws6 = b.CreateDevice("pc", 560, 650, name: "WS6");
        var ws7 = b.CreateDevice("pc", 720, 670, name: "WS7");
        var ws8 = b.CreateDevice("pc", 880, 640, name: "WS8");
        var srv3 = b.CreateDevice("server", 900, 480, name: "Сервер 3 (Ethernet)");
        foreach (var d in new[] { ws6, ws7, ws8, srv3 }) b.Connect(sw, d, "100base-tx", 10, mediaId: "utp5");

        var la = new LinkAssistantWindow(vm, ws1, fddi, null) { Owner = c.Window };
        la.Show();
        await SelfTest.Settle();
        SelectStandard(la, "cddi");
        await SelfTest.Settle();
        SelfTest.Snap(la, c.Dir, "v01-link-cddi");
        la.Close();
        c.Shots.Add(new("v01-link-cddi", "Вариант 7. Link Assistant: подключение WS1 к кольцу FDDI по CDDI (FDDI по неэкранированной витой паре кат. 5, 100 Мбит/с)",
            "Кольцо FDDI взято из группы Generic LANs → FDDI. Рабочие станции WS1–WS3 и сервер 1 подключены к кольцу инструментом Link: так как по заданию используется неэкранированная витая пара категории 5, выбрана спецификация CDDI (FDDI over copper) — 100 Мбит/с, среда UTP кат. 5. Маршрутизатор и мост тоже подключены к кольцу по CDDI.",
            sec));

        vm.ShowLinkLabels = true;
        await c.Main("v02-topology", "Вариант 7. Топология сети: FDDI-кольцо, маршрутизатор → Token Ring 16 Мбит/с, мост → Fast Ethernet 100 Мбит/с (подписаны спецификации и длины линий)",
            "Маршрутизатор соединён с кольцом Token Ring (16 Мбит/с, экранированная витая пара STP); к кольцу подключены WS4, WS5 и сервер 2. Мост соединён с коммутатором Ethernet по 100Base-TX; к коммутатору подключены WS6, WS7, WS8 и сервер 3 (Fast Ethernet 100Base-TX, UTP кат. 5).");
        vm.ShowLinkLabels = false;

        // ПО по заданию
        c.Install(srv1, "sw-small-office-db", "db", "cad");
        c.Install(srv2, "sw-file-server", "file");
        c.Install(srv2, "sw-small-office-db", "db");
        c.Install(srv3, "sw-http-server", "http", "ftp", "pop3");
        foreach (var w in new[] { ws1, ws2, ws3 }) c.Install(w, "sw-small-office-db", "db", "cad");
        foreach (var w in new[] { ws4, ws5 }) { c.Install(w, "sw-file-server", "file"); c.Install(w, "sw-small-office-db", "db"); }
        var all = new[] { ws1, ws2, ws3, ws4, ws5, ws6, ws7, ws8 };
        foreach (var w in all) c.Install(w, "sw-http-server", "http", "ftp");
        foreach (var w in new[] { ws3, ws5, ws7, ws8 }) c.Install(w, "sw-email-server", "pop3");

        await c.Software(srv1, "sw-small-office-db", "v03-srv1", "Вариант 7. Сервер 1 (FDDI): Small office database server — Database client-server и CAD/CAM client-server",
            "Сервер FDDI обслуживает WS1–WS3 как клиентов базы данных и CAD/CAM-приложений — на него установлено Small office database server с обеими службами.");
        await c.Software(srv2, "sw-file-server", "v04-srv2-file", "Вариант 7. Сервер 2 (Token Ring): File server — File client-server",
            "Сервер Token Ring является файл-сервером для WS4, WS5 (File server, File client-server) и обслуживает их как клиентов базы данных (Small office database server, Database client-server).");
        await c.Software(srv2, "sw-small-office-db", "v05-srv2-db", "Вариант 7. Сервер 2 (Token Ring): Small office database server — Database client-server");
        await c.Software(srv3, "sw-http-server", "v06-srv3", "Вариант 7. Сервер 3 (Ethernet): HTTP server — HTTP, FTP, POP3",
            "Сервер Ethernet обслуживает HTTP-, FTP- и POP3-клиентов: на него установлено HTTP server с включёнными службами HTTP, FTP, POP3.");
        await c.Selected(ws5, "v07-ws5", "Вариант 7. Свойства WS5: File server, Small office database server, HTTP server (HTTP, FTP), E-mail server (POP3)",
            "Клиентское ПО: все станции — HTTP-клиенты и обращаются на FTP-сервер за файлами (HTTP server: HTTP, FTP); WS1–WS3 — клиенты БД и CAD/CAM; WS4, WS5 — клиенты файл-сервера и БД; WS3, WS5, WS7, WS8 — POP3-клиенты (E-mail server: POP3).");

        // Проверка: назначаем трафик по ролям и моделируем.
        int flows = 0;
        void F(Device a, Device t, string profile)
        {
            var (f, e) = b.AddFlow(a, t, profile);
            if (f == null) throw new InvalidOperationException(e);
            flows++;
        }
        foreach (var w in new[] { ws1, ws2, ws3 }) { F(w, srv1, "database"); F(w, srv1, "cadcam"); }
        foreach (var w in new[] { ws4, ws5 }) { F(w, srv2, "file-cs"); F(w, srv2, "database"); }
        foreach (var w in all) { F(w, srv3, "http"); F(w, srv3, "ftp"); }
        foreach (var w in new[] { ws3, ws5, ws7, ws8 }) F(w, srv3, "email-pop");
        foreach (var s in new[] { srv1, srv2, srv3 }) { s.ShownStatistics.Add("requestsServed"); }
        tr.ShownStatistics.Add("averageUtilization");
        fddi.ShownStatistics.Add("averageUtilization");
        vm.TopologyChanged();
        vm.RunCommand.Execute(null);
        vm.TrafficSim!.RunUntil(300);
        vm.PauseCommand.Execute(null);
        vm.RefreshSimTime();
        long served = new[] { srv1, srv2, srv3 }.Sum(s => vm.TrafficSim.StatsOf(s).RequestsServed);
        if (vm.TrafficSim.Diagnostics.Count > 0) throw new InvalidOperationException(vm.TrafficSim.Diagnostics[0].Message);
        vm.ShowFlows = true;
        vm.Selected = srv3;
        await c.Main("v08-check", $"Вариант 7. Проверка: назначено {flows} потоков по ролям клиентов и серверов, моделирование {vm.TrafficSim.Now:0} с — под серверами число обслуженных запросов, под кольцами — средняя загрузка",
            $"Для проверки правильности установки ПО инструментом Traffic назначены потоки по ролям из задания (программа не даёт назначить клиент-серверный трафик, если на сервере не включена нужная служба). Все {flows} потоков назначены без ошибок, маршруты между всеми сегментами найдены; за {vm.TrafficSim.Now:0} с моделирования серверы обслужили {served} запросов.",
            "Проверка сети", rebuild: false);
        vm.ShowFlows = false;
        return (flows, served);
    }

    // ---------------- Отчёт ----------------

    private static void BuildReport(Ctx c, int exampleShots, int flows, long served)
    {
        var path = Path.Combine(c.Dir, "Отчёт ПР3 (ЛР2) — вариант 7.docx");
        using var w = new WordReport(path);
        w.Title("ОТЧЁТ");
        w.Centered("по лабораторной работе №2 (ПР3)", bold: true);
        w.Centered("«Проектирование топологии и спецификации вычислительной сети»", bold: true);
        w.Centered("Вариант 7");
        w.Centered("(выполнено в NetSim — учебном аналоге NetCracker Professional)");
        w.Paragraph("");
        w.Centered("Выполнил(а): ____________________, группа ________");
        w.Centered("Проверил(а): ____________________");
        w.Centered(DateTime.Now.ToString("dd.MM.yyyy"));
        w.PageBreak();

        w.Heading1("Цель работы");
        w.Paragraph("Научиться проектировать вычислительные сети заданной топологии и спецификации.");

        w.Heading1("1. Пример (повторение за преподавателем)");
        w.Paragraph("Задание примера: рабочие станции WS1–WS3 и сервер S1 соединены в сеть Fast Ethernet через коммутатор (неэкранированная витая пара кат. 5). Сеть Ethernet через маршрутизатор связана с сетью Token Ring 16 Мбит/с (WS4, WS5, сервер S2), а через мост — с другой сетью Fast Ethernet (WS6–WS8, сервер S3). S1 обслуживает клиентов базы данных и CAD/CAM, S2 — файл-сервер и сервер БД для WS4, WS5, S3 — HTTP, FTP, POP3. Все станции — HTTP-клиенты, WS3, WS5, WS7, WS8 — POP3-клиенты, все станции обращаются к серверу за файлами.");
        c.Images(w, c.Shots.Take(exampleShots));

        w.Heading1("2. Самостоятельная работа. Вариант 7");
        w.Paragraph("Задание: рабочие станции (1), (2), (3) и сервер (1) соединены между собой в FDDI сеть, используя неэкранированную витую пару категории 5. FDDI кольцо посредством маршрутизатора и моста связано с сетями 16 Мбит/с Token Ring и 100 Мбит/с Ethernet соответственно. Рабочие станции (4), (5) и сервер (2) соединены в сеть Token Ring. Станции (6), (7), (8) и сервер (3) соединены по технологии Fast Ethernet. FDDI сервер обслуживает WS1–WS3 — клиентов базы данных и CAD/CAM-приложений. Сервер Token Ring является файл-сервером для WS4, WS5 и обслуживает их как клиентов базы данных. Сервер Ethernet обслуживает HTTP, FTP, POP3-клиентов. Все рабочие станции являются HTTP-клиентами. Рабочие станции (3), (5), (7), (8) являются также POP3-клиентами. Кроме этого все рабочие станции обращаются на FTP-сервер за файлами.");

        var p = c.Vm.Project;
        w.Table(new ReportTable("Спецификации сегментов сети (вариант 7)",
            new[] { "Сегмент", "Устройства", "Спецификация", "Среда", "Скорость" },
            new List<string[]>
            {
                new[] { "FDDI-кольцо", "WS1, WS2, WS3, сервер 1, маршрутизатор, мост", "CDDI (FDDI по витой паре)", "UTP кат. 5", "100 Мбит/с" },
                new[] { "Token Ring", "WS4, WS5, сервер 2, маршрутизатор", "Token Ring 16", "STP", "16 Мбит/с" },
                new[] { "Fast Ethernet", "WS6, WS7, WS8, сервер 3, мост (через коммутатор)", "100Base-TX", "UTP кат. 5", "100 Мбит/с" },
            }), "Таблица");
        w.Table(ReportTables.Equipment(p, c.Vm.Catalog) with { Title = "Оборудование и установленное программное обеспечение (вариант 7)" }, "Таблица");
        w.Table(ReportTables.Links(p, c.Vm.Catalog) with { Title = "Линии связи (вариант 7)" }, "Таблица");
        c.Images(w, c.Shots.Skip(exampleShots));

        w.Heading1("Выводы");
        w.Paragraph("В ходе работы спроектирована сеть по примеру преподавателя (Fast Ethernet — Token Ring — Fast Ethernet) и по варианту 7. Освоено соединение сегментов разных технологий: маршрутизатор связывает FDDI с Token Ring, мост — FDDI с Fast Ethernet. Для каждого сегмента выбрана спецификация в соответствии с заданием: CDDI (FDDI по неэкранированной витой паре кат. 5, 100 Мбит/с) для кольца FDDI, Token Ring 16 Мбит/с для кольцевой сети и 100Base-TX для сети Fast Ethernet.");
        w.Paragraph($"На серверы установлено программное обеспечение в соответствии с их ролями (Small office database server, File server, HTTP server), на рабочие станции — клиентские службы (Database, CAD/CAM, File, HTTP, FTP, POP3). Правильность настройки проверена назначением {flows} потоков трафика по ролям клиентов и серверов и моделированием: все маршруты между сегментами найдены, серверы обслужили {served} запросов.");
    }

}
