using System.Globalization;
using YamlDotNet.Serialization;

namespace Resonalyze.Dsp;

/// <summary>
/// CamillaDSP config (YAML). Exports each PEQ band as a Biquad — Peaking, Lowshelf
/// and Highshelf take the freq/gain/q triple, Allpass takes freq/q and AllpassFO
/// freq alone — plus a Gain filter for the preamp, wired into a two-channel
/// pipeline. Imports those five biquad types and the first Gain filter; anything
/// else is skipped. The filters are cascaded, and a cascade of biquads commutes,
/// so filter order does not affect the result.
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
            // The key names the slot, not the shape: a filter that changed type
            // between two exports keeps its place in the pipeline list.
            string key = $"band_{i:000}";
            // An all-pass takes no gain — CamillaDSP's Allpass has no such
            // parameter — and its first-order variant (AllpassFO) takes no Q either.
            var parameters = new Dictionary<string, object?>
            {
                ["type"] = FilterTypeName(band.Type),
                ["freq"] = EqTextNumbers.Format(band.FrequencyHz, "0.###")
            };
            if (band.Type != PeqBandType.AllPassFirstOrder)
            {
                parameters["q"] = EqTextNumbers.Format(band.Q, "0.0");
            }
            if (!band.Type.IsAllPass())
            {
                parameters["gain"] = EqTextNumbers.Format(band.GainDb, "0.0");
            }

            filters[key] = new Dictionary<string, object?>
            {
                ["type"] = "Biquad",
                ["parameters"] = parameters
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

    public bool TryImport(string text, out EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(text);
        curve = new EqualizationCurve(Array.Empty<PeqBand>());

        object? graph;
        try
        {
            graph = new DeserializerBuilder().Build().Deserialize<object>(text);
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return false;
        }

        // Valid YAML without a "filters" map is not a CamillaDSP config.
        if (graph is not IDictionary<object, object> root ||
            GetMap(root, "filters") is not { } filters)
        {
            return false;
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
            if (!TryReadFilterType(subType, out PeqBandType bandType))
            {
                continue;
            }

            if (bands.Count >= EqualizationCurve.MaxBandCount ||
                !EqTextNumbers.TryParse(GetString(parameters, "freq"), out double frequencyHz) ||
                !double.IsFinite(frequencyHz) || frequencyHz <= 0)
            {
                continue;
            }

            // An Allpass block carries no gain, and an AllpassFO no Q either; the
            // gain-bearing shapes still require all three numbers.
            double bandGain = 0;
            if (!bandType.IsAllPass() &&
                (!EqTextNumbers.TryParse(GetString(parameters, "gain"), out bandGain) ||
                    !double.IsFinite(bandGain)))
            {
                continue;
            }

            double q = 1.0;
            if (bandType != PeqBandType.AllPassFirstOrder &&
                (!EqTextNumbers.TryParse(GetString(parameters, "q"), out q) ||
                    !double.IsFinite(q) || q <= 0))
            {
                continue;
            }

            bands.Add(new PeqBand(frequencyHz, q, bandGain, bandType));
        }

        curve = new EqualizationCurve(bands, preampDb);
        return true;
    }

    // CamillaDSP names the shelves Lowshelf/Highshelf and takes the same freq/gain/q
    // triple for them as for Peaking (it also accepts a "slope" instead of "q";
    // exports state q, which is what the library holds). Its all-pass types match
    // ours one to one: Allpass is the second-order section (freq + q), AllpassFO
    // the single-pole first order (freq only).
    private static string FilterTypeName(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => "Lowshelf",
        PeqBandType.HighShelf => "Highshelf",
        PeqBandType.AllPassFirstOrder => "AllpassFO",
        PeqBandType.AllPassSecondOrder => "Allpass",
        _ => "Peaking"
    };

    // A biquad with no stated sub-type is a Peaking one; anything outside the five
    // the library can hold (LowpassFO, Notch, the FO shelves, ...) is skipped.
    private static bool TryReadFilterType(string? name, out PeqBandType type)
    {
        type = PeqBandType.Peaking;
        if (name == null || name.Equals("Peaking", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Equals("Lowshelf", StringComparison.OrdinalIgnoreCase))
        {
            type = PeqBandType.LowShelf;
            return true;
        }

        if (name.Equals("Highshelf", StringComparison.OrdinalIgnoreCase))
        {
            type = PeqBandType.HighShelf;
            return true;
        }

        if (name.Equals("Allpass", StringComparison.OrdinalIgnoreCase))
        {
            type = PeqBandType.AllPassSecondOrder;
            return true;
        }

        if (name.Equals("AllpassFO", StringComparison.OrdinalIgnoreCase))
        {
            type = PeqBandType.AllPassFirstOrder;
            return true;
        }

        return false;
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
