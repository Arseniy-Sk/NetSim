using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetSim.App.Controls;
using NetSim.App.Localization;
using NetSim.App.ViewModels;
using NetSim.Core.Model;
using NetSim.Core.Topology;

namespace NetSim.App.Views;

/// <summary>Окно Profiles: выбор профиля трафика для потока «источник → приёмник» (Set traffic).</summary>
public sealed class ProfilesWindow : DialogBase
{
    private readonly MainViewModel _vm;
    private readonly Device _source, _target;
    private readonly ListBox _list = new();
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 0, 0, 0) };
    private readonly TextBlock _warning = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 8, 0, 0) };
    private TrafficProfile? _edited;

    public TrafficProfile? Result { get; private set; }

    public ProfilesWindow(MainViewModel vm, Device source, Device target) : base("Profiles", 760, 520)
    {
        _vm = vm;
        _source = source;
        _target = target;

        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());

        var title = new TextBlock { Text = Loc.F("Profiles_Title", source.Name, target.Name), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        Grid.SetColumnSpan(title, 2);
        grid.Children.Add(title);

        Grid.SetRow(_list, 1);
        grid.Children.Add(_list);
        var right = new StackPanel();
        right.Children.Add(_details);
        right.Children.Add(_warning);
        var edit = new Button { Content = Loc.T("Profiles_Edit"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10, 12, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        edit.Click += (_, _) => EditSelected();
        right.Children.Add(edit);
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        SetBody(grid);

        foreach (var p in vm.Catalog.Profiles)
            _list.Items.Add(new ListBoxItem { Content = p.Name(Loc.I.Lang), Tag = p });
        _list.SelectionChanged += (_, _) => { _edited = null; ShowDetails(); };
        _list.MouseDoubleClick += (_, _) => { if (Accept()) DialogResult = true; };

        // Подсказка: профиль под установленную на приёмнике службу.
        var suggested = vm.Catalog.Profiles.FirstOrDefault(p => p.RequiredService != null && target.HasService(p.RequiredService))
                        ?? vm.Catalog.Profile("small-office-p2p");
        foreach (ListBoxItem item in _list.Items) if (item.Tag == suggested) _list.SelectedItem = item;

        AddOkCancel(Accept, "Assign");
    }

    private TrafficProfile? Current => _edited ?? (_list.SelectedItem as ListBoxItem)?.Tag as TrafficProfile;

    private void ShowDetails()
    {
        var p = Current;
        if (p == null) { _details.Text = ""; return; }
        var svc = p.RequiredService == null ? Loc.T("Profiles_NoService") : _vm.Catalog.Service(p.RequiredService)?.Name(Loc.I.Lang);
        _details.Text =
            $"{p.Name(Loc.I.Lang)}{(_edited != null ? " *" : "")}\n\n" +
            $"{Loc.T("Profile_Pattern")}: {Loc.T(p.Pattern == TrafficPattern.ClientServer ? "Pattern_ClientServer" : "Pattern_PeerToPeer")}\n" +
            $"{Loc.T("Profile_Service")}: {svc}\n" +
            $"Application Layer Protocol: {p.Protocol}\n" +
            $"Transaction size ({Loc.T("Unit_Bytes")}): {p.TransactionSize.Summary}\n" +
            $"Time between transactions ({Loc.T("Unit_Sec")}): {p.TimeBetween.Summary}";
        var error = new TopologyBuilder(_vm.Project, _vm.Catalog) { Language = Loc.I.Lang }.ValidateFlow(_source, _target, p);
        _warning.Text = error ?? "";
    }

    private void EditSelected()
    {
        var p = Current;
        if (p == null) return;
        var copy = p.Clone();
        var w = new ProfileEditorWindow(_vm, copy) { Owner = this };
        if (w.ShowDialog() == true)
        {
            _edited = copy;
            ShowDetails();
        }
    }

    private bool Accept()
    {
        var p = Current;
        if (p == null) return false;
        var error = new TopologyBuilder(_vm.Project, _vm.Catalog) { Language = Loc.I.Lang }.ValidateFlow(_source, _target, p);
        if (error != null)
        {
            MessageBox.Show(this, error, "NetSim", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        Result = _edited ?? p.Clone();
        return true;
    }
}

/// <summary>Редактор профиля трафика: шаблон, служба, протокол, законы размера транзакции и паузы.</summary>
public sealed class ProfileEditorWindow : DialogBase
{
    private readonly TrafficProfile _profile;
    private readonly DistributionEditor _size;
    private readonly DistributionEditor _interval;

    public ProfileEditorWindow(MainViewModel vm, TrafficProfile profile) : base(Loc.T("ProfileEditor_Title"), 620, 720)
    {
        _profile = profile;
        var body = new StackPanel { Margin = new Thickness(12) };
        var form = new FormGrid();
        var nameRu = form.Row(Loc.T("ProfileEditor_NameRu"), new TextBox { Text = profile.NameRu });
        var nameEn = form.Row(Loc.T("ProfileEditor_NameEn"), new TextBox { Text = profile.NameEn });
        var pattern = form.Row(Loc.T("Profile_Pattern"), new ComboBox());
        pattern.Items.Add(new ComboBoxItem { Content = Loc.T("Pattern_PeerToPeer"), Tag = TrafficPattern.PeerToPeer });
        pattern.Items.Add(new ComboBoxItem { Content = Loc.T("Pattern_ClientServer"), Tag = TrafficPattern.ClientServer });
        pattern.SelectedIndex = profile.Pattern == TrafficPattern.ClientServer ? 1 : 0;
        var service = form.Row(Loc.T("Profile_Service"), new ComboBox());
        service.Items.Add(new ComboBoxItem { Content = Loc.T("Profiles_NoService"), Tag = null });
        foreach (var s in vm.Catalog.Services) service.Items.Add(new ComboBoxItem { Content = s.Name(Loc.I.Lang), Tag = s.Id });
        foreach (ComboBoxItem i in service.Items) if ((string?)i.Tag == profile.RequiredService) service.SelectedItem = i;
        var protocol = form.Row("Application Layer Protocol", new TextBox { Text = profile.Protocol });
        body.Children.Add(form);

        _size = new DistributionEditor(profile.TransactionSize, "Transaction size", Loc.T("Unit_Bytes")) { Margin = new Thickness(0, 12, 0, 0) };
        _interval = new DistributionEditor(profile.TimeBetween, "Time between transactions", Loc.T("Unit_Sec")) { Margin = new Thickness(0, 12, 0, 0) };
        body.Children.Add(_size);
        body.Children.Add(_interval);
        body.Children.Add(new TextBlock { Text = Loc.T("ProfileEditor_Hint"), Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        SetBody(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        AddOkCancel(() =>
        {
            _profile.NameRu = nameRu.Text;
            _profile.NameEn = nameEn.Text;
            _profile.Pattern = (TrafficPattern)((ComboBoxItem)pattern.SelectedItem).Tag;
            _profile.RequiredService = (service.SelectedItem as ComboBoxItem)?.Tag as string;
            _profile.Protocol = protocol.Text;
            _size.Apply();
            _interval.Apply();
            return true;
        });
    }
}
