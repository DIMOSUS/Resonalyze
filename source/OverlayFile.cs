using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze;

/// <summary>
/// Stores one overlay slot independently from OxyPlot and Windows Forms.
/// </summary>
public sealed class OverlayFile
{
    public const string CurrentFormat = "resonalyze-overlay";
    public const int CurrentVersion = 4;
    public const int MaximumSlotCount = 10;

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
    public string Title { get; set; } = string.Empty;
    public double Offset { get; set; }
    public int ColorArgb { get; set; }
    public double StrokeThickness { get; set; } = 2;
    public OverlayLineStyle LineStyle { get; set; } = OverlayLineStyle.Solid;
    public int OpacityPercent { get; set; } = 100;
    public int SmoothingInverseOctaves { get; set; }
    public OverlayPoint[] Points { get; set; } = Array.Empty<OverlayPoint>();

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
    Blend
}

public static class OverlaySmoothing
{
    public static IReadOnlyList<int> SupportedInverseOctaves { get; } =
        [0, 48, 24, 12, 6, 3];

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
