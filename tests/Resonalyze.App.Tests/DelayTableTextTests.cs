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
    public void AClickAnywhereInACell_ReadsThatCellsNumber()
    {
        string line = DelayTableText.FormatLine(DelayTableText.FirstArrivalLabel, "1.006 (+0.010)", "48.3", "0.345") +
            DelayTableText.RecommendedMarker;

        Assert.Equal(string.Empty, DelayTableText.GetValue(line, 3));
        Assert.Equal("1.006", DelayTableText.GetValue(line, DelayTableText.MillisecondsColumn + 8));
        Assert.Equal("1.006", DelayTableText.GetValue(line, DelayTableText.SamplesColumn - 1));
        Assert.Equal("48.3", DelayTableText.GetValue(line, DelayTableText.SamplesColumn));
        Assert.Equal("0.345", DelayTableText.GetValue(line, line.Length - 1));
    }

    [Fact]
    public void ACellWiderThanItsColumn_MovesTheLaterColumnsInHeaderAndRowsAlike()
    {
        string wideMilliseconds = "-163.000 (-12.604)";
        string wideSamples = "15648.0 (-12345.6)";
        DelayTableText.Columns columns = DelayTableText.Columns.Fit(
            [("1.006", "48.3"), (wideMilliseconds, wideSamples)]);
        string header = DelayTableText.FormatHeader(columns);
        string narrow = DelayTableText.FirstArrivalLabel.PadRight(DelayTableText.MillisecondsColumn) +
            DelayTableText.FormatCells("1.006", "48.3", "0.345", columns);
        string wide = DelayTableText.StrongestPeakLabel.PadRight(DelayTableText.MillisecondsColumn) +
            DelayTableText.FormatCells(wideMilliseconds, wideSamples, "55.912", columns);

        Assert.Equal(header.IndexOf("samples", StringComparison.Ordinal), wide.IndexOf("15648.0", StringComparison.Ordinal));
        Assert.Equal(header.IndexOf("samples", StringComparison.Ordinal), narrow.IndexOf("48.3", StringComparison.Ordinal));
        Assert.Equal(header.IndexOf("meters", StringComparison.Ordinal), wide.IndexOf("55.912", StringComparison.Ordinal));
        Assert.Equal(header.IndexOf("meters", StringComparison.Ordinal), narrow.IndexOf("0.345", StringComparison.Ordinal));
        Assert.Equal("15648.0", DelayTableText.GetValue(wide, columns.Samples));
        Assert.Equal("55.912", DelayTableText.GetValue(wide, columns.Meters));
        Assert.Equal(DelayTableText.Columns.Default, DelayTableText.Columns.Fit([("163.000 (+2.604)", "7824.0 (+125.0)")]));
    }

    [Fact]
    public void CopyableValue_CopiesADelayCellAndNothingElse()
    {
        RunWithInvariantCulture(() =>
        {
            string line = DelayTableText.FormatLine(DelayTableText.FirstArrivalLabel, "1.006 (+0.010)", "48.3", "0.345");

            Assert.Equal("1.006", DelayTableText.CopyableValue(line, DelayTableText.MillisecondsColumn + 3));
            Assert.Equal("0.345", DelayTableText.CopyableValue(line, DelayTableText.MetersColumn));
            Assert.Equal(string.Empty, DelayTableText.CopyableValue(line, 2));
            Assert.Equal(string.Empty, DelayTableText.CopyableValue(DelayTableText.FormatHeader(), DelayTableText.SamplesColumn));
        });
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
