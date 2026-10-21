using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using NetSim.App.Localization;
using NetSim.App.Services;
using NetSim.App.ViewModels;

namespace NetSim.App.Views;

/// <summary>Сводная таблица статистики (как в отчёте ПР4/ПР5): серверы, станции, сегменты и линии.</summary>
public sealed class StatisticsReportWindow : DialogBase
{
    private readonly MainViewModel _vm;
    private readonly DataGrid _devices = Grid();
    private readonly DataGrid _channels = Grid();
    private readonly TextBlock _time = new() { Margin = new Thickness(12, 10, 12, 0), FontWeight = FontWeights.SemiBold };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ReportTable? _deviceTable, _channelTable;

    private static DataGrid Grid() => new()
    {
        AutoGenerateColumns = false, IsReadOnly = true, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(0, 4, 0, 0),
    };

    public StatisticsReportWindow(MainViewModel vm) : base(Loc.T("Report_Title"), 1100, 700)
    {
        _vm = vm;
        ShowInTaskbar = true;
        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(12, 4, 12, 0) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var h1 = new TextBlock { Text = Loc.T("Report_Devices"), Style = (Style)FindResource("SectionHeader") };
        var h2 = new TextBlock { Text = Loc.T("Report_Channels"), Style = (Style)FindResource("SectionHeader") };
        System.Windows.Controls.Grid.SetRow(_devices, 1);
        System.Windows.Controls.Grid.SetRow(h2, 2);
        System.Windows.Controls.Grid.SetRow(_channels, 3);
        grid.Children.Add(h1);
        grid.Children.Add(_devices);
        grid.Children.Add(h2);
        grid.Children.Add(_channels);

        DockPanel.SetDock(_time, Dock.Top);
        Root.Children.Add(_time);
        SetBody(grid);
        AddButton(Loc.T("Report_Csv"), ExportCsv);
        AddButton(Loc.T("Report_Copy"), () => Clipboard.SetText(BuildCsv('\t')));
        AddClose();
        _timer.Tick += (_, _) => Fill();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Fill();
    }

    /// <summary>Показать таблицу в DataGrid: столбцы привязаны к элементам массива строки.</summary>
    private static void Show(DataGrid g, ReportTable t)
    {
        if (g.Columns.Count != t.Headers.Length)
        {
            g.Columns.Clear();
            for (int i = 0; i < t.Headers.Length; i++)
                g.Columns.Add(new DataGridTextColumn { Header = t.Headers[i], Binding = new Binding($"[{i}]"), Width = i == 0 ? 200 : DataGridLength.Auto });
        }
        g.ItemsSource = t.Rows;
    }

    private void Fill()
    {
        var sim = _vm.TrafficSim;
        _time.Text = sim == null ? Loc.T("Report_NoSim") : Loc.F("Status_SimTime", sim.Now.ToString("0.00"));
        if (sim == null) { _devices.ItemsSource = null; _channels.ItemsSource = null; return; }
        _deviceTable = ReportTables.DeviceStatistics(sim);
        _channelTable = ReportTables.ChannelStatistics(sim, _vm.Project, _vm.Catalog);
        Show(_devices, _deviceTable);
        Show(_channels, _channelTable);
    }

    private string BuildCsv(char sep)
    {
        var sb = new StringBuilder();
        string Q(string s) => s.Contains(sep) || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        sb.AppendLine(_time.Text);
        foreach (var t in new[] { _deviceTable, _channelTable })
        {
            if (t == null) continue;
            sb.AppendLine(t.Title);
            sb.AppendLine(string.Join(sep, t.Headers.Select(Q)));
            foreach (var r in t.Rows) sb.AppendLine(string.Join(sep, r.Select(Q)));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private void ExportCsv()
    {
        var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"{_vm.Project.Name}-statistics.csv" };
        if (dlg.ShowDialog(this) != true) return;
        // Точка с запятой и UTF-8 с BOM — чтобы русский Excel открыл файл корректно.
        File.WriteAllText(dlg.FileName, BuildCsv(';'), new UTF8Encoding(true));
        _vm.Log(Loc.F("Log_Exported", dlg.FileName));
    }
}
