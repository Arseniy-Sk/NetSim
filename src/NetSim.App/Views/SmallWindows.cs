using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NetSim.App.Localization;
using NetSim.App.ViewModels;
using NetSim.Core.Model;

namespace NetSim.App.Views;

/// <summary>View → Media Colors: цвета линий для каждого типа среды.</summary>
public sealed class MediaColorsWindow : DialogBase
{
    private static readonly string[] Palette =
    {
        "#1E5BB8", "#3A7BD5", "#0B3D91", "#7B3FC4", "#2E9E5B", "#156B37", "#E07A10", "#C04A00",
        "#8C8C8C", "#9B6B2F", "#C0283C", "#5A5A9A", "#000000", "#D81B60", "#00897B", "#F9A825",
    };

    public MediaColorsWindow(MainViewModel vm) : base("Media Colors", 520, 560)
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = Loc.T("Media_Hint"), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var m in vm.Catalog.MediaTypes)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var sample = new Line { X1 = 0, Y1 = 8, X2 = 60, Y2 = 8, StrokeThickness = 3, Width = 64, Height = 16, Margin = new Thickness(0, 0, 10, 0) };
            void Paint() { sample.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(m.Color)); sample.StrokeDashArray = m.Dashed ? new DoubleCollection { 3, 2 } : null; }
            Paint();
            var combo = new ComboBox { Width = 120 };
            foreach (var c in Palette.Append(m.Color).Distinct())
            {
                var item = new ComboBoxItem { Content = new Rectangle { Width = 80, Height = 12, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c)) }, Tag = c };
                combo.Items.Add(item);
                if (c == m.Color) combo.SelectedItem = item;
            }
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: string c })
                {
                    m.Color = c;
                    vm.Settings.MediaColors[m.Id] = c;
                    vm.Settings.Save();
                    Paint();
                }
            };
            DockPanel.SetDock(sample, Dock.Left);
            DockPanel.SetDock(combo, Dock.Right);
            row.Children.Add(sample);
            row.Children.Add(combo);
            row.Children.Add(new TextBlock { Text = m.Name(Loc.I.Lang), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(row);
        }
        SetBody(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        AddClose();
    }
}

/// <summary>Свойства проекта и параметры моделирования.</summary>
public sealed class ProjectSettingsWindow : DialogBase
{
    public ProjectSettingsWindow(Project p) : base(Loc.T("Menu_ProjectSettings"), 560, 520)
    {
        var form = new FormGrid(220) { Margin = new Thickness(12) };
        var name = form.Row(Loc.T("Settings_Name"), new TextBox { Text = p.Name });
        var author = form.Row(Loc.T("Settings_Author"), new TextBox { Text = p.Author });
        var desc = form.Row(Loc.T("Settings_Description"), new TextBox { Text = p.Description, AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top });
        form.Full(new TextBlock { Text = Loc.T("Settings_Simulation"), Style = (Style)FindResource("SectionHeader") });
        var seed = form.Row(Loc.T("Settings_Seed"), new TextBox { Text = p.Simulation.Seed.ToString() });
        var window = form.Row(Loc.T("Settings_Window"), new TextBox { Text = Num(p.Simulation.StatisticsWindowSec) });
        var queue = form.Row(Loc.T("Settings_Queue"), new TextBox { Text = p.Simulation.MaxQueuePackets.ToString() });
        var threshold = form.Row(Loc.T("Settings_Threshold"), new TextBox { Text = Num(p.Simulation.OverloadThreshold * 100) });
        form.Full(new TextBlock { Text = Loc.T("Settings_SeedHint"), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        SetBody(new ScrollViewer { Content = form });
        AddOkCancel(() =>
        {
            if (!int.TryParse(seed.Text, out var s) || !TryNumber(window.Text, out var w) || w <= 0
                || !int.TryParse(queue.Text, out var q) || q < 1 || !TryNumber(threshold.Text, out var t) || t <= 0 || t > 100)
            {
                MessageBox.Show(this, Loc.T("Settings_Invalid"), "NetSim", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            p.Name = name.Text.Trim();
            p.Author = author.Text.Trim();
            p.Description = desc.Text;
            p.Simulation.Seed = s;
            p.Simulation.StatisticsWindowSec = w;
            p.Simulation.MaxQueuePackets = q;
            p.Simulation.OverloadThreshold = t / 100;
            return true;
        });
    }
}

/// <summary>Справка: как выполнить каждую практику в NetSim.</summary>
public sealed class HelpWindow : DialogBase
{
    public HelpWindow() : base(Loc.T("Menu_HelpLabs"), 860, 720)
    {
        ShowInTaskbar = true;
        var tabs = new TabControl { Margin = new Thickness(12, 12, 12, 0) };
        foreach (var key in new[] { "Help_Basics", "Help_Pr2", "Help_Pr3", "Help_Pr4", "Help_Pr6", "Help_Pr8", "Help_Pr10", "Help_Protocol" })
        {
            var text = Loc.T(key);
            int nl = text.IndexOf('\n');
            tabs.Items.Add(new TabItem
            {
                Header = text[..nl],
                Content = new TextBox { Text = text[(nl + 1)..], Style = (Style)FindResource("ReportBox"), FontFamily = new FontFamily("Segoe UI"), FontSize = 13, Padding = new Thickness(8) },
            });
        }
        SetBody(tabs);
        AddClose();
    }
}
