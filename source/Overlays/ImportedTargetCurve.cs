namespace Resonalyze;

/// <summary>A house curve used instead of the parametric <see cref="TargetCurveSpec"/>; relative dB.</summary>
/// <remarks>Cleaned (duplicates averaged), anchored to 0 dB at <see cref="AnchorHz"/>, thinned to <see cref="MaximumPoints"/>
/// (stored by value). Log-linear between points; holds end values outside its range so the tuner chases nothing invented.</remarks>
public sealed class ImportedTargetCurve : IEquatable<ImportedTargetCurve>
{
    public const double AnchorHz = TargetCurveSpec.PivotHz;

    public const int MaximumPoints = 1024;

    private readonly double[] frequencies;
    private readonly double[] levelsDb;

    private ImportedTargetCurve(string name, double[] frequencies, double[] levelsDb)
    {
        Name = name;
        this.frequencies = frequencies;
        this.levelsDb = levelsDb;
    }

    public string Name { get; }

    public int PointCount => frequencies.Length;

    public double LowFrequencyHz => frequencies[0];

    public double HighFrequencyHz => frequencies[^1];

    /// <summary>Null when fewer than two usable points survive or anchoring overflows. Idempotent; every path goes through here.</summary>
    public static ImportedTargetCurve? FromPoints(
        string name,
        IEnumerable<OverlayPoint> points)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(points);

        List<OverlayPoint> usable = points
            .Where(point =>
                point.X > 0 && double.IsFinite(point.X) && double.IsFinite(point.Y))
            .OrderBy(point => point.X)
            .ToList();
        if (usable.Count < 2)
        {
            return null;
        }

        var frequencies = new List<double>(usable.Count);
        var levels = new List<double>(usable.Count);
        for (int index = 0; index < usable.Count;)
        {
            double frequency = usable[index].X;
            double sum = 0;
            int count = 0;
            while (index < usable.Count && usable[index].X == frequency)
            {
                sum += usable[index].Y;
                count++;
                index++;
            }

            frequencies.Add(frequency);
            levels.Add(sum / count);
        }

        if (frequencies.Count < 2)
        {
            return null;
        }

        double[] gridHz = frequencies.ToArray();
        double[] gridDb = levels.ToArray();
        if (gridHz.Length > MaximumPoints)
        {
            (gridHz, gridDb) = Resample(gridHz, gridDb);
        }

        double anchor = Interpolate(gridHz, gridDb, AnchorHz);
        if (anchor != 0)
        {
            for (int index = 0; index < gridDb.Length; index++)
            {
                gridDb[index] -= anchor;
            }
        }

        // ±1e308 is finite but the anchoring difference is not; the settings serializer would throw on it. Refuse.
        foreach (double level in gridDb)
        {
            if (!double.IsFinite(level))
            {
                return null;
            }
        }

        return new ImportedTargetCurve(name, gridHz, gridDb);
    }

    public double Evaluate(double frequencyHz) =>
        frequencyHz > 0 ? Interpolate(frequencies, levelsDb, frequencyHz) : 0;

    /// <summary>Flat "frequency, level, …" array, kept flat so the JSON stays readable.</summary>
    public double[] ToStorage()
    {
        var stored = new double[frequencies.Length * 2];
        for (int index = 0; index < frequencies.Length; index++)
        {
            stored[index * 2] = frequencies[index];
            stored[index * 2 + 1] = levelsDb[index];
        }

        return stored;
    }

    /// <summary>Null when nothing usable is stored; a trailing half pair is dropped.</summary>
    public static ImportedTargetCurve? FromStorage(string? name, double[]? stored)
    {
        if (stored is not { Length: >= 4 })
        {
            return null;
        }

        var points = new List<OverlayPoint>(stored.Length / 2);
        for (int index = 0; index + 1 < stored.Length; index += 2)
        {
            points.Add(new OverlayPoint(stored[index], stored[index + 1]));
        }

        return FromPoints(
            string.IsNullOrWhiteSpace(name) ? DefaultName : name,
            points);
    }

    public string Describe() =>
        $"{Name} — {PointCount} points, " +
        $"{FormatFrequency(LowFrequencyHz)} … {FormatFrequency(HighFrequencyHz)}";

    public bool Equals(ImportedTargetCurve? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other != null &&
            Name == other.Name &&
            frequencies.AsSpan().SequenceEqual(other.frequencies) &&
            levelsDb.AsSpan().SequenceEqual(other.levelsDb);
    }

    public override bool Equals(object? obj) => Equals(obj as ImportedTargetCurve);

    public override int GetHashCode() =>
        HashCode.Combine(Name, frequencies.Length, frequencies[0], frequencies[^1]);

    private const string DefaultName = "Imported curve";

    // Shared by Evaluate and resampling, so a thinned curve reads the file the same way.
    private static double Interpolate(
        double[] gridHz,
        double[] gridDb,
        double frequencyHz)
    {
        if (frequencyHz <= gridHz[0])
        {
            return gridDb[0];
        }

        if (frequencyHz >= gridHz[^1])
        {
            return gridDb[^1];
        }

        int found = Array.BinarySearch(gridHz, frequencyHz);
        if (found >= 0)
        {
            return gridDb[found];
        }

        int upper = ~found;
        int lower = upper - 1;
        double span = Math.Log10(gridHz[upper]) - Math.Log10(gridHz[lower]);
        if (!(span > 0))
        {
            return gridDb[lower];
        }

        double position = (Math.Log10(frequencyHz) - Math.Log10(gridHz[lower])) / span;
        return gridDb[lower] + position * (gridDb[upper] - gridDb[lower]);
    }

    private static (double[] Frequencies, double[] LevelsDb) Resample(
        double[] gridHz,
        double[] gridDb)
    {
        var frequencies = new double[MaximumPoints];
        var levels = new double[MaximumPoints];
        double logLow = Math.Log10(gridHz[0]);
        double logStep = (Math.Log10(gridHz[^1]) - logLow) / (MaximumPoints - 1);
        for (int index = 0; index < MaximumPoints; index++)
        {
            double frequency = Math.Pow(10, logLow + index * logStep);
            frequencies[index] = frequency;
            levels[index] = Interpolate(gridHz, gridDb, frequency);
        }

        // Keep the file's own ends so the thinned curve covers exactly its band.
        frequencies[0] = gridHz[0];
        frequencies[^1] = gridHz[^1];
        levels[0] = gridDb[0];
        levels[^1] = gridDb[^1];
        return (frequencies, levels);
    }

    private static string FormatFrequency(double frequencyHz) =>
        frequencyHz >= 1_000
            ? $"{frequencyHz / 1_000:0.###} kHz"
            : $"{frequencyHz:0.##} Hz";
}
