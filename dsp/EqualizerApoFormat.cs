namespace Resonalyze.Dsp;

/// <summary>Equalizer APO config text (also produced by many REW/AutoEQ exports).</summary>
public sealed class EqualizerApoFormat : IEqProfileFormat
{
    public string Name => "Equalizer APO";
    public string Extension => "txt";
    public bool CanImport => true;
    public bool CanExport => true;

    // APO's AP filter is second-order only; there is no first-order all-pass to
    // write one as.
    public bool SupportsAllPass(PeqBandType type) =>
        type == PeqBandType.AllPassSecondOrder;

    public string Export(EqualizationCurve curve) => PeqTextFile.Format(curve);

    public bool TryImport(string text, out EqualizationCurve curve) =>
        PeqTextFile.TryParse(text, out curve);
}
