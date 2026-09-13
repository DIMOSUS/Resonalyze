using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Reads a FIR kernel out of the files the filter designers write: a mono (or
/// multichannel, first channel taken) WAV, or a text file with one coefficient per
/// line under whatever name the tool gave it — <c>.fir</c> or <c>.txt</c>.
/// </summary>
/// <remarks>
/// The WAV is decoded by <see cref="AudioFileCodec"/>, which is where the app's
/// codecs live (NAudio never reaches the DSP library); the text formats are the
/// library's own <see cref="FirFilterTextFile"/>. Either way the taps are handed on
/// AS THEY ARE, with the rate a WAV states kept only for the block's warning: the
/// processor convolves at its own rate, so a kernel designed for another one is a
/// different filter there — see <see cref="FirFilter"/>.
/// </remarks>
internal static class FirFilterFiles
{
    /// <summary>The file dialog filter for the kernels this reader accepts.</summary>
    public const string FileDialogFilter =
        "FIR filters (*.wav;*.fir;*.txt)|*.wav;*.fir;*.txt|All files (*.*)|*.*";

    // The longest WAV decoded before the tap ceiling is checked: the ceiling at the
    // lowest selectable rate is under three seconds, and a "kernel" running to
    // minutes is program material picked by mistake — refused by the decoder's own
    // duration guard rather than loaded into memory first.
    private static readonly TimeSpan MaximumWavDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Loads the kernel at <paramref name="path"/>. Throws — an
    /// <see cref="InvalidDataException"/> for a file that is not a kernel, the
    /// decoder's or the file system's exception otherwise — so the caller can show
    /// the reason; nothing here guesses.
    /// </summary>
    public static FirFilter Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return FirFilterTextFile.Parse(File.ReadAllText(path));
        }

        AudioFileContent content = AudioFileCodec.Read(path, MaximumWavDuration, channelLimit: 1);
        if (content.ChannelCount == 0 || content.FrameCount == 0)
        {
            throw new InvalidDataException("The WAV file holds no samples.");
        }
        if (content.FrameCount > FirFilter.MaximumTaps)
        {
            throw new InvalidDataException(
                $"The WAV file holds {content.FrameCount} samples, more than the " +
                $"{FirFilter.MaximumTaps} taps this simulation accepts as a FIR filter.");
        }

        float[] samples = content.Channels[0];
        var taps = new double[samples.Length];
        for (int index = 0; index < samples.Length; index++)
        {
            taps[index] = samples[index];
        }

        return new FirFilter(taps, content.SampleRate);
    }
}
