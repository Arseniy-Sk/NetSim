using System.Collections.ObjectModel;

namespace NetSim.Core.Model;

public sealed class SimulationSettings : ObservableBase
{
    private int _seed = 12345;
    private double _statisticsWindowSec = 5.0;
    private int _maxQueuePackets = 200;
    private double _overloadThreshold = 0.8;

    /// <summary>Зерно генератора случайных чисел — одинаковое зерно даёт одинаковые результаты.</summary>
    public int Seed { get => _seed; set => Set(ref _seed, value); }

    /// <summary>Окно усреднения для «текущих» показателей (current workload, current utilization), с.</summary>
    public double StatisticsWindowSec { get => _statisticsWindowSec; set => Set(ref _statisticsWindowSec, value); }

    /// <summary>Максимальная очередь пакетов в линии; при переполнении пакеты теряются.</summary>
    public int MaxQueuePackets { get => _maxQueuePackets; set => Set(ref _maxQueuePackets, value); }

    /// <summary>Порог загрузки, выше которого линия считается перегруженной (подсвечивается красным).</summary>
    public double OverloadThreshold { get => _overloadThreshold; set => Set(ref _overloadThreshold, value); }
}

public sealed class Project : ObservableBase
{
    public const int CurrentFormatVersion = 1;

    private string _name = "";
    private string _author = "";
    private string _description = "";

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Author { get => _author; set => Set(ref _author, value); }
    public string Description { get => _description; set => Set(ref _description, value); }

    public Level Root { get; set; } = new() { Name = "Network" };
    public ObservableCollection<TrafficFlow> Flows { get; set; } = new();
    public SimulationSettings Simulation { get; set; } = new();

    /// <summary>Сохранённые результаты расчётов (калькуляторы ПР8, ПР10...) — попадают в отчёт.</summary>
    public ObservableCollection<SavedCalculation> Calculations { get; set; } = new();

    /// <summary>Запомнить расчёт; расчёт с тем же заголовком заменяется.</summary>
    public void RememberCalculation(string title, string text)
    {
        var old = Calculations.FirstOrDefault(c => c.Title == title);
        if (old != null) Calculations.Remove(old);
        Calculations.Add(new SavedCalculation { Title = title, Text = text, Time = DateTime.Now });
    }

    /// <summary>Все уровни (обход в глубину), включая корневой.</summary>
    public IEnumerable<Level> AllLevels()
    {
        var stack = new Stack<Level>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var level = stack.Pop();
            yield return level;
            foreach (var d in level.Devices)
                if (d.ChildLevel != null) stack.Push(d.ChildLevel);
        }
    }

    public IEnumerable<Device> AllDevices() => AllLevels().SelectMany(l => l.Devices);
    public IEnumerable<Link> AllLinks() => AllLevels().SelectMany(l => l.Links);

    public Device? FindDevice(string id) => AllDevices().FirstOrDefault(d => d.Id == id);

    public Level? FindLevelOf(Device device) => AllLevels().FirstOrDefault(l => l.Devices.Contains(device));

    /// <summary>Контейнер, которому принадлежит уровень (null для корня).</summary>
    public Device? OwnerOf(Level level) => AllDevices().FirstOrDefault(d => d.ChildLevel == level);

    /// <summary>Удалить устройство вместе с линиями и потоками (в т.ч. во вложенных уровнях).</summary>
    public void RemoveDevice(Device device)
    {
        var removedIds = new HashSet<string> { device.Id };
        if (device.ChildLevel != null)
        {
            var nested = new Project { Root = device.ChildLevel };
            foreach (var d in nested.AllDevices()) removedIds.Add(d.Id);
        }

        foreach (var level in AllLevels().ToList())
        {
            foreach (var link in level.Links.Where(l => removedIds.Contains(l.AId) || removedIds.Contains(l.BId)).ToList())
                level.Links.Remove(link);
            level.Devices.Remove(device);
        }

        foreach (var flow in Flows.Where(f => removedIds.Contains(f.SourceId) || removedIds.Contains(f.TargetId)).ToList())
            Flows.Remove(flow);
    }

    /// <summary>Имя вида «PC 3», уникальное в проекте.</summary>
    public string UniqueName(string baseName)
    {
        var names = AllDevices().Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!names.Contains(candidate)) return candidate;
        }
    }
}

public sealed class SavedCalculation
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Time { get; set; }
}
