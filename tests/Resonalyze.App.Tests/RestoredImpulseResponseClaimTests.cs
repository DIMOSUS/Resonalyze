using System.Numerics;
using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

/// <summary>A run blocks a restore; an externally held claim (the file import's) does not.</summary>
public sealed class RestoredImpulseResponseClaimTests
{
    private const int SampleRate = 48_000;

    private static void Restore(ExpSweepMeasurement measurement) =>
        measurement.RestoreImpulseResponse(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: SampleRate,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: [Complex.Zero, Complex.One, Complex.Zero],
            sweepDeconvolutionPeakIndex: 1);

    // The regression: a self-claimed restore must configure through its claim, which public Init refuses.
    [Fact]
    public void RestoreWithNothingHeldPublishesAndReleases()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        Assert.False(measurement.InProgress);

        Restore(measurement);

        Assert.True(measurement.HasImpulseResponse);
        Assert.Equal(SampleRate, measurement.SampleRate);
        Assert.False(measurement.InProgress);

        Restore(measurement);
        Assert.False(measurement.InProgress);
    }

    [Fact]
    public void RestoreUnderAnExternallyHeldClaimPublishesAndLeavesTheClaimStanding()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());

        using (measurement.Claim())
        {
            Assert.True(measurement.InProgress);

            Restore(measurement);

            Assert.True(measurement.HasImpulseResponse);
            Assert.True(measurement.InProgress);
        }

        Assert.False(measurement.InProgress);
    }

    // Arguments are validated after the claim, so the release must be in a finally.
    [Fact]
    public void ARefusedRestoreGivesBackTheClaimItTook()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());

        Assert.Throws<ArgumentException>(() =>
            measurement.RestoreImpulseResponse(
                lowFrequencyHz: 20,
                highFrequencyHz: 20_000,
                sampleRate: SampleRate,
                bits: 24,
                sweepDurationSeconds: 1.0,
                playChannel: PlaybackChannel.Mono,
                sweepDeconvolutionImpulseResponse: [],
                sweepDeconvolutionPeakIndex: 0));

        Assert.False(measurement.InProgress);
        Assert.False(measurement.HasImpulseResponse);

        Restore(measurement);
        Assert.True(measurement.HasImpulseResponse);
    }
}
