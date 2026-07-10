namespace Resonalyze.Dsp;

/// <summary>
/// Registry of the available EQ profile formats. Callers build file-dialog filters
/// from <see cref="Importable"/> / <see cref="Exportable"/> and pick a format by its
/// index in that list.
/// </summary>
public static class EqProfileFormats
{
    public static IReadOnlyList<IEqProfileFormat> All { get; } = new IEqProfileFormat[]
    {
        new EqualizerApoFormat(),
        new RewFilterFormat(),
        new GenericCsvFormat(),
        new EasyEffectsFormat(),
        new CamillaDspYamlFormat(),
        // Biquad coefficients are rate-specific, and the two miniDSP families
        // process at different internal rates (2x4 at 48 kHz, HD/DDRC-class at
        // 96 kHz) — one dialog entry per rate, each labeled with it, so the
        // user picks the one matching the device instead of silently getting
        // 48 kHz coefficients.
        new MiniDspFormat(48_000),
        new MiniDspFormat(96_000),
        new GraphicEqFormat()
    };

    public static IReadOnlyList<IEqProfileFormat> Importable { get; } =
        All.Where(format => format.CanImport).ToArray();

    public static IReadOnlyList<IEqProfileFormat> Exportable { get; } =
        All.Where(format => format.CanExport).ToArray();
}
