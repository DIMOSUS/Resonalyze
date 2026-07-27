using System.Text;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Acceptance checks for one captured sweep run, evaluated BEFORE the run is
/// added to the average, so one bad capture can no longer contaminate it
/// irreversibly. Deliberately limited to unambiguous failures (clipping, a
/// dead or far-too-quiet signal, an undersized capture): statistical outlier
/// checks (peak-delay vs median, IR correlation against a reference run) need
/// thresholds calibrated on real multi-run captures and are a later phase.
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
    /// Loopback peak below which the reference is rejected as too quiet
    /// (~-30 dBFS). A wired loopback is an electrical reference and sits
    /// near full scale on any sane gain staging; a "reference" tens of dB
    /// down is in practice not the wire but playback bleed into an open or
    /// mis-routed input, and the transfer function would divide the
    /// microphone response by that noise. Field case: a whole session
    /// captured with loopback peaks at -33..-49 dBFS passed the silence
    /// check and produced garbage transfer IRs on every channel. Sits far
    /// above <see cref="SilentPeakThreshold"/> and far below any real wired
    /// reference, so neither neighbor is ever misclassified.
    /// </summary>
    public const double QuietLoopbackPeakThreshold = 0.0316;

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

        if (loopback != null)
        {
            double loopbackPeak = Peak(loopback);
            if (loopbackPeak < SilentPeakThreshold)
            {
                issues.Add("the loopback reference signal is silent");
            }
            else if (loopbackPeak < QuietLoopbackPeakThreshold)
            {
                double loopbackPeakDb = DataHelper.AmplitudeToDecibels(loopbackPeak);
                issues.Add(FormattableString.Invariant(
                    $"the loopback reference is too quiet to trust (peak {loopbackPeakDb:0.0} dBFS; a wired loopback sits near full scale)"));
            }
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
    public bool IsDegraded => AcceptedRuns < RequestedRuns;

    /// <summary>
    /// User-facing summary for the end-of-measurement notice. A run whose
    /// retry succeeded DID enter the average — its line says so explicitly;
    /// only a run whose retry also failed is reported as excluded.
    /// </summary>
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
