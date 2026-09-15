namespace Resonalyze;

/// <summary>Ranks channels by sweep match, not level; a DAW reference track makes the choice the user's. See docs/tech/sweep-measurement.md#multi-channel-recordings.</summary>
internal static class RecordedSweepChannels
{
    /// <summary>Runner-up within a quarter (12 dB) of the best is ambiguous; real takes and dead channels sit a factor of ten apart.</summary>
    public const double AmbiguousShare = 0.25;

    public static double[] Rank(
        SweepMeasurementConfiguration configuration,
        IReadOnlyList<float[]> channels)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(channels);

        using var probe = new ExponentialSineSweep();
        SweepSignalConfiguration signal = configuration.Signal;
        probe.FillData(
            signal.LowFrequencyHz,
            signal.HighFrequencyHz,
            signal.RequestedDurationSeconds,
            signal.Bits,
            signal.SampleRate);

        var qualities = new double[channels.Count];
        for (int channel = 0; channel < channels.Count; channel++)
        {
            qualities[channel] = RecordedSweepDetector
                .FindSweeps(channels[channel], probe.SweepData, 1)
                .FirstOrDefault().Quality;
        }

        return qualities;
    }

    public static int Best(IReadOnlyList<double> qualities)
    {
        ArgumentNullException.ThrowIfNull(qualities);

        int best = 0;
        for (int channel = 1; channel < qualities.Count; channel++)
        {
            if (qualities[channel] > qualities[best])
            {
                best = channel;
            }
        }

        return best;
    }

    public static bool IsAmbiguous(IReadOnlyList<double> qualities)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        if (qualities.Count < 2)
        {
            return false;
        }

        int best = Best(qualities);
        if (qualities[best] <= 0)
        {
            return false;
        }

        for (int channel = 0; channel < qualities.Count; channel++)
        {
            if (channel != best &&
                qualities[channel] >= qualities[best] * AmbiguousShare)
            {
                return true;
            }
        }

        return false;
    }
}
