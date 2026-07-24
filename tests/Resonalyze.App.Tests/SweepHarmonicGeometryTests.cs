using System.Numerics;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>
/// Harmonic packets in a deconvolved sweep sit at
/// <c>SweepSamples * ln(harmonic) / ln(ratio)</c>, so every path that reads a
/// stored measurement has to use the band that was ACTUALLY swept. Reading the
/// requested band instead — or re-deriving a sweep from a band that was already
/// achieved — moves the packets, and the distortion curves with them.
/// </summary>
public sealed class SweepHarmonicGeometryTests
{
    private static Complex[] Impulse(int length, int peakIndex)
    {
        var samples = new Complex[length];
        samples[peakIndex] = new Complex(1.0, 0.0);
        return samples;
    }

    [Fact]
    public void RestoringALegacyFile_KeepsTheHarmonicOffsetsOfTheOriginalSweep()
    {
        // A pre-band file: 12 octaves ending at Nyquist, ratio exactly 4096.
        const int sampleRate = 48_000;
        const int octaves = 12;
        const double durationSeconds = 1.0;
        double nyquist = sampleRate / 2.0;
        (double legacyLowHz, double legacyHighHz) = ImpulseResponseFile.ResolveSweepBand(
            lowFrequencyHz: 0,
            highFrequencyHz: 0,
            octaves: octaves,
            sampleRate: sampleRate);

        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
            legacyLowHz,
            legacyHighHz,
            sampleRate,
            24,
            durationSeconds,
            PlaybackChannel.Mono,
            Impulse(2048, 16),
            16,
            achievedLowFrequencyHz: legacyLowHz,
            achievedHighFrequencyHz: legacyHighHz);

        Assert.Equal(nyquist, legacyHighHz);
        Assert.Equal(Math.Pow(2.0, octaves), measurement.AchievedFrequencyRatio, 6);

        // What the pre-band build computed: SweepSamples * ln(h) / (octaves * ln 2).
        double expectedSecondHarmonic =
            measurement.Sweep!.SweepSamples * Math.Log(2.0) / (octaves * Math.Log(2.0));
        Assert.Equal(expectedSecondHarmonic, measurement.HarmonicIROffset(2.0), 6);
        double expectedThirdHarmonic =
            measurement.Sweep.SweepSamples * Math.Log(3.0) / (octaves * Math.Log(2.0));
        Assert.Equal(expectedThirdHarmonic, measurement.HarmonicIROffset(3.0), 6);
    }

    [Fact]
    public void RestoringALegacyFile_DoesNotWidenItsBandWithGuardBands()
    {
        // Regression: the recorded band used to be fed back in as a REQUEST, so
        // ComputeSpec added the guard bands a second time and the reconstructed
        // sweep no longer matched the one that produced the IR.
        const int sampleRate = 48_000;
        (double legacyLowHz, double legacyHighHz) = ImpulseResponseFile.ResolveSweepBand(
            lowFrequencyHz: 0,
            highFrequencyHz: 0,
            octaves: 12,
            sampleRate: sampleRate);

        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
            legacyLowHz,
            legacyHighHz,
            sampleRate,
            24,
            1.0,
            PlaybackChannel.Mono,
            Impulse(2048, 16),
            16,
            achievedLowFrequencyHz: legacyLowHz,
            achievedHighFrequencyHz: legacyHighHz);

        // The sweep rebuilt on load cannot express the legacy geometry (its low
        // edge carried less than one whole cycle), which is exactly why the
        // recorded edges have to win.
        ExpSweepSpec rebuilt = ExponentialSineSweep.ComputeSpec(
            legacyLowHz, legacyHighHz, 1.0, sampleRate);
        Assert.True(
            Math.Abs(rebuilt.OctaveSpan - 12.0) > 0.1,
            "the rebuilt sweep is expected to differ; the test is meaningless otherwise");
        Assert.Equal(12.0, Math.Log2(measurement.AchievedFrequencyRatio), 6);
    }

    [Fact]
    public void ACapturedMeasurement_ReportsTheSweptBandNotTheRequestedOne()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                LowFrequencyHz: 1000,
                HighFrequencyHz: 20_000,
                SampleRate: 48_000,
                Bits: 24,
                RequestedDurationSeconds: 1.0,
                PlaybackChannel: PlaybackChannel.Mono),
            new SweepAudioConfiguration(WaveLoopbackInputChannelOffset: 1),
            new SweepAveragingConfiguration()));

        Assert.Equal(1000.0, measurement.LowFrequencyHz);
        Assert.Equal(20_000.0, measurement.HighFrequencyHz);
        // The swept band encloses the request, so its ratio is the larger one.
        Assert.Equal(measurement.Sweep!.LowFrequencyHz, measurement.AchievedLowFrequencyHz, 9);
        Assert.Equal(measurement.Sweep.HighFrequencyHz, measurement.AchievedHighFrequencyHz, 9);
        Assert.True(measurement.AchievedLowFrequencyHz < 1000.0);
        Assert.True(measurement.AchievedHighFrequencyHz > 20_000.0);
        Assert.True(measurement.AchievedFrequencyRatio > 20_000.0 / 1000.0);
    }

    [Fact]
    public void AStoredMeasurement_CarriesTheSweptBandForHarmonicAnalysis()
    {
        // Requested 1000-20000 Hz is 4.32 octaves while the sweep runs about 5.08:
        // handing the request to the harmonic analysis shifts every packet.
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                LowFrequencyHz: 1000,
                HighFrequencyHz: 20_000,
                SampleRate: 48_000,
                Bits: 24,
                RequestedDurationSeconds: 4.3,
                PlaybackChannel: PlaybackChannel.Mono),
            new SweepAudioConfiguration(WaveLoopbackInputChannelOffset: 1),
            new SweepAveragingConfiguration()));

        double requestedRatio = 20_000.0 / 1000.0;
        double achievedRatio = measurement.AchievedFrequencyRatio;
        Assert.True(achievedRatio > requestedRatio * 1.5, "guard bands widen the sweep");

        // The offset the requested band would have produced is far enough out to
        // land in the wrong place: tens of milliseconds at this sweep length.
        double correct = measurement.Sweep!.SweepSamples * Math.Log(2.0) / Math.Log(achievedRatio);
        double wrong = measurement.Sweep.SweepSamples * Math.Log(2.0) / Math.Log(requestedRatio);
        Assert.Equal(correct, measurement.HarmonicIROffset(2.0), 6);
        Assert.True(
            Math.Abs(wrong - correct) / measurement.SampleRate > 0.05,
            "the two bands must disagree by more than 50 ms, or this guards nothing");
    }

    [Fact]
    public void AHistorySnapshotOfAStoredFile_ResolvesTheSweptBandNotTheRequest()
    {
        // The path the Virtual DSP wizard reads its distortion curve through.
        var file = new ImpulseResponseFile
        {
            SampleRate = 48_000,
            Bits = 24,
            LowFrequencyHz = 1000,
            HighFrequencyHz = 20_000,
            AchievedLowFrequencyHz = 707.294,
            AchievedHighFrequencyHz = 23_995.547,
            SweepDurationSeconds = 4.3,
            PlayChannel = PlaybackChannel.Mono,
            SweepDeconvolutionPeakIndex = 16,
            SweepDeconvolutionRealSamples = new double[2048]
        };
        file.SweepDeconvolutionRealSamples[16] = 1.0;

        MeasurementHistorySnapshot snapshot = MeasurementHistoryService.CreateSnapshot(file);

        (double requestedLowHz, double requestedHighHz) = snapshot.ResolveSweepBand();
        Assert.Equal(1000.0, requestedLowHz);
        Assert.Equal(20_000.0, requestedHighHz);

        (double lowHz, double highHz) = snapshot.ResolveAchievedSweepBand();
        Assert.Equal(707.294, lowHz, 6);
        Assert.Equal(23_995.547, highHz, 6);
    }
}
