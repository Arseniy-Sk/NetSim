using System.Globalization;
using System.Text;

namespace NetSim.Calculators;

public enum CalcLineKind { Heading, Text, Formula, Result, Ok, Warning, Error }

public sealed record CalcLine(CalcLineKind Kind, string Text);

/// <summary>Отчёт расчёта: пошаговые формулы с подставленными значениями — удобно вставлять в отчёт по практике.</summary>
public sealed class CalcReport
{
    public List<CalcLine> Lines { get; } = new();
    public bool Ok { get; set; } = true;
    public string Lang { get; }

    public CalcReport(string lang) => Lang = lang;

    public bool Ru => Lang != "en";

    public string T(string ru, string en) => Ru ? ru : en;

    public CalcReport Heading(string ru, string en) { Lines.Add(new(CalcLineKind.Heading, T(ru, en))); return this; }
    public CalcReport Text(string ru, string en) { Lines.Add(new(CalcLineKind.Text, T(ru, en))); return this; }
    public CalcReport Formula(string text) { Lines.Add(new(CalcLineKind.Formula, text)); return this; }
    public CalcReport Result(string text) { Lines.Add(new(CalcLineKind.Result, text)); return this; }
    public CalcReport Good(string ru, string en) { Lines.Add(new(CalcLineKind.Ok, T(ru, en))); return this; }
    public CalcReport Warn(string ru, string en) { Lines.Add(new(CalcLineKind.Warning, T(ru, en))); return this; }
    public CalcReport Fail(string ru, string en) { Ok = false; Lines.Add(new(CalcLineKind.Error, T(ru, en))); return this; }

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var l in Lines)
        {
            var prefix = l.Kind switch
            {
                CalcLineKind.Heading => "\n",
                CalcLineKind.Formula => "    ",
                CalcLineKind.Result => "  ⇒ ",
                CalcLineKind.Ok => "  ✔ ",
                CalcLineKind.Warning => "  ⚠ ",
                CalcLineKind.Error => "  ✘ ",
                _ => "",
            };
            sb.Append(prefix).AppendLine(l.Text);
        }
        return sb.ToString().TrimStart('\n');
    }

    /// <summary>Число в формате отчёта (запятая для русского языка).</summary>
    public string N(double v, string format = "0.###") =>
        v.ToString(format, Ru ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.InvariantCulture);
}
