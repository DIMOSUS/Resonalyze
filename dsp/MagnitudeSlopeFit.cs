namespace Resonalyze.Dsp;

/// <summary>A weighted line through dB against log2(f): dB per octave, negative = falling.</summary>
public sealed class MagnitudeSlopeFit
{
    public const int MinimumPoints = 3;

    /// <summary>Octaves the points must span, or the room's ripple reads as a slope.</summary>
    public const double MinimumSpanOctaves = 0.25;

    private double weightSum;
    private double weightedX;
    private double weightedY;
    private double weightedXx;
    private double weightedXy;
    private double lowestX = double.PositiveInfinity;
    private double highestX = double.NegativeInfinity;

    public int Count { get; private set; }

    public double SpanOctaves =>
        Count == 0 ? 0 : highestX - lowestX;

    /// <summary>Ignores a point with no level, frequency or weight.</summary>
    public void Add(double frequencyHz, double levelDb, double weight = 1.0)
    {
        if (!(frequencyHz > 0) || !double.IsFinite(levelDb) || !(weight > 0) || !double.IsFinite(weight))
        {
            return;
        }

        double x = Math.Log2(frequencyHz);
        weightSum += weight;
        weightedX += weight * x;
        weightedY += weight * levelDb;
        weightedXx += weight * x * x;
        weightedXy += weight * x * levelDb;
        lowestX = Math.Min(lowestX, x);
        highestX = Math.Max(highestX, x);
        Count++;
    }

    /// <summary>Null while the points are too few or span too little.</summary>
    public double? DbPerOctave
    {
        get
        {
            if (Count < MinimumPoints || SpanOctaves < MinimumSpanOctaves || !(weightSum > 0))
            {
                return null;
            }

            double denominator = weightSum * weightedXx - weightedX * weightedX;
            if (!(Math.Abs(denominator) > 0))
            {
                return null;
            }

            double slope = (weightSum * weightedXy - weightedX * weightedY) / denominator;
            return double.IsFinite(slope) ? slope : null;
        }
    }
}
