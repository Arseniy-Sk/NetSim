using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NetSim.App.Localization;
using NetSim.App.ViewModels;
using NetSim.Core.Model;

namespace NetSim.App.Views;

/// <summary>Global → Data Flow: список всех потоков, галочки анимации (Uncheck All), правка и удаление.</summary>
public sealed class DataFlowWindow : DialogBase
{
    private readonly MainViewModel _vm;
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
        SelectionMode = DataGridSelectionMode.Single, Margin = new Thickness(12, 12, 12, 0),
    };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontWeight = FontWeights.SemiBold };

    public sealed class Row
    {
        public TrafficFlow Flow { get; init; } = null!;
        public string Source { get; init; } = "";
        public string Target { get; init; } = "";
        public string Profile => Flow.Profile.Name(Loc.I.Lang);
        public string Size => Flow.Profile.TransactionSize.Summary;
        public string Interval => Flow.Profile.TimeBetween.Summary;
        public bool ShowAnimation { get => Flow.ShowAnimation; set => Flow.ShowAnimation = value; }
        public bool IsActive { get => Flow.IsActive; set => Flow.IsActive = value; }
    }

    public DataFlowWindow(MainViewModel vm) : base("Data Flow", 980, 560)
    {
        _vm = vm;
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = Loc.T("Flow_Show"), Binding = new Binding(nameof(Row.ShowAnimation)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 70 });
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = Loc.T("Flow_Active"), Binding = new Binding(nameof(Row.IsActive)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 70 });
        _grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Flow_Source"), Binding = new Binding(nameof(Row.Source)), IsReadOnly = true, Width = 140 });
        _grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Flow_Target"), Binding = new Binding(nameof(Row.Target)), IsReadOnly = true, Width = 140 });
        _grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Flow_Profile"), Binding = new Binding(nameof(Row.Profile)), IsReadOnly = true, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Transaction size", Binding = new Binding(nameof(Row.Size)), IsReadOnly = true, Width = 150 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Time between", Binding = new Binding(nameof(Row.Interval)), IsReadOnly = true, Width = 140 });
        _grid.MouseDoubleClick += (_, _) => EditSelected();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 12, 12, 0) };
        top.Children.Add(Small(Loc.T("Flow_CheckAll"), () => SetAll(true)));
        top.Children.Add(Small(Loc.T("Flow_UncheckAll"), () => SetAll(false)));
        top.Children.Add(Small(Loc.T("Flow_Edit"), EditSelected));
        top.Children.Add(Small(Loc.T("Flow_Delete"), DeleteSelected));
        top.Children.Add(_count);
        DockPanel.SetDock(top, Dock.Top);
        Root.Children.Add(top);
        SetBody(_grid);
        AddClose();
        Closed += (_, _) => { _vm.IsDirty = true; _vm.RequestRedraw(); };
        Fill();
    }

    private static Button Small(string text, Action a)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        b.Click += (_, _) => a();
        return b;
    }

    private void Fill()
    {
        _grid.ItemsSource = _vm.Project.Flows.Select(f => new Row
        {
            Flow = f,
            Source = _vm.Project.FindDevice(f.SourceId)?.Name ?? "?",
            Target = _vm.Project.FindDevice(f.TargetId)?.Name ?? "?",
        }).ToList();
        _count.Text = Loc.F("Flow_Count", _vm.Project.Flows.Count);
    }

    private void SetAll(bool value)
    {
        foreach (var f in _vm.Project.Flows) f.ShowAnimation = value;
        Fill();
    }

    private void EditSelected()
    {
        if (_grid.SelectedItem is not Row r) return;
        _vm.Checkpoint();
        if (new ProfileEditorWindow(_vm, r.Flow.Profile) { Owner = this }.ShowDialog() == true) _vm.TopologyChanged();
        Fill();
    }

    private void DeleteSelected()
    {
        if (_grid.SelectedItem is not Row r) return;
        _vm.DeleteFlow(r.Flow);
        Fill();
    }
}
