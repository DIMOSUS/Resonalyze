using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>App-side metering of captured samples; shares only <see cref="AudioChannelLevel"/> with the audio library.</summary>
internal static class RecordedLevelMetering
{
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
