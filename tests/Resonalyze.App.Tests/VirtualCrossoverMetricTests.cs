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

            Assert.Equal(
                "Sum loss avg\r\nA/B: -1.23 dB avg, dip -6.50 dB " +
                "(900 Hz – 3.6 kHz)",
                text);
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
            Assert.StartsWith("Sum loss (direct) avg\r\nA/B: -1.23 dB avg, dip -6.50 dB", detail);
            Assert.Contains("not comparable", detail);
            Assert.Equal("Sum loss (direct, dB)\r\n         avg /   dip\r\n\r\n—",
                VirtualCrossoverMetric.FormatCompact([], direct: true));
            Assert.Equal("Sum loss (direct) avg: —",
                VirtualCrossoverMetric.FormatDetail([], direct: true));
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
    public void FormatStereoDeltasDetail_ExplainsAModalLatchedRow()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", 15.514, 21.811, 80, 220, RightLatched: true)
            ]);

            Assert.Contains("B: L 15.514 / R ~21.811 ms, Δ ~-6.297 ms", text);
            Assert.Contains("modal", text);
            Assert.Contains("trust its log over this row", text);
        });
    }

    [Fact]
    public void FormatStereoDeltasDetail_OmitsTheLatchLegendWhenNothingLatched()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta("B", 15.514, 21.811, 80, 220)
            ]);

            Assert.DoesNotContain("modal", text);
            Assert.DoesNotContain("nergy onset", text);
        });
    }

    [Fact]
    public void FormatStereoDeltasDetail_NamesTheEnergyOnsetRowsAndExplainsThem()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", 16.488, 17.512, 65, 200, EnergyOnset: true),
                new VirtualCrossoverMetric.StereoDelta("C", 16.533, 16.213, 200, 1_610)
            ]);

            Assert.Contains("B: L 16.488 / R 17.512 ms, Δ -1.024 ms (65 Hz – 200 Hz, energy onsets)", text);
            Assert.Contains("C: L 16.533 / R 16.213 ms, Δ +0.320 ms (200 Hz – 1.61 kHz)", text);
            Assert.Contains("Energy onsets: a pair whose shared band is centred below 300 Hz", text);
            Assert.Contains("30 dB", text);
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

            Assert.Contains(
                "(65 Hz – 200 Hz, first peaks: a side is under the 30 dB an energy onset needs)",
                text);
            Assert.DoesNotContain("Energy onsets:", text);
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
    public void FormatStereoDeltasDetail_ListsSidesExplainsTheSignAndIncludesBands()
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

            Assert.Contains("positive: right leads", text);
            Assert.Contains(
                "B: L 25.977 / R 25.724 ms, \u0394 +0.253 ms, level +1.6 dB " +
                "(175 Hz \u2013 1.3 kHz)",
                text);
            Assert.Contains(
                "C: L \u2014 / R 13.618 ms, \u0394 \u2014 (1.8 kHz \u2013 20 kHz)",
                text);
            Assert.Contains("D: \u2014 (no measurable arrival)", text);
            Assert.Contains("positive: LEFT louder", text);
        });
    }

    [Fact]
    public void FormatStereoDeltasDetail_SwapsTheLevelLegendWhenTheLevelsComeFromSpatialAverages()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", 25.977, 25.724, 175, 1_300, LevelDeltaDb: 1.63,
                    LevelFromSpatialAverage: true)
            ]);

            Assert.Contains("spatial averages", text);
            Assert.Contains("positive: LEFT louder", text);
            Assert.DoesNotContain("gated band level", text);
            Assert.DoesNotContain("(point mic)", text);
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

            Assert.Contains("level +1.6 dB (175 Hz", text);
            Assert.Contains("level -0.6 dB (point mic)", text);
            Assert.Contains("(point mic): that pair's captures cannot produce", text);
        });
    }

    [Fact]
    public void FormatStereoDeltasDetail_KeepsTheSpatialLevelWhenNoArrivalIsMeasurable()
    {
        RunWithInvariantCulture(() =>
        {
            // A capture's level outlives the arrivals, so the early "no measurable arrival" row must not swallow it.
            string text = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta(
                    "B", null, null, 175, 1_300, LevelDeltaDb: -2.5,
                    LevelFromSpatialAverage: true)
            ]);

            Assert.Contains(
                "B: — (no measurable arrival), level -2.5 dB", text);
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

            Assert.Contains("6.3 dB quieter.", text);
            Assert.Contains("2.1 dB quieter (point mic).", text);
            Assert.Contains("spatial averages", text);
            Assert.Contains("could not be read from the captures", text);
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

            Assert.Contains("no delay aligns this band (ceiling 0.34)", text);
            Assert.DoesNotContain("best 0.34 at", text);
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

            Assert.Contains("flip scores 0.42", text);
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
    public void FormatPhaseDetail_ListsEveryFigureAndTheLegend()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseDetail([PhaseJunction()]);

            Assert.Contains(
                "A/B @ 80 Hz: φ -3° at fc (R 0.93); phase score 0.96 now, " +
                "best 0.97 at -1.30 ms on A (flip scores 0.42);",
                text);
            Assert.Contains(
                "rival lobe 0.78 at -12.20 ms (margin 0.19); " +
                "fit Δτ +2.62 ms, rms 10° (40 Hz – 160 Hz)",
                text);
            Assert.Contains("the delay to add to the LOWER channel", text);
            Assert.Contains("φ near ±180° never settles it either", text);
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
