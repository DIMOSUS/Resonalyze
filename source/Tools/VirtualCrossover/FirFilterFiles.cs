using System.Globalization;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The FIR kernel's files, at the session's edge: IMPORT reads a kernel out of the
/// files the filter designers write — a mono (or multichannel, first channel taken)
/// WAV, or a text file with one coefficient per line under whatever name the tool
/// gave it, <c>.fir</c> or <c>.txt</c> — and EXPORT writes the kernel a session
/// carries back out, for the hardware or another tool. The kernel itself lives in
/// the session (see <see cref="VirtualCrossoverChannelSettings.Fir"/>); a file is
/// where it comes from and where it goes, not where it is.
/// </summary>
/// <remarks>
/// The WAV is decoded by <see cref="AudioFileCodec"/>, which is where the app's
/// codecs live (NAudio never reaches the DSP library); the text formats are the
/// library's own <see cref="FirFilterTextFile"/>. Either way the taps are handed on
/// AS THEY ARE, with the rate a WAV states kept only for the block's warning: the
/// processor convolves at its own rate, so a kernel designed for another one is a
/// different filter there — see <see cref="FirFilter"/>. The export writes the taps
/// at the PROCESSOR's rate for the same reason: that is the rate they mean here.
/// </remarks>
internal static class FirFilterFiles
{
    /// <summary>The file dialog filter for the kernels the import accepts.</summary>
    public const string ImportFileDialogFilter =
        "FIR filters (*.wav;*.fir;*.txt)|*.wav;*.fir;*.txt|All files (*.*)|*.*";

    /// <summary>
    /// The export's filter: WAV first, because it is what most processors' tools
    /// load, then the two text spellings.
    /// </summary>
    public const string ExportFileDialogFilter =
        "FIR filter WAV, 32-bit float (*.wav)|*.wav|" +
        "FIR filter text, one coefficient per line (*.txt)|*.txt|" +
        "FIR filter text, one coefficient per line (*.fir)|*.fir";

    // The longest WAV decoded before the tap ceiling is checked: the ceiling at the
    // lowest selectable rate is under three seconds, and a "kernel" running to
    // minutes is program material picked by mistake — refused by the decoder's own
    // duration guard rather than loaded into memory first.
    private static readonly TimeSpan MaximumWavDuration = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Reads the kernel at <paramref name="path"/>. Throws — an
    /// <see cref="InvalidDataException"/> for a file that is not a kernel, the
    /// decoder's or the file system's exception otherwise — so the caller can show
    /// the reason; nothing here guesses.
    /// </summary>
    public static FirFilter Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsWav(path))
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

    /// <summary>
    /// Writes <paramref name="fir"/> to <paramref name="path"/>: a mono 32-bit float
    /// WAV under <c>.wav</c> (float, not 24-bit PCM — a kernel with gain has taps past
    /// ±1, and integer PCM would clip them into another filter), one coefficient per
    /// line under any other name, with a header the import reads back (the rate line
    /// becomes the kernel's declared rate). <paramref name="sampleRateHz"/> is the
    /// rate the taps are stated at — the processor's.
    /// </summary>
    public static void Save(string path, FirFilter fir, int sampleRateHz, string? sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fir);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRateHz);

        if (IsWav(path))
        {
            var samples = new float[fir.Length];
            ReadOnlySpan<double> taps = fir.Taps;
            for (int index = 0; index < samples.Length; index++)
            {
                samples[index] = (float)taps[index];
            }

            AudioFileCodec.WriteWavFloat32(path, new AudioFileContent([samples], sampleRateHz));
            return;
        }

        var text = new StringBuilder();
        text.Append("* FIR filter exported by Resonalyze");
        if (!string.IsNullOrWhiteSpace(sourceName))
        {
            text.Append(" (imported from ").Append(sourceName).Append(')');
        }
        text.Append("\r\n");
        text.Append("* Sample rate: ").Append(sampleRateHz.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        text.Append("* ").Append(fir.Length.ToString(CultureInfo.InvariantCulture))
            .Append(" taps, one coefficient per line, first tap first\r\n");
        foreach (double tap in fir.Taps)
        {
            // "R" round-trips a double exactly; the import reads it back to the bit.
            text.Append(tap.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool IsWav(string path) =>
        string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase);
}
