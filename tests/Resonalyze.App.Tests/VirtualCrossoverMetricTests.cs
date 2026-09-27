using System.Globalization;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverMetricTests
{
    private static readonly VirtualCrossoverMetric.Entry Junction =
        new("A/B", -1.23, -6.5, 900, 3_600, IsTotal: false);

    private static readonly VirtualCrossoverMetric.Entry Total =
        new("total", -0.8, null, 100, 10_000, IsTotal: true);

    [Fact]
    public void FormatLabel_EmptyShowsPlaceholder()
    {
        Assert.Equal(
            "Sum loss avg: —",
            VirtualCrossoverMetric.FormatLabel([]));
    }

    [Fact]
    public void FormatLabel_JoinsJunctionsAndTotal()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatLabel([Junction, Total]);

            Assert.Equal(
                "Sum loss avg: A/B -1.2 dB, dip -6.5 dB   total -0.8 dB",
                text);
        });
    }

    [Fact]
    public void FormatCompact_RendersMonospaceColumn()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatCompact([Junction, Total]);

            // Two decimals: lobe alternatives differ by hundredths of a dB.
            Assert.StartsWith("Sum loss (dB)\r\n         avg /   dip\r\n\r\n", text);
            Assert.Contains("A/B    -1.23 / -6.50", text);
            Assert.Contains("Total  -0.80 /     —", text);
        });
    }

    [Fact]
    public void FormatDetail_IncludesBands()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatDetail([Junction]);

            Assert.Contains("A/B: 900 Hz – 3.6 kHz", text);
            Assert.DoesNotContain("-1.23", text);
        });
    }

    [Fact]
    public void DirectRead_IsHeadedAsSuch_SoItIsNeverMistakenForTheFullOne()
    {
        // Direct-sound (FDW-8) loss must never be compared with steady-state, so every rendering names its family.
        RunWithInvariantCulture(() =>
        {
            string compact = VirtualCrossoverMetric.FormatCompact([Junction], direct: true);
            string detail = VirtualCrossoverMetric.FormatDetail([Junction], direct: true);

            Assert.StartsWith("Sum loss (direct, dB)\r\n         avg /   dip\r\n\r\n", compact);
            Assert.Contains("A/B    -1.23 / -6.50", compact);
            Assert.StartsWith("Sum loss (direct)", detail);
            Assert.DoesNotContain("(direct)", VirtualCrossoverMetric.FormatDetail([Junction]));
            Assert.Equal("Sum loss (direct, dB)\r\n         avg /   dip\r\n\r\n—",
                VirtualCrossoverMetric.FormatCompact([], direct: true));
            Assert.StartsWith("Sum loss (direct)", VirtualCrossoverMetric.FormatDetail([], direct: true));
        });
    }

    [Fact]
    public void FormatStereoDeltasCompact_ListsPerChannelArrivalsAndDashesUnreliableSides()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasCompact(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", 25.98, 25.73, 175, 1_300, LevelDeltaDb: 1.63),
                new VirtualCrossoverMetric.StereoDelta(
                    "C", 15.34, 15.41, 1_800, 20_000, LevelDeltaDb: -0.62),
                new VirtualCrossoverMetric.StereoDelta(
                    "D", null, 13.62, 1_800, 20_000)
            ]);

            Assert.Equal(
                "Arrival (ms)\r\n" +
                "         L      R  \u0394 L\u2212R\r\n" +
                "B    25.98  25.73  +0.25\r\n" +
                "C    15.34  15.41  -0.07\r\n" +
                "D        \u2014  13.62      \u2014\r\n" +
                "\r\n" +
                "Level \u0394 L\u2212R (dB)\r\n" +
                "B     +1.6\r\n" +
                "C     -0.6\r\n" +
                "D        \u2014",
                text);
        });
    }

    [Fact]
    public void FormatStereoDeltasCompact_MonoSubShowsItsArrivalAndDashesRightAndDelta()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasCompact(
            [
                new VirtualCrossoverMetric.StereoDelta("A", 22.39, null, 20, 80),
                new VirtualCrossoverMetric.StereoDelta(
                    "B", 16.78, 17.18, 40, 160, LevelDeltaDb: 3.4)
            ]);

            Assert.Equal(
                "Arrival (ms)\r\n" +
                "         L      R  Δ L−R\r\n" +
                "A    22.39      —      —\r\n" +
                "B    16.78  17.18  -0.40\r\n" +
                "\r\n" +
                "Level Δ L−R (dB)\r\n" +
                "A        —\r\n" +
                "B     +3.4",
                text);
        });
    }

    [Fact]
    public void FormatStereoDeltasCompact_MarksModalLatchedSidesAndTheirDelta()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasCompact(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", 15.51, 21.81, 80, 220, LevelDeltaDb: 4.5,
                    RightLatched: true)
            ]);

            Assert.Contains("B    15.51 ~21.81 ~-6.30", text);
        });
    }

    [Fact]
    public void FormatStereoDeltasDetail_ExplainsTheLatchMarkOnlyWhenARowLatched()
    {
        RunWithInvariantCulture(() =>
        {
            string latched = VirtualCrossoverMetric.FormatStereoDeltasDetail(
                [new VirtualCrossoverMetric.StereoDelta("B", 15.514, 21.811, 80, 220, RightLatched: true)]);
            string clean = VirtualCrossoverMetric.FormatStereoDeltasDetail(
                [new VirtualCrossoverMetric.StereoDelta("B", 15.514, 21.811, 80, 220)]);

            Assert.Contains("~", latched);
            Assert.DoesNotContain("~", clean);
        });
    }

    [Fact]
    public void FormatStereoDeltasDetail_NamesTheEnergyOnsetRows()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", 16.488, 17.512, 65, 200, EnergyOnset: true),
                new VirtualCrossoverMetric.StereoDelta("C", 16.533, 16.213, 200, 1_610)
            ]);

            Assert.Contains("B: 65 Hz – 200 Hz, energy onsets", text);
            Assert.Contains("C: 200 Hz – 1.61 kHz\r\n", text);
        });
    }

    [Fact]
    public void FormatStereoDeltasDetail_SaysWhenALowPairIsBackOnFirstPeaks()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", 16.628, 22.248, 65, 200, EnergyOnsetWithheld: true)
            ]);

            Assert.Contains("B: 65 Hz – 200 Hz, first peaks", text);
            Assert.Contains("30 dB", text);
            Assert.DoesNotContain("energy onsets", text);
        });
    }

    [Fact]
    public void FormatStereoDeltasCompact_EmptyListRendersNothing()
    {
        Assert.Equal(
            string.Empty,
            VirtualCrossoverMetric.FormatStereoDeltasCompact([]));
    }

    [Fact]
    public void FormatStereoDeltasDetail_GivesEachRowItsBandAndStatesTheSigns()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", 25.977, 25.724, 175, 1_300, LevelDeltaDb: 1.63),
                new VirtualCrossoverMetric.StereoDelta(
                    "C", null, 13.618, 1_800, 20_000),
                new VirtualCrossoverMetric.StereoDelta(
                    "D", null, null, 1_800, 20_000)
            ]);

            Assert.Contains("positive = right leads", text);
            Assert.Contains("B: 175 Hz – 1.3 kHz", text);
            Assert.Contains("C: 1.8 kHz – 20 kHz\r\n", text);
            Assert.Contains("D: 1.8 kHz – 20 kHz, no measurable arrival", text);
            Assert.Contains("positive = LEFT louder", text);
        });
    }

    [Fact]
    public void FormatStereoDeltasDetail_SwapsTheLevelLegendWhenTheLevelsComeFromSpatialAverages()
    {
        RunWithInvariantCulture(() =>
        {
            string spatial = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", 25.977, 25.724, 175, 1_300, LevelDeltaDb: 1.63,
                    LevelFromSpatialAverage: true)
            ]);
            string gated = VirtualCrossoverMetric.FormatStereoDeltasDetail(
                [new VirtualCrossoverMetric.StereoDelta("B", 25.977, 25.724, 175, 1_300, LevelDeltaDb: 1.63)]);

            Assert.Contains("spatial averages", spatial);
            Assert.DoesNotContain("point mic", spatial);
            Assert.DoesNotContain("spatial averages", gated);
        });
    }

    [Fact]
    public void FormatStereoDeltasDetail_MarksThePointMeasuredExceptionInASpatialList()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", 25.977, 25.724, 175, 1_300, LevelDeltaDb: 1.63,
                    LevelFromSpatialAverage: true),
                new VirtualCrossoverMetric.StereoDelta(
                    "C", 15.341, 15.412, 1_800, 20_000, LevelDeltaDb: -0.62)
            ]);

            string[] lines = text.Split("\r\n");
            Assert.DoesNotContain("point mic", lines.Single(line => line.StartsWith("B:")));
            Assert.Contains("point mic", lines.Single(line => line.StartsWith("C:")));
        });
    }

    [Fact]
    public void FormatStereoDeltasDetail_KeepsTheSpatialLevelLegendWhenNoArrivalIsMeasurable()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", null, null, 175, 1_300, LevelDeltaDb: -2.5,
                    LevelFromSpatialAverage: true)
            ]);

            Assert.Contains("no measurable arrival", text);
            Assert.Contains("spatial averages", text);
        });
    }

    [Fact]
    public void FormatGroupDeltasDetail_ExplainsSpatialLevelsAndMarksThePointMeasuredException()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatGroupDeltasDetail(
            [
                new VirtualCrossoverMetric.GroupDelta(
                    VirtualCrossoverZone.Rear, 8.12, -6.3, 290, 20_000,
                    LevelFromSpatialAverage: true),
                new VirtualCrossoverMetric.GroupDelta(
                    VirtualCrossoverZone.Center, -0.35, -2.1, 290, 20_000)
            ]);

            string[] lines = text.Split("\r\n");
            string rear = VirtualCrossoverZones.DisplayName(VirtualCrossoverZone.Rear);
            string center = VirtualCrossoverZones.DisplayName(VirtualCrossoverZone.Center);
            Assert.DoesNotContain("point mic", lines.Single(line => line.StartsWith(rear + ":")));
            Assert.Contains("point mic", lines.Single(line => line.StartsWith(center + ":")));
            Assert.Contains("290 Hz – 20 kHz", text);
            Assert.Contains("spatial averages", text);
        });
    }

    [Fact]
    public void FormatGroupDeltasDetail_SaysNothingAboutAveragesForAPointMeasuredList()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatGroupDeltasDetail(
            [
                new VirtualCrossoverMetric.GroupDelta(
                    VirtualCrossoverZone.Rear, 8.12, -6.3, 290, 20_000)
            ]);

            Assert.DoesNotContain("spatial", text);
            Assert.DoesNotContain("(point mic)", text);
        });
    }

    private static VirtualCrossoverMetric.PhaseEntry PhaseJunction(
        double? lobeMargin = 0.19,
        double? rivalExtraMs = -12.20,
        double? rivalScore = 0.78,
        double phaseConsistency = 0.93,
        bool bestInvert = false,
        double oppositePolarityScore = 0.42,
        double bestExtraDelayMs = -1.30) =>
        new(
            "A/B",
            "A",
            80,
            40,
            160,
            new Resonalyze.Dsp.JunctionPhaseResult(
                CurrentScore: 0.96,
                PhaseAtCrossoverDeg: -3.4,
                PhaseConsistency: phaseConsistency,
                BestExtraDelayMs: bestExtraDelayMs,
                BestInvert: bestInvert,
                BestScore: 0.97,
                OppositePolarityScore: oppositePolarityScore,
                RivalExtraDelayMs: rivalExtraMs,
                RivalScore: rivalScore,
                LobeMargin: lobeMargin,
                FitDelayMs: 2.62,
                FitRmsDeg: 10.3));

    [Fact]
    public void FormatPhaseCompact_RendersPhaseAndFix()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseCompact([PhaseJunction()]);

            Assert.Equal(
                "Junction phase\r\n" +
                "       φfc  fix ms  score\r\n" +
                "A/B     -3°  -1.30   0.96",
                text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_ShowsWhereTheJunctionStandsNow()
    {
        RunWithInvariantCulture(() =>
        {
            VirtualCrossoverMetric.PhaseEntry cancelling = PhaseJunction() with
            {
                Result = PhaseJunction().Result with { CurrentScore = -0.42 }
            };

            Assert.Contains(
                "A/B     -3°  -1.30  -0.42",
                VirtualCrossoverMetric.FormatPhaseCompact([cancelling]));
        });
    }

    [Fact]
    public void FormatPhaseCompact_MutesAFixTooSmallToMatter()
    {
        RunWithInvariantCulture(() =>
        {
            // 0.05 ms at 80 Hz is 1.4 deg: the threshold is phase at fc, not milliseconds.
            string text = VirtualCrossoverMetric.FormatPhaseCompact(
                [PhaseJunction(bestExtraDelayMs: -0.05)]);

            Assert.Contains("A/B     -3°      ·", text);
            Assert.DoesNotContain("-0.05", text);

            VirtualCrossoverMetric.PhaseEntry high =
                PhaseJunction(bestExtraDelayMs: -0.05) with { CrossoverHz = 4_000 };
            Assert.Contains("-0.05", VirtualCrossoverMetric.FormatPhaseCompact([high]));
        });
    }

    [Fact]
    public void FormatPhaseCompact_WithholdsTheFixWhereNoDelayAlignsTheBand()
    {
        RunWithInvariantCulture(() =>
        {
            // Field case behind the threshold: a 2 kHz handover whose -0.37 ms "fix" cost 1.15 dB of summation loss.
            VirtualCrossoverMetric.PhaseEntry incoherent = PhaseJunction() with
            {
                Result = PhaseJunction().Result with
                {
                    BestScore = 0.34,
                    CurrentScore = 0.29,
                    OppositePolarityScore = 0.31
                }
            };

            string text = VirtualCrossoverMetric.FormatPhaseCompact([incoherent]);

            Assert.Contains("A/B     -3°      —   0.29", text);
            Assert.DoesNotContain("-1.30", text);
            Assert.DoesNotContain("~", text);
            Assert.DoesNotContain("!", text);

            Assert.Contains(
                "-1.30",
                VirtualCrossoverMetric.FormatPhaseCompact([PhaseJunction()]));
        });
    }

    [Fact]
    public void FormatPhaseDetail_SaysWhyTheFixIsWithheld()
    {
        RunWithInvariantCulture(() =>
        {
            VirtualCrossoverMetric.PhaseEntry incoherent = PhaseJunction() with
            {
                Result = PhaseJunction().Result with { BestScore = 0.34 }
            };

            string text = VirtualCrossoverMetric.FormatPhaseDetail([incoherent]);

            Assert.Contains("ceiling 0.34", text);
            Assert.DoesNotContain("best 0.34", text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_KeepsThePolarityMarkOnAMutedFix()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseCompact(
                [PhaseJunction(bestInvert: true, bestExtraDelayMs: -0.05)]);

            Assert.Contains("A/B     -3°      ·i", text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_KeepsASmallButSignificantFixReadable()
    {
        RunWithInvariantCulture(() =>
        {
            // Above ~5.6 kHz a meaningful fix is under 0.005 ms (0.004 ms = 23 deg at 16 kHz), so it takes a third decimal.
            VirtualCrossoverMetric.PhaseEntry entry =
                PhaseJunction(bestExtraDelayMs: -0.004) with { CrossoverHz = 16_000 };

            string text = VirtualCrossoverMetric.FormatPhaseCompact([entry]);

            Assert.Contains("A/B     -3° -0.004", text);
            Assert.DoesNotContain("0.00 ", text);
            // Since .NET Core 3.0 a negative value rounding to zero renders "-+0.00" in a two-section format.
            Assert.DoesNotContain("-+", text);
            Assert.Contains(
                "-0.004 ms", VirtualCrossoverMetric.FormatPhaseDetail([entry]));
        });
    }

    [Fact]
    public void FormatPhaseCompact_FlagsAnAmbiguousLobeMargin()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseCompact(
                [PhaseJunction(lobeMargin: 0.04)]);

            Assert.Contains("A/B     -3°  -1.30   0.96 !", text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_RaisesNoFlagWithoutARivalLobe()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseCompact(
                [PhaseJunction(
                    lobeMargin: null, rivalExtraMs: null, rivalScore: null)]);

            Assert.Contains("A/B     -3°  -1.30", text);
            Assert.DoesNotContain("!", text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_DashesAnInconsistentPhase()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseCompact(
                [PhaseJunction(phaseConsistency: 0.31)]);

            Assert.Contains("A/B       —  -1.30", text);
        });
    }

    [Fact]
    public void FormatPhaseDetail_ExplainsAnInconsistentPhase()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseDetail(
                [PhaseJunction(phaseConsistency: 0.31)]);

            Assert.Contains("φ unreliable (R 0.31", text);
            Assert.DoesNotContain("φ -3°", text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_FlagsARecommendedPolarityFlip()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseCompact(
                [PhaseJunction(bestInvert: true)]);

            Assert.Contains("A/B     -3°  -1.30i", text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_MarksAnAmbiguousPolarityWithTilde()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseCompact(
                [PhaseJunction(oppositePolarityScore: 0.96)]);

            Assert.Contains("A/B     -3°  -1.30~", text);
            Assert.DoesNotContain("!", text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_LeavesTheSlotBlankWhenPolarityIsSettled()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseCompact([PhaseJunction()]);

            Assert.Contains("A/B     -3°  -1.30", text);
        });
    }

    [Fact]
    public void FormatPhaseDetail_SpellsOutARecommendedFlip()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseDetail(
                [PhaseJunction(bestInvert: true)]);

            Assert.Contains("on A, invert A;", text);
        });
    }

    [Fact]
    public void FormatPhaseDetail_ShowsHowCloseAFlipScoresWhenKeepingPolarity()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseDetail(
                [PhaseJunction(bestInvert: false, oppositePolarityScore: 0.42)]);

            Assert.Contains("flip 0.42", text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_EmptyListRendersNothing()
    {
        Assert.Equal(
            string.Empty,
            VirtualCrossoverMetric.FormatPhaseCompact([]));
    }

    [Fact]
    public void FormatPhaseDetail_ListsWhatTheColumnLeavesOut()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseDetail([PhaseJunction()]);

            Assert.Contains("A/B @ 80 Hz:", text);
            Assert.Contains("best 0.97 at -1.30 ms on A", text);
            Assert.Contains("flip 0.42", text);
            Assert.Contains("rival 0.78 at -12.20 ms (margin 0.19)", text);
            Assert.Contains("LOWER channel", text);
        });
    }

    private static void RunWithInvariantCulture(Action assertions)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            assertions();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
