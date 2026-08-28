using System.Text;

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
