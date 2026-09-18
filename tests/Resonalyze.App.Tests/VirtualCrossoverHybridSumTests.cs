using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <remarks>Complex sum of gated spectra rescaled to the captures' levels (magnitudes + borrowed IR loss drew a 13 dB dip on a real car).
/// The phase is still one position's, so the sum shows a point's interference.</remarks>
public sealed class VirtualCrossoverHybridSumTests
{
    /// <summary>A capture gap where the IR says the channel is inaudible drops only that channel from the point.</summary>
    [Fact]
    public void AChannelThatBreaksFarBelowTheOthers_DropsOutOfThatPointOnly()
    {
        List<SignalPoint> sum = Flat(-10);
        List<SignalPoint> quiet = Flat(-12);
        quiet[3] = new SignalPoint(quiet[3].X, double.NaN);

        List<SignalPoint> quietReference = Flat(-12);
        quietReference[3] = new SignalPoint(quietReference[3].X, -52);

        List<SignalPoint> masked = VirtualCrossoverHybrid.MaskMissingContributors(
            sum, [Flat(-12), quiet], [Flat(-12), quietReference], offsetDb: 0);

        Assert.Equal(-10, masked[3].Y, 10);
    }

    [Fact]
    public void AChannelThatBreaksWhileStillPlaying_TakesThePointWithIt()
    {
        List<SignalPoint> sum = Flat(-10);
        List<SignalPoint> quiet = Flat(-12);
        quiet[3] = new SignalPoint(quiet[3].X, double.NaN);

        List<SignalPoint> masked = VirtualCrossoverHybrid.MaskMissingContributors(
            sum, [Flat(-12), quiet], [Flat(-12), Flat(-12)], offsetDb: 0);

        Assert.True(double.IsNaN(masked[3].Y));
        Assert.Equal(-10, masked[2].Y, 10);
        Assert.Equal(-10, masked[4].Y, 10);
    }

    [Fact]
    public void TheSetsOffset_MovesEveryPointByExactlyTheSameDecibels()
    {
        List<SignalPoint> sum = Flat(-10);

        List<SignalPoint> at0 = VirtualCrossoverHybrid.MaskMissingContributors(
            sum, [Flat(-12)], [Flat(-12)], offsetDb: 0);
        List<SignalPoint> at7 = VirtualCrossoverHybrid.MaskMissingContributors(
            sum, [Flat(-12)], [Flat(-12)], offsetDb: 7.5);

        for (int i = 0; i < at0.Count; i++)
        {
            Assert.Equal(at0[i].Y + 7.5, at7[i].Y, 10);
        }
    }

    /// <summary>Two relative capture runs per side are each consistent but not comparable; the opposite sum borrows the active offset.</summary>
    [Fact]
    public void RelativeCapturesFromDifferentSessionsAcrossSidesDoNotShareAnOffset()
    {
        var leftSession = Guid.NewGuid();
        var rightSession = Guid.NewGuid();
        List<LiveCaptureDocument> active = [SideCapture(leftSession), SideCapture(leftSession)];
        List<LiveCaptureDocument> opposite =
            [SideCapture(rightSession), SideCapture(rightSession)];

        Assert.True(LiveCaptureDocument.JudgeSet(active).Coherent);
        Assert.True(LiveCaptureDocument.JudgeSet(opposite).Coherent);

        Assert.False(
            VirtualCrossoverHybrid.JudgeSidesShareAnOffset(active, opposite).Coherent);
    }

    [Fact]
    public void AnchoredCapturesFromDifferentSessionsAcrossSidesMayShareAnOffset()
    {
        List<LiveCaptureDocument> active =
            [SideCapture(Guid.NewGuid(), 94.0), SideCapture(Guid.NewGuid(), 94.0)];
        List<LiveCaptureDocument> opposite =
            [SideCapture(Guid.NewGuid(), 94.0), SideCapture(Guid.NewGuid(), 94.0)];

        Assert.True(
            VirtualCrossoverHybrid.JudgeSidesShareAnOffset(active, opposite).Coherent);
    }

    [Fact]
    public void SidesTakenOnDifferentRecipesDoNotShareAnOffset()
    {
        var session = Guid.NewGuid();
        List<LiveCaptureDocument> active = [SideCapture(session)];
        LiveCaptureDocument odd = SideCapture(session);
        odd.Recipe.SequenceLength *= 2;

        LiveCaptureSetVerdict verdict =
            VirtualCrossoverHybrid.JudgeSidesShareAnOffset(active, [odd]);

        Assert.False(verdict.Coherent);
        Assert.Contains("frame length", verdict.Reason);
    }

    private static List<SignalPoint> Flat(double db)
    {
        var points = new List<SignalPoint>();
        for (int i = 0; i < 40; i++)
        {
            points.Add(new SignalPoint(50 * Math.Pow(10, 2.0 * i / 39), db));
        }

        return points;
    }

    /// <summary>Sliced by position: an off-by-one draws a zone from another zone's captures, plausibly.</summary>
    [Fact]
    public void Subset_TakesEachListAtTheSamePositions()
    {
        var whole = new HybridMagnitudes(
            [Flat(-1), Flat(-2), Flat(-3), Flat(-4)],
            [Flat(-11), Flat(-12), Flat(-13), Flat(-14)],
            [1.0, 2.0, 3.0, 4.0],
            OffsetDb: 7.5)
        {
            PointMeasuredChannels = [false, true, false, true],
            SetDatumsDb = []
        };

        HybridMagnitudes slice = whole.Subset([1, 3]);

        Assert.Equal([-2.0, -4.0], slice.Channels.Select(curve => curve[0].Y));
        Assert.Equal(
            [-12.0, -14.0], slice.UnsmoothedChannels.Select(curve => curve[0].Y));
        Assert.Equal([2.0, 4.0], slice.ChannelOffsetsDb);
        Assert.Equal([true, true], slice.PointMeasuredChannels);
    }

    [Fact]
    public void Subset_KeepsTheSetsOwnOffset()
    {
        var whole = new HybridMagnitudes(
            [Flat(-1), Flat(-2)], [Flat(-1), Flat(-2)], [null, 2.0], OffsetDb: -3.25);

        Assert.Equal(-3.25, whole.Subset([0]).OffsetDb);
    }

    [Fact]
    public void Subset_LeavesAnEmptyFallbackListEmpty()
    {
        var whole = new HybridMagnitudes(
            [Flat(-1), Flat(-2)], [Flat(-1), Flat(-2)], [1.0, 2.0], OffsetDb: 0);

        Assert.Empty(whole.Subset([1]).PointMeasuredChannels);
    }

    private static LiveCaptureDocument SideCapture(
        Guid session, double? splAnchorOffsetDb = null) =>
        new()
        {
            SavedAtUtc = DateTimeOffset.UnixEpoch,
            Title = "capture",
            CaptureSessionId = session,
            CurveDb = Enumerable.Repeat(-20.0, 1_024).ToArray(),
            GridStartHz = 20,
            GridStopHz = 20_000,
            Recipe = new LiveCaptureRecipe
            {
                AnalysisMode = LiveAnalysisMode.Mmm,
                SampleRateHz = 48_000,
                SequenceLength = 32_768,
                WindowType = WindowType.Rectangular,
                NoiseColor = NoiseColor.PinkPeriodic,
                SlopeCompensation = true,
                MagnitudeScale = MagnitudeScale.SoundPressureLevel,
                SplAnchorOffsetDb = splAnchorOffsetDb
            }
        };
}
