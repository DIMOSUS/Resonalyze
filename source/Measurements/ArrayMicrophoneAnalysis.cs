using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One microphone's contribution to a measurement's spatial average.
/// </summary>
/// <param name="ChannelOffset">
/// The hardware input it was recorded from, or the measurement microphone's own
/// channel when <paramref name="IsMeasurementMicrophone"/> is set.
/// </param>
/// <param name="IsMeasurementMicrophone">
/// Whether this is the microphone that also produced the impulse response. It
/// belongs in the array like any other position — it is a microphone in the
/// listening volume, and leaving it out would throw away the one position whose
/// level is tied to the SPL calibration — but it is marked, because it is the
/// anchor everything else is levelled onto and the only one with a gated curve
/// elsewhere in the measurement.
/// </param>
/// <param name="LevelsDb">
/// The steady-state transfer level on <see cref="SpatialAverage.BuildGrid"/>,
/// with the protective high-pass divided back out and WITHOUT any microphone
/// calibration. Raw on purpose: the calibration is stored beside the curve and
/// applied when it is read, so a curve can be recalibrated, and so the view's
/// own calibration switch still means something for these.
/// </param>
/// <param name="AcceptedRuns">
/// How many of the measurement's averaging runs this microphone contributed. Every
/// microphone in one measurement contributes the same number: a position that was
/// compromised fails the RUN rather than dropping out of it, so an array never holds
/// a position that sat part of the average out.
/// </param>
internal sealed record ArrayMicrophoneCurve(
    int ChannelOffset,
    bool IsMeasurementMicrophone,
    double[] LevelsDb,
    int AcceptedRuns)
{
    /// <summary>What the user called this position, if anything.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// The calibration this microphone was recorded through, as a curve, or null
    /// for an uncalibrated one.
    /// </summary>
    /// <remarks>
    /// Stamped on after the measurement rather than used by it: nothing in the
    /// analysis applies a calibration — the stored curve is deliberately raw —
    /// but the file has to carry it, or a measurement is not portable and the
    /// array cannot be levelled correctly on another machine.
    /// </remarks>
    public VirtualCrossoverCalibrationSettings? Calibration { get; init; }
}

/// <summary>
/// What a configured array microphone carries that the measurement itself has no
/// use for: the name the user gave the position and the calibration chosen for
/// it. Matched onto the measured curves by channel once a run completes.
/// </summary>
internal sealed record ArrayMicrophoneMetadata(
    int ChannelOffset,
    string? Note,
    VirtualCrossoverCalibrationSettings? Calibration);

/// <summary>
/// Turns the captured runs of one array microphone into the curve a spatial
/// average is built from.
/// </summary>
/// <remarks>
/// The array microphones are channels of the measurement's own device, so each
/// one has the loopback beside it, sample for sample. That is what makes this an
/// honest transfer function rather than a bare deconvolution of the sweep, and
/// the difference is not academic: a deconvolution is normalized by the digital
/// excitation, so raising the playback level between two channel measurements
/// would lift that channel's curve while its impulse response — normalized by
/// the loopback — did not move. The array would then disagree with the impulse
/// responses about the relative levels of the channels, which is exactly the
/// disagreement a hybrid view cannot survive.
/// <para>
/// Reading it through the SAME estimator as the measurement microphone is the
/// other half of that: same H1, same excitation gate, same regularization, so
/// the measurement microphone's own array curve and the further microphones'
/// differ only by where they stood.
/// </para>
/// </remarks>
internal static class ArrayMicrophoneAnalysis
{
    /// <summary>
    /// The steady-state level curve for the MEASUREMENT microphone's accepted runs.
    /// </summary>
    /// <remarks>
    /// No credibility verdict here, deliberately. These are the measurement's own
    /// frames, and they are judged by <c>RequireCredibleTransferIr</c> once the
    /// average is built — which knows things this cannot: that a quiet loopback means
    /// bleed instead of the wire, and which channel's distortion is the culprit.
    /// Refusing here would preempt that diagnosis with a worse one, and one that
    /// blames an array microphone for the reference's fault.
    /// </remarks>
    public static double[] BuildMeasurementCurve(
        IReadOnlyList<TransferFunctionFrame> frames,
        ExcitationBandGate excitationGate,
        int sampleRate,
        ProtectiveHighPassConfiguration? protectiveHighPass) =>
        BuildCurve(frames, excitationGate, sampleRate, protectiveHighPass, arrayInput: null);

    /// <summary>
    /// The steady-state level curve for one ADDITIONAL array microphone's accepted
    /// runs, refused when it did not divide into a credible response.
    /// </summary>
    /// <param name="channelOffset">
    /// Which input this is, for the refusal to name. A verdict that says "one of the
    /// array microphones" sends the user to unplug seven of them in turn.
    /// </param>
    public static double[] BuildArrayCurve(
        IReadOnlyList<TransferFunctionFrame> frames,
        ExcitationBandGate excitationGate,
        int sampleRate,
        ProtectiveHighPassConfiguration? protectiveHighPass,
        int channelOffset) =>
        BuildCurve(frames, excitationGate, sampleRate, protectiveHighPass, channelOffset);

    private static double[] BuildCurve(
        IReadOnlyList<TransferFunctionFrame> frames,
        ExcitationBandGate excitationGate,
        int sampleRate,
        ProtectiveHighPassConfiguration? protectiveHighPass,
        int? arrayInput)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException(
                "A microphone needs at least one accepted run.",
                nameof(frames));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        // The impulse response is asked for only when something will read it, so the
        // measurement microphone does not pay an inverse transform for a verdict it
        // is not subject to.
        (TransferMagnitudeEstimate estimate, Complex[]? transfer) =
            TransferFunction.ComputeAveragedMagnitudeAndIr(
                frames,
                excitationGate,
                wantImpulseResponse: arrayInput.HasValue);
        if (arrayInput is { } channelOffset)
        {
            RequireCredible(transfer, sampleRate, channelOffset);
        }
        double[] levels = SpatialAverage.FromTransferMagnitude(
            estimate.Magnitude,
            (double)sampleRate / estimate.FftLength);
        return RemoveProtectiveHighPass(levels, sampleRate, protectiveHighPass);
    }

    /// <summary>
    /// Refuses an ARRAY microphone whose transfer response has no shape — the same
    /// verdict, on the same threshold, the measurement microphone's own response is
    /// held to by <c>RequireCredibleTransferIr</c>.
    /// </summary>
    /// <remarks>
    /// The level checks a run applies catch a channel that is silent, clipped or
    /// short. They cannot catch one that is LIVE and wrong: an unused preamp hissing
    /// at -40 dBFS, a microphone plugged into the wrong socket, a capsule that has
    /// failed into noise. Any of those divides into an H1 estimate with no arrival in
    /// it — and then the spatial average trims its median level onto the measurement
    /// microphone's and gives it a full seventh of the weight. A plausible curve, no
    /// exception, and a tune fitted to a channel that measured nothing.
    /// <para>
    /// The threshold is not a new one: <c>MinimumCompactnessDb</c> was calibrated
    /// against an archived set of deliberately bad loopbacks, where a real measurement
    /// reads 29-49 dB and an unusable one divides into noise well below it.
    /// </para>
    /// </remarks>
    private static void RequireCredible(
        Complex[]? transfer,
        int sampleRate,
        int channelOffset)
    {
        if (DescribeIncredibleResponse(transfer, sampleRate) is not { } shape)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The array microphone on input {channelOffset + 1} recorded a signal, but " +
            $"it did not divide into a credible response: {shape}. It is live and " +
            "wrong rather than absent — a hissing preamp, the wrong socket, or a " +
            "failed capsule — and averaging it in would give a channel that measured " +
            "nothing a full share of the result. Check that input, then measure again.");
    }

    /// <summary>
    /// Why this transfer response is not one, or null when it is. The single
    /// definition of the verdict, so the RUN check and the averaged backstop cannot
    /// come to different answers about the same microphone.
    /// </summary>
    public static string? DescribeIncredibleResponse(
        Complex[]? transfer,
        int sampleRate,
        double floorDb = TransferIrDiagnostics.MinimumCompactnessDb)
    {
        // FAIL-CLOSED, like the measurement microphone's: a shape that cannot be
        // measured at all is a refusal, not a pass.
        TransferIrCompactness? compactness = transfer is null
            ? null
            : TransferIrDiagnostics.MeasureCompactness(transfer, sampleRate);
        if (compactness is { } measured &&
            double.IsFinite(measured.InsideOutsideDb) &&
            measured.InsideOutsideDb >= floorDb)
        {
            return null;
        }

        return compactness is { } value && double.IsFinite(value.InsideOutsideDb)
            ? FormattableString.Invariant(
                $"the energy around its peak is only {value.InsideOutsideDb:0.0} dB above the rest of the capture (a real measurement reads 29-49 dB)")
            : "its shape could not be measured at all";
    }

    /// <summary>
    /// The compactness floor one run of an N-run average is held to.
    /// </summary>
    /// <remarks>
    /// LOWER than the averaged floor, and by a derived amount rather than a chosen
    /// one. Averaging N runs leaves the coherent arrival alone and divides the
    /// uncorrelated part of everything else by N, so the average of N runs each
    /// reading R reads somewhere in [R, R + 10·log₁₀N] — exactly R when what surrounds
    /// the arrival is the room's own decay, R + 10·log₁₀N when it is noise. Judging a
    /// single run against the averaged floor would therefore refuse runs whose average
    /// would have passed. Measured on the archived cabins, genuine records read 27.7 dB
    /// and up against a floor of 22 — under 6 dB of margin, which four runs of
    /// averaging can account for on its own. This gives that margin back, and still
    /// leaves the fault it is looking for far below: gated uncorrelated noise reads
    /// about 0 dB.
    /// </remarks>
    public static double RunFloorDb(int averagedRuns) =>
        TransferIrDiagnostics.MinimumCompactnessDb -
        10.0 * Math.Log10(Math.Max(1, averagedRuns));

    /// <summary>
    /// Divides the protective high-pass back out of a level curve.
    /// </summary>
    /// <remarks>
    /// The filter sits in the user's own DSP, between the sound card output and
    /// the loudspeaker, while the loopback is taken ahead of it — so every
    /// microphone in the array carries the filter, and the measurement's transfer
    /// impulse response has already had it removed. Left in, a tweeter's array
    /// curve would sit a whole filter slope under its own impulse response: 24 dB
    /// an octave below a 2 kHz corner. The same model, the same cap and the same
    /// fade as the impulse-response path, because the two have to agree.
    /// </remarks>
    private static double[] RemoveProtectiveHighPass(
        double[] levelsDb,
        int sampleRate,
        ProtectiveHighPassConfiguration? protectiveHighPass)
    {
        if (protectiveHighPass is not { Enabled: true } filter)
        {
            return levelsDb;
        }

        double[] correction = ProtectiveHighPassCompensation.MagnitudeCorrectionDb(
            filter.ToEdge(),
            sampleRate,
            ProtectiveHighPassConfiguration.MaximumCompensationBoostDb,
            SpatialAverage.BuildGrid());
        for (int band = 0; band < levelsDb.Length; band++)
        {
            // NaN where the filter cannot be inverted: below that limit the
            // loudspeaker was given no signal at all, so there is nothing to
            // recover and nothing an equalizer should later try to fill.
            levelsDb[band] = double.IsFinite(levelsDb[band]) && double.IsFinite(correction[band])
                ? levelsDb[band] + correction[band]
                : double.NaN;
        }

        return levelsDb;
    }
}
