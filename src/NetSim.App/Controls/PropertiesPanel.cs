using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NetSim.App.Localization;
using NetSim.App.Rendering;
using NetSim.App.ViewModels;
using NetSim.Core.Model;
using NetSim.Simulation.Traffic;

namespace NetSim.App.Controls;

/// <summary>Панель свойств выделенного объекта с живой статистикой во время моделирования.</summary>
public sealed class PropertiesPanel : UserControl
{
    private MainViewModel? _vm;
    private readonly StackPanel _root = new() { Margin = new Thickness(10) };
    private readonly Grid _stats = new();
    private DateTime _lastStats;
    private bool _showsPlaceholder;

    public PropertiesPanel()
    {
        Content = new ScrollViewer { Content = _root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        Loc.I.LanguageChanged += Rebuild;
    }

    private void Attach(MainViewModel? vm)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmChanged;
            _vm.Redraw -= OnRedraw;
        }
        _vm = vm;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmChanged;
            _vm.Redraw += OnRedraw;
        }
        Rebuild();
    }

    private object? _shown;

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Selected) or nameof(MainViewModel.Mode) or nameof(MainViewModel.Project) or nameof(MainViewModel.CurrentLevel)) Rebuild();
    }

    private void OnRedraw()
    {
        if (_vm?.Selected != _shown) { Rebuild(); return; }
        bool waitingForSim = _showsPlaceholder && _vm?.TrafficSim != null;
        if (!waitingForSim && (DateTime.Now - _lastStats).TotalMilliseconds < 250) return;
        _lastStats = DateTime.Now;
        if (_vm?.Selected == null) Rebuild(); // сводка проекта: число устройств, имя
        else FillStats();
    }

    private TextBlock Header(string text) => new() { Text = text, Style = (Style)FindResource("SectionHeader") };

    private static TextBlock Line(string text, Brush? brush = null) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1), Foreground = brush ?? Brushes.Black };

    private static Button Btn(string text, Action action)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Stretch };
        b.Click += (_, _) => action();
        return b;
    }

    private void Rebuild()
    {
        _root.Children.Clear();
        _stats.Children.Clear();
        _stats.RowDefinitions.Clear();
        if (_vm == null) return;
        _shown = _vm.Selected;
        switch (_vm.Selected)
        {
            case Device d: BuildDevice(d); break;
            case Link l: BuildLink(l); break;
            case PlanShape ps: BuildShape(ps); break;
            default: BuildProject(); break;
        }
        FillStats();
    }

    private void BuildProject()
    {
        var p = _vm!.Project;
        _root.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(p.Name) ? Loc.T("Project_Untitled") : p.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(p.Description)) _root.Children.Add(Line(p.Description, Brushes.DimGray));
        _root.Children.Add(Header(Loc.T("Props_Project")));
        _root.Children.Add(Line(Loc.F("Props_Counts", p.AllDevices().Count(), p.AllLinks().Count(), p.Flows.Count)));
        _root.Children.Add(Line(Loc.F("Props_Level", _vm.CurrentLevel.Name)));
        _root.Children.Add(Header(Loc.T("Props_HowTo")));
        _root.Children.Add(Line(Loc.T("Props_HowToText"), Brushes.DimGray));
    }

    private void BuildDevice(Device d)
    {
        var cd = _vm!.Catalog.Device(d.CatalogId);
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Image { Source = IconLibrary.Get(cd?.Icon ?? "equipment"), Width = 40, Height = 40 });
        var name = new TextBox { Text = d.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), MinWidth = 170, VerticalAlignment = VerticalAlignment.Center };
        void Commit()
        {
            var n = name.Text.Trim();
            if (n.Length == 0 || n == d.Name) return;
            _vm.Checkpoint();
            d.Name = n;
            if (d.ChildLevel != null) d.ChildLevel.Name = n;
            _vm.RequestRedraw();
        }
        name.LostFocus += (_, _) => Commit();
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        head.Children.Add(name);
        _root.Children.Add(head);

        _root.Children.Add(Header(Loc.T("Props_Model")));
        _root.Children.Add(Line(cd?.Name(Loc.I.Lang) ?? d.CatalogId));
        if (cd != null && cd.Vendor != "Generic") _root.Children.Add(Line(cd.Vendor, Brushes.DimGray));
        if (cd != null && !string.IsNullOrEmpty(cd.Description(Loc.I.Lang))) _root.Children.Add(Line(cd.Description(Loc.I.Lang), Brushes.DimGray));
        _root.Children.Add(Line(Loc.F("Props_Kind", Loc.T("Kind_" + d.Kind)), Brushes.DimGray));

        if (!d.IsContainer && !d.Kind.IsSharedMedium())
        {
            _root.Children.Add(Header(Loc.T("Props_Software")));
            if (d.Software.Count == 0) _root.Children.Add(Line(Loc.T("Props_NoSoftware"), Brushes.Gray));
            foreach (var sw in d.Software)
            {
                var csw = _vm.Catalog.SoftwareById(sw.SoftwareId);
                var services = string.Join(", ", sw.EnabledServices.Select(s => _vm.Catalog.Service(s)?.NameEn ?? s));
                _root.Children.Add(Line($"• {csw?.Name(Loc.I.Lang) ?? sw.SoftwareId}" + (services.Length > 0 ? $" — {services}" : "")));
            }
            if (d.Software.Count > 0 && d.Kind is DeviceKind.Server or DeviceKind.Mainframe)
                _root.Children.Add(Line($"Reply Size: {d.Server.ReplySize.Summary}\nReply Delay: {d.Server.ReplyDelay.Summary}", Brushes.DimGray));
        }

        if (_vm.Mode == SimMode.Protocol && d.Network.Interfaces.Count > 0)
        {
            _root.Children.Add(Header(Loc.T("Props_Interfaces")));
            foreach (var i in d.Network.Interfaces)
                _root.Children.Add(Line($"{i.Name}: {(string.IsNullOrEmpty(i.IpAddress) ? "—" : i.IpAddress + "/" + i.SubnetMask)}{(i.IsUp ? "" : " (shutdown)")}"));
            if (!string.IsNullOrEmpty(d.Network.DefaultGateway)) _root.Children.Add(Line($"Gateway: {d.Network.DefaultGateway}"));
        }

        _root.Children.Add(Header(Loc.T("Props_Actions")));
        if (d.IsContainer) _root.Children.Add(Btn(Loc.T("Ctx_Open"), () => _vm.OpenContainer(d)));
        else _root.Children.Add(Btn(Loc.T("Ctx_Configuration"), () => _vm.EditDevice(d)));
        _root.Children.Add(Btn(Loc.T("Ctx_Statistics"), () => _vm.ShowStatisticsItems(d)));
        if (!d.IsContainer && !d.Kind.IsSharedMedium()) _root.Children.Add(Btn(Loc.T("Ctx_Console"), () => _vm.OpenConsole(d)));

        _root.Children.Add(Header(Loc.T("Props_LiveStats")));
        _root.Children.Add(_stats);
    }

    private void BuildShape(PlanShape sh)
    {
        var plan = _vm!.CurrentLevel.Plan;
        string m = Loc.I.IsRu ? "м" : "m";
        _root.Children.Add(new TextBlock { Text = Loc.T(sh.Kind == PlanShapeKind.Room ? "Plan_Room" : "Plan_Wall"), FontSize = 16, FontWeight = FontWeights.SemiBold });
        var name = new TextBox { Text = sh.Label, Margin = new Thickness(0, 6, 0, 0) };
        name.LostFocus += (_, _) => { if (name.Text != sh.Label) { _vm.Checkpoint(); sh.Label = name.Text; _vm.RequestRedraw(); } };
        _root.Children.Add(name);
        if (sh.Kind == PlanShapeKind.Room)
            _root.Children.Add(Line($"{plan.ToMeters(Math.Abs(sh.X2 - sh.X1)):0.##} × {plan.ToMeters(Math.Abs(sh.Y2 - sh.Y1)):0.##} {m}"));
        else
            _root.Children.Add(Line($"{plan.DistanceM(sh.X1, sh.Y1, sh.X2, sh.Y2):0.##} {m}"));
        _root.Children.Add(Line(Loc.F("Plan_LossInfo", plan.WallLossDb), Brushes.DimGray));
        _root.Children.Add(Header(Loc.T("Props_Actions")));
        _root.Children.Add(Btn(Loc.T("Ctx_Delete"), () => _vm.Delete(sh)));
    }

    private void BuildLink(Link l)
    {
        var a = _vm!.Project.FindDevice(l.AId);
        var b = _vm.Project.FindDevice(l.BId);
        var std = _vm.Catalog.Standard(l.StandardId);
        var media = _vm.Catalog.Media(l.MediaId);
        _root.Children.Add(new TextBlock { Text = Loc.T("Props_Link"), FontSize = 16, FontWeight = FontWeights.SemiBold });
        _root.Children.Add(Line($"{a?.Name} ({l.AInterface}) — {b?.Name} ({l.BInterface})"));
        _root.Children.Add(Header(Loc.T("Props_Spec")));
        _root.Children.Add(Line(std?.Name(Loc.I.Lang) ?? l.StandardId));
        _root.Children.Add(Line(Loc.F("Props_Media", media?.Name(Loc.I.Lang) ?? l.MediaId)));
        _root.Children.Add(Line(Loc.F("Props_Bandwidth", StatKeys.FormatBits(l.BandwidthBps))));
        _root.Children.Add(Line(Loc.F("Props_Length", l.LengthM.ToString("0.#"))));
        if (std != null && std.MaxLengthM > 0 && l.LengthM > std.MaxLengthM)
            _root.Children.Add(Line(Loc.F("Link_TooLong", std.MaxLengthM), Brushes.Firebrick));

        var graph = new NetSim.Core.Topology.NetworkGraph(_vm.Project, _vm.Catalog);
        if (graph.Links.Contains(l))
        {
            var ch = graph.ChannelOf(l);
            _root.Children.Add(Line(ch.IsShared ? Loc.F("Props_SharedDomain", ch.DisplayName, ch.Links.Count) : Loc.T("Props_Dedicated"), Brushes.DimGray));
        }

        _root.Children.Add(Header(Loc.T("Props_Actions")));
        _root.Children.Add(Btn(Loc.T("Ctx_LinkProps"), () => _vm.EditLink(l)));
        _root.Children.Add(Btn(Loc.T("Ctx_Statistics"), () => _vm.ShowStatisticsItems(l)));
        _root.Children.Add(Header(Loc.T("Props_LiveStats")));
        _root.Children.Add(_stats);
    }

    private void FillStats()
    {
        _stats.Children.Clear();
        _stats.RowDefinitions.Clear();
        if (_stats.ColumnDefinitions.Count == 0)
        {
            _stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _stats.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        var sim = _vm?.TrafficSim;
        if (_vm == null || _vm.Mode != SimMode.Traffic || sim == null)
        {
            _showsPlaceholder = _vm?.Selected is Device or Link;
            if (_showsPlaceholder) Add(Loc.T("Props_RunForStats"), "");
            return;
        }
        string lang = Loc.I.Lang;
        if (_vm.Selected is Device d)
        {
            var keys = d.Kind.IsEndNode() ? StatKeys.DeviceKeys : StatKeys.ChannelKeys.Concat(new[] { StatKeys.PacketsForwarded });
            foreach (var k in keys) Add(StatKeys.Title(k, lang), StatKeys.Format(k, sim.DeviceValue(d, k), lang));
        }
        else if (_vm.Selected is Link l)
        {
            foreach (var k in StatKeys.ChannelKeys) Add(StatKeys.Title(k, lang), StatKeys.Format(k, sim.LinkValue(l, k), lang));
        }
    }

    private void Add(string title, string value)
    {
        int row = _stats.RowDefinitions.Count;
        _stats.RowDefinitions.Add(new RowDefinition());
        var t = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 1, 6, 1) };
        var v = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0, 70, 170)), Margin = new Thickness(0, 1, 0, 1) };
        Grid.SetRow(t, row);
        Grid.SetRow(v, row);
        Grid.SetColumn(v, 1);
        _stats.Children.Add(t);
        _stats.Children.Add(v);
    }
}
