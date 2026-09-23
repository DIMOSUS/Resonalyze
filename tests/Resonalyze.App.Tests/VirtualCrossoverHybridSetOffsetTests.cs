using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverHybridSetOffsetTests
{
    private const int SampleRate = 48_000;
    private static readonly Guid OneSession = Guid.NewGuid();

    [Fact]
    public void AnArraySet_IsDrawnWithNoOffset_AndItsWorstDatumIsWhatItReportsInstead()
    {
        VirtualCrossoverChannel[] channels =
        [
            Channel("A", SpatialAverageMethod.MicArray, left: -30.0),
            Channel("B", SpatialAverageMethod.MicArray, left: -34.0),
            Channel("C", SpatialAverageMethod.MicArray, left: -41.0)
        ];
        VirtualCrossoverHybrid reader = Reader(VirtualCrossoverSpatialAverageMode.MicArray, channels);

        (double?[] perChannel, double offset, _) =
            reader.ResolveRawOffsetsDb(Processed(channels, rightSide: false), rightSide: false);

        Assert.Equal(0.0, offset);
        var hybrid = new HybridMagnitudes([], [], perChannel, offset);
        Assert.Equal(perChannel.MaxBy(datum => Math.Abs(datum!.Value)), hybrid.WorstDatumDb);
        HybridReadOut readOut = Assert.IsType<HybridReadOut>(reader.ReadOut(hybrid));
        Assert.True(readOut.ArrayStandOff);
        Assert.Equal(hybrid.WorstDatumDb!.Value, readOut.Db);
    }

    [Fact]
    public void AMovingMicSetSharedByBothSides_TakesOneOffset_SoAMonoChannelDoesNotMove()
    {
        VirtualCrossoverChannel sub = Channel("Sub", SpatialAverageMethod.MovingMic, left: -30.0);
        sub.Pair.Mono = true;
        VirtualCrossoverChannel mid = Channel("Mid", SpatialAverageMethod.MovingMic, left: -34.0, right: -45.0);
        VirtualCrossoverHybrid reader = Reader(VirtualCrossoverSpatialAverageMode.MovingMic, [sub, mid]);

        double left = reader.ResolveRawOffsetsDb(Processed([sub, mid], false), rightSide: false).SetOffsetDb;
        double right = reader.ResolveRawOffsetsDb(Processed([sub, mid], true), rightSide: true).SetOffsetDb;

        Assert.Equal(left, right, 9);
    }

    [Fact]
    public void SidesThatAreNotOneSet_EachKeepTheirOwnOffset()
    {
        VirtualCrossoverChannel sub = Channel("Sub", SpatialAverageMethod.MovingMic, left: -30.0);
        sub.Pair.Mono = true;
        VirtualCrossoverChannel mid = Channel("Mid", SpatialAverageMethod.MovingMic, left: -34.0, right: -45.0);
        // Another session at another gain: nothing ties the right capture's level to the left ones.
        mid.PhysicalSideState(true).SpatialAverage!.CaptureSessionId = Guid.NewGuid();
        VirtualCrossoverHybrid reader = Reader(VirtualCrossoverSpatialAverageMode.MovingMic, [sub, mid]);

        double left = reader.ResolveRawOffsetsDb(Processed([sub, mid], false), rightSide: false).SetOffsetDb;
        double right = reader.ResolveRawOffsetsDb(Processed([sub, mid], true), rightSide: true).SetOffsetDb;

        Assert.NotEqual(left, right, 3);
    }

    [Fact]
    public void AnArrayStandingOffItsImpulseResponse_IsFlagged_EvenWhenTheSetAgreesWithItself()
    {
        var session = new VirtualCrossoverSession
        {
            Project = new VirtualCrossoverProjectFile
            {
                SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MicArray
            }
        };
        var warnings = new VirtualCrossoverWarnings(session);

        // All three off by the same amount: no spread, and still a broken set.
        string? common = warnings.DescribeHybridDisagreement(
            new HybridMagnitudes([], [], [-14.0, -14.0, -14.0], 0));
        string? healthy = warnings.DescribeHybridDisagreement(
            new HybridMagnitudes([], [], [-0.9, 0.1, -0.3], 0));

        Assert.NotNull(common);
        Assert.Contains($"{-14.0:+0.0;-0.0} dB off its impulse response", common);
        Assert.Null(healthy);
    }

    private static VirtualCrossoverChannel Channel(
        string name, SpatialAverageMethod method, double left, double? right = null)
    {
        var channel = new VirtualCrossoverChannel(name);
        Attach(channel.PhysicalSideState(false), method, left);
        if (right is { } level)
        {
            Attach(channel.PhysicalSideState(true), method, level);
        }

        return channel;
    }

    private static void Attach(VirtualCrossoverChannelState state, SpatialAverageMethod method, double levelDb)
    {
        var impulse = new Complex[4_096];
        impulse[64] = Complex.One;
        state.TransferImpulseResponse = impulse;
        state.TransferPeakIndex = 64;
        state.SampleRate = SampleRate;
        LiveCaptureDocument capture = Capture(method, levelDb);
        if (method == SpatialAverageMethod.MicArray)
        {
            state.ArrayCapture = capture;
        }
        else
        {
            state.SpatialAverage = capture;
        }
    }

    private static LiveCaptureDocument Capture(SpatialAverageMethod method, double levelDb)
    {
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(200, 15_000, 128);
        return new LiveCaptureDocument
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = $"{levelDb:0} dB",
            Method = method,
            CaptureSessionId = OneSession,
            CurveDb = grid.Select(_ => levelDb).ToArray(),
            GridStartHz = grid[0],
            GridStopHz = grid[^1],
            Recipe = new LiveCaptureRecipe { SampleRateHz = SampleRate }
        };
    }

    private static VirtualCrossoverHybrid Reader(
        VirtualCrossoverSpatialAverageMode mode, IReadOnlyList<VirtualCrossoverChannel> channels)
    {
        var session = new VirtualCrossoverSession
        {
            Project = new VirtualCrossoverProjectFile { SpatialAverageMode = mode },
            MagnitudeGate = MagnitudeGateSnapshot.Initial with { SmoothingInverseOctaves = 0 }
        };
        session.Channels.AddRange(channels);
        return new VirtualCrossoverHybrid(session);
    }

    private static List<ProcessedChannel> Processed(
        IEnumerable<VirtualCrossoverChannel> channels, bool rightSide) =>
        channels
            .Select(channel => new ProcessedChannel(
                channel,
                channel.SideState(rightSide).TransferImpulseResponse!,
                channel.SideState(rightSide).TransferPeakIndex,
                SampleRate,
                OxyColors.White))
            .ToList();
}
