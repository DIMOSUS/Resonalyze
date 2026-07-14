namespace Resonalyze.Dsp;

/// <summary>
/// Evaluates the actual RBJ digital PEQ realization used by Virtual DSP and
/// coefficient exports. Sample-rate-aware fitting and previews must use this
/// instead of the sample-rate-independent analog prototype.
/// </summary>
public static class DigitalEqualizationResponse
{
    public static double MagnitudeDbAt(
        EqualizationCurve curve,
        double frequencyHz,
        double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(curve);
        double total = curve.PreampDb;
        foreach (PeqBand band in curve.Bands)
        {
            total += MagnitudeDbAt(band, frequencyHz, sampleRateHz);
        }
        return total;
    }

    public static double MagnitudeDbAt(
        PeqBand band,
        double frequencyHz,
        double sampleRateHz)
    {
        if (band.IsTransparent || frequencyHz <= 0)
        {
            return 0;
        }
        if (!double.IsFinite(sampleRateHz) || sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }

        double magnitude = BiquadResponse.Evaluate(
            PeakingBiquad.Compute(band, sampleRateHz),
            frequencyHz,
            sampleRateHz).Magnitude;
        return 20.0 * Math.Log10(Math.Max(magnitude, double.Epsilon));
    }
}
