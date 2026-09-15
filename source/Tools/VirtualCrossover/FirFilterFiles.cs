using System.Globalization;
using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>FIR kernel import (WAV, .fir, .txt) and export; taps pass as-is, a WAV's rate is kept only for the warning.</summary>
internal static class FirFilterFiles
{
    public const string ImportFileDialogFilter =
        "FIR filters (*.wav;*.fir;*.txt)|*.wav;*.fir;*.txt|All files (*.*)|*.*";

    public const string ExportFileDialogFilter =
        "FIR filter WAV, 32-bit float (*.wav)|*.wav|" +
        "FIR filter text, one coefficient per line (*.txt)|*.txt|" +
        "FIR filter text, one coefficient per line (*.fir)|*.fir";

    // Program material picked by mistake is refused by the decoder's duration guard before loading (tap ceiling < 3 s).
    private static readonly TimeSpan MaximumWavDuration = TimeSpan.FromSeconds(10);

    /// <summary>Throws <see cref="InvalidDataException"/> for a non-kernel file, or the decoder's/file system's exception.</summary>
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

    /// <summary>32-bit float WAV under <c>.wav</c> (integer PCM would clip taps past ±1), text otherwise; <paramref name="sampleRateHz"/> is the processor's.</summary>
    public static void Save(
        string path,
        FirFilter fir,
        int sampleRateHz,
        string? sourceName,
        string? designDescription = null)
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
        if (!string.IsNullOrWhiteSpace(designDescription))
        {
            // A comment line (the import skips it): the design's only record once the file leaves the session.
            text.Append("* ").Append(designDescription).Append("\r\n");
        }
        text.Append("* Sample rate: ").Append(sampleRateHz.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        text.Append("* ").Append(fir.Length.ToString(CultureInfo.InvariantCulture))
            .Append(" taps, one coefficient per line, first tap first\r\n");
        foreach (double tap in fir.Taps)
        {
            // "R" round-trips a double exactly.
            text.Append(tap.ToString("R", CultureInfo.InvariantCulture)).Append("\r\n");
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool IsWav(string path) =>
        string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase);
}
