using System.Globalization;

namespace Resonalyze;

/// <summary>Lays a mono sweep out for WAV, carrying the playback routing so the file excites the same speaker.</summary>
internal static class SweepWavExport
{
    /// <summary>External players lose the opening to unmute/ramp/crossfade; the tail holds the room decay.</summary>
    public const double SilenceSeconds = 1.0;

    public static AudioFileContent BuildContent(
        float[] monoSamples,
        int sampleRate,
        PlaybackChannel playbackChannel)
    {
        ArgumentNullException.ThrowIfNull(monoSamples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        if (monoSamples.Length == 0)
        {
            throw new ArgumentException("There is no sweep to write.", nameof(monoSamples));
        }

        int silence = (int)Math.Round(SilenceSeconds * sampleRate);
        var excitation = new float[silence + monoSamples.Length + silence];
        monoSamples.CopyTo(excitation, silence);

        float[][] channels = playbackChannel switch
        {
            PlaybackChannel.Left => [excitation, new float[excitation.Length]],
            PlaybackChannel.Right => [new float[excitation.Length], excitation],
            PlaybackChannel.Stereo => [excitation, excitation],
            _ => [excitation]
        };
        return new AudioFileContent(channels, sampleRate);
    }

    public static string SuggestFileName(
        double lowFrequencyHz,
        double highFrequencyHz,
        double durationSeconds,
        int sampleRate) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"sweep_{lowFrequencyHz:0}-{highFrequencyHz:0}Hz_" +
                $"{sampleRate / 1000.0:0.###}kHz_{durationSeconds:0.0}s.wav");
}
