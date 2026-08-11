namespace Resonalyze;

/// <summary>
/// Which channel of a multi-channel recording holds the measurement.
/// <para>
/// Ranked by how well each channel MATCHES the configured sweep rather than by
/// how loud it is: a recorder routinely delivers one live microphone beside a
/// dead input, and a dead input is rarely silent — hum, preamp hiss, a cable
/// picking up the car — so "loudest" can name the channel with no measurement in
/// it.
/// </para>
/// <para>
/// The rank alone cannot finish the job, though, and that is what
/// <see cref="IsAmbiguous"/> is for. A file written by a DAW often carries the
/// played sweep on one track beside the microphone on another, and the played
/// track is a copy of the excitation: it matches perfectly, wins, and then
/// measures as a flat, credible, completely meaningless response. No number
/// separates "the reference track" from "the take" — both are recordings of this
/// sweep — so when more than one channel plausibly holds it, the choice belongs
/// to whoever made the recording.
/// </para>
/// </summary>
internal static class RecordedSweepChannels
{
    /// <summary>
    /// How close the runner-up has to come to the best match before the choice
    /// stops being obvious: a quarter of it, i.e. within 12 dB.
    /// </summary>
    /// <remarks>
    /// Measured as the runner-up's share of the best match, over the cases that
    /// have to fall on either side of it. A channel that really holds a second
    /// recording of the sweep is not marginally behind: a microphone beside the
    /// played reference reads 0.77 and 0.85 of it on two synthetics. A channel
    /// that holds no take at all is an order of magnitude down: 0.031 and 0.032
    /// on the dead channel of the two field files, 0.004 for hum. In absolute
    /// terms the same gap — real takes score 1.00, 0.93, 0.42; hum 0.05, hiss
    /// 0.01, a recording of a DIFFERENT sweep 0.10. The rule sits between two
    /// clusters a factor of ten apart rather than on a knife edge, which is what
    /// makes a threshold defensible here and not for the quality itself.
    /// </remarks>
    public const double AmbiguousShare = 0.25;

    /// <summary>
    /// How well each channel matches the sweep the configuration describes, in
    /// channel order. The score is a normalized correlation, so it judges shape
    /// rather than level and a quiet channel holding the take outranks a loud one
    /// holding hum.
    /// </summary>
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

    /// <summary>The best-matching channel, or 0 when there is nothing to rank.</summary>
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

    /// <summary>
    /// Whether more than one channel plausibly holds the sweep, in which case the
    /// import must not choose on its own.
    /// </summary>
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
