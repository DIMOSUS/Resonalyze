using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Computes analytic-signal envelopes via the Hilbert transform.
/// </summary>
public static class SignalEnvelope
{
    public static double FindFractionalPeakOffset(double previous, double center, double next)
    {
        double denominator = previous - 2.0 * center + next;
        if (Math.Abs(denominator) < 1e-12)
        {
            return 0.0;
        }

        double offset = 0.5 * (previous - next) / denominator;
        return Math.Clamp(offset, -0.5, 0.5);
    }

    /// <summary>
    /// Computes the magnitude envelope of a real-valued signal.
    /// </summary>
    /// <param name="signal">Input samples.</param>
    /// <returns>Envelope samples with the same length as <paramref name="signal"/>.</returns>
    public static double[] Envelope(IReadOnlyList<double> signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.Count == 0)
        {
            throw new ArgumentException(
                "Signal must not be empty.",
                nameof(signal));
        }

        int length = signal.Count;
        var spectrum = new Complex[length];

        for (int i = 0; i < length; i++)
        {
            spectrum[i] = new Complex(signal[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        if ((length & 1) == 0)
        {
            for (int bin = 1; bin < length / 2; bin++)
            {
                spectrum[bin] *= 2.0;
            }

            for (int bin = length / 2 + 1; bin < length; bin++)
            {
                spectrum[bin] = Complex.Zero;
            }
        }
        else
        {
            for (int bin = 1; bin <= (length - 1) / 2; bin++)
            {
                spectrum[bin] *= 2.0;
            }

            for (int bin = (length + 1) / 2; bin < length; bin++)
            {
                spectrum[bin] = Complex.Zero;
            }
        }

        Fourier.Inverse(spectrum, FourierOptions.Matlab);

        var envelope = new double[length];
        for (int i = 0; i < length; i++)
        {
            envelope[i] = spectrum[i].Magnitude;
        }

        return envelope;
    }

    public static PeakSearchResult FindPeak(
        IReadOnlyList<double> envelope,
        int sampleRate,
        PeakSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(options);
        if (envelope.Count == 0)
        {
            throw new ArgumentException(
                "Envelope must not be empty.",
                nameof(envelope));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        (int strongestIndex, double strongestPeak) = FindStrongestPeak(
            envelope,
            sampleRate,
            options.SearchWindowMilliseconds);
        if (options.Mode == PeakSearchMode.StrongestPeak)
        {
            return new PeakSearchResult(
                strongestIndex,
                strongestIndex,
                strongestPeak,
                false);
        }

        int searchEnd = GetSearchEndIndex(
            envelope.Count,
            sampleRate,
            options.SearchWindowMilliseconds);
        double noiseRms = EstimateEnvelopeNoiseRms(envelope);
        double thresholdFromMax = strongestPeak *
            Math.Pow(10.0, -Math.Abs(options.FirstPeakThresholdBelowMaxDb) / 20.0);
        double thresholdFromNoise = noiseRms *
            Math.Pow(10.0, Math.Max(0, options.FirstPeakMinimumSnrDb) / 20.0);
        double threshold = Math.Max(thresholdFromMax, thresholdFromNoise);

        var candidates = new List<int>();
        for (int i = 1; i < searchEnd - 1; i++)
        {
            if (envelope[i] >= threshold &&
                envelope[i] >= envelope[i - 1] &&
                envelope[i] >= envelope[i + 1])
            {
                candidates.Add(i);
            }
        }

        int firstArrivalIndex = EliminatePreRingingSidelobes(
            envelope,
            candidates,
            options.AnalysisKernelEnvelope,
            threshold,
            strongestPeak);
        if (firstArrivalIndex >= 0)
        {
            return new PeakSearchResult(
                firstArrivalIndex,
                strongestIndex,
                strongestPeak,
                false);
        }

        return new PeakSearchResult(
            strongestIndex,
            strongestIndex,
            strongestPeak,
            true);
    }

    public static double EstimatePeakConfidenceDecibels(
        IReadOnlyList<double> envelope,
        double peak)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        double noiseRms = EstimateEnvelopeNoiseRms(envelope);
        return DataHelper.AmplitudeToDecibels(peak / Math.Max(noiseRms, 1e-12));
    }

    // The analysis chain ahead of the peak search is zero-phase (the bandpass
    // window and the discrete Hilbert transform's own 1/t skirt), so every
    // arrival drags an exactly symmetric train of pre-ringing lobes in front of
    // it. The stronger ones clear the first-arrival threshold and used to read
    // as earlier "arrivals" milliseconds before the true wavefront — the cleaner
    // the measurement, the more of them survived the noise gate. The kernel that
    // makes the ringing is known, so a candidate is tested against physics, not
    // heuristics: an arrival of height H can produce at offset d a lobe no
    // higher than H times the kernel envelope at d. A candidate above that
    // ceiling (with a 6 dB superposition margin) cannot be pre-ring and is a
    // genuine arrival; a candidate at or below it is corroborated by symmetry —
    // an exactly even kernel puts an equal lobe at the mirrored position after
    // the peak, and decay/reflections only add energy on the late side, so the
    // mirror cannot hide a lobe. Level-and-mirror together keep genuine early
    // arrivals in reverberant rooms (their level exceeds the kernel ceiling at
    // their distance) while rejecting the kernel's own ring exactly.
    private const double SidelobeLevelMarginRatio = 2.0;
    private const double SidelobeSymmetryRatio = 0.5;
    private const int SidelobeMirrorNeighborhood = 2;

    // Walks the threshold-passing local maxima from the latest to the earliest,
    // dropping every candidate that reads as a pre-ringing sidelobe of an
    // already-accepted later peak — a weak first arrival is itself a sidelobe
    // reference, so its own pre-ring cannot masquerade as an even earlier
    // arrival. Returns the earliest surviving candidate, or -1 when none pass.
    private static int EliminatePreRingingSidelobes(
        IReadOnlyList<double> envelope,
        IReadOnlyList<int> candidates,
        IReadOnlyList<double>? kernelEnvelope,
        double threshold,
        double strongestPeak)
    {
        // Beyond this offset not even the strongest peak can ring above the
        // candidate threshold, so no threshold-passing candidate can be anyone's
        // sidelobe there — it bounds the peak-comparison loop.
        int ringReachLimit = 0;
        int reachCap = envelope.Count / 2;
        for (int d = 1; d <= reachCap; d++)
        {
            if (strongestPeak * KernelRingLevel(kernelEnvelope, d) *
                SidelobeLevelMarginRatio >= threshold)
            {
                ringReachLimit = d;
            }
        }

        var accepted = new List<int>();
        int firstArrival = -1;
        for (int k = candidates.Count - 1; k >= 0; k--)
        {
            int candidate = candidates[k];
            bool isSidelobe = false;
            for (int a = accepted.Count - 1; a >= 0; a--)
            {
                int peakIndex = accepted[a];
                if (peakIndex - candidate > ringReachLimit)
                {
                    break;
                }

                if (IsPreRingingSidelobeOf(
                        envelope, candidate, peakIndex, kernelEnvelope))
                {
                    isSidelobe = true;
                    break;
                }
            }

            if (!isSidelobe)
            {
                accepted.Add(candidate);
                firstArrival = candidate;
            }
        }

        return firstArrival;
    }

    // The analysis kernel's envelope level at |offset| samples from its centre,
    // relative to the centre peak. With no explicit kernel the only zero-phase
    // ringing left is the discrete Hilbert transform's skirt, whose envelope
    // pedestal is 2/(pi*n) — the delta worst case; smoother arrivals ring less.
    private static double KernelRingLevel(
        IReadOnlyList<double>? kernelEnvelope,
        int offset)
    {
        if (kernelEnvelope == null || kernelEnvelope.Count == 0)
        {
            return Math.Min(1.0, 2.0 / (Math.PI * Math.Max(1, offset)));
        }

        if (offset >= kernelEnvelope.Count || kernelEnvelope[0] <= 0.0)
        {
            return 0.0;
        }

        return kernelEnvelope[offset] / kernelEnvelope[0];
    }

    private static bool IsPreRingingSidelobeOf(
        IReadOnlyList<double> envelope,
        int candidateIndex,
        int peakIndex,
        IReadOnlyList<double>? kernelEnvelope)
    {
        // An arrival of this peak's height cannot ring louder than its kernel
        // envelope allows at this distance; a candidate above that ceiling is a
        // genuine arrival, however hot the mirror side is.
        int distance = peakIndex - candidateIndex;
        double ringCeiling = envelope[peakIndex] *
            KernelRingLevel(kernelEnvelope, distance) *
            SidelobeLevelMarginRatio;
        if (envelope[candidateIndex] > ringCeiling)
        {
            return false;
        }

        // The peak's integer index is up to half a sample off the true lobe
        // centre, so read the mirror as the maximum over a small neighbourhood —
        // a deep null one sample off the exact mirror must not disguise a
        // sidelobe as a genuine arrival. Clamp the neighbourhood so it never
        // touches the peak's own lobe.
        int neighborhood = Math.Min(SidelobeMirrorNeighborhood, distance - 1);
        int mirrorIndex = 2 * peakIndex - candidateIndex;
        double mirrorLevel = 0.0;
        int first = Math.Max(0, mirrorIndex - neighborhood);
        int last = Math.Min(envelope.Count - 1, mirrorIndex + neighborhood);
        for (int i = first; i <= last; i++)
        {
            mirrorLevel = Math.Max(mirrorLevel, envelope[i]);
        }

        return mirrorLevel >= envelope[candidateIndex] * SidelobeSymmetryRatio;
    }

    private static (int Index, double Peak) FindStrongestPeak(
        IReadOnlyList<double> envelope,
        int sampleRate,
        double searchWindowMilliseconds)
    {
        int searchEnd = GetSearchEndIndex(envelope.Count, sampleRate, searchWindowMilliseconds);
        double peak = 0;
        int peakIndex = 0;
        for (int i = 0; i < searchEnd; i++)
        {
            if (envelope[i] > peak)
            {
                peak = envelope[i];
                peakIndex = i;
            }
        }

        return (peakIndex, peak);
    }

    private static int GetSearchEndIndex(
        int envelopeLength,
        int sampleRate,
        double searchWindowMilliseconds)
    {
        int requestedSamples = (int)Math.Round(
            Math.Max(1, searchWindowMilliseconds) * sampleRate / 1000.0);
        // The floor of 3 exists for the parabolic refinement, but it must never
        // exceed the envelope itself — a 1–2 sample input would otherwise be
        // read past its end.
        int cap = Math.Min(envelopeLength, Math.Max(3, envelopeLength / 2));
        return Math.Clamp(requestedSamples, Math.Min(3, cap), cap);
    }

    // The fraction of the envelope (its quietest samples) the noise-floor
    // estimate averages over.
    private const double NoiseFloorQuantile = 0.25;

    // Noise floor as the RMS of the quietest quarter of the envelope. An
    // acoustic IR's remainder is NOT noise — it is reflections, modal decay and
    // driver ringing — so a mean over everything-but-the-peak (the previous
    // estimate) read reverberation as noise: it misgraded clean reverberant
    // recordings and, worse, inflated the noise-based first-arrival threshold
    // until a genuine weak direct sound was cut out of the candidate list. The
    // quietest-quantile RMS reads the true floor as long as decay and arrivals
    // occupy less than three quarters of the record, which holds for any IR
    // with usable headroom around its reverb tail.
    private static double EstimateEnvelopeNoiseRms(IReadOnlyList<double> envelope)
    {
        var sorted = new double[envelope.Count];
        for (int i = 0; i < sorted.Length; i++)
        {
            sorted[i] = envelope[i];
        }
        Array.Sort(sorted);

        int count = Math.Max(1, (int)(sorted.Length * NoiseFloorQuantile));
        double sumSquares = 0;
        for (int i = 0; i < count; i++)
        {
            sumSquares += sorted[i] * sorted[i];
        }

        return Math.Sqrt(sumSquares / count);
    }
}

public enum PeakSearchMode
{
    FirstArrival,
    StrongestPeak
}

public sealed class PeakSearchOptions
{
    public PeakSearchMode Mode { get; init; } = PeakSearchMode.FirstArrival;
    public double FirstPeakThresholdBelowMaxDb { get; init; } = 25;
    public double FirstPeakMinimumSnrDb { get; init; } = 12;
    public double SearchWindowMilliseconds { get; init; } = 80;

    /// <summary>
    /// Envelope of the zero-phase analysis kernel that filtered the signal
    /// (e.g. the bandpass window's time response), indexed by |offset| in
    /// samples from the kernel centre; entry 0 is the kernel peak and the scale
    /// is arbitrary. The first-arrival search uses it as the exact ceiling of
    /// pre-ringing sidelobe levels at each distance. Null when the signal was
    /// not filtered — only the Hilbert transform's own skirt is assumed then.
    /// </summary>
    public IReadOnlyList<double>? AnalysisKernelEnvelope { get; init; }
}

public readonly record struct PeakSearchResult(
    int SelectedIndex,
    int StrongestIndex,
    double StrongestPeak,
    bool FallbackUsed);
