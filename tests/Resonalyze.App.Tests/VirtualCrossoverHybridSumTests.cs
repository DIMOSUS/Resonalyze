using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The hybrid view's sum, and the conditions it is drawn under.
/// </summary>
/// <remarks>
/// The sum is a COMPLEX one: each channel's gated spectrum rescaled to the level its
/// spatial average reports, then summed as phasors — DataHelper.GetGatedSubstitutedMagnitudeSum,
/// pinned in the dsp suite. It used to add the magnitudes and lay the impulse
/// responses' own summation loss on top, which is only valid while the two families
/// agree about the RELATIVE levels of the channels. At a steep junction on a real car
/// they disagreed by 23 dB, and the borrowed loss drew a 13 dB dip into a sum whose
/// own channels could not have made more than 1.9 dB.
/// <para>
/// What survives from that: the phase is still the impulse responses' own, measured
/// at ONE microphone position, so the sum draws a point's interference rather than a
/// spatially averaged one. Which way that errs is not determined; only the tendency
/// holds, that the gap grows the faster the relative phase turns across the volume.
/// </para>
/// </remarks>
public sealed class VirtualCrossoverHybridSumTests
{
    /// <summary>
    /// A channel whose capture stops where its impulse response says it is INAUDIBLE
    /// drops out of that point: it is below its own crossover, the others carry the
    /// point, and breaking the whole sum would cost more than it protects.
    /// </summary>
    [Fact]
    public void AChannelThatBreaksFarBelowTheOthers_DropsOutOfThatPointOnly()
    {
        List<SignalPoint> sum = Flat(-10);
        List<SignalPoint> quiet = Flat(-12);
        quiet[3] = new SignalPoint(quiet[3].X, double.NaN);

        // Its impulse response puts it 40 dB under the other channel here.
        List<SignalPoint> quietReference = Flat(-12);
        quietReference[3] = new SignalPoint(quietReference[3].X, -52);

        List<SignalPoint> masked = VirtualCrossoverPanel.MaskMissingContributors(
            sum, [Flat(-12), quiet], [Flat(-12), quietReference], offsetDb: 0);

        Assert.Equal(-10, masked[3].Y, 10);
    }

    /// <summary>
    /// A channel whose capture stops while it is still PLAYING takes the point with
    /// it. Carrying on would present a sum of the remaining sources as the whole.
    /// </summary>
    [Fact]
    public void AChannelThatBreaksWhileStillPlaying_TakesThePointWithIt()
    {
        List<SignalPoint> sum = Flat(-10);
        List<SignalPoint> quiet = Flat(-12);
        quiet[3] = new SignalPoint(quiet[3].X, double.NaN);

        List<SignalPoint> masked = VirtualCrossoverPanel.MaskMissingContributors(
            sum, [Flat(-12), quiet], [Flat(-12), Flat(-12)], offsetDb: 0);

        Assert.True(double.IsNaN(masked[3].Y));
        Assert.Equal(-10, masked[2].Y, 10);
        Assert.Equal(-10, masked[4].Y, 10);
    }

    [Fact]
    public void TheSetsOffset_MovesEveryPointByExactlyTheSameDecibels()
    {
        List<SignalPoint> sum = Flat(-10);

        List<SignalPoint> at0 = VirtualCrossoverPanel.MaskMissingContributors(
            sum, [Flat(-12)], [Flat(-12)], offsetDb: 0);
        List<SignalPoint> at7 = VirtualCrossoverPanel.MaskMissingContributors(
            sum, [Flat(-12)], [Flat(-12)], offsetDb: 7.5);

        for (int i = 0; i < at0.Count; i++)
        {
            Assert.Equal(at0[i].Y + 7.5, at7[i].Y, 10);
        }
    }

    /// <summary>
    /// The dashed opposite sum borrows the ACTIVE side's offset, so both sides'
    /// captures have to be one set. Judging each side on its own cannot see this:
    /// two relative capture runs, one per side, are each internally consistent and
    /// say nothing about how their levels compare, so a gain that moved between them
    /// would be drawn as an L/R imbalance the car does not have.
    /// </summary>
    [Fact]
    public void RelativeCapturesFromDifferentSessionsAcrossSidesDoNotShareAnOffset()
    {
        var leftSession = Guid.NewGuid();
        var rightSession = Guid.NewGuid();
        List<LiveCaptureDocument> active = [SideCapture(leftSession), SideCapture(leftSession)];
        List<LiveCaptureDocument> opposite =
            [SideCapture(rightSession), SideCapture(rightSession)];

        // Each side alone passes — which is exactly why the pair must be judged too.
        Assert.True(LiveCaptureDocument.JudgeSet(active).Coherent);
        Assert.True(LiveCaptureDocument.JudgeSet(opposite).Coherent);

        Assert.False(
            VirtualCrossoverPanel.JudgeSidesShareAnOffset(active, opposite).Coherent);
    }

    [Fact]
    public void AnchoredCapturesFromDifferentSessionsAcrossSidesMayShareAnOffset()
    {
        // An absolute anchor re-establishes the reference each session, which is the
        // whole reason separate sessions are allowed at all.
        List<LiveCaptureDocument> active =
            [SideCapture(Guid.NewGuid(), 94.0), SideCapture(Guid.NewGuid(), 94.0)];
        List<LiveCaptureDocument> opposite =
            [SideCapture(Guid.NewGuid(), 94.0), SideCapture(Guid.NewGuid(), 94.0)];

        Assert.True(
            VirtualCrossoverPanel.JudgeSidesShareAnOffset(active, opposite).Coherent);
    }

    [Fact]
    public void SidesTakenOnDifferentRecipesDoNotShareAnOffset()
    {
        var session = Guid.NewGuid();
        List<LiveCaptureDocument> active = [SideCapture(session)];
        LiveCaptureDocument odd = SideCapture(session);
        odd.Recipe.SequenceLength *= 2;

        LiveCaptureSetVerdict verdict =
            VirtualCrossoverPanel.JudgeSidesShareAnOffset(active, [odd]);

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

    /// <summary>
    /// The Groups view sums each zone on its own, so the set's hybrid is sliced by
    /// POSITION — and a slice that shifted by one would draw a zone's line from
    /// another zone's captures, which looks entirely plausible on the plot.
    /// </summary>
    [Fact]
    public void HybridSubset_TakesEachListAtTheSamePositions()
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

        HybridMagnitudes slice = VirtualCrossoverPanel.HybridSubset(whole, [1, 3]);

        Assert.Equal([-2.0, -4.0], slice.Channels.Select(curve => curve[0].Y));
        Assert.Equal(
            [-12.0, -14.0], slice.UnsmoothedChannels.Select(curve => curve[0].Y));
        Assert.Equal([2.0, 4.0], slice.ChannelOffsetsDb);
        Assert.Equal([true, true], slice.PointMeasuredChannels);
    }

    /// <summary>
    /// The set offset is not a property of the slice: it is what puts every group's
    /// line on the impulse responses' one axis, so all of them have to carry it.
    /// </summary>
    [Fact]
    public void HybridSubset_KeepsTheSetsOwnOffset()
    {
        var whole = new HybridMagnitudes(
            [Flat(-1), Flat(-2)], [Flat(-1), Flat(-2)], [null, 2.0], OffsetDb: -3.25);

        Assert.Equal(-3.25, VirtualCrossoverPanel.HybridSubset(whole, [0]).OffsetDb);
    }

    /// <summary>
    /// A set that carried no per-channel fallback flags stays without them rather
    /// than growing a false "point measured" answer for the slice.
    /// </summary>
    [Fact]
    public void HybridSubset_LeavesAnEmptyFallbackListEmpty()
    {
        var whole = new HybridMagnitudes(
            [Flat(-1), Flat(-2)], [Flat(-1), Flat(-2)], [1.0, 2.0], OffsetDb: 0);

        Assert.Empty(VirtualCrossoverPanel.HybridSubset(whole, [1]).PointMeasuredChannels);
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
