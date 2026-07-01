namespace Resonalyze.Dsp;

/// <summary>Equalizer APO config text (also produced by many REW/AutoEQ exports).</summary>
public sealed class EqualizerApoFormat : IEqProfileFormat
{
    public string Name => "Equalizer APO";
    public string Extension => "txt";
    public bool CanImport => true;
    public bool CanExport => true;

    public string Export(EqualizationCurve curve) => PeqTextFile.Format(curve);

    public EqualizationCurve Import(string text) => PeqTextFile.Parse(text);
}
