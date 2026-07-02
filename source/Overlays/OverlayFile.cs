using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze;

/// <summary>
/// Stores one overlay slot independently from OxyPlot and Windows Forms.
/// </summary>
public sealed class OverlayFile
{
    public const string CurrentFormat = "resonalyze-overlay";
    public const int CurrentVersion = 5;
    public const int MaximumSlotCount = 12;

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

    /// <summary>Selects how the slot produces its curve(s).</summary>
    public OverlayKind Kind { get; set; } = OverlayKind.Captured;

    public string Title { get; set; } = string.Empty;

    // Presentation (all kinds).
    public double Offset { get; set; }
    public int ColorArgb { get; set; }
    public double StrokeThickness { get; set; } = 2;
    public OverlayLineStyle LineStyle { get; set; } = OverlayLineStyle.Solid;
    public int OpacityPercent { get; set; } = 100;
    public int SmoothingInverseOctaves { get; set; }

    // Captured kind: the stored curve samples.
    public OverlayPoint[] Points { get; set; } = Array.Empty<OverlayPoint>();

    // Captured phase curves only: true if the samples are an unwrapped (continuous)
    // representation, false if wrapped (-180..180), null if unknown (e.g. imported text
    // or a non-phase mode). Additive and nullable, so it needs no file version bump:
    // older files deserialize to null and older app builds ignore the unknown property.
    public bool? PhaseUnwrapped { get; set; }

    // Operation kind: recipe referencing two operands. Each operand is a captured slot
    // (SourceSlotA/B), unless SourceCurveKeyA/B is set — then it is a live analysis
    // curve resolved by its CurveTag Key on every rebuild. Additive and nullable, so no
    // file version bump is needed (older files load the key as null = slot operand).
    public int SourceSlotA { get; set; }
    public int SourceSlotB { get; set; }
    public string? SourceCurveKeyA { get; set; }
    public string? SourceCurveKeyB { get; set; }
    public OverlayOperation Operation { get; set; } = OverlayOperation.AMinusB;
    public double BlendFrequencyHz { get; set; } = 1_000;
    public double BlendWidthOctaves { get; set; } = 1;
    public bool UseAmplitudeSpace { get; set; }

    // ComplexSum only: extra delay (ms) and a polarity flip applied to the Compare
    // transfer response before the sum, mirroring a DSP channel setup. Additive
    // with safe defaults, so no file version bump is needed.
    public double CompareDelayMs { get; set; }
    public bool CompareInvertPolarity { get; set; }

    // Target kind: compares a source against a parametric target curve.
    // TargetSourceSlot 0 means the current measurement; 1..12 a captured slot.
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
            ?? Path.Combine(AppContext.BaseDirectory, "overlays");
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
        if (!File.Exists(path))
        {
            return null;
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

        return file;
    }

    public static void Delete(
        Mode mode,
        int slot,
        string? rootDirectory = null)
    {
        string path = GetPath(mode, slot, rootDirectory);
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
        if (Mode is not (Mode.FrequencyResponse or Mode.LiveSpectrum))
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
    }

    private void ValidateOperation()
    {
        if (Operation == OverlayOperation.ComplexSum)
        {
            // Complex sum reads the Main and Compare transfer IRs directly; it has
            // no operands and only draws on the frequency-response axes.
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
            // An operand is a live curve when its CurveKey is set; otherwise a captured
            // slot whose index must be in range. The two operands must not be identical.
            bool aIsCurve = !string.IsNullOrEmpty(SourceCurveKeyA);
            bool bIsCurve = !string.IsNullOrEmpty(SourceCurveKeyB);
            if ((!aIsCurve && SourceSlotA is < 1 or > MaximumSlotCount) ||
                (!bIsCurve && SourceSlotB is < 1 or > MaximumSlotCount))
            {
                throw new InvalidDataException(
                    "Calculated overlay source slots are invalid.");
            }
            bool sameOperand = aIsCurve || bIsCurve
                ? aIsCurve && bIsCurve && SourceCurveKeyA == SourceCurveKeyB
                : SourceSlotA == SourceSlotB;
            if (sameOperand)
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
    AMinusB,
    BMinusA,
    Sum,
    Average,
    AbsoluteDifference,
    Blend,

    /// <summary>
    /// The complex (vector) sum of the Main and Compare transfer responses,
    /// FFT(h1 + h2). Takes no operands — it reads the two transfer impulse
    /// responses directly, so it captures relative delay, polarity, and phase
    /// (the physically correct summed output of two drivers), which arithmetic
    /// on dB curves cannot. Frequency Response only.
    /// </summary>
    ComplexSum
}

public enum TargetDeviationMode
{
    /// <summary>measurement − target (how far the response is from the target).</summary>
    Deviation,

    /// <summary>target − measurement (the EQ gain needed to reach the target).</summary>
    Correction,

    /// <summary>Do not draw a deviation curve.</summary>
    None
}

public enum TargetPreset
{
    Flat,
    HarmanRoom,
    RoomGentle,
    Warm,
    Car,
    CarMild,
    House,
    XCurve,
    Smiley,
    BbcDip,
    Custom
}

/// <summary>
/// A parametric target response shape (relative dB) built from an overall tilt
/// around a 1 kHz pivot, a low-frequency shelf, a high-frequency shelf, and a
/// presence bump. Presets are just parameter sets the user can edit, and the
/// four terms cover room, car, home-theater (X-curve), and voicing targets.
/// </summary>
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

    public static TargetCurveSpec FromPreset(TargetPreset preset) => preset switch
    {
        //                          tilt  bass(g,f,w)   treble(g,f,w)    presence(g,f,w)
        TargetPreset.Flat => new(0, 0, 100, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.HarmanRoom => new(-0.8, 4, 105, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.RoomGentle => new(-0.5, 2, 120, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.Warm => new(-1.0, 3, 110, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.Car => new(-1.0, 8, 80, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.CarMild => new(-0.8, 6, 75, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.House => new(0, 6, 120, 1.0, 0, 5_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.XCurve => new(0, 0, 100, 1.5, -10, 2_500, 2.0, 0, 3_000, 1.0),
        TargetPreset.Smiley => new(0, 6, 100, 1.0, 5, 4_000, 1.5, 0, 3_000, 1.0),
        TargetPreset.BbcDip => new(-0.5, 0, 100, 1.5, 0, 5_000, 1.5, -3, 2_800, 1.0),
        _ => new TargetCurveSpec(-0.5, 0, 100, 1.5, 0, 5_000, 1.5, 0, 3_000, 1.0)
    };

    /// <summary>Relative target level (dB) at the given frequency.</summary>
    public double Evaluate(double frequencyHz)
    {
        if (!(frequencyHz > 0))
        {
            return 0;
        }

        double value = TiltDbPerOctave * Math.Log2(frequencyHz / PivotHz);

        // Low shelf: → gain well below the corner, → 0 well above it.
        if (BassShelfGainDb != 0 &&
            BassShelfFrequencyHz > 0 &&
            BassShelfWidthOctaves > 0)
        {
            double x = Math.Log2(frequencyHz / BassShelfFrequencyHz) /
                BassShelfWidthOctaves;
            value += BassShelfGainDb * 0.5 * (1 - Math.Tanh(x));
        }

        // High shelf: → gain well above the corner, → 0 well below it.
        if (TrebleShelfGainDb != 0 &&
            TrebleShelfFrequencyHz > 0 &&
            TrebleShelfWidthOctaves > 0)
        {
            double x = Math.Log2(frequencyHz / TrebleShelfFrequencyHz) /
                TrebleShelfWidthOctaves;
            value += TrebleShelfGainDb * 0.5 * (1 + Math.Tanh(x));
        }

        // Presence: a log-Gaussian bump (or dip) centered on its frequency.
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

public static class OverlaySmoothing
{
    public static IReadOnlyList<int> SupportedInverseOctaves { get; } =
        [0, 48, 24, 12, 6, 3, 2, 1];

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
        inverseOctaves == 0 ? "Off" : $"1/{inverseOctaves} octave";
}
