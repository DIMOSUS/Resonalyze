using System.Buffers.Binary;

namespace Resonalyze.Integration.Rew;

/// <summary>REW IR import body from a transfer IR, and REW's samples read back (pure). The buffer is rolled by whole samples with a negative
/// startTime so the acausal tail shows before t = 0; REW carries fractional start times exactly, but this export has no fraction to state.</summary>
internal static class RewImpulseResponsePayload
{
    /// <summary>REW clamps a left gate to the pre-roll present; its own sweeps use 125 ms, so 0.1 s gave 100.4 ms and a 0.18 dB rms mismatch
    /// (REW 5.40 b132). 0.15 s leaves 25 ms margin; with equal gates the round trip agrees to 1.2e-5 dB rms.</summary>
    public const double PreRollSeconds = 0.15;

    /// <param name="impulseResponse">Transfer IR, sample 0 being the reference.</param>
    /// <param name="splOffsetDb">dBr to dB SPL offset, or null without an SPL anchor.</param>
    public static RewImpulseResponseImport Build(
        ReadOnlySpan<double> impulseResponse,
        int peakIndex,
        int sampleRate,
        string identifier,
        double? splOffsetDb)
    {
        if (impulseResponse.Length == 0)
        {
            throw new ArgumentException(
                "The impulse response is empty.",
                nameof(impulseResponse));
        }
        if ((uint)peakIndex >= (uint)impulseResponse.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(peakIndex));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        if (splOffsetDb is { } offset && !double.IsFinite(offset))
        {
            throw new ArgumentOutOfRangeException(nameof(splOffsetDb));
        }

        int length = impulseResponse.Length;
        int preRoll = Math.Min(
            (int)Math.Round(PreRollSeconds * sampleRate),
            length / 4);
        preRoll = Math.Max(preRoll, 0);

        var bytes = new byte[length * sizeof(float)];
        for (int i = 0; i < length; i++)
        {
            double value = impulseResponse[(i + length - preRoll) % length];
            BinaryPrimitives.WriteSingleBigEndian(
                bytes.AsSpan(i * sizeof(float)),
                (float)value);
        }

        double startTime = -preRoll / (double)sampleRate;
        int framedPeakIndex = (peakIndex + preRoll) % length;

        var body = new RewImpulseResponseData
        {
            Identifier = identifier,
            StartTime = startTime,
            SampleRate = sampleRate,
            SplOffset = splOffsetDb,
            ApplyCal = false,
            Data = Convert.ToBase64String(bytes)
        };

        return new RewImpulseResponseImport(
            body,
            preRoll,
            startTime + (framedPeakIndex / (double)sampleRate));
    }

    /// <summary>REW's sample encoding read back: base64 of big-endian float32.</summary>
    /// <exception cref="FormatException">Not base64, or not a whole number of samples.</exception>
    public static double[] DecodeSamples(string base64)
    {
        ArgumentNullException.ThrowIfNull(base64);
        byte[] bytes = Convert.FromBase64String(base64);
        if (bytes.Length % sizeof(float) != 0)
        {
            throw new FormatException(
                $"the sample data is {bytes.Length} bytes, which is not a whole number of 32-bit samples");
        }

        var samples = new double[bytes.Length / sizeof(float)];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(i * sizeof(float)));
        }

        return samples;
    }
}
