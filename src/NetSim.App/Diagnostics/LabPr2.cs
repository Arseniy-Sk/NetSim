using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetSim.App.Services;
using NetSim.App.ViewModels;
using NetSim.App.Views;
using NetSim.Core.Model;
using NetSim.Core.Serialization;
using NetSim.Core.Topology;

namespace NetSim.App.Diagnostics;

/// <summary>
/// Выполнение ПР2 (ЛР1 «Знакомство с программой») в NetSim по шагам методички:
/// <c>NetSim.exe --lab pr2 &lt;папка&gt;</c>. Сохраняет скриншоты, проекты и отчёт Word.
/// </summary>
public static class LabPr2
{
    private sealed record Shot(string File, string Caption, string? TextBefore, string? Section);

    public static async void Run(MainWindow window, MainViewModel vm, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var shots = new List<Shot>();
        var log = new List<string>();
        try
        {
            window.WindowState = WindowState.Normal;
            window.Width = 1400;
            window.Height = 860;
            await SelfTest.Settle();

            // Скриншот 1 — окно Проводника с файлом программы (снимается заранее и кладётся в папку).
            if (File.Exists(Path.Combine(outDir, "s01-explorer.png")))
                shots.Add(new("s01-explorer", "Скриншот 1 — окно Проводника с выделенным файлом программы NetSim.exe",
                    "Вместо установщика NetCracker используется программа NetSim — один исполняемый файл NetSim.exe (около 62 МБ), который не требует установки и дополнительных компонентов. Скриншот 2 (окно «Installation complete») не делается: установки нет.",
                    "Шаги 1.1–1.2 — Получение программы"));

            // Скриншот 3 — главное окно сразу после запуска.
            SelfTest.Snap(window, outDir, "s03-main");
            shots.Add(new("s03-main", "Скриншот 3 — главное окно программы NetSim (открылась без ошибок)",
                "Программа запущена двойным щелчком по файлу NetSim.exe.",
                "Шаг 1.3 — Запуск программы"));

            // Скриншот 4 — главное окно с подписями частей.
            SelfTest.Snap(window, outDir, "s04-parts");
            Annotate(window, Path.Combine(outDir, "s04-parts.png"));
            shots.Add(new("s04-parts", "Скриншот 4 — главное окно: браузер оборудования, рабочее поле, главное меню",
                "Главное окно состоит из трёх основных частей: браузер оборудования слева (закладки Devices и Profiles), рабочее поле по центру (здесь рисуется сеть) и главное меню сверху (Файл, Правка, Вид, План, Global, Моделирование, Инструменты, Справка). Справа расположена панель свойств выделенного объекта, внизу — журнал сообщений.",
                "2.1 — Изучение главного окна"));

            // Скриншот 5 — открыт пример Techno.
            vm.OpenSampleCommand.Execute("techno");
            await SelfTest.Settle();
            SelfTest.Snap(window, outDir, "s05-techno");
            shots.Add(new("s05-techno", "Скриншот 5 — рабочее поле с загруженным проектом Techno",
                "Через меню Файл → Открыть пример → «Techno — сеть предприятия» загружен готовый проект: магистраль FDDI, маршрутизатор Cisco 7000, отдел продаж на коммутаторе Fast Ethernet, бухгалтерия в кольце Token Ring, выход в сеть Frame Relay через DSU/CSU. Масштаб подобран автоматически (Вид → Вписать в окно).",
                "2.2 — Открытие готового проекта"));

            // Скриншоты 7–12 — группы браузера оборудования.
            var groups = new (string File, string[] Path, string? Item, string Caption)[]
            {
                ("s07-generic-lans", new[] { "c-generic-lans" }, null, "Скриншот 7 — Generic LANs: Thick Ethernet, Thin Ethernet, Token Ring, FDDI"),
                ("s08-hubs", new[] { "c-hubs", "c-hubs-shared" }, "hub-eth", "Скриншот 8 — Hubs → Shared media hubs → Ethernet Hub"),
                ("s09-switches", new[] { "c-switches", "c-sw-workgroup" }, "switch-eth", "Скриншот 9 — Switches → Workgroup → Ethernet Switch"),
                ("s10-routers", new[] { "c-routers", "c-rt-lowend" }, "router", "Скриншот 10 — Routers and bridges → Low end → Router"),
                ("s11-workstations", new[] { "c-workstations", "c-ws-pcs" }, "pc", "Скриншот 11 — LAN Workstations → PCs and Notebooks → PC"),
                ("s12-security", new[] { "c-security", "c-sec-fw" }, "firewall", "Скриншот 12 — Security → Firewalls → Firewall"),
            };
            bool first = true;
            foreach (var g in groups)
            {
                await ShowCatalog(window, vm, g.Path, g.Item);
                SelfTest.Snap(window, outDir, g.File);
                shots.Add(new(g.File, g.Caption,
                    first ? "Браузер оборудования (закладка Devices) содержит 28 групп оборудования, как в NetCracker. Группы раскрываются щелчком по стрелке слева от названия; при наведении на элемент показывается подсказка с описанием." : null,
                    first ? "3 — Знакомство с браузером оборудования" : null));
                first = false;
            }

            // Скриншот 13 — вся закладка Devices, прокручено до конца.
            Collapse(vm.CatalogTree);
            await SelfTest.Settle();
            ScrollTreeToEnd(window);
            await SelfTest.Settle();
            SelfTest.Snap(window, outDir, "s13-devices-end");
            shots.Add(new("s13-devices-end", "Скриншот 13 — закладка Devices, список прокручен до конца", null, null));

            // Скриншот 14 — окно конфигурации Cisco 7000.
            var cisco = vm.Project.AllDevices().First(d => d.Name == "Cisco 7000");
            vm.Checkpoint();
            var config = new DeviceConfigWindow(vm, cisco) { Owner = window };
            config.Show();
            await SelfTest.Settle();
            SelfTest.Snap(config, outDir, "s14-cisco-general");
            shots.Add(new("s14-cisco-general", "Скриншот 14 — окно конфигурации Cisco 7000, вкладка «Общие»",
                "Двойной щелчок по маршрутизатору Cisco 7000 открывает окно конфигурации (аналог Device Setup). На вкладке «Общие» указаны модель, число портов, поддерживаемые спецификации и описание устройства.",
                "4.1 — Свойства маршрутизатора"));
            var tabs = FindChild<TabControl>(config)!;
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Header == Localization.Loc.T("Config_Network"));
            await SelfTest.Settle();
            SelfTest.Snap(config, outDir, "s15-cisco-interfaces");
            shots.Add(new("s15-cisco-interfaces", "Скриншот 15 — интерфейсы маршрутизатора Cisco 7000 (вкладка «Сеть (IP)»)",
                "В NetSim сменные модули маршрутизатора (ATM Interface Processor и др.) не моделируются как отдельные платы; вместо окна свойств модуля показан список интерфейсов маршрутизатора с MAC-адресами — по одному на каждое подключение.",
                "4.2 — Модули и интерфейсы маршрутизатора"));
            config.Close();

            // Скриншот 16 — Media Colors.
            await SelfTest.Dialog(new MediaColorsWindow(vm), outDir, "s16-media-colors");
            shots.Add(new("s16-media-colors", "Скриншот 16 — окно Media Colors (Вид → Цвета сред)",
                "Цвет линии на схеме определяется типом среды передачи: витая пара — синие оттенки, коаксиал — зелёные, оптоволокно — оранжевые, радиоканал и спутниковый канал — пунктир. Окно открыто через меню Вид → Цвета сред (Media Colors), ничего не изменялось.",
                "Цвета линий связи"));

            // Скриншот 17 — сеть из 3 ПК и коммутатора.
            vm.IsDirty = false;
            vm.NewProjectCommand.Execute(null);
            vm.Project.Name = "Три ПК и коммутатор";
            var b = new TopologyBuilder(vm.Project, vm.Catalog);
            var sw = b.CreateDevice("switch-eth", 400, 120, name: "Коммутатор");
            var pcs = new List<Device>();
            for (int i = 0; i < 3; i++) pcs.Add(b.CreateDevice("pc", 220 + i * 180, 300, name: $"Компьютер {i + 1}"));
            var linkDlg = new LinkAssistantWindow(vm, sw, pcs[0], null) { Owner = window };
            linkDlg.Show();
            await SelfTest.Settle();
            SelfTest.Snap(linkDlg, outDir, "s17a-link-assistant");
            linkDlg.Close();
            foreach (var pc in pcs) b.Connect(sw, pc, "100base-tx", 10);
            vm.TopologyChanged();
            vm.CurrentLevel = vm.Project.Root;
            vm.ShowLinkLabels = true;
            await SelfTest.Settle();
            window.Topology.FitToView();
            await SelfTest.Settle();
            SelfTest.Snap(window, outDir, "s17-three-pcs");
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР2 — три ПК и коммутатор.nsp"));
            shots.Add(new("s17a-link-assistant", "Окно Link Assistant при соединении коммутатора с компьютером: Fast Ethernet 100Base-TX, витая пара кат. 5, длина 10 м",
                "Создан новый проект (Файл → Новый проект). Из группы Switches → Workgroup на поле перетащен Ethernet Switch, из LAN Workstations → PCs and Notebooks — три PC. Инструментом Link последовательно соединены коммутатор и каждый компьютер; в окне Link Assistant выбрана спецификация 100Base-TX (UTP), длина 10 м, нажата кнопка Link.",
                "5–6 — Соединение устройств: сеть из 3 компьютеров и 1 коммутатора"));
            shots.Add(new("s17-three-pcs", "Скриншот 17 — готовая сеть из 3 ПК и 1 коммутатора, соединённых кабелями (подписаны спецификация и длина линий)", null, null));

            // Скриншот 18 — сеть «Офис».
            vm.IsDirty = false;
            vm.NewProjectCommand.Execute(null);
            vm.Project.Name = "Офис";
            vm.Project.Description = "ПР2, самостоятельные задания 1–2: 5 компьютеров, 2 коммутатора, маршрутизатор, почтовый сервер.";
            b = new TopologyBuilder(vm.Project, vm.Catalog);
            var router = b.CreateDevice("router", 470, 90, name: "Маршрутизатор");
            var swA = b.CreateDevice("switch-eth", 260, 230, name: "Коммутатор А");
            var swB = b.CreateDevice("switch-eth", 680, 230, name: "Коммутатор Б");
            var office = new List<Device>();
            for (int i = 0; i < 3; i++) office.Add(b.CreateDevice("pc", 120 + i * 140, 400, name: $"Компьютер {i + 1}"));
            for (int i = 0; i < 2; i++) office.Add(b.CreateDevice("pc", 610 + i * 140, 400, name: $"Компьютер {i + 4}"));
            for (int i = 0; i < 3; i++) b.Connect(swA, office[i], "100base-tx", 10);
            for (int i = 3; i < 5; i++) b.Connect(swB, office[i], "100base-tx", 10);
            b.Connect(swA, router, "100base-tx", 20);
            b.Connect(swB, router, "100base-tx", 20);
            vm.TopologyChanged();
            vm.CurrentLevel = vm.Project.Root;
            vm.ShowLinkLabels = false;
            await SelfTest.Settle();
            window.Topology.FitToView();
            await SelfTest.Settle();
            SelfTest.Snap(window, outDir, "s18-office");
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР2 — Офис (задание 1).nsp"));
            shots.Add(new("s18-office", "Скриншот 18 — сеть «Офис»: компьютеры 1–3 подключены к коммутатору А, компьютеры 4–5 — к коммутатору Б, коммутаторы соединены с маршрутизатором",
                "Компьютеры 1–3 подключены к коммутатору А, компьютеры 4–5 — к коммутатору Б (Fast Ethernet 100Base-TX, 10 м). Каждый коммутатор соединён с маршрутизатором (Routers and bridges → Low end → Router) отдельным кабелем длиной 20 м. Устройства переименованы в панели свойств справа (поле «Имя»).",
                "Самостоятельное задание №1 — сеть «Офис»"));

            // Скриншот 19 — сервер с почтовым ПО и трафиком.
            var server = b.CreateDevice("server", 90, 230, name: "Почтовый сервер");
            b.Connect(swA, server, "100base-tx", 10);
            vm.InstallSoftware(server, "sw-email-server");
            var profiles = new ProfilesWindow(vm, office[0], server) { Owner = window };
            profiles.Show();
            await SelfTest.Settle();
            SelfTest.Snap(profiles, outDir, "s19a-profiles");
            profiles.Close();
            var (flow, error) = b.AddFlow(office[0], server, "email-pop");
            if (flow == null) throw new InvalidOperationException(error);
            server.ShownStatistics.Add("requestsServed");
            server.ShownStatistics.Add("packetsReceived");
            vm.TopologyChanged();
            vm.CurrentLevel = vm.Project.Root;
            vm.ShowFlows = true;
            vm.RunCommand.Execute(null);
            vm.TrafficSim!.RunUntil(600);
            vm.PauseCommand.Execute(null);
            vm.RefreshSimTime();
            vm.Selected = server;
            vm.RequestRedraw();
            await SelfTest.Settle();
            window.Topology.FitToView();
            await SelfTest.Settle();
            SelfTest.Snap(window, outDir, "s19-office-server");
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР2 — Офис с сервером (задание 2).nsp"));
            var st = vm.TrafficSim.StatsOf(server);
            log.Add($"server: packets={st.PacketsReceived}, requests={st.RequestsServed}, t={vm.TrafficSim.Now:0}");
            shots.Add(new("s19a-profiles", "Окно Profiles: трафик «Компьютер 1 → Почтовый сервер», выбран профиль E-mail (POP)",
                "К коммутатору А подключён сервер (LAN Servers → Server). Из группы Network and enterprise software → Server software на значок сервера перетащено ПО E-mail server (службы POP3 и SMTP). Инструментом Traffic выбран компьютер 1, затем сервер; в окне Profiles выбран профиль E-mail (POP) и нажата кнопка Assign.",
                "Самостоятельное задание №2 — сервер с почтовым ПО и трафиком"));
            shots.Add(new("s19-office-server", $"Скриншот 19 — сеть с сервером, почтовым ПО и настроенным трафиком (пунктир — поток E-mail (POP); под сервером — статистика после {vm.TrafficSim.Now:0} с моделирования)",
                $"Для проверки запущено моделирование (Run). За {vm.TrafficSim.Now:0} с модельного времени сервер обслужил {st.RequestsServed} запросов почтового клиента и получил {st.PacketsReceived} пакетов — трафик назначен правильно.", null));

            BuildReport(outDir, shots);
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

    // ---------------- Браузер оборудования ----------------

    private static void Collapse(IEnumerable<CatalogNode> nodes)
    {
        foreach (var n in nodes)
        {
            n.IsExpanded = false;
            n.IsSelected = false;
            Collapse(n.Children);
        }
    }

    internal static async Task ShowCatalog(MainWindow window, MainViewModel vm, string[] path, string? itemId)
    {
        Collapse(vm.CatalogTree);
        IEnumerable<CatalogNode> level = vm.CatalogTree;
        CatalogNode? node = null;
        foreach (var id in path)
        {
            node = level.First(n => n.Category?.Id == id);
            node.IsExpanded = true;
            level = node.Children;
        }
        CatalogNode? target = node;
        if (itemId != null) target = level.First(n => n.Device?.Id == itemId);
        if (target != null) target.IsSelected = true;
        await SelfTest.Settle();
        var item = FindItem(window.CatalogTree, target);
        item?.BringIntoView();
        await SelfTest.Settle();
    }

    private static TreeViewItem? FindItem(DependencyObject root, object? data)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is TreeViewItem tvi && tvi.DataContext == data) return tvi;
            var r = FindItem(c, data);
            if (r != null) return r;
        }
        return null;
    }

    internal static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) return t;
            var r = FindChild<T>(c);
            if (r != null) return r;
        }
        return null;
    }

    private static void ScrollTreeToEnd(MainWindow window) => FindChild<ScrollViewer>(window.CatalogTree)?.ScrollToEnd();

    // ---------------- Подписи на скриншоте 4 ----------------

    private static void Annotate(MainWindow window, string file)
    {
        var root = (FrameworkElement)window.Content;
        Rect Bounds(FrameworkElement e) => e.TransformToAncestor(root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
        var menu = (FrameworkElement)((DockPanel)root).Children[0];
        var areas = new (string Text, Rect Box, Point Label)[]
        {
            ("Главное меню", Bounds(menu), default),
            ("Браузер оборудования", Bounds(window.CatalogTree), default),
            ("Рабочее поле", Bounds(window.CanvasScroll), default),
        };
        areas[0].Label = new Point(Bounds(menu).Left + 560, Bounds(menu).Bottom + 60);
        areas[1].Label = new Point(Bounds(window.CatalogTree).Left + 60, Bounds(window.CatalogTree).Top + 280);
        areas[2].Label = new Point(Bounds(window.CanvasScroll).Left + 280, Bounds(window.CanvasScroll).Top + 230);

        BitmapSource src;
        using (var fs = File.OpenRead(file)) src = BitmapFrame.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        double sx = src.PixelWidth / root.ActualWidth, sy = src.PixelHeight / root.ActualHeight;
        var visual = new DrawingVisual();
        var red = new SolidColorBrush(Color.FromRgb(220, 30, 30));
        var pen = new Pen(red, 3);
        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(src, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            foreach (var (text, box, label) in areas)
            {
                dc.DrawRectangle(null, new Pen(red, 2.5) { DashStyle = DashStyles.Dash }, Rect.Inflate(box, -2, -2));
                var target = new Point(box.Left + box.Width / 2, box.Top + Math.Min(box.Height / 2, 14));
                if (text != "Главное меню") target = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2);
                var ft = new FormattedText(text, CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight, face, 20, Brushes.White, 1.0);
                var bg = new Rect(label.X - 8, label.Y - 4, ft.Width + 16, ft.Height + 8);
                var from = new Point(bg.Left + bg.Width / 2, bg.Top);
                if (text == "Главное меню") DrawArrow(dc, pen, red, from, new Point(target.X, box.Bottom - 2));
                dc.DrawRoundedRectangle(red, null, bg, 6, 6);
                dc.DrawText(ft, new Point(label.X, label.Y));
            }
        }
        var bmp = new RenderTargetBitmap(src.PixelWidth, src.PixelHeight, 96 * sx, 96 * sy, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var outFs = File.Create(file);
        enc.Save(outFs);
    }

    private static void DrawArrow(DrawingContext dc, Pen pen, Brush fill, Point from, Point to)
    {
        dc.DrawLine(pen, from, to);
        var dir = to - from;
        dir.Normalize();
        var side = new Vector(-dir.Y, dir.X) * 8;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(to, true, true);
            ctx.LineTo(to - dir * 16 + side, true, false);
            ctx.LineTo(to - dir * 16 - side, true, false);
        }
        dc.DrawGeometry(fill, null, geo);
    }

    // ---------------- Отчёт ----------------

    internal static ReportImage Load(string dir, string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(dir, name + ".png"));
        using var ms = new MemoryStream(bytes);
        var frame = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return new ReportImage("", bytes, frame.PixelWidth, frame.PixelHeight);
    }

    private static void BuildReport(string dir, List<Shot> shots)
    {
        var path = Path.Combine(dir, "Отчёт ПР2 (ЛР1).docx");
        using var w = new WordReport(path);
        w.Title("ОТЧЁТ");
        w.Centered("по лабораторной работе №1 (ПР2)", bold: true);
        w.Centered("«Знакомство с программой моделирования компьютерных сетей»", bold: true);
        w.Centered("(выполнено в NetSim — учебном аналоге NetCracker Professional)");
        w.Paragraph("");
        w.Centered("Выполнил(а): ____________________, группа ________");
        w.Centered("Проверил(а): ____________________");
        w.Centered(DateTime.Now.ToString("dd.MM.yyyy"));
        w.PageBreak();

        w.Heading1("Цель работы");
        w.Paragraph("Освоение графического интерфейса программы моделирования сетей, знакомство с её главными приложениями, с элементами оборудования и общими принципами моделирования сети.");

        w.Heading1("Ход работы");
        foreach (var s in shots)
        {
            if (s.Section != null) w.Heading2(s.Section);
            if (s.TextBefore != null) w.Paragraph(s.TextBefore);
            var img = Load(dir, s.File) with { Caption = s.Caption };
            w.Image(img, "");
        }

        w.Heading1("Выводы");
        w.Paragraph("В ходе работы изучен интерфейс программы моделирования сетей: браузер оборудования, рабочее поле, главное меню, панель свойств и журнал сообщений. Рассмотрены основные группы оборудования (сегменты Generic LANs, концентраторы, коммутаторы, маршрутизаторы и мосты, рабочие станции, серверы, средства безопасности и др.), открыт и изучен готовый проект сети предприятия Techno, просмотрены свойства маршрутизатора Cisco 7000 и цвета линий для разных сред передачи.");
        w.Paragraph("Освоены основные приёмы построения сети: размещение устройств перетаскиванием из браузера оборудования, соединение устройств инструментом Link через окно Link Assistant с выбором спецификации, среды и длины кабеля, переименование устройств, установка серверного программного обеспечения перетаскиванием на значок сервера и назначение трафика инструментом Traffic через окно Profiles. Построены сеть из трёх компьютеров и коммутатора и сеть «Офис» с двумя коммутаторами, маршрутизатором и почтовым сервером; моделирование подтвердило, что почтовый трафик между компьютером и сервером назначен верно.");
    }
}
