using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// How far a spatially averaged capture sits from the impulse response of the same
/// driver — the datum that puts a whole SET of captures on the responses' axis, and
/// the median that turns a set of them into one scalar.
/// </summary>
/// <remarks>
/// The pair of curves is the caller's business, the rule for reading a difference
/// off them is not. The plot reads it on GATED curves, because everything beside it
/// there is gated; the audition render reads it on UNGATED ones, because the kernel
/// it corrects carries the whole decay. Both must read it the SAME way once they
/// have their pair — the spread thresholds the panel warns on were calibrated under
/// exactly this band and this median, and a second copy of either would drift away
/// from the evidence they were measured against.
/// </remarks>
internal static class SpatialAverageOffsets
{
    /// <summary>
    /// How far below its own peak a channel is still read when its datum is taken.
    /// </summary>
    /// <remarks>
    /// Wide enough to hold a driver's whole working band with its crossover skirts,
    /// narrow enough to stay out of the stopband — where the impulse response shows
    /// what the room and the noise floor left of a filtered driver while the capture
    /// shows the filter's own analytic slope, and the two part by tens of dB.
    /// </remarks>
    public const double WorkingBandDb = 20;

    /// <summary>
    /// One channel's median difference inside its own working band — how far
    /// <paramref name="reference"/> sits ABOVE <paramref name="average"/>. Null when
    /// the two curves never overlap there: nothing to align against.
    /// </summary>
    public static double? ChannelDatumDb(
        IReadOnlyList<SignalPoint> average,
        IReadOnlyList<SignalPoint> reference)
    {
        ArgumentNullException.ThrowIfNull(average);
        ArgumentNullException.ThrowIfNull(reference);
        int count = Math.Min(average.Count, reference.Count);
        // The peak is taken over the points where BOTH curves exist, or the band it
        // sets could sit where the average has nothing to say.
        double peak = double.NegativeInfinity;
        for (int k = 0; k < count; k++)
        {
            if (double.IsFinite(reference[k].Y) && double.IsFinite(average[k].Y))
            {
                peak = Math.Max(peak, reference[k].Y);
            }
        }

        if (double.IsNegativeInfinity(peak))
        {
            return null;
        }

        double floor = peak - WorkingBandDb;
        var differences = new List<double>();
        for (int k = 0; k < count; k++)
        {
            double difference = reference[k].Y - average[k].Y;
            if (double.IsFinite(difference) && reference[k].Y >= floor)
            {
                differences.Add(difference);
            }
        }

        return differences.Count == 0 ? null : Median(differences);
    }

    /// <summary>
    /// The middle of a set of levels — the mean of the two central values when there
    /// is an even number of them, not the upper one.
    /// </summary>
    /// <remarks>
    /// Taking the upper central value moves the whole hybrid set by half the gap
    /// between the two middle channels, which on a four-way is not a rounding
    /// difference. The list is sorted in place.
    /// </remarks>
    public static double Median(List<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : 0.5 * (values[middle - 1] + values[middle]);
    }
}
