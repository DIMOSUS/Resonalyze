using System.Numerics;

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

    private static ImpulseResponseFile StoredFile(
        double lowHz,
        double highHz,
        int octaves,
        double durationSeconds,
        int sampleRate = 48_000)
    {
        var file = new ImpulseResponseFile
        {
            SampleRate = sampleRate,
            Bits = 24,
            LowFrequencyHz = lowHz,
            HighFrequencyHz = highHz,
            Octaves = octaves,
            SweepDurationSeconds = durationSeconds,
            PlayChannel = PlaybackChannel.Mono,
            SweepDeconvolutionPeakIndex = 16,
            SweepDeconvolutionRealSamples = new double[2048]
        };
        file.SweepDeconvolutionRealSamples[16] = 1.0;
        return file;
    }

    private static async Task<(MeasurementResult Result, int GeneratedSamples)> MeasureAsync(
        double lowHz,
        double highHz,
        double durationSeconds)
    {
        var factory = new FakeAudioSessionFactory(
            duplexFactory: (_, signal) => new RecordingDuplexSession(
                signal, (_, s, tail, _) => Task.FromResult(SyntheticCapture.Good(s, tail))));
        using var measurement = new ExpSweepMeasurement(factory);
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                LowFrequencyHz: lowHz,
                HighFrequencyHz: highHz,
                SampleRate: 48_000,
                Bits: 24,
                RequestedDurationSeconds: durationSeconds,
                PlaybackChannel: PlaybackChannel.Mono),
            new SweepAudioConfiguration(WaveLoopbackInputChannelOffset: 1),
            new SweepAveragingConfiguration()));
        MeasurementResult? result = await measurement.RunAsync();
        Assert.True(result != null, measurement.LastError?.ToString());
        return (result, measurement.Sweep!.SweepSamples);
    }

    [Fact]
    public void RestoringALegacyFile_KeepsTheHarmonicOffsetsOfTheOriginalSweep()
    {
        // Pre-band file: 12 octaves ending at Nyquist, ratio exactly 4096.
        const int sampleRate = 48_000;
        const int octaves = 12;
        const double durationSeconds = 1.0;
        double nyquist = sampleRate / 2.0;

        MeasurementResult result = StoredFile(0, 0, octaves, durationSeconds).ToResult();

        Assert.Equal(nyquist, result.HighFrequencyHz);
        Assert.Equal(Math.Pow(2.0, octaves), result.AchievedFrequencyRatio, 6);
        double expectedSecondHarmonic =
            durationSeconds * sampleRate * Math.Log(2.0) / (octaves * Math.Log(2.0));
        Assert.Equal(expectedSecondHarmonic, result.HarmonicIROffset(2.0), 6);
        double expectedThirdHarmonic =
            durationSeconds * sampleRate * Math.Log(3.0) / (octaves * Math.Log(2.0));
        Assert.Equal(expectedThirdHarmonic, result.HarmonicIROffset(3.0), 6);
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

        MeasurementResult result = StoredFile(0, 0, 12, 1.0).ToResult();

        // The rebuilt sweep cannot express the legacy geometry (under one cycle at the low edge).
        ExpSweepSpec rebuilt = ExponentialSineSweep.ComputeSpec(
            legacyLowHz, legacyHighHz, 1.0, sampleRate);
        Assert.True(
            Math.Abs(rebuilt.OctaveSpan - 12.0) > 0.1,
            "the rebuilt sweep is expected to differ; the test is meaningless otherwise");
        Assert.Equal(12.0, Math.Log2(result.AchievedFrequencyRatio), 6);
    }

    [Fact]
    public async Task ACapturedMeasurement_ReportsTheSweptBandNotTheRequestedOne()
    {
        (MeasurementResult result, _) = await MeasureAsync(1000, 20_000, 1.0);

        Assert.Equal(1000.0, result.LowFrequencyHz);
        Assert.Equal(20_000.0, result.HighFrequencyHz);
        Assert.True(result.AchievedLowFrequencyHz < 1000.0);
        Assert.True(result.AchievedHighFrequencyHz > 20_000.0);
        Assert.True(result.AchievedFrequencyRatio > 20_000.0 / 1000.0);
    }

    [Fact]
    public async Task AStoredMeasurement_CarriesTheSweptBandForHarmonicAnalysis()
    {
        // Requested 1-20 kHz is 4.32 octaves; the sweep runs ~5.08.
        (MeasurementResult result, int generatedSamples) = await MeasureAsync(1000, 20_000, 4.3);

        double requestedRatio = 20_000.0 / 1000.0;
        double achievedRatio = result.AchievedFrequencyRatio;
        Assert.True(achievedRatio > requestedRatio * 1.5, "guard bands widen the sweep");

        double correct = generatedSamples * Math.Log(2.0) / Math.Log(achievedRatio);
        double wrong = generatedSamples * Math.Log(2.0) / Math.Log(requestedRatio);
        Assert.Equal(correct, result.HarmonicIROffset(2.0), 6);
        Assert.True(
            Math.Abs(wrong - correct) / result.SampleRate > 0.05,
            "the two bands must disagree by more than 50 ms, or this guards nothing");
    }

    [Fact]
    public void RestoringASweepLongerThanTheGenerationCap_KeepsItsRealLength()
    {
        // The generator caps at MaxDurationSeconds but files store up to an hour; the rebuilt count would halve offsets at 200 s.
        const int sampleRate = 48_000;
        const double storedDurationSeconds = 200.0;
        Assert.True(storedDurationSeconds > ExponentialSineSweep.MaxDurationSeconds);

        MeasurementResult result = StoredFile(20, 20_000, 0, storedDurationSeconds).ToResult();

        Assert.Equal((int)Math.Round(storedDurationSeconds * sampleRate), result.SweepSampleCount);
        Assert.Equal(storedDurationSeconds, result.SweepSampleDurationSeconds, 6);
        int capped = (int)Math.Round(ExponentialSineSweep.MaxDurationSeconds * sampleRate);
        double expected = result.SweepSampleCount * Math.Log(2.0) / Math.Log(result.AchievedFrequencyRatio);
        Assert.Equal(expected, result.HarmonicIROffset(2.0), 6);
        Assert.True(
            result.HarmonicIROffset(2.0) >
                capped * Math.Log(2.0) / Math.Log(result.AchievedFrequencyRatio) * 1.5,
            "the capped sweep would have understated the offset by about half");
    }

    [Fact]
    public async Task ACapturedMeasurement_TakesItsSweepLengthFromTheGeneratedSweep()
    {
        (MeasurementResult result, int generatedSamples) = await MeasureAsync(20, 20_000, 2.0);

        Assert.Equal(generatedSamples, result.SweepSampleCount);
        Assert.Equal(2.0, result.SweepSampleDurationSeconds, 3);
    }

    [Fact]
    public void AStoredFile_ResolvesTheSweptBandNotTheRequest()
    {
        ImpulseResponseFile file = StoredFile(1000, 20_000, 0, 4.3);
        file.AchievedLowFrequencyHz = 707.294;
        file.AchievedHighFrequencyHz = 23_995.547;

        MeasurementResult result = file.ToResult();

        Assert.Equal(1000.0, result.LowFrequencyHz);
        Assert.Equal(20_000.0, result.HighFrequencyHz);
        Assert.Equal(707.294, result.AchievedLowFrequencyHz, 6);
        Assert.Equal(23_995.547, result.AchievedHighFrequencyHz, 6);
    }
}
