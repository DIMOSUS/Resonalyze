using System.Text.Json;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>A configured 90° file migrates to a list entry; the file-less 90°-from-0° approximation is not recreated.</summary>
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
        Assert.Equal(entry.Id, settings.EqWizard.CalibrationId);
        // Schema 12: the rig's selection is taken from the view that stamped the files, so the next sweep is labelled the same.
        Assert.Equal(entry.Id, settings.Measurement.MicrophoneCalibrationId);
        Assert.Null(settings.LiveSpectrum.CalibrationId);
        Assert.Null(settings.PhaseResponse.CalibrationId);
        Assert.Null(settings.GroupDelay.CalibrationId);
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
              "Measurement": { "MicrophoneCalibration0DegreesPath": "C:\\mics\\zero.txt" },
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
        // On a current file an absent id is a deliberate Off, not a pre-list file.
        string path = WriteSettings("""
            {
              "SchemaVersion": 11,
              "FrequencyResponse": { }
            }
            """);

        Assert.Null(Load(path).FrequencyResponse.CalibrationId);
    }

    [Fact]
    public void AFirstRunStartsUncalibrated()
    {
        MeasurementSettingsFile settings = MeasurementSettingsFile.LoadOrDefault(
            Path.Combine(tempDirectory, "absent.json"));

        Assert.Null(settings.Measurement.MicrophoneCalibrationId);
        Assert.Null(settings.FrequencyResponse.CalibrationId);
        Assert.Null(settings.LiveSpectrum.CalibrationId);
        Assert.Null(settings.EqWizard.CalibrationId);
    }

    [Fact]
    public void AZeroDegreeSelectionWithNoFileSet_ReadsAsOff()
    {
        string path = WriteSettings("""
            {
              "SchemaVersion": 13,
              "Measurement": { "MicrophoneCalibrationId": "0deg" },
              "FrequencyResponse": { "CalibrationId": "0deg" },
              "EqWizard": { "CalibrationId": "0deg" }
            }
            """);

        MeasurementSettingsFile settings = Load(path);

        Assert.Null(settings.Measurement.MicrophoneCalibrationId);
        Assert.Null(settings.FrequencyResponse.CalibrationId);
        Assert.Null(settings.EqWizard.CalibrationId);
    }

    // A set file that went missing stays selected, so the choice survives an unplugged drive.
    [Fact]
    public void AZeroDegreeSelectionWithAFileSet_StaysSelected()
    {
        string path = WriteSettings("""
            {
              "SchemaVersion": 13,
              "Measurement": {
                "MicrophoneCalibrationId": "0deg",
                "MicrophoneCalibration0DegreesPath": "Z:\\missing\\mic.txt"
              },
              "FrequencyResponse": { "CalibrationId": "0deg" }
            }
            """);

        MeasurementSettingsFile settings = Load(path);

        Assert.Equal(MicrophoneCalibrationIds.ZeroDegrees, settings.Measurement.MicrophoneCalibrationId);
        Assert.Equal(MicrophoneCalibrationIds.ZeroDegrees, settings.FrequencyResponse.CalibrationId);
    }

    /// <summary>"Own" names a rule rather than a curve, so it persists across restarts as an ordinary id.</summary>
    [Fact]
    public void TheOwnSelectionSurvivesASaveAndLoad()
    {
        string path = WriteSettings("""
            {
              "SchemaVersion": 12,
              "Measurement": { "MicrophoneCalibrationId": "0deg" },
              "FrequencyResponse": { "CalibrationId": "own" }
            }
            """);

        MeasurementSettingsFile settings = Load(path);

        Assert.Equal(MicrophoneCalibrationIds.Own, settings.FrequencyResponse.CalibrationId);
        Assert.True(MicrophoneCalibrationIds.IsOwn(settings.FrequencyResponse.CalibrationId));

        var options = new FrequencyResponseOptions();
        settings.FrequencyResponse.ApplyTo(options, new CurveVisibilityOptions());
        Assert.Equal(MicrophoneCalibrationIds.Own, options.CalibrationId);
    }

    /// <summary>Phase and group delay read timing and apply no correction, so their stored ids are dropped.</summary>
    [Fact]
    public void TheLiveAnalyzerFollowsTheRigAndTheDeadSelectionsGo()
    {
        MeasurementSettingsFile settings = Load(WriteSettings("""
            {
              "SchemaVersion": 11,
              "Measurement": { },
              "FrequencyResponse": { "CalibrationId": "cal-90" },
              "PhaseResponse": { "CalibrationId": "0deg" },
              "GroupDelay": { "CalibrationId": "0deg" },
              "LiveSpectrum": { "CalibrationId": "0deg" }
            }
            """));

        var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
        FrequencyResponseOptions frequencyResponse = new();
        FrequencyResponseOptions phase = new();
        FrequencyResponseOptions groupDelay = new();
        LiveSpectrumOptions live = new();
        settings.ApplyTo(
            measurement,
            new AnalyzerViewSettings
            {
                FrequencyResponse = frequencyResponse,
                PhaseResponse = phase,
                GroupDelay = groupDelay,
                LiveSpectrum = live
            });

        Assert.Equal("cal-90", frequencyResponse.CalibrationId);
        Assert.Equal("cal-90", settings.Measurement.MicrophoneCalibrationId);
        Assert.Equal("cal-90", live.CalibrationId);
        Assert.Null(settings.PhaseResponse.CalibrationId);
        Assert.Null(settings.GroupDelay.CalibrationId);
        Assert.Null(phase.CalibrationId);
        Assert.Null(groupDelay.CalibrationId);
    }

    [Fact]
    public void CapturingTheMeasurementSettingsKeepsTheConfiguredCalibrations()
    {
        // Capture rebuilds the measurement section from the measurement, which knows nothing about calibration files.
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
        settings.CaptureFrom(measurement, new AnalyzerViewSettings());

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
        Assert.Equal(90.0, definitions[1].AngleDegrees);
        Assert.Equal(
            MicrophoneCalibrationDefinition.DefaultFrontDiameterMm,
            definitions[1].FrontDiameterMm);
        Assert.Equal("90°", definitions[1].Name);
        // Estimates derive only from file-backed entries, so a chain of estimates falls back to 0°.
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
