using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze;

/// <summary>The main window's size and position when it was last closed, and whether it was maximized. Kept apart
/// from the measurement settings: it is read before the window exists, and losing it costs only the window's size.</summary>
internal sealed class WindowPlacementFile
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }

    /// <summary>The bounds the window restores to, in screen pixels, also while it is maximized; empty when none was saved.</summary>
    [JsonIgnore]
    public Rectangle NormalBounds
    {
        get => new(Left, Top, Width, Height);
        set => (Left, Top, Width, Height) = (value.Left, value.Top, value.Width, value.Height);
    }

    [JsonIgnore]
    private string pathOnDisk = ApplicationDataPaths.Current.WindowPlacementFile;

    public static WindowPlacementFile LoadOrDefault(string? pathOnDisk = null)
    {
        string path = pathOnDisk ?? ApplicationDataPaths.Current.WindowPlacementFile;
        try
        {
            if (File.Exists(path))
            {
                using FileStream stream = File.OpenRead(path);
                WindowPlacementFile? loaded =
                    JsonSerializer.Deserialize<WindowPlacementFile>(stream, SerializerOptions);
                if (loaded != null)
                {
                    loaded.pathOnDisk = path;
                    return loaded;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // The designer's size is a valid answer; an unreadable file is not worth a dialog on startup.
        }

        return new WindowPlacementFile { pathOnDisk = path };
    }

    /// <summary>False when the write failed; the caller has nothing to tell the user, the next start simply uses the default size.</summary>
    public bool TrySave()
    {
        try
        {
            AtomicFile.Write(
                pathOnDisk,
                stream => JsonSerializer.Serialize(stream, this, SerializerOptions));
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
