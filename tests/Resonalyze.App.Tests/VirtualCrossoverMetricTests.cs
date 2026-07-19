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

            Assert.StartsWith("Sum loss (dB)\r\n  avg / dip\r\n\r\n", text);
            Assert.Contains("A/B    -1.2 / -6.5", text);
            Assert.Contains("Total  -0.8 /    —", text);
        });
    }

    [Fact]
    public void FormatDetail_IncludesBands()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatDetail([Junction]);

            Assert.Equal(
                "Sum loss avg\r\nA/B: -1.2 dB avg, dip -6.5 dB " +
                "(900 Hz – 3.6 kHz)",
                text);
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
            // The shared mono sub carries a single arrival on the left slot; the
            // right side and the L−R delta have no meaning and read "—".
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
            // The right side's envelope timed the modal build-up, not the
            // direct rise: its number and the Δ built on it carry a "~".
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

    private static VirtualCrossoverMetric.PhaseEntry PhaseJunction(
        double? lobeMargin = 0.19,
        double? rivalExtraMs = -12.20,
        double? rivalScore = 0.78,
        double phaseConsistency = 0.93,
        bool bestInvert = false,
        double oppositePolarityScore = 0.42) =>
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
                BestExtraDelayMs: -0.30,
                BestInvert: bestInvert,
                BestScore: 0.97,
                OppositePolarityScore: oppositePolarityScore,
                RivalExtraDelayMs: rivalExtraMs,
                RivalScore: rivalScore,
                LobeMargin: lobeMargin,
                FitDelayMs: 2.62,
                FitRmsDeg: 10.3));

    [Fact]
    public void FormatPhaseCompact_RendersPhaseFixAndMargin()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseCompact([PhaseJunction()]);

            Assert.Equal(
                "Junction phase\r\n" +
                "       φfc  fix ms   lobe\r\n" +
                "A/B     -3°  -0.30   0.19",
                text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_RendersARoundedZeroFixUnsigned()
    {
        RunWithInvariantCulture(() =>
        {
            // Since the .NET Core 3.0 signed-zero change, a negative value that
            // rounds to zero renders through a TWO-section format as "-+0.00";
            // the three-section form must show a plain unsigned zero.
            VirtualCrossoverMetric.PhaseEntry entry = PhaseJunction() with
            {
                Result = PhaseJunction().Result with { BestExtraDelayMs = -0.004 }
            };

            string text = VirtualCrossoverMetric.FormatPhaseCompact([entry]);

            Assert.Contains("A/B     -3°   0.00   0.19", text);
            Assert.DoesNotContain("-+", text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_FlagsAnAmbiguousLobeMargin()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseCompact(
                [PhaseJunction(lobeMargin: 0.04)]);

            Assert.Contains("A/B     -3°  -0.30   0.04 !", text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_DashesAMissingRivalLobe()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatPhaseCompact(
                [PhaseJunction(
                    lobeMargin: null, rivalExtraMs: null, rivalScore: null)]);

            Assert.Contains("A/B     -3°  -0.30      —", text);
            Assert.DoesNotContain("!", text);
        });
    }

    [Fact]
    public void FormatPhaseCompact_DashesAnInconsistentPhase()
    {
        RunWithInvariantCulture(() =>
        {
            // A notch or spectral gap at the handover leaves the fc window's
            // bins disagreeing; the φ column must dash, not show mush.
            string text = VirtualCrossoverMetric.FormatPhaseCompact(
                [PhaseJunction(phaseConsistency: 0.31)]);

            Assert.Contains("A/B       —  -0.30   0.19", text);
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
            // BestInvert renders an "i" right after the fix; the columns stay
            // aligned with the space a non-flipped row uses there.
            string text = VirtualCrossoverMetric.FormatPhaseCompact(
                [PhaseJunction(bestInvert: true)]);

            Assert.Contains("A/B     -3°  -0.30i  0.19", text);
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
                "best 0.97 at -0.30 ms on A (flip scores 0.42);",
                text);
            Assert.Contains(
                "rival lobe 0.78 at -12.20 ms (margin 0.19); " +
                "fit Δτ +2.62 ms, rms 10° (40 Hz – 160 Hz)",
                text);
            Assert.Contains("the delay to add to the LOWER channel", text);
            Assert.Contains("does NOT by itself settle polarity", text);
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
