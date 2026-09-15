using System.Numerics;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

/// <summary>Harmonic packets sit at SweepSamples * ln(h) / ln(ratio), so stored measurements must use the ACTUALLY swept band.</summary>
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
        // Pre-band file: 12 octaves ending at Nyquist, ratio exactly 4096.
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
        // The recorded band fed back as a REQUEST got guard bands added twice.
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

        // The rebuilt sweep cannot express the legacy geometry (under one cycle at the low edge).
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
        Assert.Equal(measurement.Sweep!.LowFrequencyHz, measurement.AchievedLowFrequencyHz, 9);
        Assert.Equal(measurement.Sweep.HighFrequencyHz, measurement.AchievedHighFrequencyHz, 9);
        Assert.True(measurement.AchievedLowFrequencyHz < 1000.0);
        Assert.True(measurement.AchievedHighFrequencyHz > 20_000.0);
        Assert.True(measurement.AchievedFrequencyRatio > 20_000.0 / 1000.0);
    }

    [Fact]
    public void AStoredMeasurement_CarriesTheSweptBandForHarmonicAnalysis()
    {
        // Requested 1-20 kHz is 4.32 octaves; the sweep runs ~5.08.
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

        double correct = measurement.Sweep!.SweepSamples * Math.Log(2.0) / Math.Log(achievedRatio);
        double wrong = measurement.Sweep.SweepSamples * Math.Log(2.0) / Math.Log(requestedRatio);
        Assert.Equal(correct, measurement.HarmonicIROffset(2.0), 6);
        Assert.True(
            Math.Abs(wrong - correct) / measurement.SampleRate > 0.05,
            "the two bands must disagree by more than 50 ms, or this guards nothing");
    }

    [Fact]
    public void RestoringASweepLongerThanTheGenerationCap_KeepsItsRealLength()
    {
        // The generator caps at MaxDurationSeconds but files store up to an hour; the rebuilt count would halve offsets at 200 s.
        const int sampleRate = 48_000;
        const double storedDurationSeconds = 200.0;
        Assert.True(storedDurationSeconds > ExponentialSineSweep.MaxDurationSeconds);

        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
            20,
            20_000,
            sampleRate,
            24,
            storedDurationSeconds,
            PlaybackChannel.Mono,
            Impulse(2048, 16),
            16);

        Assert.Equal(
            (int)Math.Round(storedDurationSeconds * sampleRate),
            measurement.AchievedSweepSampleCount);
        Assert.Equal(storedDurationSeconds, measurement.AchievedSweepDurationSeconds, 6);
        Assert.Equal(
            (int)Math.Round(ExponentialSineSweep.MaxDurationSeconds * sampleRate),
            measurement.Sweep!.SweepSamples);

        double expected = measurement.AchievedSweepSampleCount * Math.Log(2.0) /
            Math.Log(measurement.AchievedFrequencyRatio);
        Assert.Equal(expected, measurement.HarmonicIROffset(2.0), 6);
        Assert.True(
            measurement.HarmonicIROffset(2.0) >
                measurement.Sweep.SweepSamples * Math.Log(2.0) /
                    Math.Log(measurement.AchievedFrequencyRatio) * 1.5,
            "the capped sweep would have understated the offset by about half");
    }

    [Fact]
    public void ACapturedMeasurement_TakesItsSweepLengthFromTheGeneratedSweep()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                LowFrequencyHz: 20,
                HighFrequencyHz: 20_000,
                SampleRate: 48_000,
                Bits: 24,
                RequestedDurationSeconds: 2.0,
                PlaybackChannel: PlaybackChannel.Mono),
            new SweepAudioConfiguration(WaveLoopbackInputChannelOffset: 1),
            new SweepAveragingConfiguration()));

        Assert.Equal(measurement.Sweep!.SweepSamples, measurement.AchievedSweepSampleCount);
        Assert.Equal(2.0, measurement.AchievedSweepDurationSeconds, 3);
    }

    [Fact]
    public void AHistorySnapshotOfAStoredFile_ResolvesTheSweptBandNotTheRequest()
    {
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
