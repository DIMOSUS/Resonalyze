using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public sealed record ProtectiveHighPassCompensationResult(
    Complex[] ImpulseResponse,
    double[] Reliability)
{
    /// <summary>Adds the target-side validity after the known high-pass (excitation validity is already folded in).</summary>
    public double[]? MaskCoherence(IReadOnlyList<double>? coherence)
    {
        if (coherence == null)
        {
            return null;
        }
        if (coherence.Count != Reliability.Length)
        {
            throw new ArgumentException(
                "Coherence and compensation reliability must use the same frequency grid.",
                nameof(coherence));
        }

        var masked = new double[coherence.Count];
        for (int i = 0; i < masked.Length; i++)
        {
            masked[i] = coherence[i] * Reliability[i];
        }

        return masked;
    }
}

/// <summary>Removes a known protective high-pass from a transfer IR: equivalent to filtering the loopback reference first,
/// while the full-band loopback stays available to H1 and coherence.</summary>
public static class ProtectiveHighPassCompensation
{
    private const int PhaseRefreshInterval = 1_024;
    private const double ReliabilityFadeWidthDb = 6.0;

    /// <summary>Divides out the edge with per-bin reliability: full trust ends 6 dB before <paramref name="maximumBoostDb"/>, raised-cosine to zero at it.
    /// The mask comes only from filter reliability, never from measured coherence.</summary>
    public static ProtectiveHighPassCompensationResult RemoveFromImpulseResponse(
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

        IReadOnlyList<BiquadCoefficients> sections =
            CrossoverFilter.BuildSections(edge, highPass: true, sampleRateHz);
        double maximumGain = Math.Pow(10.0, maximumBoostDb / 20.0);

        var spectrum = new Complex[impulseResponse.Count];
        for (int i = 0; i < spectrum.Length; i++)
        {
            spectrum[i] = impulseResponse[i];
        }
        Fourier.Forward(spectrum, FourierOptions.Matlab);

        var reliability = new double[spectrum.Length / 2 + 1];
        Complex binStep = Complex.Exp(
            new Complex(0.0, -Math.Tau / spectrum.Length));
        Complex z1 = Complex.One;
        for (int bin = 0; bin < spectrum.Length; bin++)
        {
            if (bin > 0)
            {
                // Periodic exact refresh keeps the unit-circle recurrence from drifting on multi-million-sample IRs.
                z1 = bin % PhaseRefreshInterval == 0
                    ? Complex.Exp(new Complex(
                        0.0,
                        -Math.Tau * bin / spectrum.Length))
                    : z1 * binStep;
            }

            Complex response = Response(sections, z1);
            int foldedBin = Math.Min(bin, spectrum.Length - bin);
            double reliabilityWeight;
            if (bin <= spectrum.Length / 2)
            {
                reliabilityWeight = ReliabilityWeight(
                    response.Magnitude,
                    maximumBoostDb);
                reliability[foldedBin] = reliabilityWeight;
            }
            else
            {
                reliabilityWeight = reliability[foldedBin];
            }

            spectrum[bin] *= reliabilityWeight * CappedInverse(response, maximumGain);
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);
        return new ProtectiveHighPassCompensationResult(spectrum, reliability);
    }

    /// <summary>The same compensation as dB to add per frequency (NaN where unrecoverable), for reference-free captures that still carry the filter.
    /// Must match <see cref="RemoveFromImpulseResponse"/> bin for bin (same edge, cap and fade) or the two measurements differ by a slope.</summary>
    public static double[] MagnitudeCorrectionDb(
        CrossoverEdge edge,
        double sampleRateHz,
        double maximumBoostDb,
        IReadOnlyList<double> frequenciesHz)
    {
        ArgumentNullException.ThrowIfNull(frequenciesHz);
        if (sampleRateHz <= 0 || !double.IsFinite(sampleRateHz))
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }
        if (maximumBoostDb < 0 || !double.IsFinite(maximumBoostDb))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBoostDb));
        }
        // Monotonic high-passes only: a rippled |H| > 1 would make this path and CappedInverse disagree by the ripple depth.
        if (edge.Family is not (
            CrossoverFilterFamily.Butterworth or
            CrossoverFilterFamily.LinkwitzRiley))
        {
            throw new ArgumentOutOfRangeException(
                nameof(edge),
                "Protective high-pass compensation supports only Butterworth and Linkwitz-Riley filters.");
        }

        IReadOnlyList<BiquadCoefficients> sections =
            CrossoverFilter.BuildSections(edge, highPass: true, sampleRateHz);
        var correction = new double[frequenciesHz.Count];
        for (int i = 0; i < correction.Length; i++)
        {
            double frequency = frequenciesHz[i];
            if (!(frequency > 0))
            {
                correction[i] = double.NaN;
                continue;
            }

            Complex z1 = Complex.FromPolarCoordinates(
                1.0, -Math.Tau * frequency / sampleRateHz);
            double magnitude = Response(sections, z1).Magnitude;
            double weight = ReliabilityWeight(magnitude, maximumBoostDb);
            if (weight <= 0.0)
            {
                correction[i] = double.NaN;
                continue;
            }

            double requiredBoostDb = magnitude > 0.0
                ? Math.Max(0.0, -20.0 * Math.Log10(magnitude))
                : maximumBoostDb;
            correction[i] =
                Math.Min(requiredBoostDb, maximumBoostDb) + 20.0 * Math.Log10(weight);
        }

        return correction;
    }

    /// <summary>Below this the signal is unrecoverable; zero when the whole band survives. Needed because the IR path zeroes those bins,
    /// and a gated spectrum refills them with window leakage (270 dB above truth) that looks like a plausible rolloff.</summary>
    public static double LowestRecoverableFrequencyHz(
        CrossoverEdge edge,
        double sampleRateHz,
        double maximumBoostDb)
    {
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

        IReadOnlyList<BiquadCoefficients> sections =
            CrossoverFilter.BuildSections(edge, highPass: true, sampleRateHz);
        double nyquist = sampleRateHz / 2.0;
        if (!Recoverable(sections, nyquist, sampleRateHz, maximumBoostDb))
        {
            // Cap of zero on a filter never reaching unity: nothing is recoverable.
            return nyquist;
        }

        double low = 0.0;
        double high = nyquist;
        for (int step = 0; step < 64; step++)
        {
            double middle = 0.5 * (low + high);
            if (Recoverable(sections, middle, sampleRateHz, maximumBoostDb))
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return high;
    }

    private static bool Recoverable(
        IReadOnlyList<BiquadCoefficients> sections,
        double frequencyHz,
        double sampleRateHz,
        double maximumBoostDb)
    {
        Complex z1 = Complex.FromPolarCoordinates(
            1.0, -Math.Tau * frequencyHz / sampleRateHz);
        return ReliabilityWeight(Response(sections, z1).Magnitude, maximumBoostDb) > 0.0;
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
            return Complex.Zero;
        }

        double inverseMagnitude = Math.Min(1.0 / magnitude, maximumGain);
        return Complex.FromPolarCoordinates(inverseMagnitude, -response.Phase);
    }

    private static double ReliabilityWeight(double magnitude, double maximumBoostDb)
    {
        if (!(magnitude > 0.0) || !double.IsFinite(magnitude))
        {
            return 0.0;
        }

        double requiredBoostDb = Math.Max(0.0, -20.0 * Math.Log10(magnitude));
        double fullTrustBoostDb = Math.Max(
            0.0,
            maximumBoostDb - ReliabilityFadeWidthDb);
        if (requiredBoostDb <= fullTrustBoostDb)
        {
            return 1.0;
        }
        if (requiredBoostDb >= maximumBoostDb)
        {
            return 0.0;
        }

        return 1.0 - DspMath.RaisedCosineGate(
            requiredBoostDb,
            fullTrustBoostDb,
            maximumBoostDb);
    }
}
