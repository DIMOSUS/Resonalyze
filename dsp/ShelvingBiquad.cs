namespace Resonalyze.Dsp;

/// <summary>
/// RBJ cookbook low/high shelving biquads, in the same normalised, feedback-negated
/// form as <see cref="PeakingBiquad"/> so the two are interchangeable everywhere a
/// band is realized.
/// </summary>
/// <remarks>
/// Parameterised by Q rather than by the cookbook's shelf slope S — the two are one
/// substitution (<c>1/Q = sqrt((A + 1/A)(1/S - 1) + 2)</c>), and Q is what the
/// devices this application exports to read: Equalizer APO's LSC/HSC, REW's shelf
/// filters and CamillaDSP's Lowshelf/Highshelf all take a shelf Q. Q = 1/sqrt(2)
/// is the steepest shelf that still rises monotonically; above that the response
/// overshoots the shelf before settling on it.
/// </remarks>
public static class ShelvingBiquad
{
    /// <summary>
    /// Coefficients for a shelving band at the given sample rate. The band's
    /// <see cref="PeqBand.Type"/> selects the direction; a peaking band is not a
    /// shelf and is rejected rather than silently shelved.
    /// </summary>
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
        double w0 = 2.0 * Math.PI * band.FrequencyHz / sampleRateHz;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / (2.0 * band.Q);

        double aPlus = a + 1.0;
        double aMinus = a - 1.0;
        // The one term that carries the shelf's knee; everything else is symmetric
        // between the two directions and only the sign of the cosine terms flips.
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

        // Normalised to a0 = 1, with a1/a2 negated for the additive feedback form
        // the exports use — the same convention as PeakingBiquad.
        return new BiquadCoefficients(
            b0 / a0,
            b1 / a0,
            b2 / a0,
            -(a1 / a0),
            -(a2 / a0));
    }
}
