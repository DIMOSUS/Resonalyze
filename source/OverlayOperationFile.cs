using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze;

/// <summary>
/// Stores the recipe and presentation settings for a calculated overlay slot.
/// </summary>
public sealed class OverlayOperationFile
{
    public const string CurrentFormat = "resonalyze-overlay-operation";
    public const int CurrentVersion = 4;
    public const int MinimumSlot = 11;
    public const int MaximumSlot = 12;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
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
    public int SourceSlotA { get; set; }
    public int SourceSlotB { get; set; }
    public OverlayOperation Operation { get; set; } = OverlayOperation.AMinusB;
    public double BlendFrequencyHz { get; set; } = 1_000;
    public double BlendWidthOctaves { get; set; } = 1;
    public bool UseAmplitudeSpace { get; set; }
    public double Offset { get; set; }
    public int ColorArgb { get; set; }
    public double StrokeThickness { get; set; } = 2;
    public OverlayLineStyle LineStyle { get; set; } = OverlayLineStyle.Dash;
    public int OpacityPercent { get; set; } = 100;
    public int SmoothingInverseOctaves { get; set; }

    public static string GetPath(
        Mode mode,
        int slot,
        string? rootDirectory = null)
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
            ?? throw new InvalidOperationException(
                "Overlay operation directory cannot be resolved.");
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

    public static OverlayOperationFile? Load(
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
        OverlayOperationFile file =
            JsonSerializer.Deserialize<OverlayOperationFile>(
                stream,
                SerializerOptions)
            ?? throw new InvalidDataException(
                "The overlay operation file is empty.");
        file.Validate();

        if (file.Mode != mode || file.Slot != slot)
        {
            throw new InvalidDataException(
                "The overlay operation file does not match its mode and slot.");
        }

        return file;
    }

    private static void ValidateLocation(Mode mode, int slot)
    {
        if (mode == Mode.None || !Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if (slot is < MinimumSlot or > MaximumSlot)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    private void Validate()
    {
        if (!string.Equals(Format, CurrentFormat, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported overlay operation format '{Format}'.");
        }
        if (Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported overlay operation version {Version}.");
        }

        ValidateLocation(Mode, Slot);

        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new InvalidDataException(
                "The calculated overlay title is missing.");
        }
        if (SourceSlotA is < 1 or > OverlayFile.MaximumSlotCount ||
            SourceSlotB is < 1 or > OverlayFile.MaximumSlotCount ||
            SourceSlotA == SourceSlotB)
        {
            throw new InvalidDataException(
                "Calculated overlay source slots are invalid.");
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
        if (!double.IsFinite(Offset))
        {
            throw new InvalidDataException(
                "The calculated overlay offset is invalid.");
        }
        if (!double.IsFinite(StrokeThickness) ||
            StrokeThickness is < 0.5 or > 10)
        {
            throw new InvalidDataException(
                "The calculated overlay line thickness is invalid.");
        }
        if (!Enum.IsDefined(LineStyle))
        {
            throw new InvalidDataException(
                "The calculated overlay line style is invalid.");
        }
        if (OpacityPercent is < 10 or > 100)
        {
            throw new InvalidDataException(
                "The calculated overlay opacity is invalid.");
        }
        if (!OverlaySmoothing.IsValid(SmoothingInverseOctaves) ||
            (!OverlaySmoothing.SupportsMode(Mode) &&
             SmoothingInverseOctaves != 0))
        {
            throw new InvalidDataException(
                "The calculated overlay smoothing setting is invalid.");
        }
    }
}
