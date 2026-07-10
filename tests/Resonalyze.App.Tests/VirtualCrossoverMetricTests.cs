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
    public void FormatStereoDeltasCompact_ListsPerChannelDeltasAndDashesInvalidOnes()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasCompact(
            [
                new VirtualCrossoverMetric.StereoDelta("B", 0.25, 175, 1_300),
                new VirtualCrossoverMetric.StereoDelta("C", -0.07, 1_800, 20_000),
                new VirtualCrossoverMetric.StereoDelta("D", null, 1_800, 20_000)
            ]);

            Assert.Equal(
                "\u0394 L\u2212R (ms)\r\n" +
                "B      +0.25\r\n" +
                "C      -0.07\r\n" +
                "D          \u2014",
                text);
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
    public void FormatStereoDeltasDetail_ExplainsTheSignAndIncludesBands()
    {
        RunWithInvariantCulture(() =>
        {
            string text = VirtualCrossoverMetric.FormatStereoDeltasDetail(
            [
                new VirtualCrossoverMetric.StereoDelta("B", 0.253, 175, 1_300),
                new VirtualCrossoverMetric.StereoDelta("D", null, 1_800, 20_000)
            ]);

            Assert.Contains("positive: right leads", text);
            Assert.Contains("B: +0.253 ms (175 Hz \u2013 1.3 kHz)", text);
            Assert.Contains("D: \u2014 (no measurable arrival)", text);
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
