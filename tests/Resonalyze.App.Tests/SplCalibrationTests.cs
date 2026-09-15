using System.Numerics;
using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class SplCalibrationTests
{
    private static SplCalibration ValidAnchor() => new()
    {
        ReferenceLevelDbSpl = 94.0,
        MeasuredLevelDbFs = -20.5,
        ReferenceFrequencyHz = 1_000.0,
        MeasuredFrequencyHz = 1_001.0,
        CapturedAtUtc = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
        Backend = AudioBackend.WasapiShared,
        SampleRate = 48_000,
        Bits = 24,
        MicrophoneChannelOffset = 0,
        WasapiCaptureEndpointId = "capture-id"
    };

    [Fact]
    public void OffsetDb_IsReferenceMinusMeasured()
    {
        SplCalibration anchor = ValidAnchor();

        Assert.Equal(114.5, anchor.OffsetDb, 9);
    }

    [Fact]
    public void Validate_RejectsOutOfRangeValues()
    {
        Assert.Throws<InvalidDataException>(() =>
            new SplCalibration { ReferenceLevelDbSpl = 5_000, MeasuredLevelDbFs = -20, Bits = 24, SampleRate = 48_000 }.Validate());
        Assert.Throws<InvalidDataException>(() =>
            new SplCalibration { ReferenceLevelDbSpl = 94, MeasuredLevelDbFs = double.NaN, Bits = 24, SampleRate = 48_000 }.Validate());
        Assert.Throws<InvalidDataException>(() =>
            new SplCalibration { ReferenceLevelDbSpl = 94, MeasuredLevelDbFs = -20, Bits = 20, SampleRate = 48_000 }.Validate());
    }

    [Fact]
    public void MatchesInput_TrueOnlyForTheSameDigitalTract()
    {
        SplCalibration anchor = ValidAnchor();

        Assert.True(anchor.MatchesInput(
            AudioBackend.WasapiShared, 48_000, 24, 0, null, "capture-id", null));
        Assert.False(anchor.MatchesInput(
            AudioBackend.WasapiShared, 48_000, 24, 0, null, "other-id", null));
        Assert.False(anchor.MatchesInput(
            AudioBackend.WasapiShared, 44_100, 24, 0, null, "capture-id", null));
        Assert.False(anchor.MatchesInput(
            AudioBackend.WasapiShared, 48_000, 16, 0, null, "capture-id", null));
        Assert.False(anchor.MatchesInput(
            AudioBackend.WasapiShared, 48_000, 24, 1, null, "capture-id", null));
        Assert.False(anchor.MatchesInput(
            AudioBackend.Wave, 48_000, 24, 0, 1, null, null));
    }

    [Fact]
    public void NextRunHasSplAnchor_PredictsFromTheConfiguredCalibrationAndInput()
    {
        // Decided BEFORE a sweep: only the run ahead's anchor counts; the previous one dies when it starts.
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        Assert.False(measurement.NextRunHasSplAnchor);

        MeasurementInputIdentity identity = measurement.CurrentInputIdentity();
        var anchor = new SplCalibration
        {
            ReferenceLevelDbSpl = 94,
            MeasuredLevelDbFs = -20,
            Backend = identity.Backend,
            SampleRate = identity.SampleRate,
            Bits = identity.Bits,
            MicrophoneChannelOffset = identity.MicrophoneChannelOffset,
            InputDeviceNumber = identity.InputDeviceNumber,
            WasapiCaptureEndpointId = identity.WasapiCaptureEndpointId,
            AsioDriverName = identity.AsioDriverName
        };
        measurement.SplCalibration = anchor;
        Assert.True(measurement.NextRunHasSplAnchor);

        anchor.SampleRate = identity.SampleRate + 1;
        Assert.False(measurement.NextRunHasSplAnchor);
    }

    [Fact]
    public async Task ImpulseResponseFile_RoundTripsTheAnchor()
    {
        string path = Path.Combine(Path.GetTempPath(), $"resonalyze-ir-{Guid.NewGuid():N}.json");
        var file = new ImpulseResponseFile
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            SampleRate = 48_000,
            Bits = 24,
            Octaves = 10,
            SweepDurationSeconds = 1.0,
            PlayChannel = PlaybackChannel.Mono,
            SweepDeconvolutionPeakIndex = 1,
            SweepDeconvolutionRealSamples = [0.0, 1.0, 0.0],
            SplCalibration = ValidAnchor()
        };

        try
        {
            await file.SaveAsync(path);

            string json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"splCalibration\"", json);
            Assert.DoesNotContain("\"offsetDb\"", json);

            ImpulseResponseFile loaded = await ImpulseResponseFile.LoadAsync(path);
            Assert.NotNull(loaded.SplCalibration);
            Assert.Equal(94.0, loaded.SplCalibration.ReferenceLevelDbSpl);
            Assert.Equal(-20.5, loaded.SplCalibration.MeasuredLevelDbFs);
            Assert.Equal(1_001.0, loaded.SplCalibration.MeasuredFrequencyHz);
            Assert.Equal(AudioBackend.WasapiShared, loaded.SplCalibration.Backend);
            Assert.Equal("capture-id", loaded.SplCalibration.WasapiCaptureEndpointId);
            Assert.Equal(114.5, loaded.SplCalibration.OffsetDb, 9);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ImpulseResponseFile_LoadsVersion6WithoutAnAnchor()
    {
        string path = Path.Combine(Path.GetTempPath(), $"resonalyze-ir-{Guid.NewGuid():N}.json");
        const string json = """
            {
              "format": "resonalyze-impulse-response",
              "version": 6,
              "sampleRate": 48000,
              "bits": 24,
              "octaves": 10,
              "sweepDurationSeconds": 1.0,
              "playChannel": "Mono",
              "measurementMode": "SweepDeconvolution",
              "sweepDeconvolutionPeakIndex": 0,
              "sweepDeconvolutionRealSamples": [1.0]
            }
            """;

        try
        {
            await File.WriteAllTextAsync(path, json);

            ImpulseResponseFile loaded = await ImpulseResponseFile.LoadAsync(path);

            Assert.Null(loaded.SplCalibration);
            Assert.Equal(48_000, loaded.SampleRate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SplCalibration MatchingWaveAnchor() => new()
    {
        ReferenceLevelDbSpl = 94.0,
        MeasuredLevelDbFs = -20.5,
        Backend = AudioBackend.Wave,
        SampleRate = 44_100,
        Bits = 24,
        MicrophoneChannelOffset = 0,
        InputDeviceNumber = -1
    };

    private static readonly MeasurementInputIdentity WaveInput =
        new(AudioBackend.Wave, 44_100, 24, 0, -1, null, null);

    private static ExpSweepMeasurement RestoredMeasurement(
        SplCalibration? anchor, MeasurementInputIdentity? input = null)
    {
        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Right,
            sweepDeconvolutionImpulseResponse: [Complex.Zero, Complex.One, Complex.Zero],
            sweepDeconvolutionPeakIndex: 1);
        // Restore clears both via Init; Capture validates against these, not the app's current device.
        measurement.MeasurementSplCalibration = anchor;
        measurement.MeasurementInput = input ?? WaveInput;
        return measurement;
    }

    [Fact]
    public void Capture_StampsTheMeasurementsActiveAnchor()
    {
        using ExpSweepMeasurement measurement = RestoredMeasurement(MatchingWaveAnchor());

        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);

        Assert.NotNull(file.SplCalibration);
        Assert.Equal(94.0, file.SplCalibration.ReferenceLevelDbSpl);
        Assert.Equal(114.5, file.SplCalibration.OffsetDb, 9);
    }

    [Fact]
    public async Task Capture_RejectsABackendDiscontinuityEvenWithACleanTone()
    {
        const int sampleRate = 48_000;
        var factory = new FakeAudioSessionFactory(
            streamingFactory: _ => new ToneWithDiscontinuitySession(sampleRate));
        var request = new AudioSessionRequest(
            AudioBackend.Wave, sampleRate, 24, PlaybackChannel.Mono,
            new AudioCaptureRouting(0, null));

        SplCalibrationCaptureResult result = await new SplCalibrationListener(factory)
            .CaptureAsync(
                request,
                frameLength: 2048,
                SplToneCriteria.Default,
                // Only a hang guard: a 300 ms budget cancelled a stalled CI run before its discontinuity frame.
                TimeSpan.FromSeconds(30),
                progress: null,
                CancellationToken.None);

        Assert.True(result.Reading.HasClearPeak);
        Assert.True(result.Overran);
        Assert.Equal(
            SplCalibrationFailure.CaptureOverrun, SplCalibrationListener.Evaluate(result));
    }

    private sealed class ToneWithDiscontinuitySession : IAudioStreamingSession
    {
        private readonly int sampleRate;

        public ToneWithDiscontinuitySession(int sampleRate) => this.sampleRate = sampleRate;

        public event Action<AudioCaptureFrame>? FrameAvailable;
        public event Action<AudioInputLevels>? InputLevelsAvailable { add { } remove { } }
        public event Action? CaptureDiscontinuity;

        public async Task RunAsync(
            AudioPlaybackSignal loopingSignal, int sequenceLength, CancellationToken cancellationToken)
        {
            double phase = 0.0;
            double delta = 2.0 * Math.PI * 1_000.0 / sampleRate;

            // Fixed frame count within channel capacity and no wall-clock pacing, so the test is deterministic on loaded CI.
            for (int frame = 0; frame < 8 && !cancellationToken.IsCancellationRequested; frame++)
            {
                var block = new float[sequenceLength];
                for (int i = 0; i < block.Length; i++)
                {
                    block[i] = (float)(0.2 * Math.Sin(phase));
                    phase += delta;
                }

                FrameAvailable?.Invoke(new AudioCaptureFrame([block], 0, null));
                if (frame == 5)
                {
                    CaptureDiscontinuity?.Invoke();
                }

                await Task.Yield();
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void Evaluate_RejectsAnOverrunEvenWhenTheToneLooksPerfect()
    {
        var clearTone = new SplToneReading(
            PeakFrequencyHz: 1_000,
            LevelDbFs: -13,
            ProminenceDb: 60,
            WithinFrequencyTolerance: true,
            HasClearPeak: true);
        var overran = new SplCalibrationCaptureResult(
            clearTone, InputPeakDbFs: -3, Clipped: false, LevelStabilityDb: 0.1,
            FramesAnalyzed: 20, Overran: true);
        var clean = overran with { Overran = false };

        Assert.Equal(SplCalibrationFailure.CaptureOverrun, SplCalibrationListener.Evaluate(overran));
        Assert.Equal(SplCalibrationFailure.None, SplCalibrationListener.Evaluate(clean));
    }

    [Fact]
    public void Capture_DropsAnAnchorCapturedOnADifferentInput()
    {
        using ExpSweepMeasurement measurement = RestoredMeasurement(ValidAnchor());

        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);

        Assert.Null(file.SplCalibration);
    }

    [Fact]
    public void Capture_KeepsALoadedFilesAnchorWhenReSavedOnAnotherDevice()
    {
        // Re-saving validates the anchor against the file's own input identity, not the current device.
        SplCalibration anchor = ValidAnchor();
        using ExpSweepMeasurement measurement =
            RestoredMeasurement(anchor, anchor.CaptureIdentity);

        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);

        Assert.NotNull(file.SplCalibration);
        Assert.Equal(114.5, file.SplCalibration.OffsetDb, 9);
    }

    [Fact]
    public void SettingsFile_RoundTripsTheAnchor()
    {
        string path = Path.Combine(Path.GetTempPath(), $"resonalyze-settings-{Guid.NewGuid():N}.json");
        try
        {
            MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);
            settings.Measurement.SplCalibration = ValidAnchor();
            settings.Save();

            MeasurementSettingsFile loaded = MeasurementSettingsFile.LoadOrDefault(path);

            Assert.NotNull(loaded.Measurement.SplCalibration);
            Assert.Equal(94.0, loaded.Measurement.SplCalibration.ReferenceLevelDbSpl);
            Assert.Equal(-20.5, loaded.Measurement.SplCalibration.MeasuredLevelDbFs);
            Assert.Equal("capture-id", loaded.Measurement.SplCalibration.WasapiCaptureEndpointId);
            Assert.Equal(114.5, loaded.Measurement.SplCalibration.OffsetDb, 9);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SettingsFile_Version8LoadsWithoutAnAnchorAndUpgrades()
    {
        string path = Path.Combine(Path.GetTempPath(), $"resonalyze-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"SchemaVersion\":8}");

            MeasurementSettingsFile loaded = MeasurementSettingsFile.LoadOrDefault(path);

            Assert.Null(loaded.Measurement.SplCalibration);
            Assert.Null(loaded.LoadWarning);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SettingsFile_DropsABrokenAnchorInsteadOfFailingTheWholeLoad()
    {
        string path = Path.Combine(Path.GetTempPath(), $"resonalyze-settings-{Guid.NewGuid():N}.json");
        try
        {
            MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);
            settings.Measurement.SplCalibration = new SplCalibration
            {
                ReferenceLevelDbSpl = 5_000,
                MeasuredLevelDbFs = -20.0,
                Backend = AudioBackend.Wave,
                SampleRate = 48_000,
                Bits = 24
            };
            settings.Save();

            MeasurementSettingsFile loaded = MeasurementSettingsFile.LoadOrDefault(path);

            Assert.Null(loaded.Measurement.SplCalibration);
            Assert.Null(loaded.LoadWarning);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
