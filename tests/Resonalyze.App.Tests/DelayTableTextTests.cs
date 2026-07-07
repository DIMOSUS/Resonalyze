using System.Globalization;

namespace Resonalyze.App.Tests;

public sealed class DelayTableTextTests
{
    [Fact]
    public void FormatLine_PadsToTheColumnLayout()
    {
        string line = DelayTableText.FormatLine("ms", "1.006", "2.345");

        Assert.Equal("ms", line[..2]);
        Assert.Equal("1.006", line[DelayTableText.FirstColumn..].TrimEnd()[..5]);
        Assert.Equal("2.345", line[DelayTableText.SecondColumn..]);
    }

    [Fact]
    public void GetValue_ReadsBothColumnsAndStripsDeltaSuffix()
    {
        RunWithInvariantCulture(() =>
        {
            string line = DelayTableText.FormatLine(
                "ms",
                DelayTableText.FormatValueWithDelta(1.006, 0.996, "0.000"),
                DelayTableText.FormatValueWithDelta(2.345, null, "0.000"));

            Assert.Equal("1.006", DelayTableText.GetValue(line, DelayTableText.FirstColumn));
            Assert.Equal("2.345", DelayTableText.GetValue(line, DelayTableText.SecondColumn));
        });
    }

    [Fact]
    public void GetValue_ShortLineYieldsEmpty()
    {
        Assert.Equal(
            string.Empty,
            DelayTableText.GetValue("ms", DelayTableText.FirstColumn));
    }

    [Fact]
    public void FormatValueWithDelta_SignsTheDelta()
    {
        RunWithInvariantCulture(() =>
        {
            Assert.Equal(
                "1.006 (+0.010)",
                DelayTableText.FormatValueWithDelta(1.006, 0.996, "0.000"));
            Assert.Equal(
                "0.996 (-0.010)",
                DelayTableText.FormatValueWithDelta(0.996, 1.006, "0.000"));
        });
    }

    [Fact]
    public void FormatValueWithDelta_TinyNegativeDeltaReadsPlusZero()
    {
        RunWithInvariantCulture(() =>
        {
            // A delta that rounds to zero must read "+0.000", not "-0.000".
            Assert.Equal(
                "1.000 (+0.000)",
                DelayTableText.FormatValueWithDelta(1.0, 1.0 + 1e-7, "0.000"));
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
