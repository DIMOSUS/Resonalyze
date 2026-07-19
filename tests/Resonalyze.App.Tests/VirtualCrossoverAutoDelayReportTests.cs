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
            delayConfidence, delayDetail, gainConfidence, gainDetail);
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
                    delayConfidence: AlignmentConfidence.Low,
                    delayDetail: "vs A L: margin 0.2 dB, wide seed",
                    gainDetail: "kept (mono channel)")
            ],
            stereo: true,
            sceneOffsetMs: 0.27,
            gainsRequested: true);

        Assert.Contains("stereo", report);
        Assert.Contains("Scene offset +0.27 ms", report);
        Assert.Contains("gain tilt +2.0 dB", report);
        Assert.Contains("0.00 -> 1.25", report);
        Assert.Contains("-3.0 -> -4.5", report);
        Assert.Contains("-2.0 (kept)", report);
        Assert.Contains("norm -> inv", report);
        Assert.Contains("high", report);
        Assert.Contains("LOW", report);
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
            [Outcome("A", 0.0, 0.75, delayDetail: "reference (others align to it)")],
            stereo: false,
            sceneOffsetMs: 0,
            gainsRequested: false);

        Assert.Contains("single side", report);
        Assert.DoesNotContain("Scene offset", report);
        Assert.Contains("Gains not adjusted", report);
        Assert.Contains("0.0 (kept)", report);
    }
}
