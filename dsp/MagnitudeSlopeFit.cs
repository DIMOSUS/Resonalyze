namespace Resonalyze.Dsp;

/// <summary>
/// How fast a magnitude falls through a band, in dB per octave (negative = falling): a weighted straight line through
/// dB against log2(f). An electrical filter can only make a response STEEPER, so a driver's own fall here is what
/// says whether a stated acoustic slope is reachable at all.
/// </summary>
/// <remarks>
/// An accumulator rather than a function over a curve, because its callers hold their points in different shapes
/// (junction bins, plotted curves) and the hot ones would rather not build a list to be read once.
/// </remarks>
public sealed class MagnitudeSlopeFit
{
    /// <summary>Below this a line through the points says more about the reader than about the driver.</summary>
    public const int MinimumPoints = 3;

    /// <summary>Octaves the points must span before a slope is answered: a line fitted across a twelfth of an octave
    /// reads the room's own ripple as a slope.</summary>
    public const double MinimumSpanOctaves = 0.25;

    private double weightSum;
    private double weightedX;
    private double weightedY;
    private double weightedXx;
    private double weightedXy;
    private double lowestX = double.PositiveInfinity;
    private double highestX = double.NegativeInfinity;

    public int Count { get; private set; }

    /// <summary>Octaves between the lowest and highest point taken, zero while there are none.</summary>
    public double SpanOctaves =>
        Count == 0 ? 0 : highestX - lowestX;

    /// <summary>Ignores a point with no level, no frequency or no weight: an unusable bin is not a zero.</summary>
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

    /// <summary>The fitted slope, or null while the points are too few or too close together to carry one.</summary>
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
