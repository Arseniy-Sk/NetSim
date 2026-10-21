using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NetSim.App.Localization;
using NetSim.App.Services;
using NetSim.App.ViewModels;
using NetSim.Calculators;
using NetSim.Core.Model;

namespace NetSim.App.Views;

/// <summary>Параметры отчёта по практике и сохранение в Word.</summary>
public sealed class ReportWindow : DialogBase
{
    private readonly MainViewModel _vm;
    private readonly Func<Level, ReportImage?> _render;
    private readonly Dictionary<string, CheckBox> _sections = new();
    private readonly ComboBox _practice = new() { IsEditable = true };
    private readonly TextBox _author = new();
    private readonly TextBox _group = new();
    private readonly TextBox _variant = new();
    private readonly TextBox _teacher = new();

    private static readonly string[] Practices =
    {
        "Практическая работа №2 «Знакомство с NetCracker Professional»",
        "Практическая работа №3 «Проектирование топологии и спецификации вычислительной сети»",
        "Практическая работа №4 «Задание трафика вычислительной сети»",
        "Практическая работа №5 «Моделирование трафика сети с использованием законов распределения»",
        "Практическая работа №6 «Создание многоуровневых сетевых проектов»",
        "Практическая работа №8 «Логическое проектирование локальной сети»",
        "Практическая работа №10 «Методика проектирования беспроводной локальной сети»",
    };

    private static readonly (string Key, bool Default)[] SectionKeys =
    {
        ("Rep_SecTask", true), ("Rep_SecDiagrams", true), ("Rep_SecEquipment", true), ("Rep_SecLinks", true),
        ("Rep_SecServers", true), ("Rep_SecFlows", true), ("Rep_SecStats", true), ("Rep_SecCoverage", true),
        ("Rep_SecCalcs", true), ("Rep_SecIp", true), ("Rep_SecConclusion", true),
    };

    public ReportWindow(MainViewModel vm, Func<Level, ReportImage?> render) : base(Loc.T("Rep_Title"), 640, 660)
    {
        _vm = vm;
        _render = render;
        var body = new StackPanel { Margin = new Thickness(12) };
        body.Children.Add(new TextBlock { Text = Loc.T("Rep_Hint"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });
        var f = new FormGrid(170);
        foreach (var p in Practices) _practice.Items.Add(p);
        _practice.Text = GuessPractice();
        f.Row(Loc.T("Rep_Practice"), _practice);
        _author.Text = vm.Project.Author;
        f.Row(Loc.T("Rep_Author"), _author);
        f.Row(Loc.T("Rep_Group"), _group);
        f.Row(Loc.T("Rep_Variant"), _variant);
        f.Row(Loc.T("Rep_Teacher"), _teacher);
        body.Children.Add(f);
        body.Children.Add(new TextBlock { Text = Loc.T("Rep_Sections"), Style = (Style)FindResource("SectionHeader") });
        foreach (var (key, def) in SectionKeys)
        {
            var cb = new CheckBox { Content = Loc.T(key), IsChecked = def, Margin = new Thickness(0, 2, 0, 2) };
            _sections[key] = cb;
            body.Children.Add(cb);
        }
        if (vm.TrafficSim == null)
            body.Children.Add(new TextBlock { Text = Loc.T("Rep_NoSimWarning"), Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        SetBody(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        AddOkCancel(Save, Loc.T("Rep_Save"));
    }

    private string GuessPractice()
    {
        var p = _vm.Project;
        if (p.AllLevels().Any(l => l.Plan.Shapes.Count > 0) || p.AllDevices().Any(d => d.Kind == DeviceKind.AccessPoint)) return Practices[6];
        if (p.Calculations.Any(c => c.Title.Contains("8"))) return Practices[5];
        if (p.AllDevices().Any(d => d.IsContainer)) return Practices[4];
        if (p.Flows.Count > 0) return Practices[3];
        return Practices[1];
    }

    private bool On(string key) => _sections[key].IsChecked == true;

    private bool Save()
    {
        var dlg = new SaveFileDialog { Filter = "Word (*.docx)|*.docx", FileName = $"{(_author.Text.Length > 0 ? _author.Text + " - " : "")}{_vm.Project.Name}.docx" };
        if (dlg.ShowDialog(this) != true) return false;
        _vm.Project.Author = _author.Text.Trim();
        try
        {
            Build(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.F("Msg_SaveFailed", ex.Message), "NetSim", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        _vm.Log(Loc.F("Log_Exported", dlg.FileName));
        if (MessageBox.Show(this, Loc.T("Rep_Open"), "NetSim", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        return true;
    }

    /// <summary>Сформировать документ (используется и самопроверкой).</summary>
    public void Build(string path)
    {
        var p = _vm.Project;
        string fig = Loc.Pick("Рисунок", "Figure"), tab = Loc.Pick("Таблица", "Table");
        int section = 1;
        string H(string key) => $"{section++}. {Loc.T(key)}";

        using var w = new WordReport(path);
        w.Title(Loc.Pick("ОТЧЁТ", "REPORT"));
        w.Centered(_practice.Text, bold: true);
        w.Paragraph("");
        if (_author.Text.Length > 0) w.Centered(Loc.F("Rep_Done", _author.Text.Trim()) + (_group.Text.Length > 0 ? $", {_group.Text.Trim()}" : ""));
        if (_variant.Text.Length > 0) w.Centered(Loc.F("Rep_VariantLine", _variant.Text.Trim()));
        if (_teacher.Text.Length > 0) w.Centered(Loc.F("Rep_Checked", _teacher.Text.Trim()));
        w.Centered(DateTime.Now.ToString("dd.MM.yyyy"));
        w.PageBreak();

        if (On("Rep_SecTask"))
        {
            w.Heading1(H("Rep_SecTask"));
            w.Paragraph(Loc.F("Rep_ProjectLine", p.Name));
            if (!string.IsNullOrWhiteSpace(p.Description)) w.Paragraph(p.Description);
            w.Paragraph(Loc.F("Props_Counts", p.AllDevices().Count(), p.AllLinks().Count(), p.Flows.Count));
        }

        if (On("Rep_SecDiagrams"))
        {
            w.Heading1(H("Rep_SecDiagrams"));
            foreach (var level in p.AllLevels().Where(l => l.Devices.Count > 0 || l.Plan.Shapes.Count > 0))
            {
                var img = _render(level);
                if (img != null) w.Image(img, fig);
            }
        }

        if (On("Rep_SecEquipment")) { w.Heading1(H("Rep_SecEquipment")); w.Table(ReportTables.Equipment(p, _vm.Catalog), tab); }
        if (On("Rep_SecLinks") && p.AllLinks().Any()) { w.Heading1(H("Rep_SecLinks")); w.Table(ReportTables.Links(p, _vm.Catalog), tab); }

        var servers = ReportTables.Servers(p);
        if (On("Rep_SecServers") && servers.Rows.Count > 0) { w.Heading1(H("Rep_SecServers")); w.Table(servers, tab); }

        if (On("Rep_SecFlows") && p.Flows.Count > 0) { w.Heading1(H("Rep_SecFlows")); w.Table(ReportTables.Flows(p), tab); }

        if (On("Rep_SecStats") && _vm.TrafficSim is { } sim)
        {
            w.Heading1(H("Rep_SecStats"));
            w.Paragraph(Loc.F("Status_SimTime", sim.Now.ToString("0.00")) + $". Seed = {p.Simulation.Seed}.");
            w.Table(ReportTables.DeviceStatistics(sim), tab);
            w.Table(ReportTables.ChannelStatistics(sim, p, _vm.Catalog), tab);
        }

        if (On("Rep_SecCoverage"))
        {
            bool header = false;
            foreach (var level in p.AllLevels().Where(l => l.Devices.Any(d => d.Kind == DeviceKind.AccessPoint)))
            {
                var cov = WirelessCoverage.Analyze(level, _vm.Catalog, Loc.I.Lang);
                if (cov.Rows.Count == 0) continue;
                if (!header) { w.Heading1(H("Rep_SecCoverage")); header = true; }
                w.Table(ReportTables.Coverage(cov), tab);
                w.Code(cov.Report.ToText());
            }
        }

        if (On("Rep_SecCalcs") && p.Calculations.Count > 0)
        {
            w.Heading1(H("Rep_SecCalcs"));
            foreach (var c in p.Calculations)
            {
                w.Heading2(c.Title);
                w.Code(c.Text);
            }
        }

        var ip = ReportTables.Addresses(p);
        if (On("Rep_SecIp") && ip.Rows.Count > 0) { w.Heading1(H("Rep_SecIp")); w.Table(ip, tab); }

        if (On("Rep_SecConclusion"))
        {
            w.Heading1(H("Rep_SecConclusion"));
            w.Paragraph(Loc.T("Rep_ConclusionHint"));
        }
    }
}
