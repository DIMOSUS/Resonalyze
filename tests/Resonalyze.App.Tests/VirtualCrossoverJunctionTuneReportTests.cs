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

        List<string> report = VirtualCrossoverJunctionTuneReport.Build(plan, result);
        output.WriteLine(string.Join(Environment.NewLine, report));

        Assert.StartsWith("Junction A/B —", report[0], StringComparison.Ordinal);
        Assert.All(report, line => Assert.True(
            line.Length <= Columns, $"{line.Length} characters: {line}"));

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
            numbers[2].Index + numbers[2].Length);

        // The goal's own block, with the four comparable slopes on one line.
        Assert.Contains(report, line => line.Contains("Acoustic goal: Linkwitz-Riley 24", StringComparison.Ordinal));
        Assert.Contains(report, line => line.Contains("nearest any filter", StringComparison.Ordinal));
        Assert.Contains(report, line => line.Contains("channels alone", StringComparison.Ordinal));
        Assert.Contains(report, line => line.Contains("searched", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoGoalStated_TheAcousticBlockIsAbsent()
    {
        (JunctionTunePlan plan, JunctionTuneResult result) = Tune(acoustic: null);

        List<string> report = VirtualCrossoverJunctionTuneReport.Build(plan, result);

        Assert.DoesNotContain(report, line => line.Contains("Acoustic", StringComparison.Ordinal));
        Assert.Contains(report, line => line.Contains("sum loss", StringComparison.Ordinal));
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
