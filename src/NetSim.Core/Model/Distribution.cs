namespace NetSim.Core.Model;

/// <summary>Законы распределения, используемые в практиках (ПР4, ПР5).</summary>
public enum DistributionLaw
{
    Constant,
    Uniform,
    Normal,
    Exponential,
    Erlang,
}

/// <summary>
/// Случайная величина: размер транзакции, пауза между транзакциями, Reply Size, Reply Delay.
/// Для каждого закона используются свои параметры:
/// Constant — Value; Uniform — Min/Max; Normal — Mean/StdDev; Exponential — Mean; Erlang — Mean/StdDev.
/// </summary>
public sealed class Distribution : ObservableBase
{
    private DistributionLaw _law = DistributionLaw.Constant;
    private double _value = 1;
    private double _mean = 1;
    private double _stdDev;
    private double _min;
    private double _max = 1;

    public DistributionLaw Law { get => _law; set { if (Set(ref _law, value)) OnPropertyChanged(nameof(Summary)); } }
    public double Value { get => _value; set { if (Set(ref _value, value)) OnPropertyChanged(nameof(Summary)); } }
    public double Mean { get => _mean; set { if (Set(ref _mean, value)) OnPropertyChanged(nameof(Summary)); } }
    public double StdDev { get => _stdDev; set { if (Set(ref _stdDev, value)) OnPropertyChanged(nameof(Summary)); } }
    public double Min { get => _min; set { if (Set(ref _min, value)) OnPropertyChanged(nameof(Summary)); } }
    public double Max { get => _max; set { if (Set(ref _max, value)) OnPropertyChanged(nameof(Summary)); } }

    public static Distribution Constant(double v) => new() { Law = DistributionLaw.Constant, Value = v };
    public static Distribution Uniform(double min, double max) => new() { Law = DistributionLaw.Uniform, Min = min, Max = max };
    public static Distribution Normal(double mean, double sd) => new() { Law = DistributionLaw.Normal, Mean = mean, StdDev = sd };
    public static Distribution Exponential(double mean) => new() { Law = DistributionLaw.Exponential, Mean = mean };
    public static Distribution Erlang(double mean, double sd) => new() { Law = DistributionLaw.Erlang, Mean = mean, StdDev = sd };

    /// <summary>Равномерный закон по мат. ожиданию и СКО (ПР5): Rmin = M − σ√3, Rmax = M + σ√3.</summary>
    public static Distribution UniformFromMeanStd(double mean, double sd)
    {
        var (min, max) = UniformBounds(mean, sd);
        return Uniform(min, max);
    }

    public static (double Min, double Max) UniformBounds(double mean, double sd) =>
        (mean - sd * Math.Sqrt(3), mean + sd * Math.Sqrt(3));

    /// <summary>Порядок k закона Эрланга: k = M² / σ² (не меньше 1).</summary>
    public static int ErlangOrder(double mean, double sd) =>
        sd <= 0 ? 1 : Math.Max(1, (int)Math.Round(mean * mean / (sd * sd)));

    /// <summary>Теоретическое мат. ожидание.</summary>
    public double ExpectedValue => Law switch
    {
        DistributionLaw.Constant => Value,
        DistributionLaw.Uniform => (Min + Max) / 2,
        _ => Mean,
    };

    /// <summary>Теоретическое СКО.</summary>
    public double ExpectedStdDev => Law switch
    {
        DistributionLaw.Constant => 0,
        DistributionLaw.Uniform => (Max - Min) / Math.Sqrt(12),
        DistributionLaw.Normal => StdDev,
        DistributionLaw.Exponential => Mean,
        DistributionLaw.Erlang => Mean / Math.Sqrt(ErlangOrder(Mean, StdDev)),
        _ => 0,
    };

    /// <summary>Получить случайное значение (неотрицательное).</summary>
    public double Sample(Random rng)
    {
        double x = Law switch
        {
            DistributionLaw.Constant => Value,
            DistributionLaw.Uniform => Min + (Max - Min) * rng.NextDouble(),
            DistributionLaw.Normal => Mean + StdDev * StandardNormal(rng),
            DistributionLaw.Exponential => -Mean * Math.Log(1 - rng.NextDouble()),
            DistributionLaw.Erlang => SampleErlang(rng),
            _ => Value,
        };
        return x < 0 ? 0 : x;
    }

    private double SampleErlang(Random rng)
    {
        int k = ErlangOrder(Mean, StdDev);
        double stageMean = Mean / k, sum = 0;
        for (int i = 0; i < k; i++) sum += -stageMean * Math.Log(1 - rng.NextDouble());
        return sum;
    }

    private static double StandardNormal(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public Distribution Clone() => new()
    {
        Law = Law, Value = Value, Mean = Mean, StdDev = StdDev, Min = Min, Max = Max,
    };

    public void CopyFrom(Distribution d)
    {
        Law = d.Law; Value = d.Value; Mean = d.Mean; StdDev = d.StdDev; Min = d.Min; Max = d.Max;
    }

    /// <summary>Короткое описание, не зависящее от языка интерфейса.</summary>
    public string Summary => Law switch
    {
        DistributionLaw.Constant => $"Const({Fmt(Value)})",
        DistributionLaw.Uniform => $"Uniform({Fmt(Min)}…{Fmt(Max)})",
        DistributionLaw.Normal => $"Normal(M={Fmt(Mean)}, σ={Fmt(StdDev)})",
        DistributionLaw.Exponential => $"Exp(M={Fmt(Mean)})",
        DistributionLaw.Erlang => $"Erlang(M={Fmt(Mean)}, σ={Fmt(StdDev)})",
        _ => "?",
    };

    private static string Fmt(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    public override string ToString() => Summary;
}
