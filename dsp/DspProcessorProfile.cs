using System.Text;

namespace Resonalyze.Dsp;

/// <summary>One catalog device. <see cref="Id"/> derives from the names, so renaming an entry orphans stored files.
/// See docs/tech/dsp-processor-catalog.md.</summary>
public sealed record DspProcessorPreset(
    string Manufacturer,
    string ModelName,
    int SampleRateHz,
    PeqQConvention QConvention,
    double? MaxDelayMs = null,
    bool PhaseControl = false,
    bool FirFilters = false)
{
    /// <summary>Stable file identity, e.g. <c>helix-dsp-ultra-s</c>.</summary>
    public string Id { get; } = MakeId(Manufacturer, ModelName);

    public string DisplayName => Manufacturer.Length == 0
        ? ModelName
        : $"{Manufacturer} {ModelName}";

    public DspProcessorProfile ToProfile() => new(Id, SampleRateHz, QConvention);

    public override string ToString() => DisplayName;

    private static string MakeId(string manufacturer, string modelName)
    {
        var builder = new StringBuilder(manufacturer.Length + modelName.Length + 1);
        foreach (char character in $"{manufacturer} {modelName}")
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().TrimEnd('-');
    }
}

/// <summary>Processor facts for simulation; <see cref="SampleRateHz"/> is the device's filter rate, not the measurement's.
/// <see cref="QConvention"/> affects exported numbers only, not the simulation.</summary>
public sealed record DspProcessorProfile(
    string? ModelId,
    int SampleRateHz,
    PeqQConvention QConvention)
{
    public static DspProcessorProfile Custom(
        int sampleRateHz,
        PeqQConvention qConvention) =>
        new(null, sampleRateHz, qConvention);

    public bool IsCustom => DspProcessorCatalog.Preset(ModelId) == null;

    public string DisplayName =>
        DspProcessorCatalog.Preset(ModelId)?.DisplayName ?? "Custom";

    /// <summary>Catalog figure, else <see cref="AutoAlignmentEngine.DefaultMaxDelayMs"/>.</summary>
    public double MaxDelayMs =>
        DspProcessorCatalog.Preset(ModelId)?.MaxDelayMs
            ?? AutoAlignmentEngine.DefaultMaxDelayMs;
}

public static class DspProcessorCatalog
{
    // Selector order. Q convention is per model; only Panacea's is measured. See docs/tech/dsp-processor-catalog.md#properties.
    private static readonly DspProcessorPreset[] PresetList =
    [
        new("AMP", "Panacea v1/v2", 96_000, PeqQConvention.Symmetric),

        new("HELIX", "NEXT DSP ULTRA XT", 96_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "DSP ULTRA S", 96_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "DSP ULTRA", 96_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "DSP PRO MK3", 96_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "DSP PRO MK2", 96_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "DSP.3S", 96_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "DSP.3", 96_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "DSP MINI MK2", 96_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "DSP MINI", 96_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "P SIX DSP ULTIMATE", 96_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "P SIX DSP MK2", 96_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "NEXT V EIGHT DSP ULTIMATE", 48_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "V EIGHT DSP MK2", 48_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "V TWELVE DSP MK2", 48_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "V TWELVE DSP", 48_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "V EIGHTEEN DSP", 48_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "M SIX DSP", 48_000, PeqQConvention.Rbj, PhaseControl: true),
        new("HELIX", "AMPLIFY 206 DSP", 48_000, PeqQConvention.Rbj, PhaseControl: true),

        new("Audison", "Forza AF M12.14 bit", 96_000, PeqQConvention.Rbj),
        new("Audison", "Forza AF M8.14 bit", 96_000, PeqQConvention.Rbj),
        new("Audison", "Forza AF C8.14 bit", 96_000, PeqQConvention.Rbj),
        new("Audison", "Forza AF M5.11 bit", 96_000, PeqQConvention.Rbj),
        new("Audison", "Forza AF C4.10 bit", 96_000, PeqQConvention.Rbj),
        new("Audison", "Forza AF M1.7 bit", 96_000, PeqQConvention.Rbj),

        new("Hertz", "S8 DSP", 96_000, PeqQConvention.Rbj),

        new("Mosconi", "DSP 8to12 Aerospace", 192_000, PeqQConvention.Rbj),
        new("Mosconi", "DSP 6to8 Aerospace", 96_000, PeqQConvention.Rbj),
        new("Mosconi", "DSP 8to12 PRO", 96_000, PeqQConvention.Rbj),
        new("Mosconi", "Pico 6|8 DSP v2", 96_000, PeqQConvention.Rbj),

        new("ESX", "D66SP", 96_000, PeqQConvention.Rbj),
        new("ESX", "QE812SP", 96_000, PeqQConvention.Rbj),
        new("ESX", "VE900.7SP", 96_000, PeqQConvention.Rbj),
        new("ESX", "VE1000.6SP", 96_000, PeqQConvention.Rbj),
        new("ESX", "VE1300.11SPv2", 96_000, PeqQConvention.Rbj),

        new("miniDSP", "C-DSP 8x12", 192_000, PeqQConvention.Rbj),
        new("miniDSP", "C-DSP 8x12 DL", 48_000, PeqQConvention.Rbj),
        new("miniDSP", "Harmony 8x12 DSP", 48_000, PeqQConvention.Rbj),

        new("JL Audio", "TwK 88", 48_000, PeqQConvention.Classic),
        new("JL Audio", "TwK D8", 48_000, PeqQConvention.Classic),

        new("ARC Audio", "ARC 1000.6 + IPS8.8", 96_000, PeqQConvention.Rbj)
    ];

    // Throws on id collision (names differing only in punctuation).
    private static readonly Dictionary<string, DspProcessorPreset> PresetsById =
        PresetList.ToDictionary(preset => preset.Id, StringComparer.Ordinal);

    public static IReadOnlyList<DspProcessorPreset> Presets => PresetList;

    public static IReadOnlyList<int> SelectableSampleRatesHz { get; } =
        [44_100, 48_000, 88_200, 96_000, 176_400, 192_000];

    public static IReadOnlyList<PeqQConvention> SelectableQConventions { get; } =
        [PeqQConvention.Rbj, PeqQConvention.Symmetric, PeqQConvention.Classic];

    /// <summary>Null for Custom and for ids unknown to this build (which keep their stored numbers).</summary>
    public static DspProcessorPreset? Preset(string? modelId) =>
        string.IsNullOrEmpty(modelId)
            ? null
            : PresetsById.GetValueOrDefault(modelId);

    /// <summary>A named model always answers with its current preset, so catalog corrections reach old projects.</summary>
    public static DspProcessorProfile Resolve(DspProcessorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Preset(profile.ModelId)?.ToProfile() ?? profile;
    }
}
