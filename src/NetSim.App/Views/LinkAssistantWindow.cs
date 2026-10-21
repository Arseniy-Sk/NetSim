using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetSim.App.Localization;
using NetSim.App.ViewModels;
using NetSim.Core.Catalog;
using NetSim.Core.Model;

namespace NetSim.App.Views;

/// <summary>Мастер соединений Link Assistant: спецификация (Protocol), скорость (Bandwidth), среда (Media), длина.</summary>
public sealed class LinkAssistantWindow : DialogBase
{
    private readonly MainViewModel _vm;
    private readonly CatalogDevice? _ca, _cb;
    private readonly ListBox _standards = new() { Height = 220 };
    private readonly CheckBox _showAll = new();
    private readonly TextBox _bandwidth = new();
    private readonly ComboBox _media = new();
    private readonly TextBox _length = new();
    private readonly TextBlock _warning = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Link? _existing;

    public string StandardId { get; private set; } = "";
    public string MediaId { get; private set; } = "";
    public double BandwidthBps { get; private set; }
    public double LengthM { get; private set; } = 10;

    public LinkAssistantWindow(MainViewModel vm, Device a, Device b, Link? existing) : base("Link Assistant", 600, 600)
    {
        _vm = vm;
        _existing = existing;
        _ca = vm.Catalog.Device(a.CatalogId);
        _cb = vm.Catalog.Device(b.CatalogId);

        var body = new StackPanel { Margin = new Thickness(12) };
        body.Children.Add(new TextBlock
        {
            Text = $"{a.Name} ({_ca?.Name(Loc.I.Lang)})  ↔  {b.Name} ({_cb?.Name(Loc.I.Lang)})",
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });
        body.Children.Add(new TextBlock { Text = Loc.T("Link_Protocol"), Style = (Style)FindResource("SectionHeader") });
        body.Children.Add(_standards);
        _showAll.Content = Loc.T("Link_ShowAll");
        _showAll.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(_showAll);

        var form = new FormGrid { Margin = new Thickness(0, 8, 0, 0) };
        form.Row(Loc.T("Link_Bandwidth"), _bandwidth);
        form.Row(Loc.T("Link_Media"), _media);
        form.Row(Loc.T("Link_Length"), _length);
        body.Children.Add(form);
        body.Children.Add(_warning);
        SetBody(body);

        AddOkCancel(Accept, existing == null ? "Link" : Loc.T("Btn_Ok"));

        _showAll.Checked += (_, _) => FillStandards();
        _showAll.Unchecked += (_, _) => FillStandards();
        _standards.SelectionChanged += (_, _) => OnStandardChanged();
        _length.TextChanged += (_, _) => Validate();
        _bandwidth.TextChanged += (_, _) => Validate();

        var initial = existing != null ? vm.Catalog.Standard(existing.StandardId) : vm.Catalog.SuggestStandard(_ca, _cb);
        if (initial != null && !DeviceCatalog.Supports(_ca, initial) | !DeviceCatalog.Supports(_cb, initial)) _showAll.IsChecked = true;
        FillStandards(initial?.Id);
        _length.Text = Num(existing?.LengthM ?? 10);
        if (existing != null)
        {
            _bandwidth.Text = Num(existing.BandwidthBps / 1e6, "0.####");
            foreach (ComboBoxItem item in _media.Items) if ((string)item.Tag == existing.MediaId) _media.SelectedItem = item;
        }
    }

    private void FillStandards(string? select = null)
    {
        select ??= (_standards.SelectedItem as ListBoxItem)?.Tag as string;
        _standards.Items.Clear();
        foreach (var s in _vm.Catalog.Standards)
        {
            bool ok = DeviceCatalog.Supports(_ca, s) && DeviceCatalog.Supports(_cb, s);
            if (!ok && _showAll.IsChecked != true) continue;
            var item = new ListBoxItem
            {
                Content = $"{s.Name(Loc.I.Lang)}   —   {NetSim.Simulation.Traffic.StatKeys.FormatBits(s.BandwidthBps)}",
                Tag = s.Id,
                Foreground = ok ? Brushes.Black : Brushes.Gray,
                ToolTip = ok ? null : Loc.T("Link_Incompatible"),
            };
            _standards.Items.Add(item);
            if (s.Id == select) _standards.SelectedItem = item;
        }
        if (_standards.SelectedItem == null && _standards.Items.Count > 0) _standards.SelectedIndex = 0;
        if (_standards.SelectedItem is ListBoxItem sel) _standards.ScrollIntoView(sel);
    }

    private MediaStandard? Selected => (_standards.SelectedItem as ListBoxItem)?.Tag is string id ? _vm.Catalog.Standard(id) : null;

    private void OnStandardChanged()
    {
        var s = Selected;
        if (s == null) return;
        _bandwidth.Text = Num(s.BandwidthBps / 1e6, "0.####");
        _media.Items.Clear();
        foreach (var m in s.Media)
        {
            var mt = _vm.Catalog.Media(m);
            _media.Items.Add(new ComboBoxItem { Content = mt?.Name(Loc.I.Lang) ?? m, Tag = m });
        }
        _media.SelectedIndex = 0;
        Validate();
    }

    private void Validate()
    {
        var s = Selected;
        var msgs = new List<string>();
        if (s == null) msgs.Add(Loc.T("Link_NoStandard"));
        else
        {
            if (!DeviceCatalog.Supports(_ca, s) || !DeviceCatalog.Supports(_cb, s)) msgs.Add(Loc.T("Link_Incompatible"));
            if (TryNumber(_length.Text, out var len) && s.MaxLengthM > 0 && len > s.MaxLengthM) msgs.Add(Loc.F("Link_TooLong", s.MaxLengthM));
        }
        if (!TryNumber(_length.Text, out var l) || l < 0) msgs.Add(Loc.T("Link_BadLength"));
        if (!TryNumber(_bandwidth.Text, out var bw) || bw <= 0) msgs.Add(Loc.T("Link_BadBandwidth"));
        _warning.Text = string.Join("\n", msgs);
    }

    private bool Accept()
    {
        var s = Selected;
        if (s == null || !TryNumber(_length.Text, out var len) || len < 0 || !TryNumber(_bandwidth.Text, out var bw) || bw <= 0)
        {
            Validate();
            return false;
        }
        StandardId = s.Id;
        MediaId = (_media.SelectedItem as ComboBoxItem)?.Tag as string ?? s.Media.FirstOrDefault() ?? "utp5";
        BandwidthBps = bw * 1e6;
        LengthM = len;
        return true;
    }
}
