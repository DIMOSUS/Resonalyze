using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

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

    private static AutoDelayChannelOutcome RearOutcome(string name)
    {
        AutoDelayChannelOutcome outcome = Outcome(name, 0.0, 15.0);
        outcome.Runtime.Pair.Zone = VirtualCrossoverZone.Rear;
        return outcome;
    }

    private static string Row(string report, string name) =>
        report.Split('\n')
            .First(line => line.StartsWith(name, StringComparison.Ordinal));

    [Fact]
    public void Format_StatesTheRearFillOffsetIncludingADeliberateZero()
    {
        // Zero is printed: silence would not distinguish a co-arrived rear from one held back.
        var request = new AutoDelayRunRequest(0.25, false, false, 0.0, 0.0);

        string report = VirtualCrossoverAutoDelayReport.Format(
            [Outcome("A L", 0.0, 1.0), RearOutcome("C L")],
            stereo: true,
            request,
            null);

        Assert.Contains("Rear fill 0.0 ms", report);
        Assert.Contains("co-arriving", report);
    }

    [Fact]
    public void Format_SaysHowFarBackARearFillWasHeld()
    {
        var request = new AutoDelayRunRequest(0.25, false, false, 0.0, 15.0);

        string report = VirtualCrossoverAutoDelayReport.Format(
            [Outcome("A L", 0.0, 1.0), RearOutcome("C L")],
            stereo: true,
            request,
            null);

        Assert.Contains("Rear fill 15.0 ms behind", report);
    }

    [Fact]
    public void Format_SaysNothingAboutARearFillAProjectDoesNotHave()
    {
        // The offset is stored per project; a front-only run must not print it.
        var request = new AutoDelayRunRequest(0.25, false, false, 0.0, 15.0);

        string report = VirtualCrossoverAutoDelayReport.Format(
            [Outcome("A L", 0.0, 1.0), Outcome("B L", 0.0, 2.0)],
            stereo: true,
            request,
            null);

        Assert.DoesNotContain("Rear fill", report);
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
            new AutoDelayRunRequest(
                SceneOffsetMs: 0.27,
                RightHandDrive: false,
                AdjustGains: true,
                NearSideCutDb: 1.5),
            leftSumLoss: new AutoDelaySumLossForecast(-2.0, -0.6),
            rightSumLoss: new AutoDelaySumLossForecast(-2.4, -0.8));

        Assert.Contains("stereo", report);
        Assert.Contains("Scene offset 0.27 ms (LHD: right side leads)", report);
        Assert.Contains("near-side cut 1.5 dB", report);
        Assert.Contains(
            "Changes: 3 delays (A L, B L, C R), 1 polarity (B L), 1 gain (A L)",
            report);
        Assert.Contains("Left   -2.0 -> -0.6 dB (1.4 dB better)", report);
        Assert.Contains("Right  -2.4 -> -0.8 dB (1.6 dB better)", report);
        Assert.Contains(
            "Warning: LOW delay confidence — B L (reasons in Notes)", report);
        Assert.Equal(
            1, report.Split("vs A L: margin 0.2 dB, wide seed").Length - 1);
        Assert.Contains("0.00 -> 1.25", report);
        Assert.Contains("-3.0 -> -4.5", report);
        Assert.Contains("-2.0 (kept)", report);
        Assert.Contains("norm -> inv", report);
        Assert.Contains("high", report);
        Assert.Contains("LOW", report);
        // A locked pick is a constraint, not a vote: "locked" and no LOW warning.
        Assert.Contains("locked", Row(report, "C R"));
        Assert.DoesNotContain("Warning: LOW delay confidence — C R", report);
        Assert.Contains("Gain conf", report);
        Assert.Contains("medium", Row(report, "A L"));
        Assert.Contains("  B L\r\n", report);
        Assert.Contains("    delay: vs A L: margin 0.2 dB, wide seed", report);
        Assert.Contains("    gain:  kept (mono channel)", report);
    }

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

        Assert.Contains("2.43 (kept)", Row(report, "B L"));
        Assert.DoesNotContain("2.43 -> 2.43", report);
        Assert.Contains("10.07 -> 10.37", Row(report, "D L"));
        Assert.Contains("Changes: 1 delay (D L), 1 polarity (D L)", report);
        Assert.Equal(
            Row(report, "B L").IndexOf('.', StringComparison.Ordinal),
            Row(report, "D L").IndexOf('.', StringComparison.Ordinal));
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

    // Arrow and verdict both read the rounded figures, so a rounding boundary cannot disagree.
    [Theory]
    [InlineData(-2.449, -2.451, "-2.4 -> -2.5 dB (0.1 dB worse)")]
    [InlineData(-2.451, -2.449, "-2.5 -> -2.4 dB (0.1 dB better)")]
    [InlineData(-2.44, -2.43, "-2.4 dB (unchanged)")]
    public void Format_ReadsTheForecastVerdictOffThePrintedFigures(
        double beforeDb, double afterDb, string expected)
    {
        string report = VirtualCrossoverAutoDelayReport.Format(
            [Outcome("A L", 1.0, 1.0)],
            stereo: false,
            new AutoDelayRunRequest(0, RightHandDrive: false, AdjustGains: false, 0),
            leftSumLoss: new AutoDelaySumLossForecast(beforeDb, afterDb));

        Assert.Contains(expected, report);
        Assert.DoesNotContain("0.0 dB", report);
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

        Assert.Contains("Scene offset 0.25 ms (RHD: left side leads)", report);
    }

    // The tilt is a layout-neutral near-side cut; the L-R sign comes from the layout alone.
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
        Assert.Contains("Changes: 1 delay (A), no polarity changes", report);
        Assert.DoesNotContain("gain changes", report);
        Assert.Contains("Predicted sum loss (avg over the crossover window):", report);
        Assert.Contains("  -1.5 -> -0.3 dB (1.2 dB better)", report);
        Assert.DoesNotContain("Warning:", report);
        Assert.Contains("0.0 (kept)", report);
        Assert.Contains("ref", Row(report, "A "));
    }

    [Fact]
    public void FormatPolarityMismatchWarning_NoMismatchGivesNoWarning()
    {
        Assert.Null(VirtualCrossoverAutoDelay.FormatPolarityMismatchWarning([]));
    }

    [Fact]
    public void FormatPolarityMismatchWarning_NamesTheDriversAndFlagsInversion()
    {
        string? warning = VirtualCrossoverAutoDelay.FormatPolarityMismatchWarning(
            ["Midbass", "Tweeter"]);

        Assert.NotNull(warning);
        Assert.StartsWith("⚠", warning);
        Assert.Contains("Midbass", warning);
        Assert.Contains("Tweeter", warning);
        Assert.Contains("inverted", warning);
    }
}
