using System.Collections.ObjectModel;

namespace NetSim.Core.Model;

public enum PlanShapeKind
{
    /// <summary>Стена — отрезок.</summary>
    Wall,

    /// <summary>Помещение — прямоугольник; его стороны считаются стенами.</summary>
    Room,
}

/// <summary>Элемент плана помещения (координаты — в пикселях схемы).</summary>
public sealed class PlanShape : ObservableBase
{
    private double _x1, _y1, _x2, _y2;
    private string _label = "";

    public string Id { get; set; } = Ids.New();
    public PlanShapeKind Kind { get; set; }
    public double X1 { get => _x1; set => Set(ref _x1, value); }
    public double Y1 { get => _y1; set => Set(ref _y1, value); }
    public double X2 { get => _x2; set => Set(ref _x2, value); }
    public double Y2 { get => _y2; set => Set(ref _y2, value); }
    public string Label { get => _label; set => Set(ref _label, value); }

    /// <summary>Отрезки стен этого элемента.</summary>
    public IEnumerable<(double X1, double Y1, double X2, double Y2)> Segments()
    {
        if (Kind == PlanShapeKind.Wall)
        {
            yield return (X1, Y1, X2, Y2);
            yield break;
        }
        double l = Math.Min(X1, X2), r = Math.Max(X1, X2), t = Math.Min(Y1, Y2), b = Math.Max(Y1, Y2);
        yield return (l, t, r, t);
        yield return (r, t, r, b);
        yield return (r, b, l, b);
        yield return (l, b, l, t);
    }
}

/// <summary>
/// План помещения на уровне схемы (ПР6, ПР10): масштаб, стены и кабинеты.
/// По нему вычисляются расстояния между устройствами в метрах и число стен на пути радиосигнала.
/// </summary>
public sealed class FloorPlan : ObservableBase
{
    private double _pixelsPerMeter = 20;
    private bool _isVisible = true;
    private double _wallLossDb = 6;

    /// <summary>Масштаб: сколько пикселей схемы в одном метре.</summary>
    public double PixelsPerMeter { get => _pixelsPerMeter; set => Set(ref _pixelsPerMeter, value <= 0 ? 1 : value); }

    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

    /// <summary>Затухание сигнала в одной стене Zоф, дБ (ПР10 — 6 дБ).</summary>
    public double WallLossDb { get => _wallLossDb; set => Set(ref _wallLossDb, value); }

    public ObservableCollection<PlanShape> Shapes { get; set; } = new();

    public double ToMeters(double px) => px / PixelsPerMeter;
    public double ToPixels(double meters) => meters * PixelsPerMeter;

    /// <summary>Расстояние между точками в метрах.</summary>
    public double DistanceM(double x1, double y1, double x2, double y2) =>
        ToMeters(Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1)));

    /// <summary>
    /// Сколько стен пересекает прямая линия между двумя точками. Общая сторона двух соседних кабинетов
    /// считается одной стеной (совпадающие отрезки не дублируются).
    /// </summary>
    public int WallsBetween(double x1, double y1, double x2, double y2)
    {
        var counted = new HashSet<(long, long, long, long)>();
        int n = 0;
        foreach (var shape in Shapes)
            foreach (var s in shape.Segments())
            {
                var key = Normalize(s);
                if (counted.Contains(key)) continue;
                if (Intersects(x1, y1, x2, y2, s.X1, s.Y1, s.X2, s.Y2))
                {
                    counted.Add(key);
                    n++;
                }
            }
        return n;
    }

    private static (long, long, long, long) Normalize((double X1, double Y1, double X2, double Y2) s)
    {
        long a = (long)Math.Round(s.X1), b = (long)Math.Round(s.Y1), c = (long)Math.Round(s.X2), d = (long)Math.Round(s.Y2);
        return (a, b) .CompareTo((c, d)) <= 0 ? (a, b, c, d) : (c, d, a, b);
    }

    /// <summary>Пересечение отрезков (касание концом не считается — устройство у стены её не «проходит»).</summary>
    public static bool Intersects(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        double d1 = Cross(cx, cy, dx, dy, ax, ay);
        double d2 = Cross(cx, cy, dx, dy, bx, by);
        double d3 = Cross(ax, ay, bx, by, cx, cy);
        double d4 = Cross(ax, ay, bx, by, dx, dy);
        const double eps = 1e-9;
        return ((d1 > eps && d2 < -eps) || (d1 < -eps && d2 > eps)) && ((d3 > eps && d4 < -eps) || (d3 < -eps && d4 > eps));
    }

    private static double Cross(double ax, double ay, double bx, double by, double px, double py) =>
        (bx - ax) * (py - ay) - (by - ay) * (px - ax);

    /// <summary>
    /// Построить план «два ряда кабинетов с коридором» (как на планах вариантов ПР10).
    /// Ширины кабинетов верхнего и нижнего рядов задаются в метрах.
    /// </summary>
    public void BuildCorridorPlan(IReadOnlyList<double> topWidths, IReadOnlyList<double> bottomWidths, double depthM, double corridorM,
        double originX = 60, double originY = 60, string roomPrefix = "")
    {
        double y = originY;
        void Row(IReadOnlyList<double> widths, double top, int startNumber)
        {
            double x = originX;
            int n = startNumber;
            foreach (var w in widths)
            {
                Shapes.Add(new PlanShape
                {
                    Kind = PlanShapeKind.Room, X1 = x, Y1 = top, X2 = x + ToPixels(w), Y2 = top + ToPixels(depthM),
                    Label = $"{roomPrefix}{n++}",
                });
                x += ToPixels(w);
            }
        }
        Row(topWidths, y, 1);
        if (bottomWidths.Count > 0) Row(bottomWidths, y + ToPixels(depthM + corridorM), topWidths.Count + 1);
    }
}

/// <summary>Радиопараметры точки доступа или беспроводной станции (ПР10).</summary>
public sealed class RadioSettings : ObservableBase
{
    private double _powerDbm = 20;
    private double _antennaGainDbi = 5;
    private double _cableLossDb = 1;
    private int _channel = 6;
    private double _rateMbps = 54;

    /// <summary>Мощность передатчика Pt, дБм.</summary>
    public double PowerDbm { get => _powerDbm; set => Set(ref _powerDbm, value); }

    /// <summary>Усиление антенны, дБи.</summary>
    public double AntennaGainDbi { get => _antennaGainDbi; set => Set(ref _antennaGainDbi, value); }

    /// <summary>Потери в кабеле и разъёмах, дБ.</summary>
    public double CableLossDb { get => _cableLossDb; set => Set(ref _cableLossDb, value); }

    /// <summary>Канал Wi-Fi (1–13 — 2,4 ГГц, 36–165 — 5 ГГц).</summary>
    public int Channel { get => _channel; set => Set(ref _channel, value); }

    /// <summary>Требуемая скорость, Мбит/с — определяет чувствительность приёмника.</summary>
    public double RateMbps { get => _rateMbps; set => Set(ref _rateMbps, value); }
}
