namespace Resonalyze.Dsp;

/// <summary>The one place a <see cref="PeqBand"/> becomes coefficients (preview, Virtual DSP chain, exports).</summary>
public static class PeqBiquad
{
    /// <remarks>Undefined types realise as a bell and a degenerate all-pass as pass-through: files must not break the audio path.</remarks>
    public static BiquadCoefficients Compute(PeqBand band, double sampleRateHz)
    {
        if (band.Type.IsAllPass())
        {
            return band.IsTransparent
                ? new BiquadCoefficients(1, 0, 0, 0, 0)
                : AllPassFilter.BuildSections(ToAllPassSpec(band), sampleRateHz)[0];
        }

        return band.Type.IsShelving()
            ? ShelvingBiquad.Compute(band, sampleRateHz)
            : PeakingBiquad.Compute(band, sampleRateHz);
    }

    public static AllPassSpec ToAllPassSpec(PeqBand band)
    {
        if (!band.Type.IsAllPass())
        {
            throw new ArgumentException("The band is not an all-pass.", nameof(band));
        }

        return new AllPassSpec(
            band.Type == PeqBandType.AllPassFirstOrder
                ? AllPassType.FirstOrder
                : AllPassType.SecondOrder,
            band.FrequencyHz,
            band.Q);
    }
}
