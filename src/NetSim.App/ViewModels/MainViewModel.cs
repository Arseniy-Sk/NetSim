using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NetSim.App.Localization;
using NetSim.App.Services;
using NetSim.App.Views;
using NetSim.Core.Catalog;
using NetSim.Core.Model;
using NetSim.Core.Samples;
using NetSim.Core.Serialization;
using NetSim.Core.Topology;
using NetSim.Protocols;
using NetSim.Simulation.Traffic;

namespace NetSim.App.ViewModels;

public enum ToolMode { Select, Link, Traffic, Delete, Pdu, Wall, Room }

public enum SimMode { Traffic, Protocol }

public sealed record LogEntry(DateTime Time, string Text, bool IsError)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

public sealed record BreadcrumbItem(Level Level, string Title);

/// <summary>Значения множителя скорости моделирования для списка на панели инструментов.</summary>
public static class SpeedValues
{
    public static readonly double S01 = 0.1, S05 = 0.5, S1 = 1, S5 = 5, S20 = 20, S100 = 100;
}

/// <summary>Главная модель представления: проект, текущий уровень, инструменты, моделирование.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public DeviceCatalog Catalog { get; } = DeviceCatalog.Default;
    public AppSettings Settings { get; }
    public UndoStack Undo { get; } = new();

    public ObservableCollection<CatalogNode> CatalogTree { get; }
    public ObservableCollection<LogEntry> Messages { get; } = new();
    public ObservableCollection<ProtocolEvent> ProtocolEvents { get; } = new();
    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = new();

    [ObservableProperty] private Project _project = new();
    [ObservableProperty] private Level _currentLevel = null!;
    [ObservableProperty] private object? _selected;
    [ObservableProperty] private ToolMode _tool = ToolMode.Select;
    [ObservableProperty] private SimMode _mode = SimMode.Traffic;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _speed = 1;
    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _catalogFilter = "";
    [ObservableProperty] private bool _showFlows;
    [ObservableProperty] private bool _showLinkLabels;
    [ObservableProperty] private bool _showCoverage = true;
    [ObservableProperty] private string _statusText = "";

    public TrafficSimulator? TrafficSim { get; private set; }
    public ProtocolSimulator? ProtoSim { get; private set; }

    /// <summary>Просьба к представлению перерисовать схему.</summary>
    public event Action? Redraw;

    private readonly DispatcherTimer _timer;
    private DateTime _lastTick;

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        Loc.I.Lang = Settings.Language;
        foreach (var (id, color) in Settings.MediaColors)
            if (Catalog.Media(id) is { } m) m.Color = color;
        _showFlows = Settings.ShowFlows;
        _showLinkLabels = Settings.ShowLinkLabels;
        CatalogTree = CatalogNode.Build(Catalog);
        NewProjectCore(new Project { Name = Loc.T("Project_Untitled") });
        Loc.I.LanguageChanged += () =>
        {
            foreach (var n in CatalogTree) n.RefreshTexts();
            RefreshBreadcrumbs();
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(SimTimeText));
            OnPropertyChanged(nameof(SelectionTitle));
            Redraw?.Invoke();
        };

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Log(Loc.T("Log_Welcome"));
    }

    public string Lang => Loc.I.Lang;

    public string WindowTitle => $"{(string.IsNullOrEmpty(Project.Name) ? Loc.T("Project_Untitled") : Project.Name)}{(IsDirty ? " *" : "")} — NetSim";

    public Device? OwnerOfCurrentLevel => Project.OwnerOf(CurrentLevel);

    public string SimTimeText
    {
        get
        {
            double t = Mode == SimMode.Traffic ? TrafficSim?.Now ?? 0 : ProtoSim?.Engine.Now ?? 0;
            return Loc.F("Status_SimTime", t.ToString("0.00"));
        }
    }

    public string SelectionTitle => Selected switch
    {
        Device d => d.Name,
        PlanShape ps => string.IsNullOrEmpty(ps.Label) ? Loc.T(ps.Kind == PlanShapeKind.Room ? "Plan_Room" : "Plan_Wall") : ps.Label,
        Link l => $"{Project.FindDevice(l.AId)?.Name} — {Project.FindDevice(l.BId)?.Name}",
        _ => Loc.T("Props_NothingSelected"),
    };

    partial void OnSelectedChanged(object? value)
    {
        OnPropertyChanged(nameof(SelectionTitle));
        Redraw?.Invoke();
    }

    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(WindowTitle));
    partial void OnProjectChanged(Project value) => OnPropertyChanged(nameof(WindowTitle));
    partial void OnCatalogFilterChanged(string value) { foreach (var n in CatalogTree) n.ApplyFilter(value); }

    partial void OnShowFlowsChanged(bool value) { Settings.ShowFlows = value; Settings.Save(); Redraw?.Invoke(); }
    partial void OnShowLinkLabelsChanged(bool value) { Settings.ShowLinkLabels = value; Settings.Save(); Redraw?.Invoke(); }
    partial void OnShowCoverageChanged(bool value) => Redraw?.Invoke();

    partial void OnModeChanged(SimMode value)
    {
        StopSimulation(silent: true);
        if (value == SimMode.Protocol) EnsureProtocolSim();
        if (value == SimMode.Traffic && Tool == ToolMode.Pdu) Tool = ToolMode.Select;
        Log(value == SimMode.Traffic ? Loc.T("Log_ModeTraffic") : Loc.T("Log_ModeProtocol"));
        OnPropertyChanged(nameof(IsProtocolMode));
        OnPropertyChanged(nameof(SimTimeText));
        Redraw?.Invoke();
    }

    public bool IsProtocolMode
    {
        get => Mode == SimMode.Protocol;
        set => Mode = value ? SimMode.Protocol : SimMode.Traffic;
    }

    partial void OnCurrentLevelChanged(Level value)
    {
        RefreshBreadcrumbs();
        OnPropertyChanged(nameof(OwnerOfCurrentLevel));
        Selected = null;
        Redraw?.Invoke();
    }

    public void RequestRedraw() => Redraw?.Invoke();

    /// <summary>Обновить индикатор модельного времени (после прокрутки моделирования из кода).</summary>
    public void RefreshSimTime() => OnPropertyChanged(nameof(SimTimeText));

    public void Log(string text, bool error = false)
    {
        Messages.Insert(0, new LogEntry(DateTime.Now, text, error));
        if (Messages.Count > 500) Messages.RemoveAt(Messages.Count - 1);
        StatusText = text;
    }

    // ---------------- Проект ----------------

    private void NewProjectCore(Project project, string? path = null)
    {
        StopSimulation(silent: true);
        TrafficSim = null;
        ProtoSim = null;
        Project = project;
        FilePath = path;
        CurrentLevel = project.Root;
        Selected = null;
        IsDirty = false;
        Undo.Clear();
        ProtocolEvents.Clear();
        if (Mode == SimMode.Protocol) EnsureProtocolSim();
        RefreshBreadcrumbs();
        Redraw?.Invoke();
    }

    /// <summary>Снимок перед изменением (для отмены) и пометка «изменён».</summary>
    public void Checkpoint()
    {
        Undo.Push(Project);
        IsDirty = true;
    }

    /// <summary>Топология изменилась — модели надо перестроить.</summary>
    public void TopologyChanged()
    {
        IsDirty = true;
        if (TrafficSim != null)
        {
            bool wasRunning = IsRunning;
            TrafficSim = null;
            if (wasRunning)
            {
                Log(Loc.T("Log_SimRestarted"));
                StartTrafficSim();
            }
        }
        if (ProtoSim != null) ProtoSim.Rebuild();
        OnPropertyChanged(nameof(SimTimeText));
        Redraw?.Invoke();
    }

    private bool ConfirmDiscard()
    {
        if (!IsDirty) return true;
        var r = MessageBox.Show(Loc.T("Msg_SaveChanges"), "NetSim", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return SaveCore(FilePath);
        return true;
    }

    public bool CanClose() => ConfirmDiscard();

    [RelayCommand]
    private void NewProject()
    {
        if (!ConfirmDiscard()) return;
        NewProjectCore(new Project { Name = Loc.T("Project_Untitled") });
        Log(Loc.T("Log_NewProject"));
    }

    [RelayCommand]
    private void OpenProject()
    {
        if (!ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = Loc.T("File_Filter"), DefaultExt = ProjectSerializer.Extension };
        if (dlg.ShowDialog() != true) return;
        OpenFile(dlg.FileName);
    }

    public void OpenFile(string path)
    {
        try
        {
            var p = ProjectSerializer.Load(path);
            NewProjectCore(p, path);
            Settings.AddRecent(path);
            OnPropertyChanged(nameof(RecentFiles));
            Log(Loc.F("Log_Opened", path));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Msg_OpenFailed", ex.Message), "NetSim", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public IReadOnlyList<string> RecentFiles => Settings.RecentFiles;

    [RelayCommand]
    private void OpenRecent(string path)
    {
        if (!ConfirmDiscard()) return;
        if (!File.Exists(path))
        {
            Settings.RecentFiles.Remove(path);
            Settings.Save();
            OnPropertyChanged(nameof(RecentFiles));
            Log(Loc.F("Msg_OpenFailed", path), true);
            return;
        }
        OpenFile(path);
    }

    [RelayCommand]
    private void OpenSample(string id)
    {
        if (!ConfirmDiscard()) return;
        var p = SampleProjects.Create(id);
        NewProjectCore(p);
        if (id == "ip-demo") Mode = SimMode.Protocol;
        Log(Loc.F("Log_SampleOpened", p.Name));
    }

    [RelayCommand]
    private void Save() => SaveCore(FilePath);

    [RelayCommand]
    private void SaveAs() => SaveCore(null);

    private bool SaveCore(string? path)
    {
        if (path == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = Loc.T("File_Filter"), DefaultExt = ProjectSerializer.Extension,
                FileName = string.IsNullOrWhiteSpace(Project.Name) ? "network" : Project.Name,
            };
            if (dlg.ShowDialog() != true) return false;
            path = dlg.FileName;
        }
        try
        {
            if (string.IsNullOrWhiteSpace(Project.Name) || Project.Name == Loc.T("Project_Untitled"))
                Project.Name = Path.GetFileNameWithoutExtension(path);
            ProjectSerializer.Save(Project, path);
            FilePath = path;
            IsDirty = false;
            Settings.AddRecent(path);
            OnPropertyChanged(nameof(RecentFiles));
            OnPropertyChanged(nameof(WindowTitle));
            Log(Loc.F("Log_Saved", path));
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Msg_SaveFailed", ex.Message), "NetSim", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    [RelayCommand]
    private void ProjectSettings()
    {
        var w = new ProjectSettingsWindow(Project) { Owner = Application.Current.MainWindow };
        Checkpoint();
        if (w.ShowDialog() == true) { OnPropertyChanged(nameof(WindowTitle)); TopologyChanged(); }
    }

    [RelayCommand]
    private void UndoAction()
    {
        var p = Undo.Undo(Project);
        if (p != null) RestoreSnapshot(p);
    }

    [RelayCommand]
    private void RedoAction()
    {
        var p = Undo.Redo(Project);
        if (p != null) RestoreSnapshot(p);
    }

    private void RestoreSnapshot(Project p)
    {
        var levelId = CurrentLevel.Id;
        StopSimulation(silent: true);
        TrafficSim = null;
        Project = p;
        CurrentLevel = p.AllLevels().FirstOrDefault(l => l.Id == levelId) ?? p.Root;
        if (ProtoSim != null) ProtoSim = new ProtocolSimulator(Project, Catalog);
        IsDirty = true;
        RefreshBreadcrumbs();
        Redraw?.Invoke();
    }

    // ---------------- Уровни (ПР6) ----------------

    private void RefreshBreadcrumbs()
    {
        Breadcrumbs.Clear();
        var chain = new List<Level>();
        Level? level = CurrentLevel;
        while (level != null)
        {
            chain.Insert(0, level);
            var owner = Project.OwnerOf(level);
            level = owner == null ? null : Project.FindLevelOf(owner);
        }
        foreach (var l in chain)
        {
            var owner = Project.OwnerOf(l);
            Breadcrumbs.Add(new BreadcrumbItem(l, owner?.Name ?? (string.IsNullOrWhiteSpace(l.Name) ? Loc.T("Level_Root") : l.Name)));
        }
        OnPropertyChanged(nameof(CanGoUp));
    }

    public bool CanGoUp => CurrentLevel != Project.Root;

    public void OpenContainer(Device container)
    {
        if (container.ChildLevel == null)
        {
            Checkpoint();
            container.ChildLevel = new Level { Name = container.Name, Background = Catalog.Device(container.CatalogId)?.ChildBackground ?? "plain" };
        }
        CurrentLevel = container.ChildLevel;
        Log(Loc.F("Log_LevelOpened", container.Name));
    }

    [RelayCommand]
    private void GoUp()
    {
        var owner = OwnerOfCurrentLevel;
        if (owner == null) return;
        CurrentLevel = Project.FindLevelOf(owner) ?? Project.Root;
        Selected = owner;
    }

    [RelayCommand]
    private void GoToLevel(Level level) => CurrentLevel = level;

    // ---------------- Редактирование ----------------

    public Device? AddDevice(string catalogId, double x, double y)
    {
        Checkpoint();
        var b = new TopologyBuilder(Project, Catalog) { Language = Lang };
        var d = b.CreateDevice(catalogId, x, y, CurrentLevel);
        Selected = d;
        TopologyChanged();
        Log(Loc.F("Log_DeviceAdded", d.Name));
        return d;
    }

    public void InstallSoftware(Device device, string softwareId)
    {
        if (device.IsContainer || device.Kind.IsSharedMedium())
        {
            Log(Loc.F("Log_SoftwareNotAllowed", device.Name), true);
            return;
        }
        Checkpoint();
        var b = new TopologyBuilder(Project, Catalog);
        b.InstallSoftware(device, softwareId);
        var sw = Catalog.SoftwareById(softwareId);
        Log(Loc.F("Log_SoftwareInstalled", sw?.Name(Lang) ?? softwareId, device.Name));
        Selected = device;
        TopologyChanged();
    }

    public void RequestLink(Device a, Device b)
    {
        if (a == b) return;
        var w = new LinkAssistantWindow(this, a, b, null) { Owner = Application.Current.MainWindow };
        if (w.ShowDialog() != true) return;
        Checkpoint();
        var builder = new TopologyBuilder(Project, Catalog);
        var level = builder.FindLinkLevel(a, b);
        var link = builder.Connect(a, b, w.StandardId, w.LengthM, level, w.MediaId);
        link.BandwidthBps = w.BandwidthBps;
        Selected = link;
        TopologyChanged();
        Log(Loc.F("Log_Linked", a.Name, b.Name, Catalog.Standard(link.StandardId)?.Name(Lang) ?? link.StandardId));
    }

    public void EditLink(Link link)
    {
        var a = Project.FindDevice(link.AId);
        var b = Project.FindDevice(link.BId);
        if (a == null || b == null) return;
        var w = new LinkAssistantWindow(this, a, b, link) { Owner = Application.Current.MainWindow };
        if (w.ShowDialog() != true) return;
        Checkpoint();
        link.StandardId = w.StandardId;
        link.MediaId = w.MediaId;
        link.BandwidthBps = w.BandwidthBps;
        link.LengthM = w.LengthM;
        TopologyChanged();
    }

    public void RequestFlow(Device a, Device b)
    {
        if (a == b) return;
        if (a.IsContainer || b.IsContainer)
        {
            Log(Loc.T("Log_FlowContainer"), true);
            return;
        }
        var w = new ProfilesWindow(this, a, b) { Owner = Application.Current.MainWindow };
        if (w.ShowDialog() != true || w.Result == null) return;
        Checkpoint();
        var builder = new TopologyBuilder(Project, Catalog) { Language = Lang };
        var (flow, error) = builder.AddFlow(a, b, w.Result);
        if (flow == null)
        {
            MessageBox.Show(error, "NetSim", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Log(Loc.F("Log_FlowAdded", a.Name, b.Name, flow.Profile.Name(Lang)));
        TopologyChanged();
    }

    public void EditDevice(Device d)
    {
        Checkpoint();
        var w = new DeviceConfigWindow(this, d) { Owner = Application.Current.MainWindow };
        if (w.ShowDialog() != true)
        {
            UndoAction(); // «Отмена» — откатить все изменения, сделанные в окне
            return;
        }
        TopologyChanged();
        OnPropertyChanged(nameof(SelectionTitle));
    }

    public void ShowStatisticsItems(object target)
    {
        var w = new StatisticsItemsWindow(this, target) { Owner = Application.Current.MainWindow };
        if (w.ShowDialog() == true)
        {
            IsDirty = true;
            Redraw?.Invoke();
        }
    }

    public void Rename(Device d)
    {
        var name = InputWindow.Ask(Loc.T("Dlg_Rename"), Loc.T("Props_Name"), d.Name);
        if (string.IsNullOrWhiteSpace(name) || name == d.Name) return;
        Checkpoint();
        d.Name = name.Trim();
        if (d.ChildLevel != null) d.ChildLevel.Name = d.Name;
        RefreshBreadcrumbs();
        OnPropertyChanged(nameof(SelectionTitle));
        Redraw?.Invoke();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Selected is Device d) Delete(d);
        else if (Selected is Link l) Delete(l);
        else if (Selected is PlanShape ps) Delete(ps);
    }

    // ---------------- План помещения (ПР6, ПР10) ----------------

    public void AddPlanShape(PlanShape shape)
    {
        Checkpoint();
        CurrentLevel.Plan.Shapes.Add(shape);
        CurrentLevel.Plan.IsVisible = true;
        Selected = shape;
        Redraw?.Invoke();
    }

    public void Delete(PlanShape shape)
    {
        Checkpoint();
        CurrentLevel.Plan.Shapes.Remove(shape);
        Selected = null;
        Redraw?.Invoke();
    }

    public void RenameShape(PlanShape shape)
    {
        var name = InputWindow.Ask(Loc.T("Dlg_Rename"), Loc.T("Props_Name"), shape.Label);
        if (name == null) return;
        Checkpoint();
        shape.Label = name.Trim();
        Redraw?.Invoke();
    }

    [RelayCommand]
    private void PlanWizard()
    {
        var w = new PlanWizardWindow(CurrentLevel.Plan) { Owner = Application.Current.MainWindow };
        Checkpoint();
        if (w.ShowDialog() == true) { Log(Loc.T("Log_PlanCreated")); Redraw?.Invoke(); }
        else UndoAction();
    }

    [RelayCommand]
    private void PlanSettings()
    {
        var w = new PlanSettingsWindow(CurrentLevel.Plan) { Owner = Application.Current.MainWindow };
        Checkpoint();
        if (w.ShowDialog() == true) Redraw?.Invoke();
        else UndoAction();
    }

    [RelayCommand]
    private void ClearPlan()
    {
        if (CurrentLevel.Plan.Shapes.Count == 0) return;
        if (MessageBox.Show(Loc.T("Msg_ClearPlan"), "NetSim", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Checkpoint();
        CurrentLevel.Plan.Shapes.Clear();
        Selected = null;
        Redraw?.Invoke();
    }

    [RelayCommand]
    private void TogglePlan()
    {
        CurrentLevel.Plan.IsVisible = !CurrentLevel.Plan.IsVisible;
        IsDirty = true;
        Redraw?.Invoke();
    }

    [RelayCommand]
    private void ShowCoverageAnalysis() => new CoverageWindow(this) { Owner = Application.Current.MainWindow }.Show();

    public void Delete(Device d)
    {
        Checkpoint();
        Project.RemoveDevice(d);
        Selected = null;
        TopologyChanged();
        Log(Loc.F("Log_Deleted", d.Name));
    }

    public void Delete(Link l)
    {
        Checkpoint();
        foreach (var level in Project.AllLevels()) level.Links.Remove(l);
        Selected = null;
        TopologyChanged();
        Log(Loc.T("Log_LinkDeleted"));
    }

    public void DeleteFlow(TrafficFlow f)
    {
        Checkpoint();
        Project.Flows.Remove(f);
        TopologyChanged();
    }

    // ---------------- Моделирование ----------------

    private void StartTrafficSim()
    {
        TrafficSim ??= new TrafficSimulator(Project, Catalog, Lang);
        foreach (var d in TrafficSim.Diagnostics) Log(d.Message, true);
        if (Project.Flows.Count == 0) Log(Loc.T("Log_NoFlows"), true);
        IsRunning = true;
        _lastTick = DateTime.Now;
    }

    private void EnsureProtocolSim()
    {
        if (ProtoSim != null) return;
        ProtoSim = new ProtocolSimulator(Project, Catalog) { ProcessingDelay = 0.25 };
        ProtoSim.EventLogged += e =>
        {
            ProtocolEvents.Insert(0, e);
            if (ProtocolEvents.Count > 1000) ProtocolEvents.RemoveAt(ProtocolEvents.Count - 1);
        };
    }

    [RelayCommand]
    private void Run()
    {
        if (Mode == SimMode.Traffic)
        {
            StartTrafficSim();
            Log(Loc.T("Log_SimStarted"));
        }
        else
        {
            EnsureProtocolSim();
            IsRunning = true;
            _lastTick = DateTime.Now;
        }
    }

    [RelayCommand]
    private void Pause()
    {
        if (!IsRunning) return;
        IsRunning = false;
        Log(Loc.T("Log_SimPaused"));
    }

    [RelayCommand]
    private void Stop() => StopSimulation(silent: false);

    private void StopSimulation(bool silent)
    {
        if (!IsRunning) return;
        IsRunning = false;
        if (!silent) Log(Loc.T("Log_SimStopped"));
        Redraw?.Invoke();
    }

    [RelayCommand]
    private void ResetSimulation()
    {
        IsRunning = false;
        TrafficSim = null;
        ProtoSim?.Rebuild();
        ProtocolEvents.Clear();
        OnPropertyChanged(nameof(SimTimeText));
        Log(Loc.T("Log_SimReset"));
        Redraw?.Invoke();
    }

    private void Tick()
    {
        var now = DateTime.Now;
        var dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        if (!IsRunning) return;
        dt = Math.Min(dt, 0.25) * Speed;
        if (Mode == SimMode.Traffic && TrafficSim != null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            TrafficSim.RunUntil(TrafficSim.Now + dt);
            if (sw.ElapsedMilliseconds > 500 && Speed > 1)
            {
                Speed = Math.Max(1, Speed / 2);
                Log(Loc.T("Log_SpeedReduced"), true);
            }
        }
        else if (Mode == SimMode.Protocol && ProtoSim != null)
        {
            ProtoSim.Engine.RunUntil(ProtoSim.Engine.Now + dt);
        }
        OnPropertyChanged(nameof(SimTimeText));
        Redraw?.Invoke();
    }

    public void SendPdu(Device a, Device b)
    {
        EnsureProtocolSim();
        var session = ProtoSim!.SendSimplePdu(a, b);
        if (session == null)
        {
            Log(Loc.T("Log_PduNoIp"), true);
            return;
        }
        session.Updated += s =>
        {
            var r = s.Results.LastOrDefault();
            Log(r.Rtt.HasValue ? Loc.F("Log_PduOk", a.Name, b.Name) : Loc.F("Log_PduFail", a.Name, b.Name, r.Error ?? ""), !r.Rtt.HasValue);
        };
        if (!IsRunning) Run();
        Log(Loc.F("Log_PduSent", a.Name, b.Name));
    }

    public void OpenConsole(Device d)
    {
        EnsureProtocolSim();
        var w = new ConsoleWindow(this, d) { Owner = Application.Current.MainWindow };
        w.Show();
    }

    // ---------------- Окна ----------------

    [RelayCommand]
    private void ShowDataFlow() => new DataFlowWindow(this) { Owner = Application.Current.MainWindow }.ShowDialog();

    [RelayCommand]
    private void ShowReport() => new StatisticsReportWindow(this) { Owner = Application.Current.MainWindow }.Show();

    [RelayCommand]
    private void ShowCalculators() => new CalculatorsWindow(this) { Owner = Application.Current.MainWindow }.Show();

    [RelayCommand]
    private void ShowMediaColors()
    {
        new MediaColorsWindow(this) { Owner = Application.Current.MainWindow }.ShowDialog();
        Redraw?.Invoke();
    }

    [RelayCommand]
    private void ShowHelp() => new HelpWindow { Owner = Application.Current.MainWindow }.Show();

    [RelayCommand]
    private void ShowAbout() => MessageBox.Show(Loc.T("About_Text"), "NetSim", MessageBoxButton.OK, MessageBoxImage.Information);

    [RelayCommand]
    private void SetLanguage(string lang)
    {
        Loc.I.Lang = lang;
        Settings.Language = lang;
        Settings.Save();
    }

    [RelayCommand]
    private void SetTool(ToolMode tool)
    {
        if (tool == ToolMode.Pdu && Mode != SimMode.Protocol) Mode = SimMode.Protocol;
        Tool = tool;
    }

    [RelayCommand]
    private void ConsoleForSelected()
    {
        if (Selected is Device d && !d.IsContainer) OpenConsole(d);
        else Log(Loc.T("Log_SelectDevice"), true);
    }

    [RelayCommand]
    private void ConfigureSelected()
    {
        if (Selected is Device d) { if (d.IsContainer) OpenContainer(d); else EditDevice(d); }
        else if (Selected is Link l) EditLink(l);
    }

    [RelayCommand]
    private void StatisticsForSelected()
    {
        if (Selected != null) ShowStatisticsItems(Selected);
    }
}
