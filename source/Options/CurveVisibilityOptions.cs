using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>
/// Per-mode curve visibility for the frequency-response family of analysis modes
/// (frequency response, phase, group delay). These are presentation choices, so
/// they live in the app rather than on the DSP <see cref="FrequencyResponseOptions"/>.
/// One instance is kept per mode, mirroring the one-options-object-per-mode pattern;
/// each mode reads only the flags it uses. The flat shape maps 1:1 onto the
/// persisted settings DTO so saving/loading stays byte-for-byte unchanged.
/// </summary>
public sealed class CurveVisibilityOptions
{
    // Frequency-response mode.
    public bool ShowPrimary { get; set; } = true;
    public bool ShowHd2 { get; set; } = true;
    public bool ShowHd3 { get; set; } = true;
    public bool ShowHd4 { get; set; } = true;
    public bool ShowThdPlusNoise { get; set; } = true;
    public bool ShowNoiseFloor { get; set; } = true;

    // Phase mode.
    public bool ShowMeasuredPhase { get; set; } = true;
    public bool ShowMinimumPhase { get; set; } = true;
    public bool ShowExcessPhase { get; set; } = true;

    // Group-delay mode.
    public bool ShowGroupDelay { get; set; } = true;

    // Shared coherence curve, shown in all three modes.
    public bool ShowCoherence { get; set; } = true;

    /// <summary>
    /// The frequency-response curves to compute, translated for the DSP layer.
    /// </summary>
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
