namespace Resonalyze.App.Tests;

/// <summary>
/// The options panel previews a sweep through <see cref="ExponentialSineSweep.ComputeSpec"/>
/// while a run reaches the generator through
/// <see cref="MeasurementSettingsFile.SweepMeasurementSettings.ApplyTo"/>. Both
/// have to resolve a request the same way, or the panel promises a sweep that is
/// not the one that runs.
/// </summary>
public sealed class SweepDurationLimitTests
{
    private const int SampleRate = 44_100;

    [Fact]
    public void APaceLongerThanTheCap_PreviewsTheDurationThatWillActuallyRun()
    {
        // 20 s per octave over 20-20000 Hz asks for about 212 s; the run is capped.
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
    // A whole cycle at 20 Hz takes 50 ms, so a sweep pacing every octave in 5 or
    // 25 ms cannot reach the requested low edge however it is quantized.
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
            // The panel shows these edges, so they must stay honest rather than
            // report the request back.
            Assert.True(spec.LowFrequencyHz > 20.0);
        }
    }

    [Fact]
    public void AShortSweepWhoseTrajectoryPassesTheBand_StillDoesNotCoverIt()
    {
        // Regression: coverage used to be read off the frequency trajectory alone.
        // At 5 ms/oct over 1000-20000 Hz the trajectory does span the request
        // (about 692-22012 Hz), but the fades are padded to their minimum length
        // and keep the envelope closed well inside it, so 20 kHz is reached at a
        // fraction of full amplitude.
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
        // The fades are sized to span the guard bands, so with room to breathe the
        // envelope opens at the request and the whole band is measured flat.
        double total = ExponentialSineSweep.TotalDurationForOctavePace(
            20, 20_000, perOctaveSeconds: 0.2, SampleRate);
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(20, 20_000, total, SampleRate);

        Assert.True(spec.Covers(20, 20_000));
        // The fades stop within a sample of the requested edges — they are sized to
        // span the guard bands, and a whole-sample length is the only slack.
        double perSample = Math.Exp(Math.Log(spec.FrequencyRatio) / spec.SampleCount);
        Assert.InRange(spec.FullAmplitudeLowFrequencyHz, 20.0 / perSample, 20.0 * perSample);
        Assert.InRange(
            spec.FullAmplitudeHighFrequencyHz,
            20_000.0 / perSample,
            20_000.0 * perSample);
        // And the trajectory still runs past them, into the guard bands.
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
