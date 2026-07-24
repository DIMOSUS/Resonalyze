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
    public void AShortSweepOverANarrowBand_StillCoversTheRequest()
    {
        // The limit is the low edge, not the pace: 1 kHz needs 1 ms per cycle.
        double total = ExponentialSineSweep.TotalDurationForOctavePace(
            1000, 20_000, perOctaveSeconds: 0.005, SampleRate);
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(1000, 20_000, total, SampleRate);

        Assert.True(spec.Covers(1000, 20_000));
    }
}
