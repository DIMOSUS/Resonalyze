using System.Buffers.Binary;

namespace Resonalyze.Integration.Rew;

/// <summary>
/// Builds the body of a REW impulse-response import from a measurement's transfer
/// IR. Pure: no HTTP, no WinForms, no measurement types — samples in, payload out,
/// which is what makes the framing and the encoding testable without REW.
/// </summary>
/// <remarks>
/// <para><b>Framing.</b> A loopback-referenced transfer IR has the reference at
/// sample 0, so sending it as it stands means <c>startTime = 0</c> and no pre-roll
/// — correct, but REW draws nothing before t = 0 and the deconvolution's acausal
/// part is wrapped into the buffer's tail, far to the right of where it belongs.
/// The buffer is therefore rolled by whole samples and the shift stated as a
/// negative start time. A circular roll moves no energy and loses no sample, and
/// t = 0 still lands exactly on the loopback reference.</para>
/// <para><b>Fractions.</b> None are needed here. REW carries a fractional start
/// time exactly (measured on 5.40 Beta 132 / API 0.9.6: a half-sample and a
/// third-of-a-sample offset both came back bit for bit, with the peak still on its
/// original index), so a fraction would live in <c>startTime</c> rather than in a
/// resampled buffer. This export has no fraction to state — its reference is a
/// sample of its own recording — so the roll stays whole and the payload stays the
/// samples that were measured.</para>
/// </remarks>
internal static class RewImpulseResponsePayload
{
    /// <summary>
    /// How much of the wrapped tail to bring round to the front, in seconds. Long
    /// enough that the acausal ringing of a deconvolution is visible where it
    /// belongs, short enough to stay a margin rather than half the graph — and, the
    /// binding constraint, long enough for REW's own left gate.
    /// </summary>
    /// <remarks>
    /// The pre-roll is what REW has to build a left gate out of, and REW CLAMPS a
    /// gate to the pre-roll present rather than reporting that it could not apply
    /// the one it was asked for. Measured on 5.40 Beta 132 / API 0.9.6: REW gives an
    /// IMPORT a 100 ms left window by default, while the swept measurements it makes
    /// itself carry 125 ms. At 0.1 s — the value this started with — an imported
    /// measurement therefore could not be given the same gate as the REW measurement
    /// beside it: asking for 125 ms yielded 100.4. Comparing the two then compares
    /// REW's windowing as much as the responses, and over a nine-measurement
    /// crossover bench that showed up as 0.18 dB rms and 0.94 degrees rms. With the
    /// gates equal the same round trip agrees to 1.2e-5 dB rms and 7.7e-5 degrees
    /// rms — the same response to the precision a float32 payload can carry. 0.15 s
    /// leaves 25 ms above the 125 ms an imported measurement has to be able to
    /// match.
    /// </remarks>
    public const double PreRollSeconds = 0.15;

    /// <summary>
    /// Builds the import for one measurement.
    /// </summary>
    /// <param name="impulseResponse">The transfer IR, sample 0 being the reference.</param>
    /// <param name="peakIndex">The arrival's index in that buffer.</param>
    /// <param name="sampleRate">The rate the measurement was made at.</param>
    /// <param name="identifier">The name REW files it under.</param>
    /// <param name="splOffsetDb">
    /// The measurement's own dBr → dB SPL offset, or null when it has no SPL anchor.
    /// </param>
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
            // Read the source through the roll rather than materializing a rolled
            // copy: the destination index is the one that has to advance evenly.
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
}
