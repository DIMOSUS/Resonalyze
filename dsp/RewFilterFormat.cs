using System.Text;

namespace Resonalyze.Dsp;

/// <summary>
/// REW "Filter Settings" text. The filter lines are identical to Equalizer APO
/// (Filter N: ON PK Fc ... Gain ... dB Q ...), so parsing is shared; export adds
/// REW's header and a preamp line so the value round-trips.
/// </summary>
public sealed class RewFilterFormat : IEqProfileFormat
{
    public string Name => "REW filter settings";
    public string Extension => "txt";
    public bool CanImport => true;
    public bool CanExport => true;

    public string Export(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        var builder = new StringBuilder();
        builder.AppendLine("Filter Settings file");
        builder.AppendLine();
        builder.AppendLine("Room EQ V5");
        builder.AppendLine();
        builder.AppendLine("Equaliser: Generic");
        builder.AppendLine(PeqTextFile.FormatPreampLine(curve.PreampDb));
        builder.Append(PeqTextFile.FormatFilters(curve));
        return builder.ToString();
    }

    public EqualizationCurve Import(string text) => PeqTextFile.Parse(text);
}
