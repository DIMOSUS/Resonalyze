using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Which curve the Virtual DSP chain plot shows for each channel.</summary>
public enum DspPlotMode
{
    Magnitude,
    Phase,
    GroupDelay
}

/// <summary>
/// One channel of the virtual crossover: which measurement feeds it and the DSP
/// chain applied before the virtual sum. The source is re-resolved on load — by
/// history entry first, then by file path — so a renamed history label or a moved
/// file degrades gracefully instead of failing the whole project.
/// </summary>
public sealed class VirtualCrossoverChannelSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When set (and the channel is enabled), the channel contributes its raw
    /// measured signal with the whole DSP chain bypassed — no gain, delay,
    /// polarity, crossover or PEQ — for an A/B against the processed result.
    /// </summary>
    public bool Bypass { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public string? SourceFilePath { get; set; }
    public Guid? HistoryEntryId { get; set; }

    public double GainDb { get; set; }
    public double DelayMs { get; set; }
    public bool InvertPolarity { get; set; }

    public CrossoverKind CrossoverKind { get; set; } = CrossoverKind.Off;
    public CrossoverEdge LowPassEdge { get; set; } =
        new(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);
    public CrossoverEdge HighPassEdge { get; set; } =
        new(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);

    public double PeqPreampDb { get; set; }
    public List<PeqBand> PeqBands { get; set; } = new();
    /// <summary>The PEQ file the bands came from; display only.</summary>
    public string? PeqSourceName { get; set; }

    // Per-channel curve visibility on the acoustic plot.
    public bool ShowRawCurve { get; set; }
    public bool ShowProcessedCurve { get; set; } = true;

    public bool HasSource =>
        HistoryEntryId.HasValue || !string.IsNullOrWhiteSpace(SourceFilePath);

    /// <summary>The DSP chain these settings describe.</summary>
    public DspChannelChain ToChain()
    {
        CrossoverSpec crossover = CrossoverKind switch
        {
            CrossoverKind.LowPass => new CrossoverSpec(CrossoverKind, LowPassEdge),
            CrossoverKind.HighPass => new CrossoverSpec(CrossoverKind, HighPassEdge: HighPassEdge),
            CrossoverKind.BandPass => new CrossoverSpec(CrossoverKind, LowPassEdge, HighPassEdge),
            _ => CrossoverSpec.Off
        };
        EqualizationCurve? peq = PeqBands.Count > 0 || PeqPreampDb != 0
            ? new EqualizationCurve(PeqBands, PeqPreampDb)
            : null;
        return new DspChannelChain(GainDb, DelayMs, InvertPolarity, crossover, peq);
    }

    public void Validate()
    {
        if (!double.IsFinite(GainDb) || Math.Abs(GainDb) > 60)
        {
            throw new InvalidDataException("The channel gain is invalid.");
        }
        if (!double.IsFinite(DelayMs) || DelayMs is < 0 or > 1_000)
        {
            throw new InvalidDataException("The channel delay is invalid.");
        }
        if (!Enum.IsDefined(CrossoverKind))
        {
            throw new InvalidDataException("The crossover kind is invalid.");
        }
        ValidateEdge(LowPassEdge);
        ValidateEdge(HighPassEdge);
        if (!double.IsFinite(PeqPreampDb) || Math.Abs(PeqPreampDb) > 60)
        {
            throw new InvalidDataException("The PEQ preamp is invalid.");
        }
        if (PeqBands.Count > EqualizationCurve.MaxBandCount)
        {
            throw new InvalidDataException("The PEQ band count is invalid.");
        }
        foreach (PeqBand band in PeqBands)
        {
            if (!double.IsFinite(band.FrequencyHz) || band.FrequencyHz <= 0 ||
                !double.IsFinite(band.Q) || band.Q <= 0 ||
                !double.IsFinite(band.GainDb))
            {
                throw new InvalidDataException("A PEQ band is invalid.");
            }
        }
    }

    // Both edges are validated even when the kind ignores them: they are still
    // shown (greyed out) in the UI and must round-trip as sane values.
    private static void ValidateEdge(CrossoverEdge edge)
    {
        if (!Enum.IsDefined(edge.Family))
        {
            throw new InvalidDataException("The crossover family is invalid.");
        }
        if (!double.IsFinite(edge.FrequencyHz) || edge.FrequencyHz is < 10 or > 24_000)
        {
            throw new InvalidDataException("The crossover corner frequency is invalid.");
        }
        if (!CrossoverFilter.SupportedSlopes(edge.Family).Contains(edge.SlopeDbPerOctave))
        {
            throw new InvalidDataException("The crossover slope is invalid.");
        }
    }
}

/// <summary>
/// One speaker of the car as the Virtual DSP tool models it since schema v2: a
/// left/right PAIR of measurement + DSP chain sets under one channel letter.
/// A mono pair (the shared subwoofer) has one physical driver serving both
/// sides: only <see cref="Left"/> is meaningful and it participates in both
/// side views and both sides' calculations.
/// </summary>
public sealed class VirtualCrossoverChannelPairSettings
{
    public bool Mono { get; set; }
    public VirtualCrossoverChannelSettings Left { get; set; } = new();
    public VirtualCrossoverChannelSettings Right { get; set; } = new();

    /// <summary>
    /// The settings the given side view edits and computes with: a mono pair
    /// always answers with its single (left) set.
    /// </summary>
    public VirtualCrossoverChannelSettings SideFor(bool rightSide) =>
        Mono || !rightSide ? Left : Right;

    public void Validate()
    {
        Left.Validate();
        Right.Validate();
    }
}

/// <summary>
/// Persists the Virtual DSP tool state (channel pairs, their DSP chains and
/// the plot view flags) so a tuning session survives an application restart.
/// The pair count is user-resizable in the tool, from two up to
/// <see cref="MaximumChannelCount"/>.
/// </summary>
public sealed class VirtualCrossoverProjectFile
{
    public const string CurrentFormat = "resonalyze-virtual-crossover";

    // Bump on an incompatible schema change and add a per-version migration
    // step in Migrate below. Files from a NEWER version (a downgraded app)
    // are never migrated: LoadOrDefault backs them up and starts fresh,
    // LoadFrom rejects them with an explicit error.
    public const int CurrentVersion = 4;
    public const int MaximumChannelCount = 8;
    private const string FileName = "virtual-crossover.json";

    /// <summary>
    /// The widest scene offset (ms) the stereo Auto delay accepts: beyond a
    /// couple of milliseconds an inter-side lead is no longer an image shift
    /// but an audible echo, so a larger magnitude is a typo.
    /// </summary>
    public const double MaximumSceneOffsetMs = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Format { get; set; } = CurrentFormat;
    public int Version { get; set; } = CurrentVersion;
    public DateTimeOffset SavedAtUtc { get; set; }

    // Schema v1 payload, kept only so old files deserialize for migration:
    // Migrate moves these into Pairs (as the left side) and empties the list.
    // v2 files serialize it as an empty array.
    public List<VirtualCrossoverChannelSettings> Channels { get; set; } = new();

    // One entry per channel block in the tool (A, B, C), each an L/R pair; a
    // side without a source simply does not participate in that side's sum.
    public List<VirtualCrossoverChannelPairSettings> Pairs { get; set; } =
    [
        new VirtualCrossoverChannelPairSettings(),
        new VirtualCrossoverChannelPairSettings(),
        new VirtualCrossoverChannelPairSettings()
    ];

    // The stereo Auto delay scene offset (ms): positive makes the right side
    // LEAD (arrive earlier), pulling the image toward the dash center for a
    // left-seated driver; right-seated drivers use a negative value.
    public double StereoSceneOffsetMs { get; set; } = 0.25;

    // Which side the tool currently displays and edits (view state).
    public bool ActiveSideRight { get; set; }

    // Acoustic-plot view state shared by all channels.
    public bool ShowSumCurve { get; set; } = true;
    public bool ShowLossCurve { get; set; }
    public bool ShowPhaseView { get; set; }
    // The main plot's impulse view (the gated IR preview promoted to the main
    // plot). Additive: older files lack it, and when set it wins over
    // ShowPhaseView. Kept as a second flag so files written by this version
    // still open in older builds (which fall back to magnitude/phase).
    public bool ShowImpulseView { get; set; }
    public int SmoothingInverseOctaves { get; set; } = 12;

    // Which curve the per-channel DSP chain plot shows. Additive: older files lack
    // it and default to Magnitude.
    public DspPlotMode DspPlotMode { get; set; } = DspPlotMode.Magnitude;

    // Microphone calibration applied to the magnitude curves. The measurement is
    // loopback-referenced, so calibration is optional and off by default.
    // Additive: older files lack the field and default to Off.
    public MicrophoneCalibrationMode CalibrationMode { get; set; } =
        MicrophoneCalibrationMode.Off;

    // Phase-view gate, mirroring the Phase mode: a Tukey window of left + plateau
    // + right milliseconds whose left shoulder ends at the gate offset, so room
    // reflections can be gated out of the phase traces. A null offset follows the
    // earliest processed arrival automatically (the pre-configuration default).
    public double? PhaseGateOffsetMs { get; set; }
    public double PhaseGateLeftMs { get; set; } =
        FrequencyResponseOptions.DefaultPhaseLeftMs;
    public double PhaseGatePlateauMs { get; set; } =
        FrequencyResponseOptions.DefaultPhasePlateauMs;
    public double PhaseGateRightMs { get; set; } =
        FrequencyResponseOptions.DefaultPhaseRightMs;

    // Phase-view τ detrend (ms, absolute from the IR start): one linear-phase
    // reference removed from every channel and the sum alike, so the traces stay
    // readable while their relative phase is preserved. Null follows the
    // earliest processed arrival automatically.
    public double? PhaseDetrendMs { get; set; }
    public PhaseWindowMode PhaseWindowMode { get; set; } =
        PhaseWindowMode.FrequencyDependent;
    public int PhaseFdwCycles { get; set; } = PhaseAnalysisSettings.DefaultFdwCycles;
    public PhaseDetrendMode PhaseDetrendMode { get; set; } = PhaseDetrendMode.Auto;

    public static string GetPath(string? rootDirectory = null) =>
        Path.Combine(
            rootDirectory ?? ApplicationDataPaths.Current.ToolsDirectory,
            FileName);

    public void Save(string? rootDirectory = null)
    {
        Validate();
        SavedAtUtc = DateTimeOffset.UtcNow;

        string path = GetPath(rootDirectory);
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The virtual crossover directory cannot be resolved.");
        Directory.CreateDirectory(directory);

        string temporaryPath = path + ".tmp";
        try
        {
            using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                JsonSerializer.Serialize(stream, this, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// Exports the session to a user-chosen file (same format as the internal
    /// project file), so a tuning setup can be shared or archived.
    /// </summary>
    public void SaveTo(string path)
    {
        Validate();
        SavedAtUtc = DateTimeOffset.UtcNow;
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, this, SerializerOptions);
    }

    /// <summary>
    /// Imports a session from a user-chosen file. Unlike <see cref="LoadOrDefault"/>
    /// this throws on a broken or incompatible file — an explicit import deserves
    /// an explicit error instead of silently starting fresh.
    /// </summary>
    public static VirtualCrossoverProjectFile LoadFrom(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        VirtualCrossoverProjectFile file =
            JsonSerializer.Deserialize<VirtualCrossoverProjectFile>(
                stream,
                SerializerOptions)
            ?? throw new InvalidDataException("The session file is empty.");
        Migrate(file);
        file.Validate();
        return file;
    }

    // Per-version upgrade steps, applied before validation. Newer-than-current
    // versions are deliberately NOT touched: validation rejects them and the
    // callers handle that (backup + fresh, or an explicit import error).
    private static void Migrate(VirtualCrossoverProjectFile file)
    {
        if (file.Version == 1)
        {
            // v1 stored single-sided channels; they become the LEFT side of a
            // pair (the historical measurements were the user's only side) and
            // the right side starts empty.
            file.Pairs = file.Channels
                .Select(channel => new VirtualCrossoverChannelPairSettings
                {
                    Left = channel,
                    Right = new VirtualCrossoverChannelSettings()
                })
                .ToList();
            file.Channels = new List<VirtualCrossoverChannelSettings>();
            file.Version = 2;
        }
        if (file.Version == 2)
        {
            // v2 only had a fixed gate and a numeric common detrend.
            file.PhaseWindowMode = PhaseWindowMode.Fixed;
            file.PhaseFdwCycles = PhaseAnalysisSettings.DefaultFdwCycles;
            file.PhaseDetrendMode = PhaseDetrendMode.Manual;
            file.Version = 3;
        }
        if (file.Version == 3)
        {
            file.Version = 4;
        }
    }

    /// <summary>
    /// When <see cref="LoadOrDefault"/> could not use an existing file and moved
    /// it aside, this holds the path of the <c>.backup</c> it created so the tool
    /// can tell the user their previous session was preserved. Null on a clean
    /// load (or when the aside-move itself failed). Not serialized.
    /// </summary>
    [JsonIgnore]
    public string? BackupNoticePath { get; private set; }

    /// <summary>
    /// Loads the saved project, falling back to a fresh default when the file
    /// is missing, unreadable or from an unknown version — the tool state is a
    /// convenience, so it must never block startup. A file that exists but
    /// cannot be used is renamed to <c>.backup</c> first: the next scheduled
    /// save overwrites the project path, and a downgrade or a bug must not
    /// cost the user their tuning session.
    /// </summary>
    public static VirtualCrossoverProjectFile LoadOrDefault(string? rootDirectory = null)
    {
        string path = GetPath(rootDirectory);
        try
        {
            if (!File.Exists(path))
            {
                return new VirtualCrossoverProjectFile();
            }

            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            VirtualCrossoverProjectFile file =
                JsonSerializer.Deserialize<VirtualCrossoverProjectFile>(
                    stream,
                    SerializerOptions)
                ?? throw new InvalidDataException("The project file is empty.");
            Migrate(file);
            file.Validate();
            return file;
        }
        catch
        {
            return new VirtualCrossoverProjectFile
            {
                BackupNoticePath = BackupUnusableFile(path)
            };
        }
    }

    private static string? BackupUnusableFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string backupPath = path + ".backup";
                File.Move(path, backupPath, overwrite: true);
                return backupPath;
            }
        }
        catch
        {
            // Best effort (the file may be locked); startup must not block.
        }

        return null;
    }

    public void Validate()
    {
        if (!string.Equals(Format, CurrentFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported virtual crossover format '{Format}'.");
        }
        if (Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported virtual crossover version {Version}.");
        }
        if (Pairs.Count is < 2 or > MaximumChannelCount)
        {
            throw new InvalidDataException(
                "The virtual crossover channel count is invalid.");
        }
        if (!double.IsFinite(StereoSceneOffsetMs) ||
            Math.Abs(StereoSceneOffsetMs) > MaximumSceneOffsetMs)
        {
            throw new InvalidDataException("The stereo scene offset is invalid.");
        }
        if (!OverlaySmoothing.IsValid(SmoothingInverseOctaves))
        {
            throw new InvalidDataException(
                "The virtual crossover smoothing setting is invalid.");
        }
        if (!Enum.IsDefined(CalibrationMode))
        {
            throw new InvalidDataException(
                "The virtual crossover calibration mode is invalid.");
        }
        if (!Enum.IsDefined(DspPlotMode))
        {
            throw new InvalidDataException(
                "The virtual crossover DSP plot mode is invalid.");
        }
        if (!Enum.IsDefined(PhaseWindowMode) || !Enum.IsDefined(PhaseDetrendMode))
        {
            throw new InvalidDataException("The phase analysis mode is invalid.");
        }
        if (PhaseFdwCycles is not (4 or 6 or 8))
        {
            PhaseFdwCycles = PhaseAnalysisSettings.DefaultFdwCycles;
        }
        if (PhaseGateOffsetMs is { } gateOffset &&
            (!double.IsFinite(gateOffset) || gateOffset is < 0 or > 10_000))
        {
            throw new InvalidDataException("The phase gate offset is invalid.");
        }
        if (PhaseDetrendMs is { } detrend &&
            (!double.IsFinite(detrend) || detrend is < 0 or > 10_000))
        {
            throw new InvalidDataException("The phase detrend is invalid.");
        }
        if (!IsValidGatePart(PhaseGateLeftMs) ||
            !IsValidGatePart(PhaseGatePlateauMs) ||
            !IsValidGatePart(PhaseGateRightMs) ||
            PhaseGateLeftMs + PhaseGatePlateauMs + PhaseGateRightMs <= 0)
        {
            throw new InvalidDataException("The phase gate window is invalid.");
        }

        foreach (VirtualCrossoverChannelPairSettings pair in Pairs)
        {
            pair.Validate();
        }
    }

    private static bool IsValidGatePart(double milliseconds) =>
        double.IsFinite(milliseconds) && milliseconds is >= 0 and <= 1_000;
}
