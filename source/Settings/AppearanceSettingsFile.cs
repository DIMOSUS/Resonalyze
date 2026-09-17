using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze;

/// <summary>The look of the application, kept apart from the measurement settings because it is read before the
/// first form exists — <see cref="MeasurementSettingsFile"/> resolves audio devices on load, which is far too much
/// work to do for one colour decision.</summary>
internal sealed class AppearanceSettingsFile
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public UiTheme Theme { get; set; } = UiTheme.Dark;

    /// <summary>Why the last <see cref="TrySave"/> failed; telling the user is the caller's job, not this layer's.</summary>
    [JsonIgnore]
    public string? SaveWarning { get; private set; }

    [JsonIgnore]
    private string pathOnDisk = ApplicationDataPaths.Current.AppearanceFile;

    public static AppearanceSettingsFile LoadOrDefault(string? pathOnDisk = null)
    {
        string path = pathOnDisk ?? ApplicationDataPaths.Current.AppearanceFile;
        try
        {
            if (File.Exists(path))
            {
                using FileStream stream = File.OpenRead(path);
                AppearanceSettingsFile? loaded =
                    JsonSerializer.Deserialize<AppearanceSettingsFile>(stream, SerializerOptions);
                if (loaded != null && Enum.IsDefined(loaded.Theme))
                {
                    loaded.pathOnDisk = path;
                    return loaded;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable appearance file is not worth a dialog on startup: the default theme is a valid answer.
        }

        return new AppearanceSettingsFile { pathOnDisk = path };
    }

    /// <summary>False when the file still holds the old theme, with <see cref="SaveWarning"/> saying why: a caller
    /// must not then act as if the new one were in force.</summary>
    public bool TrySave()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pathOnDisk)!);
            using FileStream stream = File.Create(pathOnDisk);
            JsonSerializer.Serialize(stream, this, SerializerOptions);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            SaveWarning =
                $"The appearance settings could not be saved to '{pathOnDisk}':\r\n\r\n{exception.Message}";
            return false;
        }
    }
}
