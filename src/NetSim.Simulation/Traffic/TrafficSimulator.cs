using NetSim.Core.Catalog;
using NetSim.Core.Model;
using NetSim.Core.Topology;
using NetSim.Simulation.Engine;

namespace NetSim.Simulation.Traffic;

/// <summary>Пакет, находящийся сейчас в линии — для анимации.</summary>
public sealed class InFlightPacket
{
    public TrafficFlow Flow { get; init; } = null!;
    public bool IsReply { get; init; }
    public PathHop Hop { get; init; } = null!;
    public double StartTime { get; init; }
    public double EndTime { get; init; }
    public int Bytes { get; init; }

    /// <summary>Доля пройденного пути участка (0..1).</summary>
    public double Progress(double now) => EndTime <= StartTime ? 1 : Math.Clamp((now - StartTime) / (EndTime - StartTime), 0, 1);
}

public sealed class SimDiagnostic
{
    public string Message { get; init; } = "";
    public TrafficFlow? Flow { get; init; }
}

/// <summary>
/// Моделирование производительности сети (режим NetCracker).
/// Клиент по профилю посылает запросы; сервер через Reply Delay отвечает пакетом Reply Size;
/// пакеты занимают каналы на время передачи, отсюда загрузка, очереди и потери.
/// </summary>
public sealed class TrafficSimulator
{
    /// <summary>Накладные расходы канального уровня на пакет, байт (заголовок + преамбула + IFG).</summary>
    public const int FrameOverheadBytes = 38;

    /// <summary>Доля номинальной скорости, реально доступная в радиоканале (протокол 802.11, CSMA/CA).</summary>
    public const double WirelessEfficiency = 0.45;

    private readonly SimEngine _engine = new();
    private readonly Project _project;
    private readonly DeviceCatalog _catalog;
    private readonly string _lang;
    private NetworkGraph _graph = null!;
    private Random _rng = null!;

    private readonly Dictionary<string, DeviceStats> _deviceStats = new();
    private readonly Dictionary<(Channel, int), Transmitter> _transmitters = new();
    private readonly HashSet<InFlightPacket> _inFlight = new();
    private readonly List<SimDiagnostic> _diagnostics = new();
    private readonly Dictionary<(string, string), List<PathHop>?> _routeCache = new();

    public TrafficSimulator(Project project, DeviceCatalog? catalog = null, string lang = "ru")
    {
        _project = project;
        _catalog = catalog ?? DeviceCatalog.Default;
        _lang = lang;
        Reset();
    }

    public double Now => _engine.Now;
    public NetworkGraph Graph => _graph;
    public IReadOnlyCollection<InFlightPacket> InFlight => _inFlight;
    public IReadOnlyList<SimDiagnostic> Diagnostics => _diagnostics;
    public long ProcessedEvents => _engine.ProcessedEvents;

    private double Window => _project.Simulation.StatisticsWindowSec;

    /// <summary>Перестроить модель по текущему проекту и обнулить статистику.</summary>
    public void Reset()
    {
        _engine.Reset();
        _graph = new NetworkGraph(_project, _catalog);
        _rng = new Random(_project.Simulation.Seed);
        _deviceStats.Clear();
        _transmitters.Clear();
        _inFlight.Clear();
        _diagnostics.Clear();
        _routeCache.Clear();

        foreach (var d in _graph.Devices.Values) _deviceStats[d.Id] = new DeviceStats(Window);

        foreach (var flow in _project.Flows.Where(f => f.IsActive))
        {
            if (!_graph.Devices.ContainsKey(flow.SourceId) || !_graph.Devices.ContainsKey(flow.TargetId)) continue;
            var src = _graph.Devices[flow.SourceId];
            var dst = _graph.Devices[flow.TargetId];
            if (Route(src.Id, dst.Id) == null)
            {
                _diagnostics.Add(new SimDiagnostic
                {
                    Flow = flow,
                    Message = _lang == "en"
                        ? $"No route from \"{src.Name}\" to \"{dst.Name}\" — check links."
                        : $"Нет маршрута от «{src.Name}» к «{dst.Name}» — проверьте соединения.",
                });
                continue;
            }
            if (flow.Profile.Pattern == TrafficPattern.ClientServer && Route(dst.Id, src.Id) == null) continue;

            // Первая транзакция — в случайный момент первого интервала, чтобы потоки не стартовали одновременно.
            var first = flow.Profile.TimeBetween.Sample(_rng) * _rng.NextDouble();
            _engine.At(first, () => StartTransaction(flow));
        }
    }

    /// <summary>Продвинуть моделирование до момента <paramref name="time"/>.</summary>
    public void RunUntil(double time) => _engine.RunUntil(time);

    public DeviceStats StatsOf(Device d) => _deviceStats.TryGetValue(d.Id, out var s) ? s : new DeviceStats(Window);

    private List<PathHop>? Route(string from, string to)
    {
        if (_routeCache.TryGetValue((from, to), out var hops)) return hops;
        var path = _graph.FindPath(from, to);
        hops = path == null ? null : _graph.SplitIntoHops(from, path);
        _routeCache[(from, to)] = hops;
        return hops;
    }

    // ---------------- Генерация трафика ----------------

    private void StartTransaction(TrafficFlow flow)
    {
        if (!flow.IsActive) return;
        var src = _graph.Devices[flow.SourceId];
        var dst = _graph.Devices[flow.TargetId];
        var size = Math.Max(1, (long)Math.Round(flow.Profile.TransactionSize.Sample(_rng)));
        StatsOf(src).TransactionsStarted++;
        SendMessage(flow, src, dst, size, isReply: false, requestStart: _engine.Now);

        var gap = flow.Profile.TimeBetween.Sample(_rng);
        if (gap <= 0) gap = 1e-3;
        _engine.After(gap, () => StartTransaction(flow));
    }

    private sealed class Message
    {
        public TrafficFlow Flow = null!;
        public Device Source = null!;
        public Device Target = null!;
        public bool IsReply;
        public double RequestStart;
        public int PacketsLeft;
        public bool Failed;
    }

    private sealed class Packet
    {
        public Message Message = null!;
        public int Bytes;
        public List<PathHop> Hops = null!;
        public int HopIndex;
    }

    private void SendMessage(TrafficFlow flow, Device src, Device dst, long size, bool isReply, double requestStart)
    {
        var hops = Route(src.Id, dst.Id);
        if (hops == null) return;
        int mtu = hops.Count == 0 ? 1500 : hops.Min(h => h.Channel.Mtu);
        int count = (int)Math.Ceiling(size / (double)mtu);
        var msg = new Message
        {
            Flow = flow, Source = src, Target = dst, IsReply = isReply, RequestStart = requestStart, PacketsLeft = count,
        };
        long left = size;
        for (int i = 0; i < count; i++)
        {
            int bytes = (int)Math.Min(mtu, left);
            left -= bytes;
            var p = new Packet { Message = msg, Bytes = bytes, Hops = hops };
            StatsOf(src).PacketsSent++;
            StatsOf(src).BytesSent += bytes;
            StatsOf(src).Workload.Add(_engine.Now, bytes);
            if (hops.Count == 0) Arrive(p); else EnterHop(p);
        }
    }

    // ---------------- Передача по каналам ----------------

    private sealed class Transmitter
    {
        public readonly Queue<(Packet Packet, PathHop Hop)> Queue = new();
        public bool Busy;
        public double BusySince = double.NaN;
        public TransmitterStats Stats;
        public Transmitter(double window) => Stats = new TransmitterStats(window);
    }

    /// <summary>
    /// Передатчик канала. Общая среда (хаб, шина, кольцо, радио) — один передатчик на всех.
    /// Отдельная линия — дуплекс: по передатчику на каждое направление.
    /// </summary>
    private Transmitter TransmitterFor(PathHop hop)
    {
        int dir = 0;
        if (!hop.Channel.IsShared)
            dir = string.CompareOrdinal(hop.FromId, hop.Links[0].Other(hop.FromId)) < 0 ? 0 : 1;
        var key = (hop.Channel, dir);
        if (!_transmitters.TryGetValue(key, out var tx)) _transmitters[key] = tx = new Transmitter(Window);
        return tx;
    }

    private double EffectiveBandwidth(Channel ch)
    {
        var bw = ch.BandwidthBps;
        var std = ch.Links.Count > 0 ? _graph.StandardOf(ch.Links[0]) : null;
        if (std?.Family == TechFamily.Wireless) bw *= WirelessEfficiency;
        return Math.Max(bw, 1);
    }

    private void EnterHop(Packet p)
    {
        var hop = p.Hops[p.HopIndex];
        var tx = TransmitterFor(hop);
        if (tx.Queue.Count >= _project.Simulation.MaxQueuePackets)
        {
            tx.Stats.Dropped++;
            StatsOf(_graph.Devices[hop.FromId]).PacketsDropped++;
            p.Message.Failed = true;
            return;
        }
        tx.Queue.Enqueue((p, hop));
        tx.Stats.MaxQueue = Math.Max(tx.Stats.MaxQueue, tx.Queue.Count);
        if (!tx.Busy) StartNext(tx);
    }

    private void StartNext(Transmitter tx)
    {
        if (tx.Queue.Count == 0) { tx.Busy = false; tx.BusySince = double.NaN; return; }
        var (p, hop) = tx.Queue.Dequeue();
        tx.Busy = true;
        double start = _engine.Now;
        tx.BusySince = start;
        double bits = (p.Bytes + FrameOverheadBytes) * 8.0;
        double txTime = bits / EffectiveBandwidth(hop.Channel);
        double propagation = hop.Links.Sum(l => l.LengthM * (_graph.StandardOf(l)?.PropagationNsPerM ?? 5)) * 1e-9;

        tx.Stats.Packets++;
        tx.Stats.Bytes += p.Bytes;
        tx.Stats.Bits.Add(start, bits);

        var flight = new InFlightPacket
        {
            Flow = p.Message.Flow, IsReply = p.Message.IsReply, Hop = hop, Bytes = p.Bytes,
            StartTime = start, EndTime = start + txTime + propagation,
        };
        _inFlight.Add(flight);

        _engine.After(txTime, () =>
        {
            tx.Stats.Busy.AddBusy(start, _engine.Now);
            StartNext(tx);
        });
        _engine.After(txTime + propagation, () =>
        {
            _inFlight.Remove(flight);
            OnHopDone(p);
        });
    }

    private void OnHopDone(Packet p)
    {
        p.HopIndex++;
        if (p.HopIndex >= p.Hops.Count) { Arrive(p); return; }

        // Транзитное устройство: задержка обработки (коммутация, маршрутизация, облако).
        var at = _graph.Devices[p.Hops[p.HopIndex].FromId];
        StatsOf(at).PacketsForwarded++;
        var delayMs = _catalog.Device(at.CatalogId)?.ProcessingDelayMs ?? 0;
        if (delayMs > 0) _engine.After(delayMs / 1000.0, () => EnterHop(p));
        else EnterHop(p);
    }

    private void Arrive(Packet p)
    {
        var msg = p.Message;
        var st = StatsOf(msg.Target);
        st.PacketsReceived++;
        st.BytesReceived += p.Bytes;
        st.Workload.Add(_engine.Now, p.Bytes);
        if (--msg.PacketsLeft > 0 || msg.Failed) return;

        if (!msg.IsReply)
        {
            if (msg.Flow.Profile.Pattern == TrafficPattern.ClientServer)
            {
                // Сервер обрабатывает запрос: ждёт Reply Delay и отвечает пакетом Reply Size.
                var server = msg.Target;
                st.RequestsServed++;
                var delay = server.Server.ReplyDelay.Sample(_rng);
                var size = Math.Max(1, (long)Math.Round(server.Server.ReplySize.Sample(_rng)));
                _engine.After(delay, () => SendMessage(msg.Flow, server, msg.Source, size, isReply: true, msg.RequestStart));
            }
            else
            {
                var src = StatsOf(msg.Source);
                src.TransactionsCompleted++;
                src.TotalResponseTime += _engine.Now - msg.RequestStart;
            }
        }
        else
        {
            var client = StatsOf(msg.Target);
            client.TransactionsCompleted++;
            client.TotalResponseTime += _engine.Now - msg.RequestStart;
        }
    }

    // ---------------- Показатели для интерфейса ----------------

    private IEnumerable<Transmitter> TransmittersOf(Channel ch) =>
        new[] { 0, 1 }.Select(dir => _transmitters.GetValueOrDefault((ch, dir))).OfType<Transmitter>();

    /// <summary>Средняя загрузка канала (для дуплексной линии — по более загруженному направлению).</summary>
    public double ChannelUtilization(Channel ch) =>
        TransmittersOf(ch).Select(t => t.Stats.Busy.Average(Now, t.Busy ? t.BusySince : double.NaN)).DefaultIfEmpty(0).Max();

    public double ChannelCurrentUtilization(Channel ch) =>
        TransmittersOf(ch).Select(t => CurrentBusy(t)).DefaultIfEmpty(0).Max();

    private double CurrentBusy(Transmitter t)
    {
        var v = t.Stats.Busy.Current(Now);
        if (t.Busy && !double.IsNaN(t.BusySince))
            v = Math.Min(1, v + Math.Min(Now - t.BusySince, Window) / Math.Min(Window, Math.Max(Now, 1e-9)));
        return v;
    }

    public double ChannelValue(Channel ch, string key)
    {
        var txs = TransmittersOf(ch).ToList();
        return key switch
        {
            StatKeys.AverageUtilization => ChannelUtilization(ch),
            StatKeys.CurrentUtilization => ChannelCurrentUtilization(ch),
            StatKeys.CurrentLoad => txs.Sum(t => t.Stats.Bits.Rate(Now)),
            StatKeys.PacketsCarried => txs.Sum(t => t.Stats.Packets),
            StatKeys.BytesCarried => txs.Sum(t => t.Stats.Bytes),
            StatKeys.PacketsDropped => txs.Sum(t => t.Stats.Dropped),
            StatKeys.MaxQueue => txs.Select(t => t.Stats.MaxQueue).DefaultIfEmpty(0).Max(),
            _ => 0,
        };
    }

    public double LinkValue(Link link, string key) =>
        _graph.Links.Contains(link) ? ChannelValue(_graph.ChannelOf(link), key) : 0;

    public bool IsChannelKey(string key) => StatKeys.ChannelKeys.Contains(key);

    /// <summary>
    /// Значение показателя устройства. Для хаба, сегмента и точки доступа показатели канала
    /// относятся к их домену коллизий; для коммутатора/маршрутизатора — среднее по подключённым линиям.
    /// </summary>
    public double DeviceValue(Device d, string key)
    {
        if (IsChannelKey(key) && key != StatKeys.PacketsDropped)
        {
            var links = _graph.LinksOf(d.Id);
            if (links.Count == 0) return 0;
            var shared = links.Where(l => _graph.IsSharedAttachment(d, l)).Select(_graph.ChannelOf).Distinct().ToList();
            if (shared.Count > 0) return ChannelValue(shared[0], key);
            var values = links.Select(_graph.ChannelOf).Distinct().Select(c => ChannelValue(c, key)).ToList();
            return key is StatKeys.AverageUtilization or StatKeys.CurrentUtilization ? values.Average() : values.Sum();
        }

        var s = StatsOf(d);
        return key switch
        {
            StatKeys.CurrentWorkload => s.Workload.Rate(Now),
            StatKeys.AverageWorkload => Now <= 0 ? 0 : (s.BytesReceived + s.BytesSent) / Now,
            StatKeys.PacketsReceived => s.PacketsReceived,
            StatKeys.PacketsSent => s.PacketsSent,
            StatKeys.BytesReceived => s.BytesReceived,
            StatKeys.BytesSent => s.BytesSent,
            StatKeys.PacketsDropped => s.PacketsDropped,
            StatKeys.RequestsServed => s.RequestsServed,
            StatKeys.Transactions => s.TransactionsCompleted,
            StatKeys.AvgResponseTime => s.AverageResponseTime,
            StatKeys.PacketsForwarded => s.PacketsForwarded,
            _ => 0,
        };
    }

    /// <summary>Перегружена ли линия (средняя загрузка выше порога проекта).</summary>
    public bool IsOverloaded(Link link) =>
        _graph.Links.Contains(link) && ChannelCurrentUtilization(_graph.ChannelOf(link)) >= _project.Simulation.OverloadThreshold;
}
