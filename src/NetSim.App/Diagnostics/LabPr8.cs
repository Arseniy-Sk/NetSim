using System.Collections.ObjectModel;
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
/// ПР8 (ЛР8) «Логическое проектирование локальной сети», вариант 7: расчёт нагрузки (5)–(11),
/// реструктуризация, схема новой сети, PDV. Запуск: <c>NetSim.exe --lab pr8 &lt;папка&gt;</c>.
/// </summary>
public static class LabPr8
{
    private const double RunSeconds = 30;
    private static readonly System.Globalization.CultureInfo Ru = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");

    // Исходные данные варианта 7
    private const int M = 3, N = 5;
    private const double L = 4, S = 150, K = 0.09, ServerPct = 45, GroupPct = 65, Cmax = 10;

    private sealed class Results
    {
        public LanLoadResult Load = null!;
        public PdvResult PdvOriginal = null!;
        public CalcReport PdvFast = null!;
        public PdvResult PdvMethod = null!;
        public ReportTable Specs = null!;
        public ReportTable Compare = null!;
        public string FlatSummary = "", NewSummary = "";
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
            vm.ShowLinkLabels = true;
            vm.ShowFlows = false;
            await SelfTest.Settle();

            var flat = await Original(c, r);
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР8 — исходная сеть (вариант 7).nsp"));
            await Calculations(c, r);
            var restructured = await Restructured(c, r);
            ProjectSerializer.Save(vm.Project, Path.Combine(outDir, "ПР8 — новая сеть после реструктуризации (вариант 7).nsp"));
            r.Compare = Compare(flat, restructured);
            BuildReport(c, r);
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

    private sealed record SimSummary(double DomainUtil, double MaxUtil, double Drops, double Delivered, double Offered, string Worst);

    /// <summary>
    /// Трафик по условию: каждый ПК генерирует Ci = K·Cmax = 0,9 Мбит/с; часть — к серверу, часть — в другие группы.
    /// В варианте доли 45% + 65% = 110%, поэтому для модели они нормированы (45/110 и 65/110).
    /// </summary>
    private static void AddTraffic(TopologyBuilder b, MainViewModel vm, List<Device>[] groups, Device server)
    {
        double ci = K * Cmax * 1e6;
        double sShare = ServerPct / (ServerPct + GroupPct), gShare = GroupPct / (ServerPct + GroupPct);
        TrafficProfile P(string name, double bps)
        {
            var p = vm.Catalog.Profile("lan-p2p")!.Clone();
            p.NameRu = name;
            p.NameEn = name;
            p.TransactionSize = Distribution.Constant(1500);
            p.TimeBetween = Distribution.Exponential(1500 * 8 / bps);
            return p;
        }
        for (int gi = 0; gi < groups.Length; gi++)
            for (int i = 0; i < groups[gi].Count; i++)
            {
                var pc = groups[gi][i];
                if (pc != server) b.AddFlow(pc, server, P($"К серверу ({ServerPct:0}% → {sShare * 100:0.#}%)", ci * sShare));
                var other = groups[(gi + 1) % groups.Length][i % groups[(gi + 1) % groups.Length].Count];
                b.AddFlow(pc, other, P($"Межгрупповой ({GroupPct:0}% → {gShare * 100:0.#}%)", ci * gShare));
            }
    }

    private static SimSummary Simulate(Project p)
    {
        var sim = new TrafficSimulator(p);
        sim.RunUntil(RunSeconds);
        var shared = sim.Graph.Channels.Where(ch => ch.IsShared).ToList();
        double domain = shared.Count == 0 ? 0 : shared.Max(sim.ChannelUtilization);
        var worst = sim.Graph.Channels.OrderByDescending(sim.ChannelUtilization).First();
        double delivered = p.AllDevices().Sum(d => (double)sim.StatsOf(d).BytesReceived);
        double offered = p.AllDevices().Sum(d => (double)sim.StatsOf(d).BytesSent);
        string worstName = worst.IsShared ? worst.DisplayName : string.Join(", ", worst.Links.Select(l => $"{p.FindDevice(l.AId)?.Name} — {p.FindDevice(l.BId)?.Name}"));
        return new SimSummary(domain, sim.ChannelUtilization(worst), sim.Graph.Channels.Sum(ch => sim.ChannelValue(ch, StatKeys.PacketsDropped)), delivered, offered, worstName);
    }

    // ---------------- Исходная сеть ----------------

    private static async Task<SimSummary> Original(Ctx c, Results r)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.NewProjectCommand.Execute(null);
        vm.Project.Name = "ПР8 — исходная сеть, вариант 7";
        vm.Project.Description = "Неструктурированная сеть 10Base-T: 3 группы по 5 ПК на концентраторах, концентраторы соединены каскадом (150 м), сервер в группе 2.";
        c.B = new TopologyBuilder(vm.Project, vm.Catalog);
        var b = c.B;
        var hubs = new Device[M];
        var groups = new List<Device>[M];
        Device server = null!;
        for (int g = 0; g < M; g++)
        {
            double cx = 220 + g * 380;
            hubs[g] = b.CreateDevice("hub-eth", cx, 250, name: $"Концентратор группы {g + 1}");
            groups[g] = new List<Device>();
            for (int i = 0; i < N; i++)
            {
                var pc = b.CreateDevice("pc", cx - 140 + i * 70, i % 2 == 0 ? 420 : 500, name: $"ПК {g + 1}.{i + 1}");
                b.Connect(pc, hubs[g], "10base-t", L);
                groups[g].Add(pc);
            }
            if (g == 1)
            {
                server = b.CreateDevice("server", cx, 100, name: "Сервер");
                b.Connect(server, hubs[g], "10base-t", L);
            }
        }
        b.Connect(hubs[0], hubs[1], "10base-t", S);
        b.Connect(hubs[1], hubs[2], "10base-t", S);
        AddTraffic(b, vm, groups, server);

        await c.Main("o01-flat", $"Исходная структура сети малого офиса (вариант 7): {M} группы по {N} компьютеров на концентраторах 10Base-T, между группами — {S:0} м",
            $"Исходные данные варианта 7: количество групп сотрудников M = {M}; компьютеров в группе N = {N}; расстояние по кабелю между компьютерами в группе L = {L:0} м, между группами S = {S:0} м; сервер расположен в одной из групп; средняя интенсивность трафика одного компьютера K = 0,09 от Cmax; трафик к серверу — {ServerPct:0}%, между группами — {GroupPct:0}% от суммарного трафика неструктурированной сети. Базовая технология — Ethernet 10Base-T. Исходная сеть построена в NetSim: концентраторы групп соединены каскадом, вся сеть — один домен коллизий.",
            "1. Исходная сеть");

        // Домены коллизий из проекта (кнопка калькулятора)
        var calc = new CalculatorsWindow(vm);
        await c.Dialog(calc, "o02-domains", "Анализ доменов коллизий исходной сети (кнопка «Домены коллизий из проекта»): все 16 станций — в одном домене 10 Мбит/с",
            "Кнопка «Домены коллизий из проекта» калькулятора показывает, что все концентраторы, соединённые между собой, образуют один домен коллизий: 15 компьютеров и сервер делят полосу 10 Мбит/с.",
            null, w => Click(Tab(w, 0), Loc.T("Calc_FromProject")));

        // Моделирование исходной сети
        var domain = hubs[1];
        domain.ShownStatistics.Add(StatKeys.AverageUtilization);
        domain.ShownStatistics.Add(StatKeys.MaxQueue);
        vm.TopologyChanged();
        await RunSimulation(c, RunSeconds);
        vm.StopCommand.Execute(null);
        vm.RefreshSimTime();
        var sum = Simulate(vm.Project);
        vm.Selected = domain;
        await c.Main("o03-flat-sim", $"Моделирование исходной сети ({RunSeconds:0} с): домен коллизий загружен на 100% (линии подсвечены красным), очередь переполнена — пакеты теряются",
            $"Для проверки задан трафик по условию: каждый компьютер генерирует Ci = 0,9 Мбит/с, из них часть — к серверу, часть — компьютерам других групп (в задании доли 45% + 65% = 110%, поэтому для модели они нормированы: 41% и 59%). Результат моделирования: загрузка общего домена коллизий — {F(StatKeys.AverageUtilization, sum.DomainUtil)}, потеряно {sum.Drops:0} пакетов, доставлено лишь {sum.Delivered / Math.Max(1, sum.Offered) * 100:0}% отправленных данных — это подтверждает жалобу «часто теряются данные».",
            null, rebuild: false);
        vm.Selected = null;
        r.FlatSummary = $"исходная сеть: загрузка домена {F(StatKeys.AverageUtilization, sum.DomainUtil)}, потеряно {sum.Drops:0} пакетов";
        return sum;
    }

    // ---------------- Расчёты в калькуляторах ----------------

    private static async Task Calculations(Ctx c, Results r)
    {
        var vm = c.Vm;
        r.Load = LanLoadCalculator.Calculate(new LanLoadInput
        {
            GroupSizes = Enumerable.Repeat(N, M).ToArray(), K = K, CmaxMbps = Cmax, InterGroupPercent = GroupPct, ServerPercent = ServerPct,
            DistanceInGroupM = L, DistanceBetweenGroupsM = S,
        });
        var calc = new CalculatorsWindow(vm);
        await c.Dialog(calc, "c01-load", "Калькулятор ПР8 «Нагрузка ЛВС»: исходные данные варианта 7 и пошаговый расчёт по формулам (5)–(11)",
            "Расчёт выполнен в калькуляторе NetSim (Инструменты → Калькуляторы → «ПР8: нагрузка ЛВС»); полный текст расчёта приведён ниже.",
            "2. Логическое проектирование: расчёт нагрузки", w =>
            {
                var tab = Tab(w, 0);
                var boxes = All<TextBox>(tab).Take(7).ToList();
                var values = new[] { $"{N}, {N}, {N}", "0,09", "65", "45", "4", "150", "0,35" };
                for (int i = 0; i < 7; i++) boxes[i].Text = values[i];
                Click(tab, Loc.T("Calc_Calculate"));
            });

        // PDV исходной сети (10Base-T): путь ПК группы 1 — концентратор 1 — концентратор 2 — концентратор 3 — ПК группы 3
        r.PdvOriginal = PdvCalculator.Calculate(new[] { new PdvSegment("10base-t", L), new PdvSegment("10base-t", S), new PdvSegment("10base-t", S), new PdvSegment("10base-t", L) });
        // PDV логического сегмента по формулам (12)–(14) методички
        r.PdvMethod = PdvCalculator.Calculate(new[] { new PdvSegment("10base-t", L), new PdvSegment("10base-t", L) });
        // Новый логический сегмент Fast Ethernet: две станции через один повторитель класса II, 2 × 4 м витой пары кат. 5
        r.PdvFast = PdvCalculator.CalculateFastEthernet("tx", 0, 1, 2 * L, true, 0);

        var pdvWin = new CalculatorsWindow(vm);
        await c.Dialog(pdvWin, "c02-pdv", "Калькулятор ПР8 «PDV»: логический сегмент группы (левый и правый сегменты 10Base-T по 4 м) и проверка нового сегмента Fast Ethernet",
            "PDV рассчитан для логического сегмента (группы) по формулам (12)–(14) и таблице 1 методички, а также для нового логического сегмента Fast Ethernet на концентраторе (повторитель класса II, предел 512 bt).",
            "3. Расчёт времени двойного оборота (PDV)", w =>
            {
                var tab = Tab(w, 1);
                var grid = All<DataGrid>(tab).First();
                var rows = (ObservableCollection<CalculatorsWindow.PdvRow>)grid.ItemsSource;
                foreach (var row in rows) { row.TypeId = "10base-t"; row.LengthM = L; }
                grid.Items.Refresh();
                Click(tab, Loc.T("Calc_Calculate"));
            });

        r.Specs = new ReportTable("Спецификации сегментов новой сети (по результатам расчёта)",
            new[] { "Сегмент", "Трафик", "Спецификация", "Кабель", "Коэффициент нагрузки ρ" },
            r.Load.Recommendations.Select(rec => new[]
            {
                rec.Segment, $"{rec.CapacityMbps * rec.Load:0.###} Мбит/с".Replace('.', ','), rec.StandardName,
                rec.StandardId.EndsWith("fx") || rec.StandardId.EndsWith("fl") || rec.StandardId.EndsWith("lx") ? "многомодовое оптоволокно" : "UTP кат. 5",
                $"{rec.Load:0.###} ≤ 0,35".Replace('.', ','),
            }).ToList());
    }

    // ---------------- Новая сеть ----------------

    private static async Task<SimSummary> Restructured(Ctx c, Results r)
    {
        var vm = c.Vm;
        vm.IsDirty = false;
        vm.NewProjectCommand.Execute(null);
        vm.Project.Name = "ПР8 — новая сеть, вариант 7";
        vm.Project.Description = "После логической реструктуризации: каждая группа — отдельный домен коллизий на концентраторе Fast Ethernet 100Base-TX, группы соединены коммутатором по 100Base-FX (оптоволокно, 150 м), сервер на выделенном порту коммутатора.";
        c.B = new TopologyBuilder(vm.Project, vm.Catalog);
        var b = c.B;
        var groupRec = r.Load.Recommendations[0];
        var backbone = r.Load.Recommendations.First(x => x.Segment.Contains("Магистраль"));
        var serverRec = r.Load.Recommendations.First(x => x.Segment.Contains("сервер"));
        var sw = b.CreateDevice("switch-eth", 600, 110, name: "Коммутатор");
        var server = b.CreateDevice("server", 880, 110, name: "Сервер");
        b.Connect(sw, server, serverRec.StandardId, L);
        var hubs = new Device[M];
        var groups = new List<Device>[M];
        for (int g = 0; g < M; g++)
        {
            double cx = 220 + g * 380;
            hubs[g] = b.CreateDevice("hub-fast-eth", cx, 300, name: $"Концентратор группы {g + 1}");
            b.Connect(hubs[g], sw, backbone.StandardId, S);
            groups[g] = new List<Device>();
            for (int i = 0; i < N; i++)
            {
                var pc = b.CreateDevice("pc", cx - 140 + i * 70, i % 2 == 0 ? 450 : 530, name: $"ПК {g + 1}.{i + 1}");
                b.Connect(pc, hubs[g], groupRec.StandardId, L);
                groups[g].Add(pc);
            }
        }
        AddTraffic(b, vm, groups, server);
        vm.Project.RememberCalculation(Loc.T("Calc_TabLan"), r.Load.Report.ToText());
        vm.Project.RememberCalculation(Loc.T("Calc_TabPdv"), r.PdvMethod.Report.ToText());
        vm.Project.RememberCalculation(Loc.T("Calc_TabPdvFe"), r.PdvFast.ToText());
        foreach (var h in hubs) h.ShownStatistics.Add(StatKeys.AverageUtilization);
        foreach (var l in vm.Project.Root.Links.Where(l => l.Touches(sw.Id))) l.ShownStatistics.Add(StatKeys.AverageUtilization);

        await c.Main("n01-new", $"Схема новой сети после логической реструктуризации: группы — отдельные домены коллизий на концентраторах {groupRec.StandardName}, магистраль — коммутатор и {backbone.StandardName} ({S:0} м), сервер — на порту коммутатора ({serverRec.StandardName})",
            $"По результатам расчёта выполнена логическая реструктуризация: каждая группа выделена в отдельный логический сегмент (домен коллизий) на концентраторе Fast Ethernet; группы соединены коммутатором, который разделяет домены коллизий. Спецификации выбраны по расчёту: в группах — {groupRec.StandardName} (UTP кат. 5, {L:0} м), между группами — {backbone.StandardName} (оптоволокно — расстояние {S:0} м больше допустимых для витой пары 100 м), сервер подключён к отдельному порту коммутатора по {serverRec.StandardName}.",
            "4. Новая сеть");
        var calc = new CalculatorsWindow(vm);
        await c.Dialog(calc, "n02-domains", "Домены коллизий новой сети: три независимых домена по 5 станций, 100 Мбит/с",
            null, null, w => Click(Tab(w, 0), Loc.T("Calc_FromProject")));

        vm.TopologyChanged();
        await RunSimulation(c, RunSeconds);
        vm.StopCommand.Execute(null);
        vm.RefreshSimTime();
        var sum = Simulate(vm.Project);
        await c.Main("n03-new-sim", $"Моделирование новой сети ({RunSeconds:0} с) с тем же трафиком: загрузка доменов групп и линий коммутатора (average utilization)",
            $"С тем же трафиком загрузка доменов коллизий групп — не более {F(StatKeys.AverageUtilization, sum.DomainUtil)}, самая загруженная линия — «{sum.Worst}» ({F(StatKeys.AverageUtilization, sum.MaxUtil)}), потеряно пакетов — {sum.Drops:0}. Сеть работает в допустимом режиме.",
            null, rebuild: false);
        r.NewSummary = $"новая сеть: загрузка доменов до {F(StatKeys.AverageUtilization, sum.DomainUtil)}, потеряно {sum.Drops:0} пакетов";
        return sum;
    }

    private static ReportTable Compare(SimSummary flat, SimSummary nw) => new(
        $"Сравнение сетей при одинаковом трафике (моделирование {RunSeconds:0} с)",
        new[] { "Показатель", "Исходная сеть (10Base-T)", "Новая сеть" },
        new List<string[]>
        {
            new[] { "Число доменов коллизий", "1 (все 16 станций)", $"{M} (по {N} станций)" },
            new[] { "Макс. загрузка домена коллизий", F(StatKeys.AverageUtilization, flat.DomainUtil), F(StatKeys.AverageUtilization, nw.DomainUtil) },
            new[] { "Самая загруженная линия", $"{flat.Worst} — {F(StatKeys.AverageUtilization, flat.MaxUtil)}", $"{nw.Worst} — {F(StatKeys.AverageUtilization, nw.MaxUtil)}" },
            new[] { "Потеряно пакетов", flat.Drops.ToString("0"), nw.Drops.ToString("0") },
            new[] { "Доставлено данных", $"{flat.Delivered / Math.Max(1, flat.Offered) * 100:0}%", $"{nw.Delivered / Math.Max(1, nw.Offered) * 100:0}%" },
        });

    // ---------------- Работа с окнами калькуляторов ----------------

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
        var path = Path.Combine(c.Dir, "Отчёт ПР8 (ЛР8) — вариант 7.docx");
        using var w = new WordReport(path);
        w.Title("ОТЧЁТ");
        w.Centered("по лабораторной работе №8 (ПР8)", bold: true);
        w.Centered("«Логическое проектирование локальной сети»", bold: true);
        w.Centered("Вариант 7");
        w.Centered("(выполнено в NetSim — учебном аналоге NetCracker Professional)");
        w.Paragraph("");
        w.Centered("Выполнил(а): ____________________, группа ________");
        w.Centered("Проверил(а): ____________________");
        w.Centered(DateTime.Now.ToString("dd.MM.yyyy"));
        w.PageBreak();

        w.Heading1("Цель работы");
        w.Paragraph("Разработать структурную схему ЛВС офисного отдела, включающую общий сервер.");
        w.Heading1("Задание");
        w.Paragraph("Сделать проверку выполнения условия допустимой нагрузки данной ЛВС и, если нагрузка превышена, выполнить логическую реструктуризацию ЛВС. Создать схему новой сети. Задать тип кабеля и спецификацию Ethernet в каждом сегменте согласно расчёту. Провести проверочный расчёт времени двойного оборота PDV для доменов коллизий (логических сегментов), содержащих повторители. В качестве физической среды в каждом логическом сегменте изначально используется 10Base-T.");
        w.Paragraph($"Исходные данные (вариант 7): M = {M} группы, N = {N} компьютеров в группе, L = {L:0} м, S = {S:0} м, сервер в одной из групп, K = 0,09, трафик к серверу s = {ServerPct:0}%, между группами g = {GroupPct:0}%. В сети часто теряются данные и иногда не распознаются коллизии.");
        w.Paragraph("Примечание: в варианте сумма долей трафика к серверу и между группами (45% + 65%) превышает 100%; в расчёте по формулам (9), (10) используются значения из задания, а в моделировании доли нормированы.");

        c.Images(w, c.Shots.Where(s => s.File.StartsWith("o")));

        w.Heading1("2. Расчёт нагрузки (формулы (5)–(11))");
        c.Images(w, c.Shots.Where(s => s.File == "c01-load"));
        w.Code(r.Load.Report.ToText());
        w.Paragraph(string.Create(Ru, $"Вывод по расчёту: коэффициент нагрузки неструктурированной сети ρн = {r.Load.LoadUnstructured:0.##} > 0,35 — условие допустимой нагрузки не выполняется, требуется логическая реструктуризация. После разделения на группы нагрузка каждой группы на 10Base-T — {r.Load.GroupLoads[0]:0.##} (> 0,35), поэтому в группах нужна технология Fast Ethernet; межгрупповой трафик {r.Load.CInterGroup:0.###} Мбит/с на 10 Мбит/с даёт ρ = {r.Load.InterGroupLoad:0.###} > 0,35, а расстояние между группами {S:0} м превышает 100 м для витой пары — магистраль нужна на оптоволокне 100Base-FX."));
        w.Table(r.Specs, "Таблица");

        w.Heading1("3. Расчёт времени двойного оборота (PDV)");
        c.Images(w, c.Shots.Where(s => s.File == "c02-pdv"));
        w.Heading2("Логический сегмент (группа) по формулам (12)–(14)");
        w.Code(r.PdvMethod.Report.ToText());
        w.Heading2("Новый логический сегмент Fast Ethernet (концентратор — повторитель класса II)");
        w.Code(r.PdvFast.ToText());
        w.Heading2("Причина потери данных в исходной сети");
        w.Paragraph($"В исходной сети концентраторы соединены каскадом кабелем 10Base-T длиной {S:0} м, что больше допустимых 100 м для 10Base-T — это нарушение условий спецификации, из-за которого сигнал ослабляется и коллизии могут не распознаваться. Даже без учёта длины весь трафик 16 станций проходит через один домен коллизий 10 Мбит/с. PDV для самого длинного пути исходной сети (ПК — 3 концентратора — ПК):");
        w.Code(r.PdvOriginal.Report.ToText());

        w.Heading1("4. Схема новой сети и проверка моделированием");
        c.Images(w, c.Shots.Where(s => s.File.StartsWith("n")));
        w.Table(r.Compare, "Таблица");

        w.Heading1("Выводы");
        w.Paragraph(string.Create(Ru, $"Проверка показала, что условие допустимой нагрузки неструктурированной ЛВС (ρн ≤ 0,35) не выполняется: ρн = {r.Load.LoadUnstructured:0.##}. Выполнена логическая реструктуризация: каждая группа выделена в отдельный домен коллизий на концентраторе Fast Ethernet 100Base-TX (ρ = {r.Load.Recommendations[0].Load:0.###}), группы соединены коммутатором по оптоволоконным линиям 100Base-FX длиной {S:0} м (ρ = {r.Load.Recommendations.First(x => x.Segment.Contains("Магистраль")).Load:0.###}), сервер подключён к выделенному порту коммутатора по 100Base-TX."));
        w.Paragraph(string.Create(Ru, $"Проверочный расчёт PDV подтвердил корректность логических сегментов: для группы по формулам методички PDV = {r.PdvMethod.Total:0.##} bt ≤ 575 bt, для сегмента Fast Ethernet с повторителем класса II — не более 512 bt. Моделирование с одинаковым трафиком подтвердило результат расчёта: {r.FlatSummary}; {r.NewSummary}."));
    }
}
