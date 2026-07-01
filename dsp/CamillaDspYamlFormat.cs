using System.Globalization;
using YamlDotNet.Serialization;

namespace Resonalyze.Dsp;

/// <summary>
/// CamillaDSP config (YAML). Exports each PEQ band as a Biquad/Peaking filter plus a
/// Gain filter for the preamp, wired into a two-channel pipeline. Imports Peaking
/// biquads and the first Gain filter; other filter types are skipped. Peaking filters
/// sum commutatively, so filter order does not affect the result.
/// </summary>
public sealed class CamillaDspYamlFormat : IEqProfileFormat
{
    private const string PreampFilterName = "preamp";

    public string Name => "CamillaDSP";
    public string Extension => "yml";
    public bool CanImport => true;
    public bool CanExport => true;

    public string Export(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        var filters = new Dictionary<string, object?>
        {
            [PreampFilterName] = new Dictionary<string, object?>
            {
                ["type"] = "Gain",
                ["parameters"] = new Dictionary<string, object?>
                {
                    ["gain"] = EqTextNumbers.Format(curve.PreampDb, "0.0")
                }
            }
        };

        var names = new List<object?> { PreampFilterName };
        for (int i = 0; i < curve.Bands.Count; i++)
        {
            PeqBand band = curve.Bands[i];
            string key = $"peaking_{i:000}";
            filters[key] = new Dictionary<string, object?>
            {
                ["type"] = "Biquad",
                ["parameters"] = new Dictionary<string, object?>
                {
                    ["type"] = "Peaking",
                    ["freq"] = EqTextNumbers.Format(band.FrequencyHz, "0.###"),
                    ["q"] = EqTextNumbers.Format(band.Q, "0.0"),
                    ["gain"] = EqTextNumbers.Format(band.GainDb, "0.0")
                }
            };
            names.Add(key);
        }

        var pipeline = new List<object?>();
        foreach (int channel in new[] { 0, 1 })
        {
            pipeline.Add(new Dictionary<string, object?>
            {
                ["type"] = "Filter",
                ["channel"] = channel,
                ["names"] = new List<object?>(names)
            });
        }

        var root = new Dictionary<string, object?>
        {
            ["filters"] = filters,
            ["pipeline"] = pipeline
        };

        return new SerializerBuilder().Build().Serialize(root);
    }

    public EqualizationCurve Import(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        object? graph;
        try
        {
            graph = new DeserializerBuilder().Build().Deserialize<object>(text);
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return new EqualizationCurve(Array.Empty<PeqBand>());
        }

        if (graph is not IDictionary<object, object> root ||
            GetMap(root, "filters") is not { } filters)
        {
            return new EqualizationCurve(Array.Empty<PeqBand>());
        }

        double preampDb = 0;
        bool preampRead = false;
        var bands = new List<PeqBand>();

        foreach (KeyValuePair<object, object> entry in filters)
        {
            if (entry.Value is not IDictionary<object, object> filter)
            {
                continue;
            }

            string? type = GetString(filter, "type");
            if (GetMap(filter, "parameters") is not { } parameters)
            {
                continue;
            }

            if (!preampRead &&
                string.Equals(type, "Gain", StringComparison.OrdinalIgnoreCase) &&
                EqTextNumbers.TryParse(GetString(parameters, "gain"), out double gainValue))
            {
                preampDb = gainValue;
                preampRead = true;
                continue;
            }

            if (!string.Equals(type, "Biquad", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? subType = GetString(parameters, "type");
            if (subType != null && !subType.Equals("Peaking", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (bands.Count < EqualizationCurve.MaxBandCount &&
                EqTextNumbers.TryParse(GetString(parameters, "freq"), out double frequencyHz) &&
                EqTextNumbers.TryParse(GetString(parameters, "gain"), out double bandGain) &&
                EqTextNumbers.TryParse(GetString(parameters, "q"), out double q) &&
                double.IsFinite(frequencyHz) && frequencyHz > 0 &&
                double.IsFinite(q) && q > 0 &&
                double.IsFinite(bandGain))
            {
                bands.Add(new PeqBand(frequencyHz, q, bandGain));
            }
        }

        return new EqualizationCurve(bands, preampDb);
    }

    private static IDictionary<object, object>? GetMap(IDictionary<object, object> map, string key) =>
        map.TryGetValue(key, out object? value) && value is IDictionary<object, object> child
            ? child
            : null;

    private static string? GetString(IDictionary<object, object> map, string key) =>
        map.TryGetValue(key, out object? value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;
}
