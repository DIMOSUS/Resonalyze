using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// What the Virtual DSP lower plot shows: one curve per channel's DSP chain
/// (magnitude / phase / group delay), or one adjacent channel pair's junction
/// view — the lag-domain correlation (<see cref="Correlation"/>) or the
/// per-band arrival-coherence ladder (<see cref="Coherence"/>). Neither
/// junction mode ever reaches the stored project field — each persists as an
/// additive flag (see
/// <see cref="VirtualCrossoverProjectFile.SetDspPlotMode"/>) so older builds
/// open such a session on the magnitude view instead of rejecting an unknown
/// enum value.
/// </summary>
public enum DspPlotMode
{
    Magnitude,
    Phase,
    GroupDelay,
    Correlation,
    Coherence
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
    /// Null (the gate dialog's Auto) follows this side's earliest estimated
    /// channel IR start automatically, so the gate tracks source and delay
    /// changes until the user pins it in the gate dialog.
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
/// The EQ target as a session stores it. A flat mirror of the shape and the
/// styling, deliberately not a reference to a preset: presets are a starting
/// point whose numbers can change between versions, while a session has to open
/// aiming at exactly the curve it was tuned against.
/// </summary>
/// <remarks>
/// A bad field here does not fail the load: <see cref="ToCurve"/> normalizes it
/// away instead, because moving a whole tuning session aside over a decoration
/// would be the worse answer. Normalizing is not optional though — a target is
/// not only drawn, it also fills the settings dialog, and this file allows
/// named floating-point literals (see EqTargetCurve.Normalized).
/// </remarks>
public sealed class VirtualCrossoverTargetSettings
{
    public TargetPreset Preset { get; set; } = TargetPreset.Flat;
    public double TiltDbPerOctave { get; set; }
    public double BassShelfGainDb { get; set; }
    public double BassShelfFrequencyHz { get; set; } = 100;
    public double BassShelfWidthOctaves { get; set; } = 1.5;
    public double TrebleShelfGainDb { get; set; }
    public double TrebleShelfFrequencyHz { get; set; } = 5_000;
    public double TrebleShelfWidthOctaves { get; set; } = 1.5;
    public double PresenceGainDb { get; set; }
    public double PresenceFrequencyHz { get; set; } = 3_000;
    public double PresenceWidthOctaves { get; set; } = 1.0;
    public double ToleranceDb { get; set; } = 3;
    public TargetDeviationMode DeviationMode { get; set; } = TargetDeviationMode.Deviation;
    public int ColorArgb { get; set; } = unchecked((int)0xFF37C8A0);
    public double StrokeThickness { get; set; } = 2;
    public OverlayLineStyle LineStyle { get; set; } = OverlayLineStyle.Dash;
    public int SmoothingInverseOctaves { get; set; }

    // Normalized on the way out, never on the way in: what the app produces is
    // already sound, and the file is the only place a NaN or an undefined enum
    // can enter from.
    internal EqTargetCurve ToCurve() => new EqTargetCurve(
        Preset,
        new TargetCurveSpec(
            TiltDbPerOctave,
            BassShelfGainDb,
            BassShelfFrequencyHz,
            BassShelfWidthOctaves,
            TrebleShelfGainDb,
            TrebleShelfFrequencyHz,
            TrebleShelfWidthOctaves,
            PresenceGainDb,
            PresenceFrequencyHz,
            PresenceWidthOctaves),
        ToleranceDb,
        DeviationMode,
        Color.FromArgb(ColorArgb),
        StrokeThickness,
        LineStyle,
        SmoothingInverseOctaves).Normalized();

    internal static VirtualCrossoverTargetSettings FromCurve(EqTargetCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        return new VirtualCrossoverTargetSettings
        {
            Preset = curve.Preset,
            TiltDbPerOctave = curve.Spec.TiltDbPerOctave,
            BassShelfGainDb = curve.Spec.BassShelfGainDb,
            BassShelfFrequencyHz = curve.Spec.BassShelfFrequencyHz,
            BassShelfWidthOctaves = curve.Spec.BassShelfWidthOctaves,
            TrebleShelfGainDb = curve.Spec.TrebleShelfGainDb,
            TrebleShelfFrequencyHz = curve.Spec.TrebleShelfFrequencyHz,
            TrebleShelfWidthOctaves = curve.Spec.TrebleShelfWidthOctaves,
            PresenceGainDb = curve.Spec.PresenceGainDb,
            PresenceFrequencyHz = curve.Spec.PresenceFrequencyHz,
            PresenceWidthOctaves = curve.Spec.PresenceWidthOctaves,
            ToleranceDb = curve.ToleranceDb,
            DeviationMode = curve.DeviationMode,
            ColorArgb = curve.Color.ToArgb(),
            StrokeThickness = curve.StrokeThickness,
            LineStyle = curve.LineStyle,
            SmoothingInverseOctaves = curve.SmoothingInverseOctaves
        };
    }
}

/// <summary>
/// One channel of the virtual crossover: which measurement feeds it and the DSP
/// chain applied before the virtual sum. The source is re-resolved on load — by
/// history entry first, then by file path, and finally beside the imported session
/// file itself (see <see cref="VirtualCrossoverSourceLocator"/>) — so a renamed
/// history label or a moved file degrades gracefully instead of failing the whole
/// project.
/// </summary>
public sealed class VirtualCrossoverChannelSettings
{
    // Schema v6 payload, kept only so an older file deserializes for migration:
    // Mute, Bypass and the two curve toggles describe the BLOCK rather than one
    // side's measurement, so v7 moved them onto the pair (see
    // VirtualCrossoverChannelPairSettings and the migration that folds these up).
    // Nullable purely to tell "absent" from a real value; nothing but Migrate
    // reads them, and it clears them once the pair carries the answer.
    [JsonPropertyName("enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyEnabled { get; set; }
    [JsonPropertyName("bypass")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyBypass { get; set; }
    [JsonPropertyName("showRawCurve")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyShowRawCurve { get; set; }
    [JsonPropertyName("showProcessedCurve")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyShowProcessedCurve { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public string? SourceFilePath { get; set; }

    /// <summary>
    /// The same measurement as <see cref="SourceFilePath"/>, expressed relative to
    /// the folder of the session file this project came from (see
    /// <see cref="VirtualCrossoverSourceLocator.Relativize"/>). Absolute paths are
    /// written on the machine that measured, so they are the first thing a session
    /// loses when it travels; the relative one survives as long as the measurements
    /// travel with it in the same arrangement.
    /// <para>
    /// In memory this holds what the loaded session carried, and it stays put: what
    /// each WRITE puts on the wire — the export's own folder, nothing for the
    /// autosave — is decided per write and does not touch the value here. Null when
    /// there is no source, when the measurement sits on another volume, or when the
    /// project was never loaded from a session file. Additive: files written before
    /// it existed simply have no such property.
    /// </para>
    /// </summary>
    public string? SourceRelativePath { get; set; }

    /// <summary>
    /// The moving-microphone capture attached to this side, by path — the same
    /// discipline as <see cref="SourceFilePath"/>, and for the same reason.
    /// </summary>
    /// <remarks>
    /// A reference rather than the capture itself. The payload is a raw bin spectrum
    /// (about 900 kB per channel, most of it the spectrum), and a session carries up
    /// to sixteen sides and is rewritten on every knob turn — embedding would put
    /// megabytes through the debounced autosave to save re-finding a file the session
    /// already knows how to re-find for its measurements. Absent from files written
    /// before the hybrid view existed, which therefore open with no average attached.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpatialAveragePath { get; set; }

    /// <summary>
    /// The same capture expressed relative to the exporting session's folder, so an
    /// exported session finds it again beside the measurements. Written per export,
    /// exactly like <see cref="SourceRelativePath"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpatialAverageRelativePath { get; set; }

    public Guid? HistoryEntryId { get; set; }

    public double GainDb { get; set; }
    public double DelayMs { get; set; }
    public bool InvertPolarity { get; set; }

    public CrossoverKind CrossoverKind { get; set; } = CrossoverKind.Off;
    public CrossoverEdge LowPassEdge { get; set; } =
        new(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);
    public CrossoverEdge HighPassEdge { get; set; } =
        new(CrossoverFilterFamily.LinkwitzRiley, 2_000, 24);

    // Schema v7 payload, kept only so an older file deserializes for migration:
    // the per-channel all-pass stage became a band of the PEQ bank in v8 (see the
    // v7→v8 step in Migrate, which appends it to PeqBands and clears these).
    // Nullable purely to tell "absent" from a real value; nothing but Migrate
    // reads them.
    // A STRING rather than the enum, so a hand-edited or truncated value cannot take
    // the file down: the enum converter throws on a name it does not know, and that
    // throw happens during deserialization — before Migrate, which is where this
    // field's tolerance is supposed to live. Parsed there instead, where an
    // unreadable type simply means "no all-pass".
    [JsonPropertyName("allPassType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyAllPassType { get; set; }
    [JsonPropertyName("allPassFrequencyHz")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyAllPassFrequencyHz { get; set; }
    [JsonPropertyName("allPassQ")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyAllPassQ { get; set; }

    public double PeqPreampDb { get; set; }
    public List<PeqBand> PeqBands { get; set; } = new();
    /// <summary>The PEQ file the bands came from; display only.</summary>
    public string? PeqSourceName { get; set; }

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
        if (!double.IsFinite(GainDb) ||
            Math.Abs(GainDb) > DspChannelChain.MaximumGainDb)
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
                !double.IsFinite(band.GainDb) ||
                !Enum.IsDefined(band.Type))
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

    /// <summary>
    /// View state: the block is folded to its header in the tool. Both sides share
    /// it — the fold belongs to the block on screen, not to a measurement — and it
    /// changes nothing the chain computes. Absent from older files, which therefore
    /// open expanded.
    /// </summary>
    public bool Collapsed { get; set; }

    /// <summary>
    /// Whether the channel takes part at all — Mute in the tool. Shared by both
    /// sides, like everything below: these four switches describe the BLOCK, not a
    /// measurement. A driver pair is one part of the system, and muting the left
    /// tweeter while the right one still plays describes no setup worth predicting;
    /// per side they also made a side switch quietly change what the plot drew.
    /// Moved here in schema v7 (see the migration).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When set (and the channel is enabled), the channel contributes its raw
    /// measured signal with the whole DSP chain bypassed — no gain, delay,
    /// polarity, crossover or PEQ — for an A/B against the processed result.
    /// </summary>
    public bool Bypass { get; set; }

    // Curve visibility on the acoustic plot, per channel block.
    public bool ShowRawCurve { get; set; }
    public bool ShowProcessedCurve { get; set; } = true;

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
/// The microphone calibration a session was tuned with, carried INSIDE the
/// session as the curve itself rather than as a reference to the machine's
/// calibration list. A calibration describes the microphone the measurements
/// were taken with, so it belongs with the measurements: a session that travels
/// with its data must arrive with the correction its author saw, on a machine
/// that has never heard of that file. A few dozen (Hz, dB) points cost nothing
/// next to the impulse responses the session already points at.
/// </summary>
public sealed class VirtualCrossoverCalibrationSettings
{
    /// <summary>The name the author's calibration list showed for it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The file the curve came from, by name only — the folder means nothing on
    /// another machine. Null for a curve that was estimated rather than read.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; set; }

    /// <summary>Ascending <c>[frequency Hz, correction dB]</c> pairs.</summary>
    public List<double[]> Points { get; set; } = new();

    public static VirtualCrossoverCalibrationSettings From(
        CalibrationFile calibration,
        string name,
        string? fileName)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        return new VirtualCrossoverCalibrationSettings
        {
            Name = name,
            FileName = fileName,
            Points = calibration.Points
                .Select(point => new[] { point.FrequencyHz, point.Decibels })
                .ToList()
        };
    }

    public CalibrationFile ToCalibrationFile() =>
        CalibrationFile.FromPoints(
            Points.Select(point => new CalibrationPoint(point[0], point[1])),
            Name);

    public void Validate()
    {
        Name = Name?.Trim() ?? string.Empty;
        FileName = string.IsNullOrWhiteSpace(FileName) ? null : FileName.Trim();
        // Two DISTINCT frequencies: the reader merges duplicates into one knot,
        // and a one-knot curve is no curve — it would load as "available", apply
        // nothing, and fail this very check on the next save.
        if (Points.Any(point =>
                point is not { Length: 2 } ||
                !double.IsFinite(point[0]) || point[0] <= 0 ||
                !double.IsFinite(point[1])) ||
            Points.Select(point => point[0]).Distinct().Count() < 2)
        {
            throw new InvalidDataException("The session's calibration curve is invalid.");
        }
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
    public const int CurrentVersion = 8;
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

    // ---------------------------------------------------------- DSP processor

    // The processor this project is designed for, by catalog id (see
    // DspProcessorCatalog). Additive: a file from before the selector, or one naming a
    // device this build does not know, opens as Custom and keeps the numbers below —
    // exactly the simulation that file described.
    public string? DspProcessorModelId { get; set; }

    // The rate the processor runs its filters at, and the ONLY rate the simulated
    // biquads are designed for — deliberately independent of the rate the channels
    // were measured at, so a 48 kHz sound card can carry a 96 kHz processor. Null
    // means "follow the measurements", the Custom default; a named model ignores it
    // and answers from the catalog.
    public int? DspProcessorSampleRateHz { get; set; }

    // How the processor READS the Q of a peaking band. It does not change the
    // simulation — every band here is an RBJ biquad — only how the numbers are
    // stated where they leave for the device (the tuning sheets).
    public PeqQConvention DspProcessorQConvention { get; set; } = PeqQConvention.Rbj;

    /// <summary>
    /// True while this project states no rate of its own and takes the measurements'
    /// instead. That is what a file written before the selector says, and what the DSP
    /// processor dialog's "Follow measurements" entry writes — deliberately distinct
    /// from a STATED rate that happens to equal the measurements today, which keeps
    /// its number when the measurements are replaced at another rate. A named model
    /// never follows: it brings its own.
    /// </summary>
    [JsonIgnore]
    public bool DspProcessorRateFollowsMeasurements =>
        DspProcessorSampleRateHz == null &&
        DspProcessorCatalog.Preset(DspProcessorModelId) == null;

    /// <summary>
    /// The processor this project is designed for, resolved against the rate its
    /// measurements were taken at: a named model answers from the catalog (so a
    /// corrected preset corrects every project naming it), a Custom one answers with
    /// its stored rate, and one that follows answers with the measurements'.
    /// </summary>
    public DspProcessorProfile ResolveDspProcessor(int measurementSampleRateHz)
    {
        if (measurementSampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(measurementSampleRateHz));
        }

        return DspProcessorCatalog.Preset(DspProcessorModelId)?.ToProfile() ??
            DspProcessorProfile.Custom(
                DspProcessorSampleRateHz ?? measurementSampleRateHz,
                DspProcessorQConvention);
    }

    /// <summary>
    /// Records a processor choice. <paramref name="followsMeasurements"/> stores the
    /// INTENT rather than the number, which is the one thing the profile itself cannot
    /// carry — its rate is always resolved (see
    /// <see cref="DspProcessorRateFollowsMeasurements"/>).
    /// </summary>
    public void SetDspProcessor(DspProcessorProfile profile, bool followsMeasurements)
    {
        ArgumentNullException.ThrowIfNull(profile);
        DspProcessorModelId = profile.ModelId;
        DspProcessorQConvention = profile.QConvention;
        DspProcessorSampleRateHz = followsMeasurements && profile.IsCustom
            ? null
            : profile.SampleRateHz;
    }

    // The stereo Auto delay scene offset (ms) ON THE WIRE: the magnitude
    // with the steering layout in its SIGN (negative = right-hand drive) —
    // deliberately the exact pre-flag format. A build from before
    // StereoRightHandDrive existed reads an RHD session correctly and even
    // RESAVES it without silently flipping it to LHD: the unknown flag would
    // not survive such a resave, the sign does. In-app code reads the
    // magnitude via StereoSceneOffsetMagnitudeMs and the layout via the
    // flag, and writes both through SetStereoScene so they never disagree.
    public double StereoSceneOffsetMs { get; set; } = 0.25;

    // The steering position the stereo Auto delay aligns for: false = LHD
    // (the left side is the timing reference and the right side leads by the
    // scene offset), true = RHD (mirrored — the right side is the reference
    // and lags the left by the offset). Kept explicit despite the sign
    // above carrying the same fact, so a zero offset still remembers the
    // layout; Migrate re-aligns the pair when a legacy or foreign file has
    // only one of them. Additive: older files lack it and open as LHD
    // unless a negative offset says otherwise.
    public bool StereoRightHandDrive { get; set; }

    // RHD with a ZERO offset still needs its layout on the wire — the sign
    // IS the layout for pre-flag builds, IEEE -0.0 neither compares below
    // zero nor survives a decimal round-trip, and the explicit flag does not
    // survive an old build's resave. So a zero RHD magnitude serializes as
    // this tiny negative marker instead: a tenth of the UI's 0.01 ms grid
    // and a twentieth of a sample at 48 kHz, i.e. exactly zero to every
    // consumer (old builds apply it as an inaudible scene offset and
    // preserve it on resave), and the magnitude accessor reads it back as
    // zero. The UI cannot produce a genuine 0.001 ms offset, so the marker
    // is unambiguous.
    private const double RhdZeroOffsetMarkerMs = 0.001;

    /// <summary>The scene offset as the UI edits it: a layout-neutral,
    /// non-negative magnitude (the sign on the wire belongs to the layout —
    /// see <see cref="StereoSceneOffsetMs"/> and the zero-marker note).</summary>
    [JsonIgnore]
    public double StereoSceneOffsetMagnitudeMs =>
        Math.Abs(StereoSceneOffsetMs) <= RhdZeroOffsetMarkerMs
            ? 0
            : Math.Abs(StereoSceneOffsetMs);

    /// <summary>
    /// The one writer of the stereo scene: keeps the wire sign and the
    /// layout flag consistent (see <see cref="StereoSceneOffsetMs"/>).
    /// </summary>
    public void SetStereoScene(double offsetMagnitudeMs, bool rightHandDrive)
    {
        StereoRightHandDrive = rightHandDrive;
        StereoSceneOffsetMs = rightHandDrive
            ? -Math.Max(Math.Abs(offsetMagnitudeMs), RhdZeroOffsetMarkerMs)
            : Math.Abs(offsetMagnitudeMs);
    }

    // The intentional level difference (dB) the Auto delay gain balance aims
    // for, stored as LEFT minus RIGHT: the default asks for the left side
    // 1 dB BELOW the right, the same image direction as the scene offset
    // traded as level instead of time. The tuner's own figure, not a value
    // derived from the offset. The UI edits it as a layout-neutral,
    // non-negative NEAR-SIDE CUT; the sign written here follows
    // StereoRightHandDrive (LHD: negative, RHD: positive), so older builds
    // read the same file unchanged. Additive: older files lack it and open
    // on this default.
    public double StereoLevelDifferenceDb { get; set; } = -1.0;

    // Which side the tool currently displays and edits (view state).
    public bool ActiveSideRight { get; set; }

    // Acoustic-plot view state shared by all channels.
    //
    // The Sum is answered PER VIEW. On the magnitude plot the sum is the point
    // of the whole tool; on the phase plot it is one more trace across an
    // already dense picture, and the same session usually wants it there and not
    // here. The impulse view draws no sum at all, so it has no flag. This one
    // stays the magnitude answer, so a file written by this build still opens
    // the way it looks here in a build that knows the single flag only; the
    // phase answer is additive and inherits it when the file predates it (see
    // ShowSumCurveOnPhase).
    public bool ShowSumCurve { get; set; } = true;
    public bool? ShowSumCurvePhase { get; set; }
    public bool ShowLossCurve { get; set; }

    /// <summary>
    /// Whether the magnitude view draws the hybrid — each channel from its spatial
    /// average — rather than the impulse responses alone.
    /// </summary>
    /// <remarks>
    /// Stored with the captures it needs, because the two are one decision: a session
    /// that brings its averages back and then opens on the point measurements would
    /// make the user re-tick the toggle every time. The tick is dropped on load when
    /// the set no longer has an average on every channel that plays, so a session
    /// whose captures went missing simply opens honest. Additive: files written
    /// before the hybrid view existed carry none and open honest too.
    /// </remarks>
    public bool ShowHybridCurves { get; set; }

    /// <summary>
    /// Whether the Sum is drawn on the PHASE view. A file written before the two
    /// views answered separately carries no such flag and inherits the magnitude
    /// answer, which is exactly what it used to draw.
    /// </summary>
    [JsonIgnore]
    public bool ShowSumCurveOnPhase
    {
        get => ShowSumCurvePhase ?? ShowSumCurve;
        set => ShowSumCurvePhase = value;
    }
    public bool ShowPhaseView { get; set; }
    // The main plot's impulse view (the gated IR preview promoted to the main
    // plot). Additive: older files lack it, and when set it wins over
    // ShowPhaseView. Kept as a second flag so files written by this version
    // still open in older builds (which fall back to magnitude/phase).
    public bool ShowImpulseView { get; set; }

    // The EQ target curve drawn over the acoustic plot: whether it is shown, the
    // level (dB) it hangs at, and the target itself. Additive: older files lack
    // all three and open with the curve hidden, on the app's current target.
    public bool ShowTargetCurve { get; set; }
    public double TargetLevelDb { get; set; }

    /// <summary>
    /// The EQ target this session was tuned against — the whole custom shape,
    /// not a preset name, so a session that travels aims at the curve its owner
    /// drew rather than at whatever the receiving machine happened to have set.
    /// Loading a session applies it as the app's target (the EQ Wizard owns and
    /// persists that one definition); null in a file written before the target
    /// was stored, and then the current target is kept and written on the next
    /// save.
    /// </summary>
    public VirtualCrossoverTargetSettings? Target { get; set; }
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

    // The junction-coherence ladder of the lower plot — the second junction
    // mode, additive for the same reason. Correlation wins if a hand-edited
    // file sets both (see EffectiveDspPlotMode).
    public bool DspPlotCoherenceView { get; set; }

    // Which adjacent channel pair the junction views (correlation and
    // coherence — they share the selector) analyze, as an index into the
    // by-band-ordered pair list (0 = the lowest junction). Additive.
    public int CorrelationPairIndex { get; set; }

    /// <summary>
    /// The in-memory lower-plot mode: <see cref="DspPlotMode.Correlation"/> or
    /// <see cref="DspPlotMode.Coherence"/> when the matching flag is set, the
    /// stored enum value otherwise. Write through
    /// <see cref="SetDspPlotMode"/> so the multi-field representation cannot
    /// half-apply.
    /// </summary>
    [JsonIgnore]
    public DspPlotMode EffectiveDspPlotMode =>
        DspPlotCorrelationView ? DspPlotMode.Correlation
        : DspPlotCoherenceView ? DspPlotMode.Coherence
        : DspPlotMode;

    public void SetDspPlotMode(DspPlotMode mode)
    {
        DspPlotCorrelationView = mode == DspPlotMode.Correlation;
        DspPlotCoherenceView = mode == DspPlotMode.Coherence;
        DspPlotMode = mode is DspPlotMode.Correlation or DspPlotMode.Coherence
            ? DspPlotMode.Magnitude
            : mode;
    }

    // The microphone calibration applied to the magnitude curves. The
    // measurement is loopback-referenced, so calibration is optional and off by
    // default. Two fields state it: Calibration is the CURVE the session is tuned
    // with and is what another machine reads; CalibrationId is the entry of THIS
    // machine's calibration list the selection maps to — a local name for the
    // same curve, null when the session is tuned with a curve it carries itself
    // and no configured entry matches. An id alone (no curve) is how sessions
    // were written before the curve travelled, and it is read as a hint, never
    // as an identity: the "90deg" id is minted on every machine that migrated a
    // legacy 90° slot, so two machines' ids agreeing says nothing about their
    // files. See VirtualCrossoverCalibrationSelection.
    public string? CalibrationId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VirtualCrossoverCalibrationSettings? Calibration { get; set; }

    // Schema v5 payload, kept only so an older file deserializes for migration:
    // the selection used to be one of three fixed modes.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyMicrophoneCalibrationMode? CalibrationMode { get; set; }

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
            // The autosave has no folder of its own for a relative path to be
            // relative TO, so it writes none (see WriteWithExportRelativePaths).
            WriteWithExportRelativePaths(null, () =>
            {
                using FileStream stream = new(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);
                JsonSerializer.Serialize(stream, this, SerializerOptions);
                stream.Flush(flushToDisk: true);
            });

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
    /// project file), so a tuning setup can be shared or archived. Each source also
    /// gets a path relative to THIS file's folder, which is what lets the export
    /// find its measurements again on another machine.
    /// </summary>
    public void SaveTo(string path)
    {
        Validate();
        SavedAtUtc = DateTimeOffset.UtcNow;
        WriteWithExportRelativePaths(
            SafeDirectoryOf(path),
            () => AtomicFile.Write(
                path,
                stream => JsonSerializer.Serialize(stream, this, SerializerOptions)));
    }

    // A relative path means nothing without the folder it was computed against, so
    // each write states its own: an export writes paths relative to ITS folder, and
    // the internal autosave writes none — it lives in the application data folder,
    // no measurement sits beside it, and a value left over from some earlier export
    // would be a confident wrong answer if that file were hand-carried to another
    // machine and imported.
    //
    // Strictly a property of the WRITE, never of the project: the values are swapped
    // in around the serialization and put back afterwards. The live ones belong to
    // the session this project was IMPORTED from and are still in use — the tool
    // reads them to find measurements whose absolute paths are dead, including
    // during the relink prompt, which an autosave can fire behind (a modal dialog
    // keeps pumping the message loop, so the debounced save runs while the user
    // reads the question). Clearing them for real there would delete the very hint
    // the relink is about to need, and would also leave a re-export unable to
    // restate the arrangement it was imported with.
    private void WriteWithExportRelativePaths(string? exportDirectory, Action write)
    {
        List<(VirtualCrossoverChannelSettings Side, string? Source, string? Average)>
            restore = [];
        foreach (VirtualCrossoverChannelPairSettings pair in Pairs)
        {
            foreach (VirtualCrossoverChannelSettings side in new[] { pair.Left, pair.Right })
            {
                restore.Add((
                    side, side.SourceRelativePath, side.SpatialAverageRelativePath));
                side.SourceRelativePath = exportDirectory == null
                    ? null
                    : VirtualCrossoverSourceLocator.Relativize(
                        side.SourceFilePath, exportDirectory);
                // The capture travels with the measurements and is found the same
                // way, so it is restated against the same folder.
                side.SpatialAverageRelativePath = exportDirectory == null
                    ? null
                    : VirtualCrossoverSourceLocator.Relativize(
                        side.SpatialAveragePath, exportDirectory);
            }
        }

        try
        {
            write();
        }
        finally
        {
            foreach ((VirtualCrossoverChannelSettings side, string? source, string? average)
                in restore)
            {
                side.SourceRelativePath = source;
                side.SpatialAverageRelativePath = average;
            }
        }
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
        file.ProjectDirectory = SafeDirectoryOf(path);
        return file;
    }

    /// <summary>
    /// The folder the session file was imported from, kept so a channel whose
    /// stored absolute path no longer exists can be looked for beside the session
    /// itself (see <see cref="VirtualCrossoverSourceLocator"/>). Null for the
    /// internal autosave, which lives in the application data folder: measurements
    /// never sit there, so searching it could only produce a false match. Not
    /// serialized — it describes where the file came from, not what it contains.
    /// </summary>
    [JsonIgnore]
    public string? ProjectDirectory { get; private set; }

    private static string? SafeDirectoryOf(string path)
    {
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch (Exception exception) when (
            exception is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // The file just opened, so this practically cannot fail; if it does,
            // the session still loads and simply gets no folder to search.
            return null;
        }
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
        if (file.Version == 5)
        {
            // v5 picked the calibration from three fixed modes; it is now an id
            // into the user's calibration list. 90° maps to the entry the
            // settings migration creates from the old second slot — the session
            // says WHICH calibration it was tuned with, and this machine either
            // has that entry or is told it does not.
            file.CalibrationId = MeasurementSettingsFile.ResolveCalibrationId(
                file.CalibrationId,
                file.CalibrationMode,
                legacyUseCalibration: false);
            file.CalibrationMode = null;
            file.Version = 6;
        }
        if (file.Version == 6)
        {
            // v6 kept Mute, Bypass and the two curve toggles per SIDE, so switching
            // sides could silently change what the block contributed and what the plot
            // drew. They belong to the block, and v7 moves them onto the pair.
            // The pair inherits the sides that actually carry a measurement — the single
            // left slot of a mono pair, the one loaded side of a half-loaded pair — so
            // every project that could not disagree opens exactly as it looked. Where two
            // loaded sides DID disagree the louder answer wins: muted, bypassed and
            // "curve shown" each survive, because a mute lost in a migration is the one
            // outcome the tuner has no way to see coming.
            foreach (VirtualCrossoverChannelPairSettings pair in file.Pairs)
            {
                VirtualCrossoverChannelSettings[] both = [pair.Left, pair.Right];
                List<VirtualCrossoverChannelSettings> sides = pair.Mono
                    ? [pair.Left]
                    : both.Where(side => side.HasSource).ToList();
                if (sides.Count == 0)
                {
                    sides = [pair.Left];
                }

                pair.Enabled = sides.TrueForAll(side => side.LegacyEnabled ?? true);
                pair.Bypass = sides.Exists(side => side.LegacyBypass ?? false);
                pair.ShowRawCurve = sides.Exists(side => side.LegacyShowRawCurve ?? false);
                pair.ShowProcessedCurve =
                    sides.Exists(side => side.LegacyShowProcessedCurve ?? true);
                foreach (VirtualCrossoverChannelSettings side in both)
                {
                    side.LegacyEnabled = null;
                    side.LegacyBypass = null;
                    side.LegacyShowRawCurve = null;
                    side.LegacyShowProcessedCurve = null;
                }
            }

            file.Version = 7;
        }
        if (file.Version == 7)
        {
            // v7 kept one all-pass per channel side as its own stage; v8 carries it
            // as a band of the PEQ bank, where the hardware's own EQ slot table
            // holds it (AP1/AP2). The band realizes bit for bit the same biquad the
            // stage ran (pinned by AllPassBandTests), so a migrated project sounds
            // exactly as it did. Defensive on the legacy numbers — Migrate runs
            // before Validate, so a hand-edited stage must degrade to "no all-pass"
            // rather than abort the whole session; and a bank already holding the
            // full 32 bands has no slot to take the stage, so it is dropped there
            // too rather than invalidating the file.
            foreach (VirtualCrossoverChannelPairSettings pair in file.Pairs)
            {
                foreach (VirtualCrossoverChannelSettings side in
                    new[] { pair.Left, pair.Right })
                {
                    AllPassType? stage =
                        Enum.TryParse(side.LegacyAllPassType, out AllPassType parsed) &&
                        Enum.IsDefined(parsed)
                            ? parsed
                            : null;
                    bool firstOrder = stage == AllPassType.FirstOrder;
                    double frequencyHz = side.LegacyAllPassFrequencyHz ?? 0;
                    double q = firstOrder ? 1.0 : side.LegacyAllPassQ ?? 1.0;
                    if (stage is AllPassType.FirstOrder or AllPassType.SecondOrder &&
                        double.IsFinite(frequencyHz) && frequencyHz > 0 &&
                        double.IsFinite(q) && q > 0)
                    {
                        // A v7 side could hold a FULL bank and an all-pass stage
                        // beside it, which v8 has no room for. Something is lost
                        // either way, so lose the one that can be put back: a bell
                        // is a magnitude correction Auto Tune can propose again,
                        // while an all-pass sits on a junction that was aligned by
                        // ear. The user is told rather than left to find out — see
                        // MigrationNoticeText.
                        if (side.PeqBands.Count >= EqualizationCurve.MaxBandCount)
                        {
                            int last = side.PeqBands.FindLastIndex(
                                band => !band.Type.IsAllPass());
                            if (last >= 0)
                            {
                                side.PeqBands.RemoveAt(last);
                                file.migratedFullBanks++;
                            }
                        }

                        if (side.PeqBands.Count < EqualizationCurve.MaxBandCount)
                        {
                            side.PeqBands.Add(new PeqBand(
                                frequencyHz,
                                q,
                                0,
                                firstOrder
                                    ? PeqBandType.AllPassFirstOrder
                                    : PeqBandType.AllPassSecondOrder));
                        }
                    }

                    side.LegacyAllPassType = null;
                    side.LegacyAllPassFrequencyHz = null;
                    side.LegacyAllPassQ = null;
                }
            }

            file.Version = 8;
        }

        // The scene offset's wire SIGN and the layout flag state one fact
        // (see the properties): re-align them here for files that carry only
        // one — a pre-flag file (sign only, possibly resaved by an older
        // build that dropped the flag) or a hand-edited one. The sign is the
        // wider channel (every build honors it), so it wins over a missing
        // flag; a set flag over a positive offset wins the other way, so a
        // zero-or-positive RHD file keeps its layout.
        if (file.StereoSceneOffsetMs < 0)
        {
            file.StereoRightHandDrive = true;
        }
        else if (file.StereoRightHandDrive)
        {
            file.StereoSceneOffsetMs = -file.StereoSceneOffsetMs;
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

    // How many channel sides had a full 32-band bank when their v7 all-pass stage
    // was migrated into it, and so gave up their last gain-bearing band to make
    // room. Not persisted: it describes THIS load, and the host tells the user
    // once (see MigrationNoticeText).
    private int migratedFullBanks;

    /// <summary>
    /// What this load had to change beyond restating it, or null when nothing was
    /// lost. A migration that silently drops a filter is how a tune quietly stops
    /// being the tune that was saved.
    /// </summary>
    [JsonIgnore]
    public string? MigrationNoticeText =>
        migratedFullBanks == 0
            ? null
            : $"{migratedFullBanks} channel side" +
                (migratedFullBanks == 1 ? " had" : "s had") +
                " a full 32-filter bank and an all-pass stage beside it. The " +
                "all-pass is a band of the bank in this version, and there was no " +
                "free slot for it, so the last gain-bearing filter of " +
                (migratedFullBanks == 1 ? "that side" : "each of those sides") +
                " gave up its place — an equalizer band can be fitted again, an " +
                "all-pass sits on a junction that was aligned by ear. Check those " +
                "channels before saving over the session.";

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
        // Signed on the wire (the sign is the layout); only the magnitude is
        // bounded.
        if (!double.IsFinite(StereoSceneOffsetMs) ||
            Math.Abs(StereoSceneOffsetMs) > MaximumSceneOffsetMs)
        {
            throw new InvalidDataException("The stereo scene offset is invalid.");
        }
        if (!double.IsFinite(StereoLevelDifferenceDb) ||
            Math.Abs(StereoLevelDifferenceDb) >
                GainBalanceEngine.MaxLevelDifferenceDb)
        {
            throw new InvalidDataException(
                "The stereo L/R level difference is invalid.");
        }
        if (!OverlaySmoothing.IsValid(SmoothingInverseOctaves))
        {
            throw new InvalidDataException(
                "The virtual crossover smoothing setting is invalid.");
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
        Calibration?.Validate();
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
