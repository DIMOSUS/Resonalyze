using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class MeasurementSettingsMigrationTests
{
    [Fact]
    public void LegacySeparateLoopbackDeviceResetsTheLoopbackChannel()
    {
        var settings = new MeasurementSettingsFile();
        settings.Measurement.InputDeviceNumber = 1;
        settings.Measurement.WaveInputChannelOffset = 0;
        settings.Measurement.WaveLoopbackInputChannelOffset = 0;
        settings.Measurement.WaveLoopbackDeviceNumber = 3;

        Migrate(settings);

        Assert.Null(settings.Measurement.WaveLoopbackInputChannelOffset);
        Assert.Null(settings.Measurement.WaveLoopbackDeviceNumber);
        Assert.True(settings.LegacyDualDeviceLoopbackReset);
        Assert.False(settings.Measurement.HasLoopbackConfigured);
    }

    [Fact]
    public void LegacyLoopbackOnTheMicrophoneDeviceIsKept()
    {
        var settings = new MeasurementSettingsFile();
        settings.Measurement.InputDeviceNumber = 1;
        settings.Measurement.WaveInputChannelOffset = 0;
        settings.Measurement.WaveLoopbackInputChannelOffset = 1;
        settings.Measurement.WaveLoopbackDeviceNumber = 1;

        Migrate(settings);

        Assert.Equal(1, settings.Measurement.WaveLoopbackInputChannelOffset);
        Assert.Null(settings.Measurement.WaveLoopbackDeviceNumber);
        Assert.False(settings.LegacyDualDeviceLoopbackReset);
    }

    [Fact]
    public void ModernFileWithoutTheLegacyFieldIsUntouched()
    {
        var settings = new MeasurementSettingsFile();
        settings.Measurement.WaveLoopbackInputChannelOffset = 1;

        Migrate(settings);

        Assert.Equal(1, settings.Measurement.WaveLoopbackInputChannelOffset);
        Assert.False(settings.LegacyDualDeviceLoopbackReset);
    }

    [Fact]
    public void WasapiEndpointIdsAndBufferAreCapturedWithoutOpeningHardware()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                20,
                20_000,
                48_000,
                24,
                1.0,
                PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                Backend: AudioBackend.WasapiShared,
                WaveInputChannelOffset: 0,
                WaveLoopbackInputChannelOffset: 1,
                WasapiCaptureEndpointId: "capture-id",
                WasapiRenderEndpointId: "render-id",
                WasapiBufferMilliseconds: 40,
                WasapiCaptureEndpointName: "USB Input",
                WasapiRenderEndpointName: "USB Output"),
            new SweepAveragingConfiguration()));

        MeasurementSettingsFile.SweepMeasurementSettings captured =
            MeasurementSettingsFile.SweepMeasurementSettings.Capture(measurement);

        Assert.Equal(AudioBackend.WasapiShared, captured.AudioBackend);
        Assert.Equal("capture-id", captured.WasapiCaptureEndpointId);
        Assert.Equal("render-id", captured.WasapiRenderEndpointId);
        Assert.Equal(40, captured.WasapiBufferMilliseconds);
        Assert.Equal("USB Input", captured.WasapiCaptureEndpointName);
        Assert.Equal("USB Output", captured.WasapiRenderEndpointName);
    }

    [Fact]
    public void MeasurementTime_SurvivesASaveAndIsNotRestamped()
    {
        // The measurement time, not the save stamp, is the only evidence two array channels came from one sitting.
        var measured = new DateTimeOffset(2026, 3, 4, 9, 30, 0, TimeSpan.Zero);
        MeasurementResult restored = TestMeasurementResults.Restored(
            lowFrequencyHz: 20,
            highFrequencyHz: 20_000,
            sampleRate: 48_000,
            bits: 24,
            sweepDurationSeconds: 1.0,
            playChannel: PlaybackChannel.Mono,
            sweepDeconvolutionImpulseResponse: new System.Numerics.Complex[1024],
            sweepDeconvolutionPeakIndex: 0,
            measuredAtUtc: measured);

        Assert.Equal(measured, restored.MeasuredAtUtc);

        ImpulseResponseFile saved = ImpulseResponseFile.From(restored);
        Assert.Equal(measured, saved.MeasuredAtUtc);
        Assert.NotEqual(measured, saved.SavedAtUtc);
    }

    [Fact]
    public void ProtectiveHighPass_RoundTripsThroughCapturedSettings()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                20,
                20_000,
                48_000,
                24,
                1.0,
                PlaybackChannel.Mono),
            new SweepAudioConfiguration(),
            new SweepAveragingConfiguration(),
            new ProtectiveHighPassConfiguration(
                ProtectiveHighPassKind.Butterworth,
                3_150,
                36)));

        MeasurementSettingsFile.SweepMeasurementSettings captured =
            MeasurementSettingsFile.SweepMeasurementSettings.Capture(measurement);
        SweepMeasurementConfiguration rebuilt = captured.BuildConfiguration();

        Assert.Equal(ProtectiveHighPassKind.Butterworth, captured.ProtectiveHighPassKind);
        Assert.Equal(3_150, captured.ProtectiveHighPassFrequencyHz);
        Assert.Equal(36, captured.ProtectiveHighPassSlopeDbPerOctave);
        Assert.Equal(measurement.ProtectiveHighPass, rebuilt.ProtectiveHighPass);
    }

    [Fact]
    public void ProtectiveHighPass_InvalidLinkwitzRileySlopeFallsBackToTwentyFour()
    {
        var settings = new MeasurementSettingsFile.SweepMeasurementSettings
        {
            ProtectiveHighPassKind = ProtectiveHighPassKind.LinkwitzRiley,
            ProtectiveHighPassFrequencyHz = 2_500,
            ProtectiveHighPassSlopeDbPerOctave = 18
        };

        ProtectiveHighPassConfiguration protectiveHighPass =
            settings.BuildConfiguration().ProtectiveHighPass!;

        Assert.Equal(ProtectiveHighPassKind.LinkwitzRiley, protectiveHighPass.Kind);
        Assert.Equal(2_500, protectiveHighPass.FrequencyHz);
        Assert.Equal(24, protectiveHighPass.SlopeDbPerOctave);
    }

    [Fact]
    public void WasapiChannelOffsetsAreNotLimitedToStereo()
    {
        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        measurement.Init(new SweepMeasurementConfiguration(
            new SweepSignalConfiguration(
                20,
                20_000,
                48_000,
                24,
                1.0,
                PlaybackChannel.Mono),
            new SweepAudioConfiguration(
                Backend: AudioBackend.WasapiShared,
                WaveInputChannelOffset: 5,
                WaveLoopbackInputChannelOffset: 7,
                WasapiCaptureEndpointId: "capture-id",
                WasapiRenderEndpointId: "render-id"),
            new SweepAveragingConfiguration()));

        Assert.Equal(5, measurement.WaveInputChannelOffset);
        Assert.Equal(7, measurement.WaveLoopbackInputChannelOffset);
    }

    // Real JSON without the fields: System.Text.Json never assigns a missing property, so initializers survive.
    // A custom gate offset stays manual; an untouched default gets Auto.
    [Fact]
    public void PreAutoFileWithACustomGateOffsetStaysManual()
    {
        MeasurementSettingsFile.FrequencyResponseSettings settings =
            DeserializeFrequencyResponse(
                """{"PhaseGateOffsetMs": 6.5, "GroupDelayGateOffsetMs": 12.25}""");
        var options = new FrequencyResponseOptions();

        settings.ApplyTo(options, new CurveVisibilityOptions());

        Assert.False(options.PhaseGateAutoFit);
        Assert.Equal(6.5, options.PhaseGateOffsetMs);
        Assert.False(options.GroupDelayGateAutoFit);
        Assert.Equal(12.25, options.GroupDelayGateOffsetMs);
    }

    [Fact]
    public void PreAutoFileWithTheDefaultGateOffsetGetsAuto()
    {
        MeasurementSettingsFile.FrequencyResponseSettings settings =
            DeserializeFrequencyResponse("""{"PhasePlateauMs": 4.0}""");
        var options = new FrequencyResponseOptions();

        settings.ApplyTo(options, new CurveVisibilityOptions());

        Assert.True(options.PhaseGateAutoFit);
        Assert.True(options.GroupDelayGateAutoFit);
    }

    [Fact]
    public void StoredAutoFitChoiceIsAppliedAsIs()
    {
        MeasurementSettingsFile.FrequencyResponseSettings settings =
            DeserializeFrequencyResponse(
                """
                {"PhaseGateAutoFit": false,
                 "GroupDelayGateAutoFit": true, "GroupDelayGateOffsetMs": 12.25}
                """);
        var options = new FrequencyResponseOptions();

        settings.ApplyTo(options, new CurveVisibilityOptions());

        Assert.False(options.PhaseGateAutoFit);
        Assert.True(options.GroupDelayGateAutoFit);
    }

    [Fact]
    public void StoredFadesAreContainedInTheirWindow_AndOddCyclesReadAsTheDefault()
    {
        MeasurementSettingsFile.FrequencyResponseSettings settings =
            DeserializeFrequencyResponse(
                """
                {"Window": 1000, "LeftTukeyWindow": 700, "RightTukeyWindow": 700,
                 "PhaseFdwCycles": 5, "GroupDelayFdwCycles": 7, "MagnitudeFdwCycles": 3}
                """);
        var options = new FrequencyResponseOptions();

        settings.ApplyTo(options, new CurveVisibilityOptions());

        Assert.Equal((700, 300), (options.LeftTukeyWindow, options.RightTukeyWindow));
        int fallback = PhaseAnalysisSettings.DefaultFdwCycles;
        Assert.Equal((fallback, fallback, fallback), (options.PhaseFdwCycles, options.GroupDelayFdwCycles, options.MagnitudeFdwCycles));
    }

    [Fact]
    public void PreFdwMagnitudeFileStaysFixed()
    {
        MeasurementSettingsFile.FrequencyResponseSettings settings =
            DeserializeFrequencyResponse("""{"Window": 2048}""");
        var options = new FrequencyResponseOptions
        {
            MagnitudeWindowMode = PhaseWindowMode.FrequencyDependent,
            MagnitudeFdwCycles = 8
        };

        settings.ApplyTo(options, new CurveVisibilityOptions());

        Assert.Equal(PhaseWindowMode.Fixed, options.MagnitudeWindowMode);
        Assert.Equal(
            PhaseAnalysisSettings.DefaultFdwCycles, options.MagnitudeFdwCycles);
    }

    [Fact]
    public void MagnitudeFdwRoundTripsAndValidatesCycles()
    {
        var stored = new FrequencyResponseOptions
        {
            MagnitudeWindowMode = PhaseWindowMode.FrequencyDependent,
            MagnitudeFdwCycles = 8
        };
        var restored = new FrequencyResponseOptions();

        MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                stored, new CurveVisibilityOptions())
            .ApplyTo(restored, new CurveVisibilityOptions());

        Assert.Equal(PhaseWindowMode.FrequencyDependent, restored.MagnitudeWindowMode);
        Assert.Equal(8, restored.MagnitudeFdwCycles);

        MeasurementSettingsFile.FrequencyResponseSettings settings =
            DeserializeFrequencyResponse("""{"MagnitudeFdwCycles": 123}""");
        var options = new FrequencyResponseOptions();

        settings.ApplyTo(options, new CurveVisibilityOptions());

        Assert.Equal(
            PhaseAnalysisSettings.DefaultFdwCycles, options.MagnitudeFdwCycles);
    }

    [Fact]
    public void GroupDelayCurveFlags_DefaultOnForOldFilesAndRoundTripWhenOff()
    {
        MeasurementSettingsFile.FrequencyResponseSettings legacy =
            DeserializeFrequencyResponse("""{"ShowGroupDelay": false}""");
        var visibility = new CurveVisibilityOptions();

        legacy.ApplyTo(new FrequencyResponseOptions(), visibility);

        Assert.False(visibility.ShowGroupDelay);
        Assert.True(visibility.ShowMinimumPhaseGroupDelay);
        Assert.True(visibility.ShowExcessGroupDelay);

        var stored = new CurveVisibilityOptions
        {
            ShowMinimumPhaseGroupDelay = false,
            ShowExcessGroupDelay = false
        };
        var restored = new CurveVisibilityOptions();
        MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                new FrequencyResponseOptions(), stored)
            .ApplyTo(new FrequencyResponseOptions(), restored);

        Assert.False(restored.ShowMinimumPhaseGroupDelay);
        Assert.False(restored.ShowExcessGroupDelay);
    }

    // Told apart by schema version, not a missing field, because the field's default must be FDW for a first run.
    [Fact]
    public void PreWindowGroupDelayFileStaysFixed()
    {
        string path = Path.Combine(Path.GetTempPath(), $"resonalyze-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """{"SchemaVersion": 12, "GroupDelay": {"GroupDelayPlateauMs": 8.0}}""");
            MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);
            var options = new FrequencyResponseOptions();

            settings.GroupDelay.ApplyTo(options, new CurveVisibilityOptions());

            Assert.Equal(PhaseWindowMode.Fixed, options.GroupDelayWindowMode);
            Assert.Equal(PhaseAnalysisSettings.DefaultFdwCycles, options.GroupDelayFdwCycles);
            Assert.Equal(8.0, options.GroupDelayPlateauMs);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The first-run default must survive ApplyTo, not just sit on a fresh options object.
    [Fact]
    public void FirstRunStartsTheGroupDelayOnFdw()
    {
        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(
            Path.Combine(Path.GetTempPath(), $"resonalyze-absent-{Guid.NewGuid():N}.json"));
        var options = new FrequencyResponseOptions { GroupDelayWindowMode = PhaseWindowMode.Fixed };

        settings.GroupDelay.ApplyTo(options, new CurveVisibilityOptions());

        Assert.Equal(PhaseWindowMode.FrequencyDependent, options.GroupDelayWindowMode);
        Assert.Equal(PhaseAnalysisSettings.DefaultFdwCycles, options.GroupDelayFdwCycles);
    }

    [Fact]
    public void GroupDelayWindowRoundTripsAndValidatesCycles()
    {
        var stored = new FrequencyResponseOptions
        {
            GroupDelayWindowMode = PhaseWindowMode.FrequencyDependent,
            GroupDelayFdwCycles = 8
        };
        var restored = new FrequencyResponseOptions
        {
            GroupDelayWindowMode = PhaseWindowMode.Fixed,
            GroupDelayFdwCycles = 4
        };

        MeasurementSettingsFile.FrequencyResponseSettings.Capture(
                stored, new CurveVisibilityOptions())
            .ApplyTo(restored, new CurveVisibilityOptions());

        Assert.Equal(PhaseWindowMode.FrequencyDependent, restored.GroupDelayWindowMode);
        Assert.Equal(8, restored.GroupDelayFdwCycles);

        MeasurementSettingsFile.FrequencyResponseSettings settings =
            DeserializeFrequencyResponse(
                """{"GroupDelayWindowMode": 0, "GroupDelayFdwCycles": 123}""");
        var options = new FrequencyResponseOptions();

        settings.ApplyTo(options, new CurveVisibilityOptions());

        Assert.Equal(PhaseWindowMode.Fixed, options.GroupDelayWindowMode);
        Assert.Equal(PhaseAnalysisSettings.DefaultFdwCycles, options.GroupDelayFdwCycles);
    }

    private static MeasurementSettingsFile.FrequencyResponseSettings
        DeserializeFrequencyResponse(string json) =>
        System.Text.Json.JsonSerializer
            .Deserialize<MeasurementSettingsFile.FrequencyResponseSettings>(json)!;

    private static void Migrate(MeasurementSettingsFile settings) =>
        settings.MigrateLegacyDualDeviceLoopback();
}
