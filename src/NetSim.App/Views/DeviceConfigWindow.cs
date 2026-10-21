using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using NetSim.App.Controls;
using NetSim.App.Localization;
using NetSim.App.Rendering;
using NetSim.App.ViewModels;
using NetSim.Core.Model;
using NetSim.Core.Topology;
using NetSim.Simulation.Traffic;

namespace NetSim.App.Views;

/// <summary>
/// Конфигурация устройства (Device Setup): общие сведения, установленное ПО и службы (Plugin Setup),
/// параметры сервера (Reply Size / Reply Delay), сетевые интерфейсы и маршруты, показ статистики.
/// Изменения применяются сразу; «Отмена» откатывает их через снимок проекта.
/// </summary>
public sealed class DeviceConfigWindow : DialogBase
{
    private readonly MainViewModel _vm;
    private readonly Device _d;
    private DistributionEditor? _replySize, _replyDelay;

    public DeviceConfigWindow(MainViewModel vm, Device device) : base(Loc.F("Config_Title", device.Name), 720, 640)
    {
        _vm = vm;
        _d = device;
        var tabs = new TabControl { Margin = new Thickness(12, 12, 12, 0) };
        tabs.Items.Add(new TabItem { Header = Loc.T("Config_General"), Content = General() });
        if (!device.IsContainer && !device.Kind.IsSharedMedium())
        {
            tabs.Items.Add(new TabItem { Header = Loc.T("Config_Software"), Content = Software() });
            tabs.Items.Add(new TabItem { Header = Loc.T("Config_Server"), Content = Server() });
        }
        var cd = vm.Catalog.Device(device.CatalogId);
        if (device.Kind == DeviceKind.AccessPoint || cd?.Standards.Contains("@wireless") == true)
            tabs.Items.Add(new TabItem { Header = Loc.T("Config_Radio"), Content = Radio() });
        tabs.Items.Add(new TabItem { Header = Loc.T("Config_Network"), Content = Network() });
        if (device.Kind is DeviceKind.Server or DeviceKind.Router or DeviceKind.Mainframe or DeviceKind.Gateway or DeviceKind.Firewall)
            tabs.Items.Add(new TabItem { Header = Loc.T("Config_Services"), Content = Services() });
        tabs.Items.Add(new TabItem { Header = Loc.T("Config_Statistics"), Content = Statistics() });
        SetBody(tabs);
        AddOkCancel(() =>
        {
            _replySize?.Apply();
            _replyDelay?.Apply();
            return true;
        });
    }

    private UIElement General()
    {
        var cd = _vm.Catalog.Device(_d.CatalogId);
        var panel = new StackPanel { Margin = new Thickness(10) };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        head.Children.Add(new Image { Source = IconLibrary.Get(cd?.Icon ?? "equipment"), Width = 56, Height = 56 });
        var info = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = cd?.Name(Loc.I.Lang) ?? _d.CatalogId, FontWeight = FontWeights.SemiBold, FontSize = 14 });
        info.Children.Add(new TextBlock { Text = $"{cd?.Vendor} · {Loc.T("Kind_" + _d.Kind)}", Foreground = Brushes.DimGray });
        head.Children.Add(info);
        panel.Children.Add(head);

        var form = new FormGrid();
        var name = form.Row(Loc.T("Props_Name"), new TextBox { Text = _d.Name });
        name.TextChanged += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) return;
            _d.Name = name.Text;
            if (_d.ChildLevel != null) _d.ChildLevel.Name = _d.Name;
        };
        if (cd != null)
        {
            form.Row(Loc.T("Config_Ports"), new TextBlock { Text = cd.Ports.ToString() });
            if (cd.ProcessingDelayMs > 0) form.Row(Loc.T("Config_Delay"), new TextBlock { Text = $"{Num(cd.ProcessingDelayMs)} ms" });
            var stds = cd.Standards.Count == 0 ? Loc.T("Config_AnyStandard") : string.Join(", ", cd.Standards);
            form.Row(Loc.T("Config_Standards"), new TextBlock { Text = stds, TextWrapping = TextWrapping.Wrap });
        }
        var notes = form.Row(Loc.T("Config_Notes"), new TextBox { Text = _d.Notes, AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top });
        notes.TextChanged += (_, _) => _d.Notes = notes.Text;
        panel.Children.Add(form);
        if (!string.IsNullOrEmpty(cd?.Description(Loc.I.Lang)))
            panel.Children.Add(new TextBlock { Text = cd!.Description(Loc.I.Lang), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 10, 0, 0) });
        return panel;
    }

    private UIElement Software()
    {
        var grid = new Grid { Margin = new Thickness(10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var installed = new ListBox();
        var services = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        grid.Children.Add(installed);
        Grid.SetColumn(services, 1);
        grid.Children.Add(services);

        void FillInstalled(InstalledSoftware? select = null)
        {
            installed.Items.Clear();
            foreach (var sw in _d.Software)
            {
                var item = new ListBoxItem { Content = _vm.Catalog.SoftwareById(sw.SoftwareId)?.Name(Loc.I.Lang) ?? sw.SoftwareId, Tag = sw };
                installed.Items.Add(item);
                if (sw == select) installed.SelectedItem = item;
            }
            if (installed.SelectedItem == null && installed.Items.Count > 0) installed.SelectedIndex = 0;
            if (installed.Items.Count == 0) FillServices(null);
        }

        void FillServices(InstalledSoftware? sw)
        {
            services.Children.Clear();
            services.Children.Add(new TextBlock { Text = Loc.T("Config_PluginSetup"), Style = (Style)FindResource("SectionHeader"), Margin = new Thickness(0, 0, 0, 6) });
            if (sw == null) { services.Children.Add(new TextBlock { Text = Loc.T("Props_NoSoftware"), Foreground = Brushes.Gray }); return; }
            var csw = _vm.Catalog.SoftwareById(sw.SoftwareId);
            foreach (var s in csw?.Services ?? new List<string>())
            {
                var cb = new CheckBox { Content = _vm.Catalog.Service(s)?.Name(Loc.I.Lang) ?? s, IsChecked = sw.EnabledServices.Contains(s), Margin = new Thickness(0, 3, 0, 3) };
                cb.Checked += (_, _) => { if (!sw.EnabledServices.Contains(s)) sw.EnabledServices.Add(s); };
                cb.Unchecked += (_, _) => sw.EnabledServices.Remove(s);
                services.Children.Add(cb);
            }
            if (!string.IsNullOrEmpty(csw?.Description(Loc.I.Lang)))
                services.Children.Add(new TextBlock { Text = csw!.Description(Loc.I.Lang), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            services.Children.Add(new TextBlock { Text = Loc.T("Config_ServicesHint"), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        }

        installed.SelectionChanged += (_, _) => FillServices((installed.SelectedItem as ListBoxItem)?.Tag as InstalledSoftware);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var add = new ComboBox { Width = 230 };
        foreach (var sw in _vm.Catalog.Software) add.Items.Add(new ComboBoxItem { Content = sw.Name(Loc.I.Lang), Tag = sw.Id });
        add.SelectedIndex = 0;
        var addBtn = new Button { Content = Loc.T("Config_Install"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        addBtn.Click += (_, _) =>
        {
            if (add.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            var sw = new TopologyBuilder(_vm.Project, _vm.Catalog).InstallSoftware(_d, id);
            FillInstalled(sw);
        };
        var remove = new Button { Content = Loc.T("Config_Uninstall"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        remove.Click += (_, _) =>
        {
            if ((installed.SelectedItem as ListBoxItem)?.Tag is InstalledSoftware sw) { _d.Software.Remove(sw); FillInstalled(); }
        };
        bar.Children.Add(add);
        bar.Children.Add(addBtn);
        bar.Children.Add(remove);
        Grid.SetRow(bar, 1);
        Grid.SetColumnSpan(bar, 2);
        grid.Children.Add(bar);

        FillInstalled();
        return grid;
    }

    private UIElement Server()
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = Loc.T("Config_ServerHint"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });
        _replySize = new DistributionEditor(_d.Server.ReplySize, "Reply Size", Loc.T("Unit_Bytes"));
        _replyDelay = new DistributionEditor(_d.Server.ReplyDelay, "Reply Delay", Loc.T("Unit_Sec")) { Margin = new Thickness(0, 16, 0, 0) };
        panel.Children.Add(_replySize);
        panel.Children.Add(_replyDelay);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement Network()
    {
        var panel = new DockPanel { Margin = new Thickness(10) };
        var form = new FormGrid();
        var host = form.Row("Hostname", new TextBox { Text = _d.Network.Hostname });
        host.TextChanged += (_, _) => _d.Network.Hostname = host.Text.Trim();
        var gw = form.Row(Loc.T("Config_Gateway"), new TextBox { Text = _d.Network.DefaultGateway });
        gw.TextChanged += (_, _) => _d.Network.DefaultGateway = gw.Text.Trim();
        var dns = form.Row(Loc.T("Config_DnsServer"), new TextBox { Text = _d.Network.DnsServer });
        dns.TextChanged += (_, _) => _d.Network.DnsServer = dns.Text.Trim();
        if (_d.Kind.IsEndNode())
        {
            var dhcpRow = new StackPanel { Orientation = Orientation.Horizontal };
            var useDhcp = new CheckBox { Content = Loc.T("Config_UseDhcp"), IsChecked = _d.Network.UseDhcp, VerticalAlignment = VerticalAlignment.Center };
            useDhcp.Checked += (_, _) => _d.Network.UseDhcp = true;
            useDhcp.Unchecked += (_, _) => _d.Network.UseDhcp = false;
            var renew = new Button { Content = Loc.T("Config_DhcpRenew"), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1) };
            var result = new TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray };
            renew.Click += (_, _) =>
            {
                var sim = new NetSim.Protocols.ProtocolSimulator(_vm.Project, _vm.Catalog);
                var lines = sim.DhcpRenewBlocking(_d);
                result.Text = lines.FirstOrDefault()?.Trim() ?? "";
                gw.Text = _d.Network.DefaultGateway;
                dns.Text = _d.Network.DnsServer;
                useDhcp.IsChecked = true;
            };
            dhcpRow.Children.Add(useDhcp);
            dhcpRow.Children.Add(renew);
            dhcpRow.Children.Add(result);
            form.Row("DHCP", dhcpRow);
        }
        DockPanel.SetDock(form, Dock.Top);
        panel.Children.Add(form);

        var hint = new TextBlock { Text = Loc.T("Config_NetworkHint"), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(hint, Dock.Bottom);
        panel.Children.Add(hint);

        var routesPanel = new DockPanel { Height = 150, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(routesPanel, Dock.Bottom);
        var routesHeader = new DockPanel();
        DockPanel.SetDock(routesHeader, Dock.Top);
        routesHeader.Children.Add(new TextBlock { Text = Loc.T("Config_Routes"), Style = (Style)FindResource("SectionHeader"), Margin = new Thickness(0, 0, 0, 4) });
        var addRoute = new Button { Content = "+", Width = 26, HorizontalAlignment = HorizontalAlignment.Right };
        var delRoute = new Button { Content = "−", Width = 26, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4, 0, 0, 0) };
        var routeButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        routeButtons.Children.Add(addRoute);
        routeButtons.Children.Add(delRoute);
        routesHeader.Children.Add(routeButtons);
        routesPanel.Children.Add(routesHeader);
        var routes = new DataGrid { ItemsSource = _d.Network.StaticRoutes, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
        routes.Columns.Add(new DataGridTextColumn { Header = Loc.T("Config_RouteNetwork"), Binding = new Binding(nameof(StaticRoute.Network)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        routes.Columns.Add(new DataGridTextColumn { Header = Loc.T("Config_Mask"), Binding = new Binding(nameof(StaticRoute.Mask)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        routes.Columns.Add(new DataGridTextColumn { Header = "Next hop", Binding = new Binding(nameof(StaticRoute.NextHop)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        addRoute.Click += (_, _) => _d.Network.StaticRoutes.Add(new StaticRoute { Mask = "255.255.255.0" });
        delRoute.Click += (_, _) => { if (routes.SelectedItem is StaticRoute r) _d.Network.StaticRoutes.Remove(r); };
        routesPanel.Children.Add(routes);
        panel.Children.Add(routesPanel);

        var grid = new DataGrid { ItemsSource = _d.Network.Interfaces, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(0, 8, 0, 0) };
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Config_Interface"), Binding = new Binding(nameof(NetInterface.Name)), IsReadOnly = true, Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "IP", Binding = new Binding(nameof(NetInterface.IpAddress)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Config_Mask"), Binding = new Binding(nameof(NetInterface.SubnetMask)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "MAC", Binding = new Binding(nameof(NetInterface.MacAddress)), IsReadOnly = true, Width = 120 });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = Loc.T("Config_Up"), Binding = new Binding(nameof(NetInterface.IsUp)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 50 });
        grid.Columns.Add(new DataGridTextColumn { Header = "VLAN", Binding = new Binding(nameof(NetInterface.Vlan)), Width = 50 });
        if (_d.Kind is DeviceKind.Switch or DeviceKind.Bridge or DeviceKind.AccessPoint)
        {
            grid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = Loc.T("Config_PortMode"), ItemsSource = Enum.GetValues<SwitchportMode>(), SelectedItemBinding = new Binding(nameof(NetInterface.Mode)), Width = 80,
            });
            grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("Config_AllowedVlans"), Binding = new Binding(nameof(NetInterface.AllowedVlans)), Width = 90 });
        }
        panel.Children.Add(grid);
        return panel;
    }

    /// <summary>Службы DHCP и DNS (как вкладка Services в Packet Tracer).</summary>
    private UIElement Services()
    {
        var net = _d.Network;
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = Loc.T("Config_ServicesTabHint"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });

        var dhcpOn = new CheckBox { Content = Loc.T("Config_DhcpServer"), IsChecked = net.DhcpServerEnabled, FontWeight = FontWeights.SemiBold };
        dhcpOn.Checked += (_, _) => net.DhcpServerEnabled = true;
        dhcpOn.Unchecked += (_, _) => net.DhcpServerEnabled = false;
        panel.Children.Add(dhcpOn);
        var pools = new DataGrid { ItemsSource = net.DhcpPools, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, Height = 120, Margin = new Thickness(0, 4, 0, 0) };
        void Col(DataGrid g, string h, string path, double w) => g.Columns.Add(new DataGridTextColumn { Header = h, Binding = new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = w });
        Col(pools, Loc.T("Config_PoolName"), nameof(DhcpPool.Name), 70);
        Col(pools, Loc.T("Config_RouteNetwork"), nameof(DhcpPool.Network), 100);
        Col(pools, Loc.T("Config_Mask"), nameof(DhcpPool.Mask), 100);
        Col(pools, Loc.T("Config_Gateway"), nameof(DhcpPool.DefaultRouter), 100);
        Col(pools, "DNS", nameof(DhcpPool.DnsServer), 100);
        Col(pools, Loc.T("Config_PoolStart"), nameof(DhcpPool.StartAddress), 100);
        Col(pools, Loc.T("Config_PoolMax"), nameof(DhcpPool.MaxUsers), 60);
        panel.Children.Add(pools);
        panel.Children.Add(AddRemove(() =>
        {
            var ip = net.Interfaces.Select(i => i.IpAddress).FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "";
            var pool = new DhcpPool { Name = "LAN" + (net.DhcpPools.Count + 1) };
            if (NetSim.Protocols.Ip.Ipv4.TryParse(ip, out var a))
            {
                pool.Network = a.Network(NetSim.Protocols.Ip.Ipv4.Parse("255.255.255.0")).ToString();
                pool.DefaultRouter = _d.Kind == DeviceKind.Router ? ip : net.DefaultGateway;
            }
            net.DhcpPools.Add(pool);
            net.DhcpServerEnabled = true;
            dhcpOn.IsChecked = true;
        }, () => { if (pools.SelectedItem is DhcpPool p) net.DhcpPools.Remove(p); }));

        var dnsOn = new CheckBox { Content = Loc.T("Config_DnsServerOn"), IsChecked = net.DnsServerEnabled, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) };
        dnsOn.Checked += (_, _) => net.DnsServerEnabled = true;
        dnsOn.Unchecked += (_, _) => net.DnsServerEnabled = false;
        panel.Children.Add(dnsOn);
        var records = new DataGrid { ItemsSource = net.DnsRecords, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, Height = 120, Margin = new Thickness(0, 4, 0, 0) };
        Col(records, Loc.T("Config_DnsName"), nameof(DnsRecord.Name), 220);
        Col(records, "IP", nameof(DnsRecord.Address), 140);
        panel.Children.Add(records);
        panel.Children.Add(AddRemove(() => { net.DnsRecords.Add(new DnsRecord { Name = "www.example.local" }); net.DnsServerEnabled = true; dnsOn.IsChecked = true; },
            () => { if (records.SelectedItem is DnsRecord r) net.DnsRecords.Remove(r); }));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static StackPanel AddRemove(Action add, Action remove)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var a = new Button { Content = "+", Width = 28 };
        a.Click += (_, _) => add();
        var r = new Button { Content = "−", Width = 28, Margin = new Thickness(4, 0, 0, 0) };
        r.Click += (_, _) => remove();
        bar.Children.Add(a);
        bar.Children.Add(r);
        return bar;
    }

    private UIElement Radio()
    {
        var r = _d.Radio;
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock { Text = Loc.T("Config_RadioHint"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 8) });
        var f = new FormGrid(240);
        void NumberRow(string label, double value, Action<double> set)
        {
            var tb = f.Row(label, new TextBox { Text = Num(value) });
            tb.TextChanged += (_, _) => { if (TryNumber(tb.Text, out var v)) set(v); };
        }
        NumberRow(Loc.T("Calc_Pt"), r.PowerDbm, v => r.PowerDbm = v);
        NumberRow(Loc.T("Config_RadioGain"), r.AntennaGainDbi, v => r.AntennaGainDbi = v);
        NumberRow(Loc.T("Config_RadioLoss"), r.CableLossDb, v => r.CableLossDb = v);
        var channel = f.Row(Loc.T("Calc_Channel"), new ComboBox());
        foreach (var c in Enumerable.Range(1, 13).Concat(new[] { 36, 40, 44, 48, 52, 56, 60, 64, 100, 104, 108, 112, 116, 120, 124, 128, 132, 136, 140, 149, 153, 157, 161, 165 }))
        {
            var item = new ComboBoxItem { Content = $"{c} ({NetSim.Calculators.WirelessCalculator.ChannelFrequency(c)} MHz)", Tag = c };
            channel.Items.Add(item);
            if (c == r.Channel) channel.SelectedItem = item;
        }
        channel.SelectionChanged += (_, _) => { if (channel.SelectedItem is ComboBoxItem { Tag: int c }) r.Channel = c; };
        var rate = f.Row(Loc.T("Calc_Speed"), new ComboBox());
        foreach (var (mbps, dbm) in NetSim.Calculators.WirelessCalculator.Sensitivity)
        {
            var item = new ComboBoxItem { Content = $"{mbps} Mbit/s → {dbm} dBm", Tag = mbps };
            rate.Items.Add(item);
            if (Math.Abs(mbps - r.RateMbps) < 1e-9) rate.SelectedItem = item;
        }
        rate.SelectionChanged += (_, _) => { if (rate.SelectedItem is ComboBoxItem { Tag: double v }) r.RateMbps = v; };
        panel.Children.Add(f);
        return panel;
    }

    private UIElement Statistics() => StatisticsItemsWindow.BuildChecklist(_d.Kind.IsEndNode() ? StatKeys.DeviceKeys : StatKeys.ChannelKeys.Append(StatKeys.PacketsForwarded).ToArray(), _d.ShownStatistics);
}
