using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// What the Virtual DSP lower plot shows: one curve per channel's DSP chain
/// (magnitude / phase / group delay), or the junction-correlation view of one
/// adjacent channel pair. <see cref="Correlation"/> never reaches the stored
/// project field — it persists as an additive flag (see
/// <see cref="VirtualCrossoverProjectFile.SetDspPlotMode"/>) so older builds
/// open such a session on the magnitude view instead of rejecting an unknown
/// enum value.
/// </summary>
public enum DspPlotMode
{
    Magnitude,
    Phase,
    GroupDelay,
    Correlation
}

/// <summary>
/// WHERE the phase gate sits for one side: the offset the Tukey window's left shoulder
/// ends at, and the τ the traces are detrended against.
/// <para>
/// Only placement lives here, because only placement is physical: the left and right
/// drivers sit at different distances from the microphone, so their arrivals — and the
/// reflections the gate exists to cut — do not land at the same time. Everything that
/// decides HOW the phase is read stays on the project: the window's left/plateau/right
/// LENGTHS (they set the frequency resolution), the window mode, the detrend mode and the
/// FDW cycle count. Two sides read through different-length windows would not be
/// comparable, and comparing them is what the view is for.
/// </para>
/// </summary>
public sealed class VirtualCrossoverPhaseGateSettings
{
    /// <summary>
    /// Null follows this side's earliest processed arrival automatically, so the gate
    /// tracks source and delay changes until the user pins it in the gate dialog.
    /// </summary>
    public double? OffsetMs { get; set; }

    /// <summary>
    /// The linear-phase reference (ms, absolute from the IR start) removed from every
    /// channel and the sum alike, so the traces stay readable while their relative phase
    /// is preserved. Null follows this side's earliest processed arrival.
    /// </summary>
    public double? DetrendMs { get; set; }

    public void Validate()
    {
        if (OffsetMs is { } offset &&
            (!double.IsFinite(offset) || offset is < 0 or > 10_000))
        {
            throw new InvalidDataException("The phase gate offset is invalid.");
        }
        if (DetrendMs is { } detrend &&
            (!double.IsFinite(detrend) || detrend is < 0 or > 10_000))
        {
            throw new InvalidDataException("The phase detrend is invalid.");
        }
    }
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

    /// <summary>
    /// The largest all-pass Q a project accepts. Unlike the Chebyshev ripple cap this is
    /// NOT a mathematical limit — an extreme Q is merely a very sharp phase turn and stays
    /// perfectly stable — it is just the sane range the UI offers, kept here so the field
    /// and the validator cannot drift apart. The real lower bound (Q > 0, or alpha divides
    /// by zero) is enforced by the DSP itself.
    /// </summary>
    public const double MaximumAllPassQ = 20.0;

    /// <summary>
    /// The all-pass stage: a phase rotation with no effect on magnitude. Independent of
    /// <see cref="CrossoverKind"/> — hardware runs it as its own stage, so it applies
    /// even with the crossover off.
    /// </summary>
    public AllPassType AllPassType { get; set; } = AllPassType.Off;
    public double AllPassFrequencyHz { get; set; } = 2_000;
    public double AllPassQ { get; set; } = 1.0;

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
        var allPass = new AllPassSpec(AllPassType, AllPassFrequencyHz, AllPassQ);
        return new DspChannelChain(GainDb, DelayMs, InvertPolarity, crossover, peq, allPass);
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
        ValidateAllPass();
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

    // Validated even when the type is Off, for the same reason as the crossover edges:
    // the values stay visible (greyed) in the UI and must round-trip as sane ones.
    private void ValidateAllPass()
    {
        if (!Enum.IsDefined(AllPassType))
        {
            throw new InvalidDataException("The all-pass type is invalid.");
        }
        if (!double.IsFinite(AllPassFrequencyHz) || AllPassFrequencyHz is < 10 or > 24_000)
        {
            throw new InvalidDataException("The all-pass frequency is invalid.");
        }
        // Q drives only the second-order section, but it is bounded regardless so an
        // imported project cannot smuggle in a value the UI could never show.
        if (!double.IsFinite(AllPassQ) || AllPassQ <= 0 || AllPassQ > MaximumAllPassQ)
        {
            throw new InvalidDataException("The all-pass Q is invalid.");
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
        // Ripple only drives the Chebyshev family; outside (0, max] its pole math is
        // undefined (NaN), so it is validated only there, against the same cap the UI
        // and the DSP use.
        if (edge.Family == CrossoverFilterFamily.Chebyshev &&
            (!double.IsFinite(edge.RippleDb) || edge.RippleDb <= 0 ||
             edge.RippleDb > CrossoverFilter.MaximumChebyshevRippleDb))
        {
            throw new InvalidDataException("The crossover passband ripple is invalid.");
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
    public const int CurrentVersion = 5;
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

    // The psychoacoustic magnitude smoothing mode (see SpectrumSmoothing in
    // dsp). Stored as a separate additive flag while SmoothingInverseOctaves
    // keeps the plain base width, so an older build opens such a session as
    // plain 1/6-octave smoothing instead of rejecting the file.
    public bool PsychoacousticSmoothing { get; set; }

    /// <summary>
    /// The in-memory smoothing code of this project (see
    /// <see cref="OverlayFile.SmoothingCode"/> for the pattern): the
    /// psychoacoustic code when the flag is set, the stored width otherwise.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int SmoothingCode =>
        PsychoacousticSmoothing
            ? Resonalyze.Dsp.SpectrumSmoothing.PsychoacousticCode
            : SmoothingInverseOctaves;

    public void SetSmoothingCode(int code)
    {
        PsychoacousticSmoothing =
            Resonalyze.Dsp.SpectrumSmoothing.IsPsychoacoustic(code);
        SmoothingInverseOctaves =
            Resonalyze.Dsp.SpectrumSmoothing.EquivalentInverseOctaves(code);
    }

    // Which curve the per-channel DSP chain plot shows. Additive: older files lack
    // it and default to Magnitude. Never stores Correlation — see the flag below.
    public DspPlotMode DspPlotMode { get; set; } = DspPlotMode.Magnitude;

    // The junction-correlation view of the lower plot, stored as a separate
    // additive flag (same pattern as PsychoacousticSmoothing): the legacy enum
    // field keeps a value every build knows, so an older build opens the
    // session on the magnitude view instead of failing on an unknown enum
    // string. No file version bump.
    public bool DspPlotCorrelationView { get; set; }

    // Which adjacent channel pair the correlation view analyzes, as an index
    // into the by-band-ordered pair list (0 = the lowest junction). Additive.
    public int CorrelationPairIndex { get; set; }

    /// <summary>
    /// The in-memory lower-plot mode: <see cref="DspPlotMode.Correlation"/>
    /// when the flag is set, the stored enum value otherwise. Write through
    /// <see cref="SetDspPlotMode"/> so the dual representation cannot
    /// half-apply.
    /// </summary>
    [JsonIgnore]
    public DspPlotMode EffectiveDspPlotMode =>
        DspPlotCorrelationView ? DspPlotMode.Correlation : DspPlotMode;

    public void SetDspPlotMode(DspPlotMode mode)
    {
        DspPlotCorrelationView = mode == DspPlotMode.Correlation;
        DspPlotMode = mode == DspPlotMode.Correlation
            ? DspPlotMode.Magnitude
            : mode;
    }

    // Microphone calibration applied to the magnitude curves. The measurement is
    // loopback-referenced, so calibration is optional and off by default.
    // Additive: older files lack the field and default to Off.
    public MicrophoneCalibrationMode CalibrationMode { get; set; } =
        MicrophoneCalibrationMode.Off;

    /// <summary>
    /// The phase gate's placement, per SIDE. The two sides' drivers sit at different
    /// distances, so their arrivals — and the reflections the gate exists to cut — do not
    /// land at the same time; one shared gate meant that fitting it on one side threw the
    /// other's traces off.
    /// </summary>
    public VirtualCrossoverPhaseGateSettings PhaseGateLeft { get; set; } = new();
    public VirtualCrossoverPhaseGateSettings PhaseGateRight { get; set; } = new();

    // Schema v4 payload, kept only so an older file deserializes for migration: WHERE the
    // gate sits went per-side in v5. Migrate copies these onto BOTH sides, so a migrated
    // project draws exactly as it did; nothing else reads them. Nullable purely to tell
    // "absent" from a real value — v4's own offset/detrend were already nullable, and null
    // there meant "follow the earliest arrival", which migrates to the same thing.
    [JsonPropertyName("phaseGateOffsetMs")]
    public double? LegacyPhaseGateOffsetMs { get; set; }
    [JsonPropertyName("phaseDetrendMs")]
    public double? LegacyPhaseDetrendMs { get; set; }

    // The Tukey window's LENGTHS stay project-wide, alongside the analysis modes below:
    // they set the frequency resolution the phase is read at, and two sides read through
    // different-length windows cannot be compared against each other — which is the whole
    // reason the view exists. Only the gate's PLACEMENT (offset, and the τ it references)
    // is per-side, since only that follows the drivers' differing distances.
    // These kept v4's names on the wire, so an older file deserializes straight into them.
    public double PhaseGateLeftMs { get; set; } =
        FrequencyResponseOptions.DefaultPhaseLeftMs;
    public double PhaseGatePlateauMs { get; set; } =
        FrequencyResponseOptions.DefaultPhasePlateauMs;
    public double PhaseGateRightMs { get; set; } =
        FrequencyResponseOptions.DefaultPhaseRightMs;

    // These three decide HOW the phase is analysed rather than where it is looked at,
    // so the two sides must be analysed alike.
    //
    // Fixed by default: the Virtual DSP phase view exists to align channels at
    // the listening position, where the early in-cabin reflections FDW removes
    // are physically part of the summed sound — and of any verification
    // measurement taken afterwards. FDW remains a per-project opt-in for
    // inspecting the drivers' direct sound.
    public PhaseWindowMode PhaseWindowMode { get; set; } = PhaseWindowMode.Fixed;
    public int PhaseFdwCycles { get; set; } = PhaseAnalysisSettings.DefaultFdwCycles;
    public PhaseDetrendMode PhaseDetrendMode { get; set; } = PhaseDetrendMode.Auto;

    /// <summary>The gate of one side; the tool always draws and edits the ACTIVE side's.</summary>
    public VirtualCrossoverPhaseGateSettings PhaseGateFor(bool rightSide) =>
        rightSide ? PhaseGateRight : PhaseGateLeft;

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
        if (file.Version == 4)
        {
            // v4 pinned the gate's PLACEMENT once for the whole project, so fitting it on
            // one side threw the other's traces off — the sides' arrivals differ. Both
            // sides inherit the old shared value, leaving a migrated project drawing
            // exactly as before; they diverge only once the user moves one of them. The
            // window's lengths did not move: they kept their names on the project and
            // deserialize straight into it.
            foreach (VirtualCrossoverPhaseGateSettings gate in
                new[] { file.PhaseGateLeft, file.PhaseGateRight })
            {
                gate.OffsetMs = file.LegacyPhaseGateOffsetMs;
                gate.DetrendMs = file.LegacyPhaseDetrendMs;
            }

            file.LegacyPhaseGateOffsetMs = null;
            file.LegacyPhaseDetrendMs = null;
            file.Version = 5;
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
        if (CorrelationPairIndex is < 0 or >= MaximumChannelCount)
        {
            throw new InvalidDataException(
                "The virtual crossover correlation pair index is invalid.");
        }
        if (!Enum.IsDefined(PhaseWindowMode) || !Enum.IsDefined(PhaseDetrendMode))
        {
            throw new InvalidDataException("The phase analysis mode is invalid.");
        }
        if (PhaseFdwCycles is not (4 or 6 or 8))
        {
            PhaseFdwCycles = PhaseAnalysisSettings.DefaultFdwCycles;
        }
        PhaseGateLeft.Validate();
        PhaseGateRight.Validate();
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
