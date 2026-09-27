using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;
using Xunit;

namespace Resonalyze.App.Tests;

public class VirtualCrossoverHeadroomTests
{
    private static VirtualCrossoverChannel Channel(string name, bool measuredLeft = true, bool measuredRight = true)
    {
        var channel = new VirtualCrossoverChannel(name);
        foreach ((bool rightSide, bool measured) in new[] { (false, measuredLeft), (true, measuredRight) })
        {
            if (measured)
            {
                VirtualCrossoverChannelState state = channel.SideState(rightSide);
                state.TransferImpulseResponse = new Complex[64];
                state.SampleRate = 48_000;
            }
        }

        return channel;
    }

    [Fact]
    public void Read_TakesGainAndBoostOnEachSide()
    {
        VirtualCrossoverChannel channel = Channel("A");
        channel.Pair.Left.GainDb = -3;
        channel.Pair.Left.PeqBands = [new PeqBand(2_000, 4, 5)];
        channel.Pair.Right.GainDb = -6;

        HeadroomRow row = Assert.Single(new VirtualCrossoverHeadroom().Read(VirtualCrossoverHeadroom.Capture([channel])));

        Assert.Equal(-2, row.Left!.Value.HeadroomDb, 3);
        Assert.True(row.Left.Value.Clips);
        Assert.Equal(2_000, row.Left.Value.PeakHz, 1);
        Assert.Equal(6, row.Right!.Value.HeadroomDb, 3);
        Assert.False(row.Right.Value.Clips);
    }

    [Fact]
    public void Read_FollowsAnEditOfTheSameChain()
    {
        VirtualCrossoverChannel channel = Channel("A", measuredRight: false);
        channel.Pair.Left.PeqBands = [new PeqBand(500, 2, 3)];
        var reader = new VirtualCrossoverHeadroom();
        reader.Read(VirtualCrossoverHeadroom.Capture([channel]));

        channel.Pair.Left.PeqBands = [new PeqBand(500, 2, 1)];
        HeadroomRow band = Assert.Single(reader.Read(VirtualCrossoverHeadroom.Capture([channel])));
        channel.Pair.Left.PeqPreampDb = -2;
        HeadroomRow preamp = Assert.Single(reader.Read(VirtualCrossoverHeadroom.Capture([channel])));
        channel.Pair.Left.DelayMs = 3;
        HeadroomRow delayed = Assert.Single(reader.Read(VirtualCrossoverHeadroom.Capture([channel])));

        Assert.Equal(-1, band.Left!.Value.HeadroomDb, 3);
        Assert.Equal(1, preamp.Left!.Value.HeadroomDb, 3);
        Assert.Equal(1, delayed.Left!.Value.HeadroomDb, 3);
    }

    [Fact]
    public void Capture_SkipsDisabledAndUnmeasured_AndReadsAMonoBlockOnce()
    {
        VirtualCrossoverChannel disabled = Channel("A");
        disabled.Pair.Enabled = false;
        VirtualCrossoverChannel unmeasured = Channel("B", measuredLeft: false, measuredRight: false);
        VirtualCrossoverChannel halfMeasured = Channel("C", measuredRight: false);
        VirtualCrossoverChannel mono = Channel("D");
        mono.Pair.Mono = true;
        mono.Pair.Left.GainDb = 4;
        mono.Pair.Right.GainDb = -20;

        List<HeadroomRow> rows =
            new VirtualCrossoverHeadroom().Read(VirtualCrossoverHeadroom.Capture([disabled, unmeasured, halfMeasured, mono]));

        Assert.Equal(["C", "D"], rows.Select(row => row.Channel));
        Assert.NotNull(rows[0].Left);
        Assert.Null(rows[0].Right);
        Assert.True(rows[1].Mono);
        Assert.Equal(-4, rows[1].Left!.Value.HeadroomDb, 3);
        Assert.Null(rows[1].Right);
    }

    [Fact]
    public void Read_ABypassedBlockHasNoChainGain()
    {
        VirtualCrossoverChannel channel = Channel("A");
        channel.Pair.Bypass = true;
        channel.Pair.Left.GainDb = 12;

        HeadroomRow row = Assert.Single(new VirtualCrossoverHeadroom().Read(VirtualCrossoverHeadroom.Capture([channel])));

        Assert.True(row.Left!.Value.Bypass);
        Assert.Equal(0, row.Left.Value.HeadroomDb, 9);
        Assert.False(row.Left.Value.Clips);
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(-0.04, false)]
    [InlineData(-0.06, true)]
    [InlineData(3.0, false)]
    public void Clips_OnlyWhenTheShownFigureIsNegative(double headroomDb, bool clips) =>
        Assert.Equal(clips, new HeadroomReading(headroomDb, 1_000, Bypass: false).Clips);

    [Fact]
    public void FormatCompact_ColoursEachSideByItsOwnReading()
    {
        var rows = new List<HeadroomRow>
        {
            new("A", false, new HeadroomReading(-1.5, 100, false), new HeadroomReading(2.0, 100, false)),
            new("B", true, new HeadroomReading(0.0, 100, false), null)
        };

        List<ToneLine> lines = VirtualCrossoverHeadroom.FormatCompact(rows);

        // Spans: name, left cell, right cell.
        ToneSpan[] a = [.. lines.Single(line => line.Text.StartsWith('A')).Spans];
        Assert.Equal([TextTone.Bad, TextTone.Good], a.Skip(1).Select(span => span.Tone));
        ToneSpan[] b = [.. lines.Single(line => line.Text.StartsWith('B')).Spans];
        Assert.Equal(TextTone.Good, b[1].Tone);
        Assert.DoesNotContain(b, span => span.Tone == TextTone.Bad);
    }

    [Fact]
    public void FormatDetail_NamesWhereEachSidePeaks()
    {
        var rows = new List<HeadroomRow>
        {
            new("A", true, new HeadroomReading(1.0, 48, false), null),
            new("B", false, new HeadroomReading(-1.0, 77, false), new HeadroomReading(2.0, 107, false)),
            new("C", false, null, new HeadroomReading(0.0, 20, true))
        };

        string[] lines = VirtualCrossoverHeadroom.FormatDetail(rows).Split("\r\n");

        Assert.Equal(["A: 48 Hz", "B: L 77 Hz, R 107 Hz", "C: R bypassed"], lines.Skip(1));
    }

    [Fact]
    public void FormatReadOut_LeadsWithTheHeadroom()
    {
        var rows = new List<HeadroomRow> { new("A", true, new HeadroomReading(-1.0, 100, false), null) };

        (List<ToneLine> compact, _) = VirtualCrossoverMetric.FormatReadOut(rows, [], false, [], [], [], null);

        int headroomRow = compact.FindIndex(line => line.Spans.Any(span => span.Tone == TextTone.Bad));
        int lossRow = compact.FindIndex(line => line.Text.Contains("Sum loss", StringComparison.Ordinal));
        Assert.InRange(headroomRow, 0, lossRow - 1);
    }

    [Fact]
    public void OverUnity_MeetsTheCurveAtEachCrossing()
    {
        List<DataPoint> over = VirtualCrossoverDspChainPlot.OverUnity(
            [new(100, -1), new(200, 1), new(400, 1), new(800, -3)]);

        // Crossings linear in log frequency: the log midpoint of 100–200 Hz, a quarter of 400–800 Hz.
        Assert.Equal(
            [
                new DataPoint(100, 0), new DataPoint(Math.Sqrt(100 * 200), 0), new DataPoint(200, 1),
                new DataPoint(400, 1), new DataPoint(400 * Math.Pow(2, 0.25), 0), new DataPoint(800, 0)
            ],
            over.Select(point => new DataPoint(Math.Round(point.X, 9), point.Y)),
            (x, y) => Math.Abs(x.X - Math.Round(y.X, 9)) < 1e-6 && x.Y == y.Y);
        Assert.Empty(VirtualCrossoverDspChainPlot.OverUnity([new(100, -1), new(200, 0)]));
    }
}
