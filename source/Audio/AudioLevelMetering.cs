using Resonalyze.Dsp;

namespace Resonalyze;

internal static class AudioLevelMetering
{
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
        double safeSampleCount = Math.Max(sampleCount, 1);
        for (int i = 0; i < peaks.Length; i++)
        {
            double peak = Math.Clamp(peaks[i], 0, 1);
            double rms = Math.Sqrt(Math.Max(sumSquares[i], 0) / safeSampleCount);
            levels[i] = new AudioChannelLevel(
                DataHelper.AmplitudeToDecibels(peak),
                DataHelper.AmplitudeToDecibels(rms),
                peak >= 0.999);
        }

        return levels;
    }
}
