namespace Resonalyze.Dsp;

public static class EqProfileFormats
{
    public static IReadOnlyList<IEqProfileFormat> All { get; } = new IEqProfileFormat[]
    {
        new EqualizerApoFormat(),
        new RewFilterFormat(),
        new GenericCsvFormat(),
        new EasyEffectsFormat(),
        new CamillaDspYamlFormat(),
        new AudiotecFischerFormat(),
        // Coefficients are rate-specific, so one labelled entry per device rate (car DSPs 44.1k, miniDSP 2x4 48k, HD 96k, C-DSP 8x12 192k).
        new MiniDspFormat(44_100),
        new MiniDspFormat(48_000),
        new MiniDspFormat(96_000),
        new MiniDspFormat(192_000),
        new GraphicEqFormat()
    };

    public static IReadOnlyList<IEqProfileFormat> Importable { get; } =
        All.Where(format => format.CanImport).ToArray();

    public static IReadOnlyList<IEqProfileFormat> Exportable { get; } =
        All.Where(format => format.CanExport).ToArray();
}
