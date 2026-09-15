namespace Resonalyze.Dsp;

/// <summary>Array curves levelled to the anchor, their average and spread. Null curve/trim = microphone left out.</summary>
public sealed record SpatialAverageResult(
    IReadOnlyList<double[]?> TrimmedCurvesDb,
    IReadOnlyList<double?> TrimsDb,
    double[] AverageDb,
    double[] SpreadDb);

/// <summary>Power average of a driver over microphone positions, on the shared log grid.
/// See docs/tech/spatial-average.md#averaging-core.</summary>
public static class SpatialAverage
{
    public const double GridStartHz = 20.0;

    public const double GridStopHz = 20_000.0;

    public const int GridBandCount = 1_024;

    /// <summary>The app-wide frequency grid, shared so no boundary needs resampling.</summary>
    public static IReadOnlyList<double> BuildGrid() =>
        EqualizationCurve.LogFrequencyGrid(GridStartHz, GridStopHz, GridBandCount);

    /// <summary>Trim compares only bands within this many dB of the anchor peak; the rest is noise floor.</summary>
    public const double DefaultTrimBandDb = 20.0;

    /// <summary>Band mean of power onto <see cref="BuildGrid"/>; bands with no measured bin are NaN.
    /// See docs/tech/spatial-average.md#reading-bins-onto-the-grid.</summary>
    public static double[] FromTransferMagnitude(
        IReadOnlyList<double> magnitude,
        double binWidthHz)
    {
        ArgumentNullException.ThrowIfNull(magnitude);
        if (!double.IsFinite(binWidthHz) || binWidthHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(binWidthHz));
        }

        IReadOnlyList<double> grid = BuildGrid();
        var levels = new double[grid.Count];
        double halfStep = Math.Sqrt(grid[1] / grid[0]);
        int highestBin = magnitude.Count - 1;
        for (int band = 0; band < grid.Count; band++)
        {
            int firstBin = (int)Math.Ceiling(grid[band] / halfStep / binWidthHz);
            int lastBin = (int)Math.Floor(grid[band] * halfStep / binWidthHz);
            // A band narrower than the bin spacing reads the bin it sits in.
            firstBin = Math.Max(firstBin, 1);
            lastBin = Math.Min(lastBin, highestBin);
            if (firstBin > lastBin)
            {
                int nearest = (int)Math.Round(grid[band] / binWidthHz);
                firstBin = Math.Clamp(nearest, 1, highestBin);
                lastBin = firstBin;
            }

            double power = 0.0;
            int measured = 0;
            for (int bin = firstBin; bin <= lastBin; bin++)
            {
                double value = magnitude[bin];
                if (value > 0.0)
                {
                    power += value * value;
                    measured++;
                }
            }

            levels[band] = measured == 0
                ? double.NaN
                : 10.0 * Math.Log10(power / measured);
        }

        return levels;
    }

    /// <summary>Levels every microphone to the anchor (not the set mean), then averages.
    /// See docs/tech/spatial-average.md#levelling-to-the-anchor.</summary>
    public static SpatialAverageResult Average(
        IReadOnlyList<IReadOnlyList<double>> curvesDb,
        int anchorIndex,
        double trimBandDb = DefaultTrimBandDb)
    {
        int bandCount = RequireCommonGrid(curvesDb);
        if ((uint)anchorIndex >= (uint)curvesDb.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(anchorIndex));
        }

        IReadOnlyList<double> anchor = curvesDb[anchorIndex];
        var trims = new double?[curvesDb.Count];
        var trimmed = new double[]?[curvesDb.Count];
        for (int microphone = 0; microphone < curvesDb.Count; microphone++)
        {
            double? trim = microphone == anchorIndex
                ? 0.0
                : ResolveTrimDb(curvesDb[microphone], anchor, trimBandDb);
            trims[microphone] = trim;
            if (trim is not { } offset)
            {
                continue;
            }

            IReadOnlyList<double> curve = curvesDb[microphone];
            var placed = new double[bandCount];
            for (int band = 0; band < bandCount; band++)
            {
                placed[band] = double.IsFinite(curve[band])
                    ? curve[band] + offset
                    : double.NaN;
            }

            trimmed[microphone] = placed;
        }

        var placedCurves = new List<double[]>(curvesDb.Count);
        foreach (double[]? curve in trimmed)
        {
            if (curve != null)
            {
                placedCurves.Add(curve);
            }
        }

        return new SpatialAverageResult(
            trimmed,
            trims,
            RmsAverageDb(placedCurves),
            SpreadDb(placedCurves));
    }

    /// <summary>Median offset over the common working band; null when there is none (callers must drop the microphone).</summary>
    public static double? ResolveTrimDb(
        IReadOnlyList<double> curveDb,
        IReadOnlyList<double> anchorDb,
        double trimBandDb = DefaultTrimBandDb)
    {
        ArgumentNullException.ThrowIfNull(curveDb);
        ArgumentNullException.ThrowIfNull(anchorDb);
        if (curveDb.Count != anchorDb.Count)
        {
            throw new ArgumentException(
                "The microphone and the anchor must be on the same grid.",
                nameof(curveDb));
        }
        if (!double.IsFinite(trimBandDb) || trimBandDb <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trimBandDb));
        }

        double peak = double.NegativeInfinity;
        for (int band = 0; band < anchorDb.Count; band++)
        {
            if (double.IsFinite(anchorDb[band]) && double.IsFinite(curveDb[band]))
            {
                peak = Math.Max(peak, anchorDb[band]);
            }
        }

        if (double.IsNegativeInfinity(peak))
        {
            return null;
        }

        double floor = peak - trimBandDb;
        var differences = new List<double>();
        for (int band = 0; band < anchorDb.Count; band++)
        {
            if (double.IsFinite(anchorDb[band]) &&
                double.IsFinite(curveDb[band]) &&
                anchorDb[band] >= floor)
            {
                differences.Add(anchorDb[band] - curveDb[band]);
            }
        }

        return differences.Count == 0 ? null : Median(differences);
    }

    /// <summary>Power (RMS pressure) average per band, not a dB mean; a linear filter factors out of it.</summary>
    public static double[] RmsAverageDb(IReadOnlyList<IReadOnlyList<double>> curvesDb)
    {
        int bandCount = RequireCommonGrid(curvesDb);
        var average = new double[bandCount];
        for (int band = 0; band < bandCount; band++)
        {
            double power = 0.0;
            int count = 0;
            foreach (IReadOnlyList<double> curve in curvesDb)
            {
                double level = curve[band];
                if (!double.IsFinite(level))
                {
                    continue;
                }

                power += Math.Pow(10.0, level / 10.0);
                count++;
            }

            average[band] = count == 0
                ? double.NaN
                : 10.0 * Math.Log10(power / count);
        }

        return average;
    }

    /// <summary>Loudest minus quietest per band; NaN below two microphones (not 0, which would mean perfect agreement).</summary>
    public static double[] SpreadDb(IReadOnlyList<IReadOnlyList<double>> curvesDb)
    {
        int bandCount = RequireCommonGrid(curvesDb);
        var spread = new double[bandCount];
        for (int band = 0; band < bandCount; band++)
        {
            double lowest = double.PositiveInfinity;
            double highest = double.NegativeInfinity;
            int count = 0;
            foreach (IReadOnlyList<double> curve in curvesDb)
            {
                double level = curve[band];
                if (!double.IsFinite(level))
                {
                    continue;
                }

                lowest = Math.Min(lowest, level);
                highest = Math.Max(highest, level);
                count++;
            }

            spread[band] = count < 2 ? double.NaN : highest - lowest;
        }

        return spread;
    }

    private static int RequireCommonGrid(IReadOnlyList<IReadOnlyList<double>> curvesDb)
    {
        ArgumentNullException.ThrowIfNull(curvesDb);
        if (curvesDb.Count == 0)
        {
            throw new ArgumentException(
                "There are no microphones to average.",
                nameof(curvesDb));
        }

        int bandCount = -1;
        for (int i = 0; i < curvesDb.Count; i++)
        {
            IReadOnlyList<double> curve = curvesDb[i] ?? throw new ArgumentException(
                "A microphone curve is missing.",
                nameof(curvesDb));
            if (bandCount < 0)
            {
                bandCount = curve.Count;
                if (bandCount == 0)
                {
                    throw new ArgumentException(
                        "A microphone curve is empty.",
                        nameof(curvesDb));
                }
                continue;
            }
            if (curve.Count != bandCount)
            {
                throw new ArgumentException(
                    "Every microphone must be on the same grid.",
                    nameof(curvesDb));
            }
        }

        return bandCount;
    }

    // Even count: mean of the two central values. Sorts in place.
    private static double Median(List<double> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : 0.5 * (values[middle - 1] + values[middle]);
    }
}
