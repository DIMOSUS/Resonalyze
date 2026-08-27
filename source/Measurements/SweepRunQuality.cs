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
    /// A failure here does NOT reject the run. The array is a passenger: the
    /// measurement microphone, the loopback and the impulse response are all
    /// unaffected by a clipped microphone three seats away, and throwing the run
    /// out would let one badly placed array microphone destroy a measurement that
    /// is otherwise perfect. The microphone drops out of that run instead.
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
/// <summary>
/// How one array microphone fared across the measurement's runs.
/// </summary>
internal sealed record SweepArrayMicrophoneOutcome(
    int ChannelOffset,
    int AcceptedRuns,
    IReadOnlyList<string> Issues);

internal sealed record SweepRunQualityReport(
    int RequestedRuns,
    int AcceptedRuns,
    IReadOnlyList<SweepRunRejection> Rejections)
{
    /// <summary>
    /// The array microphones' outcomes, empty when no array was recorded. Only
    /// microphones that lost at least one run appear.
    /// </summary>
    public IReadOnlyList<SweepArrayMicrophoneOutcome> ArrayMicrophones { get; init; } = [];

    /// <summary>
    /// Whether the end-of-measurement notice has anything to say. An array
    /// microphone that lost runs counts: its curve is the only thing kept of it,
    /// so "one microphone measured nothing" has to be said out loud or the
    /// spatial average silently averages fewer positions than the user set up.
    /// </summary>
    public bool IsDegraded => AcceptedRuns < RequestedRuns || ArrayMicrophones.Count > 0;

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

        foreach (SweepArrayMicrophoneOutcome microphone in ArrayMicrophones)
        {
            text.Append(CRLF);
            text.Append(microphone.AcceptedRuns == 0
                ? $"Array microphone on input {microphone.ChannelOffset + 1}: left out of " +
                    $"the spatial average ({string.Join(", ", microphone.Issues)})"
                : $"Array microphone on input {microphone.ChannelOffset + 1}: used " +
                    $"{microphone.AcceptedRuns} of {AcceptedRuns} runs " +
                    $"({string.Join(", ", microphone.Issues)})");
        }

        return text.ToString();
    }

    private static string JoinIssues(SweepRunRejection? rejection) =>
        rejection == null ? "-" : string.Join(", ", rejection.Issues);
}
