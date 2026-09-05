using System.Globalization;

namespace Resonalyze.App.Tests;

public sealed class DelayTableTextTests
{
    [Fact]
    public void FormatLine_PadsToTheColumnLayout()
    {
        string line = DelayTableText.FormatLine(
            DelayTableText.FirstArrivalLabel, "1.006", "48.3", "0.345");

        Assert.StartsWith(DelayTableText.FirstArrivalLabel, line);
        Assert.Equal("1.006", line[DelayTableText.MillisecondsColumn..].TrimEnd()[..5]);
        Assert.Equal("48.3", line[DelayTableText.SamplesColumn..].TrimEnd()[..4]);
        Assert.Equal("0.345", line[DelayTableText.MetersColumn..]);
    }

    [Fact]
    public void FormatHeader_NamesTheUnitsOverTheirColumns()
    {
        string header = DelayTableText.FormatHeader();

        Assert.StartsWith("Measured delay:", header);
        Assert.StartsWith("ms", header[DelayTableText.MillisecondsColumn..]);
        Assert.StartsWith("samples", header[DelayTableText.SamplesColumn..]);
        Assert.StartsWith("meters", header[DelayTableText.MetersColumn..]);
    }

    [Fact]
    public void GetValue_ReadsEveryColumnAndStripsTheDeltaAndTheMarker()
    {
        RunWithInvariantCulture(() =>
        {
            string line = DelayTableText.FormatLine(
                DelayTableText.EnergyOnsetLabel,
                DelayTableText.FormatValueWithDelta(1.006, 0.996, "0.000"),
                DelayTableText.FormatValueWithDelta(48.3, 47.8, "0.0"),
                DelayTableText.FormatValueWithDelta(0.345, null, "0.000")) +
                DelayTableText.RecommendedMarker;

            Assert.Equal("1.006", DelayTableText.GetValue(line, DelayTableText.MillisecondsColumn));
            Assert.Equal("48.3", DelayTableText.GetValue(line, DelayTableText.SamplesColumn));
            Assert.Equal("0.345", DelayTableText.GetValue(line, DelayTableText.MetersColumn));
        });
    }

    [Fact]
    public void GetValue_ShortLineYieldsEmpty()
    {
        Assert.Equal(
            string.Empty,
            DelayTableText.GetValue("First Arrival", DelayTableText.MillisecondsColumn));
    }

    [Fact]
    public void IsDelayRow_RecognizesTheThreeRowsOnly()
    {
        Assert.True(DelayTableText.IsDelayRow("First Arrival     1.006"));
        Assert.True(DelayTableText.IsDelayRow("Strongest Peak    1.006"));
        Assert.True(DelayTableText.IsDelayRow("Energy onset      1.006"));
        Assert.False(DelayTableText.IsDelayRow(DelayTableText.FormatHeader()));
        Assert.False(DelayTableText.IsDelayRow("Arrival probe: verified"));
    }

    [Fact]
    public void CellAt_MapsAClickColumnToItsCell()
    {
        Assert.Null(DelayTableText.CellAt(3));
        Assert.Equal(DelayTableText.MillisecondsColumn, DelayTableText.CellAt(DelayTableText.MillisecondsColumn + 2));
        Assert.Equal(DelayTableText.SamplesColumn, DelayTableText.CellAt(DelayTableText.SamplesColumn));
        Assert.Equal(DelayTableText.MetersColumn, DelayTableText.CellAt(DelayTableText.MetersColumn + 10));
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
