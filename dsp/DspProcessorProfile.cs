using System.Text;

namespace Resonalyze.Dsp;

/// <summary>
/// One device in the catalog: how it is named on screen and the properties a
/// simulation reads off it. <see cref="Id"/> is DERIVED from the two name parts, so
/// adding a processor is one line in <see cref="DspProcessorCatalog"/> and nothing
/// else.
/// </summary>
/// <remarks>
/// <para>
/// This is where a device's facts are written down, and the place to GROW when more
/// of them are needed — a delay step and maximum, a PEQ band count per channel, the
/// crossover families and slopes the device offers, gain and Q limits. Add a property
/// here with a default, fill it in for the devices that differ, and the untouched
/// lines keep compiling.
/// </para>
/// <para>
/// The id is what project and settings files store, so RENAMING an entry renames its
/// id: files naming the old one fall back to a Custom profile carrying the numbers
/// they were saved with — the same simulation, having lost only the model's name.
/// Correcting a device's rate or convention is safe; renaming it is the one edit that
/// costs something.
/// </para>
/// </remarks>
public sealed record DspProcessorPreset(
    string Manufacturer,
    string ModelName,
    int SampleRateHz,
    PeqQConvention QConvention)
{
    /// <summary>Stable file identity, e.g. <c>helix-dsp-ultra-s</c>.</summary>
    public string Id { get; } = MakeId(Manufacturer, ModelName);

    /// <summary>Manufacturer and model as one line, the way the selector lists it.</summary>
    public string DisplayName => Manufacturer.Length == 0
        ? ModelName
        : $"{Manufacturer} {ModelName}";

    public DspProcessorProfile ToProfile() => new(Id, SampleRateHz, QConvention);

    public override string ToString() => DisplayName;

    // Lower-case, alphanumerics kept, every other run collapsed to a single dash.
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

/// <summary>
/// What a simulation has to know about the processor being designed for.
/// <para>
/// <see cref="SampleRateHz"/> is the rate the DEVICE runs its filters at, which is
/// independent of the rate the measurements were taken at: the bilinear transform
/// warps every corner by the rate it was designed at, so filters built at the
/// measurement's rate are not the ones the device realizes (an LR4 low-pass at 8 kHz
/// designed at 48 kHz sits 1.5 dB below the 96 kHz one at 10 kHz, 4.1 dB at 12 kHz).
/// Keeping the two apart is what lets a 48 kHz sound card simulate a 96 kHz processor
/// exactly — see <see cref="PreparedDspResponse"/>.
/// </para>
/// <para>
/// <see cref="QConvention"/> does NOT change the simulation. Every band in this
/// library is realized as an RBJ biquad; the convention states how the target device
/// READS a Q number, so it applies where numbers leave for that device (the tuning
/// sheets) and nowhere else.
/// </para>
/// </summary>
/// <param name="ModelId">
/// The catalog entry this profile names, or null/empty for a hand-configured one.
/// An id this build does not know behaves as Custom, keeping the stored numbers.
/// </param>
public sealed record DspProcessorProfile(
    string? ModelId,
    int SampleRateHz,
    PeqQConvention QConvention)
{
    /// <summary>
    /// A hand-configured processor: the user owns both properties, and no preset
    /// overrides them.
    /// </summary>
    public static DspProcessorProfile Custom(
        int sampleRateHz,
        PeqQConvention qConvention) =>
        new(null, sampleRateHz, qConvention);

    /// <summary>
    /// True while the properties are the user's own to edit. A named model owns them
    /// instead, and the editors lock them to the preset.
    /// </summary>
    public bool IsCustom => DspProcessorCatalog.Preset(ModelId) == null;

    public string DisplayName =>
        DspProcessorCatalog.Preset(ModelId)?.DisplayName ?? "Custom";
}

/// <summary>
/// The known processors — the single place a device's facts are written down. Adding
/// one is a single line in <see cref="Presets"/>.
/// </summary>
public static class DspProcessorCatalog
{
    // Ordered as the selector lists them: by manufacturer, flagships first. The Q
    // convention is a property of the MODEL rather than of the maker (see
    // PeqQConvention) — JL Audio's TwK reads Classic while its own VXi does not — so
    // every line states its own.
    //
    // The numbers are the owner's table, read off the makers' published processing
    // rates; only AMP Panacea's convention is confirmed by measurement here. The
    // catalog tests pin what this file SAYS, which is not the same as pinning that a
    // device really behaves so — a correction to a line is a data fix, and it reaches
    // every project naming that model (see Resolve).
    private static readonly DspProcessorPreset[] PresetList =
    [
        // AMP Panacea is a Cirrus Logic CS47048C; its Symmetric Q is confirmed by measurement.
        new("AMP", "Panacea v1/v2", 96_000, PeqQConvention.Symmetric),

        new("HELIX", "NEXT DSP ULTRA XT", 96_000, PeqQConvention.Rbj),
        new("HELIX", "DSP ULTRA S", 96_000, PeqQConvention.Rbj),
        new("HELIX", "DSP ULTRA", 96_000, PeqQConvention.Rbj),
        new("HELIX", "DSP PRO MK3", 96_000, PeqQConvention.Rbj),
        new("HELIX", "DSP PRO MK2", 96_000, PeqQConvention.Rbj),
        new("HELIX", "DSP.3S", 96_000, PeqQConvention.Rbj),
        new("HELIX", "DSP.3", 96_000, PeqQConvention.Rbj),
        new("HELIX", "DSP MINI MK2", 96_000, PeqQConvention.Rbj),
        new("HELIX", "DSP MINI", 96_000, PeqQConvention.Rbj),
        new("HELIX", "P SIX DSP ULTIMATE", 96_000, PeqQConvention.Rbj),
        new("HELIX", "P SIX DSP MK2", 96_000, PeqQConvention.Rbj),
        new("HELIX", "NEXT V EIGHT DSP ULTIMATE", 48_000, PeqQConvention.Rbj),
        new("HELIX", "V EIGHT DSP MK2", 48_000, PeqQConvention.Rbj),
        new("HELIX", "V TWELVE DSP MK2", 48_000, PeqQConvention.Rbj),
        new("HELIX", "V TWELVE DSP", 48_000, PeqQConvention.Rbj),
        new("HELIX", "V EIGHTEEN DSP", 48_000, PeqQConvention.Rbj),
        new("HELIX", "M SIX DSP", 48_000, PeqQConvention.Rbj),
        new("HELIX", "AMPLIFY 206 DSP", 48_000, PeqQConvention.Rbj),

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
        new("JL Audio", "TwK D8", 48_000, PeqQConvention.Classic)
    ];

    // Ids are derived from the names, so two entries that differ only in punctuation
    // would collide and one would become unreachable. Fail loudly at first use — a
    // catalog test trips this the moment such a line is added.
    private static readonly Dictionary<string, DspProcessorPreset> PresetsById =
        PresetList.ToDictionary(preset => preset.Id, StringComparer.Ordinal);

    /// <summary>Every known device, in selector order. "Custom" is not one of them.</summary>
    public static IReadOnlyList<DspProcessorPreset> Presets => PresetList;

    /// <summary>
    /// The rates a processor may be set to by hand. A device outside this list is
    /// still expressible — a project stores the NUMBER, and a Custom profile accepts
    /// any positive rate; the list is only what the selector offers.
    /// </summary>
    public static IReadOnlyList<int> SelectableSampleRatesHz { get; } =
        [44_100, 48_000, 88_200, 96_000, 176_400, 192_000];

    /// <summary>The Q conventions a Custom profile may be set to.</summary>
    public static IReadOnlyList<PeqQConvention> SelectableQConventions { get; } =
        [PeqQConvention.Rbj, PeqQConvention.Symmetric, PeqQConvention.Classic];

    /// <summary>
    /// The catalog entry with this id, or null for a Custom profile (no id) and for
    /// an id this build does not know — a file from a newer catalog, which keeps the
    /// numbers it was saved with rather than losing them to an unknown name.
    /// </summary>
    public static DspProcessorPreset? Preset(string? modelId) =>
        string.IsNullOrEmpty(modelId)
            ? null
            : PresetsById.GetValueOrDefault(modelId);

    /// <summary>
    /// The profile a stored one really means: a named model always answers with its
    /// preset, so a device whose properties are corrected in a later build corrects
    /// every project that named it rather than keeping the numbers a stale file
    /// happened to be saved with. Anything else is returned unchanged.
    /// </summary>
    public static DspProcessorProfile Resolve(DspProcessorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Preset(profile.ModelId)?.ToProfile() ?? profile;
    }
}
