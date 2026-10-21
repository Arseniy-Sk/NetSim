using System.Globalization;

namespace NetSim.Simulation.Traffic;

/// <summary>Скользящее окно: сумма величины за последние W секунд.</summary>
public sealed class RateMeter
{
    private readonly Queue<(double Time, double Value)> _items = new();
    private double _sum;
    public double Window { get; set; }

    public RateMeter(double window) => Window = window;

    public void Add(double time, double value)
    {
        _items.Enqueue((time, value));
        _sum += value;
    }

    /// <summary>Величина в единицу времени за последнее окно.</summary>
    public double Rate(double now)
    {
        while (_items.Count > 0 && _items.Peek().Time < now - Window) _sum -= _items.Dequeue().Value;
        var w = Math.Min(Window, Math.Max(now, 1e-9));
        return _items.Count == 0 ? 0 : Math.Max(0, _sum) / w;
    }
}

/// <summary>Учёт занятости передатчика: суммарная и в скользящем окне.</summary>
public sealed class BusyMeter
{
    private readonly Queue<(double Start, double End)> _intervals = new();
    public double Window { get; set; }
    public double TotalBusy { get; private set; }

    public BusyMeter(double window) => Window = window;

    public void AddBusy(double start, double end)
    {
        _intervals.Enqueue((start, end));
        TotalBusy += end - start;
    }

    /// <summary>Доля времени занятости за последнее окно (0..1).</summary>
    public double Current(double now)
    {
        double from = now - Window;
        while (_intervals.Count > 0 && _intervals.Peek().End < from) _intervals.Dequeue();
        double busy = 0;
        foreach (var (s, e) in _intervals)
        {
            var a = Math.Max(s, from);
            var b = Math.Min(e, now);
            if (b > a) busy += b - a;
        }
        var w = Math.Min(Window, Math.Max(now, 1e-9));
        return Math.Clamp(busy / w, 0, 1);
    }

    /// <summary>Средняя загрузка с начала моделирования (0..1). Незавершённая передача учитывается до текущего момента.</summary>
    public double Average(double now, double inProgressStart = double.NaN)
    {
        if (now <= 0) return 0;
        double busy = TotalBusy;
        if (!double.IsNaN(inProgressStart) && inProgressStart < now) busy += now - inProgressStart;
        return Math.Clamp(busy / now, 0, 1);
    }
}

public sealed class DeviceStats
{
    public long PacketsReceived;
    public long PacketsSent;
    public long BytesReceived;
    public long BytesSent;
    public long PacketsDropped;
    public long RequestsServed;
    public long TransactionsStarted;
    public long TransactionsCompleted;
    public double TotalResponseTime;
    public long PacketsForwarded;
    public RateMeter Workload;

    public DeviceStats(double window) => Workload = new RateMeter(window);

    public double AverageResponseTime => TransactionsCompleted == 0 ? 0 : TotalResponseTime / TransactionsCompleted;
}

/// <summary>Статистика канала (линии или домена коллизий) в одном направлении передачи.</summary>
public sealed class TransmitterStats
{
    public long Packets;
    public long Bytes;
    public long Dropped;
    public int MaxQueue;
    public BusyMeter Busy;
    public RateMeter Bits;

    public TransmitterStats(double window)
    {
        Busy = new BusyMeter(window);
        Bits = new RateMeter(window);
    }
}

/// <summary>Показатели, которые можно вывести под значком (диалог Statistics Items).</summary>
public static class StatKeys
{
    // Для устройств
    public const string CurrentWorkload = "currentWorkload";
    public const string AverageWorkload = "averageWorkload";
    public const string PacketsReceived = "packetsReceived";
    public const string PacketsSent = "packetsSent";
    public const string BytesReceived = "bytesReceived";
    public const string BytesSent = "bytesSent";
    public const string PacketsDropped = "packetsDropped";
    public const string RequestsServed = "requestsServed";
    public const string Transactions = "transactions";
    public const string AvgResponseTime = "avgResponseTime";
    public const string PacketsForwarded = "packetsForwarded";

    // Для линий и сегментов
    public const string AverageUtilization = "averageUtilization";
    public const string CurrentUtilization = "currentUtilization";
    public const string CurrentLoad = "currentLoad";
    public const string PacketsCarried = "packetsCarried";
    public const string BytesCarried = "bytesCarried";
    public const string MaxQueue = "maxQueue";

    public static readonly string[] DeviceKeys =
    {
        CurrentWorkload, AverageWorkload, PacketsReceived, PacketsSent, BytesReceived, BytesSent,
        RequestsServed, Transactions, AvgResponseTime, PacketsDropped, PacketsForwarded,
    };

    public static readonly string[] ChannelKeys =
    {
        AverageUtilization, CurrentUtilization, CurrentLoad, PacketsCarried, BytesCarried, PacketsDropped, MaxQueue,
    };

    public static string Title(string key, string lang)
    {
        bool ru = lang != "en";
        return key switch
        {
            CurrentWorkload => ru ? "Текущая нагрузка (current workload)" : "Current workload",
            AverageWorkload => ru ? "Средняя нагрузка (average workload)" : "Average workload",
            PacketsReceived => ru ? "Получено пакетов (packets received)" : "Packets received",
            PacketsSent => ru ? "Отправлено пакетов (packets sent)" : "Packets sent",
            BytesReceived => ru ? "Получено байт" : "Bytes received",
            BytesSent => ru ? "Отправлено байт" : "Bytes sent",
            PacketsDropped => ru ? "Потеряно пакетов" : "Packets dropped",
            RequestsServed => ru ? "Обслужено запросов" : "Requests served",
            Transactions => ru ? "Завершено транзакций" : "Transactions completed",
            AvgResponseTime => ru ? "Среднее время ответа" : "Average response time",
            PacketsForwarded => ru ? "Переслано пакетов" : "Packets forwarded",
            AverageUtilization => ru ? "Средний % использования (average utilization)" : "Average utilization",
            CurrentUtilization => ru ? "Текущий % использования (current utilization)" : "Current utilization",
            CurrentLoad => ru ? "Текущая нагрузка канала" : "Current load",
            PacketsCarried => ru ? "Передано пакетов" : "Packets carried",
            BytesCarried => ru ? "Передано байт" : "Bytes carried",
            MaxQueue => ru ? "Макс. очередь, пакетов" : "Max queue, packets",
            _ => key,
        };
    }

    /// <summary>Короткая подпись для вывода на схеме.</summary>
    public static string Format(string key, double value, string lang)
    {
        bool ru = lang != "en";
        var c = CultureInfo.InvariantCulture;
        return key switch
        {
            CurrentWorkload or AverageWorkload => $"{value.ToString("0.0", c)} byte/sec",
            AverageUtilization or CurrentUtilization => $"{(value * 100).ToString("0.00", c)}%",
            CurrentLoad => FormatBits(value),
            AvgResponseTime => $"{value.ToString("0.000", c)} {(ru ? "с" : "s")}",
            PacketsReceived or PacketsSent or PacketsDropped or PacketsCarried or PacketsForwarded => FormatCount(value) + (ru ? " пак." : " packets"),
            BytesReceived or BytesSent or BytesCarried => FormatBytes(value),
            _ => FormatCount(value),
        };
    }

    public static string FormatCount(double v)
    {
        var c = CultureInfo.InvariantCulture;
        if (v >= 1e6) return (v / 1e6).ToString("0.##", c) + "M";
        if (v >= 1e4) return (v / 1e3).ToString("0.#", c) + "K";
        return v.ToString("0", c);
    }

    public static string FormatBytes(double v)
    {
        var c = CultureInfo.InvariantCulture;
        if (v >= 1 << 30) return (v / (1 << 30)).ToString("0.##", c) + " GB";
        if (v >= 1 << 20) return (v / (1 << 20)).ToString("0.##", c) + " MB";
        if (v >= 1 << 10) return (v / (1 << 10)).ToString("0.#", c) + " KB";
        return v.ToString("0", c) + " B";
    }

    public static string FormatBits(double bps)
    {
        var c = CultureInfo.InvariantCulture;
        if (bps >= 1e9) return (bps / 1e9).ToString("0.##", c) + " Gbit/s";
        if (bps >= 1e6) return (bps / 1e6).ToString("0.##", c) + " Mbit/s";
        if (bps >= 1e3) return (bps / 1e3).ToString("0.#", c) + " kbit/s";
        return bps.ToString("0", c) + " bit/s";
    }
}
