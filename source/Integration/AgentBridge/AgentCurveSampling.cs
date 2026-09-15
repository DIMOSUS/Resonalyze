using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

/// <param name="ScoreDb">Penalized summation loss at the lobe, dB; 0 is perfect.</param>
internal sealed record AgentLobe(double DelayMs, bool Invert, double ScoreDb);

/// <summary>Curve densities. Packages descend <see cref="Ladder"/> to fit; figures are computed before sampling, so thinning never changes the numbers. See docs/tech/agent-bridge.md#package-size.</summary>
internal sealed record AgentSampling(
    int BroadbandPointsPerOctave,
    int JunctionPointsPerOctave,
    int SweepRows,
    int CorrelationRows)
{
    public static readonly AgentSampling Nominal = new(12, 24, 48, 48);

    /// <summary>Junction grid and lag series thin first, then broadband; the last step still resolves 1/3 octave and a dozen lags.</summary>
    public static readonly IReadOnlyList<AgentSampling> Ladder =
    [
        Nominal,
        new(12, 16, 32, 32),
        new(8, 12, 24, 24),
        new(6, 8, 16, 16),
        new(4, 6, 12, 12)
    ];

    public const int MaxPointsPerOctave = 48;

    public const int MaxRows = 192;
}

/// <summary>Fixed grids independent of plot width or zoom: a package copied at two window sizes is the same package.</summary>
internal static class AgentCurveSampling
{
    public const int BroadbandPointsPerOctave = 12;
    public const int JunctionPointsPerOctave = 24;
    public const double BroadbandLowHz = 20;
    public const double BroadbandHighHz = 20_000;

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

    /// <summary>An octave each side of the crossover, clipped to the span; the crossover itself is always a point.</summary>
    public static List<double> JunctionGrid(
        double crossoverHz, double lowHz, double highHz, int pointsPerOctave = JunctionPointsPerOctave)
    {
        double low = Math.Max(crossoverHz / 2, lowHz);
        double high = Math.Min(crossoverHz * 2, highHz);
        List<double> grid = LogGrid(low, high, pointsPerOctave);
        if (crossoverHz > low && crossoverHz < high &&
            !grid.Any(frequency => Math.Abs(frequency / crossoverHz - 1) < 1e-6))
        {
            int at = grid.FindIndex(frequency => frequency > crossoverHz);
            grid.Insert(at < 0 ? grid.Count : at, crossoverHz);
        }

        return grid;
    }

    /// <summary>Log-frequency linear interpolation; null outside the curve or next to a NaN (holes are never bridged).</summary>
    public static double? Sample(IReadOnlyList<SignalPoint> curve, double frequencyHz)
    {
        ArgumentNullException.ThrowIfNull(curve);

        int count = curve.Count;
        if (count == 0 || frequencyHz < curve[0].X || frequencyHz > curve[count - 1].X)
        {
            return null;
        }

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

    /// <summary>Local maxima of either polarity's score sweep, best first: the candidates an Auto delay run would weigh.</summary>
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
                // Endpoints are not lobes: a sweep climbing into its edge has its lobe outside the window.
                if (index > 0 && index < sweep.Count - 1 && risesBefore && fallsAfter)
                {
                    lobes.Add(new AgentLobe(sweep[index].X, invert, value));
                }
            }
        }
    }

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

    public static double? Round(double? value, int decimals) =>
        value is { } number && double.IsFinite(number) ? Math.Round(number, decimals) : null;

    /// <summary>Four significant digits: 1234.5 Hz reads as 1235, 20.03 as 20.03.</summary>
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
