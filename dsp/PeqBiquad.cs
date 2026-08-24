namespace Resonalyze.Dsp;

/// <summary>
/// The one place a <see cref="PeqBand"/> becomes coefficients, whatever its
/// <see cref="PeqBandType"/>. Everything that realizes a band — the response
/// preview, the Virtual DSP chain and the coefficient exports — goes through here,
/// so a new band shape is added in one place instead of three.
/// </summary>
public static class PeqBiquad
{
    /// <remarks>
    /// A band whose type is neither a shelf nor an all-pass is realized as a bell,
    /// which includes a value no enum member matches: a hand-edited project or
    /// settings file must degrade to the default shape, not throw out of the audio
    /// path. The same contract holds for a degenerate all-pass (non-positive
    /// frequency or Q), which degrades to a pass-through.
    /// </remarks>
    public static BiquadCoefficients Compute(PeqBand band, double sampleRateHz)
    {
        if (band.Type.IsAllPass())
        {
            // Reuses the all-pass stage's realization — including its Nyquist clamp,
            // which the bell path does not apply. The type is never Off here, so the
            // section list holds exactly one entry.
            return band.IsTransparent
                ? new BiquadCoefficients(1, 0, 0, 0, 0)
                : AllPassFilter.BuildSections(ToAllPassSpec(band), sampleRateHz)[0];
        }

        return band.Type.IsShelving()
            ? ShelvingBiquad.Compute(band, sampleRateHz)
            : PeakingBiquad.Compute(band, sampleRateHz);
    }

    /// <summary>
    /// Restates an all-pass band as the <see cref="AllPassSpec"/> the filter and
    /// its group-delay readouts (<see cref="AllPassFilter.CornerGroupDelaySeconds"/>)
    /// take. Only valid for a band whose type <see cref="PeqBandTypes.IsAllPass"/>.
    /// </summary>
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
