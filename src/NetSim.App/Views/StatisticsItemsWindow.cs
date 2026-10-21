using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetSim.App.Localization;
using NetSim.App.ViewModels;
using NetSim.Core.Model;
using NetSim.Simulation.Traffic;

namespace NetSim.App.Views;

/// <summary>Диалог Statistics Items: какие показатели выводить под значком устройства или на линии.</summary>
public sealed class StatisticsItemsWindow : DialogBase
{
    public StatisticsItemsWindow(MainViewModel vm, object target) : base("Statistics Items", 460, 480)
    {
        string name;
        ObservableCollection<string> shown;
        string[] keys;
        switch (target)
        {
            case Device d:
                name = d.Name;
                shown = d.ShownStatistics;
                keys = d.Kind.IsEndNode() ? StatKeys.DeviceKeys : StatKeys.ChannelKeys.Append(StatKeys.PacketsForwarded).ToArray();
                break;
            case Link l:
                name = $"{vm.Project.FindDevice(l.AId)?.Name} — {vm.Project.FindDevice(l.BId)?.Name}";
                shown = l.ShownStatistics;
                keys = StatKeys.ChannelKeys;
                break;
            default:
                throw new ArgumentException(nameof(target));
        }

        var copy = new ObservableCollection<string>(shown);
        var body = new DockPanel { Margin = new Thickness(12) };
        var title = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(title, Dock.Top);
        body.Children.Add(title);
        body.Children.Add(BuildChecklist(keys, copy));
        SetBody(body);
        AddOkCancel(() =>
        {
            shown.Clear();
            foreach (var k in keys.Where(copy.Contains)) shown.Add(k);
            return true;
        });
    }

    /// <summary>Список галочек показателей, связанный с коллекцией выбранных ключей.</summary>
    public static UIElement BuildChecklist(string[] keys, ObservableCollection<string> selected)
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = Loc.T("Stats_Hint"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var k in keys)
        {
            var cb = new CheckBox { Content = StatKeys.Title(k, Loc.I.Lang), IsChecked = selected.Contains(k), Margin = new Thickness(0, 3, 0, 3) };
            cb.Checked += (_, _) => { if (!selected.Contains(k)) selected.Add(k); };
            cb.Unchecked += (_, _) => selected.Remove(k);
            panel.Children.Add(cb);
        }
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
