using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed partial class MeasurementSettingsFile
{
    private const int CurrentSchemaVersion = 12;
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

    // How the DSP the user is tuning reads the Q of a peaking band. A property of the
    // hardware rather than of any one mode, so it lives at the top level and every
    // tuning sheet — EQ Wizard and Virtual DSP alike — prints its Q column for it.
    // Defaults to RBJ, which is what the fitting and the previews realize, so an
    // existing settings file keeps behaving exactly as before.
    public PeqQConvention TargetDspQConvention { get; set; } = PeqQConvention.Rbj;

    public string? LastImpulseResponseDirectory { get; set; }

    // True when loading reset a loopback configuration that pointed at the
    // removed separate-loopback-device capability; the shell shows a one-time
    // notice telling the user to pick a loopback channel again.
    [JsonIgnore]
    public bool LegacyDualDeviceLoopbackReset { get; private set; }

    [JsonIgnore]
    public string? LoadWarning { get; private set; }

    [JsonIgnore]
    private string pathOnDisk = ApplicationDataPaths.Current.SettingsFile;

    [JsonIgnore]
    // A load failure is safe to recover from only after the original file has
    // been moved aside. Keep automatic UI saves from overwriting it meanwhile.
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

            // Version 10 persists the EQ Wizard's filter bank; 7..9 files carry
            // only its filter count and rebuild a default spread from it.
            //
            // Version 9 added the SPL calibration; 7 and 8 files simply carry none.
            // A structurally broken anchor drops to null rather than failing the
            // whole settings load — the measurement configuration is the value here.
            try
            {
                settings.Measurement.SplCalibration?.Validate();
            }
            catch (InvalidDataException)
            {
                settings.Measurement.SplCalibration = null;
            }

            // Version 11 replaced the two fixed microphone-calibration slots
            // with a named list; the selections it rewrites are only readable
            // while the file still carries the legacy fields.
            if (settings.SchemaVersion < 11)
            {
                settings.MigrateLegacyMicrophoneCalibrations();
            }

            // Version 12 moved the measurement microphone's calibration out of the
            // Frequency Response view and into the rig.
            if (settings.SchemaVersion < 12)
            {
                settings.MigrateMicrophoneCalibrationHome();
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

    // A settings object with no file behind it is a FIRST RUN, and the measurement
    // views used to start corrected: the selection was a bare on/off flag that
    // defaulted to true, and then a mode that defaulted to 0°. Without this a fresh
    // installation would leave every view uncalibrated after the user configures
    // their 0° file, until they also picked it in each mode's selector.
    // A LOADED file is never touched here: an absent id there is a deliberate Off,
    // and that difference is the whole reason this lives in the load path rather
    // than in the property initializers. The EQ Wizard is excluded because it
    // always defaulted to no correction.
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

    // The separate-loopback-device capability was removed: microphone and
    // loopback are always channels of ONE input device now. A file written by
    // an older version with the loopback on a DIFFERENT device carries channel
    // offsets that are meaningless on the shared device (the channels may
    // legitimately be equal, and the microphone device may be mono), so the
    // loopback selection is reset to "unset" — the existing loopback-required
    // flow then walks the user through picking a channel — instead of being
    // silently misread as a shared-device configuration.
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

    // The 90° calibration used to be a second fixed slot, optionally backed by a
    // file and otherwise approximated from the 0° one. The slot is gone: a
    // CONFIGURED file becomes a named entry of the calibration list, keeping
    // every view that selected it working, while the approximation is not
    // recreated — an estimate now needs the microphone's geometry, which a
    // legacy file does not carry, so those views fall back to no correction
    // rather than to a curve nobody chose.
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

    /// <summary>
    /// Moves the measurement microphone's calibration from the Frequency Response
    /// view to the measurement's own settings, and takes the duplicates with it.
    /// </summary>
    /// <remarks>
    /// The FR selection is the honest source: it is the one a run used to freeze into
    /// its file, so carrying it over keeps every future sweep stamped exactly as the
    /// last one was. Phase and Group Delay had selections of their own, and a
    /// magnitude correction cannot differ by which tab is open — they follow the
    /// Frequency Response view now, so their stored ids go. Live Spectrum's went with
    /// them: a live capture is taken on the same rig, through the same capsule, as the
    /// sweeps beside it.
    /// </remarks>
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

    // Runs for every file, not just a migrated one: the list is hand-editable
    // JSON, and a definition with a duplicate id, no id, or an angle outside the
    // model's range would otherwise reach the estimator.
    private void NormalizeMicrophoneCalibrations()
    {
        List<MicrophoneCalibrationDefinition> definitions =
            Measurement.AdditionalMicrophoneCalibrations;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MicrophoneCalibrationIds.ZeroDegrees
        };
        // Forward, so a duplicated id keeps its FIRST entry: that is the one the
        // stored selections were written against, and the one the list showed.
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

        // An estimate may only be derived from a file-backed entry (or from the
        // 0° slot, which BaseId leaves null); anything else — a missing entry, or
        // a chain of estimates — falls back to the 0° calibration.
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
        // Temp file + move keeps the settings intact if the write is interrupted.
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

    public void ApplyTo(
        ExpSweepMeasurement measurement,
        FrequencyResponseOptions frequencyResponse,
        CurveVisibilityOptions frequencyResponseVisibility,
        FrequencyResponseOptions phaseResponse,
        CurveVisibilityOptions phaseResponseVisibility,
        FrequencyResponseOptions groupDelay,
        CurveVisibilityOptions groupDelayVisibility,
        ImpulseResponseOptions impulseResponse,
        WaterfallGenerateOptions waterfall,
        WaterfallGenerateOptions burstDecay,
        LiveSpectrumOptions liveSpectrum,
        TimeAlignmentOptions timeAlignment)
    {
        Measurement.ApplyTo(measurement);
        FrequencyResponse.ApplyTo(frequencyResponse, frequencyResponseVisibility);
        PhaseResponse.ApplyTo(phaseResponse, phaseResponseVisibility);
        GroupDelay.ApplyTo(groupDelay, groupDelayVisibility);
        ImpulseResponse.ApplyTo(impulseResponse);
        Waterfall.ApplyTo(waterfall, WaterfallMode.Fourier);
        BurstDecay.ApplyTo(burstDecay, WaterfallMode.BurstDecay);
        LiveSpectrum.ApplyTo(liveSpectrum);
        // A live capture is taken on the rig, so it is corrected by the rig's own
        // microphone calibration rather than by a selection of its own.
        liveSpectrum.CalibrationId = Measurement.MicrophoneCalibrationId;
        TimeAlignment.ApplyTo(timeAlignment, measurement.SampleRate);
    }

    public void CaptureFrom(
        ExpSweepMeasurement measurement,
        FrequencyResponseOptions frequencyResponse,
        CurveVisibilityOptions frequencyResponseVisibility,
        FrequencyResponseOptions phaseResponse,
        CurveVisibilityOptions phaseResponseVisibility,
        FrequencyResponseOptions groupDelay,
        CurveVisibilityOptions groupDelayVisibility,
        ImpulseResponseOptions impulseResponse,
        WaterfallGenerateOptions waterfall,
        WaterfallGenerateOptions burstDecay,
        LiveSpectrumOptions liveSpectrum,
        TimeAlignmentOptions timeAlignment)
    {
        SchemaVersion = CurrentSchemaVersion;
        SweepMeasurementSettings previousMeasurement = Measurement;
        Measurement = SweepMeasurementSettings.Capture(measurement);
        Measurement.CopyCalibrationFrom(previousMeasurement);
        FrequencyResponse = FrequencyResponseSettings.Capture(frequencyResponse, frequencyResponseVisibility);
        PhaseResponse = FrequencyResponseSettings.Capture(phaseResponse, phaseResponseVisibility);
        GroupDelay = FrequencyResponseSettings.Capture(groupDelay, groupDelayVisibility);
        // Not stored for these two: phase and group delay read timing rather than
        // level and apply no correction at all, so an id there was state nothing
        // could act on — and it drifted, sitting on 0° while the Frequency Response
        // view moved to another curve and stamped the files with it.
        PhaseResponse.CalibrationId = null;
        GroupDelay.CalibrationId = null;
        ImpulseResponse = ImpulseResponseSettings.Capture(impulseResponse);
        Waterfall = WaterfallSettings.Capture(waterfall);
        BurstDecay = WaterfallSettings.Capture(burstDecay);
        LiveSpectrum = LiveSpectrumSettings.Capture(liveSpectrum);
        // Same: the rig's calibration is the live capture's, and it is stored once,
        // in the measurement's own settings.
        LiveSpectrum.CalibrationId = null;
        TimeAlignment = TimeAlignmentSettings.Capture(timeAlignment);
    }}
