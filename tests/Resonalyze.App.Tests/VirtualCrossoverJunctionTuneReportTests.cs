using System.Numerics;
using System.Text.RegularExpressions;
using Resonalyze.Dsp;
using Xunit.Abstractions;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverJunctionTuneReportTests(ITestOutputHelper output)
{
    private const int SampleRate = 48_000;

    /// About 118 characters of Consolas 9 fit the designed pane.
    private const int Columns = 100;

    /// <summary>Lines the pane shows without scrolling at the designed size.</summary>
    private const int PaneLines = 16;

    // Each tuner run is shared by the class; every test only reads its plan and result.
    private static readonly Lazy<(JunctionTunePlan Plan, JunctionTuneResult Result)> NoGoal =
        new(() => Tune(acoustic: null));

    private static readonly Lazy<(JunctionTunePlan Plan, JunctionTuneResult Result)> LinkwitzRiley24Goal =
        new(() => Tune(new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24)));

    [Fact]
    [Trait("Category", "Slow")]
    public void TheReportReadsAsColumns_AndFitsThePane()
    {
        (JunctionTunePlan plan, JunctionTuneResult result) = LinkwitzRiley24Goal.Value;

        List<string> report = VirtualCrossoverJunctionTuneReport.Build(plan, result)
            .Select(line => line.Text)
            .ToList();
        output.WriteLine(string.Join(Environment.NewLine, report));

        Assert.StartsWith("A/B —", report[0], StringComparison.Ordinal);
        Assert.All(report, line => Assert.True(
            line.Length <= Columns, $"{line.Length} characters: {line}"));
        Assert.True(
            report.Count <= PaneLines,
            $"{report.Count} lines:{Environment.NewLine}{string.Join(Environment.NewLine, report)}");

        int header = report.FindIndex(line => line.Contains("sum loss", StringComparison.Ordinal));
        Assert.True(header > 0, "the readings are a table with a header.");
        string sideRow = report[header + 1];
        Assert.Contains("left", sideRow, StringComparison.Ordinal);
        MatchCollection numbers = Regex.Matches(sideRow, @"-?\d+\.\d+");
        Assert.True(numbers.Count >= 3, sideRow);
        Assert.Equal(
            report[header].IndexOf("ripple, dB", StringComparison.Ordinal) + "ripple, dB".Length,
            numbers[^1].Index + numbers[^1].Length);

        Assert.Contains(report, line => line.Contains("Acoustic Linkwitz-Riley 24", StringComparison.Ordinal));
        Assert.Contains(report, line => line.Contains("nearest any filter", StringComparison.Ordinal));
        Assert.Contains(report, line => line.Contains("the channels fall", StringComparison.Ordinal));
        Assert.DoesNotContain(report, line => line.Contains("candidates", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoGoalStated_TheAcousticBlockIsAbsent()
    {
        (JunctionTunePlan plan, JunctionTuneResult result) = NoGoal.Value;

        List<string> report = VirtualCrossoverJunctionTuneReport.Build(plan, result)
            .Select(line => line.Text)
            .ToList();

        Assert.DoesNotContain(report, line => line.Contains("Acoustic", StringComparison.Ordinal));
        Assert.Contains(report, line => line.Contains("sum loss", StringComparison.Ordinal));
    }

    [Fact]
    public void AReadingThatGotWorse_IsToldApartFromOneThatGotBetter()
    {
        (JunctionTunePlan plan, JunctionTuneResult result) = LinkwitzRiley24Goal.Value;

        List<JunctionTuneLine> report = VirtualCrossoverJunctionTuneReport.Build(plan, result);
        int header = report.FindIndex(line => line.Text.Contains("sum loss", StringComparison.Ordinal));
        JunctionTuneLine row = report[header + 1];

        Assert.Contains("→", row.Text, StringComparison.Ordinal);
        Assert.Equal(7, row.Spans.Count);
        Assert.All(
            row.Spans.Where((_, index) => index is 0 or 1 or 3 or 5),
            span => Assert.Equal(JunctionTuneTone.Plain, span.Tone));
        JunctionTuneSpan ripple = row.Spans[^1];
        Assert.Equal(JunctionTuneTone.Better, ripple.Tone);
        Assert.DoesNotContain("→", ripple.Text, StringComparison.Ordinal);
        Assert.Equal(JunctionTuneTone.Better, report[0].Spans[^1].Tone);
        Assert.All(report[header].Spans, span => Assert.Equal(JunctionTuneTone.Plain, span.Tone));
    }

    [Fact]
    public void AChangeTheGoalPaidForInSum_IsCalledNearerTheGoal_NotBetter()
    {
        (JunctionTunePlan plain, _) = NoGoal.Value;
        JunctionTunePlan plan = plain with
        {
            Options = plain.Options with
            {
                AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24),
                SumSlackDb = 1.0
            }
        };
        CrossoverEdge now = new(CrossoverFilterFamily.Butterworth, 180, 36);
        CrossoverEdge found = new(CrossoverFilterFamily.Butterworth, 230, 18);
        JunctionTuneReading[] sums = [new("left", -0.5, -1.8, 4.4, new JunctionAcousticFit(7.7, 7.7, 34, 32, 21.4))];
        JunctionTuneReading[] lands = [new("left", -0.8, -2.3, 4.9, new JunctionAcousticFit(1.3, 1.3, 31, 26, 21.4))];
        var result = new JunctionTuneResult(
            new JunctionTuneCandidate(now, now, sums, sums, 100, 500),
            new JunctionTuneCandidate(found, found, lands, lands, 100, 500),
            Changed: true,
            [], [], [], 856, 100, 500, [],
            ClosestAcousticCostDb: 1.1,
            BestSumScoreDb: 5.5);

        List<JunctionTuneLine> report = VirtualCrossoverJunctionTuneReport.Build(plan, result);

        Assert.EndsWith("a crossover nearer the acoustic goal was found.", report[0].Text, StringComparison.Ordinal);
        Assert.Contains(report, line => line.Text.Contains("(budget 1.0 dB)", StringComparison.Ordinal));
    }

    [Fact]
    public void AFoundCrossoverShortOfTheMargin_IsAdvisedAgainst_NotCalledUnapplied()
    {
        (JunctionTunePlan plan, _) = NoGoal.Value;
        CrossoverEdge now = new(CrossoverFilterFamily.Butterworth, 180, 36);
        CrossoverEdge foundLow = new(CrossoverFilterFamily.Butterworth, 175, 36);
        CrossoverEdge foundHigh = new(CrossoverFilterFamily.Butterworth, 185, 24);
        JunctionTuneReading[] kept = [new("left", -0.5, -1.8, 4.4)];
        JunctionTuneReading[] found = [new("left", -0.5, -1.6, 4.3)];
        var result = new JunctionTuneResult(
            new JunctionTuneCandidate(now, now, kept, kept, 90, 360),
            new JunctionTuneCandidate(foundLow, foundHigh, found, found, 90, 360),
            Changed: false,
            [], [], [], 661, 90, 360, []);

        List<string> report = VirtualCrossoverJunctionTuneReport.Build(plan, result)
            .Select(line => line.Text)
            .ToList();

        Assert.EndsWith("keeping the crossover on screen is recommended.", report[0], StringComparison.Ordinal);
        string line = Assert.Single(report, line => line.TrimStart().StartsWith("found", StringComparison.Ordinal));
        Assert.Contains("175 Hz", line, StringComparison.Ordinal);
        Assert.Contains("not worth it", line, StringComparison.Ordinal);
        Assert.DoesNotContain(report, text => text.Contains("NOT applied", StringComparison.Ordinal));
    }
    [Fact]
    public void AGoalTheCrossoverMisses_IsStillWritten_AndTheReportSaysWhatThatMeans()
    {
        (JunctionTunePlan plain, _) = NoGoal.Value;
        JunctionTunePlan plan = plain with
        {
            Options = plain.Options with
            {
                AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.Butterworth, 24)
            }
        };
        CrossoverEdge now = new(CrossoverFilterFamily.Butterworth, 216, 24);
        CrossoverEdge foundLow = new(CrossoverFilterFamily.Butterworth, 211, 30);
        CrossoverEdge foundHigh = new(CrossoverFilterFamily.Butterworth, 251, 36);
        JunctionTuneReading[] before = [new("left", -0.9, -4.3, 3.4, new JunctionAcousticFit(6.0, 6.0, 45, 52, 21.5))];
        JunctionTuneReading[] after = [new("left", -0.6, -1.8, 2.8, new JunctionAcousticFit(4.6, 4.6, 42, 51, 21.5))];
        var result = new JunctionTuneResult(
            new JunctionTuneCandidate(now, now, before, before, 100, 500),
            new JunctionTuneCandidate(foundLow, foundHigh, after, after, 100, 500),
            Changed: true,
            [], [], [], 857, 100, 500,
            [new JunctionDriverSlopes("left", 14.1, 16.4)],
            ClosestAcousticCostDb: 1.3,
            BestSumScoreDb: 3.5);

        List<JunctionTuneLine> report = VirtualCrossoverJunctionTuneReport.Build(plan, result);

        JunctionTuneLine verdict = report[^1];
        Assert.Contains("Apply writes it anyway", verdict.Text, StringComparison.Ordinal);
        Assert.Contains(
            report,
            line => line.Text.Contains("it costs 0.5 dB of summation score", StringComparison.Ordinal) &&
                line.Text.Contains("(budget 0.2 dB)", StringComparison.Ordinal));
        Assert.Equal(JunctionTuneTone.Worse, verdict.Spans[^1].Tone);
        Assert.All(report, line => Assert.True(
            line.Text.Length <= Columns, $"{line.Text.Length} characters: {line.Text}"));
    }

    [Fact]
    public void WithTwoSides_TheWorstChannelDecides_AndEverySideIsShown()
    {
        (JunctionTunePlan plan, JunctionTuneResult result) = TwoSides(rightTweeterFallsDbPerOctave: 9.0, closestDb: 1.0);

        List<JunctionTuneLine> report = VirtualCrossoverJunctionTuneReport.Build(plan, result);
        List<string> text = report.Select(line => line.Text).ToList();
        output.WriteLine(string.Join(Environment.NewLine, text));

        Assert.Contains(text, line => line.Contains("off by 3.7 dB at worst (right B), 1.1 on average.", StringComparison.Ordinal));
        Assert.Contains(text, line => line.TrimStart().StartsWith("left", StringComparison.Ordinal) && line.Contains("got", StringComparison.Ordinal));
        Assert.Contains(text, line => line.TrimStart().StartsWith("right", StringComparison.Ordinal) && line.Contains("got", StringComparison.Ordinal));
        Assert.Contains(text, line => line.Contains("landing right B on it takes about 15.0 dB/oct", StringComparison.Ordinal));
        Assert.Contains(text, line => line.Contains("one shift of B for both sides", StringComparison.Ordinal) &&
            line.Contains("+0.20 ms", StringComparison.Ordinal));
        JunctionTuneLine verdict = report[^1];
        Assert.Contains("Apply writes it anyway", verdict.Text, StringComparison.Ordinal);
        Assert.Contains("a filter that lands on it sums worse.", verdict.Text, StringComparison.Ordinal);
        Assert.Equal(JunctionTuneTone.Worse, verdict.Spans[^1].Tone);
        Assert.All(text, line => Assert.True(line.Length <= Columns, $"{line.Length} characters: {line}"));
        Assert.True(report.Count <= PaneLines, $"{report.Count} lines.");
    }

    [Fact]
    public void AChannelFallingFasterThanAskedByItself_IsNamed_AndSoIsASearchTooNarrow()
    {
        (JunctionTunePlan steep, JunctionTuneResult tooSteep) = TwoSides(rightTweeterFallsDbPerOctave: 30.0, closestDb: 3.0);
        (JunctionTunePlan narrow, JunctionTuneResult notFound) = TwoSides(rightTweeterFallsDbPerOctave: 9.0, closestDb: 3.0);

        string driver = VirtualCrossoverJunctionTuneReport.Build(steep, tooSteep)[^1].Text;
        string search = VirtualCrossoverJunctionTuneReport.Build(narrow, notFound)[^1].Text;

        Assert.Contains("right B alone already falls faster.", driver, StringComparison.Ordinal);
        Assert.Contains("no filter in this search lands on it.", search, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void AChannelNotRead_IsNamed_AndTheGoalIsNotCalledLanded()
    {
        (JunctionTunePlan plan, JunctionTuneResult result) = TwoSides(rightTweeterFallsDbPerOctave: 9.0, closestDb: null);
        JunctionTuneReading[] halfRead =
        [
            new("left", -0.3, -1.0, 2.0, new JunctionAcousticFit(0.2, 0.2, 25, 24, 24)),
            new("right", -0.4, -1.2, 2.1, new JunctionAcousticFit(0.3, null, 25, null, 24))
        ];
        var candidate = result.Current with { Sides = halfRead, RankingSides = halfRead };

        List<JunctionTuneLine> report = VirtualCrossoverJunctionTuneReport.Build(
            plan, result with { Current = candidate, Best = candidate });

        Assert.Contains(report, line => line.Text.Contains("right B not read.", StringComparison.Ordinal));
        Assert.Contains(report, line => line.Text.Contains("not enough data", StringComparison.Ordinal));
        Assert.DoesNotContain(report, line => line.Text.Contains("OUT OF REACH", StringComparison.Ordinal));
        Assert.Contains("right B could not be read against it.", report[^1].Text, StringComparison.Ordinal);
        Assert.Equal(JunctionTuneTone.Worse, report[^1].Spans[^1].Tone);
    }

    private static (JunctionTunePlan Plan, JunctionTuneResult Result) TwoSides(
        double rightTweeterFallsDbPerOctave, double? closestDb)
    {
        (JunctionTunePlan plain, _) = NoGoal.Value;
        JunctionTunePlan plan = plain with
        {
            Options = plain.Options with
            {
                AcousticTarget = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24),
                OneAlignmentForAllSides = true
            }
        };
        CrossoverEdge edge = new(CrossoverFilterFamily.LinkwitzRiley, 1_000, 24);
        JunctionTuneReading[] sides =
        [
            new("left", -0.3, -1.0, 2.0, new JunctionAcousticFit(0.2, 0.2, 25, 24, 24)),
            new("right", -0.4, -1.2, 2.1, new JunctionAcousticFit(0.3, 3.7, 25, 33, 24))
        ];
        var candidate = new JunctionTuneCandidate(edge, edge, sides, sides, 500, 2_000);
        var result = new JunctionTuneResult(
            candidate,
            candidate,
            Changed: false,
            [],
            [
                new JunctionTuneAlignment("left", 0.2, false, -0.3, -1.0),
                new JunctionTuneAlignment("right", 0.2, false, -0.4, -1.2)
            ],
            [],
            100,
            500,
            2_000,
            [
                new JunctionDriverSlopes("left", 1.0, 2.0),
                new JunctionDriverSlopes("right", 1.0, rightTweeterFallsDbPerOctave)
            ],
            ClosestAcousticCostDb: closestDb);
        return (plan, result);
    }

    private static (JunctionTunePlan Plan, JunctionTuneResult Result) Tune(JunctionAcousticTarget? acoustic)
    {
        CrossoverEdge lr = new(CrossoverFilterFamily.LinkwitzRiley, 1_000, 48);
        var side = new JunctionTuneSide(
            "left",
            Impulse(),
            new DspChannelChain(Crossover: new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: lr)),
            Impulse(),
            new DspChannelChain(Crossover: new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: lr)),
            SampleRate);
        var options = new JunctionTuneOptions(
            [CrossoverFilterFamily.LinkwitzRiley],
            [24, 48],
            950,
            1_050,
            IndependentSlopes: false,
            SampleRate,
            AcousticTarget: acoustic);
        return (
            new JunctionTunePlan(
                "Junction tune A/B",
                new VirtualCrossoverChannel("A"),
                new VirtualCrossoverChannel("B"),
                [side],
                options),
            CrossoverJunctionTuner.Tune([side], options));
    }

    private static Complex[] Impulse()
    {
        var impulse = new Complex[16_384];
        impulse[480] = 1;
        return impulse;
    }
}
