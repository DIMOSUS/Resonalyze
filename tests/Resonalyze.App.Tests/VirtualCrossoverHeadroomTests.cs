using System.Numerics;
using OxyPlot;
using Resonalyze.Ui;
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

        HeadroomRow row = Assert.Single(VirtualCrossoverHeadroom.Read(new ChainHeadroomReader(), VirtualCrossoverHeadroom.Capture([channel])));

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
        var reader = new ChainHeadroomReader();
        VirtualCrossoverHeadroom.Read(reader, VirtualCrossoverHeadroom.Capture([channel]));

        channel.Pair.Left.PeqBands = [new PeqBand(500, 2, 1)];
        HeadroomRow band = Assert.Single(VirtualCrossoverHeadroom.Read(reader, VirtualCrossoverHeadroom.Capture([channel])));
        channel.Pair.Left.PeqPreampDb = -2;
        HeadroomRow preamp = Assert.Single(VirtualCrossoverHeadroom.Read(reader, VirtualCrossoverHeadroom.Capture([channel])));
        channel.Pair.Left.DelayMs = 3;
        HeadroomRow delayed = Assert.Single(VirtualCrossoverHeadroom.Read(reader, VirtualCrossoverHeadroom.Capture([channel])));

        Assert.Equal(-1, band.Left!.Value.HeadroomDb, 3);
        Assert.Equal(1, preamp.Left!.Value.HeadroomDb, 3);
        Assert.Equal(1, delayed.Left!.Value.HeadroomDb, 3);
    }

    [Fact]
    public void Capture_KeepsEveryEnabledBlock_MeasuredOrNot_AndReadsAMonoBlockOnce()
    {
        VirtualCrossoverChannel disabled = Channel("A");
        disabled.Pair.Enabled = false;
        VirtualCrossoverChannel unmeasured = Channel("B", measuredLeft: false, measuredRight: false);
        VirtualCrossoverChannel halfMeasured = Channel("C", measuredRight: false);
        VirtualCrossoverChannel mono = Channel("D");
        mono.Pair.Mono = true;
        mono.Pair.Left.GainDb = 4;
        mono.Pair.Right.GainDb = -20;

        List<HeadroomRow> rows = VirtualCrossoverHeadroom.Read(
            new ChainHeadroomReader(),
            VirtualCrossoverHeadroom.Capture([disabled, unmeasured, halfMeasured, mono]));

        Assert.Equal(["B", "C", "D"], rows.Select(row => row.Channel));
        Assert.Null(rows[0].Left);
        Assert.Null(rows[0].Right);
        Assert.NotNull(rows[1].Left);
        Assert.Null(rows[1].Right);
        Assert.True(rows[2].Mono);
        Assert.Equal(-4, rows[2].Left!.Value.HeadroomDb, 3);
        Assert.Null(rows[2].Right);
    }

    [Fact]
    public void AnUnmeasuredBlock_ReadsAsDashes_InTheColumnAndTheTooltip()
    {
        var rows = new List<HeadroomRow> { new("B", false, null, null), new("E", true, null, null) };

        List<ToneLine> lines = VirtualCrossoverHeadroom.FormatCompact(rows);
        string[] detail = VirtualCrossoverHeadroom.FormatDetail(rows).Split("\r\n");

        Assert.All(
            lines.Where(line => line.Text.StartsWith('B') || line.Text.StartsWith('E')),
            line => Assert.Contains("—", line.Text));
        Assert.Contains("—", Assert.Single(detail, line => line.StartsWith("B:")));
        string e = Assert.Single(detail, line => line.StartsWith("E:"));
        Assert.Contains("—", e);
        Assert.DoesNotContain("bypass", e);
    }

    [Fact]
    public async Task AFirSide_IsPendingUntilTheReaderLandsIt_AndNeverColoursAsAClip()
    {
        VirtualCrossoverChannel channel = Channel("A", measuredRight: false);
        double[] kernel = new double[64];
        kernel[32] = 2;
        channel.Pair.Left.Fir = new FirFilter(kernel);
        var reader = new ChainHeadroomReader();

        HeadroomReading pending = Assert.Single(
            VirtualCrossoverHeadroom.Read(reader, VirtualCrossoverHeadroom.Capture([channel]))).Left!.Value;
        await reader.FillAsync(() => { });
        HeadroomReading landed = Assert.Single(
            VirtualCrossoverHeadroom.Read(reader, VirtualCrossoverHeadroom.Capture([channel]))).Left!.Value;

        Assert.True(pending.Pending);
        Assert.False(pending.Clips);
        Assert.False(landed.Pending);
        Assert.Equal(-20 * Math.Log10(2), landed.HeadroomDb, 3);
        Assert.True(landed.Clips);
    }

    [Fact]
    public void Read_ABypassedBlockHasNoChainGain()
    {
        VirtualCrossoverChannel channel = Channel("A");
        channel.Pair.Bypass = true;
        channel.Pair.Left.GainDb = 12;

        HeadroomRow row = Assert.Single(VirtualCrossoverHeadroom.Read(new ChainHeadroomReader(), VirtualCrossoverHeadroom.Capture([channel])));

        Assert.True(row.Left!.Value.Bypass);
        Assert.Equal(0, row.Left.Value.HeadroomDb, 9);
        Assert.False(row.Left.Value.Clips);
    }

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(-0.04, false)]
    [InlineData(-0.05, true)]
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

        string a = Assert.Single(lines, line => line.StartsWith("A:"));
        Assert.Contains("48 Hz", a);
        Assert.DoesNotContain("L ", a);
        string b = Assert.Single(lines, line => line.StartsWith("B:"));
        Assert.True(b.IndexOf("77 Hz") < b.IndexOf("107 Hz"), b);
        Assert.Contains("L", b);
        Assert.Contains("R", b);
        string c = Assert.Single(lines, line => line.StartsWith("C:"));
        Assert.Contains("bypass", c);
        Assert.DoesNotContain("L ", c);
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
    public void OverUnity_GivesEachRunItsOwnCrossings()
    {
        List<List<DataPoint>> runs = VirtualCrossoverHeadroom.OverUnity(
            [new(100, -1), new(200, 1), new(400, 1), new(800, -3), new(1_600, 2)]);

        // Crossings linear in log frequency: the log midpoint of 100–200 Hz, a quarter of 400–800 Hz, 3/5 of 800–1600 Hz.
        Assert.Equal(2, runs.Count);
        AssertPoints(
            [new(Math.Sqrt(100 * 200), 0), new(200, 1), new(400, 1), new(400 * Math.Pow(2, 0.25), 0)], runs[0]);
        AssertPoints([new(800 * Math.Pow(2, 0.6), 0), new(1_600, 2)], runs[1]);
        Assert.Empty(VirtualCrossoverHeadroom.OverUnity([new(100, -1), new(200, 0)]));
    }

    private static void AssertPoints(DataPoint[] expected, List<DataPoint> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].X, actual[i].X, 6);
            Assert.Equal(expected[i].Y, actual[i].Y, 9);
        }
    }

    [Fact]
    public void Capture_ReadsASideWithoutARateAsUnmeasured()
    {
        VirtualCrossoverChannel channel = Channel("A");
        channel.SideState(rightSide: true).SampleRate = 0;

        HeadroomRow row = Assert.Single(VirtualCrossoverHeadroom.Read(new ChainHeadroomReader(), VirtualCrossoverHeadroom.Capture([channel])));

        Assert.NotNull(row.Left);
        Assert.Null(row.Right);
    }

    [Fact]
    public void ShowLines_KeepsEachSpansColourThroughTheHandlesCreation()
    {
        StaTest.Run(() =>
        {
            using var box = new StatusRichTextBox { ForeColor = UiPalette.Warning };
            ToneLine line = new([new ToneSpan("ab"), new ToneSpan("cd", TextTone.Bad), new ToneSpan("ef", TextTone.Good)]);
            box.ShowLines([line]);
            box.CreateControl();
            _ = box.Handle;

            Assert.Equal("abcdef", box.Text);
            Assert.Equal(
                [UiPalette.Warning.ToArgb(), UiPalette.Error.ToArgb(), UiPalette.Success.ToArgb()],
                new[] { 0, 2, 4 }.Select(start => ColourAt(box, start)));

            box.ShowLines([new ToneLine([new ToneSpan("ab"), new ToneSpan("cd", TextTone.Good), new ToneSpan("ef")])]);
            Assert.Equal(UiPalette.Success.ToArgb(), ColourAt(box, 2));
        });
    }

    private static int ColourAt(StatusRichTextBox box, int start)
    {
        box.Select(start, 2);
        return box.SelectionColor.ToArgb();
    }

    // A 6 Hz FIR lobe over a -6 dB floor: no point of the plot's grid rises above 0 dB, only the peak the headroom found.
    [Fact]
    public void TheChainPlotFillsAFirLobeTheGridMisses_OnceItIsGivenThePeak()
    {
        const int rate = 48_000;
        double[] kernel = new double[16_384];
        for (int n = 0; n < kernel.Length; n++)
        {
            kernel[n] = 2.0 / kernel.Length * Math.Cos(2 * Math.PI * 4_321.7 / rate * n);
        }

        kernel[0] += 0.5;
        var chain = new DspChannelChain(Fir: new FirFilter(kernel));
        (double peakHz, _) = DspChainPeak.InAudioBand(chain, rate);

        List<OxyPlot.Series.AreaSeries> Fills(double? peak)
        {
            using var plotView = new OxyPlot.WindowsForms.PlotView();
            var plot = new VirtualCrossoverDspChainPlot(plotView, DspPlotMode.Magnitude);
            plot.Draw(DspPlotMode.Magnitude, [new DspChainCurve("A filter", chain, rate, OxyColors.Red, peak)]);
            return [.. ((PlotModel)plotView.Model).Series.OfType<OxyPlot.Series.AreaSeries>()];
        }

        Assert.Empty(Fills(null));
        // The lobe's own width: its first nulls sit rate / taps either side of the peak, the grid's points 59 Hz apart.
        OxyPlot.Series.AreaSeries fill = Assert.Single(Fills(peakHz));
        double lobeHz = (double)rate / kernel.Length;
        Assert.InRange(fill.Points[0].X, peakHz - lobeHz, peakHz);
        Assert.InRange(fill.Points[^1].X, peakHz, peakHz + lobeHz);
    }

    [Fact]
    public async Task EachFirChainLandsAsSoonAsItIsRead()
    {
        var reader = new ChainHeadroomReader();
        DspChainResponseKey[] keys = [FirKey(-1), FirKey(-2)];
        int landings = 0;

        Assert.All(keys, key => Assert.Null(reader.Peak(key)));
        await reader.FillAsync(() => landings++);

        Assert.Equal(2, landings);
        Assert.All(keys, key => Assert.NotNull(reader.KnownPeak(key)));
    }

    private static DspChainResponseKey FirKey(double gainDb)
    {
        double[] kernel = new double[64];
        kernel[32] = 1;
        return new DspChainResponseKey(new DspChannelChain(GainDb: gainDb, Fir: new FirFilter(kernel)), 48_000);
    }
}
