using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The single rule for a capture-vs-IR datum and set median, shared by plot and audition (spread thresholds were calibrated on it). See docs/tech/spatial-average.md#set-offset-and-spread.</summary>
internal static class SpatialAverageOffsets
{
    /// <summary>dB below the channel peak still read: whole working band with skirts, out of the stopband.</summary>
    public const double WorkingBandDb = 20;

    /// <summary>Median of <paramref name="reference"/> minus <paramref name="average"/> in the working band; null without overlap.</summary>
    public static double? ChannelDatumDb(
        IReadOnlyList<SignalPoint> average,
        IReadOnlyList<SignalPoint> reference)
    {
        ArgumentNullException.ThrowIfNull(average);
        ArgumentNullException.ThrowIfNull(reference);
        int count = Math.Min(average.Count, reference.Count);
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

    /// <summary>True median (mean of the central pair); sorts in place.</summary>
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
