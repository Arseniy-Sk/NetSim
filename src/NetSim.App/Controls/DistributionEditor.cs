using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NetSim.App.Localization;
using NetSim.Calculators;
using NetSim.Core.Model;

namespace NetSim.App.Controls;

/// <summary>
/// Редактор закона распределения (ПР4, ПР5): выбор закона, параметры, подсказка Rmin/Rmax
/// и гистограмма 5000 случайных значений для наглядности.
/// </summary>
public sealed class DistributionEditor : UserControl
{
    private readonly Distribution _target;
    private readonly Distribution _work;
    private readonly ComboBox _law = new() { MinWidth = 200 };
    private readonly TextBox _p1 = new() { Width = 90 };
    private readonly TextBox _p2 = new() { Width = 90 };
    private readonly TextBlock _l1 = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), MinWidth = 120 };
    private readonly TextBlock _l2 = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), MinWidth = 120 };
    private readonly StackPanel _row2;
    private readonly TextBlock _hint = new() { Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Canvas _histogram = new() { Height = 70, Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)), ClipToBounds = true };
    private readonly Button _fromMeanStd = new() { Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0) };
    private bool _loading;

    public string Unit { get; }

    public DistributionEditor(Distribution target, string title, string unit)
    {
        _target = target;
        _work = target.Clone();
        Unit = unit;

        foreach (DistributionLaw law in Enum.GetValues<DistributionLaw>())
            _law.Items.Add(new ComboBoxItem { Content = Loc.T("Law_" + law), Tag = law });

        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var row0 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row0.Children.Add(new TextBlock { Text = Loc.T("Dist_Law"), VerticalAlignment = VerticalAlignment.Center, MinWidth = 120, Margin = new Thickness(0, 0, 6, 0) });
        row0.Children.Add(_law);
        root.Children.Add(row0);
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row1.Children.Add(_l1);
        row1.Children.Add(_p1);
        root.Children.Add(row1);
        _row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        _row2.Children.Add(_l2);
        _row2.Children.Add(_p2);
        _row2.Children.Add(_fromMeanStd);
        root.Children.Add(_row2);
        root.Children.Add(_hint);
        root.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 6, 0, 0), Child = _histogram });
        Content = root;

        _fromMeanStd.Content = Loc.T("Dist_FromMeanStd");
        _fromMeanStd.ToolTip = Loc.T("Dist_FromMeanStdTip");
        _fromMeanStd.Click += (_, _) => ConvertFromMeanStd();

        _law.SelectionChanged += (_, _) =>
        {
            if (_loading || _law.SelectedItem is not ComboBoxItem { Tag: DistributionLaw law }) return;
            var oldMean = _work.ExpectedValue;
            var oldStd = _work.ExpectedStdDev;
            _work.Law = law;
            // Переносим параметры между законами, чтобы не вводить заново.
            switch (law)
            {
                case DistributionLaw.Constant: _work.Value = oldMean; break;
                case DistributionLaw.Uniform:
                    var (min, max) = Distribution.UniformBounds(oldMean, oldStd);
                    _work.Min = Math.Max(0, min); _work.Max = max; break;
                default: _work.Mean = oldMean; _work.StdDev = oldStd > 0 ? oldStd : oldMean / 4; break;
            }
            Load();
        };
        _p1.TextChanged += (_, _) => Read();
        _p2.TextChanged += (_, _) => Read();
        SizeChanged += (_, _) => DrawHistogram();
        Load();
    }

    private static string Fmt(double v) => v.ToString("0.####", CultureInfo.CurrentCulture);

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private void Load()
    {
        _loading = true;
        foreach (ComboBoxItem item in _law.Items)
            if ((DistributionLaw)item.Tag == _work.Law) _law.SelectedItem = item;
        switch (_work.Law)
        {
            case DistributionLaw.Constant:
                _l1.Text = $"{Loc.T("Dist_Value")}, {Unit}"; _p1.Text = Fmt(_work.Value); _row2.Visibility = Visibility.Collapsed; break;
            case DistributionLaw.Uniform:
                _l1.Text = $"Rmin, {Unit}"; _p1.Text = Fmt(_work.Min);
                _l2.Text = $"Rmax, {Unit}"; _p2.Text = Fmt(_work.Max); _row2.Visibility = Visibility.Visible;
                _fromMeanStd.Visibility = Visibility.Visible; break;
            case DistributionLaw.Exponential:
                _l1.Text = $"{Loc.T("Dist_Mean")}, {Unit}"; _p1.Text = Fmt(_work.Mean); _row2.Visibility = Visibility.Collapsed; break;
            default:
                _l1.Text = $"{Loc.T("Dist_Mean")}, {Unit}"; _p1.Text = Fmt(_work.Mean);
                _l2.Text = $"{Loc.T("Dist_Std")}, {Unit}"; _p2.Text = Fmt(_work.StdDev); _row2.Visibility = Visibility.Visible;
                _fromMeanStd.Visibility = Visibility.Collapsed; break;
        }
        _loading = false;
        UpdateHint();
    }

    private void Read()
    {
        if (_loading) return;
        bool ok1 = TryNum(_p1.Text, out var a);
        bool ok2 = TryNum(_p2.Text, out var b);
        _p1.Background = ok1 && a >= 0 ? Brushes.White : Brushes.MistyRose;
        _p2.Background = !_row2.IsVisible || (ok2 && b >= 0) ? Brushes.White : Brushes.MistyRose;
        switch (_work.Law)
        {
            case DistributionLaw.Constant: if (ok1) _work.Value = a; break;
            case DistributionLaw.Uniform: if (ok1) _work.Min = a; if (ok2) _work.Max = b; break;
            case DistributionLaw.Exponential: if (ok1) _work.Mean = a; break;
            default: if (ok1) _work.Mean = a; if (ok2) _work.StdDev = b; break;
        }
        UpdateHint();
    }

    /// <summary>Перевести Rmin/Rmax в равномерный закон по введённым M и σ (ПР5: Rmin = M − σ√3, Rmax = M + σ√3).</summary>
    private void ConvertFromMeanStd()
    {
        var mean = InputWindowNumber(Loc.T("Dist_Mean"), _work.ExpectedValue);
        if (mean == null) return;
        var sd = InputWindowNumber(Loc.T("Dist_Std"), _work.ExpectedStdDev);
        if (sd == null) return;
        var (min, max) = Distribution.UniformBounds(mean.Value, sd.Value);
        _work.Min = Math.Max(0, min);
        _work.Max = max;
        Load();
    }

    private static double? InputWindowNumber(string label, double initial)
    {
        var s = Views.InputWindow.Ask(Loc.T("Dist_FromMeanStd"), label, initial.ToString("0.####", CultureInfo.CurrentCulture));
        return s != null && TryNum(s, out var v) ? v : null;
    }

    private void UpdateHint()
    {
        var report = DistributionCalculator.Explain(_work, Loc.I.Lang);
        _hint.Text = string.Join("\n", report.Lines.Where(l => l.Kind != NetSim.Calculators.CalcLineKind.Heading).Select(l => l.Text));
        DrawHistogram();
    }

    private void DrawHistogram()
    {
        _histogram.Children.Clear();
        double w = Math.Max(_histogram.ActualWidth, 300), h = _histogram.Height;
        if (_work.Law == DistributionLaw.Constant) return;
        var (min, max, counts, mean, std) = DistributionCalculator.Histogram(_work, 4000, 40);
        int top = Math.Max(1, counts.Max());
        double bw = w / counts.Length;
        for (int i = 0; i < counts.Length; i++)
        {
            double bh = (h - 16) * counts[i] / top;
            var r = new Rectangle { Width = Math.Max(1, bw - 1), Height = bh, Fill = new SolidColorBrush(Color.FromRgb(91, 164, 230)) };
            Canvas.SetLeft(r, i * bw);
            Canvas.SetTop(r, h - 14 - bh);
            _histogram.Children.Add(r);
        }
        var label = new TextBlock
        {
            Text = $"{Fmt(min)} … {Fmt(max)}   x̄ = {Fmt(mean)}, s = {Fmt(std)}",
            FontSize = 10, Foreground = Brushes.DimGray,
        };
        Canvas.SetLeft(label, 4);
        Canvas.SetTop(label, h - 14);
        _histogram.Children.Add(label);
    }

    /// <summary>Записать изменения в исходный объект.</summary>
    public void Apply() => _target.CopyFrom(_work);
}
