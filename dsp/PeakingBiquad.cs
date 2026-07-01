namespace Resonalyze.Dsp;

/// <summary>
/// Normalised biquad coefficients (a0 = 1) in the sign convention used by miniDSP
/// advanced-biquad text: y[n] = b0 x[n] + b1 x[n-1] + b2 x[n-2] + a1 y[n-1] + a2 y[n-2].
/// </summary>
public readonly record struct BiquadCoefficients(
    double B0,
    double B1,
    double B2,
    double A1,
    double A2);

public static class PeakingBiquad
{
    /// <summary>
    /// RBJ cookbook peaking-EQ biquad for a band at the given sample rate. The
    /// returned a1/a2 are already negated for miniDSP's additive feedback form.
    /// </summary>
    public static BiquadCoefficients Compute(PeqBand band, double sampleRateHz)
    {
        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }

        double a = Math.Pow(10.0, band.GainDb / 40.0);
        double w0 = 2.0 * Math.PI * band.FrequencyHz / sampleRateHz;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * band.Q);

        double a0 = 1.0 + alpha / a;
        double b0 = (1.0 + alpha * a) / a0;
        double b1 = (-2.0 * cos) / a0;
        double b2 = (1.0 - alpha * a) / a0;
        double a1 = (-2.0 * cos) / a0;
        double a2 = (1.0 - alpha / a) / a0;

        // miniDSP subtracts the feedback terms, so the file stores negated a1/a2.
        return new BiquadCoefficients(b0, b1, b2, -a1, -a2);
    }
}
