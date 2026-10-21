using System.Windows;
using System.Windows.Controls;
using NetSim.App.Localization;
using NetSim.App.Services;
using NetSim.App.ViewModels;
using NetSim.App.Views;
using NetSim.Core.Model;
using NetSim.Core.Topology;

namespace NetSim.App.Diagnostics;

/// <summary>Скриншот для отчёта: файл, подпись, текст перед ним и заголовок раздела.</summary>
public sealed record Shot(string File, string Caption, string? Text, string? Section);

public sealed class Ctx
{
    public MainWindow Window = null!;
    public MainViewModel Vm = null!;
    public string Dir = "";
    public List<Shot> Shots = new();
    public TopologyBuilder B = null!;

    public async Task Main(string file, string caption, string? text = null, string? section = null, bool fit = true, bool rebuild = true, Level? level = null)
    {
        if (rebuild) Vm.TopologyChanged();
        Vm.CurrentLevel = level ?? Vm.Project.Root;
        await SelfTest.Settle();
        if (fit) Window.Topology.FitToView();
        await SelfTest.Settle();
        SelfTest.Snap(Window, Dir, file);
        Shots.Add(new(file, caption, text, section));
    }

    /// <summary>Окно конфигурации на вкладке «Программы» с выбранным ПО (как Plugin Setup).</summary>
    public async Task Software(Device d, string softwareId, string file, string caption, string? text = null, string? section = null)
    {
        Vm.Checkpoint();
        var w = new DeviceConfigWindow(Vm, d) { Owner = Window };
        w.Show();
        await SelfTest.Settle();
        var tabs = LabPr2.FindChild<TabControl>(w)!;
        var tab = tabs.Items.OfType<TabItem>().First(t => (string)t.Header == Loc.T("Config_Software"));
        tabs.SelectedItem = tab;
        await SelfTest.Settle();
        var list = LabPr2.FindChild<ListBox>((DependencyObject)tab.Content)!;
        list.SelectedItem = list.Items.OfType<ListBoxItem>().First(i => ((InstalledSoftware)i.Tag).SoftwareId == softwareId);
        await SelfTest.Settle();
        SelfTest.Snap(w, Dir, file);
        w.Close();
        Shots.Add(new(file, caption, text, section));
    }

    /// <summary>Главное окно с выделенным устройством — справа видно всё ПО и включённые службы.</summary>
    public async Task Selected(Device d, string file, string caption, string? text = null, string? section = null)
    {
        Vm.Selected = d;
        await SelfTest.Settle();
        SelfTest.Snap(Window, Dir, file);
        Shots.Add(new(file, caption, text, section));
    }

    /// <summary>Окно конфигурации устройства на указанной вкладке.</summary>
    public async Task ConfigTab(Device d, string tabKey, string file, string caption, string? text = null, string? section = null)
    {
        Vm.Checkpoint();
        var w = new DeviceConfigWindow(Vm, d) { Owner = Window };
        w.Show();
        await SelfTest.Settle();
        var tabs = LabPr2.FindChild<TabControl>(w)!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Header == Loc.T(tabKey));
        await SelfTest.Settle();
        SelfTest.Snap(w, Dir, file);
        w.Close();
        Shots.Add(new(file, caption, text, section));
    }

    /// <summary>Скриншот произвольного окна (диалога).</summary>
    public async Task Dialog(Window w, string file, string caption, string? text = null, string? section = null, Action<Window>? prepare = null)
    {
        w.Owner = Window;
        w.Show();
        await SelfTest.Settle();
        if (prepare != null) { prepare(w); await SelfTest.Settle(); }
        SelfTest.Snap(w, Dir, file);
        w.Close();
        Shots.Add(new(file, caption, text, section));
    }

    /// <summary>Вкладка «Сервер (Reply)»: отдельные снимки редакторов Reply Size (0) и Reply Delay (1).</summary>
    public async Task ServerEditors(Device d, params (int Index, string File, string Caption, string? Text, string? Section)[] shots)
    {
        Vm.Checkpoint();
        var w = new DeviceConfigWindow(Vm, d) { Owner = Window };
        w.Show();
        await SelfTest.Settle();
        var tabs = LabPr2.FindChild<TabControl>(w)!;
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => (string)t.Header == Loc.T("Config_Server"));
        await SelfTest.Settle();
        var editors = FindAll<NetSim.App.Controls.DistributionEditor>(w);
        foreach (var s in shots)
        {
            SelfTest.SnapElement(editors[s.Index], Dir, s.File);
            Shots.Add(new(s.File, s.Caption, s.Text, s.Section));
        }
        w.Close();
    }

    private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        var list = new List<T>();
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T t) list.Add(t);
            list.AddRange(FindAll<T>(c));
        }
        return list;
    }

    public void Install(Device d, string softwareId, params string[] services)
    {
        var sw = B.InstallSoftware(d, softwareId);
        sw.EnabledServices.Clear();
        foreach (var s in services) sw.EnabledServices.Add(s);
    }

    public void Images(WordReport w, IEnumerable<Shot> shots)
    {
        foreach (var s in shots)
        {
            if (s.Section != null) w.Heading2(s.Section);
            if (s.Text != null) w.Paragraph(s.Text);
            w.Image(LabPr2.Load(Dir, s.File) with { Caption = s.Caption }, "");
        }
    }
}
