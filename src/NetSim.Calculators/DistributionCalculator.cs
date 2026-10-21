using NetSim.Core.Model;

namespace NetSim.Calculators;

/// <summary>Пересчёт параметров законов распределения (ПР4, ПР5).</summary>
public static class DistributionCalculator
{
    public static CalcReport Explain(Distribution d, string lang = "ru")
    {
        var r = new CalcReport(lang);
        switch (d.Law)
        {
            case DistributionLaw.Normal:
                r.Heading("Нормальный закон", "Normal distribution");
                r.Formula($"Rmin = Mx − σ = {r.N(d.Mean)} − {r.N(d.StdDev)} = {r.N(d.Mean - d.StdDev)}");
                r.Formula($"Rmax = Mx + σ = {r.N(d.Mean)} + {r.N(d.StdDev)} = {r.N(d.Mean + d.StdDev)}");
                r.Text("≈68% значений попадает в [Mx − σ; Mx + σ], ≈95% — в [Mx − 2σ; Mx + 2σ].",
                       "≈68% of values fall within [M − σ; M + σ], ≈95% within [M − 2σ; M + 2σ].");
                break;
            case DistributionLaw.Uniform:
                r.Heading("Равномерный закон", "Uniform distribution");
                r.Formula($"Mx = (Rmin + Rmax) / 2 = ({r.N(d.Min)} + {r.N(d.Max)}) / 2 = {r.N(d.ExpectedValue)}");
                r.Formula($"σ = (Rmax − Rmin) / √12 = {r.N(d.ExpectedStdDev)}");
                r.Text("Если даны Mx и σ: Rmin = Mx − σ·√3, Rmax = Mx + σ·√3.", "Given M and σ: Rmin = M − σ·√3, Rmax = M + σ·√3.");
                break;
            case DistributionLaw.Exponential:
                r.Heading("Экспоненциальный закон", "Exponential distribution");
                r.Formula($"Mx = σ = {r.N(d.Mean)};  λ = 1 / Mx = {r.N(1 / Math.Max(d.Mean, 1e-12))}");
                r.Text("Маленькие значения часты, большие — редки. Описывает время между событиями.",
                       "Small values are frequent, large ones rare. Models time between events.");
                break;
            case DistributionLaw.Erlang:
                int k = Distribution.ErlangOrder(d.Mean, d.StdDev);
                r.Heading("Закон Эрланга", "Erlang distribution");
                r.Formula($"k = Mx² / σ² = {r.N(d.Mean)}² / {r.N(d.StdDev)}² ≈ {k}");
                r.Formula($"λ = k / Mx = {k} / {r.N(d.Mean)} = {r.N(k / Math.Max(d.Mean, 1e-12))}");
                r.Text("Сумма k экспоненциальных величин — «сглаженный» экспоненциальный закон.",
                       "Sum of k exponential variables — a \"smoothed\" exponential.");
                break;
            default:
                r.Heading("Постоянная величина", "Constant");
                r.Formula($"x = {r.N(d.Value)}");
                break;
        }
        return r;
    }

    /// <summary>Гистограмма выборки (для предпросмотра в редакторе закона).</summary>
    public static (double Min, double Max, int[] Counts, double SampleMean, double SampleStd) Histogram(Distribution d, int samples = 5000, int bins = 30, int seed = 1)
    {
        var rng = new Random(seed);
        var xs = new double[samples];
        for (int i = 0; i < samples; i++) xs[i] = d.Sample(rng);
        double min = xs.Min(), max = xs.Max();
        if (max - min < 1e-12) max = min + 1;
        var counts = new int[bins];
        foreach (var x in xs)
        {
            int b = (int)((x - min) / (max - min) * bins);
            counts[Math.Clamp(b, 0, bins - 1)]++;
        }
        double mean = xs.Average();
        double std = Math.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / samples);
        return (min, max, counts, mean, std);
    }
}
