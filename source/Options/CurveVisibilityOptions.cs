using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>Presentation flags per FR-family mode, kept out of the DSP options; maps 1:1 onto the settings DTO.</summary>
public sealed class CurveVisibilityOptions
{
    public bool ShowPrimary { get; set; } = true;
    public bool ShowHd2 { get; set; } = true;
    public bool ShowHd3 { get; set; } = true;
    public bool ShowHd4 { get; set; } = true;
    public bool ShowThdPlusNoise { get; set; } = true;
    public bool ShowNoiseFloor { get; set; } = true;

    public bool ShowMeasuredPhase { get; set; } = true;
    public bool ShowMinimumPhase { get; set; } = true;
    public bool ShowExcessPhase { get; set; } = true;

    public bool ShowGroupDelay { get; set; } = true;
    public bool ShowMinimumPhaseGroupDelay { get; set; } = true;
    public bool ShowExcessGroupDelay { get; set; } = true;

    public bool ShowCoherence { get; set; } = true;

    // Off by default: shown only when the user asks.
    public bool ShowArrayAverage { get; set; }
    public bool ShowArrayMicrophones { get; set; }
    public bool ShowArraySpread { get; set; }

    internal CurveVisibilityOptions Copy() => (CurveVisibilityOptions)MemberwiseClone();

    public SpectrumCurves ToSpectrumCurves()
    {
        SpectrumCurves curves = SpectrumCurves.None;
        if (ShowPrimary)
        {
            curves |= SpectrumCurves.Primary;
        }
        if (ShowHd2)
        {
            curves |= SpectrumCurves.SecondHarmonic;
        }
        if (ShowHd3)
        {
            curves |= SpectrumCurves.ThirdHarmonic;
        }
        if (ShowHd4)
        {
            curves |= SpectrumCurves.FourthHarmonic;
        }
        if (ShowThdPlusNoise)
        {
            curves |= SpectrumCurves.ThdPlusNoise;
        }
        if (ShowNoiseFloor)
        {
            curves |= SpectrumCurves.NoiseFloor;
        }

        return curves;
    }
}
