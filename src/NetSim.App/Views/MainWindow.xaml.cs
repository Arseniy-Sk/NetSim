using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NetSim.App.Controls;
using NetSim.App.Localization;
using NetSim.App.ViewModels;

namespace NetSim.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private Point _dragStart;
    private CatalogNode? _dragNode;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        FillProfiles();
        Loc.I.LanguageChanged += FillProfiles;
        PreviewKeyDown += OnPreviewKeyDown;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentLevel))
                Dispatcher.BeginInvoke(() => { Topology.FitToView(); ZoomText.Text = $"{Topology.Zoom * 100:0}%"; }, System.Windows.Threading.DispatcherPriority.Loaded);
        };
        Closing += OnClosing;

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 3 && args[1] == "--lab" && args[2] == "pr2")
            Loaded += (_, _) => Diagnostics.LabPr2.Run(this, _vm, args[3]);
        else if (args.Length > 3 && args[1] == "--lab" && args[2] == "pr10")
            Loaded += (_, _) => Diagnostics.LabPr10.Run(this, _vm, args[3], args.Length > 4 ? args[4] : null);
        else if (args.Length > 3 && args[1] == "--lab" && args[2] == "pr8")
            Loaded += (_, _) => Diagnostics.LabPr8.Run(this, _vm, args[3]);
        else if (args.Length > 3 && args[1] == "--lab" && args[2] == "pr6")
            Loaded += (_, _) => Diagnostics.LabPr6.Run(this, _vm, args[3]);
        else if (args.Length > 3 && args[1] == "--lab" && args[2] == "pr5")
            Loaded += (_, _) => Diagnostics.LabPr5.Run(this, _vm, args[3]);
        else if (args.Length > 4 && args[1] == "--lab" && args[2] == "pr4")
            Loaded += (_, _) => Diagnostics.LabPr4.Run(this, _vm, args[3], args[4]);
        else if (args.Length > 3 && args[1] == "--lab" && args[2] == "pr3")
            Loaded += (_, _) => Diagnostics.LabPr3.Run(this, _vm, args[3]);
        else if (args.Length > 2 && args[1] == "--selftest")
            Loaded += (_, _) => Diagnostics.SelfTest.Run(this, _vm, args[2]);
        else if (args.Length > 1 && System.IO.File.Exists(args[1])) _vm.OpenFile(args[1]);
    }

    private void FillProfiles()
    {
        ProfilesList.Items.Clear();
        foreach (var p in _vm.Catalog.Profiles)
        {
            var svc = p.RequiredService == null ? "" : $" [{_vm.Catalog.Service(p.RequiredService)?.NameEn}]";
            ProfilesList.Items.Add(new ListBoxItem
            {
                Content = p.Name(Loc.I.Lang) + svc,
                Tag = p,
                ToolTip = $"{p.Protocol}\n{Loc.T("Profile_Size")}: {p.TransactionSize.Summary}\n{Loc.T("Profile_Interval")}: {p.TimeBetween.Summary}",
            });
        }
    }

    private void ProfilesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProfilesList.SelectedItem is ListBoxItem { Tag: NetSim.Core.Model.TrafficProfile p })
        {
            new ProfileEditorWindow(_vm, p) { Owner = this }.ShowDialog();
            FillProfiles();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (e.Key == Key.Delete)
        {
            _vm.DeleteSelectedCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Topology.CancelPending();
            _vm.Tool = ToolMode.Select;
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_vm.CanClose()) e.Cancel = true;
        else Application.Current.Shutdown();
    }

    private void RecentMenu_Opened(object sender, RoutedEventArgs e)
    {
        RecentMenu.Items.Clear();
        if (_vm.RecentFiles.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = Loc.T("Menu_RecentEmpty"), IsEnabled = false });
            return;
        }
        foreach (var path in _vm.RecentFiles)
            RecentMenu.Items.Add(new MenuItem { Header = path, Command = _vm.OpenRecentCommand, CommandParameter = path });
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = _vm.Project.Name + ".png" };
        if (dlg.ShowDialog() != true) return;
        Topology.ExportPng(dlg.FileName);
        _vm.Log(Loc.F("Log_Exported", dlg.FileName));
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Report_Click(object sender, RoutedEventArgs e) =>
        new ReportWindow(_vm, RenderLevel) { Owner = this }.ShowDialog();

    /// <summary>Отрисовать любой уровень проекта для отчёта (временно переключая рабочее поле).</summary>
    public NetSim.App.Services.ReportImage? RenderLevel(NetSim.Core.Model.Level level)
    {
        var saved = _vm.CurrentLevel;
        var selected = _vm.Selected;
        try
        {
            _vm.CurrentLevel = level;
            _vm.Selected = null;
            var (png, w, h) = Topology.RenderPng();
            var owner = _vm.Project.OwnerOf(level);
            var caption = owner == null ? Loc.F("Rep_LevelRoot", _vm.Project.Name) : Loc.F("Rep_LevelOf", owner.Name);
            return new NetSim.App.Services.ReportImage(caption, png, w, h);
        }
        finally
        {
            _vm.CurrentLevel = saved;
            _vm.Selected = selected;
        }
    }

    private void SetZoom(double z)
    {
        Topology.SetZoom(z);
        ZoomText.Text = $"{Topology.Zoom * 100:0}%";
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(Topology.Zoom * 1.2);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(Topology.Zoom / 1.2);
    private void Zoom100_Click(object sender, RoutedEventArgs e) => SetZoom(1);
    private void ZoomFit_Click(object sender, RoutedEventArgs e) { Topology.FitToView(); ZoomText.Text = $"{Topology.Zoom * 100:0}%"; }

    // Перетаскивание из браузера оборудования на схему.

    private void CatalogTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragNode = (e.OriginalSource as DependencyObject) is { } src ? FindNode(src) : null;
    }

    private static CatalogNode? FindNode(DependencyObject src)
    {
        while (src != null && src is not TreeViewItem) src = VisualTreeHelper.GetParent(src);
        return (src as TreeViewItem)?.DataContext as CatalogNode;
    }

    private void CatalogTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragNode is not { IsItem: true } node) return;
        var d = e.GetPosition(null) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var payload = node.Device != null ? "dev:" + node.Device.Id : "sw:" + node.Software!.Id;
        var data = new DataObject(TopologyView.DragFormat, payload);
        _dragNode = null;
        DragDrop.DoDragDrop(CatalogTree, data, DragDropEffects.Copy);
    }

    private void CatalogTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject) is not { } src || FindNode(src) is not { } node) return;
        if (node.Device != null)
        {
            // Двойной щелчок — добавить в видимую часть поля.
            double x = (CanvasScroll.HorizontalOffset + CanvasScroll.ViewportWidth / 2) / Topology.Zoom;
            double y = (CanvasScroll.VerticalOffset + CanvasScroll.ViewportHeight / 2) / Topology.Zoom;
            var rnd = new Random();
            _vm.AddDevice(node.Device.Id, Math.Round(x + rnd.Next(-60, 60)), Math.Round(y + rnd.Next(-60, 60)));
            e.Handled = true;
        }
        else if (node.Software != null)
        {
            if (_vm.Selected is NetSim.Core.Model.Device dev) _vm.InstallSoftware(dev, node.Software.Id);
            else _vm.Log(Loc.T("Log_DropSoftwareOnDevice"), true);
            e.Handled = true;
        }
    }
}
