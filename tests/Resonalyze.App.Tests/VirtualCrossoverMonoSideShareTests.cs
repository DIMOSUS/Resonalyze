using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverMonoSideShareTests
{
    private const int Arrival = 100;

    [Theory]
    // Fed from both inputs when measured: a side gets half of it, −6.02 dB.
    [InlineData(true, true, 0.5)]
    // Measured from one input: that is what a side plays.
    [InlineData(true, false, 1.0)]
    // A stereo pair has its own measurement per side.
    [InlineData(false, true, 1.0)]
    public void EachSideReadsTheMeasurementAndTheCaptureAtItsShare(bool mono, bool bothInputs, double share)
    {
        var channel = new VirtualCrossoverChannel("A") { Pair = { Mono = mono, MeasuredFromBothInputs = bothInputs } };
        var measured = new Complex[1_024];
        measured[Arrival] = 1.0;
        VirtualCrossoverChannelState state = channel.PhysicalSideState(rightSide: false);
        state.TransferImpulseResponse = measured;
        state.SpatialAverage = Capture(-20.0);

        Assert.Equal(share, state.TransferImpulseResponse![Arrival].Real, 12);
        Assert.Equal(share, state.ProcessingSource!.CroppedImpulseResponse[Arrival].Real, 12);
        LiveCaptureDocument read = state.SpatialAverageFor(VirtualCrossoverSpatialAverageMode.MovingMic)!;
        Assert.All(read.CurveDb, level => Assert.Equal(-20.0 + 20.0 * Math.Log10(share), level, 9));
        Assert.Equal(1.0, measured[Arrival].Real);
        Assert.All(state.SpatialAverage.CurveDb, level => Assert.Equal(-20.0, level));
    }

    [Fact]
    public void TurningTheFlagRereadsTheSource_AndAnUnchangedOneKeepsIt()
    {
        var channel = new VirtualCrossoverChannel("Sub") { Pair = { Mono = true } };
        VirtualCrossoverChannelState state = channel.SideState(rightSide: false);
        state.TransferImpulseResponse = new Complex[1_024];
        VirtualCrossoverSourceSnapshot half = state.ProcessingSource!;

        Assert.Same(half, state.ProcessingSource);
        channel.Pair.MeasuredFromBothInputs = false;
        Assert.NotSame(half, state.ProcessingSource);
    }

    private static LiveCaptureDocument Capture(double db) => new()
    {
        CurveDb = [.. Enumerable.Repeat(db, 16)],
        SpectrumDb = [.. Enumerable.Repeat(db, 16)],
        GridStartHz = 20,
        GridStopHz = 20_000
    };
}
