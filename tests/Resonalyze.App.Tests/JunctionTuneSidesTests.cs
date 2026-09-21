using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class JunctionTuneSidesTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void TheSpatialAverage_IsThePlant_WhereAutoTuneWouldFitIt()
    {
        VirtualCrossoverChannel lower = Channel("A", CrossoverKind.LowPass);
        lower.Settings.PeqBands = [new PeqBand(150, 2, -6)];
        VirtualCrossoverChannel upper = Channel("B", CrossoverKind.HighPass);
        LiveCaptureDocument capture = Capture();
        lower.SpatialAverage = capture;
        (List<JunctionTuneSide> sides, string? refusal) = AgentProbeReader.JunctionTuneSides(lower, upper, null);
        Assert.Null(refusal);

        List<JunctionTuneSide> drawn = AgentProbeReader.WithSpatialAverages(
            sides, lower, upper, VirtualCrossoverSpatialAverageMode.MovingMic, SpatialAverageCalibration.Own,
            SampleRate);
        List<JunctionTuneSide> notDrawn = AgentProbeReader.WithSpatialAverages(
            sides, lower, upper, mode: null, SpatialAverageCalibration.Own, SampleRate);

        JunctionTuneSide left = Assert.Single(drawn);
        List<SignalPoint> expected = SpatialAverageHybrid.BuildChannelCurve(
            capture,
            CrossoverJunctionTuner.WithoutLowPass(sides[0].LowerChain) with { Peq = null },
            SampleRate,
            SpatialAverageCalibration.Own,
            capture.ToCurvePoints().Select(point => point.X).ToList(),
            smoothingCode: 0)!;
        Assert.Equal(expected, left.LowerMagnitude);
        Assert.Null(left.UpperMagnitude);
        Assert.Null(Assert.Single(notDrawn).LowerMagnitude);
    }

    [Fact]
    public void AMonoBlock_MakesOneReAlignmentServeBothSides()
    {
        VirtualCrossoverChannel lower = Channel("A", CrossoverKind.LowPass);
        VirtualCrossoverChannel upper = Channel("B", CrossoverKind.HighPass);

        Assert.False(AgentProbeReader.SharesOneAlignment(lower, upper));
        lower.Pair.Mono = true;
        Assert.True(AgentProbeReader.SharesOneAlignment(lower, upper));
        lower.Pair.Mono = false;
        upper.Pair.Mono = true;
        Assert.True(AgentProbeReader.SharesOneAlignment(lower, upper));
    }

    private static VirtualCrossoverChannel Channel(string name, CrossoverKind kind)
    {
        var impulseResponse = new Complex[4_096];
        impulseResponse[480] = 1;
        var channel = new VirtualCrossoverChannel(name)
        {
            SampleRate = SampleRate,
            TransferImpulseResponse = impulseResponse,
            TransferPeakIndex = 480
        };
        channel.Settings.CrossoverKind = kind;
        channel.Settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 200, 24);
        channel.Settings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 200, 24);
        return channel;
    }

    private static LiveCaptureDocument Capture() => new()
    {
        SavedAtUtc = DateTimeOffset.UnixEpoch,
        Title = "l mid mmm",
        CurveDb = Enumerable.Range(0, 1_024)
            .Select(index => -20.0 + 3 * Math.Sin(index / 40.0))
            .ToArray(),
        GridStartHz = 20,
        GridStopHz = 20_000,
        Recipe = new LiveCaptureRecipe
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            SampleRateHz = SampleRate
        }
    };
}
