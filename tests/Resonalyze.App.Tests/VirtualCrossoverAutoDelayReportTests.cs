using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The Auto delay dialog's report formatter: one row per channel, values the
/// proposal changes written "before -> after" and everything else
/// "value (kept)", the confidence columns, and the per-channel notes.
/// Text-shaping only — the values come in pre-computed.
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

    private static string Row(string report, string name) =>
        report.Split('\n')
            .First(line => line.StartsWith(name, StringComparison.Ordinal));

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
            new AutoDelayRunRequest(
                SceneOffsetMs: 0.27,
                RightHandDrive: false,
                AdjustGains: true,
                NearSideCutDb: 1.5),
            leftSumLoss: new AutoDelaySumLossForecast(-2.0, -0.6),
            rightSumLoss: new AutoDelaySumLossForecast(-2.4, -0.8));

        Assert.Contains("stereo", report);
        // Both figures are layout-neutral magnitudes; the layout names the
        // sides they act on.
        Assert.Contains("Scene offset 0.27 ms (LHD: right side leads)", report);
        Assert.Contains("near-side cut 1.5 dB", report);
        // The at-a-glance summary NAMES the channels each kind of change
        // lands on, so the table only has to be read for the values.
        Assert.Contains(
            "Changes: 3 delays (A L, B L, C R), 1 polarity (B L), 1 gain (A L)",
            report);
        // The forecast states what the proposal buys instead of leaving two
        // similar numbers to be subtracted by eye.
        Assert.Contains("Left   -2.0 -> -0.6 dB (1.4 dB better)", report);
        Assert.Contains("Right  -2.4 -> -0.8 dB (1.6 dB better)", report);
        // One warning line per kind, naming the channels; the reason for each
        // stays in the notes rather than being printed twice.
        Assert.Contains(
            "Warning: LOW delay confidence — B L (reasons in Notes)", report);
        Assert.DoesNotContain(
            "Warning: LOW delay confidence — B L (vs A L", report);
        Assert.Contains("0.00 -> 1.25", report);
        Assert.Contains("-3.0 -> -4.5", report);
        Assert.Contains("-2.0 (kept)", report);
        Assert.Contains("norm -> inv", report);
        Assert.Contains("high", report);
        Assert.Contains("LOW", report);
        // A locked pick is a constraint of the task, not a measurement vote:
        // its row reads "locked" instead of a confidence, and it raises no
        // LOW warning even without a confidence figure.
        Assert.Contains("locked", Row(report, "C R"));
        Assert.DoesNotContain("Warning: LOW delay confidence — C R", report);
        // The gain confidence column is shown because the balance scored a
        // channel here.
        Assert.Contains("Gain conf", report);
        Assert.Contains("medium", Row(report, "A L"));
        // The notes wrap each channel into short indented lines, so the
        // dialog's word-wrapping report box never needs a horizontal scroll.
        Assert.Contains("  B L\r\n", report);
        Assert.Contains("    delay: vs A L: margin 0.2 dB, wide seed", report);
        Assert.Contains("    gain:  kept (mono channel)", report);
    }

    // The complaint this replaced: "2.43 -> 2.43" made every row look like a
    // change, and the one row that WAS a change carried a wider number, which
    // pushed its whole line sideways.
    [Fact]
    public void Format_KeepsUnchangedValuesAndAlignsTheDecimalPoints()
    {
        string report = VirtualCrossoverAutoDelayReport.Format(
            [
                Outcome("B L", 2.43, 2.43, delayConfidence: AlignmentConfidence.Medium),
                Outcome("D L", 10.07, 10.37, afterInvert: true, beforeGain: -1.0,
                    delayConfidence: AlignmentConfidence.Low)
            ],
            stereo: true,
            new AutoDelayRunRequest(0.26, RightHandDrive: false, AdjustGains: false, 0));

        // Row-scoped: the legend at the foot of the report spells the two
        // shapes out with example numbers of its own.
        Assert.Contains("2.43 (kept)", Row(report, "B L"));
        Assert.DoesNotContain("2.43 -> 2.43", report);
        Assert.Contains("10.07 -> 10.37", Row(report, "D L"));
        Assert.Contains("Changes: 1 delay (D L), 1 polarity (D L)", report);
        // The first dot of a row is its delay figure: the narrower number is
        // padded inside its cell, so the two land in the same column.
        Assert.Equal(
            Row(report, "B L").IndexOf('.', StringComparison.Ordinal),
            Row(report, "D L").IndexOf('.', StringComparison.Ordinal));
        // Nothing scored a gain, so that confidence column would be a wall of
        // dashes — it is dropped instead.
        Assert.DoesNotContain("Gain conf", report);
    }

    [Fact]
    public void Format_ReportsAProposalThatChangesNothing()
    {
        string report = VirtualCrossoverAutoDelayReport.Format(
            [Outcome("A L", 1.5, 1.5, delayConfidence: AlignmentConfidence.High)],
            stereo: true,
            new AutoDelayRunRequest(0.25, RightHandDrive: false, AdjustGains: false, 0),
            leftSumLoss: new AutoDelaySumLossForecast(-0.2, -0.2));

        Assert.Contains(
            "Changes: none — the current settings already match the proposal.",
            report);
        Assert.Contains("-0.2 dB (unchanged)", report);
        Assert.DoesNotContain("->", Row(report, "A L"));
    }

    [Fact]
    public void Format_RightHandDriveNamesTheLeftSideAsLeading()
    {
        string report = VirtualCrossoverAutoDelayReport.Format(
            [Outcome("A L", 0.0, 1.25)],
            stereo: true,
            new AutoDelayRunRequest(
                SceneOffsetMs: 0.25,
                RightHandDrive: true,
                AdjustGains: false,
                NearSideCutDb: 0));

        // RHD mirrors the reference: the right side is fitted to lag, so the
        // LEFT side is the one the offset makes lead.
        Assert.Contains("Scene offset 0.25 ms (RHD: left side leads)", report);
    }

    // The user enters the tilt as a layout-neutral near-side cut; the sign
    // of the gain engine's L-R figure comes from the layout alone (near =
    // left on LHD, right on RHD) — switching LHD/RHD must never require
    // re-entering a sign, exactly like the scene offset.
    [Fact]
    public void RunRequest_SignsTheNearSideCutByTheLayout()
    {
        Assert.Equal(
            -1.5,
            new AutoDelayRunRequest(0.25, RightHandDrive: false, true, 1.5)
                .LevelDifferenceDb);
        Assert.Equal(
            1.5,
            new AutoDelayRunRequest(0.25, RightHandDrive: true, true, 1.5)
                .LevelDifferenceDb);
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
            new AutoDelayRunRequest(
                SceneOffsetMs: 0,
                RightHandDrive: false,
                AdjustGains: false,
                NearSideCutDb: 0),
            leftSumLoss: new AutoDelaySumLossForecast(-1.5, -0.3));

        Assert.Contains("single side", report);
        Assert.DoesNotContain("Scene offset", report);
        Assert.Contains("Gains not adjusted", report);
        // One side, so the forecast needs no side label — and with gains off
        // the summary drops the gain part the header line already covered.
        Assert.Contains("Changes: 1 delay (A), no polarity changes", report);
        Assert.DoesNotContain("gain changes", report);
        Assert.Contains("Predicted sum loss (avg over the crossover window):", report);
        Assert.Contains("  -1.5 -> -0.3 dB (1.2 dB better)", report);
        Assert.DoesNotContain("Warning:", report);
        Assert.Contains("0.0 (kept)", report);
        // The reference was not chosen at all — its row reads "ref", not a
        // confidence.
        Assert.Contains("ref", Row(report, "A "));
    }

    [Fact]
    public void FormatPolarityMismatchWarning_NoMismatchGivesNoWarning()
    {
        Assert.Null(VirtualCrossoverPanel.FormatPolarityMismatchWarning([]));
    }

    [Fact]
    public void FormatPolarityMismatchWarning_NamesTheDriversAndFlagsInversion()
    {
        string? warning = VirtualCrossoverPanel.FormatPolarityMismatchWarning(
            ["Midbass", "Tweeter"]);

        Assert.NotNull(warning);
        Assert.StartsWith("⚠", warning);
        Assert.Contains("Midbass", warning);
        Assert.Contains("Tweeter", warning);
        Assert.Contains("inverted", warning);
    }
}
