using System.Text.Json;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>
/// The two fixed calibration slots became a named list. A settings file written
/// before that must keep pointing at the same curve: a CONFIGURED 90° file
/// becomes an entry every view still selects, while the old 90°-from-0°
/// approximation — which needed no file and no geometry — is not recreated, so
/// those views fall back to no correction instead of a curve nobody chose.
/// </summary>
public sealed class MicrophoneCalibrationMigrationTests : IDisposable
{
    private readonly string tempDirectory;

    public MicrophoneCalibrationMigrationTests()
    {
        tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "resonalyze-calibration-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void AConfiguredNinetyDegreeFileBecomesAnEntryEverySelectionFollows()
    {
        MeasurementSettingsFile settings = Load(WriteLegacySettings(
            ninetyDegreePath: @"C:\mics\ninety.txt",
            frequencyResponseMode: "Degrees90",
            liveSpectrumMode: "Degrees90",
            eqWizardMode: "Degrees90"));

        MicrophoneCalibrationDefinition entry = Assert.Single(
            settings.Measurement.AdditionalMicrophoneCalibrations);
        Assert.Equal(MicrophoneCalibrationDefinition.LegacyNinetyDegreesId, entry.Id);
        Assert.Equal("90°", entry.Name);
        Assert.Equal(MicrophoneCalibrationKind.File, entry.Kind);
        Assert.Equal(@"C:\mics\ninety.txt", entry.Path);
        Assert.Null(settings.Measurement.MicrophoneCalibration90DegreesPath);

        Assert.Equal(entry.Id, settings.FrequencyResponse.CalibrationId);
        Assert.Equal(entry.Id, settings.LiveSpectrum.CalibrationId);
        Assert.Equal(entry.Id, settings.EqWizard.CalibrationId);
    }

    [Fact]
    public void TheApproximatedNinetyDegreeSelectionFallsBackToNoCorrection()
    {
        MeasurementSettingsFile settings = Load(WriteLegacySettings(
            ninetyDegreePath: null,
            frequencyResponseMode: "Degrees90",
            liveSpectrumMode: "Degrees90",
            eqWizardMode: "Degrees90"));

        Assert.Empty(settings.Measurement.AdditionalMicrophoneCalibrations);
        Assert.Null(settings.FrequencyResponse.CalibrationId);
        Assert.Null(settings.LiveSpectrum.CalibrationId);
        Assert.Null(settings.EqWizard.CalibrationId);
    }

    [Fact]
    public void ZeroDegreesAndOffMigrateToTheirIds()
    {
        MeasurementSettingsFile settings = Load(WriteLegacySettings(
            ninetyDegreePath: null,
            frequencyResponseMode: "Degrees0",
            liveSpectrumMode: "Off",
            eqWizardMode: "Off"));

        Assert.Equal(
            MicrophoneCalibrationIds.ZeroDegrees,
            settings.FrequencyResponse.CalibrationId);
        Assert.Null(settings.LiveSpectrum.CalibrationId);
        Assert.Null(settings.EqWizard.CalibrationId);
    }

    [Fact]
    public void AFileOlderThanTheModesFallsBackToItsUseCalibrationFlag()
    {
        string path = WriteSettings("""
            {
              "SchemaVersion": 7,
              "FrequencyResponse": { "UseCalibration": true },
              "PhaseResponse": { "UseCalibration": false }
            }
            """);

        MeasurementSettingsFile settings = Load(path);

        Assert.Equal(
            MicrophoneCalibrationIds.ZeroDegrees,
            settings.FrequencyResponse.CalibrationId);
        Assert.Null(settings.PhaseResponse.CalibrationId);
    }

    [Fact]
    public void ACurrentFileWithoutASelectionStaysUncalibrated()
    {
        // The migration must not run again on a current file: an absent id there
        // is a deliberate "Off", not a pre-list file to be re-derived.
        string path = WriteSettings("""
            {
              "SchemaVersion": 11,
              "FrequencyResponse": { }
            }
            """);

        Assert.Null(Load(path).FrequencyResponse.CalibrationId);
    }

    [Fact]
    public void AFirstRunStartsCorrectedByTheMicrophonesOwnCalibration()
    {
        // No file at all: the measurement views used to start corrected (their
        // persisted flag defaulted to true), and configuring a 0° file must keep
        // working without visiting every mode's selector.
        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(
            Path.Combine(tempDirectory, "absent.json"));

        Assert.Equal(
            MicrophoneCalibrationIds.ZeroDegrees,
            settings.FrequencyResponse.CalibrationId);
        Assert.Equal(
            MicrophoneCalibrationIds.ZeroDegrees,
            settings.PhaseResponse.CalibrationId);
        Assert.Equal(
            MicrophoneCalibrationIds.ZeroDegrees,
            settings.GroupDelay.CalibrationId);
        Assert.Equal(
            MicrophoneCalibrationIds.ZeroDegrees,
            settings.LiveSpectrum.CalibrationId);
        // The EQ Wizard always defaulted to no correction.
        Assert.Null(settings.EqWizard.CalibrationId);

        var options = new FrequencyResponseOptions();
        settings.FrequencyResponse.ApplyTo(options, new CurveVisibilityOptions());
        Assert.Equal(MicrophoneCalibrationIds.ZeroDegrees, options.CalibrationId);
    }

    [Fact]
    public void CapturingTheMeasurementSettingsKeepsTheConfiguredCalibrations()
    {
        // Capture rebuilds the measurement section from the measurement itself,
        // which knows the audio configuration but nothing about calibration
        // files; applying Record Settings used to drop the list.
        var settings = new MeasurementSettingsFile();
        settings.Measurement.MicrophoneCalibration0DegreesPath = @"C:\mics\zero.txt";
        settings.Measurement.AdditionalMicrophoneCalibrations.Add(
            new MicrophoneCalibrationDefinition
            {
                Id = "cal1",
                Name = "Passenger 45°",
                Kind = MicrophoneCalibrationKind.Angle,
                AngleDegrees = 45
            });

        using var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        settings.CaptureFrom(
            measurement,
            new FrequencyResponseOptions(),
            new CurveVisibilityOptions(),
            new FrequencyResponseOptions(),
            new CurveVisibilityOptions(),
            new FrequencyResponseOptions(),
            new CurveVisibilityOptions(),
            new ImpulseResponseOptions(),
            new WaterfallGenerateOptions(),
            new WaterfallGenerateOptions(),
            new LiveSpectrumOptions(),
            new TimeAlignmentOptions());

        Assert.Equal(
            @"C:\mics\zero.txt",
            settings.Measurement.MicrophoneCalibration0DegreesPath);
        MicrophoneCalibrationDefinition kept = Assert.Single(
            settings.Measurement.AdditionalMicrophoneCalibrations);
        Assert.Equal("cal1", kept.Id);
        Assert.Equal(45, kept.AngleDegrees);
    }

    [Fact]
    public void TheStoredListIsNormalizedOnLoad()
    {
        string path = WriteSettings("""
            {
              "SchemaVersion": 11,
              "Measurement": {
                "AdditionalMicrophoneCalibrations": [
                  { "Id": "cal1", "Name": "File", "Kind": "File", "Path": "C:\\a.txt" },
                  { "Id": "cal2", "Name": "", "Kind": "Angle", "AngleDegrees": 140,
                    "FrontDiameterMm": 0 },
                  { "Id": "cal3", "Name": "Chained", "Kind": "Angle",
                    "AngleDegrees": 45, "BaseId": "cal2" },
                  { "Id": "cal1", "Name": "Duplicate id", "Kind": "File" },
                  { "Id": "", "Name": "No id", "Kind": "File" }
                ]
              }
            }
            """);

        List<MicrophoneCalibrationDefinition> definitions =
            Load(path).Measurement.AdditionalMicrophoneCalibrations;

        Assert.Equal(["cal1", "cal2", "cal3"], definitions.Select(entry => entry.Id));
        // Out-of-range geometry is clamped rather than reaching the model.
        Assert.Equal(90.0, definitions[1].AngleDegrees);
        Assert.Equal(
            MicrophoneCalibrationDefinition.DefaultFrontDiameterMm,
            definitions[1].FrontDiameterMm);
        Assert.Equal("90°", definitions[1].Name);
        // An estimate may only be derived from a file-backed entry, so a chain of
        // estimates falls back to the microphone's own 0° calibration.
        Assert.Null(definitions[2].BaseId);
    }

    [Fact]
    public void SavingDropsTheLegacyFieldsInsteadOfRewritingThem()
    {
        string path = WriteLegacySettings(
            ninetyDegreePath: @"C:\mics\ninety.txt",
            frequencyResponseMode: "Degrees90",
            liveSpectrumMode: "Off",
            eqWizardMode: "Off");

        Load(path).Save();
        string json = File.ReadAllText(path);

        Assert.DoesNotContain("CalibrationMode", json);
        Assert.DoesNotContain("MicrophoneCalibration90DegreesPath", json);
        Assert.DoesNotContain("UseCalibration", json);
        Assert.Contains(MicrophoneCalibrationDefinition.LegacyNinetyDegreesId, json);
    }

    [Fact]
    public void AVirtualDspProjectKeepsPointingAtTheMigratedEntry()
    {
        string root = Path.Combine(tempDirectory, "project");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "virtual-crossover.json"),
            JsonSerializer.Serialize(new
            {
                format = "resonalyze-virtual-crossover",
                version = 5,
                calibrationMode = "Degrees90",
                pairs = new[]
                {
                    new { }, new { }
                }
            }));

        VirtualCrossoverProjectFile project = VirtualCrossoverProjectFile.LoadOrDefault(root);

        Assert.Equal(VirtualCrossoverProjectFile.CurrentVersion, project.Version);
        Assert.Equal(
            MicrophoneCalibrationDefinition.LegacyNinetyDegreesId,
            project.CalibrationId);
        Assert.Null(project.CalibrationMode);
        Assert.Null(project.BackupNoticePath);
    }

    private MeasurementSettingsFile Load(string path)
    {
        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(path);
        Assert.Null(settings.LoadWarning);
        return settings;
    }

    private string WriteLegacySettings(
        string? ninetyDegreePath,
        string frequencyResponseMode,
        string liveSpectrumMode,
        string eqWizardMode)
    {
        string ninety = ninetyDegreePath == null
            ? string.Empty
            : $"""
                "MicrophoneCalibration90DegreesPath": {JsonSerializer.Serialize(ninetyDegreePath)},
            """;
        return WriteSettings($$"""
            {
              "SchemaVersion": 10,
              "Measurement": {
                {{ninety}}
                "MicrophoneCalibration0DegreesPath": "C:\\mics\\zero.txt"
              },
              "FrequencyResponse": { "UseCalibration": true, "CalibrationMode": "{{frequencyResponseMode}}" },
              "PhaseResponse": { "UseCalibration": true, "CalibrationMode": "{{frequencyResponseMode}}" },
              "GroupDelay": { "UseCalibration": true, "CalibrationMode": "{{frequencyResponseMode}}" },
              "LiveSpectrum": { "UseCalibration": true, "CalibrationMode": "{{liveSpectrumMode}}" },
              "EqWizard": { "CalibrationMode": "{{eqWizardMode}}" }
            }
            """);
    }

    private string WriteSettings(string json)
    {
        string path = Path.Combine(tempDirectory, $"settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
