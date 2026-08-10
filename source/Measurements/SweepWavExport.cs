using System.Globalization;

namespace Resonalyze;

/// <summary>
/// Lays a generated sweep out for a WAV file. The sweep itself is mono; the
/// playback channel is what decides which output it reaches, so an exported
/// file has to carry that routing or playing it back would excite a different
/// speaker than the measurement does.
/// </summary>
internal static class SweepWavExport
{
    /// <summary>
    /// Silence written before and after the sweep. The file is played by
    /// something that is not Resonalyze, and a sweep starting at the first sample
    /// loses its opening to whatever the chain does when audio begins: a
    /// Bluetooth link or a class-D amplifier coming out of mute, a phone ramping
    /// its output, a player crossfading the previous track. The trailing second
    /// gives the room its decay before the file ends.
    /// </summary>
    public const double SilenceSeconds = 1.0;

    /// <summary>
    /// The file's channels for <paramref name="playbackChannel"/>: a single
    /// channel for <see cref="PlaybackChannel.Mono"/> — which every player feeds
    /// to both outputs, exactly as the mono routing does during a measurement —
    /// and two channels otherwise, with the unused side silent. The sweep sits
    /// between <see cref="SilenceSeconds"/> of silence on either side.
    /// </summary>
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

        // Stereo hands the same buffer out twice; the writer only reads it.
        float[][] channels = playbackChannel switch
        {
            PlaybackChannel.Left => [excitation, new float[excitation.Length]],
            PlaybackChannel.Right => [new float[excitation.Length], excitation],
            PlaybackChannel.Stereo => [excitation, excitation],
            _ => [excitation]
        };
        return new AudioFileContent(channels, sampleRate);
    }

    /// <summary>
    /// The default file name offered for a sweep, carrying the settings that
    /// produced it so two exports never look alike. Invariant-formatted: a
    /// decimal comma in a file name is legal but reads as a mistake.
    /// </summary>
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
