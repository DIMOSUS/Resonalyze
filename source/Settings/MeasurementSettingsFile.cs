using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal sealed partial class MeasurementSettingsFile
{
    private const int CurrentSchemaVersion = 9;
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
                return new MeasurementSettingsFile { pathOnDisk = path };
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

            settings.SchemaVersion = CurrentSchemaVersion;
            settings.MigrateLegacyDualDeviceLoopback();
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
            };
        }
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
        Measurement = SweepMeasurementSettings.Capture(measurement);
        FrequencyResponse = FrequencyResponseSettings.Capture(frequencyResponse, frequencyResponseVisibility);
        PhaseResponse = FrequencyResponseSettings.Capture(phaseResponse, phaseResponseVisibility);
        GroupDelay = FrequencyResponseSettings.Capture(groupDelay, groupDelayVisibility);
        ImpulseResponse = ImpulseResponseSettings.Capture(impulseResponse);
        Waterfall = WaterfallSettings.Capture(waterfall);
        BurstDecay = WaterfallSettings.Capture(burstDecay);
        LiveSpectrum = LiveSpectrumSettings.Capture(liveSpectrum);
        TimeAlignment = TimeAlignmentSettings.Capture(timeAlignment);
    }}
