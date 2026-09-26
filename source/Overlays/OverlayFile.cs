using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Dsp;

namespace Resonalyze;

public sealed class OverlayFile
{
    public const string CurrentFormat = "resonalyze-overlay";
    public const int CurrentVersion = 5;
    public const int MaximumSlotCount = 12;

    /// <summary>Hz; the tilt adds 0 dB here.</summary>
    public const double DefaultTiltPivotHz = 1_000;

    public const double DefaultTiltDbPerOctave = 6;

    /// <summary>dB per octave, either sign.</summary>
    public const double MaximumTiltDbPerOctave = 24;

    // Every mode switch re-reads all 12 slot files; cached by write stamp. Loaded instances are never mutated.
    private static readonly ConcurrentDictionary<
        string,
        (DateTime WriteTimeUtc, long Length, OverlayFile File)> LoadCache =
        new(StringComparer.OrdinalIgnoreCase);

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
    public Mode Mode { get; set; }
    public int Slot { get; set; }

    public OverlayKind Kind { get; set; } = OverlayKind.Captured;

    public string Title { get; set; } = string.Empty;

    public double Offset { get; set; }
    public int ColorArgb { get; set; }
    public double StrokeThickness { get; set; } = 2;
    public OverlayLineStyle LineStyle { get; set; } = OverlayLineStyle.Solid;
    public int OpacityPercent { get; set; } = 100;
    public int SmoothingInverseOctaves { get; set; }

    // Separate flag (width kept in SmoothingInverseOctaves) so older builds read it as plain 1/6 octave.
    public bool PsychoacousticSmoothing { get; set; }

    /// <summary>Read/write only through this and <see cref="SetSmoothingCode"/> so the dual representation cannot half-apply.</summary>
    [JsonIgnore]
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

    public OverlayPoint[] Points { get; set; } = Array.Empty<OverlayPoint>();

    public string? CapturedYAxisKey { get; set; }

    // Gates which magnitude axis shows the slot. New properties below are additive: no file version bump.
    public Resonalyze.Dsp.MagnitudeScale CapturedMagnitudeScale { get; set; } =
        Resonalyze.Dsp.MagnitudeScale.Relative;

    // Phase only: true unwrapped, false wrapped (-180..180), null unknown.
    public bool? PhaseUnwrapped { get; set; }

    public Resonalyze.Dsp.AnalysisCurveKind? CapturedCurveKind { get; set; }

    // Captured FR only: oversampled raw spectrum, re-smoothed with the mode's LogarithmicResample for exact reproduction.
    public OverlayPoint[] RawSpectrum { get; set; } = Array.Empty<OverlayPoint>();

    // Impulse only: absolute sample indices and raw linear values, so the slot re-draws under the current
    // time unit, origin, amplitude scale and polarity. Legacy files keep the frozen Points.
    public OverlayPoint[] RawImpulse { get; set; } = Array.Empty<OverlayPoint>();

    // False in files whose RawImpulse runs 0..N-1 from record start; loading moves their second half before zero.
    public bool RawImpulseSignedLags { get; set; }

    // Record peak at capture, used only when no live measurement is available to normalize against.
    public double? RawImpulsePeakReference { get; set; }

    // Calibration on the 1024 log output frequencies, subtracted after smoothing like the primary FR path.
    public double[] RawCalibrationCorrectionDb { get; set; } = Array.Empty<double>();

    /// <summary>Band measured behind <see cref="RawSpectrum"/>; 0/0 = everywhere. Re-applied after every re-smoothing.</summary>
    public double MeasuredLowFrequencyHz { get; set; }

    /// <inheritdoc cref="MeasuredLowFrequencyHz"/>
    public double MeasuredHighFrequencyHz { get; set; }

    // No-raw (dB SPL) captures only: correction baked into Points, per point, so a consumer can swap calibration exactly.
    public double[] PointsCalibrationCorrectionDb { get; set; } = Array.Empty<double>();

    // Smoothing baked into Points (SpectrumSmoothing encoding); a consumer may re-smooth only when 0. Null = unknown.
    public int? CapturedSmoothingCode { get; set; }

    public int? SampleRateHz { get; set; }

    // An operand is a live curve (CurveTag Key) when SourceCurveKeyA/B is set, otherwise a slot.
    public int SourceSlotA { get; set; }
    public int SourceSlotB { get; set; }
    public string? SourceCurveKeyA { get; set; }
    public string? SourceCurveKeyB { get; set; }
    public OverlayOperation Operation { get; set; } = OverlayOperation.AMinusB;
    public double BlendFrequencyHz { get; set; } = 1_000;
    public double BlendWidthOctaves { get; set; } = 1;
    public bool UseAmplitudeSpace { get; set; }

    // Compensates a sloped excitation (pink noise falls 3 dB/octave on a constant-bandwidth analyzer).
    public bool TiltEnabled { get; set; }
    public double TiltDbPerOctave { get; set; } = DefaultTiltDbPerOctave;
    public double TiltPivotHz { get; set; } = DefaultTiltPivotHz;

    // Applied to the Compare transfer response before the sum, mirroring a DSP channel.
    public double CompareDelayMs { get; set; }
    public bool CompareInvertPolarity { get; set; }

    // TargetSourceSlot 0 = current measurement; 1..12 = a captured slot.
    public int TargetSourceSlot { get; set; }
    public TargetPreset TargetPreset { get; set; } = TargetPreset.HarmanRoom;
    public double TargetTiltDbPerOctave { get; set; }
    public double TargetBassShelfGainDb { get; set; }
    public double TargetBassShelfFrequencyHz { get; set; } = 100;
    public double TargetBassShelfWidthOctaves { get; set; } = 1.5;
    public double TargetTrebleShelfGainDb { get; set; }
    public double TargetTrebleShelfFrequencyHz { get; set; } = 5_000;
    public double TargetTrebleShelfWidthOctaves { get; set; } = 1.5;
    public double TargetPresenceGainDb { get; set; }
    public double TargetPresenceFrequencyHz { get; set; } = 3_000;
    public double TargetPresenceWidthOctaves { get; set; } = 1.0;
    public double TargetToleranceDb { get; set; }
    public TargetDeviationMode TargetDeviationMode { get; set; } =
        TargetDeviationMode.Deviation;

    public static string GetPath(Mode mode, int slot, string? rootDirectory = null)
    {
        ValidateLocation(mode, slot);
        string root = rootDirectory
            ?? ApplicationDataPaths.Current.OverlaysDirectory;
        return Path.Combine(root, mode.ToString(), $"overlay-{slot:00}.json");
    }

    public void Save(string? rootDirectory = null)
    {
        Validate();

        string path = GetPath(Mode, Slot, rootDirectory);
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Overlay directory cannot be resolved.");
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
            // Invalidate rather than store `this`: only Load-created instances are safe from mutation.
            LoadCache.TryRemove(path, out _);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static OverlayFile? Load(
        Mode mode,
        int slot,
        string? rootDirectory = null)
    {
        string path = GetPath(mode, slot, rootDirectory);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            LoadCache.TryRemove(path, out _);
            return null;
        }

        if (LoadCache.TryGetValue(path, out var cached) &&
            cached.WriteTimeUtc == info.LastWriteTimeUtc &&
            cached.Length == info.Length)
        {
            return cached.File;
        }

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        OverlayFile file = JsonSerializer.Deserialize<OverlayFile>(
            stream,
            SerializerOptions)
            ?? throw new InvalidDataException("The overlay file is empty.");
        file.Validate();

        if (file.Mode != mode || file.Slot != slot)
        {
            throw new InvalidDataException(
                "The overlay file does not match its mode and slot.");
        }

        LoadCache[path] = (info.LastWriteTimeUtc, info.Length, file);
        return file;
    }

    /// <summary>Renames a failed slot file to "&lt;name&gt;.corrupt"; null when there is no file.</summary>
    public static string? QuarantineCorruptFile(
        Mode mode,
        int slot,
        string? rootDirectory = null)
    {
        string path = GetPath(mode, slot, rootDirectory);
        if (!File.Exists(path))
        {
            return null;
        }

        string quarantinePath = path + ".corrupt";
        File.Move(path, quarantinePath, overwrite: true);
        LoadCache.TryRemove(path, out _);
        return quarantinePath;
    }

    public static void Delete(
        Mode mode,
        int slot,
        string? rootDirectory = null)
    {
        string path = GetPath(mode, slot, rootDirectory);
        LoadCache.TryRemove(path, out _);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void ValidateLocation(Mode mode, int slot)
    {
        if (mode == Mode.None || !Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (slot is < 1 or > MaximumSlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    private void Validate()
    {
        if (!string.Equals(Format, CurrentFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported overlay format '{Format}'.");
        }
        if (Version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported overlay version {Version}.");
        }

        ValidateLocation(Mode, Slot);

        if (!Enum.IsDefined(Kind))
        {
            throw new InvalidDataException("The overlay kind is invalid.");
        }
        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new InvalidDataException("The overlay title is missing.");
        }
        if (!double.IsFinite(Offset))
        {
            throw new InvalidDataException("The overlay offset is invalid.");
        }
        if (!double.IsFinite(StrokeThickness) ||
            StrokeThickness is < 0.5 or > 10)
        {
            throw new InvalidDataException("The overlay line thickness is invalid.");
        }
        if (!Enum.IsDefined(LineStyle))
        {
            throw new InvalidDataException("The overlay line style is invalid.");
        }
        if (OpacityPercent is < 10 or > 100)
        {
            throw new InvalidDataException("The overlay opacity is invalid.");
        }
        if (!OverlaySmoothing.IsValid(SmoothingInverseOctaves) ||
            (!OverlaySmoothing.SupportsMode(Mode) &&
             SmoothingInverseOctaves != 0))
        {
            throw new InvalidDataException(
                "The overlay smoothing setting is invalid.");
        }

        switch (Kind)
        {
            case OverlayKind.Captured:
                ValidateCaptured();
                break;
            case OverlayKind.Operation:
                ValidateOperation();
                break;
            case OverlayKind.Target:
                ValidateTarget();
                break;
            default:
                throw new InvalidDataException("The overlay kind is invalid.");
        }
    }

    private void ValidateTarget()
    {
        if (!OverlayTargets.SupportsMode(Mode))
        {
            throw new InvalidDataException(
                "Target overlays are only supported in frequency-based modes.");
        }
        if (TargetSourceSlot is < 0 or > MaximumSlotCount)
        {
            throw new InvalidDataException("The target source slot is invalid.");
        }
        if (!Enum.IsDefined(TargetPreset))
        {
            throw new InvalidDataException("The target preset is invalid.");
        }
        if (!double.IsFinite(TargetTiltDbPerOctave) ||
            !double.IsFinite(TargetBassShelfGainDb) ||
            !(TargetBassShelfFrequencyHz > 0) ||
            !(TargetBassShelfWidthOctaves > 0) ||
            !double.IsFinite(TargetTrebleShelfGainDb) ||
            !(TargetTrebleShelfFrequencyHz > 0) ||
            !(TargetTrebleShelfWidthOctaves > 0) ||
            !double.IsFinite(TargetPresenceGainDb) ||
            !(TargetPresenceFrequencyHz > 0) ||
            !(TargetPresenceWidthOctaves > 0))
        {
            throw new InvalidDataException("The target curve parameters are invalid.");
        }
        if (!double.IsFinite(TargetToleranceDb) || TargetToleranceDb < 0)
        {
            throw new InvalidDataException("The target tolerance is invalid.");
        }
        if (!Enum.IsDefined(TargetDeviationMode))
        {
            throw new InvalidDataException("The target deviation mode is invalid.");
        }
    }

    private void ValidateCaptured()
    {
        if (Points == null || Points.Length < 2)
        {
            throw new InvalidDataException("The overlay must contain at least two points.");
        }

        for (int i = 0; i < Points.Length; i++)
        {
            if (!double.IsFinite(Points[i].X) || double.IsInfinity(Points[i].Y))
            {
                throw new InvalidDataException(
                    $"Overlay point {i} contains a non-finite value.");
            }
        }

        if (RawSpectrum == null || RawCalibrationCorrectionDb == null)
        {
            throw new InvalidDataException("The raw overlay data is invalid.");
        }
        if (RawCalibrationCorrectionDb.Length != 0 &&
            (RawSpectrum.Length < 2 ||
             RawCalibrationCorrectionDb.Length != RawCurveRenderer.PointCount))
        {
            throw new InvalidDataException(
                "The raw calibration correction does not match the overlay grid.");
        }
        if (RawCalibrationCorrectionDb.Any(value => !double.IsFinite(value)))
        {
            throw new InvalidDataException(
                "The raw calibration correction contains a non-finite value.");
        }
        if (PointsCalibrationCorrectionDb == null)
        {
            throw new InvalidDataException("The points calibration correction is invalid.");
        }
        // A mismatched length would silently shift the correction in frequency.
        if (PointsCalibrationCorrectionDb.Length != 0 &&
            PointsCalibrationCorrectionDb.Length != Points.Length)
        {
            throw new InvalidDataException(
                "The points calibration correction does not match the overlay points.");
        }
        if (PointsCalibrationCorrectionDb.Any(value => !double.IsFinite(value)))
        {
            throw new InvalidDataException(
                "The points calibration correction contains a non-finite value.");
        }
        if (SampleRateHz is <= 0)
        {
            throw new InvalidDataException("The captured sample rate is invalid.");
        }
    }

    private void ValidateOperation()
    {
        if (Operation is OverlayOperation.ComplexSum or OverlayOperation.ComplexSumLoss)
        {
            if (Mode != Mode.FrequencyResponse)
            {
                throw new InvalidDataException(
                    "The complex-sum overlay is only supported in Frequency Response.");
            }
            if (!double.IsFinite(CompareDelayMs) || Math.Abs(CompareDelayMs) > 1_000)
            {
                throw new InvalidDataException(
                    "The complex-sum Compare delay is invalid.");
            }
        }
        else
        {
            // Operands must differ, unless the operation reads A alone.
            bool aIsCurve = !string.IsNullOrEmpty(SourceCurveKeyA);
            bool bIsCurve = !string.IsNullOrEmpty(SourceCurveKeyB);
            bool usesB = Operation != OverlayOperation.CurveA;
            if ((!aIsCurve && SourceSlotA is < 1 or > MaximumSlotCount) ||
                (usesB && !bIsCurve && SourceSlotB is < 1 or > MaximumSlotCount))
            {
                throw new InvalidDataException(
                    "Calculated overlay source slots are invalid.");
            }
            bool sameOperand = aIsCurve || bIsCurve
                ? aIsCurve && bIsCurve && SourceCurveKeyA == SourceCurveKeyB
                : SourceSlotA == SourceSlotB;
            if (usesB && sameOperand)
            {
                throw new InvalidDataException(
                    "Calculated overlay operands must differ.");
            }
        }

        if (!Enum.IsDefined(Operation))
        {
            throw new InvalidDataException(
                "The calculated overlay operation is invalid.");
        }
        if (Operation == OverlayOperation.Blend)
        {
            if (!double.IsFinite(BlendFrequencyHz) || BlendFrequencyHz <= 0)
            {
                throw new InvalidDataException(
                    "The blend crossover frequency is invalid.");
            }
            if (!double.IsFinite(BlendWidthOctaves) || BlendWidthOctaves <= 0)
            {
                throw new InvalidDataException(
                    "The blend transition width is invalid.");
            }
        }
        if (UseAmplitudeSpace && !OverlayMath.SupportsAmplitudeSpace(Mode))
        {
            throw new InvalidDataException(
                "Amplitude-space overlay math is not supported in this mode.");
        }
        if (TiltEnabled)
        {
            if (!OverlayMath.SupportsAmplitudeSpace(Mode))
            {
                throw new InvalidDataException(
                    "The overlay tilt is not supported in this mode.");
            }
            if (!double.IsFinite(TiltDbPerOctave) ||
                Math.Abs(TiltDbPerOctave) > MaximumTiltDbPerOctave)
            {
                throw new InvalidDataException("The overlay tilt slope is invalid.");
            }
            if (!double.IsFinite(TiltPivotHz) || TiltPivotHz <= 0)
            {
                throw new InvalidDataException("The overlay tilt pivot frequency is invalid.");
            }
        }
    }
}

public enum OverlayKind
{
    Captured,
    Operation,
    Target
}

public readonly record struct OverlayPoint(double X, double Y);

public enum OverlayLineStyle
{
    Solid,
    Dash,
    Dot,
    DashDot
}

public enum OverlayOperation
{
    /// <summary>A alone, so the slot's smoothing, offset and tilt apply to a single curve.</summary>
    CurveA,

    AMinusB,
    BMinusA,
    Sum,
    Average,
    AbsoluteDifference,
    Blend,

    /// <summary>FFT(h1 + h2) from the Main/Compare transfer IRs: keeps delay, polarity and phase. FR only.</summary>
    ComplexSum,

    /// <summary>|H1| + |H2| over |H1 + H2| in dB (&gt;= 0): what phase-blind addition overestimates. FR only.</summary>
    ComplexSumLoss
}

public enum TargetDeviationMode
{
    Deviation,

    /// <summary>target − measurement: the EQ gain needed.</summary>
    Correction,

    None
}

public enum TargetPreset
{
    Flat,

    /// <summary>Shown as "Room (Harman-style)", not a published curve; name kept because presets persist by name.</summary>
    HarmanRoom,
    RoomGentle,
    Warm,
    Car,
    CarMild,
    CarBass,
    House,
    XCurve,
    Smiley,
    BbcDip,
    Custom
}

/// <summary>Parametric relative-dB target (tilt at 1 kHz, bass/treble shelves, presence), or <see cref="Imported"/>.</summary>
public sealed record TargetCurveSpec(
    double TiltDbPerOctave,
    double BassShelfGainDb,
    double BassShelfFrequencyHz,
    double BassShelfWidthOctaves,
    double TrebleShelfGainDb,
    double TrebleShelfFrequencyHz,
    double TrebleShelfWidthOctaves,
    double PresenceGainDb,
    double PresenceFrequencyHz,
    double PresenceWidthOctaves)
{
    public const double PivotHz = 1_000.0;

    /// <summary>Replaces the parametric terms while set; lives in the spec so every <see cref="Evaluate"/> caller sees one shape.</summary>
    public ImportedTargetCurve? Imported { get; init; }

    public static TargetCurveSpec FromPreset(TargetPreset preset) => preset switch
    {
        //                          tilt  bass(g,f,w)   treble(g,f,w)    presence(g,f,w)
        TargetPreset.Flat => new(0, 0, 100, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.HarmanRoom => new(-0.8, 4, 105, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.RoomGentle => new(-0.5, 2, 120, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.Warm => new(-1.0, 3, 110, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0),
        // Car: bass shelf on a flat 630 Hz–5 kHz band, ≈3 dB down by 20 kHz (OverlayTargetTests.CarTargetTable, within 0.2 dB).
        // Variants move only the shelf gain.
        TargetPreset.Car => new(0, 9.2, 150, 0.9, -3, 10_000, 0.7, 0, 3_000, 1.0),
        TargetPreset.CarMild => new(0, 6, 150, 0.9, -3, 10_000, 0.7, 0, 3_000, 1.0),
        TargetPreset.CarBass => new(0, 12, 150, 0.9, -3, 10_000, 0.7, 0, 3_000, 1.0),
        TargetPreset.House => new(0, 6, 120, 1.0, 0, 5_000, 1.5, 0, 3_000, 1.0),
        // ISO 2969 / SMPTE ST 202: flat to 2 kHz, then -3 dB/oct; within 0.6 dB (OverlayTargetTests.XCurveTable).
        TargetPreset.XCurve => new(0, 0, 100, 1.5, -10, 6_300, 1.2, 0, 3_000, 1.0),
        TargetPreset.Smiley => new(0, 6, 100, 1.0, 5, 4_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.BbcDip => new(-0.5, 0, 100, 1.5, 0, 5_000, 1.5, -3, 2_800, 1.0),
        _ => new TargetCurveSpec(-0.5, 0, 100, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0)
    };

    public double Evaluate(double frequencyHz)
    {
        if (!(frequencyHz > 0))
        {
            return 0;
        }

        if (Imported != null)
        {
            return Imported.Evaluate(frequencyHz);
        }

        double value = TiltDbPerOctave * Math.Log2(frequencyHz / PivotHz);

        if (BassShelfGainDb != 0 &&
            BassShelfFrequencyHz > 0 &&
            BassShelfWidthOctaves > 0)
        {
            double x = Math.Log2(frequencyHz / BassShelfFrequencyHz) /
                BassShelfWidthOctaves;
            value += BassShelfGainDb * 0.5 * (1 - Math.Tanh(x));
        }

        if (TrebleShelfGainDb != 0 &&
            TrebleShelfFrequencyHz > 0 &&
            TrebleShelfWidthOctaves > 0)
        {
            double x = Math.Log2(frequencyHz / TrebleShelfFrequencyHz) /
                TrebleShelfWidthOctaves;
            value += TrebleShelfGainDb * 0.5 * (1 + Math.Tanh(x));
        }

        // Presence: log-Gaussian bump centered on its frequency.
        if (PresenceGainDb != 0 &&
            PresenceFrequencyHz > 0 &&
            PresenceWidthOctaves > 0)
        {
            double x = Math.Log2(frequencyHz / PresenceFrequencyHz) /
                PresenceWidthOctaves;
            value += PresenceGainDb * Math.Exp(-0.5 * x * x);
        }

        return value;
    }
}

public sealed record TargetCurveResult(
    OverlayPoint[] Target,
    OverlayPoint[] Deviation,
    OverlayPoint[] ToleranceUpper,
    OverlayPoint[] ToleranceLower);

public static class OverlayTargets
{
    public const TargetPreset DefaultPreset = TargetPreset.Car;

    /// <summary>Targets persist parameters, so one whose preset numbers changed since reports as <see cref="TargetPreset.Custom"/>.</summary>
    public static TargetPreset ResolvePreset(TargetPreset preset, TargetCurveSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return preset == TargetPreset.Custom ||
               spec == TargetCurveSpec.FromPreset(preset)
            ? preset
            : TargetPreset.Custom;
    }

    /// <summary>Only dB-over-frequency modes; takes the canonical overlay mode (OverlayModes.SlotModeFor).</summary>
    public static bool SupportsMode(Mode mode)
    {
        return mode is Mode.FrequencyResponse or Mode.LiveSpectrum;
    }
}

public static class OverlaySmoothing
{
    public static IReadOnlyList<int> SupportedInverseOctaves { get; } =
        [0, 48, 24, 12, 6, SpectrumSmoothing.PsychoacousticCode, 3, 2, 1];

    public static bool SupportsMode(Mode mode)
    {
        return mode is
            Mode.FrequencyResponse or
            Mode.PhaseResponse or
            Mode.GroupDelay or
            Mode.LiveSpectrum;
    }

    public static bool IsValid(int inverseOctaves) =>
        SupportedInverseOctaves.Contains(inverseOctaves);

    public static string GetLabel(int inverseOctaves) =>
        inverseOctaves == 0
            ? "Off"
            : SpectrumSmoothing.IsPsychoacoustic(inverseOctaves)
                ? "Psychoacoustic"
                : $"1/{inverseOctaves} octave";
}
