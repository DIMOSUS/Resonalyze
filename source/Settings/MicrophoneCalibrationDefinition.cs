using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What an additional calibration entry is made of.</summary>
internal enum MicrophoneCalibrationKind
{
    /// <summary>A calibration file of its own, picked by the user.</summary>
    File,

    /// <summary>
    /// A curve ESTIMATED from another calibration for an angle of incidence.
    /// Stored as the recipe rather than as points, so editing the microphone's
    /// 0° file updates every angle derived from it.
    /// </summary>
    Angle
}

/// <summary>
/// One entry of the additional-calibration list: a name the user chose plus
/// either a file or the recipe for an angular estimate. <see cref="Id"/> is what
/// every view persists, so it must survive a rename.
/// </summary>
internal sealed class MicrophoneCalibrationDefinition
{
    public const double DefaultFrontDiameterMm = 12.7;
    public const double MinFrontDiameterMm = 1.0;
    public const double MaxFrontDiameterMm = 60.0;

    /// <summary>
    /// The id the pre-list 90° slot migrates to. Fixed rather than generated so
    /// a Virtual DSP project written before the migration resolves to the same
    /// entry the settings file created.
    /// </summary>
    public const string LegacyNinetyDegreesId = "90deg";

    private const string GeneratedIdPrefix = "cal";

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public MicrophoneCalibrationKind Kind { get; set; }

    /// <summary>The calibration file, for <see cref="MicrophoneCalibrationKind.File"/>.</summary>
    public string? Path { get; set; }

    /// <summary>
    /// Which calibration an angular estimate is derived from: another file entry
    /// by id, or the microphone's own 0° calibration when null. Only file-backed
    /// entries are allowed here, so an estimate can never be derived from
    /// another estimate.
    /// </summary>
    public string? BaseId { get; set; }

    public double AngleDegrees { get; set; }

    public double FrontDiameterMm { get; set; } = DefaultFrontDiameterMm;

    public MicrophoneProtectionGrid Grid { get; set; }

    public MicrophoneAngleReference Reference { get; set; }

    public MicrophoneCalibrationDefinition Clone() =>
        (MicrophoneCalibrationDefinition)MemberwiseClone();

    public MicrophoneAngleRequest ToAngleRequest() =>
        new(AngleDegrees, FrontDiameterMm, Grid, Reference);

    /// <summary>
    /// Clamps a definition into the range the model accepts and gives it a name
    /// if the user left one out. Runs on load, so a hand-edited or corrupted
    /// settings file cannot push an angle or a diameter into the model.
    /// </summary>
    public void Normalize()
    {
        Id = Id.Trim();
        Name = Name.Trim();
        if (!Enum.IsDefined(Kind))
        {
            Kind = MicrophoneCalibrationKind.File;
        }

        Path = string.IsNullOrWhiteSpace(Path) ? null : Path.Trim();
        BaseId = string.IsNullOrWhiteSpace(BaseId) || BaseId.Trim() == Id
            ? null
            : BaseId.Trim();
        AngleDegrees = double.IsFinite(AngleDegrees)
            ? Math.Clamp(AngleDegrees, 0.0, 90.0)
            : 0.0;
        // A stored zero (or worse) is an absent value, not a 1 mm microphone:
        // clamping it into range would quietly produce a front too small to
        // diffract and an estimate that corrects nothing.
        FrontDiameterMm = double.IsFinite(FrontDiameterMm) && FrontDiameterMm > 0
            ? Math.Clamp(FrontDiameterMm, MinFrontDiameterMm, MaxFrontDiameterMm)
            : DefaultFrontDiameterMm;
        if (!Enum.IsDefined(Grid))
        {
            Grid = MicrophoneProtectionGrid.Unknown;
        }

        if (!Enum.IsDefined(Reference))
        {
            Reference = MicrophoneAngleReference.GrasGeometry;
        }

        if (Name.Length == 0)
        {
            Name = Kind == MicrophoneCalibrationKind.Angle
                ? FormatAngleName(AngleDegrees)
                : Path is { } path
                    ? System.IO.Path.GetFileNameWithoutExtension(path)
                    : "Calibration";
        }
    }

    public static string FormatAngleName(double angleDegrees) =>
        $"{angleDegrees:0.#}°";

    /// <summary>
    /// A short id that no existing entry uses and that cannot collide with the
    /// reserved ones (the 0° slot and the migrated 90° entry).
    /// </summary>
    public static string CreateId(IEnumerable<MicrophoneCalibrationDefinition> existing)
    {
        var taken = new HashSet<string>(
            existing.Select(definition => definition.Id),
            StringComparer.OrdinalIgnoreCase)
        {
            MicrophoneCalibrationIds.ZeroDegrees,
            LegacyNinetyDegreesId
        };
        for (int index = 1; ; index++)
        {
            string candidate = $"{GeneratedIdPrefix}{index}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }
}

/// <summary>
/// One selectable calibration as the mode panels see it: the id they persist,
/// the name they show, and whether it currently resolves to a usable curve.
/// </summary>
/// <param name="Available">
/// False when the file is missing or unparsable, or when an angular estimate has
/// no base curve. The entry stays selectable regardless — dropping it would move
/// the selection to Off and the next save would erase the user's choice.
/// </param>
internal sealed record MicrophoneCalibrationEntry(
    string Id,
    string Name,
    bool Available);
