using System.Text.Json;
using System.Text.Json.Serialization;
using Resonalyze.Audio;

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
    /// The interface the array DIALOG's figure is drawn for.
    /// </summary>
    /// <remarks>
    /// The dialog states how many inputs are still free and where that count comes
    /// from, and a measurement file records none of it: not the loopback's input, not
    /// the backend, not how many inputs the device has. The tool used to fill that in
    /// by itself, which put a status line in the manual that nobody had authored — so
    /// the figure is not taken until the rig is stated here. It is the figure's rig
    /// rather than the measurement's: a set may have been assembled over sittings on a
    /// smaller interface, and which device the dialog should be shown for is the
    /// author's decision to make out loud, not the tool's to guess.
    /// </remarks>
    [JsonPropertyName("arrayRig")]
    public ArrayRig? Rig { get; set; }

    /// <summary>
    /// Where the PNGs land. Empty means the repository's own <c>assets/images</c>,
    /// found by walking up from the executable — the usual case, where re-shooting is
    /// meant to overwrite the committed figures in place.
    /// </summary>
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

/// <summary>
/// The interface an array measurement was recorded on, as the dialog counts it.
/// </summary>
internal sealed class ArrayRig
{
    /// <summary>How many inputs the backend offers on that device.</summary>
    [JsonPropertyName("inputs")]
    public int Inputs { get; set; }

    /// <summary>
    /// Which input carries the loopback, numbered the way the dialog prints inputs —
    /// Input 1 is 1. It is excluded from the free ones, so the status line only comes
    /// out right when this is the input the measurement actually used.
    /// </summary>
    [JsonPropertyName("loopbackInput")]
    public int LoopbackInput { get; set; }

    /// <summary>
    /// Which backend recorded it: <c>asio</c>, <c>wasapiShared</c>,
    /// <c>wasapiExclusive</c> or <c>wave</c> (MME). It decides the wording of the
    /// status line's parenthesis, which is the application's own.
    /// </summary>
    [JsonPropertyName("backend")]
    public AudioBackend Backend { get; set; } = AudioBackend.Asio;
}
