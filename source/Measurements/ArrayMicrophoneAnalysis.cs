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
/// How many of the measurement's averaging runs this microphone contributed. A
/// microphone that failed a run is missing from that run only.
/// </param>
/// <param name="Issues">
/// Why runs were dropped, if any — the text the end-of-measurement notice shows.
/// </param>
internal sealed record ArrayMicrophoneCurve(
    int ChannelOffset,
    bool IsMeasurementMicrophone,
    double[] LevelsDb,
    int AcceptedRuns,
    IReadOnlyList<string> Issues)
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
    /// The steady-state level curve for one microphone's accepted runs.
    /// </summary>
    public static double[] BuildCurve(
        IReadOnlyList<TransferFunctionFrame> frames,
        ExcitationBandGate excitationGate,
        int sampleRate,
        ProtectiveHighPassConfiguration? protectiveHighPass)
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

        TransferMagnitudeEstimate estimate = TransferFunction.ComputeAveragedMagnitude(
            frames,
            excitationGate);
        double[] levels = SpatialAverage.FromTransferMagnitude(
            estimate.Magnitude,
            (double)sampleRate / estimate.FftLength);
        return RemoveProtectiveHighPass(levels, sampleRate, protectiveHighPass);
    }

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
