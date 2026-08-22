using System.Numerics;
using Resonalyze.Audio;

namespace Resonalyze.App.Tests;

/// <summary>
/// Publishing a stored result through <c>RestoreImpulseResponse</c> while the
/// measurement is busy. A run must still block it; a claim — which is what the
/// caller that decoded the file has been holding across the read — must not,
/// and with nothing held it takes its own for the publish and gives it back.
/// </summary>
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

    // The plain case, and the one that regressed: nothing is held, so the restore
    // claims for itself — and then has to configure the measurement THROUGH that
    // claim, which the public Init refuses by design. A second restore is what
    // proves the flag came back rather than merely reading false once.
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

    // The case the claim was added for: the file import claims before it reads,
    // so the measurement is already busy by the time the decoded result arrives.
    // The restore must go through, and must leave the claim to its owner.
    [Fact]
    public void RestoreUnderAnExternallyHeldClaimPublishesAndLeavesTheClaimStanding()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());

        using (measurement.Claim())
        {
            Assert.True(measurement.InProgress);

            Restore(measurement);

            Assert.True(measurement.HasImpulseResponse);
            // Still the claim holder's, not handed back by the restore.
            Assert.True(measurement.InProgress);
        }

        Assert.False(measurement.InProgress);
    }

    // The arguments are validated after the claim is taken, so a refused restore
    // is the path that leaves the measurement busy forever if the release is not
    // in a finally.
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

        // And the measurement is still usable, which is the point of giving it back.
        Restore(measurement);
        Assert.True(measurement.HasImpulseResponse);
    }
}
