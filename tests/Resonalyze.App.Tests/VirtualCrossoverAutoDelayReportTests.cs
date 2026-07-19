using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The Auto delay dialog's report formatter: one before/after row per
/// channel, gains marked kept when they were not adjusted, the confidence
/// columns, and the per-channel notes. Text-shaping only — the values come in
/// pre-computed.
/// </summary>
public sealed class VirtualCrossoverAutoDelayReportTests
{
    private static AutoDelayChannelOutcome Outcome(
        string name,
        double beforeDelay,
        double afterDelay,
        bool beforeInvert = false,
        bool afterInvert = false,
        double beforeGain = 0,
        double afterGain = 0,
        bool gainAdjusted = false,
        AlignmentDecisionKind? delayKind = null,
        AlignmentConfidence? delayConfidence = null,
        string delayDetail = "",
        AlignmentConfidence? gainConfidence = null,
        string gainDetail = "")
    {
        var runtime = new VirtualCrossoverChannel(name);
        return new AutoDelayChannelOutcome(
            runtime, runtime.Settings, name,
            beforeDelay, beforeInvert, beforeGain,
            afterDelay, afterInvert, afterGain, gainAdjusted,
            delayKind, delayConfidence, delayDetail, gainConfidence, gainDetail);
    }

    [Fact]
    public void Format_ShowsBeforeAfterAndConfidence()
    {
        string report = VirtualCrossoverAutoDelayReport.Format(
            [
                Outcome(
                    "A L", 0.0, 1.25,
                    beforeGain: -3.0, afterGain: -4.5, gainAdjusted: true,
                    delayConfidence: AlignmentConfidence.High,
                    delayDetail: "vs B L: margin 2.1 dB",
                    gainConfidence: AlignmentConfidence.Medium,
                    gainDetail: "L-R spread 2.4 dB"),
                Outcome(
                    "B L", 0.5, 0.85, afterInvert: true,
                    beforeGain: -2.0, afterGain: -2.0,
                    delayKind: AlignmentDecisionKind.Search,
                    delayConfidence: AlignmentConfidence.Low,
                    delayDetail: "vs A L: margin 0.2 dB, wide seed",
                    gainDetail: "kept (mono channel)"),
                Outcome(
                    "C R", 1.0, 1.2,
                    delayKind: AlignmentDecisionKind.Locked)
            ],
            stereo: true,
            sceneOffsetMs: 0.27,
            gainsRequested: true,
            leftSumLoss: new AutoDelaySumLossForecast(-2.0, -0.6),
            rightSumLoss: new AutoDelaySumLossForecast(-2.4, -0.8));

        Assert.Contains("stereo", report);
        Assert.Contains("Scene offset +0.27 ms", report);
        Assert.Contains("gain tilt +2.0 dB", report);
        // The at-a-glance summary: change counts, the predicted sum-loss
        // improvement per side, and one warning line per LOW-confidence call.
        Assert.Contains("Changes: 3 delays, 1 polarities, 1 gains", report);
        Assert.Contains("Left   -2.0 -> -0.6 dB", report);
        Assert.Contains("Right  -2.4 -> -0.8 dB", report);
        Assert.Contains(
            "Warning: B L delay has LOW confidence " +
            "(vs A L: margin 0.2 dB, wide seed)", report);
        Assert.Contains("0.00 -> 1.25", report);
        Assert.Contains("-3.0 -> -4.5", report);
        Assert.Contains("-2.0 (kept)", report);
        Assert.Contains("norm -> inv", report);
        Assert.Contains("high", report);
        Assert.Contains("LOW", report);
        // A locked pick is a constraint of the task, not a measurement vote:
        // its row reads "locked" instead of a confidence, and it raises no
        // LOW warning even without a confidence figure.
        string lockedRow = report.Split('\n')
            .First(line => line.StartsWith("C R", StringComparison.Ordinal));
        Assert.Contains("locked", lockedRow);
        Assert.DoesNotContain("Warning: C R", report);
        // The notes wrap each channel into short indented lines, so the
        // dialog's word-wrapping report box never needs a horizontal scroll.
        Assert.Contains("  B L\r\n", report);
        Assert.Contains("    delay: vs A L: margin 0.2 dB, wide seed", report);
        Assert.Contains("    gain:  kept (mono channel)", report);
    }

    [Fact]
    public void Format_SingleSideWithoutGains()
    {
        string report = VirtualCrossoverAutoDelayReport.Format(
            [Outcome(
                "A", 0.0, 0.75,
                delayKind: AlignmentDecisionKind.Reference,
                delayDetail: "reference (others align to it)")],
            stereo: false,
            sceneOffsetMs: 0,
            gainsRequested: false,
            leftSumLoss: new AutoDelaySumLossForecast(-1.5, -0.3));

        Assert.Contains("single side", report);
        Assert.DoesNotContain("Scene offset", report);
        Assert.Contains("Gains not adjusted", report);
        Assert.Contains("Changes: 1 delays, 0 polarities, 0 gains", report);
        Assert.Contains(
            "Predicted sum loss: -1.5 -> -0.3 dB (avg over the crossover window)",
            report);
        Assert.DoesNotContain("Warning:", report);
        Assert.Contains("0.0 (kept)", report);
        // The reference was not chosen at all — its row reads "ref", not a
        // confidence.
        string referenceRow = report.Split('\n')
            .First(line => line.StartsWith("A ", StringComparison.Ordinal));
        Assert.Contains("ref", referenceRow);
    }
}
