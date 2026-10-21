using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using NetSim.App.Localization;
using NetSim.App.ViewModels;
using NetSim.Calculators;
using NetSim.Core.Model;

namespace NetSim.App.Views;

/// <summary>Мастер плана: два ряда кабинетов с коридором (как планы вариантов ПР10) или один ряд.</summary>
public sealed class PlanWizardWindow : DialogBase
{
    public PlanWizardWindow(FloorPlan plan) : base(Loc.T("Plan_WizardTitle"), 560, 480)
    {
        var body = new StackPanel { Margin = new Thickness(12) };
        body.Children.Add(new TextBlock { Text = Loc.T("Plan_WizardHint"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });
        var f = new FormGrid(240);
        var top = f.Row(Loc.T("Plan_TopRow"), new TextBox { Text = "7, 15, 20" });
        var bottom = f.Row(Loc.T("Plan_BottomRow"), new TextBox { Text = "7, 15, 20" });
        var depth = f.Row(Loc.T("Plan_Depth"), new TextBox { Text = "7" });
        var corridor = f.Row(Loc.T("Plan_Corridor"), new TextBox { Text = "3" });
        var scale = f.Row(Loc.T("Plan_Scale"), new TextBox { Text = Num(plan.PixelsPerMeter) });
        var prefix = f.Row(Loc.T("Plan_Prefix"), new TextBox { Text = Loc.T("Plan_RoomPrefix") });
        var replace = new CheckBox { Content = Loc.T("Plan_Replace"), IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
        f.Full(replace);
        body.Children.Add(f);
        SetBody(body);

        static List<double> Parse(string s) => s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => TryNumber(x, out var v) ? v : -1).ToList();

        AddOkCancel(() =>
        {
            var t = Parse(top.Text);
            var b = Parse(bottom.Text);
            if (t.Count == 0 || t.Any(x => x <= 0) || b.Any(x => x <= 0) || !TryNumber(depth.Text, out var d) || d <= 0
                || !TryNumber(corridor.Text, out var c) || c < 0 || !TryNumber(scale.Text, out var sc) || sc <= 0)
            {
                MessageBox.Show(this, Loc.T("Settings_Invalid"), "NetSim", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            if (replace.IsChecked == true) plan.Shapes.Clear();
            plan.PixelsPerMeter = sc;
            plan.IsVisible = true;
            plan.BuildCorridorPlan(t, b, d, c, 60, 60, prefix.Text.Length > 0 ? prefix.Text + " " : "");
            return true;
        });
    }
}

/// <summary>Масштаб плана и затухание в стенах.</summary>
public sealed class PlanSettingsWindow : DialogBase
{
    public PlanSettingsWindow(FloorPlan plan) : base(Loc.T("Plan_SettingsTitle"), 480, 300)
    {
        var f = new FormGrid(240) { Margin = new Thickness(12) };
        var scale = f.Row(Loc.T("Plan_Scale"), new TextBox { Text = Num(plan.PixelsPerMeter) });
        var loss = f.Row(Loc.T("Plan_WallLoss"), new TextBox { Text = Num(plan.WallLossDb) });
        var visible = f.Row(Loc.T("Plan_Visible"), new CheckBox { IsChecked = plan.IsVisible });
        f.Full(new TextBlock { Text = Loc.T("Plan_ScaleHint"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 8, 0, 0) });
        SetBody(f);
        AddOkCancel(() =>
        {
            if (!TryNumber(scale.Text, out var s) || s <= 0 || !TryNumber(loss.Text, out var l) || l < 0)
            {
                MessageBox.Show(this, Loc.T("Settings_Invalid"), "NetSim", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            plan.PixelsPerMeter = s;
            plan.WallLossDb = l;
            plan.IsVisible = visible.IsChecked == true;
            return true;
        });
    }
}

/// <summary>Анализ покрытия Wi-Fi по плану (ПР10): таблица станций и пошаговый отчёт.</summary>
public sealed class CoverageWindow : DialogBase
{
    private readonly MainViewModel _vm;
    private readonly DataGrid _grid = new() { AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, Height = 220 };
    private readonly TextBox _report = new();
    private CoverageResult? _result;

    public sealed record Row(string Ap, string Station, string Distance, int Walls, string Range, string Status);

    public CoverageWindow(MainViewModel vm) : base(Loc.T("Plan_CoverageTitle"), 900, 700)
    {
        _vm = vm;
        ShowInTaskbar = true;
        _report.SetResourceReference(StyleProperty, "ReportBox");
        void Col(string h, string path, double w) => _grid.Columns.Add(new DataGridTextColumn { Header = h, Binding = new Binding(path), Width = w });
        Col(Loc.T("Plan_ColAp"), nameof(Row.Ap), 140);
        Col(Loc.T("Plan_ColStation"), nameof(Row.Station), 160);
        Col(Loc.T("Plan_ColDistance"), nameof(Row.Distance), 110);
        Col(Loc.T("Plan_ColWalls"), nameof(Row.Walls), 80);
        Col(Loc.T("Plan_ColRange"), nameof(Row.Range), 130);
        Col(Loc.T("Plan_ColStatus"), nameof(Row.Status), 150);

        var body = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(_grid, Dock.Top);
        body.Children.Add(_grid);
        _report.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(_report);
        SetBody(body);
        AddButton(Loc.T("Plan_Refresh"), Fill);
        AddButton(Loc.T("Plan_ApplyLengths"), () =>
        {
            if (_result == null) return;
            _vm.Checkpoint();
            var n = WirelessCoverage.ApplyLengths(_result);
            _vm.TopologyChanged();
            _vm.Log(Loc.F("Log_LengthsApplied", n));
        });
        AddButton(Loc.T("Calc_Copy"), () => { if (_report.Text.Length > 0) Clipboard.SetText(_report.Text); });
        AddClose();
        Fill();
    }

    private void Fill()
    {
        _result = WirelessCoverage.Analyze(_vm.CurrentLevel, _vm.Catalog, Loc.I.Lang);
        string m = Loc.I.IsRu ? "м" : "m";
        _grid.ItemsSource = _result.Rows.OrderBy(r => r.AccessPoint.Name).ThenByDescending(r => r.DistanceM)
            .Select(r => new Row(r.AccessPoint.Name, r.Station.Name, $"{Num(r.DistanceM, "0.0")} {m}", r.Walls, $"{Num(r.RangeM, "0.0")} {m}",
                r.Ok ? Loc.T("Plan_StatusOk") : Loc.T("Plan_StatusFail")))
            .ToList();
        var text = _result.Report.ToText();
        if (_vm.CurrentLevel.Plan.Shapes.Count == 0) text = Loc.T("Plan_NoPlan") + "\n\n" + text;
        _report.Text = text;
    }
}
