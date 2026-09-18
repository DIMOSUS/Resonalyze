using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <remarks>Moving-mic sets are all-or-nothing (one analyzer session); an array is levelled by the IRs' own loopback, so a sub without one is legitimate.</remarks>
public sealed class VirtualCrossoverArrayFallbackTests
{
    private const int Points = 64;

    private static IReadOnlyList<double> Frequencies() =>
        EqualizationCurve.LogFrequencyGrid(20, 20_000, Points);

    private static AnalysisCurve Reference(double db) =>
        new(
            "raw",
            Frequencies().Select(frequency => new SignalPoint(frequency, db)).ToList());

    private static LiveCaptureDocument Capture(double db, SpatialAverageMethod method)
    {
        IReadOnlyList<double> grid = Frequencies();
        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = $"{db:0} dB",
            Method = method,
            CurveDb = grid.Select(_ => db).ToArray(),
            GridStartHz = grid[0],
            GridStopHz = grid[^1],
            Recipe = new LiveCaptureRecipe { SampleRateHz = 48_000 }
        };
    }

    private static VirtualCrossoverHybrid Reader(VirtualCrossoverSpatialAverageMode mode) =>
        new(new VirtualCrossoverSession
        {
            Project = new VirtualCrossoverProjectFile { SpatialAverageMode = mode }
        });

    private static HybridMagnitudes? Build(
        VirtualCrossoverHybrid reader,
        IReadOnlyList<VirtualCrossoverChannel> channels,
        IReadOnlyList<AnalysisCurve> references)
    {
        List<ProcessedChannel> processed = channels
            .Select(channel => new ProcessedChannel(
                channel,
                [],
                PeakIndex: 0,
                SampleRate: 48_000,
                OxyPlot.OxyColors.White))
            .ToList();
        return reader.Build(processed, references, rightSide: false, smoothingCode: 0);
    }

    private static VirtualCrossoverChannel Channel(string name, LiveCaptureDocument? array)
    {
        var channel = new VirtualCrossoverChannel(name);
        foreach (bool side in new[] { false, true })
        {
            VirtualCrossoverChannelState state = channel.PhysicalSideState(side);
            state.SampleRate = 48_000;
            state.ArrayCapture = array;
            // The offset datum is read on the measurements, so without a real IR the test passes for the wrong reason.
            var impulse = new System.Numerics.Complex[4_096];
            impulse[64] = System.Numerics.Complex.One;
            state.TransferImpulseResponse = impulse;
            state.TransferPeakIndex = 64;
        }

        return channel;
    }

    [Fact]
    public void AChannelWithoutAnArrayIsDrawnFromItsOwnMeasurementAndMarked()
    {
        VirtualCrossoverHybrid reader = Reader(VirtualCrossoverSpatialAverageMode.MicArray);
        var channels = new[]
        {
            Channel("mid", Capture(-20, SpatialAverageMethod.MicArray)),
            Channel("sub", array: null)
        };
        AnalysisCurve[] references = [Reference(-24), Reference(-30)];

        HybridMagnitudes? hybrid = Build(reader, channels, references);

        Assert.NotNull(hybrid);
        Assert.Equal([false, true], hybrid!.PointMeasuredChannels);
        Assert.Equal(1, hybrid.PointMeasuredCount);

        // Set curves are held without the offset, so the fallback curve arrives pre-subtracted.
        double drawn = hybrid.Channels[1][Points / 2].Y + hybrid.OffsetDb;
        Assert.Equal(-30, drawn, 6);
    }

    [Fact]
    public void AChannelWithoutAnArrayContributesNoOffsetDatum()
    {
        VirtualCrossoverHybrid reader = Reader(VirtualCrossoverSpatialAverageMode.MicArray);
        var channels = new[]
        {
            Channel("mid", Capture(-20, SpatialAverageMethod.MicArray)),
            Channel("sub", array: null)
        };
        AnalysisCurve[] references = [Reference(-24), Reference(-30)];

        HybridMagnitudes? hybrid = Build(reader, channels, references);

        Assert.NotNull(hybrid!.ChannelOffsetsDb[0]);
        Assert.Null(hybrid.ChannelOffsetsDb[1]);
    }

    [Fact]
    public void AMovingMicSetStillRefusesAChannelWithoutOne()
    {
        VirtualCrossoverHybrid reader = Reader(VirtualCrossoverSpatialAverageMode.MovingMic);
        var channels = new[]
        {
            Channel("mid", array: null),
            Channel("sub", array: null)
        };
        channels[0].PhysicalSideState(false).SpatialAverage =
            Capture(-20, SpatialAverageMethod.MovingMic);
        AnalysisCurve[] references = [Reference(-24), Reference(-30)];

        Assert.Null(Build(reader, channels, references));
    }

    [Fact]
    public void EveryChannelWithAnArrayIsMarkedAsMeasured()
    {
        VirtualCrossoverHybrid reader = Reader(VirtualCrossoverSpatialAverageMode.MicArray);
        var channels = new[]
        {
            Channel("mid", Capture(-20, SpatialAverageMethod.MicArray)),
            Channel("tweeter", Capture(-22, SpatialAverageMethod.MicArray))
        };
        AnalysisCurve[] references = [Reference(-24), Reference(-26)];

        HybridMagnitudes? hybrid = Build(reader, channels, references);

        Assert.NotNull(hybrid);
        Assert.Equal(0, hybrid!.PointMeasuredCount);
    }
}
