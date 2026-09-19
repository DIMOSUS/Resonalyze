using System.Numerics;
using Resonalyze.Integration.Rew;

namespace Resonalyze.App.Tests;

/// <summary>One result for every input: what a file holds comes back out of it, and each import fills what it cannot know.</summary>
public sealed class MeasurementResultTests
{
    // A re-saved file used to lose these: the engine it was restored into had never run a session.
    [Fact]
    public void AReSavedFileKeepsItsAudioSessionDiagnostics()
    {
        ImpulseResponseFile loaded = ImpulseResponseFileAtomicSaveTests.CreateFile(sampleValue: 1.0);
        loaded.AudioSession = new ImpulseResponseFile.AudioSessionFileEntry
        {
            Backend = "WasapiExclusive",
            CaptureEndpointId = "capture-id",
            RenderEndpointId = "render-id",
            CaptureSampleRate = 48_000,
            RenderSampleRate = 48_000,
            AnalysisSampleRate = 48_000,
            RequestedBufferMilliseconds = 10,
            ActualBufferFrames = 480,
            CapturePackets = 1_234
        };

        ImpulseResponseFile saved = ImpulseResponseFile.From(loaded.ToResult());

        Assert.NotNull(saved.AudioSession);
        Assert.Equal("capture-id", saved.AudioSession!.CaptureEndpointId);
        Assert.Equal(480, saved.AudioSession.ActualBufferFrames);
        Assert.Equal(1_234, saved.AudioSession.CapturePackets);
    }

    [Fact]
    public void AFileWithoutFullAmplitudeEdgesReadsOverItsAchievedBand()
    {
        ImpulseResponseFile file = ImpulseResponseFileAtomicSaveTests.CreateFile(sampleValue: 1.0);
        file.LowFrequencyHz = 1_000;
        file.HighFrequencyHz = 20_000;
        file.AchievedLowFrequencyHz = 707.3;
        file.AchievedHighFrequencyHz = 23_995.5;

        MeasurementResult result = file.ToResult();

        Assert.Equal(707.3, result.MeasuredLowFrequencyHz);
        Assert.Equal(23_995.5, result.MeasuredHighFrequencyHz);
    }

    [Fact]
    public void AFileKeepsTheTimeItWasMeasuredNotTheTimeItWasSaved()
    {
        ImpulseResponseFile file = ImpulseResponseFileAtomicSaveTests.CreateFile(sampleValue: 1.0);
        var measured = new DateTimeOffset(2026, 3, 4, 9, 30, 0, TimeSpan.Zero);
        file.MeasuredAtUtc = measured;

        Assert.Equal(measured, file.ToResult().MeasuredAtUtc);

        file.MeasuredAtUtc = default;
        Assert.Equal(file.SavedAtUtc, file.ToResult().MeasuredAtUtc);
    }

    // Both REW routes build it: a loopback transfer, uncalibrated, with nothing REW does not state.
    [Fact]
    public void AnImportFromRewIsAnUncalibratedLoopbackTransfer()
    {
        double[] samples = [0.0, 1.0, 0.25, 0.0];
        double[] referenced = [1.0, 0.25, 0.0, 0.0];
        DateTimeOffset before = DateTimeOffset.UtcNow;

        MeasurementResult result = RewMeasurementImport.ToResult(
            samples,
            referenced,
            48_000,
            lowHz: null,
            highHz: null,
            sweepLengthSamples: 96_000,
            sweepCount: 0,
            TimingReference.SynchronizedLoopback);

        Assert.Equal(SweepMeasurementMode.LoopbackTransfer, result.MeasurementMode);
        Assert.Equal(RewMeasurementImport.ImportedBitDepth, result.Bits);
        Assert.Equal(PlaybackChannel.Mono, result.PlaybackChannel);
        Assert.Equal(RewMeasurementImport.FallbackLowFrequencyHz, result.LowFrequencyHz);
        Assert.Equal(24_000, result.HighFrequencyHz);
        Assert.Equal(result.LowFrequencyHz, result.MeasuredLowFrequencyHz);
        Assert.Equal(2.0, result.SweepDurationSeconds);
        Assert.Equal(1, result.AverageRunCount);
        Assert.Equal(1, result.SweepDeconvolution.PeakIndex);
        Assert.Equal(0, result.Transfer!.PeakIndex);
        Assert.Null(result.SplCalibration);
        Assert.Null(result.MicrophoneCalibration);
        Assert.False(result.Levels.Loopback.Available);
        Assert.InRange(result.MeasuredAtUtc, before, DateTimeOffset.UtcNow);
    }

    // A recording carries no time of its own, so it is stamped when it is imported.
    [Fact]
    public void ARecordedSweepIsStampedWhenItIsImported()
    {
        var configuration = new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(200, 5_000, 48_000, 24, 0.2, PlaybackChannel.Mono),
            new SweepAudioConfiguration(WaveInputChannelOffset: 0, WaveLoopbackInputChannelOffset: 1),
            new SweepAveragingConfiguration(1));
        using var sweep = new ExponentialSineSweep();
        sweep.FillData(200, 5_000, 0.2, 24, 48_000);
        var recording = new float[2_000 + sweep.SweepData.Length + 4_096];
        for (int i = 0; i < sweep.SweepData.Length; i++)
        {
            recording[2_000 + i] = sweep.SweepData[i] * 0.3f;
        }

        DateTimeOffset before = DateTimeOffset.UtcNow;
        MeasurementResult result =
            ExpSweepMeasurement.ImportRecordedSweep(configuration, recording, 48_000).Result;

        Assert.InRange(result.MeasuredAtUtc, before, DateTimeOffset.UtcNow);
        Assert.Equal(TimingReference.RecordedSweep, result.TimingReference);
    }

    [Fact]
    public void ATransferPeakOutsideItsResponseIsRefused()
    {
        MeasurementResult result = TestMeasurementResults.Restored(
            20, 20_000, 48_000, 24, 1.0, PlaybackChannel.Mono,
            [Complex.Zero, Complex.One],
            sweepDeconvolutionPeakIndex: 1);

        MeasurementResult broken = result with
        {
            MeasurementMode = SweepMeasurementMode.LoopbackTransfer,
            Transfer = new MeasurementImpulseResponse([Complex.One], 3)
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => broken.Validated());
    }
}
