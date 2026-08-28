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

/// <summary>One rejected capture attempt of an averaging run.</summary>
internal sealed record SweepRunRejection(
    int Run,
    bool Retried,
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
    /// one is rejected like any other bad run, so it shows up as a run that did not
    /// enter the average, with the input named among its reasons — the array cannot
    /// quietly end up with fewer positions than the user set up, because a
    /// measurement that would have is not a measurement at all.
    /// <para>
    /// A REJECTION counts even when the retry then succeeded, which it did not before.
    /// <see cref="Describe"/> has always had a line for that case — "first attempt
    /// rejected; the retry was accepted" — and it could never be reached, because
    /// every run had entered the average and the gate above asked only about that. A
    /// channel that clipped once and passed on the second try is exactly the thing a
    /// user wants to hear about while the microphone is still where they put it.
    /// </para>
    /// </remarks>
    public bool IsDegraded => AcceptedRuns < RequestedRuns || Rejections.Count > 0;

    /// <summary>
    /// User-facing summary for the end-of-measurement notice. A run whose
    /// retry succeeded DID enter the average — its line says so explicitly;
    /// only a run whose retry also failed is reported as excluded.
    /// </summary>
    private const string CRLF = "\r\n";

    public string Describe()
    {
        var text = new StringBuilder();
        text.Append(
            $"The averaged measurement used {AcceptedRuns} of the " +
            $"{RequestedRuns} requested sweep runs (a run failing the capture " +
            "quality checks is retried once):");
        foreach (IGrouping<int, SweepRunRejection> run in Rejections.GroupBy(
            rejection => rejection.Run))
        {
            SweepRunRejection? attempt = run.FirstOrDefault(
                rejection => !rejection.Retried);
            SweepRunRejection? retry = run.FirstOrDefault(
                rejection => rejection.Retried);
            text.Append("\r\n");
            text.Append(retry != null
                ? $"Run {run.Key}: excluded from the average (first attempt: " +
                    $"{JoinIssues(attempt)}; retry: {JoinIssues(retry)})"
                : $"Run {run.Key}: first attempt rejected ({JoinIssues(attempt)}); " +
                    "the retry was accepted");
        }

        return text.ToString();
    }

    private static string JoinIssues(SweepRunRejection? rejection) =>
        rejection == null ? "-" : string.Join(", ", rejection.Issues);
}
