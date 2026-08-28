using System.Text.Json;
using System.Text.Json.Serialization;

namespace Resonalyze.Screenshots;

/// <summary>
/// Where the tool reads its material and writes its results.
/// </summary>
/// <remarks>
/// The measurements the screenshots are taken from are large and personal — they are
/// one car, measured once — so they live outside the repository and their paths are a
/// local setting. <c>screenshots.json</c> beside the executable (or named with
/// <c>--config</c>) supplies them; <c>screenshots.example.json</c> is the committed
/// template. The shot LIST is code, because each shot has to drive real panels.
/// </remarks>
internal sealed class ShotConfig
{
    /// <summary>An impulse response: the source for every analysis-mode shot.</summary>
    [JsonPropertyName("measurement")]
    public string Measurement { get; set; } = string.Empty;

    /// <summary>A finished Virtual DSP project, for every Virtual DSP shot.</summary>
    [JsonPropertyName("session")]
    public string Session { get; set; } = string.Empty;

    /// <summary>
    /// A measurement recorded with a microphone array, for the array figures — both
    /// the curves and the dialog, whose rows are read out of this same file so the
    /// two cannot describe different sets. Optional: without it the array scene is
    /// skipped rather than failed, since not every rig has one.
    /// </summary>
    [JsonPropertyName("arrayMeasurement")]
    public string ArrayMeasurement { get; set; } = string.Empty;

    /// <summary>
    /// Where the PNGs land. Empty means the repository's own <c>assets/images</c>,
    /// found by walking up from the executable — the usual case, where re-shooting is
    /// meant to overwrite the committed figures in place.
    /// </summary>
    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;

    private string? resolvedOutput;

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
            JsonSerializer.Deserialize<ShotConfig>(File.ReadAllText(file))
            ?? throw new InvalidOperationException($"{file} is empty.");
        config.Validate(file);
        return config;
    }

    // Beside the executable first, then in the project folder — which is where the
    // .gitignore entry expects it, and where it survives a clean of bin/.
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

        // Optional, but a path that is set and wrong is a typo rather than a choice.
        if (!string.IsNullOrWhiteSpace(ArrayMeasurement) && !File.Exists(ArrayMeasurement))
        {
            throw new FileNotFoundException(
                $"{file}: \"arrayMeasurement\" → {ArrayMeasurement}", ArrayMeasurement);
        }
    }

    /// <summary>Turns a shot name into the file it writes.</summary>
    public string Resolve(string name) =>
        Path.GetFullPath(Path.Combine(OutputRoot, name + ".png"));

    /// <summary>The folder the shots are written into.</summary>
    public string OutputRoot => resolvedOutput ??=
        string.IsNullOrWhiteSpace(Output) ? FindRepositoryAssets() : Output;

    // The executable sits in tools/Resonalyze.Screenshots/bin/<config>/<tfm>, so the
    // repository is a few levels up. Walking rather than counting survives a changed
    // output path.
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
