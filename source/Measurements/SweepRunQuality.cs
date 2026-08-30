using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Acceptance checks for one captured sweep run, evaluated BEFORE the run is
/// added to the average, so one bad capture cannot contaminate it irreversibly.
/// Deliberately limited to unambiguous failures (clipping, a
/// dead signal, an undersized capture): statistical outlier checks
/// (peak-delay vs median, IR correlation against a reference run) need
/// thresholds calibrated on real multi-run captures and are a later phase.
/// A quiet-but-present loopback is deliberately NOT a failure here: transfer
/// estimation is scale-invariant, so a cleanly attenuated wire (the readme
/// itself says to turn the playback level well down) measures fine — whether
/// a reference was usable is judged by the transfer IR's SHAPE after the
/// runs (see ExpSweepMeasurement.RequireCredibleTransferIr).
/// </summary>
internal static class SweepRunQualityCheck
{
    /// <summary>
    /// Peak amplitude below which a channel counts as carrying no signal at
    /// all (~-80 dBFS): an unplugged cable, a wrong channel or a dead device.
    /// Far below any usable capture level, so a quiet-but-working signal is
    /// never rejected.
    /// </summary>
    public const double SilentPeakThreshold = 1e-4;

    /// <summary>
    /// Issues found in the captured run; empty means the run is accepted.
    /// Judges the ENTIRE capture — both recorders reset per run, and the
    /// whole snapshot (including the pre-playback roll) feeds the
    /// deconvolution and transfer analysis, so the checked range and the
    /// analyzed range must match. A full-scale loopback is NOT flagged: by
    /// the metering convention the loopback is the reference and routinely
    /// sits at full scale.
    /// </summary>
    public static IReadOnlyList<string> Assess(
        float[] microphone,
        float[]? loopback,
        int expectedSweepSamples)
    {
        ArgumentNullException.ThrowIfNull(microphone);

        var issues = new List<string>();
        if (microphone.Length < expectedSweepSamples)
        {
            issues.Add(
                $"the capture is shorter than the sweep " +
                $"({microphone.Length} of {expectedSweepSamples} samples)");
        }

        double microphonePeak = Peak(microphone);
        if (microphonePeak >= RecordedLevelMetering.FullScaleThreshold)
        {
            issues.Add("the microphone signal clipped");
        }
        else if (microphonePeak < SilentPeakThreshold)
        {
            issues.Add("the microphone signal is silent");
        }

        if (loopback != null && Peak(loopback) < SilentPeakThreshold)
        {
            issues.Add("the loopback reference signal is silent");
        }

        return issues;
    }

    /// <summary>
    /// The same checks for one ARRAY microphone: clipping, silence and a short
    /// capture, without the loopback — that is judged once for the run, not once
    /// per microphone.
    /// </summary>
    /// <remarks>
    /// A failure here REJECTS THE RUN, the same as one on the measurement
    /// microphone: the caller folds what this returns into the run's own issues.
    /// This used to drop the offending microphone from that run and keep the rest,
    /// which bought a measurement that looks complete and is not — the array keeps
    /// only the curve each position produced, so a position that lost its runs is
    /// simply absent, and an average of six positions where seven were set up is a
    /// different measurement wearing the same name. A sweep is cheap; a spatial
    /// average built over a listening volume the user did not choose is not.
    /// </remarks>
    public static IReadOnlyList<string> AssessArrayMicrophone(
        float[] samples,
        int expectedSweepSamples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var issues = new List<string>();
        if (samples.Length < expectedSweepSamples)
        {
            issues.Add(
                $"the capture is shorter than the sweep " +
                $"({samples.Length} of {expectedSweepSamples} samples)");
        }

        double peak = Peak(samples);
        if (peak >= RecordedLevelMetering.FullScaleThreshold)
        {
            issues.Add("the signal clipped");
        }
        else if (peak < SilentPeakThreshold)
        {
            issues.Add("the signal is silent");
        }

        return issues;
    }

    private static double Peak(float[] samples)
    {
        double peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }

        return peak;
    }
}

/// <summary>The rejected run that stopped an averaged measurement.</summary>
internal sealed record SweepRunRejection(
    int Run,
    IReadOnlyList<string> Issues);

/// <summary>
/// A published measurement that cleared the refusals and should not have been left
/// to speak for itself.
/// </summary>
/// <remarks>
/// The pre-arrival refusal is calibrated on the garbage side of its gap, so a real
/// capture is never thrown away for it. That leaves the band between
/// <see cref="TransferIrDiagnostics.SuspectPreArrivalDb"/> and
/// <see cref="TransferIrDiagnostics.MaximumPreArrivalDb"/>, where a reference is
/// starting to cancel itself but the result is not yet garbage — and where a
/// measurement used to be saved without a word. The field session behind this one
/// spent an evening on eleven takes whose reference ran through an interface's
/// direct mixer; the two worst read -14.8 and -14.1 dB and are refused outright,
/// but the milder ones from the same rig are exactly what this band is for.
/// <para>
/// A notice rather than a refusal, because the cost is asymmetric: at this depth
/// the record is still usable outside the affected band, and the user is the one
/// who knows whether the channel is worth re-measuring. It also catches the
/// readings the refusal deliberately hands back — a window holding one discrete
/// event rather than a ring (see
/// <see cref="TransferIrDiagnostics.PreArrivalCrestDb"/>), which is a different
/// fault and gets a different sentence.
/// </para>
/// </remarks>
internal sealed record SweepResultCaution(double PreArrivalDb, double CrestDb)
{
    /// <summary>User-facing summary for the end-of-measurement notice.</summary>
    public string Describe() =>
        FormattableString.Invariant(
            $"The measurement was saved, but it carries energy well before its arrival: the stretch from {TransferIrDiagnostics.PreArrivalStartSeconds * 1000:0} to {TransferIrDiagnostics.PreArrivalEndSeconds * 1000:0} ms AHEAD of the peak reads {PreArrivalDb:0.0} dB against the arrival itself, where a clean field record reads -39 dB or less.\r\n\r\n") +
        // The two shapes that reading comes in need different sentences: sending a
        // user to check wiring that is correct is the failure the distortion
        // diagnosis was written to end, and it would be repeated here.
        (CrestDb >= TransferIrDiagnostics.PreArrivalCrestDb
            ? "That energy is one discrete event rather than a ring, which is what " +
                "a record whose strongest sample is NOT its direct sound looks " +
                "like: an obstructed or badly aimed driver, where a later " +
                "reflection outweighs the arrival. The reference is probably fine. " +
                "Check what the microphone was pointed at, and read the arrival " +
                "time on this record with that in mind."
            : "Nothing physical arrives before the direct sound, and a room cannot " +
                "ring backwards, so this is not the cabin — it is what the " +
                "microphone was divided BY. Check that the loopback carries the " +
                "excitation itself: a wire from the output, not an interface " +
                "direct-mixer or monitor path with effects, sends or faders in it. " +
                "The result is still usable away from the affected frequencies, " +
                "but compare it against a channel that measures cleanly before you " +
                "tune on this one.");
}

/// <summary>
/// Outcome of the per-run acceptance over a whole averaged measurement.
/// </summary>
internal sealed record SweepRunQualityReport(
    int RequestedRuns,
    int AcceptedRuns,
    IReadOnlyList<SweepRunRejection> Rejections)
{
    /// <summary>
    /// Whether the end-of-measurement notice has anything to say.
    /// </summary>
    /// <remarks>
    /// An array microphone needs no clause of its own here. A run that compromised
    /// one stops the measurement like any other bad run, with the input named among
    /// its reasons — the array cannot quietly end up with fewer positions than the
    /// user set up, because a measurement that would have is not a measurement at all.
    /// <para>
    /// There is no retry to report. One used to run automatically, and the field
    /// answer is that it never recovered anything: what these checks catch is a gain
    /// set wrong, a cable in the wrong socket, a channel that is not there —
    /// configuration, which the next sweep reproduces exactly. Sweeping again to prove
    /// it costs the user their time twice over.
    /// </para>
    /// </remarks>
    public bool IsDegraded => AcceptedRuns < RequestedRuns || Rejections.Count > 0;

    /// <summary>
    /// User-facing summary for the end-of-measurement notice.
    /// </summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.Append(
            $"The averaged measurement used {AcceptedRuns} of the " +
            $"{RequestedRuns} requested sweep runs:");
        foreach (SweepRunRejection rejection in Rejections)
        {
            text.Append("\r\n");
            text.Append(
                $"Run {rejection.Run}: stopped the measurement " +
                $"({string.Join(", ", rejection.Issues)})");
        }

        return text.ToString();
    }
}
