namespace Resonalyze.Audio;

internal static class AudioLevelMetering
{
    /// <summary>Peak amplitude at or above which a channel counts as full scale.</summary>
    public const double FullScaleThreshold = 0.999;

    // Inlined amplitude→dBFS: the audio library must not depend on Resonalyze.Dsp.
    // Mirrors DataHelper.AmplitudeToDecibels (floor at 1e-8 → -160 dBFS).
    private const double MinimumAmplitude = 1e-8;

    private static double AmplitudeToDecibels(double amplitude) =>
        20.0 * Math.Log10(Math.Max(amplitude, MinimumAmplitude));

    /// <summary>
    /// The one Peak/RMS/dB summary all metering paths share: from an
    /// accumulated peak, sum of squares and sample count.
    /// </summary>
    public static AudioChannelLevel Measure(double peak, double sumSquares, long sampleCount)
    {
        double rms = Math.Sqrt(Math.Max(sumSquares, 0) / Math.Max(sampleCount, 1));
        return new AudioChannelLevel(
            AmplitudeToDecibels(peak),
            AmplitudeToDecibels(rms),
            peak >= FullScaleThreshold);
    }

    /// <summary>Peak/RMS level of one recorded channel.</summary>
    public static AudioChannelLevel MeasureSamples(IReadOnlyList<float> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        double peak = 0;
        double sumSquares = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            double magnitude = Math.Abs(samples[i]);
            peak = Math.Max(peak, magnitude);
            sumSquares += (double)samples[i] * samples[i];
        }

        return Measure(peak, sumSquares, samples.Count);
    }

    public static AudioChannelLevel[] MeasureChannels(
        double[] peaks,
        double[] sumSquares,
        int sampleCount)
    {
        ArgumentNullException.ThrowIfNull(peaks);
        ArgumentNullException.ThrowIfNull(sumSquares);
        if (peaks.Length != sumSquares.Length)
        {
            throw new ArgumentException("Peak and RMS buffers must have the same length.");
        }

        var levels = new AudioChannelLevel[peaks.Length];
        for (int i = 0; i < peaks.Length; i++)
        {
            levels[i] = Measure(Math.Clamp(peaks[i], 0, 1), sumSquares[i], sampleCount);
        }

        return levels;
    }
}
