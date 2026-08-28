using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public sealed record ProtectiveHighPassCompensationResult(
    Complex[] ImpulseResponse,
    double[] Reliability)
{
    /// <summary>
    /// Applies the compensation-validity mask to an existing coherence estimate.
    /// The transfer estimator already folds its excitation validity into coherence;
    /// this adds the corresponding target-side validity after the known high-pass.
    /// </summary>
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
    private const double ReliabilityFadeWidthDb = 6.0;

    /// <summary>
    /// Returns a copy of <paramref name="impulseResponse"/> with the magnitude
    /// and phase of <paramref name="edge"/> divided out, plus the per-bin
    /// reliability of that inversion. Full trust ends 6 dB before
    /// <paramref name="maximumBoostDb"/>; a raised-cosine fade reaches zero at
    /// the limit. Unrecoverable bins are suppressed by a smooth frequency mask
    /// derived only from that known-filter reliability; measured coherence is
    /// deliberately not punched into the IR bin by bin.
    /// </summary>
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

        var reliability = new double[spectrum.Length / 2 + 1];
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

    /// <summary>
    /// The same compensation as a per-frequency dB correction, for a curve that is
    /// only ever a magnitude: the dB to ADD at each frequency, or NaN where the
    /// filter has taken the signal below what <paramref name="maximumBoostDb"/>
    /// allows recovering.
    /// </summary>
    /// <remarks>
    /// A reference-free capture CARRIES the protective high-pass, because the filter
    /// sits in the hardware ahead of the loudspeaker and there is no loopback to
    /// divide it out with. A swept impulse response has it removed by
    /// <see cref="RemoveFromImpulseResponse"/>. Compared against each other without
    /// this, the two measurements of one tweeter sit a whole filter slope apart —
    /// 28 dB at 900 Hz under a 2 kHz / 24 dB per octave corner — which is exactly
    /// the smooth, plausible discrepancy a spatial average must not carry.
    /// <para>
    /// Deliberately the same edge, the same cap and the same raised-cosine fade as
    /// the impulse-response path: the two corrections have to agree bin for bin, or
    /// the curves they produce cannot be compared, which is the only reason either
    /// exists. NaN rather than a very negative level where the fade reaches zero —
    /// there is nothing to recover there, and a plotted −900 dB is a lie a break in
    /// the curve is not.
    /// </para>
    /// </remarks>
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
        // The same families the impulse-response path accepts, refused the same way.
        // The promise above is that the two corrections agree bin for bin, and they do
        // only for a MONOTONIC high-pass: where a rippled passband puts |H| above one,
        // this path floors its correction at zero while CappedInverse attenuates, so
        // the two measurements of one driver would part by the ripple depth — the
        // smooth, plausible discrepancy both methods exist to remove.
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

    /// <summary>
    /// The lowest frequency this compensation can speak about: below it the
    /// high-pass has taken the signal past <paramref name="maximumBoostDb"/> and
    /// there is nothing left to recover. Zero when the whole band survives.
    /// </summary>
    /// <remarks>
    /// The same question <see cref="MagnitudeCorrectionDb"/> answers per frequency,
    /// as the single number a half-line actually is — a high-pass takes everything
    /// below one frequency and nothing above it. It exists because the two paths
    /// end differently: a magnitude curve can carry NaN and say "nothing here", but
    /// an impulse response is a time series and cannot, so
    /// <see cref="RemoveFromImpulseResponse"/> zeroes those bins instead. A gated
    /// spectrum of that response then fills them back in with the analysis window's
    /// own leakage — measured 270 dB above the truth on a 1 kHz / 48 dB per octave
    /// corner, and drawn as a smooth, entirely plausible driver rolloff. Whoever
    /// draws such a curve needs this frequency to break it at.
    /// <para>
    /// Found by bisection on the same sections and the same reliability rule as
    /// both corrections, rather than from the analogue asymptote, so the three can
    /// never disagree about where the signal ended.
    /// </para>
    /// </remarks>
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
            // A cap of zero on a filter that never quite reaches unity gain. Nothing
            // is recoverable, and saying so beats returning a frequency that implies
            // the top of the band is.
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
            // A high-pass has an exact zero at DC. There is no phase or signal
            // there to recover, so keep that bin at zero instead of inventing a
            // maximum-gain DC component.
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
