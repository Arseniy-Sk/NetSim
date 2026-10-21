using System.Windows;
using System.Windows.Media;

namespace NetSim.App.Rendering;

/// <summary>
/// Векторные значки устройств (поле 32×32). Нарисованы примитивами, чтобы не зависеть от файлов картинок.
/// Новый значок: добавьте ключ в словарь и укажите его в каталоге ("icon").
/// </summary>
public static class IconLibrary
{
    private static readonly Dictionary<string, DrawingImage> Cache = new();

    private static Brush B(string hex) { var b = (Brush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }

    private static readonly Brush Dark = B("#2B3A4A");
    private static readonly Brush Screen = B("#5BA4E6");
    private static readonly Brush Case = B("#D9DEE5");
    private static readonly Brush CaseDark = B("#9AA5B1");
    private static readonly Brush Blue = B("#2F6FC0");
    private static readonly Brush Green = B("#3AA25A");
    private static readonly Brush Orange = B("#E3872D");
    private static readonly Brush Red = B("#C8463D");
    private static readonly Brush Purple = B("#7E57C2");
    private static readonly Brush Yellow = B("#F2C335");
    private static readonly Brush White = Brushes.White;
    private static readonly Brush Teal = B("#2A9D8F");
    private static readonly Brush Sky = B("#BFE0F7");

    private record Part(string Geometry, Brush? Fill, Brush? Stroke = null, double Thickness = 1);

    private static Part P(string g, Brush? fill, Brush? stroke = null, double t = 1) => new(g, fill, stroke, t);

    private static readonly Dictionary<string, Part[]> Parts = new()
    {
        ["pc"] = new[]
        {
            P("M3,3 H29 V21 H3 Z", Case, Dark), P("M5,5 H27 V19 H5 Z", Screen),
            P("M13,21 H19 V25 H13 Z", CaseDark), P("M7,25 H25 V28 H7 Z", Case, Dark),
        },
        ["notebook"] = new[]
        {
            P("M6,4 H26 V19 H6 Z", Case, Dark), P("M8,6 H24 V17 H8 Z", Screen),
            P("M2,20 H30 L27,26 H5 Z", Case, Dark), P("M12,22 H20 V23 H12 Z", CaseDark),
        },
        ["workstation"] = new[]
        {
            P("M2,4 H21 V18 H2 Z", Case, Dark), P("M4,6 H19 V16 H4 Z", Screen),
            P("M8,18 H15 V21 H8 Z", CaseDark), P("M23,6 H30 V28 H23 Z", Case, Dark),
            P("M24.5,9 H28.5 V10 H24.5 Z", Dark), P("M24.5,12 H28.5 V13 H24.5 Z", Dark), P("M3,24 H21 V27 H3 Z", Case, Dark),
        },
        ["terminal"] = new[]
        {
            P("M4,3 H28 V22 H4 Z", Case, Dark), P("M6,5 H26 V20 H6 Z", B("#1E3B2A")),
            P("M8,8 H18 V9 H8 Z", Green), P("M8,11 H15 V12 H8 Z", Green), P("M6,24 H26 V28 H6 Z", Case, Dark),
        },
        ["server"] = new[]
        {
            P("M8,2 H24 V30 H8 Z", Case, Dark), P("M10,5 H22 V8 H10 Z", CaseDark), P("M10,10 H22 V13 H10 Z", CaseDark),
            P("M10,15 H22 V18 H10 Z", CaseDark), P("M19,24 A1.5,1.5 0 1 1 19,24.01 Z", Green),
        },
        ["mainframe"] = new[]
        {
            P("M3,4 H14 V29 H3 Z", B("#5D6B7A"), Dark), P("M18,4 H29 V29 H18 Z", B("#5D6B7A"), Dark),
            P("M5,7 H12 V9 H5 Z", Yellow), P("M20,7 H27 V9 H20 Z", Yellow), P("M5,12 H12 V25 H5 Z", B("#3D4956")), P("M20,12 H27 V25 H20 Z", B("#3D4956")),
        },
        ["printer"] = new[]
        {
            P("M9,3 H23 V11 H9 Z", White, Dark), P("M3,11 H29 V23 H3 Z", Case, Dark), P("M8,19 H24 V29 H8 Z", White, Dark),
            P("M11,22 H21 V23 H11 Z", CaseDark), P("M11,25 H19 V26 H11 Z", CaseDark), P("M25,14 A1.2,1.2 0 1 1 25,14.01 Z", Green),
        },
        ["scanner"] = new[] { P("M2,14 H30 V24 H2 Z", Case, Dark), P("M4,10 L28,10 L30,14 L2,14 Z", B("#7FA7C9"), Dark), P("M5,19 H20 V20 H5 Z", Blue) },
        ["storage"] = new[]
        {
            P("M5,3 H27 V29 H5 Z", B("#6C7A89"), Dark), P("M7,6 H25 V10 H7 Z", Case), P("M7,12 H25 V16 H7 Z", Case),
            P("M7,18 H25 V22 H7 Z", Case), P("M22,25 A1.2,1.2 0 1 1 22,25.01 Z", Green),
        },
        ["phone"] = new[] { P("M6,6 H26 V27 H6 Z", Case, Dark), P("M9,9 H23 V14 H9 Z", Screen), P("M3,4 Q2,12 6,14 V6 Z", Dark), P("M10,17 H22 V24 H10 Z", CaseDark) },
        ["video"] = new[] { P("M3,9 H21 V24 H3 Z", B("#4A5866"), Dark), P("M21,13 L29,8 V25 L21,20 Z", B("#4A5866"), Dark), P("M8,13 A4,4 0 1 1 8,13.01 Z", Screen) },
        ["equipment"] = new[]
        {
            P("M3,6 H29 V27 H3 Z", B("#EAF2F8"), Dark), P("M14,10 H18 V14 H22 V18 H18 V22 H14 V18 H10 V14 H14 Z", Red),
        },
        ["hub"] = new[]
        {
            P("M2,11 L8,6 H30 V17 L24,22 H2 Z", B("#8BC34A"), Dark), P("M2,11 H24 V22", null, Dark),
            P("M24,11 L30,6", null, Dark), P("M5,15 H7 V18 H5 Z", Dark), P("M10,15 H12 V18 H10 Z", Dark), P("M15,15 H17 V18 H15 Z", Dark), P("M20,15 H22 V18 H20 Z", Dark),
        },
        ["switch"] = new[]
        {
            P("M2,11 L8,6 H30 V17 L24,22 H2 Z", B("#4A90D9"), Dark), P("M2,11 H24 V22", null, Dark), P("M24,11 L30,6", null, Dark),
            P("M5,14 H14 L12,12.5 M14,14 L12,15.5", null, White, 1.4), P("M21,19 H12 L14,17.5 M12,19 L14,20.5", null, White, 1.4),
        },
        ["router"] = new[]
        {
            P("M3,12 A13,5 0 0 1 29,12 V20 A13,5 0 0 1 3,20 Z", B("#3F7FCF"), Dark), P("M3,12 A13,5 0 0 0 29,12", null, Dark),
            P("M10,10 L16,12 L22,10 M16,12 V8", null, White, 1.4), P("M10,14 L16,12 L22,14", null, White, 1.4),
        },
        ["bridge"] = new[] { P("M2,14 H30 V22 H2 Z", Teal, Dark), P("M6,14 Q16,2 26,14", null, Dark, 2), P("M10,14 V9 M16,14 V7 M22,14 V9", null, Dark, 1.2) },
        ["converter"] = new[] { P("M4,10 H28 V22 H4 Z", B("#90A4AE"), Dark), P("M7,16 H13 L11,14 M13,16 L11,18", null, Orange, 1.5), P("M25,16 H19 L21,14 M19,16 L21,18", null, Blue, 1.5) },
        ["gateway"] = new[] { P("M4,6 H28 V26 H4 Z", B("#8E7CC3"), Dark), P("M9,16 H23 M19,12 L23,16 L19,20", null, White, 2) },
        ["firewall"] = new[]
        {
            P("M3,6 H29 V27 H3 Z", B("#C0583A"), Dark), P("M3,13 H29 M3,20 H29 M10,6 V13 M20,6 V13 M15,13 V20 M25,13 V20 M10,20 V27 M20,20 V27", null, B("#F3D3C6"), 1.3),
        },
        ["encryptor"] = new[] { P("M8,14 V10 A8,8 0 0 1 24,10 V14", null, Dark, 2.5), P("M5,14 H27 V29 H5 Z", Yellow, Dark), P("M16,19 A2,2 0 1 1 16,19.01 Z M15,21 H17 V25 H15 Z", Dark) },
        ["modem"] = new[] { P("M2,14 L6,10 H30 V18 L26,22 H2 Z", B("#B0BEC5"), Dark), P("M5,17 H7 M10,17 H12 M15,17 H17", null, Green, 2), P("M24,10 V4", null, Dark, 1.5) },
        ["dsu"] = new[] { P("M2,12 H30 V22 H2 Z", B("#78909C"), Dark), P("M5,15 H12 V19 H5 Z", B("#263238")), P("M16,17 H18 M21,17 H23 M26,17 H28", null, Yellow, 2) },
        ["frad"] = new[] { P("M2,12 H30 V22 H2 Z", B("#5C6BC0"), Dark), P("M6,17 L10,14 L14,20 L18,14 L22,20 L26,17", null, White, 1.5) },
        ["mux"] = new[] { P("M4,4 L28,10 V22 L4,28 Z", B("#26A69A"), Dark), P("M7,10 H14 M7,16 H14 M7,22 H14 M20,16 H27", null, White, 1.6) },
        ["pbx"] = new[] { P("M3,8 H29 V26 H3 Z", B("#8D6E63"), Dark), P("M7,12 H11 V15 H7 Z M14,12 H18 V15 H14 Z M21,12 H25 V15 H21 Z M7,18 H11 V21 H7 Z M14,18 H18 V21 H14 Z M21,18 H25 V21 H21 Z", Case) },
        ["accessserver"] = new[] { P("M5,3 H27 V29 H5 Z", B("#546E7A"), Dark), P("M8,7 H24 V10 H8 Z M8,13 H24 V16 H8 Z", Case), P("M10,22 L16,19 L22,22 L16,25 Z", Yellow) },
        ["cloud"] = new[]
        {
            P("M8,25 A6,6 0 0 1 7,13 A8,8 0 0 1 22,10 A6,6 0 0 1 27,25 Z", B("#E3EEF9"), B("#5C7EA8"), 1.5),
        },
        ["satellite"] = new[]
        {
            P("M5,24 A12,12 0 0 1 22,7 Z", B("#CFD8DC"), Dark), P("M13,16 L22,7", null, Dark, 1.5), P("M22,7 A2,2 0 1 1 22,7.01 Z", Red),
            P("M10,24 H18 L16,29 H12 Z", CaseDark, Dark), P("M24,3 A6,6 0 0 1 29,8 M24,0 A9,9 0 0 1 32,8", null, Blue, 1.2),
        },
        ["ap"] = new[]
        {
            P("M5,20 H27 V27 H5 Z", Case, Dark), P("M16,20 V9", null, Dark, 1.6), P("M16,8 A1.5,1.5 0 1 1 16,8.01 Z", Dark),
            P("M11,6 A6,6 0 0 0 11,12 M21,6 A6,6 0 0 1 21,12 M8,3 A10,10 0 0 0 8,15 M24,3 A10,10 0 0 1 24,15", null, Blue, 1.4),
            P("M8,23 H10 M13,23 H15", null, Green, 2),
        },
        ["tower"] = new[] { P("M16,4 L10,29 M16,4 L22,29 M12,20 H20 M13.5,13 H18.5", null, Dark, 1.6), P("M10,4 A8,8 0 0 0 10,12 M22,4 A8,8 0 0 1 22,12", null, Blue, 1.4) },
        ["bus"] = new[]
        {
            P("M1,14 H31 V18 H1 Z", B("#2E7D32"), Dark), P("M1,12 V20 M31,12 V20", null, Dark, 2.5), P("M8,18 V24 M16,18 V24 M24,18 V24", null, Dark, 1.4),
        },
        ["ring"] = new[] { P("M16,4 A13,11 0 1 1 15.99,4 Z M16,9 A8,6 0 1 0 16.01,9 Z", B("#7E57C2"), Dark) },
        ["country"] = new[]
        {
            P("M16,2 A14,14 0 1 1 15.99,2 Z", B("#BBDEFB"), B("#1565C0"), 1.2),
            P("M7,9 Q11,7 13,10 T19,12 Q22,9 25,12 L24,17 Q20,19 18,23 Q14,22 12,18 Q8,17 7,13 Z", B("#81C784"), B("#2E7D32"), 0.8),
        },
        ["city"] = new[]
        {
            P("M2,29 H30", null, Dark, 1.5), P("M3,29 V15 H10 V29 Z", B("#90A4AE"), Dark), P("M11,29 V6 H19 V29 Z", B("#78909C"), Dark),
            P("M20,29 V11 H29 V29 Z", B("#B0BEC5"), Dark), P("M13,9 H15 V11 H13 Z M16,9 H18 V11 H16 Z M13,14 H15 V16 H13 Z M16,14 H18 V16 H16 Z M13,19 H15 V21 H13 Z M16,19 H18 V21 H16 Z", Yellow),
        },
        ["campus"] = new[] { P("M2,28 H30", null, Dark, 1.5), P("M3,28 V16 H13 V28 Z M18,28 V12 H29 V28 Z", B("#A1887F"), Dark), P("M8,16 L8,10 L13,16 Z M18,12 L23.5,5 L29,12 Z", B("#6D4C41"), Dark) },
        ["building"] = new[]
        {
            P("M6,29 V4 H26 V29 Z", B("#B0BEC5"), Dark), P("M9,7 H13 V10 H9 Z M19,7 H23 V10 H19 Z M9,13 H13 V16 H9 Z M19,13 H23 V16 H19 Z M9,19 H13 V22 H9 Z M19,19 H23 V22 H19 Z", Sky, Dark, 0.5),
            P("M14,24 H18 V29 H14 Z", B("#6D4C41")),
        },
        ["floor"] = new[] { P("M3,8 H29 V26 H3 Z", B("#ECEFF1"), Dark), P("M3,17 H12 M16,8 V17 M16,21 V26 M20,17 H29", null, Dark, 1.2) },
        ["room"] = new[] { P("M4,6 H28 V27 H4 Z", B("#FFF8E1"), Dark, 1.6), P("M4,20 H9 M14,6 V12", null, Dark, 1.2), P("M18,16 H25 V22 H18 Z", B("#BCAAA4")) },
        ["workgroup"] = new[]
        {
            P("M12,8 A4,4 0 1 1 12,8.01 Z M22,8 A4,4 0 1 1 22,8.01 Z", B("#90CAF9"), Dark),
            P("M4,26 Q4,14 12,14 Q20,14 20,26 Z M15,26 Q16,15 22,15 Q29,15 29,26 Z", B("#64B5F6"), Dark),
        },
        ["software"] = new[] { P("M5,4 H23 L28,9 V28 H5 Z", B("#ECEFF1"), Dark), P("M9,13 H24 M9,17 H24 M9,21 H18", null, Blue, 1.6) },
        ["folder"] = new[] { P("M2,8 H13 L16,11 H30 V27 H2 Z", B("#F6C85F"), B("#B68A1A")) },
        ["pdu"] = new[] { P("M3,8 H29 V25 H3 Z", B("#FFF3B0"), Dark), P("M3,8 L16,18 L29,8", null, Dark) },
    };

    public static DrawingImage Get(string key)
    {
        if (Cache.TryGetValue(key, out var img)) return img;
        if (!Parts.TryGetValue(key, out var parts)) parts = Parts["equipment"];
        var group = new DrawingGroup();
        // Прозрачная рамка 32×32, чтобы все значки масштабировались одинаково.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 32, 32))));
        foreach (var p in parts)
        {
            var geometry = Geometry.Parse(p.Geometry);
            var pen = p.Stroke == null ? null : new Pen(p.Stroke, p.Thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            group.Children.Add(new GeometryDrawing(p.Fill, pen, geometry));
        }
        group.Freeze();
        img = new DrawingImage(group);
        img.Freeze();
        Cache[key] = img;
        return img;
    }

    public static IEnumerable<string> Keys => Parts.Keys;
}
