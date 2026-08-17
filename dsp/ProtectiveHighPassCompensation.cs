using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Removes a known protective high-pass from a measured transfer impulse
/// response. This is the frequency-domain equivalent of filtering the clean
/// loopback reference through the same high-pass before dividing microphone by
/// reference, while keeping the original full-band loopback available to the H1
/// estimator and its coherence calculation.
/// </summary>
public static class ProtectiveHighPassCompensation
{
    private const int PhaseRefreshInterval = 1_024;

    /// <summary>
    /// Returns a copy of <paramref name="impulseResponse"/> with the magnitude
    /// and phase of <paramref name="edge"/> divided out. Inverse gain is capped
    /// at <paramref name="maximumBoostDb"/> because a stop-band bin contains no
    /// recoverable loudspeaker information once the protection filter has buried
    /// it in the measurement noise.
    /// </summary>
    public static Complex[] RemoveFromImpulseResponse(
        IReadOnlyList<Complex> impulseResponse,
        CrossoverEdge edge,
        double sampleRateHz,
        double maximumBoostDb)
    {
        ArgumentNullException.ThrowIfNull(impulseResponse);
        if (impulseResponse.Count == 0)
        {
            throw new ArgumentException(
                "The impulse response must not be empty.",
                nameof(impulseResponse));
        }
        if (sampleRateHz <= 0 || !double.IsFinite(sampleRateHz))
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }
        if (maximumBoostDb < 0 || !double.IsFinite(maximumBoostDb))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBoostDb));
        }
        if (edge.Family is not (
            CrossoverFilterFamily.Butterworth or
            CrossoverFilterFamily.LinkwitzRiley))
        {
            throw new ArgumentOutOfRangeException(
                nameof(edge),
                "Protective high-pass compensation supports only Butterworth and Linkwitz-Riley filters.");
        }

        // BuildSections owns the shared validation of the corner and slope. Do
        // this once before the FFT loop, rather than rediscovering an invalid
        // setting independently at every bin through CrossoverFilter.Response.
        IReadOnlyList<BiquadCoefficients> sections =
            CrossoverFilter.BuildSections(edge, highPass: true, sampleRateHz);
        double maximumGain = Math.Pow(10.0, maximumBoostDb / 20.0);

        var spectrum = new Complex[impulseResponse.Count];
        for (int i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] = impulseResponse[i];
        }
        Fourier.Forward(spectrum, FourierOptions.Matlab);

        Complex binStep = Complex.Exp(
            new Complex(0.0, -Math.Tau / spectrum.Length));
        Complex z1 = Complex.One;
        for (int bin = 0; bin < spectrum.Length; bin++)
        {
            if (bin > 0)
            {
                // Step around the unit circle instead of evaluating one complex
                // exponential per section per bin. Periodic exact refreshes keep
                // the recurrence from drifting over multi-million-sample IRs.
                z1 = bin % PhaseRefreshInterval == 0
                    ? Complex.Exp(new Complex(
                        0.0,
                        -Math.Tau * bin / spectrum.Length))
                    : z1 * binStep;
            }

            Complex response = Response(sections, z1);
            spectrum[bin] *= CappedInverse(response, maximumGain);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return spectrum;
    }

    private static Complex Response(
        IReadOnlyList<BiquadCoefficients> sections,
        Complex z1)
    {
        Complex response = Complex.One;
        Complex z2 = z1 * z1;
        foreach (BiquadCoefficients section in sections)
        {
            response *= BiquadResponse.Evaluate(section, z1, z2);
        }

        return response;
    }

    private static Complex CappedInverse(Complex response, double maximumGain)
    {
        double magnitude = response.Magnitude;
        if (!(magnitude > 0) || !double.IsFinite(magnitude))
        {
            // A high-pass has an exact zero at DC. There is no phase or signal
            // there to recover, so keep that bin at zero instead of inventing a
            // maximum-gain DC component.
            return Complex.Zero;
        }

        double inverseMagnitude = Math.Min(1.0 / magnitude, maximumGain);
        return Complex.FromPolarCoordinates(inverseMagnitude, -response.Phase);
    }
}
