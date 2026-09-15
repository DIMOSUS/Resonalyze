namespace Resonalyze.App.Tests;

/// <summary>The panel preview (ComputeSpec) and the run (ApplyTo) must resolve a request identically.</summary>
public sealed class SweepDurationLimitTests
{
    private const int SampleRate = 44_100;

    [Fact]
    public void APaceLongerThanTheCap_PreviewsTheDurationThatWillActuallyRun()
    {
        double total = ExponentialSineSweep.TotalDurationForOctavePace(
            20, 20_000, perOctaveSeconds: 20.0, SampleRate);
        Assert.True(
            total > ExponentialSineSweep.MaxDurationSeconds,
            "the pace has to exceed the cap, or this guards nothing");

        ExpSweepSpec preview = ExponentialSineSweep.ComputeSpec(20, 20_000, total, SampleRate);
        ExpSweepSpec run = ExponentialSineSweep.ComputeSpec(
            20,
            20_000,
            Math.Clamp(total, 0.001, ExponentialSineSweep.MaxDurationSeconds),
            SampleRate);

        Assert.Equal(ExponentialSineSweep.MaxDurationSeconds, preview.ComputedDurationSeconds, 3);
        Assert.Equal(run.ComputedDurationSeconds, preview.ComputedDurationSeconds, 9);
        Assert.Equal(run.SampleCount, preview.SampleCount);
        Assert.Equal(run.LowFrequencyHz, preview.LowFrequencyHz, 9);
        Assert.Equal(run.HighFrequencyHz, preview.HighFrequencyHz, 9);
    }

    [Fact]
    public void TheGeneratedSweep_NeverExceedsTheCap()
    {
        using var sweep = new ExponentialSineSweep();
        sweep.FillData(20, 20_000, requestedDuration: 500.0, 24, SampleRate);

        Assert.Equal(
            ExponentialSineSweep.MaxDurationSeconds,
            sweep.ComputedDuration,
            3);
        Assert.Equal(
            (int)Math.Round(SampleRate * ExponentialSineSweep.MaxDurationSeconds),
            sweep.SweepSamples);
    }

    [Fact]
    public void ASweepWithinTheCap_IsHonouredToTheSample()
    {
        double total = ExponentialSineSweep.TotalDurationForOctavePace(
            20, 20_000, perOctaveSeconds: 0.2, SampleRate);
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(20, 20_000, total, SampleRate);

        Assert.True(spec.ComputedDurationSeconds < ExponentialSineSweep.MaxDurationSeconds);
        Assert.Equal((int)Math.Round(SampleRate * total), spec.SampleCount);
    }

    [Theory]
    // A cycle at 20 Hz takes 50 ms, so 5 or 25 ms per octave cannot reach the low edge.
    [InlineData(5.0, false)]
    [InlineData(25.0, false)]
    [InlineData(50.0, true)]
    [InlineData(200.0, true)]
    public void WhetherTheSweptBandCoversTheRequest_IsReportedNotAssumed(
        double perOctaveMs,
        bool expectedToCover)
    {
        double total = ExponentialSineSweep.TotalDurationForOctavePace(
            20, 20_000, perOctaveMs * 0.001, SampleRate);
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(20, 20_000, total, SampleRate);

        Assert.True(spec.IsValid);
        Assert.Equal(expectedToCover, spec.Covers(20, 20_000));
        if (!expectedToCover)
        {
            Assert.True(spec.LowFrequencyHz > 20.0);
        }
    }

    [Fact]
    public void AShortSweepWhoseTrajectoryPassesTheBand_StillDoesNotCoverIt()
    {
        // 5 ms/oct over 1-20 kHz: the trajectory spans 692-22012 Hz but minimum-length fades keep the envelope closed inside it.
        double total = ExponentialSineSweep.TotalDurationForOctavePace(
            1000, 20_000, perOctaveSeconds: 0.005, SampleRate);
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(1000, 20_000, total, SampleRate);

        Assert.True(spec.LowFrequencyHz <= 1000.0, "the trajectory does reach below 1 kHz");
        Assert.True(spec.HighFrequencyHz >= 20_000.0, "and above 20 kHz");
        Assert.True(spec.FullAmplitudeLowFrequencyHz > 1000.0);
        Assert.True(spec.FullAmplitudeHighFrequencyHz < 20_000.0);
        Assert.False(spec.Covers(1000, 20_000));
    }

    [Fact]
    public void AnUnhurriedSweep_ReachesFullAmplitudeExactlyAtTheRequestedEdges()
    {
        double total = ExponentialSineSweep.TotalDurationForOctavePace(
            20, 20_000, perOctaveSeconds: 0.2, SampleRate);
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(20, 20_000, total, SampleRate);

        Assert.True(spec.Covers(20, 20_000));
        double perSample = Math.Exp(Math.Log(spec.FrequencyRatio) / spec.SampleCount);
        Assert.InRange(spec.FullAmplitudeLowFrequencyHz, 20.0 / perSample, 20.0 * perSample);
        Assert.InRange(
            spec.FullAmplitudeHighFrequencyHz,
            20_000.0 / perSample,
            20_000.0 * perSample);
        Assert.True(spec.LowFrequencyHz < spec.FullAmplitudeLowFrequencyHz);
        Assert.True(spec.FullAmplitudeHighFrequencyHz < spec.HighFrequencyHz);
    }

    [Fact]
    public void FullAmplitudeEdges_FollowTheFadeLengths()
    {
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(20, 20_000, 4.0, SampleRate);
        double beta = Math.Log(spec.FrequencyRatio);

        Assert.Equal(
            spec.LowFrequencyHz * Math.Exp(spec.FadeInSamples / (double)spec.SampleCount * beta),
            spec.FullAmplitudeLowFrequencyHz,
            9);
        Assert.Equal(
            spec.LowFrequencyHz * Math.Exp(
                (spec.SampleCount - spec.FadeOutSamples) / (double)spec.SampleCount * beta),
            spec.FullAmplitudeHighFrequencyHz,
            9);
    }
}
