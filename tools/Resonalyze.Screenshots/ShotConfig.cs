using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Audio;

namespace Resonalyze.Screenshots;

/// <remarks>Measurements are large and personal, so their paths are a local setting (<c>screenshots.json</c>; template
/// <c>screenshots.example.json</c>). The shot list is code.</remarks>
internal sealed class ShotConfig
{
    [JsonPropertyName("measurement")]
    public string Measurement { get; set; } = string.Empty;

    [JsonPropertyName("session")]
    public string Session { get; set; } = string.Empty;

    /// <summary>Optional: without it the array scene is skipped. The dialog figure reads its rows from this same file.</summary>
    [JsonPropertyName("arrayMeasurement")]
    public string ArrayMeasurement { get; set; } = string.Empty;

    /// <summary>Authored rig for the array dialog figure: the measurement records no device facts, and guessing them put an unauthored status line in the manual.</summary>
    [JsonPropertyName("arrayRig")]
    public ArrayRig? Rig { get; set; }

    /// <summary>Empty means the repository's <c>assets/images</c>, overwriting committed figures in place.</summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;

    private string? resolvedOutput;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ShotConfig Load(string? path)
    {
        string file = path ?? Discover();
        if (!File.Exists(file))
        {
            throw new FileNotFoundException(
                "No screenshots.json. Copy tools/Resonalyze.Screenshots/" +
                "screenshots.example.json next to it as screenshots.json — it is " +
                "git-ignored — point it at your own measurements, or pass " +
                "--config <path>.",
                file);
        }

        ShotConfig config =
            JsonSerializer.Deserialize<ShotConfig>(File.ReadAllText(file), SerializerOptions)
            ?? throw new InvalidOperationException($"{file} is empty.");
        config.Validate(file);
        return config;
    }

    // Project folder second: where .gitignore expects it and where it survives a clean of bin/.
    private static string Discover()
    {
        string local = Path.Combine(AppContext.BaseDirectory, "screenshots.json");
        if (File.Exists(local))
        {
            return local;
        }

        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine(
                directory.FullName, "tools", "Resonalyze.Screenshots", "screenshots.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return local;
    }

    private void Validate(string file)
    {
        foreach ((string name, string value) in new[]
        {
            ("measurement", Measurement), ("session", Session)
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{file}: \"{name}\" is not set.");
            }

            if (!File.Exists(value))
            {
                throw new FileNotFoundException($"{file}: \"{name}\" → {value}", value);
            }
        }

        if (!string.IsNullOrWhiteSpace(ArrayMeasurement) && !File.Exists(ArrayMeasurement))
        {
            throw new FileNotFoundException(
                $"{file}: \"arrayMeasurement\" → {ArrayMeasurement}", ArrayMeasurement);
        }

        if (Rig == null)
        {
            return;
        }

        if (Rig.Inputs < 2)
        {
            throw new InvalidOperationException(
                $"{file}: \"arrayRig.inputs\" is {Rig.Inputs}; an array needs a device with " +
                "the measurement microphone, the loopback and at least one further input.");
        }

        if (Rig.LoopbackInput < 1 || Rig.LoopbackInput > Rig.Inputs)
        {
            throw new InvalidOperationException(
                $"{file}: \"arrayRig.loopbackInput\" is {Rig.LoopbackInput}, which is not one " +
                $"of the {Rig.Inputs} inputs. It is numbered as the dialog shows it: Input 1 is 1.");
        }
    }

    public string Resolve(string name) =>
        Path.GetFullPath(Path.Combine(OutputRoot, name + ".png"));

    public string OutputRoot => resolvedOutput ??=
        string.IsNullOrWhiteSpace(Output) ? FindRepositoryAssets() : Output;

    private static string FindRepositoryAssets()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "assets", "images");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            "No assets/images above the executable; set \"output\" in the config.");
    }
}

internal sealed class ArrayRig
{
    [JsonPropertyName("inputs")]
    public int Inputs { get; set; }

    /// <summary>1-based like the dialog; must be the input the measurement actually used.</summary>
    [JsonPropertyName("loopbackInput")]
    public int LoopbackInput { get; set; }

    [JsonPropertyName("backend")]
    public AudioBackend Backend { get; set; } = AudioBackend.Asio;

    /// <summary>One name per row, or absent for the stored names (a set recorded with one moved mic repeats one name).</summary>
    [JsonPropertyName("calibrations")]
    public string[]? Calibrations { get; set; }
}
