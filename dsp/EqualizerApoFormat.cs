namespace Resonalyze.Dsp;

public sealed class EqualizerApoFormat : IEqProfileFormat
{
    public string Name => "Equalizer APO";
    public string Extension => "txt";
    public bool CanImport => true;
    public bool CanExport => true;

    public bool SupportsAllPass(PeqBandType type) =>
        type == PeqBandType.AllPassSecondOrder;

    public string Export(EqualizationCurve curve) => PeqTextFile.Format(curve);

    public bool TryImport(string text, out EqualizationCurve curve) =>
        PeqTextFile.TryParse(text, out curve);
}
