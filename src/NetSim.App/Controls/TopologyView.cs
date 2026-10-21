using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NetSim.App.Localization;
using NetSim.App.Rendering;
using NetSim.App.ViewModels;
using NetSim.Calculators;
using NetSim.Core.Model;
using NetSim.Simulation.Traffic;

namespace NetSim.App.Controls;

/// <summary>
/// Рабочее поле: рисует устройства, линии, потоки, пакеты и статистику текущего уровня,
/// обрабатывает инструменты (выбор, Link, Set traffic, удаление, PDU) и перетаскивание из каталога.
/// </summary>
public sealed class TopologyView : FrameworkElement
{
    public const string DragFormat = "NetSim.CatalogItem";
    private const double IconSize = 44;
    private static readonly Point ProxyPosition = new(70, 60);

    private MainViewModel? _vm;
    private Device? _dragDevice;
    private Vector _dragOffset;
    private bool _dragMoved;
    private Point _panStart;
    private Vector _panScrollStart;
    private bool _panning;
    private Device? _pendingSource;
    private Point _mouse;
    private Device? _hover;
    private Point? _drawStart;
    private readonly ToolTip _toolTip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };

    private readonly Typeface _font = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Typeface _fontBold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private readonly Typeface _mono = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public double Zoom { get; private set; } = 1;

    public TopologyView()
    {
        AllowDrop = true;
        Focusable = true;
        ClipToBounds = true;
        ToolTip = _toolTip;
        ToolTipService.SetInitialShowDelay(this, 600);
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        LayoutTransform = new ScaleTransform(1, 1);
    }

    private void Attach(MainViewModel? vm)
    {
        if (_vm != null) _vm.Redraw -= OnRedraw;
        _vm = vm;
        if (_vm != null) _vm.Redraw += OnRedraw;
        OnRedraw();
    }

    private void OnRedraw()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private ScrollViewer? Scroller => Parent as ScrollViewer ?? VisualTreeHelper.GetParent(this) as ScrollViewer;

    public void SetZoom(double zoom)
    {
        Zoom = Math.Clamp(zoom, 0.25, 3);
        ((ScaleTransform)LayoutTransform).ScaleX = Zoom;
        ((ScaleTransform)LayoutTransform).ScaleY = Zoom;
    }

    /// <summary>Подобрать масштаб, чтобы весь уровень поместился в видимую область (не крупнее 100%).</summary>
    public void FitToView()
    {
        if (Level == null || Level.Devices.Count == 0 || Scroller is not { } sv || sv.ViewportWidth < 10) return;
        double maxX = Level.Devices.Max(d => d.X) + 90, maxY = Level.Devices.Max(d => d.Y) + 90;
        SetZoom(Math.Min(1, Math.Min(sv.ViewportWidth / maxX, sv.ViewportHeight / maxY)));
        sv.ScrollToHome();
    }

    public void CancelPending()
    {
        _pendingSource = null;
        InvalidateVisual();
    }

    // ---------------- Геометрия ----------------

    private Level? Level => _vm?.CurrentLevel;
    private Device? Owner => _vm?.OwnerOfCurrentLevel;

    /// <summary>Позиции устройств текущего уровня; контейнер-владелец — значок в левом верхнем углу.</summary>
    private Dictionary<string, Point> Positions()
    {
        var map = new Dictionary<string, Point>();
        if (Level == null) return map;
        foreach (var d in Level.Devices) map[d.Id] = new Point(d.X, d.Y);
        if (Owner != null) map[Owner.Id] = ProxyPosition;
        return map;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = 1600, h = 1000;
        if (Level != null)
            foreach (var d in Level.Devices) { w = Math.Max(w, d.X + 300); h = Math.Max(h, d.Y + 300); }
        if (Level != null)
            foreach (var sh in Level.Plan.Shapes) { w = Math.Max(w, Math.Max(sh.X1, sh.X2) + 300); h = Math.Max(h, Math.Max(sh.Y1, sh.Y2) + 300); }
        return new Size(w, h);
    }

    private Device? HitDevice(Point p)
    {
        if (Level == null) return null;
        foreach (var d in Level.Devices.Reverse())
        {
            var r = new Rect(d.X - IconSize / 2 - 4, d.Y - IconSize / 2 - 4, IconSize + 8, IconSize + 24);
            if (r.Contains(p)) return d;
        }
        if (Owner != null && new Rect(ProxyPosition.X - 26, ProxyPosition.Y - 26, 52, 70).Contains(p)) return Owner;
        return null;
    }

    private Link? HitLink(Point p)
    {
        if (Level == null) return null;
        var pos = Positions();
        foreach (var l in Level.Links)
        {
            if (!pos.TryGetValue(l.AId, out var a) || !pos.TryGetValue(l.BId, out var b)) continue;
            if (DistanceToSegment(p, a, b) < 6) return l;
        }
        return null;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared;
        if (len2 < 1e-6) return (p - a).Length;
        var t = Math.Clamp(Vector.Multiply(p - a, ab) / len2, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    // ---------------- Отрисовка ----------------

    private static readonly Brush SelectionBrush = Freeze(new SolidColorBrush(Color.FromArgb(60, 255, 140, 0)));
    private static readonly Pen SelectionPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(255, 140, 0)), 2));
    private static readonly Pen PendingPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(255, 140, 0)), 2) { DashStyle = DashStyles.Dash });
    private static readonly Brush StatBrush = Freeze(new SolidColorBrush(Color.FromRgb(0, 70, 170)));
    private static readonly Brush IpBrush = Freeze(new SolidColorBrush(Color.FromRgb(90, 90, 90)));
    private static readonly Brush LabelBg = Freeze(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(232, 236, 241)), 1));

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    private FormattedText Text(string s, double size, Brush brush, Typeface? face = null) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face ?? _font, size, brush, Dpi);

    private void DrawLabel(DrawingContext dc, string s, Point center, double size, Brush brush, bool background, Typeface? face = null)
    {
        var ft = Text(s, size, brush, face);
        var origin = new Point(center.X - ft.Width / 2, center.Y);
        if (background) dc.DrawRoundedRectangle(LabelBg, null, new Rect(origin.X - 3, origin.Y - 1, ft.Width + 6, ft.Height + 2), 3, 3);
        dc.DrawText(ft, origin);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = new Size(Math.Max(ActualWidth, 1), Math.Max(ActualHeight, 1));
        DrawBackground(dc, size);
        if (_vm == null || Level == null) return;

        var pos = Positions();
        var traffic = _vm.Mode == SimMode.Traffic ? _vm.TrafficSim : null;
        var lang = Loc.I.Lang;

        // План помещения и зоны покрытия Wi-Fi
        var plan = Level.Plan;
        bool hasPlan = plan.IsVisible && plan.Shapes.Count > 0;
        if (plan.IsVisible) DrawPlan(dc, plan);
        CoverageResult? coverage = null;
        if (hasPlan && _vm.ShowCoverage && Level.Devices.Any(d => d.Kind == DeviceKind.AccessPoint))
        {
            coverage = WirelessCoverage.Analyze(Level, _vm.Catalog, lang);
            DrawCoverage(dc, coverage, plan);
        }
        if (_drawStart is { } ds) DrawShapePreview(dc, ds, Snap(_mouse), plan);

        // Линии
        foreach (var link in Level.Links)
        {
            if (!pos.TryGetValue(link.AId, out var a) || !pos.TryGetValue(link.BId, out var b)) continue;
            var media = _vm.Catalog.Media(link.MediaId);
            var color = (Color)ColorConverter.ConvertFromString(media?.Color ?? "#2060C0");
            bool overloaded = traffic != null && traffic.IsOverloaded(link);
            double thickness = overloaded ? 4 : 2;
            if (_vm.Selected == link) dc.DrawLine(new Pen(SelectionBrush, 10), a, b);
            var pen = new Pen(new SolidColorBrush(overloaded ? Colors.Red : color), thickness);
            if (media?.Dashed == true) pen.DashStyle = DashStyles.Dash;
            dc.DrawLine(pen, a, b);

            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            double y = mid.Y + 2;
            if (_vm.ShowLinkLabels)
            {
                // Короткое имя спецификации: «100Base-TX» вместо «Fast Ethernet 100Base-TX».
                var stdName = _vm.Catalog.Standard(link.StandardId)?.NameEn ?? link.StandardId;
                foreach (var prefix in new[] { "10 Gigabit Ethernet ", "Gigabit Ethernet ", "Fast Ethernet ", "Ethernet " })
                    if (stdName.StartsWith(prefix)) { stdName = stdName[prefix.Length..]; break; }
                int paren = stdName.IndexOf(" (", StringComparison.Ordinal);
                if (paren > 0) stdName = stdName[..paren];
                // Подпись ближе к второму концу, чтобы подписи линий одного узла не накладывались.
                // Подпись — ближе к концу с меньшим числом линий (обычно к ПК), а не к хабу или кольцу.
                int degA = Level.Links.Count(x => x.Touches(link.AId)), degB = Level.Links.Count(x => x.Touches(link.BId));
                var at = degA <= degB ? a + (b - a) * 0.38 : a + (b - a) * 0.62;
                DrawLabel(dc, $"{stdName}, {link.LengthM:0.#} {(Loc.I.IsRu ? "м" : "m")}", new Point(at.X, at.Y - 7), 10, Brushes.DimGray, true);
                y += 14;
            }
            var cov = coverage?.Rows.FirstOrDefault(r => r.Link == link);
            if (cov != null)
            {
                var txt = Loc.I.IsRu ? $"{cov.DistanceM:0.#} м · стен {cov.Walls}" : $"{cov.DistanceM:0.#} m · walls {cov.Walls}";
                // Подпись ближе к станции, чтобы подписи разных линий не накладывались у точки доступа.
                var at = pos[cov.AccessPoint.Id] + (pos[cov.Station.Id] - pos[cov.AccessPoint.Id]) * 0.7;
                DrawLabel(dc, (cov.Ok ? "✓ " : "✗ ") + txt, new Point(at.X, at.Y - 7), 10, cov.Ok ? Brushes.DarkGreen : Brushes.Firebrick, true, _fontBold);
            }
            if (traffic != null)
                foreach (var key in link.ShownStatistics)
                {
                    DrawLabel(dc, StatKeys.Format(key, traffic.LinkValue(link, key), lang), new Point(mid.X, y), 11, StatBrush, true, _fontBold);
                    y += 15;
                }
        }

        // Потоки (Set traffic)
        if (_vm.ShowFlows) DrawFlows(dc, pos);

        // Резиновая линия инструмента
        if (_pendingSource != null && pos.TryGetValue(_pendingSource.Id, out var src))
            dc.DrawLine(PendingPen, src, _mouse);

        // Устройства
        if (Owner != null) DrawProxy(dc, Owner);
        foreach (var d in Level.Devices) DrawDevice(dc, d, traffic, lang);

        // Пакеты
        if (traffic != null) DrawTrafficPackets(dc, traffic, pos);
        if (_vm.Mode == SimMode.Protocol && _vm.ProtoSim != null) DrawFrames(dc, pos);
    }

    // ---------------- План помещения ----------------

    private static readonly Brush RoomFill = Freeze(new SolidColorBrush(Color.FromArgb(70, 255, 248, 225)));
    private static readonly Pen WallPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(70, 70, 80)), 4) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square });
    private static readonly Pen WallSelPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(255, 140, 0)), 8));
    private static readonly Brush RoomLabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(120, 110, 90)));

    /// <summary>Привязка к сетке 0,5 м (Alt — без привязки).</summary>
    private Point Snap(Point p)
    {
        var plan = Level?.Plan;
        if (plan == null || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return p;
        double step = Math.Max(4, plan.PixelsPerMeter / 2);
        return new Point(Math.Round(p.X / step) * step, Math.Round(p.Y / step) * step);
    }

    private void DrawPlan(DrawingContext dc, FloorPlan plan)
    {
        string m = Loc.I.IsRu ? "м" : "m";
        foreach (var sh in plan.Shapes.Where(x => x.Kind == PlanShapeKind.Room))
        {
            var r = new Rect(new Point(sh.X1, sh.Y1), new Point(sh.X2, sh.Y2));
            dc.DrawRectangle(RoomFill, null, r);
            var label = string.IsNullOrEmpty(sh.Label) ? "" : sh.Label + "  ";
            dc.DrawText(Text($"{label}{plan.ToMeters(r.Width):0.#}×{plan.ToMeters(r.Height):0.#} {m}", 11, RoomLabelBrush), new Point(r.Left + 6, r.Top + 4));
        }
        foreach (var sh in plan.Shapes)
            foreach (var seg in sh.Segments())
            {
                var a = new Point(seg.X1, seg.Y1);
                var b = new Point(seg.X2, seg.Y2);
                if (_vm!.Selected == sh) dc.DrawLine(WallSelPen, a, b);
                dc.DrawLine(WallPen, a, b);
            }
        if (plan.Shapes.Count > 0) DrawScaleBar(dc, plan);
    }

    private void DrawScaleBar(DrawingContext dc, FloorPlan plan)
    {
        double meters = plan.PixelsPerMeter >= 15 ? 5 : plan.PixelsPerMeter >= 4 ? 10 : 50;
        double len = plan.ToPixels(meters);
        double bottom = (Level?.Devices.Count > 0 ? Level.Devices.Max(d => d.Y) : 0);
        bottom = Math.Max(bottom, plan.Shapes.Max(x => Math.Max(x.Y1, x.Y2))) + 60;
        var origin = new Point(20, bottom);
        var pen = new Pen(Brushes.Black, 2);
        dc.DrawLine(pen, origin, origin + new Vector(len, 0));
        dc.DrawLine(pen, origin + new Vector(0, -5), origin + new Vector(0, 5));
        dc.DrawLine(pen, origin + new Vector(len, -5), origin + new Vector(len, 5));
        dc.DrawText(Text($"{meters} {(Loc.I.IsRu ? "м" : "m")}", 11, Brushes.Black), origin + new Vector(len / 2 - 8, -18));
    }

    private void DrawCoverage(DrawingContext dc, CoverageResult coverage, FloorPlan plan)
    {
        foreach (var (ap, (clear, one)) in coverage.Radius)
        {
            var c = new Point(ap.X, ap.Y);
            double r0 = plan.ToPixels(clear), r1 = plan.ToPixels(one);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(24, 46, 160, 67)), new Pen(new SolidColorBrush(Color.FromArgb(160, 46, 160, 67)), 1.5), c, r0, r0);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(160, 230, 120, 30)), 1.2) { DashStyle = DashStyles.Dash }, c, r1, r1);
        }
    }

    private void DrawShapePreview(DrawingContext dc, Point a, Point b, FloorPlan plan)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(255, 140, 0)), 3) { DashStyle = DashStyles.Dash };
        string m = Loc.I.IsRu ? "м" : "m";
        string text;
        if (_vm!.Tool == ToolMode.Room)
        {
            var r = new Rect(a, b);
            dc.DrawRectangle(null, pen, r);
            text = $"{plan.ToMeters(r.Width):0.#} × {plan.ToMeters(r.Height):0.#} {m}";
        }
        else
        {
            dc.DrawLine(pen, a, b);
            text = $"{plan.ToMeters((b - a).Length):0.#} {m}";
        }
        DrawLabel(dc, text, b + new Vector(0, 8), 11, Brushes.Black, true, _fontBold);
    }

    private PlanShape? HitShape(Point p)
    {
        if (Level == null || !Level.Plan.IsVisible) return null;
        foreach (var sh in Level.Plan.Shapes.Reverse())
            foreach (var seg in sh.Segments())
                if (DistanceToSegment(p, new Point(seg.X1, seg.Y1), new Point(seg.X2, seg.Y2)) < 5) return sh;
        return null;
    }

    private void DrawBackground(DrawingContext dc, Size size)
    {
        var bgName = Level?.Background ?? "plain";
        var (bg, accent) = bgName switch
        {
            "country" => ("#EAF3FB", "#D3E6F5"),
            "city" => ("#F4F2EE", "#E4E0D8"),
            "building" => ("#F6F4EF", "#E2DCCF"),
            "room" => ("#FBF8F1", "#ECE4D3"),
            _ => ("#FFFFFF", "#E8ECF1"),
        };
        dc.DrawRectangle(new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)), null, new Rect(size));
        var pen = bgName == "plain" ? GridPen : new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)), bgName == "city" ? 6 : 1);
        double step = bgName == "city" ? 160 : 20;
        if (bgName == "plain" || bgName == "city")
        {
            for (double x = 0; x < size.Width; x += step) dc.DrawLine(pen, new Point(x, 0), new Point(x, size.Height));
            for (double y = 0; y < size.Height; y += step) dc.DrawLine(pen, new Point(0, y), new Point(size.Width, y));
        }
        if (bgName != "plain" && Level != null)
        {
            var ft = Text(Level.Name, 28, new SolidColorBrush((Color)ColorConverter.ConvertFromString(accent)), _fontBold);
            dc.DrawText(ft, new Point(size.Width - ft.Width - 30, 16));
        }
    }

    private void DrawDevice(DrawingContext dc, Device d, TrafficSimulator? traffic, string lang)
    {
        var cd = _vm!.Catalog.Device(d.CatalogId);
        var icon = IconLibrary.Get(cd?.Icon ?? "equipment");
        bool big = d.Kind is DeviceKind.Segment or DeviceKind.Container;
        double s = big ? IconSize * 1.25 : IconSize;
        var rect = new Rect(d.X - s / 2, d.Y - s / 2, s, s);

        if (_vm.Selected == d) dc.DrawRoundedRectangle(SelectionBrush, SelectionPen, Rect.Inflate(rect, 5, 5), 6, 6);
        else if (_hover == d) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(30, 0, 120, 215)), null, Rect.Inflate(rect, 5, 5), 6, 6);
        if (_pendingSource == d) dc.DrawEllipse(null, SelectionPen, new Point(d.X, d.Y), s / 2 + 8, s / 2 + 8);
        dc.DrawImage(icon, rect);

        if (d.Software.Count > 0 && !d.IsContainer)
            dc.DrawImage(IconLibrary.Get("software"), new Rect(rect.Right - 12, rect.Top - 2, 14, 14));
        if (d.IsContainer)
            dc.DrawText(Text("⤢", 12, Brushes.DimGray), new Point(rect.Right - 10, rect.Top - 6));

        double y = rect.Bottom + 1;
        DrawLabel(dc, d.Name, new Point(d.X, y), 12, Brushes.Black, true);
        y += 17;

        if (_vm.Mode == SimMode.Protocol)
        {
            foreach (var i in d.Network.Interfaces.Where(i => !string.IsNullOrEmpty(i.IpAddress)).Take(2))
            {
                DrawLabel(dc, i.IpAddress, new Point(d.X, y), 10, IpBrush, false, _mono);
                y += 13;
            }
        }
        else if (traffic != null)
        {
            foreach (var key in d.ShownStatistics)
            {
                DrawLabel(dc, StatKeys.Format(key, traffic.DeviceValue(d, key), lang), new Point(d.X, y), 11, StatBrush, true, _fontBold);
                y += 15;
            }
        }
    }

    private void DrawProxy(DrawingContext dc, Device owner)
    {
        var cd = _vm!.Catalog.Device(owner.CatalogId);
        var rect = new Rect(ProxyPosition.X - 22, ProxyPosition.Y - 22, 44, 44);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(40, 120, 120, 120)), new Pen(Brushes.Gray, 1) { DashStyle = DashStyles.Dot }, Rect.Inflate(rect, 6, 6), 6, 6);
        if (_pendingSource == owner) dc.DrawEllipse(null, SelectionPen, ProxyPosition, 32, 32);
        dc.DrawImage(IconLibrary.Get(cd?.Icon ?? "building"), rect);
        DrawLabel(dc, "↑ " + owner.Name, new Point(ProxyPosition.X, rect.Bottom + 6), 11, Brushes.DimGray, true);
    }

    private static Color FlowColor(TrafficFlow f)
    {
        var palette = new[] { Color.FromRgb(230, 120, 30), Color.FromRgb(46, 160, 67), Color.FromRgb(160, 60, 200), Color.FromRgb(214, 51, 108), Color.FromRgb(23, 140, 160), Color.FromRgb(120, 100, 40), Color.FromRgb(60, 90, 220) };
        int h = 0;
        foreach (var c in f.Profile.Id) h = h * 31 + c;
        return palette[Math.Abs(h) % palette.Length];
    }

    private void DrawFlows(DrawingContext dc, Dictionary<string, Point> pos)
    {
        var pairIndex = new Dictionary<(string, string), int>();
        foreach (var f in _vm!.Project.Flows)
        {
            if (!pos.TryGetValue(f.SourceId, out var a) || !pos.TryGetValue(f.TargetId, out var b)) continue;
            var key = string.CompareOrdinal(f.SourceId, f.TargetId) < 0 ? (f.SourceId, f.TargetId) : (f.TargetId, f.SourceId);
            pairIndex.TryGetValue(key, out int k);
            pairIndex[key] = k + 1;

            var dir = b - a;
            if (dir.Length < 1) continue;
            var normal = new Vector(-dir.Y, dir.X);
            normal.Normalize();
            var ctrl = a + dir / 2 + normal * (28 + 16 * k);
            var color = FlowColor(f);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(f.IsActive ? 120 : 50), color.R, color.G, color.B)), 1.5) { DashStyle = DashStyles.Dash };
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(a, false, false);
                ctx.QuadraticBezierTo(ctrl, b, true, true);
            }
            dc.DrawGeometry(null, pen, geo);
            // Стрелка у приёмника
            var tangent = b - ctrl;
            tangent.Normalize();
            var tip = b - tangent * (IconSize / 2 + 2);
            var side = new Vector(-tangent.Y, tangent.X) * 4;
            var arrow = new StreamGeometry();
            using (var ctx = arrow.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(tip - tangent * 9 + side, true, false);
                ctx.LineTo(tip - tangent * 9 - side, true, false);
            }
            dc.DrawGeometry(pen.Brush, null, arrow);
        }
    }

    private void DrawTrafficPackets(DrawingContext dc, TrafficSimulator sim, Dictionary<string, Point> pos)
    {
        int drawn = 0;
        double now = sim.Now;
        foreach (var p in sim.InFlight)
        {
            if (drawn > 400) break;
            if (!p.Flow.ShowAnimation) continue;
            var pts = new List<Point>();
            if (!pos.TryGetValue(p.Hop.FromId, out var start)) continue;
            pts.Add(start);
            var at = p.Hop.FromId;
            bool ok = true;
            foreach (var l in p.Hop.Links)
            {
                at = l.Other(at);
                if (!pos.TryGetValue(at, out var next)) { ok = false; break; }
                pts.Add(next);
            }
            if (!ok || pts.Count < 2) continue;
            var point = Along(pts, p.Progress(now));
            var c = FlowColor(p.Flow);
            var brush = new SolidColorBrush(p.IsReply ? Color.FromRgb((byte)((c.R + 255) / 2), (byte)((c.G + 255) / 2), (byte)((c.B + 255) / 2)) : c);
            dc.DrawEllipse(brush, new Pen(Brushes.White, 1), point, p.IsReply ? 4 : 5, p.IsReply ? 4 : 5);
            drawn++;
        }
    }

    private void DrawFrames(DrawingContext dc, Dictionary<string, Point> pos)
    {
        var sim = _vm!.ProtoSim!;
        foreach (var f in sim.InFlight)
        {
            if (!pos.TryGetValue(f.FromDeviceId, out var a) || !pos.TryGetValue(f.ToDeviceId, out var b)) continue;
            var p = a + (b - a) * f.Progress(sim.Engine.Now);
            var color = f.Frame.Protocol == "ARP" ? Color.FromRgb(242, 195, 53) : Color.FromRgb(230, 90, 160);
            var rect = new Rect(p.X - 9, p.Y - 6, 18, 12);
            dc.DrawRectangle(new SolidColorBrush(color), new Pen(Brushes.Black, 1), rect);
            dc.DrawLine(new Pen(Brushes.Black, 1), rect.TopLeft, new Point(p.X, p.Y + 1));
            dc.DrawLine(new Pen(Brushes.Black, 1), rect.TopRight, new Point(p.X, p.Y + 1));
        }
    }

    private static Point Along(List<Point> pts, double t)
    {
        double total = 0;
        for (int i = 1; i < pts.Count; i++) total += (pts[i] - pts[i - 1]).Length;
        double target = total * t;
        for (int i = 1; i < pts.Count; i++)
        {
            var seg = (pts[i] - pts[i - 1]).Length;
            if (target <= seg || i == pts.Count - 1) return pts[i - 1] + (pts[i] - pts[i - 1]) * (seg < 1e-6 ? 1 : Math.Min(1, target / seg));
            target -= seg;
        }
        return pts[^1];
    }

    // ---------------- Мышь ----------------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (_vm == null) return;
        var p = e.GetPosition(this);
        var device = HitDevice(p);
        var link = device == null ? HitLink(p) : null;

        if (_vm.Tool is ToolMode.Wall or ToolMode.Room)
        {
            _drawStart = Snap(p);
            _mouse = p;
            CaptureMouse();
            return;
        }
        var shape = device == null && link == null ? HitShape(p) : null;

        if (e.ClickCount == 2 && _vm.Tool == ToolMode.Select && shape != null)
        {
            _vm.RenameShape(shape);
            return;
        }
        if (e.ClickCount == 2 && _vm.Tool == ToolMode.Select)
        {
            if (device == Owner && device != null) _vm.GoUpCommand.Execute(null);
            else if (device != null) { if (device.IsContainer) _vm.OpenContainer(device); else _vm.EditDevice(device); }
            else if (link != null) _vm.EditLink(link);
            return;
        }

        switch (_vm.Tool)
        {
            case ToolMode.Link:
            case ToolMode.Traffic:
            case ToolMode.Pdu:
                if (device == null) { CancelPending(); return; }
                if (_pendingSource == null) { _pendingSource = device; InvalidateVisual(); return; }
                var source = _pendingSource;
                _pendingSource = null;
                InvalidateVisual();
                if (source == device) return;
                if (_vm.Tool == ToolMode.Link) _vm.RequestLink(source, device);
                else if (_vm.Tool == ToolMode.Traffic) _vm.RequestFlow(source, device);
                else _vm.SendPdu(source, device);
                return;

            case ToolMode.Delete:
                if (device != null && device != Owner) _vm.Delete(device);
                else if (link != null) _vm.Delete(link);
                else if (shape != null) _vm.Delete(shape);
                return;
        }

        if (device != null && device != Owner)
        {
            _vm.Selected = device;
            _dragDevice = device;
            _dragOffset = new Point(device.X, device.Y) - p;
            _dragMoved = false;
            CaptureMouse();
        }
        else if (device == Owner && device != null) _vm.Selected = null;
        else if (link != null) _vm.Selected = link;
        else if (shape != null) _vm.Selected = shape;
        else
        {
            _vm.Selected = null;
            if (Scroller is { } sv)
            {
                _panning = true;
                _panStart = e.GetPosition(sv);
                _panScrollStart = new Vector(sv.HorizontalOffset, sv.VerticalOffset);
                Cursor = Cursors.SizeAll;
                CaptureMouse();
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_vm == null) return;
        _mouse = e.GetPosition(this);
        if (_dragDevice != null && e.LeftButton == MouseButtonState.Pressed)
        {
            var np = _mouse + _dragOffset;
            if (!_dragMoved && (np - new Point(_dragDevice.X, _dragDevice.Y)).Length > 2)
            {
                _vm.Checkpoint();
                _dragMoved = true;
            }
            if (_dragMoved)
            {
                _dragDevice.X = Math.Max(30, Math.Round(np.X));
                _dragDevice.Y = Math.Max(30, Math.Round(np.Y));
                InvalidateVisual();
            }
            return;
        }
        if (_drawStart != null)
        {
            InvalidateVisual();
            return;
        }
        if (_panning && Scroller is { } sv)
        {
            var d = e.GetPosition(sv) - _panStart;
            sv.ScrollToHorizontalOffset(_panScrollStart.X - d.X);
            sv.ScrollToVerticalOffset(_panScrollStart.Y - d.Y);
            return;
        }
        var hover = HitDevice(_mouse);
        if (hover != _hover)
        {
            _hover = hover;
            UpdateToolTip();
            InvalidateVisual();
        }
        if (_pendingSource != null) InvalidateVisual();
        Cursor = _vm.Tool switch
        {
            ToolMode.Link or ToolMode.Traffic or ToolMode.Pdu or ToolMode.Wall or ToolMode.Room => Cursors.Cross,
            ToolMode.Delete => Cursors.No,
            _ => hover != null ? Cursors.Hand : Cursors.Arrow,
        };
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragDevice != null)
        {
            if (_dragMoved) InvalidateMeasure();
            _dragDevice = null;
        }
        if (_drawStart is { } start && _vm != null && Level != null)
        {
            var end = Snap(e.GetPosition(this));
            _drawStart = null;
            bool room = _vm.Tool == ToolMode.Room;
            bool big = room ? Math.Abs(end.X - start.X) > 8 && Math.Abs(end.Y - start.Y) > 8 : (end - start).Length > 8;
            if (big)
                _vm.AddPlanShape(new PlanShape
                {
                    Kind = room ? PlanShapeKind.Room : PlanShapeKind.Wall,
                    X1 = room ? Math.Min(start.X, end.X) : start.X,
                    Y1 = room ? Math.Min(start.Y, end.Y) : start.Y,
                    X2 = room ? Math.Max(start.X, end.X) : end.X,
                    Y2 = room ? Math.Max(start.Y, end.Y) : end.Y,
                    Label = room ? Loc.T("Plan_Room") + " " + (Level.Plan.Shapes.Count(x => x.Kind == PlanShapeKind.Room) + 1) : "",
                });
            InvalidateVisual();
        }
        _panning = false;
        Cursor = Cursors.Arrow;
        ReleaseMouseCapture();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        if (_vm == null) return;
        var p = e.GetPosition(this);
        var device = HitDevice(p);
        var link = device == null ? HitLink(p) : null;
        CancelPending();
        ContextMenu? menu = null;
        if (device != null && device == Owner)
        {
            menu = new ContextMenu();
            menu.Items.Add(Item(Loc.T("Ctx_GoUp"), () => _vm.GoUpCommand.Execute(null)));
        }
        else if (device != null)
        {
            _vm.Selected = device;
            menu = new ContextMenu();
            if (device.IsContainer) menu.Items.Add(Item(Loc.T("Ctx_Open"), () => _vm.OpenContainer(device), bold: true));
            else menu.Items.Add(Item(Loc.T("Ctx_Configuration"), () => _vm.EditDevice(device), bold: true));
            menu.Items.Add(Item(Loc.T("Ctx_Statistics"), () => _vm.ShowStatisticsItems(device)));
            if (!device.IsContainer && !device.Kind.IsSharedMedium())
                menu.Items.Add(Item(Loc.T("Ctx_Console"), () => _vm.OpenConsole(device)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Loc.T("Ctx_Rename"), () => _vm.Rename(device)));
            menu.Items.Add(Item(Loc.T("Ctx_Delete"), () => _vm.Delete(device)));
        }
        else if (link == null && HitShape(p) is { } sh)
        {
            _vm.Selected = sh;
            menu = new ContextMenu();
            menu.Items.Add(Item(Loc.T("Ctx_Rename"), () => _vm.RenameShape(sh)));
            menu.Items.Add(Item(Loc.T("Ctx_Delete"), () => _vm.Delete(sh)));
        }
        else if (link != null)
        {
            _vm.Selected = link;
            menu = new ContextMenu();
            menu.Items.Add(Item(Loc.T("Ctx_LinkProps"), () => _vm.EditLink(link), bold: true));
            menu.Items.Add(Item(Loc.T("Ctx_Statistics"), () => _vm.ShowStatisticsItems(link)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Loc.T("Ctx_Delete"), () => _vm.Delete(link)));
        }
        if (menu != null)
        {
            menu.PlacementTarget = this;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    private static MenuItem Item(string header, Action action, bool bold = false)
    {
        var mi = new MenuItem { Header = header };
        if (bold) mi.FontWeight = FontWeights.SemiBold;
        mi.Click += (_, _) => action();
        return mi;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SetZoom(Zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
            e.Handled = true;
        }
        base.OnMouseWheel(e);
    }

    private void UpdateToolTip()
    {
        if (_hover == null || _vm == null) { _toolTip.IsOpen = false; _toolTip.Content = null; return; }
        var d = _hover;
        var cd = _vm.Catalog.Device(d.CatalogId);
        var lines = new List<string> { d.Name, cd?.Name(Loc.I.Lang) ?? d.CatalogId };
        foreach (var sw in d.Software)
        {
            var csw = _vm.Catalog.SoftwareById(sw.SoftwareId);
            lines.Add($"• {csw?.Name(Loc.I.Lang) ?? sw.SoftwareId}: {string.Join(", ", sw.EnabledServices.Select(s => _vm.Catalog.Service(s)?.NameEn ?? s))}");
        }
        foreach (var i in d.Network.Interfaces.Where(i => !string.IsNullOrEmpty(i.IpAddress)))
            lines.Add($"{i.Name}: {i.IpAddress}/{i.SubnetMask}");
        if (d.IsContainer) lines.Add(Loc.T("Tip_OpenContainer"));
        _toolTip.Content = string.Join("\n", lines);
    }

    // ---------------- Перетаскивание из каталога ----------------

    protected override void OnDragOver(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (_vm == null || e.Data.GetData(DragFormat) is not string item) return;
        var p = e.GetPosition(this);
        if (item.StartsWith("dev:"))
        {
            _vm.AddDevice(item[4..], Math.Max(40, Math.Round(p.X)), Math.Max(40, Math.Round(p.Y)));
        }
        else if (item.StartsWith("sw:"))
        {
            var target = HitDevice(p);
            if (target == null || target == Owner) _vm.Log(Loc.T("Log_DropSoftwareOnDevice"), true);
            else _vm.InstallSoftware(target, item[3..]);
        }
        e.Handled = true;
    }

    /// <summary>Сохранить схему текущего уровня в PNG.</summary>
    public void ExportPng(string path) => System.IO.File.WriteAllBytes(path, RenderPng().Png);

    /// <summary>Отрисовать текущий уровень в PNG (для экспорта и отчёта).</summary>
    public (byte[] Png, int Width, int Height) RenderPng()
    {
        UpdateLayout();
        var w = (int)Math.Max(1, ActualWidth);
        var h = (int)Math.Max(1, ActualHeight);
        if (Level != null)
        {
            var xs = Level.Devices.Select(d => d.X).Concat(Level.Plan.Shapes.SelectMany(sh => new[] { sh.X1, sh.X2 })).DefaultIfEmpty(400);
            var ys = Level.Devices.Select(d => d.Y).Concat(Level.Plan.Shapes.SelectMany(sh => new[] { sh.Y1, sh.Y2 })).DefaultIfEmpty(300);
            w = (int)Math.Min(Math.Max(w, 400), xs.Max() + 160);
            h = (int)Math.Min(Math.Max(h, 300), ys.Max() + 120);
        }
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) OnRender(dc);
        rtb.Render(visual);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var ms = new System.IO.MemoryStream();
        enc.Save(ms);
        return (ms.ToArray(), w, h);
    }
}
