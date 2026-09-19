using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed partial class MeasurementSettingsFile
{
    private const int CurrentSchemaVersion = 13;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public SweepMeasurementSettings Measurement { get; set; } = new();
    public FrequencyResponseSettings FrequencyResponse { get; set; } = new();
    public FrequencyResponseSettings PhaseResponse { get; set; } = new();
    public FrequencyResponseSettings GroupDelay { get; set; } = new();
    public ImpulseResponseSettings ImpulseResponse { get; set; } = new();
    public WaterfallSettings Waterfall { get; set; } = new();
    public WaterfallSettings BurstDecay { get; set; } = new();
    public LiveSpectrumSettings LiveSpectrum { get; set; } = new();
    public TimeAlignmentSettings TimeAlignment { get; set; } = new();
    public EqWizardSettings EqWizard { get; set; } = new();

    // A hardware property, so top-level; every tuning sheet prints Q in it. RBJ is what fitting and previews realize.
    public PeqQConvention TargetDspQConvention { get; set; } = PeqQConvention.Rbj;

    public string? LastImpulseResponseDirectory { get; set; }

    // Null = REW's default address (a client constant), not "unconfigured".
    public string? RewApiBaseUrl { get; set; }

    [JsonIgnore]
    public bool LegacyDualDeviceLoopbackReset { get; private set; }

    [JsonIgnore]
    public string? LoadWarning { get; private set; }

    [JsonIgnore]
    private string pathOnDisk = ApplicationDataPaths.Current.SettingsFile;

    [JsonIgnore]
    // Recover only after the original file is moved aside; block automatic UI saves meanwhile.
    private bool preserveExistingFileBeforeSave;

    public static MeasurementSettingsFile LoadOrDefault(string? pathOnDisk = null)
    {
        string path = pathOnDisk ?? ApplicationDataPaths.Current.SettingsFile;
        try
        {
            if (!File.Exists(path))
            {
                return new MeasurementSettingsFile { pathOnDisk = path }
                    .WithFirstRunCalibrationDefaults();
            }

            using FileStream stream = File.OpenRead(path);
            MeasurementSettingsFile? settings =
                JsonSerializer.Deserialize<MeasurementSettingsFile>(
                    stream,
                    SerializerOptions);
            if (settings == null || settings.SchemaVersion is < 7 or > CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    settings == null
                        ? "The settings file is empty."
                        : $"Settings schema version {settings.SchemaVersion} is not supported.");
            }

            if (settings.SchemaVersion == 7)
            {
                settings.PhaseResponse.PhaseWindowMode =
                    Resonalyze.Dsp.PhaseWindowMode.Fixed;
                settings.PhaseResponse.PhaseDetrendMode =
                    Resonalyze.Dsp.PhaseDetrendMode.Manual;
                settings.PhaseResponse.PhaseFdwCycles =
                    PhaseAnalysisSettings.DefaultFdwCycles;
            }

            // v10 persists the EQ Wizard bank (7..9 carry only a count). v9 added the SPL anchor; a broken anchor drops to null.
            try
            {
                settings.Measurement.SplCalibration?.Validate();
            }
            catch (InvalidDataException)
            {
                settings.Measurement.SplCalibration = null;
            }

            // v11: named calibration list; legacy fields are only readable before this migration.
            if (settings.SchemaVersion < 11)
            {
                settings.MigrateLegacyMicrophoneCalibrations();
            }

            if (settings.SchemaVersion < 12)
            {
                settings.MigrateMicrophoneCalibrationHome();
            }

            // v13: Group Delay window selector. Older files keep the Fixed gate; fresh installs start on FDW.
            if (settings.SchemaVersion < 13)
            {
                settings.GroupDelay.GroupDelayWindowMode =
                    Resonalyze.Dsp.PhaseWindowMode.Fixed;
            }

            settings.SchemaVersion = CurrentSchemaVersion;
            settings.MigrateLegacyDualDeviceLoopback();
            settings.NormalizeMicrophoneCalibrations();
            settings.pathOnDisk = path;
            return settings;
        }
        catch (Exception exception)
        {
            BackupResult backup = BackupUnusableFile(path);
            string? backupPath = backup.Path;
            string preservation = backupPath == null
                ? "The unusable file could not be backed up. Changes will not be saved " +
                    "until the original file can be preserved; check file permissions."
                : $"The unusable file was preserved as '{backupPath}'.";
            return new MeasurementSettingsFile
            {
                pathOnDisk = path,
                LoadWarning = $"Settings could not be loaded: {exception.Message}\r\n\r\n{preservation}",
                preserveExistingFileBeforeSave = backup.Status == BackupStatus.Failed
            }.WithFirstRunCalibrationDefaults();
        }
    }

    // No file = first run: default views to the 0° calibration. A loaded file's absent id is a deliberate Off,
    // which is why this lives in the load path. EQ Wizard always defaulted to no correction.
    private MeasurementSettingsFile WithFirstRunCalibrationDefaults()
    {
        Measurement.MicrophoneCalibrationId = MicrophoneCalibrationIds.ZeroDegrees;
        FrequencyResponse.CalibrationId = MicrophoneCalibrationIds.ZeroDegrees;
        return this;
    }

    private static BackupResult BackupUnusableFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new BackupResult(BackupStatus.NotFound, null);
            }

            string backupPath = GetAvailableBackupPath(path);
            File.Move(path, backupPath);
            return new BackupResult(BackupStatus.Preserved, backupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new BackupResult(BackupStatus.Failed, null);
        }
    }

    private static string GetAvailableBackupPath(string path)
    {
        string backupPath = path + ".backup";
        for (int suffix = 1; File.Exists(backupPath); suffix++)
        {
            backupPath = path + $".backup.{suffix}";
        }

        return backupPath;
    }

    // Mic and loopback are now channels of one device; offsets from a separate loopback device are meaningless,
    // so reset the loopback to unset and let the loopback-required flow ask again.
    internal void MigrateLegacyDualDeviceLoopback()
    {
        if (Measurement.WaveLoopbackDeviceNumber is int legacyDevice &&
            legacyDevice != Measurement.InputDeviceNumber)
        {
            Measurement.WaveLoopbackInputChannelOffset = null;
            LegacyDualDeviceLoopbackReset = true;
        }

        Measurement.WaveLoopbackDeviceNumber = null;
    }

    // A configured 90° file becomes a named entry; the 0°-derived approximation is not recreated (needs geometry),
    // so those views fall back to no correction.
    internal void MigrateLegacyMicrophoneCalibrations()
    {
        string? legacyPath = Measurement.MicrophoneCalibration90DegreesPath;
        Measurement.MicrophoneCalibration90DegreesPath = null;
        bool migrated = !string.IsNullOrWhiteSpace(legacyPath);
        if (migrated &&
            !Measurement.AdditionalMicrophoneCalibrations.Any(definition =>
                string.Equals(
                    definition.Id,
                    MicrophoneCalibrationDefinition.LegacyNinetyDegreesId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            Measurement.AdditionalMicrophoneCalibrations.Add(
                new MicrophoneCalibrationDefinition
                {
                    Id = MicrophoneCalibrationDefinition.LegacyNinetyDegreesId,
                    Name = "90°",
                    Kind = MicrophoneCalibrationKind.File,
                    Path = legacyPath
                });
        }

        FrequencyResponse.CalibrationId = MigrateSelection(
            FrequencyResponse.CalibrationId,
            FrequencyResponse.CalibrationMode,
            FrequencyResponse.UseCalibration,
            migrated);
        PhaseResponse.CalibrationId = MigrateSelection(
            PhaseResponse.CalibrationId,
            PhaseResponse.CalibrationMode,
            PhaseResponse.UseCalibration,
            migrated);
        GroupDelay.CalibrationId = MigrateSelection(
            GroupDelay.CalibrationId,
            GroupDelay.CalibrationMode,
            GroupDelay.UseCalibration,
            migrated);
        LiveSpectrum.CalibrationId = MigrateSelection(
            LiveSpectrum.CalibrationId,
            LiveSpectrum.CalibrationMode,
            LiveSpectrum.UseCalibration,
            migrated);
        EqWizard.CalibrationId = MigrateSelection(
            EqWizard.CalibrationId,
            EqWizard.CalibrationMode,
            legacyUseCalibration: false,
            migrated);
        FrequencyResponse.CalibrationMode = null;
        PhaseResponse.CalibrationMode = null;
        GroupDelay.CalibrationMode = null;
        LiveSpectrum.CalibrationMode = null;
        EqWizard.CalibrationMode = null;
        FrequencyResponse.UseCalibration = null;
        PhaseResponse.UseCalibration = null;
        GroupDelay.UseCalibration = null;
        LiveSpectrum.UseCalibration = null;
    }

    /// <summary>Moves the mic calibration from the FR view into the measurement settings; other views' ids are dropped.</summary>
    private void MigrateMicrophoneCalibrationHome()
    {
        Measurement.MicrophoneCalibrationId = FrequencyResponse.CalibrationId;
        PhaseResponse.CalibrationId = null;
        GroupDelay.CalibrationId = null;
        LiveSpectrum.CalibrationId = null;
    }

    private static string? MigrateSelection(
        string? calibrationId,
        LegacyMicrophoneCalibrationMode? legacyMode,
        bool? legacyUseCalibration,
        bool ninetyDegreeFileMigrated)
    {
        string? resolved = ResolveCalibrationId(
            calibrationId,
            legacyMode,
            legacyUseCalibration);
        return !ninetyDegreeFileMigrated &&
            resolved == MicrophoneCalibrationDefinition.LegacyNinetyDegreesId
                ? null
                : resolved;
    }

    // Every file: the list is hand-editable, and bad ids or angles would reach the estimator.
    private void NormalizeMicrophoneCalibrations()
    {
        List<MicrophoneCalibrationDefinition> definitions =
            Measurement.AdditionalMicrophoneCalibrations;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MicrophoneCalibrationIds.ZeroDegrees
        };
        // Forward: a duplicate id keeps its first entry, the one stored selections refer to.
        for (int index = 0; index < definitions.Count; index++)
        {
            MicrophoneCalibrationDefinition definition = definitions[index];
            definition.Normalize();
            if (definition.Id.Length == 0 || !seen.Add(definition.Id))
            {
                definitions.RemoveAt(index);
                index--;
            }
        }

        // Estimates derive only from file-backed entries or the 0° slot; anything else falls back to 0°.
        var fileBacked = new HashSet<string>(
            definitions
                .Where(definition => definition.Kind == MicrophoneCalibrationKind.File)
                .Select(definition => definition.Id),
            StringComparer.OrdinalIgnoreCase);
        foreach (MicrophoneCalibrationDefinition definition in definitions)
        {
            if (definition.BaseId is { } baseId && !fileBacked.Contains(baseId))
            {
                definition.BaseId = null;
            }
        }
    }

    public void Save()
    {
        if (preserveExistingFileBeforeSave)
        {
            BackupResult backup = BackupUnusableFile(pathOnDisk);
            if (backup.Status == BackupStatus.Failed)
            {
                return;
            }

            preserveExistingFileBeforeSave = false;
        }

        SchemaVersion = CurrentSchemaVersion;
        string directory = Path.GetDirectoryName(pathOnDisk)
            ?? throw new InvalidOperationException("Settings directory cannot be resolved.");
        Directory.CreateDirectory(directory);
        string tempPath = pathOnDisk + ".tmp";
        using (FileStream stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, this, SerializerOptions);
        }

        File.Move(tempPath, pathOnDisk, overwrite: true);
    }

    private enum BackupStatus
    {
        NotFound,
        Preserved,
        Failed
    }

    private readonly record struct BackupResult(BackupStatus Status, string? Path);

    public void ApplyTo(ExpSweepMeasurement measurement, AnalyzerViewSettings view)
    {
        Measurement.ApplyTo(measurement);
        FrequencyResponse.ApplyTo(view.FrequencyResponse, view.FrequencyResponseVisibility);
        PhaseResponse.ApplyTo(view.PhaseResponse, view.PhaseResponseVisibility);
        GroupDelay.ApplyTo(view.GroupDelay, view.GroupDelayVisibility);
        ImpulseResponse.ApplyTo(view.ImpulseResponse);
        Waterfall.ApplyTo(view.Waterfall, WaterfallMode.Fourier);
        BurstDecay.ApplyTo(view.BurstDecay, WaterfallMode.BurstDecay);
        LiveSpectrum.ApplyTo(view.LiveSpectrum);
        // A live capture is corrected by the rig's microphone calibration.
        view.LiveSpectrum.CalibrationId = Measurement.MicrophoneCalibrationId;
        TimeAlignment.ApplyTo(view.TimeAlignment, measurement.SampleRate);
    }

    public void CaptureFrom(ExpSweepMeasurement measurement, AnalyzerViewSettings view)
    {
        SchemaVersion = CurrentSchemaVersion;
        SweepMeasurementSettings previousMeasurement = Measurement;
        Measurement = SweepMeasurementSettings.Capture(measurement);
        Measurement.CopyCalibrationFrom(previousMeasurement);
        FrequencyResponse = FrequencyResponseSettings.Capture(view.FrequencyResponse, view.FrequencyResponseVisibility);
        PhaseResponse = FrequencyResponseSettings.Capture(view.PhaseResponse, view.PhaseResponseVisibility);
        GroupDelay = FrequencyResponseSettings.Capture(view.GroupDelay, view.GroupDelayVisibility);
        // Phase and group delay apply no correction; a stored id only drifted.
        PhaseResponse.CalibrationId = null;
        GroupDelay.CalibrationId = null;
        ImpulseResponse = ImpulseResponseSettings.Capture(view.ImpulseResponse);
        Waterfall = WaterfallSettings.Capture(view.Waterfall);
        BurstDecay = WaterfallSettings.Capture(view.BurstDecay);
        LiveSpectrum = LiveSpectrumSettings.Capture(view.LiveSpectrum);
        LiveSpectrum.CalibrationId = null;
        TimeAlignment = TimeAlignmentSettings.Capture(view.TimeAlignment);
    }}
