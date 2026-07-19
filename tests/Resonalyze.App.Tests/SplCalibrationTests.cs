using System.Numerics;
using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// The SPL calibration anchor and its persistence into the settings and impulse
/// response files. The anchor is stored as raw ingredients (reference level,
/// measured level, capture identity); these tests hold that they round-trip and
/// that a broken anchor never takes a whole file down with it.
/// </summary>
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
        // A different endpoint, rate, bits, or channel each break the match.
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
            // The derived offset is never written; it is recomputed on load.
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
        // The version bump (6 -> 7) must not reject a file written before the
        // anchor existed; it simply carries none.
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

    // Matches the default (Wave, 44.1 kHz, 24-bit, channel 0) input a restored
    // measurement carries, so the anchor is stamped rather than dropped.
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

    // The default Wave 44.1 kHz / 24-bit / channel-0 input a fresh restored
    // measurement represents.
    private static readonly MeasurementInputIdentity WaveInput =
        new(AudioBackend.Wave, 44_100, 24, 0, -1, null, null);

    private static ExpSweepMeasurement RestoredMeasurement(
        SplCalibration? anchor, MeasurementInputIdentity? input = null)
    {
        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.RestoreImpulseResponse(
            octaves: 12,
            sampleRate: 44_100,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Right,
            sweepDeconvolutionImpulseResponse: [Complex.Zero, Complex.One, Complex.Zero],
            sweepDeconvolutionPeakIndex: 1);
        // The result's own frozen calibration and input identity (Restore clears both
        // via Init); Capture stamps and validates against these, not the configured
        // calibration or the app's current device.
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
                // The fake session ends the capture itself after its fixed frame
                // count, so the duration is only a hang guard and must sit far
                // above CI scheduling noise: a 300 ms budget lost the race on a
                // stalled runner and cancelled the session BEFORE its
                // discontinuity frame, reading Overran == false.
                TimeSpan.FromSeconds(30),
                progress: null,
                CancellationToken.None);

        // The tone itself is clean, but a single backend discontinuity (loss before
        // the FFT queue) must still reject the capture.
        Assert.True(result.Reading.HasClearPeak);
        Assert.True(result.Overran);
        Assert.Equal(
            SplCalibrationFailure.CaptureOverrun, SplCalibrationListener.Evaluate(result));
    }

    // Delivers a clean, continuous 1 kHz tone and raises CaptureDiscontinuity once.
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

            // Deliver a fixed, small number of frames (within the listener's bounded
            // channel capacity, so none is dropped by flooding), raise one backend
            // discontinuity, then return to end the capture. No wall-clock delay: the
            // test must be deterministic on a loaded CI runner, not race the capture
            // duration (paced frames vs a fixed timeout previously flaked here).
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

                // Yield so the processing task drains the channel between frames,
                // keeping the queue well under capacity regardless of scheduling.
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
        // ValidAnchor is a WASAPI 48 kHz calibration; the measurement ran on the
        // default Wave 44.1 kHz input. A mismatched anchor must not ride along, or a
        // reopened file would show a confidently wrong dB SPL offset.
        using ExpSweepMeasurement measurement = RestoredMeasurement(ValidAnchor());

        ImpulseResponseFile file = ImpulseResponseFile.Capture(measurement);

        Assert.Null(file.SplCalibration);
    }

    [Fact]
    public void Capture_KeepsALoadedFilesAnchorWhenReSavedOnAnotherDevice()
    {
        // A file measured on WASAPI, reopened while the app is configured for Wave.
        // Its result identity is the file's own input (the anchor's capture identity),
        // so re-saving validates the anchor against that — not the current Wave device
        // — and keeps it. Without this, Save would silently drop the calibration.
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
                ReferenceLevelDbSpl = 5_000, // out of range
                MeasuredLevelDbFs = -20.0,
                Backend = AudioBackend.Wave,
                SampleRate = 48_000,
                Bits = 24
            };
            settings.Save();

            MeasurementSettingsFile loaded = MeasurementSettingsFile.LoadOrDefault(path);

            Assert.Null(loaded.Measurement.SplCalibration);
            // The rest of the settings survived — no backup, no fresh-start warning.
            Assert.Null(loaded.LoadWarning);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
