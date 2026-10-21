namespace NetSim.Simulation.Engine;

/// <summary>
/// Дискретно-событийный движок: очередь событий, упорядоченная по модельному времени.
/// Общий для режима трафика (NetCracker) и режима протоколов (Packet Tracer).
/// </summary>
public sealed class SimEngine
{
    private readonly PriorityQueue<Action, (double Time, long Seq)> _queue = new();
    private long _seq;

    /// <summary>Текущее модельное время, с.</summary>
    public double Now { get; private set; }

    public int PendingEvents => _queue.Count;

    public long ProcessedEvents { get; private set; }

    /// <summary>Запланировать событие на абсолютное время.</summary>
    public void At(double time, Action action)
    {
        if (time < Now) time = Now;
        _queue.Enqueue(action, (time, _seq++));
    }

    /// <summary>Запланировать событие через заданную задержку.</summary>
    public void After(double delay, Action action) => At(Now + Math.Max(0, delay), action);

    /// <summary>Выполнить все события до момента <paramref name="time"/> включительно и перевести часы.</summary>
    public void RunUntil(double time, int maxEvents = int.MaxValue)
    {
        int n = 0;
        while (_queue.TryPeek(out _, out var key) && key.Time <= time && n < maxEvents)
        {
            var action = _queue.Dequeue();
            Now = key.Time;
            action();
            ProcessedEvents++;
            n++;
        }
        if (n < maxEvents && time > Now) Now = time;
    }

    /// <summary>Выполнить одно ближайшее событие (пошаговый режим, как Simulation mode в Packet Tracer).</summary>
    public bool Step()
    {
        if (!_queue.TryDequeue(out var action, out var key)) return false;
        Now = key.Time;
        action();
        ProcessedEvents++;
        return true;
    }

    public double? NextEventTime => _queue.TryPeek(out _, out var key) ? key.Time : null;

    public void Reset()
    {
        _queue.Clear();
        _seq = 0;
        Now = 0;
        ProcessedEvents = 0;
    }
}
