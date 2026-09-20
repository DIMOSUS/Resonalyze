using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Lower-plot mode. Junction modes persist as additive flags, never in the enum field (see <see cref="VirtualCrossoverProjectFile.SetDspPlotMode"/>).</summary>
public enum DspPlotMode
{
    Magnitude,
    Phase,
    GroupDelay,
    Correlation,
    Coherence
}

/// <summary>Per-side phase gate placement; window lengths and analysis modes stay project-wide so the sides stay comparable.</summary>
public sealed class VirtualCrossoverPhaseGateSettings
{
    /// <summary>Null = Auto: follows this side's earliest estimated channel IR start.</summary>
    public double? OffsetMs { get; set; }

    /// <summary>Linear-phase reference (ms from the IR start) removed from every trace; null follows the earliest processed arrival.</summary>
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

/// <summary>The EQ target stored by value, not as a preset reference. See docs/tech/virtual-dsp-session-file.md#eq-target.</summary>
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
    public string? ImportedName { get; set; }
    public double[]? ImportedCurve { get; set; }
    public double ToleranceDb { get; set; } = 3;
    public TargetDeviationMode DeviationMode { get; set; } = TargetDeviationMode.Deviation;
    public int ColorArgb { get; set; } = unchecked((int)0xFF37C8A0);
    public double StrokeThickness { get; set; } = 2;
    public OverlayLineStyle LineStyle { get; set; } = OverlayLineStyle.Dash;
    public int SmoothingInverseOctaves { get; set; }

    // Normalized on the way out only: the file is the only place a NaN or an undefined enum can enter.
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
            PresenceWidthOctaves)
        {
            Imported = ImportedTargetCurve.FromStorage(ImportedName, ImportedCurve)
        },
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
            ImportedName = curve.Spec.Imported?.Name,
            ImportedCurve = curve.Spec.Imported?.ToStorage(),
            ToleranceDb = curve.ToleranceDb,
            DeviationMode = curve.DeviationMode,
            ColorArgb = curve.Color.ToArgb(),
            StrokeThickness = curve.StrokeThickness,
            LineStyle = curve.LineStyle,
            SmoothingInverseOctaves = curve.SmoothingInverseOctaves
        };
    }
}

/// <summary>One channel side. The source is re-resolved on load (see <see cref="VirtualCrossoverSourceLocator"/>).</summary>
public sealed class VirtualCrossoverChannelSettings
{
    // Schema v6 payload, read only by Migrate (moved onto the pair in v7).
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

    /// <summary>Source path relative to the imported session's folder; each write decides its own value. See docs/tech/virtual-dsp-session-file.md#source-paths.</summary>
    public string? SourceRelativePath { get; set; }

    /// <summary>Moving-mic capture by path; not embedded (~900 kB per side through the debounced autosave).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpatialAveragePath { get; set; }

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

    // Schema v7 payload, read only by Migrate (the all-pass became a PEQ band in v8).
    // A string, not the enum: an unknown name would throw during deserialization, before Migrate can tolerate it.
    [JsonPropertyName("allPassType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyAllPassType { get; set; }
    [JsonPropertyName("allPassFrequencyHz")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyAllPassFrequencyHz { get; set; }
    [JsonPropertyName("allPassQ")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? LegacyAllPassQ { get; set; }

    /// <summary>Channel phase control angle (degrees), 0 = unused. Its reference is the channel's own crossover, not stored.</summary>
    public double PhaseRotationDegrees { get; set; }

    public double PeqPreampDb { get; set; }
    public List<PeqBand> PeqBands { get; set; } = new();
    public string? PeqSourceName { get; set; }

    /// <summary>FIR kernel taps stored in the session (schema v11), not by path; files are import/export only.</summary>
    [JsonIgnore]
    public FirFilter? Fir { get; set; }

    [JsonPropertyName("fir")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FirKernelWire? FirWire
    {
        get => Fir == null ? null : FirKernelWire.From(Fir);
        set => Fir = value?.ToFilter();
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FirSourceName { get; set; }

    /// <summary>Constructor design of <see cref="Fir"/>, null for imported kernels; replaced together with the kernel.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FirCrossoverDesign? FirDesign { get; set; }

    [JsonIgnore]
    public bool HasFir => Fir != null;

    /// <summary>Rate the panel last resolved for this side's FIR stage; context, not part of the tune.</summary>
    [JsonIgnore]
    public int? FirRunSampleRateHz { get; set; }

    [JsonIgnore]
    public bool HasFirCrossover => Fir != null && FirDesign != null;

    /// <summary>Corners that junctions, Auto Tune and the phase control read: the IIR crossover when on, else the FIR design's corners scaled by run/design rate. Never used to build the chain.</summary>
    /// <remarks>See docs/tech/virtual-dsp-session-file.md#effective-crossover.</remarks>
    [JsonIgnore]
    public CrossoverSpec EffectiveCrossover =>
        CrossoverKind != CrossoverKind.Off || FirDesignCrossover is not { } fir
            ? new CrossoverSpec(CrossoverKind, LowPassEdge, HighPassEdge)
            : fir;

    /// <summary>The designed FIR stage's own corners, scaled by run/design rate; null where this side runs no designed FIR (an imported kernel names no corners).</summary>
    /// <remarks>Read beside <see cref="EffectiveCrossover"/> where both stages filter at once, since that one answers the IIR while it is on.</remarks>
    [JsonIgnore]
    public CrossoverSpec? FirDesignCrossover
    {
        get
        {
            if (!HasFirCrossover)
            {
                return null;
            }

            FirCrossoverDesign design = FirDesign!;
            double scale = FirRunSampleRateHz is { } runHz && runHz != design.SampleRateHz
                ? (double)runHz / design.SampleRateHz
                : 1.0;
            return new CrossoverSpec(
                design.Kind,
                design.LowPassEdge with { FrequencyHz = design.LowPassEdge.FrequencyHz * scale },
                design.HighPassEdge with { FrequencyHz = design.HighPassEdge.FrequencyHz * scale });
        }
    }

    [JsonIgnore]
    public double? EffectiveHighPassHz => EffectiveCrossover.HighPassHz;

    [JsonIgnore]
    public double? EffectiveLowPassHz => EffectiveCrossover.LowPassHz;

    public bool HasSource =>
        HistoryEntryId.HasValue || !string.IsNullOrWhiteSpace(SourceFilePath);

    /// <param name="zone">Required, not defaulted: the phase control references the low-pass on subwoofers and the high-pass elsewhere.</param>
    public DspChannelChain ToChain(VirtualCrossoverZone zone)
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
        return new DspChannelChain(
            GainDb,
            DelayMs,
            InvertPolarity,
            crossover,
            peq,
            PhaseRotation(zone),
            Fir);
    }

    /// <summary>Phase control angle, reference corner, and which corner it is (the junction search moves one corner at a time).</summary>
    public PhaseRotationSpec PhaseRotation(VirtualCrossoverZone zone)
    {
        bool referenceIsLowPass = zone == VirtualCrossoverZone.Sub;
        return new PhaseRotationSpec(
            PhaseRotationDegrees,
            (referenceIsLowPass ? LowPassEdge : HighPassEdge).FrequencyHz,
            referenceIsLowPass);
    }

    /// <summary>Low-pass corner on subwoofers, high-pass otherwise, as CONFIGURED even when that filter is disengaged (bench-measured behaviour).</summary>
    public double PhaseReferenceHz(VirtualCrossoverZone zone) =>
        PhaseRotation(zone).ReferenceHz;

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
        // Range only, not the hardware's 5.625° grid: editors snap, and a hand-written angle still builds.
        if (!double.IsFinite(PhaseRotationDegrees) ||
            PhaseRotationDegrees is < 0 or > PhaseRotationControl.MaximumDegrees)
        {
            throw new InvalidDataException("The channel phase rotation is invalid.");
        }
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
        if (FirDesign is { } design)
        {
            // A design without its kernel (or of another length) describes nothing; refused, not dropped.
            if (Fir is not { } kernel || kernel.Length != design.TapCount)
            {
                throw new InvalidDataException(
                    "The FIR crossover design does not describe the channel's FIR kernel.");
            }
            if (design.Problem() is { } problem)
            {
                throw new InvalidDataException($"The FIR crossover design is invalid: {problem}");
            }
            // Slopes checked against the constructor's list: the IIR edge check would refuse kernels past 48 dB/oct.
            ValidateDesignEdge(design.LowPassEdge, design.Method);
            ValidateDesignEdge(design.HighPassEdge, design.Method);
        }
    }

    // Same contract as FirCrossoverDesign.Problem: family and slope are validated only where the method reads them.
    private static void ValidateDesignEdge(CrossoverEdge edge, FirCrossoverMethod method)
    {
        if (!Enum.IsDefined(edge.Family) ||
            (method == FirCrossoverMethod.IirMagnitude &&
             !FirCrossoverDesign.SupportedSlopes(edge.Family).Contains(edge.SlopeDbPerOctave)))
        {
            throw new InvalidDataException("The FIR crossover design's slope is invalid.");
        }
        if (!double.IsFinite(edge.FrequencyHz) || edge.FrequencyHz is < 10 or > 24_000)
        {
            throw new InvalidDataException("The FIR crossover design's corner frequency is invalid.");
        }
    }

    // Both edges validated even when unused: they are shown greyed out and must round-trip.
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
        // Ripple matters only for Chebyshev; outside (0, max] its pole math is NaN.
        if (edge.Family == CrossoverFilterFamily.Chebyshev &&
            (!double.IsFinite(edge.RippleDb) || edge.RippleDb <= 0 ||
             edge.RippleDb > CrossoverFilter.MaximumChebyshevRippleDb))
        {
            throw new InvalidDataException("The crossover passband ripple is invalid.");
        }
    }
}

/// <summary>One speaker as an L/R pair; a mono pair uses only <see cref="Left"/> for both sides.</summary>
public sealed class VirtualCrossoverChannelPairSettings
{
    public bool Mono { get; set; }

    /// <summary>Installation zone; guessed by migration for files before v9.</summary>
    public VirtualCrossoverZone Zone { get; set; } = VirtualCrossoverZone.Front;

    public bool Collapsed { get; set; }

    /// <summary>Mute. Block-level, like Bypass and the curve toggles (per side until v7).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Contribute the raw measurement with the whole DSP chain bypassed.</summary>
    public bool Bypass { get; set; }

    public bool ShowRawCurve { get; set; }
    public bool ShowProcessedCurve { get; set; } = true;

    public VirtualCrossoverChannelSettings Left { get; set; } = new();
    public VirtualCrossoverChannelSettings Right { get; set; } = new();

    /// <summary>Builds a side's chain with the block's <see cref="Zone"/>; prefer this over the side's own ToChain.</summary>
    public DspChannelChain ToChain(bool rightSide) => SideFor(rightSide).ToChain(Zone);

    /// <summary>A mono pair always answers with its left set.</summary>
    public VirtualCrossoverChannelSettings SideFor(bool rightSide) =>
        Mono || !rightSide ? Left : Right;

    public void Validate()
    {
        if (!Enum.IsDefined(Zone))
        {
            throw new InvalidDataException("The channel zone is invalid.");
        }

        Left.Validate();
        Right.Validate();
    }
}

/// <summary>Mic calibration carried inside the session as the curve itself. See docs/tech/virtual-dsp-session-file.md#calibration.</summary>
public sealed class VirtualCrossoverCalibrationSettings
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Source file name only; null for an estimated curve.</summary>
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
        // Two DISTINCT frequencies: duplicates merge, and a one-knot curve would load but apply nothing.
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

/// <summary>Which spatial-average family a project reads; project-wide because the hybrid levels a set with one scalar.</summary>
public enum VirtualCrossoverSpatialAverageMode
{
    Off,

    MovingMic,

    MicArray
}

public sealed class VirtualCrossoverProjectFile
{
    public const string CurrentFormat = "resonalyze-virtual-crossover";

    // Bump on an incompatible change and add a Migrate step. Newer files are never migrated: LoadOrDefault backs up, LoadFrom rejects.
    public const int CurrentVersion = 11;

    // Channel letters and the plot palette go up to this count.
    public const int MaximumChannelCount = 12;
    private const string FileName = "virtual-crossover.json";
    private const string ResetBackupFileName = "virtual-crossover.before-reset.json";

    /// <summary>Beyond a couple of ms an inter-side lead is an echo, not an image shift, so larger is a typo.</summary>
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

    /// <summary>Null = not yet settled (pre-array files); the panel guesses while null and stores the guess once a capture exists.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VirtualCrossoverSpatialAverageMode? SpatialAverageMode { get; set; }

    // Schema v1 payload for migration only; v2+ writes an empty array.
    public List<VirtualCrossoverChannelSettings> Channels { get; set; } = new();

    public List<VirtualCrossoverChannelPairSettings> Pairs { get; set; } =
    [
        new VirtualCrossoverChannelPairSettings(),
        new VirtualCrossoverChannelPairSettings(),
        new VirtualCrossoverChannelPairSettings()
    ];

    // Catalog id; an absent or unknown id opens as Custom with the numbers below.
    public string? DspProcessorModelId { get; set; }

    // The only rate the simulated biquads are designed at, independent of the measurement rate.
    // Null follows the measurements; a named model answers from the catalog.
    public int? DspProcessorSampleRateHz { get; set; }

    // Only how Q is stated on tuning sheets; every simulated band is an RBJ biquad.
    public PeqQConvention DspProcessorQConvention { get; set; } = PeqQConvention.Rbj;

    /// <summary>Null = the catalog answers (see <see cref="ResolveDspPhaseControl"/>). Not a view switch: false clears rotations via <see cref="ClearUnavailablePhaseRotations"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DspProcessorPhaseControl { get; set; }

    /// <summary>FIR counterpart of <see cref="DspProcessorPhaseControl"/>; false removes kernels via <see cref="ClearUnavailableFirFilters"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DspProcessorFirFilters { get; set; }

    /// <summary>Installation notes sent with every Copy for AI package. Empty is stored as absent so old files round-trip byte for byte.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AiNotes
    {
        get => aiNotes;
        set => aiNotes = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private string? aiNotes;

    /// <summary>No stated rate: takes the measurements'. Distinct from a stated rate that equals it today.</summary>
    [JsonIgnore]
    public bool DspProcessorRateFollowsMeasurements =>
        DspProcessorSampleRateHz == null &&
        DspProcessorCatalog.Preset(DspProcessorModelId) == null;

    /// <summary>A named model answers from the catalog, Custom with its stored rate, a following profile with the measurements'.</summary>
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

    /// <summary>Zeroes rotations when the processor has no phase control; returns the count. Invariant: a non-zero angle means a device that can dial one.</summary>
    public int ClearUnavailablePhaseRotations()
    {
        if (ResolveDspPhaseControl())
        {
            return 0;
        }

        int cleared = 0;
        foreach (VirtualCrossoverChannelPairSettings pair in Pairs)
        {
            foreach (VirtualCrossoverChannelSettings side in new[] { pair.Left, pair.Right })
            {
                if (side.PhaseRotationDegrees != 0)
                {
                    side.PhaseRotationDegrees = 0;
                    cleared++;
                }
            }
        }

        return cleared;
    }

    public bool ResolveDspPhaseControl() =>
        DspProcessorPhaseControl ??
        DspProcessorCatalog.Preset(DspProcessorModelId)?.PhaseControl ??
        false;

    public bool ResolveDspFirFilters() =>
        DspProcessorFirFilters ??
        DspProcessorCatalog.Preset(DspProcessorModelId)?.FirFilters ??
        false;

    /// <summary>Detaches FIR kernels when the processor has no FIR stage; returns the count.</summary>
    public int ClearUnavailableFirFilters()
    {
        if (ResolveDspFirFilters())
        {
            return 0;
        }

        int cleared = 0;
        foreach (VirtualCrossoverChannelPairSettings pair in Pairs)
        {
            foreach (VirtualCrossoverChannelSettings side in new[] { pair.Left, pair.Right })
            {
                if (side.HasFir)
                {
                    side.Fir = null;
                    side.FirSourceName = null;
                    side.FirDesign = null;
                    cleared++;
                }
            }
        }

        return cleared;
    }

    /// <summary><paramref name="followsMeasurements"/> stores the intent, which the resolved profile cannot carry.</summary>
    public void SetDspProcessor(DspProcessorProfile profile, bool followsMeasurements)
    {
        ArgumentNullException.ThrowIfNull(profile);
        DspProcessorModelId = profile.ModelId;
        DspProcessorQConvention = profile.QConvention;
        DspProcessorSampleRateHz = followsMeasurements && profile.IsCustom
            ? null
            : profile.SampleRateHz;
    }

    // Magnitude with the layout in its SIGN (negative = RHD), so pre-flag builds read and resave RHD correctly.
    // Read via StereoSceneOffsetMagnitudeMs, write via SetStereoScene. See docs/tech/virtual-dsp-session-file.md#stereo-scene.
    public double StereoSceneOffsetMs { get; set; } = 0.25;

    // false = LHD (left is the reference), true = RHD. Explicit so a zero offset keeps its layout; Migrate re-aligns sign and flag.
    public bool StereoRightHandDrive { get; set; }

    // Zero RHD offset on the wire: -0.0 does not survive, so this sub-grid marker (1/10 of the UI step) stands in and reads as zero.
    private const double RhdZeroOffsetMarkerMs = 0.001;

    /// <summary>Layout-neutral non-negative magnitude, as the UI edits it.</summary>
    [JsonIgnore]
    public double StereoSceneOffsetMagnitudeMs =>
        Math.Abs(StereoSceneOffsetMs) <= RhdZeroOffsetMarkerMs
            ? 0
            : Math.Abs(StereoSceneOffsetMs);

    /// <summary>The only writer: keeps the wire sign and the layout flag consistent.</summary>
    public void SetStereoScene(double offsetMagnitudeMs, bool rightHandDrive)
    {
        StereoRightHandDrive = rightHandDrive;
        StereoSceneOffsetMs = rightHandDrive
            ? -Math.Max(Math.Abs(offsetMagnitudeMs), RhdZeroOffsetMarkerMs)
            : Math.Abs(offsetMagnitudeMs);
    }

    // LEFT minus RIGHT; the UI edits a non-negative near-side cut and the sign here follows StereoRightHandDrive.
    public double StereoLevelDifferenceDb { get; set; } = -1.0;

    public bool ActiveSideRight { get; set; }

    // Sum visibility is per view; this is the magnitude answer, the only flag older builds know (see ShowSumCurveOnPhase).
    public bool ShowSumCurve { get; set; } = true;
    public bool? ShowSumCurvePhase { get; set; }
    // Legacy flag, still written by the selector so builds that know only the flag agree.
    public bool ShowLossCurve { get; set; }

    public SumLossWindow? LossWindow { get; set; }

    /// <summary>The stored window; for a legacy file, Full when the old flag is on (it could only be set by hand), Direct otherwise.</summary>
    [JsonIgnore]
    public SumLossWindow SumLossWindowMode
    {
        get => LossWindow ?? (ShowLossCurve ? SumLossWindow.Full : SumLossWindow.Direct);
        set
        {
            LossWindow = value;
            ShowLossCurve = value != SumLossWindow.Off;
        }
    }

    /// <summary>Files predating group views open on FrontAndSub, which is what they always drew.</summary>
    public VirtualCrossoverGroupView GroupView { get; set; } =
        VirtualCrossoverGroupView.FrontAndSub;

    /// <summary>Rear fill delay behind the front stage (ms); part of the tune, not a dialog default.</summary>
    public double RearFillOffsetMs { get; set; } =
        VirtualCrossoverAutoDelayDialog.DefaultRearFillOffsetMs;

    /// <summary>Draw the hybrid (spatial-average) magnitude. Intent: kept on load, drawn only while every playing channel has an average.</summary>
    public bool ShowHybridCurves { get; set; }

    /// <summary>Older files inherit the magnitude answer.</summary>
    [JsonIgnore]
    public bool ShowSumCurveOnPhase
    {
        get => ShowSumCurvePhase ?? ShowSumCurve;
        set => ShowSumCurvePhase = value;
    }
    public bool ShowPhaseView { get; set; }
    // Wins over ShowPhaseView; a separate flag so older builds fall back to magnitude/phase.
    public bool ShowImpulseView { get; set; }
    // Wins over ShowPhaseView, loses to ShowImpulseView; ShowPhaseView is written beside it as older builds' fallback.
    public bool ShowGroupDelayView { get; set; }
    // Wins over every older flag; ShowImpulseView is written beside it as older builds' fallback.
    public bool ShowStepView { get; set; }

    /// <summary>Older files inherit the phase answer.</summary>
    public bool? ShowSumCurveGroupDelay { get; set; }

    [JsonIgnore]
    public bool ShowSumCurveOnGroupDelay
    {
        get => ShowSumCurveGroupDelay ?? ShowSumCurveOnPhase;
        set => ShowSumCurveGroupDelay = value;
    }

    /// <summary>Older files inherit the magnitude answer (the impulse view draws no Sum).</summary>
    public bool? ShowSumCurveStep { get; set; }

    [JsonIgnore]
    public bool ShowSumCurveOnStep
    {
        get => ShowSumCurveStep ?? ShowSumCurve;
        set => ShowSumCurveStep = value;
    }

    public bool ShowTargetCurve { get; set; }
    public double TargetLevelDb { get; set; }

    /// <summary>The whole target shape this session was tuned against, applied as the app's target on load; null in older files.</summary>
    public VirtualCrossoverTargetSettings? Target { get; set; }
    public int SmoothingInverseOctaves { get; set; } = 12;

    // Separate additive flag so older builds open the session as plain smoothing instead of rejecting it.
    public bool PsychoacousticSmoothing { get; set; }

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

    // Never stores Correlation or Coherence: those are additive flags so older builds don't fail on an unknown enum.
    public DspPlotMode DspPlotMode { get; set; } = DspPlotMode.Magnitude;

    public bool DspPlotCorrelationView { get; set; }

    // Correlation wins if both flags are set.
    public bool DspPlotCoherenceView { get; set; }

    // Index into the band-ordered junction list (0 = lowest).
    public int CorrelationPairIndex { get; set; }

    /// <summary>Write through <see cref="SetDspPlotMode"/> so the multi-field representation cannot half-apply.</summary>
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

    // Calibration is the curve another machine reads; CalibrationId maps it to this machine's list and is only a hint.
    // See docs/tech/virtual-dsp-session-file.md#calibration.
    public string? CalibrationId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VirtualCrossoverCalibrationSettings? Calibration { get; set; }

    // Schema v5 payload for migration only.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyMicrophoneCalibrationMode? CalibrationMode { get; set; }

    public VirtualCrossoverPhaseGateSettings PhaseGateLeft { get; set; } = new();
    public VirtualCrossoverPhaseGateSettings PhaseGateRight { get; set; } = new();

    // Schema v4 payload (one shared gate); Migrate copies it onto both sides.
    [JsonPropertyName("phaseGateOffsetMs")]
    public double? LegacyPhaseGateOffsetMs { get; set; }
    [JsonPropertyName("phaseDetrendMs")]
    public double? LegacyPhaseDetrendMs { get; set; }

    // Lengths stay project-wide (they set resolution; sides must compare). 41 ms total reaches one period at ~24 Hz.
    // See docs/tech/virtual-dsp-session-file.md#phase-gate.
    public const double DefaultPhaseGateLeftMs = 1.0;
    public const double DefaultPhaseGatePlateauMs = 30.0;
    public const double DefaultPhaseGateRightMs = 10.0;

    public double PhaseGateLeftMs { get; set; } = DefaultPhaseGateLeftMs;
    public double PhaseGatePlateauMs { get; set; } = DefaultPhaseGatePlateauMs;
    public double PhaseGateRightMs { get; set; } = DefaultPhaseGateRightMs;

    // Analysis modes are shared by both sides; FDW-8 is the gentlest cycle count. See docs/tech/virtual-dsp-session-file.md#phase-gate.
    public const int DefaultPhaseFdwCycles = 8;

    public PhaseWindowMode PhaseWindowMode { get; set; } =
        PhaseWindowMode.FrequencyDependent;
    public int PhaseFdwCycles { get; set; } = DefaultPhaseFdwCycles;
    public PhaseDetrendMode PhaseDetrendMode { get; set; } = PhaseDetrendMode.Auto;

    public VirtualCrossoverPhaseGateSettings PhaseGateFor(bool rightSide) =>
        rightSide ? PhaseGateRight : PhaseGateLeft;

    public static string GetPath(string? rootDirectory = null) =>
        Path.Combine(
            rootDirectory ?? ApplicationDataPaths.Current.ToolsDirectory,
            FileName);

    public static string ResetBackupPath(string? rootDirectory = null) =>
        Path.Combine(
            rootDirectory ?? ApplicationDataPaths.Current.ToolsDirectory,
            ResetBackupFileName);

    /// <summary>Serializes the in-memory project aside before a Reset; returns the path or the error. See docs/tech/virtual-dsp-session-file.md#autosave-reset-backup-and-load-fallback.</summary>
    public (string? Path, string? Error) SaveResetBackup(string? rootDirectory = null)
    {
        string backupPath = ResetBackupPath(rootDirectory);
        try
        {
            SaveAutosaveTo(backupPath);
            return (backupPath, null);
        }
        catch (Exception exception)
        {
            return (null, exception.Message);
        }
    }

    public void Save(string? rootDirectory = null) =>
        SaveAutosaveTo(GetPath(rootDirectory));

    // The autosave's write, not the export: it lives in app data, so it writes no relative paths.
    private void SaveAutosaveTo(string path)
    {
        Validate();
        SavedAtUtc = DateTimeOffset.UtcNow;

        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The virtual crossover directory cannot be resolved.");
        Directory.CreateDirectory(directory);

        string temporaryPath = path + ".tmp";
        try
        {
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

    /// <summary>Export: the project format plus source paths relative to this file's folder.</summary>
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

    // Relative paths belong to the write: swapped in around serialization and restored, because the live values are
    // still needed by the relink prompt (an autosave can fire behind its modal dialog).
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

    /// <summary>Imports a session; unlike <see cref="LoadOrDefault"/> it throws on a broken or incompatible file.</summary>
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
        file.clearedPhaseRotations = file.ClearUnavailablePhaseRotations();
        file.clearedFirFilters = file.ClearUnavailableFirFilters();
        file.ProjectDirectory = SafeDirectoryOf(path);
        return file;
    }

    /// <summary>Folder of the imported session file, searched for moved measurements; null for the autosave.</summary>
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
            return null;
        }
    }

    // Newer-than-current versions are not touched: validation rejects them. See docs/tech/virtual-dsp-session-file.md#schema-versions-and-migrations.
    private static void Migrate(VirtualCrossoverProjectFile file)
    {
        if (file.Version == 1)
        {
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
            // 90° maps to the entry the settings migration created from the old second slot.
            file.CalibrationId = MeasurementSettingsFile.ResolveCalibrationId(
                file.CalibrationId,
                file.CalibrationMode,
                legacyUseCalibration: false);
            file.CalibrationMode = null;
            file.Version = 6;
        }
        if (file.Version == 6)
        {
            // Loaded sides win; where two disagree, muted/bypassed/shown survive (a lost mute is invisible to the tuner).
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
            // The band is bit-identical to the old stage; bad legacy numbers degrade to no all-pass (Migrate runs before Validate).
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
                        // Full bank: drop the last gain-bearing band (Auto Tune can re-propose it), keep the ear-aligned all-pass; reported in MigrationNoticeText.
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
        if (file.Version == 8)
        {
            // Zone guessed from the mono flag and filter; nothing else changes, so a wrong guess costs one combo box.
            foreach (VirtualCrossoverChannelPairSettings pair in file.Pairs)
            {
                pair.Zone = VirtualCrossoverZones.GuessForLegacyPair(
                    pair.Mono, pair.Left.CrossoverKind);
            }

            file.Version = 9;
        }
        if (file.Version == 9)
        {
            // Additive, but bumped so older builds refuse a rotated tune instead of drawing it without the filter.
            file.Version = 10;
        }
        if (file.Version == 10)
        {
            // Bumped for the same reason as v10, for FIR kernels.
            file.Version = 11;
        }

        // Re-align the wire sign and layout flag for files carrying only one; a negative sign wins over a missing flag.
        if (file.StereoSceneOffsetMs < 0)
        {
            file.StereoRightHandDrive = true;
        }
        else if (file.StereoRightHandDrive)
        {
            file.StereoSceneOffsetMs = -file.StereoSceneOffsetMs;
        }
    }

    /// <summary>Path of the <c>.backup</c> <see cref="LoadOrDefault"/> moved an unusable file to; null otherwise.</summary>
    [JsonIgnore]
    public string? BackupNoticePath { get; private set; }

    // Sides whose full 32-band bank lost a gain-bearing band to a migrated all-pass on THIS load.
    private int migratedFullBanks;

    // Only a hand-edited file reaches this state, but a silently dropped filter must be reported.
    private int clearedPhaseRotations;

    private int clearedFirFilters;

    /// <summary>What this load had to drop, or null when nothing was lost.</summary>
    [JsonIgnore]
    public string? MigrationNoticeText => string.Join(
        Environment.NewLine + Environment.NewLine,
        new[] { FullBankNotice, PhaseRotationNotice, FirFilterNotice }
            .Where(notice => notice != null))
        is { Length: > 0 } text
        ? text
        : null;

    private string? FirFilterNotice =>
        clearedFirFilters == 0
            ? null
            : $"{clearedFirFilters} channel side" +
                (clearedFirFilters == 1 ? " carried" : "s carried") +
                " a FIR filter, and the processor this session names has no FIR " +
                "stage. The kernel" + (clearedFirFilters == 1 ? " was" : "s were") +
                " removed from the session rather than left shaping a curve no " +
                "button on screen explains. Name a device that convolves — or tick " +
                "FIR filters yourself in the DSP processor dialog — and import them again.";

    private string? PhaseRotationNotice =>
        clearedPhaseRotations == 0
            ? null
            : $"{clearedPhaseRotations} channel side" +
                (clearedPhaseRotations == 1 ? " carried" : "s carried") +
                " a phase rotation, and the processor this session names has no such " +
                "control. The angle" + (clearedPhaseRotations == 1 ? " was" : "s were") +
                " cleared rather than left bending a curve no field on screen " +
                "explains. Name a device that has the control — or tick it yourself " +
                "for a Custom profile — and dial them in again.";

    private string? FullBankNotice =>
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

    /// <summary>Loads the autosave, falling back to a fresh default; an unusable file is moved to <c>.backup</c> first so the next save cannot overwrite it.</summary>
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
            file.clearedPhaseRotations = file.ClearUnavailablePhaseRotations();
            file.clearedFirFilters = file.ClearUnavailableFirFilters();
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
        if (!Enum.IsDefined(GroupView))
        {
            throw new InvalidDataException(
                "The virtual crossover group view is invalid.");
        }
        if (!double.IsFinite(RearFillOffsetMs) || RearFillOffsetMs is < 0 or > 30)
        {
            throw new InvalidDataException(
                "The virtual crossover rear fill offset is invalid.");
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
            PhaseFdwCycles = DefaultPhaseFdwCycles;
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
