using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>A delay-search lobe: one local best of the junction's score sweep.</summary>
/// <param name="ScoreDb">The penalized summation loss at the lobe, dB, 0 being perfect.</param>
internal sealed record AgentLobe(double DelayMs, bool Invert, double ScoreDb);

/// <summary>
/// The protocol's sampling: fixed grids in points per octave, log-frequency
/// interpolation off the analysis curves, and the thinning that keeps a series
/// readable. None of it depends on a plot's width or zoom — a package copied at
/// two window sizes is the same package.
/// </summary>
internal static class AgentCurveSampling
{
    public const int BroadbandPointsPerOctave = 12;
    public const int JunctionPointsPerOctave = 24;
    public const double BroadbandLowHz = 20;
    public const double BroadbandHighHz = 20_000;

    /// <summary>
    /// Log-spaced frequencies from <paramref name="lowHz"/> to <paramref name="highHz"/>,
    /// both included, at the given density. Empty when the span is not a span.
    /// </summary>
    public static List<double> LogGrid(double lowHz, double highHz, int pointsPerOctave)
    {
        var grid = new List<double>();
        if (!(lowHz > 0) || !(highHz > lowHz) || pointsPerOctave <= 0)
        {
            return grid;
        }

        double octaves = Math.Log2(highHz / lowHz);
        int steps = (int)Math.Floor(octaves * pointsPerOctave + 1e-9);
        for (int index = 0; index <= steps; index++)
        {
            grid.Add(lowHz * Math.Pow(2, (double)index / pointsPerOctave));
        }
        if (highHz / grid[^1] > 1.0001)
        {
            grid.Add(highHz);
        }

        return grid;
    }

    /// <summary>
    /// The dense grid around a junction: an octave to each side of the crossover
    /// at <see cref="JunctionPointsPerOctave"/>, clipped to the given span, with
    /// the crossover frequency itself always a point.
    /// </summary>
    public static List<double> JunctionGrid(double crossoverHz, double lowHz, double highHz)
    {
        double low = Math.Max(crossoverHz / 2, lowHz);
        double high = Math.Min(crossoverHz * 2, highHz);
        List<double> grid = LogGrid(low, high, JunctionPointsPerOctave);
        if (crossoverHz > low && crossoverHz < high &&
            !grid.Any(frequency => Math.Abs(frequency / crossoverHz - 1) < 1e-6))
        {
            int at = grid.FindIndex(frequency => frequency > crossoverHz);
            grid.Insert(at < 0 ? grid.Count : at, crossoverHz);
        }

        return grid;
    }

    /// <summary>
    /// The curve's value at a frequency, interpolated linearly in log-frequency
    /// between its two neighbours; null outside the curve, and null where either
    /// neighbour is not a number — a hole in a measured band is reported as a hole,
    /// never bridged.
    /// </summary>
    public static double? Sample(IReadOnlyList<SignalPoint> curve, double frequencyHz)
    {
        ArgumentNullException.ThrowIfNull(curve);

        int count = curve.Count;
        if (count == 0 || frequencyHz < curve[0].X || frequencyHz > curve[count - 1].X)
        {
            return null;
        }

        // First point at or beyond the frequency.
        int low = 0;
        int high = count - 1;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (curve[mid].X < frequencyHz)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        SignalPoint upper = curve[low];
        if (upper.X == frequencyHz || low == 0)
        {
            return Finite(upper.Y);
        }

        SignalPoint lower = curve[low - 1];
        if (!double.IsFinite(lower.Y) || !double.IsFinite(upper.Y))
        {
            return null;
        }
        if (upper.X <= lower.X || lower.X <= 0)
        {
            return upper.Y;
        }

        double t = Math.Log(frequencyHz / lower.X) / Math.Log(upper.X / lower.X);
        return lower.Y + (upper.Y - lower.Y) * t;
    }

    /// <summary>At most <paramref name="maxCount"/> items, evenly spaced, first and last kept.</summary>
    public static List<T> Thin<T>(IReadOnlyList<T> items, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count <= maxCount || maxCount < 2)
        {
            return [.. items];
        }

        var thinned = new List<T>(maxCount);
        for (int index = 0; index < maxCount; index++)
        {
            int source = (int)Math.Round((double)index * (items.Count - 1) / (maxCount - 1));
            thinned.Add(items[source]);
        }

        return thinned;
    }

    /// <summary>
    /// The lobes of a junction's score sweep: every local maximum of either
    /// polarity's curve, best first, at most <paramref name="max"/>. The sweep is
    /// the search's own surface, so these are the candidates an Auto delay run
    /// would weigh — read off the drawn curve rather than re-searched.
    /// </summary>
    public static List<AgentLobe> Lobes(
        IReadOnlyList<SignalPoint> normal,
        IReadOnlyList<SignalPoint> inverted,
        int max)
    {
        var lobes = new List<AgentLobe>();
        Collect(normal, invert: false);
        Collect(inverted, invert: true);
        return lobes
            .OrderByDescending(lobe => lobe.ScoreDb)
            .Take(max)
            .ToList();

        void Collect(IReadOnlyList<SignalPoint> sweep, bool invert)
        {
            for (int index = 0; index < sweep.Count; index++)
            {
                double value = sweep[index].Y;
                if (!double.IsFinite(value))
                {
                    continue;
                }
                bool risesBefore = index == 0 || !(sweep[index - 1].Y >= value);
                bool fallsAfter = index == sweep.Count - 1 || !(sweep[index + 1].Y > value);
                // Endpoints are not lobes: a sweep climbing into its edge says the
                // lobe sits outside the window, not at it.
                if (index > 0 && index < sweep.Count - 1 && risesBefore && fallsAfter)
                {
                    lobes.Add(new AgentLobe(sweep[index].X, invert, value));
                }
            }
        }
    }

    /// <summary>The curve's highest (or lowest) finite point, as (x, y); null on an empty curve.</summary>
    public static (double X, double Y)? Extremum(IReadOnlyList<SignalPoint> curve, bool maximum)
    {
        ArgumentNullException.ThrowIfNull(curve);

        (double X, double Y)? best = null;
        foreach (SignalPoint point in curve)
        {
            if (!double.IsFinite(point.Y))
            {
                continue;
            }
            if (best == null || (maximum ? point.Y > best.Value.Y : point.Y < best.Value.Y))
            {
                best = (point.X, point.Y);
            }
        }

        return best;
    }

    /// <summary>Rounded to a fixed number of decimals; null where the value is not a number.</summary>
    public static double? Round(double? value, int decimals) =>
        value is { } number && double.IsFinite(number) ? Math.Round(number, decimals) : null;

    /// <summary>A frequency to four significant digits — 1234.5 Hz reads as 1235, 20.03 as 20.03.</summary>
    public static double Frequency(double hz)
    {
        if (!(hz > 0) || !double.IsFinite(hz))
        {
            return hz;
        }

        int decimals = 3 - (int)Math.Floor(Math.Log10(hz));
        if (decimals < 0)
        {
            double scale = Math.Pow(10, -decimals);
            return Math.Round(hz / scale, MidpointRounding.AwayFromZero) * scale;
        }

        return Math.Round(hz, Math.Min(decimals, 6), MidpointRounding.AwayFromZero);
    }

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
}
