using System.Text.Json;

namespace Resonalyze.Dsp;

/// <summary>EasyEffects preset (JSON), Bell bands only; level maps to output gain. Its shelf and all-pass bands are LSP filters
/// shaped by mode/slope, not our Q, so they are unsupported. EasyEffects 7 names each plugin instance ("equalizer#0") and
/// loads only the plugins its "plugins_order" lists, so an export carries both.</summary>
public sealed class EasyEffectsFormat : IEqProfileFormat
{
    private const string EqualizerInstance = "equalizer#0";

    public string Name => "EasyEffects";
    public string Extension => "json";
    public bool CanImport => true;
    public bool CanExport => true;
    public bool SupportsShelvingFilters => false;

    public bool SupportsAllPass(PeqBandType type) => false;

    public string Export(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        var bands = new Dictionary<string, object?>();
        for (int i = 0; i < curve.Bands.Count; i++)
        {
            PeqBand band = curve.Bands[i];
            // Real presets have no "width" field: a Bell's bandwidth is q alone.
            bands[$"band{i}"] = new Dictionary<string, object?>
            {
                ["type"] = "Bell",
                ["mode"] = "RLC (BT)",
                ["slope"] = "x1",
                ["solo"] = false,
                ["mute"] = false,
                ["gain"] = band.GainDb,
                ["frequency"] = band.FrequencyHz,
                ["q"] = band.Q
            };
        }

        var equalizer = new Dictionary<string, object?>
        {
            ["bypass"] = false,
            ["input-gain"] = 0.0,
            ["output-gain"] = curve.PreampDb,
            ["mode"] = "IIR",
            ["num-bands"] = curve.Bands.Count,
            ["split-channels"] = false,
            ["left"] = bands,
            ["right"] = bands
        };

        var root = new Dictionary<string, object?>
        {
            ["output"] = new Dictionary<string, object?>
            {
                ["blocklist"] = Array.Empty<string>(),
                [EqualizerInstance] = equalizer,
                ["plugins_order"] = new[] { EqualizerInstance }
            }
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    public bool TryImport(string text, out EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(text);
        curve = new EqualizationCurve(Array.Empty<PeqBand>());

        try
        {
            // All JsonElement access must happen before the document is disposed.
            using JsonDocument document = JsonDocument.Parse(text);
            if (!TryFindEqualizer(document.RootElement, out JsonElement equalizer))
            {
                return false;
            }

            // Both gains are flat level stages around the bands, so together they are the preamp.
            double preampDb = ReadDouble(equalizer, "input-gain", 0) + ReadDouble(equalizer, "output-gain", 0);

            // Current versions nest bands under "left"/"right"; older ones under the equalizer.
            JsonElement bandsHost = equalizer;
            if (equalizer.TryGetProperty("left", out JsonElement left) &&
                left.ValueKind == JsonValueKind.Object)
            {
                bandsHost = left;
            }

            var bands = new List<PeqBand>();
            foreach (JsonProperty property in bandsHost.EnumerateObject())
            {
                if (bands.Count >= EqualizationCurve.MaxBandCount)
                {
                    break;
                }

                if (!property.Name.StartsWith("band", StringComparison.OrdinalIgnoreCase) ||
                    property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (TryReadBand(property.Value, out PeqBand band))
                {
                    bands.Add(band);
                }
            }

            curve = new EqualizationCurve(bands, preampDb);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindEqualizer(JsonElement root, out JsonElement equalizer)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (TryFindEqualizerIn(root, out equalizer))
            {
                return true;
            }

            foreach (string pipeline in new[] { "output", "input" })
            {
                if (root.TryGetProperty(pipeline, out JsonElement host) &&
                    host.ValueKind == JsonValueKind.Object &&
                    TryFindEqualizerIn(host, out equalizer))
                {
                    return true;
                }
            }

            if (root.TryGetProperty("num-bands", out _) || root.TryGetProperty("left", out _))
            {
                equalizer = root;
                return true;
            }
        }

        equalizer = default;
        return false;
    }

    // EasyEffects 7 keys an instance "equalizer#N"; older presets key it "equalizer". The first equalizer the
    // pipeline order runs wins, then the first in the file.
    private static bool TryFindEqualizerIn(JsonElement host, out JsonElement equalizer)
    {
        if (host.TryGetProperty("plugins_order", out JsonElement order) && order.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement name in order.EnumerateArray())
            {
                if (name.ValueKind == JsonValueKind.String &&
                    IsEqualizerKey(name.GetString()!) &&
                    host.TryGetProperty(name.GetString()!, out equalizer) &&
                    equalizer.ValueKind == JsonValueKind.Object)
                {
                    return true;
                }
            }
        }

        foreach (JsonProperty property in host.EnumerateObject())
        {
            if (IsEqualizerKey(property.Name) && property.Value.ValueKind == JsonValueKind.Object)
            {
                equalizer = property.Value;
                return true;
            }
        }

        equalizer = default;
        return false;
    }

    private static bool IsEqualizerKey(string name) =>
        name.Equals("equalizer", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("equalizer#", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadBand(JsonElement band, out PeqBand result)
    {
        result = default;

        if (band.TryGetProperty("type", out JsonElement type) &&
            type.ValueKind == JsonValueKind.String &&
            !type.GetString()!.Equals("Bell", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // A muted band does not reach the output.
        if (band.TryGetProperty("mute", out JsonElement mute) && mute.ValueKind == JsonValueKind.True)
        {
            return false;
        }

        double frequency = ReadDouble(band, "frequency", double.NaN);
        double gain = ReadDouble(band, "gain", double.NaN);
        double q = ReadDouble(band, "q", double.NaN);
        if (!double.IsFinite(frequency) || frequency <= 0 ||
            !double.IsFinite(q) || q <= 0 ||
            !double.IsFinite(gain))
        {
            return false;
        }

        result = new PeqBand(frequency, q, gain);
        return true;
    }

    private static double ReadDouble(JsonElement element, string name, double fallback) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double result)
            ? result
            : fallback;
}
