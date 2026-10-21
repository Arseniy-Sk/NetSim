using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using NetSim.App.Controls;
using NetSim.App.Localization;
using NetSim.App.ViewModels;
using NetSim.Calculators;
using NetSim.Core.Model;
using NetSim.Core.Topology;

namespace NetSim.App.Views;

/// <summary>Расчёты по методичкам: ПР8 (нагрузка ЛВС, PDV), ПР10 (беспроводная сеть), ПР4/ПР5 (законы распределения).</summary>
public sealed class CalculatorsWindow : DialogBase
{
    private readonly MainViewModel _vm;

    public CalculatorsWindow(MainViewModel vm) : base(Loc.T("Calc_Title"), 1100, 760)
    {
        _vm = vm;
        ShowInTaskbar = true;
        var tabs = new TabControl { Margin = new Thickness(12, 12, 12, 0) };
        tabs.Items.Add(new TabItem { Header = Loc.T("Calc_TabLan"), Content = LanTab() });
        tabs.Items.Add(new TabItem { Header = Loc.T("Calc_TabPdv"), Content = PdvTab() });
        tabs.Items.Add(new TabItem { Header = Loc.T("Calc_TabWifi"), Content = WifiTab() });
        tabs.Items.Add(new TabItem { Header = Loc.T("Calc_TabRange"), Content = RangeTab() });
        tabs.Items.Add(new TabItem { Header = Loc.T("Calc_TabDist"), Content = DistTab() });
        SetBody(tabs);
        AddClose();
    }

    /// <summary>Разметка вкладки: слева форма, справа отчёт с кнопкой «Копировать».</summary>
    private static (Grid Grid, StackPanel Form, TextBox Report) Layout()
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var form = new StackPanel();
        grid.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 10, 0) });
        var report = new TextBox();
        report.SetResourceReference(StyleProperty, "ReportBox");
        var right = new DockPanel();
        var copy = new Button { Content = Loc.T("Calc_Copy"), HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 0, 4) };
        copy.Click += (_, _) => { if (!string.IsNullOrEmpty(report.Text)) Clipboard.SetText(report.Text); };
        DockPanel.SetDock(copy, Dock.Top);
        right.Children.Add(copy);
        right.Children.Add(report);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return (grid, form, report);
    }

    private static TextBox Box(string text) => new() { Text = text };

    /// <summary>Сохранить результат в проекте — он попадёт в отчёт по практике.</summary>
    private void Remember(string tabKey, TextBox report)
    {
        if (string.IsNullOrWhiteSpace(report.Text)) return;
        _vm.Project.RememberCalculation(Loc.T(tabKey), report.Text);
        _vm.IsDirty = true;
    }

    private static Button CalcButton(Action a)
    {
        var b = new Button { Content = Loc.T("Calc_Calculate"), Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, FontWeight = FontWeights.SemiBold };
        b.Click += (_, _) => a();
        return b;
    }

    private static double Read(TextBox t, double fallback = 0) => TryNumber(t.Text, out var v) ? v : fallback;

    // ---------------- ПР8: нагрузка ЛВС ----------------

    private UIElement LanTab()
    {
        var (grid, form, report) = Layout();
        form.Children.Add(new TextBlock { Text = Loc.T("Calc_LanIntro"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
        var f = new FormGrid(220);
        var groups = f.Row(Loc.T("Calc_Groups"), Box("6, 6"));
        var k = f.Row(Loc.T("Calc_K"), Box("0,08"));
        var cmax = f.Row(Loc.T("Calc_Cmax"), new ComboBox());
        foreach (var v in new[] { "10", "100", "1000" }) cmax.Items.Add(v);
        cmax.SelectedIndex = 0;
        var g = f.Row(Loc.T("Calc_G"), Box("50"));
        var s = f.Row(Loc.T("Calc_S"), Box("50"));
        var l = f.Row(Loc.T("Calc_L"), Box("2"));
        var sDist = f.Row(Loc.T("Calc_SDist"), Box("350"));
        var maxLoad = f.Row(Loc.T("Calc_MaxLoad"), Box("0,35"));
        form.Children.Add(f);

        var fromProject = new Button { Content = Loc.T("Calc_FromProject"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        fromProject.Click += (_, _) =>
        {
            var (sizes, text) = AnalyzeCollisionDomains();
            if (sizes.Count > 0) groups.Text = string.Join(", ", sizes);
            report.Text = text;
        };
        form.Children.Add(fromProject);
        form.Children.Add(CalcButton(() =>
        {
            var sizes = groups.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var n) ? n : 0).Where(n => n > 0).ToArray();
            if (sizes.Length == 0) { report.Text = Loc.T("Calc_BadGroups"); return; }
            var res = LanLoadCalculator.Calculate(new LanLoadInput
            {
                GroupSizes = sizes, K = Read(k), CmaxMbps = double.Parse((string)cmax.SelectedItem), InterGroupPercent = Read(g), ServerPercent = Read(s),
                DistanceInGroupM = Read(l), DistanceBetweenGroupsM = Read(sDist), MaxLoad = Read(maxLoad, 0.35),
            }, Loc.I.Lang);
            report.Text = res.Report.ToText(); Remember("Calc_TabLan", report);
        }));
        return grid;
    }

    /// <summary>Домены коллизий текущего проекта: сколько станций в каждом общем сегменте (хаб, шина, кольцо, радио).</summary>
    private (List<int> Sizes, string Text) AnalyzeCollisionDomains()
    {
        var graph = new NetworkGraph(_vm.Project, _vm.Catalog);
        var sizes = new List<int>();
        var lines = new List<string> { Loc.T("Calc_DomainsHeader") };
        foreach (var ch in graph.Channels.Where(c => c.IsShared))
        {
            var stations = ch.Links.SelectMany(x => new[] { x.AId, x.BId }).Distinct()
                .Select(id => graph.Devices[id]).Where(d => d.Kind.IsEndNode()).ToList();
            sizes.Add(stations.Count);
            lines.Add($"• {ch.DisplayName}: {stations.Count} {Loc.T("Calc_Stations")}, {NetSim.Simulation.Traffic.StatKeys.FormatBits(ch.BandwidthBps)}");
            lines.Add("    " + string.Join(", ", stations.Select(d => d.Name)));
        }
        if (sizes.Count == 0) lines.Add(Loc.T("Calc_NoDomains"));
        return (sizes, string.Join("\n", lines));
    }

    // ---------------- ПР8: PDV ----------------

    public sealed class PdvRow
    {
        public string TypeId { get; set; } = "10base-t";
        public double LengthM { get; set; } = 3;
    }

    private UIElement PdvTab()
    {
        var (grid, form, report) = Layout();
        form.Children.Add(new TextBlock { Text = Loc.T("Calc_PdvIntro"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
        var rows = new ObservableCollection<PdvRow> { new(), new() };
        var dg = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, Height = 200, Margin = new Thickness(0, 8, 0, 0) };
        dg.Columns.Add(new DataGridComboBoxColumn
        {
            Header = Loc.T("Calc_SegType"), ItemsSource = PdvCalculator.Types, DisplayMemberPath = nameof(PdvSegmentType.Name), SelectedValuePath = nameof(PdvSegmentType.Id),
            SelectedValueBinding = new Binding(nameof(PdvRow.TypeId)), Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        dg.Columns.Add(new DataGridTextColumn { Header = Loc.T("Calc_SegLength"), Binding = new Binding(nameof(PdvRow.LengthM)), Width = 110 });
        form.Children.Add(dg);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var add = new Button { Content = Loc.T("Calc_AddSegment"), Padding = new Thickness(8, 2, 8, 2) };
        add.Click += (_, _) => rows.Insert(Math.Max(1, rows.Count - 1), new PdvRow { TypeId = "10base-fl", LengthM = 500 });
        var remove = new Button { Content = Loc.T("Calc_RemoveSegment"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
        remove.Click += (_, _) => { if (dg.SelectedItem is PdvRow r && rows.Count > 2) rows.Remove(r); };
        bar.Children.Add(add);
        bar.Children.Add(remove);
        form.Children.Add(bar);
        form.Children.Add(CalcButton(() =>
        {
            dg.CommitEdit();
            var res = PdvCalculator.Calculate(rows.Select(r => new PdvSegment(r.TypeId, r.LengthM)).ToList(), Loc.I.Lang);
            report.Text = res.Report.ToText(); Remember("Calc_TabPdv", report);
        }));

        form.Children.Add(new TextBlock { Text = "Fast Ethernet (100 Mbit/s)", Style = (Style)FindResource("SectionHeader"), Margin = new Thickness(0, 18, 0, 4) });
        var f = new FormGrid(220);
        var dte = f.Row(Loc.T("Calc_FeDte"), new ComboBox());
        dte.Items.Add("100Base-TX / FX");
        dte.Items.Add("100Base-T4");
        dte.SelectedIndex = 0;
        var c1 = f.Row(Loc.T("Calc_FeClass1"), Box("0"));
        var c2 = f.Row(Loc.T("Calc_FeClass2"), Box("1"));
        var copper = f.Row(Loc.T("Calc_FeCopper"), Box("200"));
        var cat5 = f.Row(Loc.T("Calc_FeCat5"), new CheckBox { IsChecked = true });
        var fiber = f.Row(Loc.T("Calc_FeFiber"), Box("0"));
        form.Children.Add(f);
        var feBtn = CalcButton(() =>
        {
            var r = PdvCalculator.CalculateFastEthernet(dte.SelectedIndex == 1 ? "t4" : "tx", (int)Read(c1), (int)Read(c2), Read(copper), cat5.IsChecked == true, Read(fiber), Loc.I.Lang);
            report.Text = r.ToText(); Remember("Calc_TabPdvFe", report);
        });
        form.Children.Add(feBtn);
        return grid;
    }

    // ---------------- ПР10: пропускная способность ----------------

    private UIElement WifiTab()
    {
        var (grid, form, report) = Layout();
        form.Children.Add(new TextBlock { Text = Loc.T("Calc_WifiIntro"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
        var f = new FormGrid(240);
        var k = f.Row(Loc.T("Calc_WifiK"), Box("20"));
        var ci = f.Row(Loc.T("Calc_WifiCi"), Box("2"));
        var cs = f.Row(Loc.T("Calc_WifiCs"), Box("6"));
        var cm = f.Row(Loc.T("Calc_WifiCm"), Box("6"));
        var h = f.Row(Loc.T("Calc_WifiH"), new ComboBox());
        h.Items.Add(Loc.T("Calc_WifiH1"));
        h.Items.Add(Loc.T("Calc_WifiH2"));
        h.SelectedIndex = 0;
        var std = f.Row(Loc.T("Calc_WifiStd"), new ComboBox());
        foreach (var s in WirelessCalculator.Standards) std.Items.Add(new ComboBoxItem { Content = $"{s.Name} ({s.NominalMbps} Mbit/s, {s.Band} GHz)", Tag = s.Id });
        std.SelectedIndex = 2;
        var ap = f.Row(Loc.T("Calc_WifiAp"), Box("0"));
        var clients = f.Row(Loc.T("Calc_WifiClients"), Box("30"));
        var inet = f.Row(Loc.T("Calc_WifiInternet"), Box("100"));
        var vi = f.Row(Loc.T("Calc_WifiVi"), Box("2"));
        var inetK = f.Row(Loc.T("Calc_WifiInetK"), Box("0"));
        form.Children.Add(f);
        form.Children.Add(CalcButton(() =>
        {
            var res = WirelessCalculator.Capacity(new WirelessCapacityInput
            {
                Stations = (int)Read(k, 1), CiMbps = Read(ci), CsMbps = Read(cs), CmMbps = Read(cm), H = h.SelectedIndex + 1,
                Standard = (string)((ComboBoxItem)std.SelectedItem).Tag, ApThroughputMbps = Read(ap), MaxClientsPerAp = (int)Read(clients, 30),
                InternetMbps = Read(inet), ViMbps = Read(vi), InternetStations = (int)Read(inetK),
            }, Loc.I.Lang);
            report.Text = res.Report.ToText(); Remember("Calc_TabWifi", report);
        }));
        return grid;
    }

    // ---------------- ПР10: дальность связи ----------------

    private UIElement RangeTab()
    {
        var (grid, form, report) = Layout();
        form.Children.Add(new TextBlock { Text = Loc.T("Calc_RangeIntro"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
        var f = new FormGrid(240);
        var pt = f.Row(Loc.T("Calc_Pt"), Box("20"));
        var gt = f.Row(Loc.T("Calc_Gt"), Box("5"));
        var gr = f.Row(Loc.T("Calc_Gr"), Box("2"));
        var speed = f.Row(Loc.T("Calc_Speed"), new ComboBox());
        foreach (var (mbps, dbm) in WirelessCalculator.Sensitivity) speed.Items.Add(new ComboBoxItem { Content = $"{mbps} Mbit/s → {dbm} dBm", Tag = dbm });
        speed.SelectedIndex = 1;
        var lt = f.Row(Loc.T("Calc_Lt"), Box("1"));
        var lr = f.Row(Loc.T("Calc_Lr"), Box("1"));
        var som = f.Row("SOM, dB", Box("10"));
        var channel = f.Row(Loc.T("Calc_Channel"), new ComboBox());
        for (int c = 1; c <= 13; c++) channel.Items.Add(new ComboBoxItem { Content = $"2.4 GHz: {c} ({WirelessCalculator.ChannelFrequency(c)} MHz)", Tag = WirelessCalculator.ChannelFrequency(c) });
        foreach (var c in new[] { 36, 40, 44, 48, 52, 56, 60, 64, 100, 104, 108, 112, 116, 120, 124, 128, 132, 136, 140, 149, 153, 157, 161, 165 })
            channel.Items.Add(new ComboBoxItem { Content = $"5 GHz: {c} ({WirelessCalculator.ChannelFrequency(c)} MHz)", Tag = WirelessCalculator.ChannelFrequency(c) });
        channel.SelectedIndex = 5;
        var walls = f.Row(Loc.T("Calc_Walls"), Box("0"));
        var wallLoss = f.Row(Loc.T("Calc_WallLoss"), Box("6"));
        var check = f.Row(Loc.T("Calc_CheckDistance"), Box("0"));
        form.Children.Add(f);
        form.Children.Add(CalcButton(() =>
        {
            var res = WirelessCalculator.Range(new RadioRangeInput
            {
                PtDbm = Read(pt), GtDbi = Read(gt), GrDbi = Read(gr), PminDbm = (double)((ComboBoxItem)speed.SelectedItem).Tag,
                LtDb = Read(lt), LrDb = Read(lr), SomDb = Read(som, 10), FrequencyMhz = (double)((ComboBoxItem)channel.SelectedItem).Tag,
                Walls = (int)Read(walls), WallLossDb = Read(wallLoss, 6), CheckDistanceM = Read(check),
            }, Loc.I.Lang);
            report.Text = res.Report.ToText(); Remember("Calc_TabRange", report);
        }));
        return grid;
    }

    // ---------------- ПР4/ПР5: законы распределения ----------------

    private UIElement DistTab()
    {
        var (grid, form, report) = Layout();
        form.Children.Add(new TextBlock { Text = Loc.T("Calc_DistIntro"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });
        var d = Distribution.Normal(2048, 512);
        var editor = new DistributionEditor(d, Loc.T("Calc_DistValue"), "");
        form.Children.Add(editor);
        form.Children.Add(CalcButton(() =>
        {
            editor.Apply();
            var r = DistributionCalculator.Explain(d, Loc.I.Lang);
            var (min, max, _, mean, std) = DistributionCalculator.Histogram(d, 20000);
            report.Text = r.ToText() + "\n\n" + Loc.F("Calc_DistSample", Num(mean), Num(std), Num(min), Num(max)); Remember("Calc_TabDist", report);
        }));
        return grid;
    }
}
