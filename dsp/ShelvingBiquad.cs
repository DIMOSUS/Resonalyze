namespace Resonalyze.Dsp;

/// <summary>RBJ shelving biquads in the same form as <see cref="PeakingBiquad"/>, parameterised by Q (what export targets read), not slope S.
/// Q = 1/sqrt(2) is the steepest monotonic shelf; higher Q overshoots.</summary>
public static class ShelvingBiquad
{
    /// <summary>A peaking band is rejected rather than silently shelved.</summary>
    public static BiquadCoefficients Compute(PeqBand band, double sampleRateHz)
    {
        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }

        bool low = band.Type switch
        {
            PeqBandType.LowShelf => true,
            PeqBandType.HighShelf => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(band),
                band.Type,
                "A shelving biquad needs a low- or high-shelf band.")
        };

        double a = Math.Pow(10.0, band.GainDb / 40.0);
        // Past Nyquist sin(w0) turns negative and a pole leaves the unit circle; the crossover and all-pass sections
        // clamp the same way.
        double w0 = 2.0 * Math.PI * BilinearTransform.ClampBelowNyquist(band.FrequencyHz, sampleRateHz) / sampleRateHz;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * band.Q);

        double aPlus = a + 1.0;
        double aMinus = a - 1.0;
        double knee = 2.0 * Math.Sqrt(a) * alpha;

        double b0;
        double b1;
        double b2;
        double a0;
        double a1;
        double a2;
        if (low)
        {
            b0 = a * (aPlus - aMinus * cos + knee);
            b1 = 2.0 * a * (aMinus - aPlus * cos);
            b2 = a * (aPlus - aMinus * cos - knee);
            a0 = aPlus + aMinus * cos + knee;
            a1 = -2.0 * (aMinus + aPlus * cos);
            a2 = aPlus + aMinus * cos - knee;
        }
        else
        {
            b0 = a * (aPlus + aMinus * cos + knee);
            b1 = -2.0 * a * (aMinus + aPlus * cos);
            b2 = a * (aPlus + aMinus * cos - knee);
            a0 = aPlus - aMinus * cos + knee;
            a1 = 2.0 * (aMinus - aPlus * cos);
            a2 = aPlus - aMinus * cos - knee;
        }

        // a1/a2 negated for the additive feedback form, as in PeakingBiquad.
        return new BiquadCoefficients(
            b0 / a0,
            b1 / a0,
            b2 / a0,
            -(a1 / a0),
            -(a2 / a0));
    }
}
