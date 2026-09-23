using Resonalyze.Dsp;

namespace Resonalyze;

internal enum MicrophoneCalibrationKind
{
    File,

    /// <summary>Stored as a recipe so editing the 0° file updates every derived angle.</summary>
    Angle
}

/// <summary><see cref="Id"/> is what views persist, so it must survive a rename.</summary>
internal sealed class MicrophoneCalibrationDefinition
{
    public const double DefaultFrontDiameterMm = 12.7;
    public const double MinFrontDiameterMm = 1.0;
    public const double MaxFrontDiameterMm = 60.0;

    /// <summary>Fixed (not generated) so pre-migration Virtual DSP projects resolve to it; every machine mints it for its own file,
    /// so sessions from elsewhere are resolved by their carried curve (<c>VirtualCrossoverCalibrationSelection</c>).</summary>
    public const string LegacyNinetyDegreesId = "90deg";

    private const string GeneratedIdPrefix = "cal-";

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public MicrophoneCalibrationKind Kind { get; set; }

    public string? Path { get; set; }

    /// <summary>File entry id, or null for the 0° calibration; never another estimate.</summary>
    public string? BaseId { get; set; }

    public double AngleDegrees { get; set; }

    public double FrontDiameterMm { get; set; } = DefaultFrontDiameterMm;

    public MicrophoneProtectionGrid Grid { get; set; }

    public MicrophoneAngleReference Reference { get; set; }

    public MicrophoneCalibrationDefinition Clone() =>
        (MicrophoneCalibrationDefinition)MemberwiseClone();

    public MicrophoneAngleRequest ToAngleRequest() =>
        new(AngleDegrees, FrontDiameterMm, Grid, Reference);

    /// <summary>Clamps into model range and names unnamed entries; runs on load against hand-edited files.</summary>
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
        // Zero or less is absent, not a 1 mm mic that diffracts nothing.
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

    public static bool IsGeneratedId(string? id) =>
        id?.StartsWith(GeneratedIdPrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Never reuses a deleted entry's id: sessions and history keep stale ids marked missing on purpose.</summary>
    public static string CreateId(IEnumerable<MicrophoneCalibrationDefinition> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var taken = new HashSet<string>(
            existing.Select(definition => definition.Id),
            StringComparer.OrdinalIgnoreCase)
        {
            MicrophoneCalibrationIds.ZeroDegrees,
            LegacyNinetyDegreesId
        };
        string candidate;
        do
        {
            candidate = GeneratedIdPrefix + Guid.NewGuid().ToString("N");
        }
        while (!taken.Add(candidate));
        return candidate;
    }
}

/// <param name="Available">False for a missing/unparsable file or baseless estimate; still selectable so a save keeps the choice.</param>
internal sealed record MicrophoneCalibrationEntry(
    string Id,
    string Name,
    bool Available,
    string? FileName = null)
{
    /// <summary>As a list shows it: a slot no file was set for reads "not set", a set file that cannot be read "unavailable".</summary>
    public string Label =>
        Available ? Name
        : FileName == null &&
            string.Equals(Id, MicrophoneCalibrationIds.ZeroDegrees, StringComparison.OrdinalIgnoreCase)
            ? $"{Name} (not set)"
        : $"{Name} (unavailable)";
}
