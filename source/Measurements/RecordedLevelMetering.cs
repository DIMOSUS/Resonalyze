using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Peak/RMS/dBFS metering of captured samples for the measurement layer's final
/// input-level snapshot. Kept application-side (using the DSP dB helper) so the
/// audio library owns only its own live metering; the two never share a type
/// except the neutral <see cref="AudioChannelLevel"/> result.
/// </summary>
internal static class RecordedLevelMetering
{
    /// <summary>Peak amplitude at or above which a channel counts as full scale.</summary>
    public const double FullScaleThreshold = 0.999;

    public static AudioChannelLevel Measure(double peak, double sumSquares, long sampleCount)
    {
        double rms = Math.Sqrt(Math.Max(sumSquares, 0) / Math.Max(sampleCount, 1));
        return new AudioChannelLevel(
            DataHelper.AmplitudeToDecibels(peak),
            DataHelper.AmplitudeToDecibels(rms),
            peak >= FullScaleThreshold);
    }

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

    public static AudioChannelLevel[] MeasureChannels(float[][] sampleChannels)
    {
        ArgumentNullException.ThrowIfNull(sampleChannels);
        var levels = new AudioChannelLevel[sampleChannels.Length];
        for (int channel = 0; channel < sampleChannels.Length; channel++)
        {
            levels[channel] = MeasureSamples(sampleChannels[channel]);
        }

        return levels;
    }
}
