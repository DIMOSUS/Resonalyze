using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Owns text PEQ profile I/O independently of file dialogs and WinForms controls.
/// </summary>
internal static class EqWizardProfileFileService
{
    public static EqualizationCurve Import(string fileName, IEqProfileFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(format);
        return format.Import(File.ReadAllText(fileName));
    }

    public static void Export(
        string fileName,
        IEqProfileFormat format,
        EqualizationCurve curve,
        double sampleRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(curve);
        if (!double.IsFinite(sampleRate) || sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        IEqProfileFormat effectiveFormat = format is GraphicEqFormat
            ? new GraphicEqFormat(sampleRate)
            : format;
        File.WriteAllText(fileName, effectiveFormat.Export(curve));
    }
}
