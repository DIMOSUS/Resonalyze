namespace Resonalyze;

/// <summary>
/// A target shape read from a file — a house curve — used INSTEAD of the
/// parametric terms of <see cref="TargetCurveSpec"/>. Its points are relative dB
/// over frequency, exactly like the parametric shape, so the level it hangs at
/// stays the plot's own (see <see cref="EqTargetCurve"/>).
/// </summary>
/// <remarks>
/// <para>
/// Three things happen on the way in, and they are what make an arbitrary file
/// usable as a target rather than merely readable:
/// </para>
/// <list type="bullet">
/// <item>the pairs are cleaned and ordered — non-finite values and frequencies at
/// or below zero are dropped, points are sorted, and duplicates of one frequency
/// are averaged, because the curve is interpolated by frequency and two values at
/// one frequency have no order to interpolate along;</item>
/// <item>the shape is anchored: whatever the curve reads at <see cref="AnchorHz"/>
/// is subtracted from it, so a file written around 75 dB SPL and one written
/// around 0 dB become the same target. The level a target hangs at belongs to the
/// plot and not to the shape, and this is the same 1 kHz pivot the parametric tilt
/// turns around;</item>
/// <item>a dense file is thinned to <see cref="MaximumPoints"/>. The curve is
/// carried BY VALUE into the settings file and into a Virtual DSP session — a
/// stored path would break the moment the file moved — and a full-resolution
/// export runs to tens of thousands of lines, which is a target stated far finer
/// than a target means anything at.</item>
/// </list>
/// <para>
/// Between its points the curve is straight in log frequency and dB. OUTSIDE its
/// range it HOLDS its end values instead of continuing their slope: a house curve
/// that stops at 200 Hz says nothing about 10 kHz, and extending the last slope
/// there would invent a target the file never stated — one the auto-tuner would
/// then dutifully chase.
/// </para>
/// </remarks>
public sealed class ImportedTargetCurve : IEquatable<ImportedTargetCurve>
{
    /// <summary>
    /// The frequency the imported shape is anchored to 0 dB at — the pivot the
    /// parametric tilt turns around, so both kinds of target mean the same thing
    /// by "relative dB".
    /// </summary>
    public const double AnchorHz = TargetCurveSpec.PivotHz;

    /// <summary>
    /// The most points a stored target keeps; a denser file is resampled onto a
    /// log grid of this size across its own range.
    /// </summary>
    public const int MaximumPoints = 1024;

    private readonly double[] frequencies;
    private readonly double[] levelsDb;

    private ImportedTargetCurve(string name, double[] frequencies, double[] levelsDb)
    {
        Name = name;
        this.frequencies = frequencies;
        this.levelsDb = levelsDb;
    }

    /// <summary>Where the curve came from — a file name, kept as a label.</summary>
    public string Name { get; }

    public int PointCount => frequencies.Length;

    public double LowFrequencyHz => frequencies[0];

    public double HighFrequencyHz => frequencies[^1];

    /// <summary>
    /// The curve the given pairs describe, or <c>null</c> when fewer than two
    /// usable points survive the cleaning above — one point is a level, not a
    /// shape. Every path that builds a curve goes through here, including the one
    /// that reads a stored one back: a file is where a NaN or an unordered pair
    /// can enter, and the anchoring is idempotent, so re-running it costs nothing.
    /// </summary>
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

        return new ImportedTargetCurve(name, gridHz, gridDb);
    }

    /// <summary>Relative target level (dB) at the given frequency.</summary>
    public double Evaluate(double frequencyHz) =>
        frequencyHz > 0 ? Interpolate(frequencies, levelsDb, frequencyHz) : 0;

    /// <summary>
    /// The curve as the flat "frequency, level, frequency, level, …" array the
    /// settings file and a Virtual DSP session store it as. One array rather than
    /// an object per point: this is a plain list of numbers, and at up to
    /// <see cref="MaximumPoints"/> pairs the shape of the JSON is what decides
    /// whether the file is still readable by a person.
    /// </summary>
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

    /// <summary>
    /// Reads back what <see cref="ToStorage"/> wrote, or <c>null</c> when the file
    /// carries no curve — or carries one nothing usable survives. A trailing half
    /// pair is dropped rather than refused, in the spirit of the text import: what
    /// is readable is read.
    /// </summary>
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

    /// <summary>What the curve is, for a menu tooltip or a dialog label.</summary>
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

    // The name a stored curve falls back to when the file did not record one.
    private const string DefaultName = "Imported curve";

    // Straight in log frequency and dB between points, held flat outside the range
    // (see the remarks above). Shared by Evaluate and by the resampling below, so a
    // thinned curve is the same reading of the file as an unthinned one.
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

        // The ends are the file's own, not what rounding put just inside or outside
        // them: the thinned curve must still cover exactly the band it stated.
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
