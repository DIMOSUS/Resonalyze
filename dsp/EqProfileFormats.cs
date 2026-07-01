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
        new MiniDspFormat(),
        new GraphicEqFormat()
    };

    public static IReadOnlyList<IEqProfileFormat> Importable { get; } =
        All.Where(format => format.CanImport).ToArray();

    public static IReadOnlyList<IEqProfileFormat> Exportable { get; } =
        All.Where(format => format.CanExport).ToArray();
}
