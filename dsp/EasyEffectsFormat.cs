using System.Text.Json;

namespace Resonalyze.Dsp;

/// <summary>
/// EasyEffects equalizer preset (JSON). Only peaking ("Bell") bands are handled;
/// other band types are skipped on import. The overall level maps to the
/// equalizer's output gain.
/// </summary>
public sealed class EasyEffectsFormat : IEqProfileFormat
{
    public string Name => "EasyEffects";
    public string Extension => "json";
    public bool CanImport => true;
    public bool CanExport => true;

    public string Export(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        var bands = new Dictionary<string, object?>();
        for (int i = 0; i < curve.Bands.Count; i++)
        {
            PeqBand band = curve.Bands[i];
            bands[$"band{i}"] = new Dictionary<string, object?>
            {
                ["type"] = "Bell",
                ["mode"] = "RLC (BT)",
                ["slope"] = "x1",
                ["solo"] = false,
                ["mute"] = false,
                ["gain"] = band.GainDb,
                ["frequency"] = band.FrequencyHz,
                ["q"] = band.Q,
                ["width"] = band.Q
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
            ["output"] = new Dictionary<string, object?> { ["equalizer"] = equalizer }
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
    }

    public EqualizationCurve Import(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        try
        {
            // All JsonElement access must happen before the document is disposed.
            using JsonDocument document = JsonDocument.Parse(text);
            if (!TryFindEqualizer(document.RootElement, out JsonElement equalizer))
            {
                return new EqualizationCurve(Array.Empty<PeqBand>());
            }

            double preampDb = ReadDouble(equalizer, "output-gain", 0);

            // Bands live under "left" (and a mirrored "right") in current versions,
            // or directly under the equalizer in older ones.
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

            return new EqualizationCurve(bands, preampDb);
        }
        catch (JsonException)
        {
            return new EqualizationCurve(Array.Empty<PeqBand>());
        }
    }

    private static bool TryFindEqualizer(JsonElement root, out JsonElement equalizer)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("equalizer", out equalizer))
            {
                return true;
            }

            if (root.TryGetProperty("output", out JsonElement output) &&
                output.ValueKind == JsonValueKind.Object &&
                output.TryGetProperty("equalizer", out equalizer))
            {
                return true;
            }

            // The root may already be the equalizer node.
            if (root.TryGetProperty("num-bands", out _) || root.TryGetProperty("left", out _))
            {
                equalizer = root;
                return true;
            }
        }

        equalizer = default;
        return false;
    }

    private static bool TryReadBand(JsonElement band, out PeqBand result)
    {
        result = default;

        if (band.TryGetProperty("type", out JsonElement type) &&
            type.ValueKind == JsonValueKind.String &&
            !type.GetString()!.Equals("Bell", StringComparison.OrdinalIgnoreCase))
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
