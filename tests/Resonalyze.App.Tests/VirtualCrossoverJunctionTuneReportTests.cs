using System.Numerics;
using System.Text.RegularExpressions;
using Resonalyze.Dsp;
using Xunit.Abstractions;

namespace Resonalyze.App.Tests;

/// <summary>The dialog's report is read in a monospace pane, so its shape is part of the feature: columns that line
/// up, no line wider than the pane, and the acoustic block only where a goal was stated.</summary>
public sealed class VirtualCrossoverJunctionTuneReportTests(ITestOutputHelper output)
{
    private const int SampleRate = 48_000;

    /// <summary>Fits the designed pane (860 px of Consolas 9 is about 118 characters) with room to spare.</summary>
    private const int Columns = 100;

    [Fact]
    public void TheReportReadsAsColumns_AndFitsThePane()
    {
        (JunctionTunePlan plan, JunctionTuneResult result) = Tune(
            new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24));

        List<string> report = VirtualCrossoverJunctionTuneReport.Build(plan, result)
            .Select(line => line.Text)
            .ToList();
        output.WriteLine(string.Join(Environment.NewLine, report));

        Assert.StartsWith("A/B —", report[0], StringComparison.Ordinal);
        Assert.All(report, line => Assert.True(
            line.Length <= Columns, $"{line.Length} characters: {line}"));
        // It has to be readable at a glance and fit the pane without scrolling; a wall of text is not a report.
        Assert.True(
            report.Count <= VirtualCrossoverJunctionTuneReport.PaneLines,
            $"{report.Count} lines:{Environment.NewLine}{string.Join(Environment.NewLine, report)}");

        // The per-side table: a header, then one row per side, with the numbers under their own headings.
        int header = report.FindIndex(line => line.Contains("sum loss", StringComparison.Ordinal));
        Assert.True(header > 0, "the readings are a table with a header.");
        string sideRow = report[header + 1];
        Assert.Contains("left", sideRow, StringComparison.Ordinal);
        // Right-aligned under its own heading: the ripple figure ends where the word "ripple, dB" ends.
        MatchCollection numbers = Regex.Matches(sideRow, @"-?\d+\.\d+");
        Assert.True(numbers.Count >= 3, sideRow);
        Assert.Equal(
            report[header].IndexOf("ripple, dB", StringComparison.Ordinal) + "ripple, dB".Length,
            numbers[^1].Index + numbers[^1].Length);

        // The goal in three lines: how far off, what it came to, and whether it travels to the EQ stage.
        Assert.Contains(report, line => line.Contains("Acoustic Linkwitz-Riley 24", StringComparison.Ordinal));
        Assert.Contains(report, line => line.Contains("nearest any filter", StringComparison.Ordinal));
        Assert.Contains(report, line => line.Contains("the channels fall", StringComparison.Ordinal));
        // The search's extent is the status line's business, not the pane's.
        Assert.DoesNotContain(report, line => line.Contains("candidates", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoGoalStated_TheAcousticBlockIsAbsent()
    {
        (JunctionTunePlan plan, JunctionTuneResult result) = Tune(acoustic: null);

        List<string> report = VirtualCrossoverJunctionTuneReport.Build(plan, result)
            .Select(line => line.Text)
            .ToList();

        Assert.DoesNotContain(report, line => line.Contains("Acoustic", StringComparison.Ordinal));
        Assert.Contains(report, line => line.Contains("sum loss", StringComparison.Ordinal));
    }

    [Fact]
    public void AReadingThatGotWorse_IsToldApartFromOneThatGotBetter()
    {
        // The colour IS the reading for most people: green where the answer improves a figure, red where it costs
        // one, and nothing where it did not move. Only the second half of a "now→best" cell carries it.
        (JunctionTunePlan plan, JunctionTuneResult result) = Tune(
            new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24));

        List<JunctionTuneLine> report = VirtualCrossoverJunctionTuneReport.Build(plan, result);
        int header = report.FindIndex(line => line.Text.Contains("sum loss", StringComparison.Ordinal));
        JunctionTuneLine row = report[header + 1];

        Assert.Contains("→", row.Text, StringComparison.Ordinal);
        // The side name, then three cells of a plain lead-in and the figure it leads to.
        Assert.Equal(7, row.Spans.Count);
        Assert.All(
            row.Spans.Where((_, index) => index is 0 or 1 or 3 or 5),
            span => Assert.Equal(JunctionTuneTone.Plain, span.Tone));
        // Ripple falls from 0.2 to 0.0 here, which is an improvement, and it is the figure that says so.
        JunctionTuneSpan ripple = row.Spans[^1];
        Assert.Equal(JunctionTuneTone.Better, ripple.Tone);
        Assert.DoesNotContain("→", ripple.Text, StringComparison.Ordinal);
        // The verdict carries a tone too, and the table header never does.
        Assert.Equal(JunctionTuneTone.Better, report[0].Spans[^1].Tone);
        Assert.All(report[header].Spans, span => Assert.Equal(JunctionTuneTone.Plain, span.Tone));
    }

    [Fact]
    public void AChangeTheGoalPaidForInSum_IsCalledNearerTheGoal_NotBetter()
    {
        // The budget lets a slope that lands on the goal replace a crossover that sums better; the headline must
        // say which way it is better, or it contradicts the table right under it.
        (JunctionTunePlan plain, _) = Tune(acoustic: null);
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
        JunctionTuneReading[] sums = [new("left", -0.5, -1.8, 4.4, new JunctionAcousticFit(7.7, -7.7, 34, 32, 21.4))];
        JunctionTuneReading[] lands = [new("left", -0.8, -2.3, 4.9, new JunctionAcousticFit(1.3, 0.5, 31, 26, 21.4))];
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
        // Apply writes the found crossover whether or not it cleared the keep margin, so the report may advise
        // against it but must not say it will not be applied: that told the user Apply would do something it
        // then did not.
        (JunctionTunePlan plan, _) = Tune(acoustic: null);
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
        // The goal is the user's statement and Apply writes it; what the report owes the reader is that Auto
        // Tune will then aim at a slope the filter does not make. It says so in one line that fits the pane.
        (JunctionTunePlan plain, _) = Tune(acoustic: null);
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
        JunctionTuneReading[] before = [new("left", -0.9, -4.3, 3.4, new JunctionAcousticFit(6.0, -6, 45, 52, 21.5))];
        JunctionTuneReading[] after = [new("left", -0.6, -1.8, 2.8, new JunctionAcousticFit(4.6, -4.6, 42, 51, 21.5))];
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
        // And what the goal was paid for with, against the budget it was allowed: the found crossover scores
        // 4.0 against the best sum's 3.5.
        Assert.Contains(
            report,
            line => line.Text.Contains("it costs 0.5 dB of summation score", StringComparison.Ordinal) &&
                line.Text.Contains("(budget 0.2 dB)", StringComparison.Ordinal));
        Assert.Equal(JunctionTuneTone.Worse, verdict.Spans[^1].Tone);
        Assert.All(report, line => Assert.True(
            line.Text.Length <= Columns, $"{line.Text.Length} characters: {line.Text}"));
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
