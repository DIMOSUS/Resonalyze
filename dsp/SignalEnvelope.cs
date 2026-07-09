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
        double noiseRms = EstimateEnvelopeNoiseRms(envelope, strongestIndex);
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

        int firstArrivalIndex = EliminatePreRingingSidelobes(envelope, candidates);
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
        int peakIndex,
        double peak)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        double noiseRms = EstimateEnvelopeNoiseRms(envelope, peakIndex);
        return DataHelper.AmplitudeToDecibels(peak / Math.Max(noiseRms, 1e-12));
    }

    // The whole analysis chain ahead of the peak search is zero-phase (the
    // bandpass window, the band limits of the sweep deconvolution, and the
    // discrete Hilbert transform's own 1/t skirt), so every arrival drags an
    // exactly symmetric train of pre-ringing lobes in front of it. The stronger
    // ones clear the first-arrival threshold and used to read as earlier
    // "arrivals" milliseconds before the true wavefront — the cleaner the
    // measurement, the more of them survived the noise gate. Three checks
    // identify such a lobe against a later arrival without knowing the bandwidth
    // that made it: it sits within a few main-lobe widths of that arrival, it is
    // well below its peak (a first sidelobe is >= ~13 dB down for any window;
    // 6 dB is a safe gate), and — because a zero-phase kernel is exactly even —
    // it has a counterpart at the mirrored position after the peak at a
    // comparable level. Room decay and reflections only add energy on the late
    // side, so the mirror test cannot hide a lobe; a genuine earlier arrival is
    // kept because its mirror lands in the later arrival's own low tail. The
    // price is honest: a real arrival closer to a >= 6 dB stronger one than a
    // few lobe widths is below the resolution of that bandwidth and resolves to
    // the stronger peak.
    private const int SidelobeZoneLobeWidths = 5;
    private const double SidelobeDominanceRatio = 2.0;
    private const double SidelobeSymmetryRatio = 0.5;
    private const int SidelobeMirrorNeighborhood = 2;

    // Walks the threshold-passing local maxima from the latest to the earliest,
    // dropping every candidate that reads as a pre-ringing sidelobe of an
    // already-accepted later peak — a weak first arrival is itself a sidelobe
    // reference, so its own pre-ring cannot masquerade as an even earlier
    // arrival. Returns the earliest surviving candidate, or -1 when none pass.
    private static int EliminatePreRingingSidelobes(
        IReadOnlyList<double> envelope,
        IReadOnlyList<int> candidates)
    {
        var accepted = new List<(int Index, int Zone)>();
        int maxZone = 0;
        int firstArrival = -1;
        for (int k = candidates.Count - 1; k >= 0; k--)
        {
            int candidate = candidates[k];
            bool isSidelobe = false;
            for (int a = accepted.Count - 1; a >= 0; a--)
            {
                (int peakIndex, int zone) = accepted[a];
                int distance = peakIndex - candidate;
                if (distance > maxZone)
                {
                    break;
                }

                if (distance <= zone &&
                    IsPreRingingSidelobeOf(envelope, candidate, peakIndex))
                {
                    isSidelobe = true;
                    break;
                }
            }

            if (isSidelobe)
            {
                continue;
            }

            int candidateZone = SidelobeZoneLobeWidths *
                MeasureHalfHeightLobeWidth(envelope, candidate, envelope[candidate]);
            accepted.Add((candidate, candidateZone));
            maxZone = Math.Max(maxZone, candidateZone);
            firstArrival = candidate;
        }

        return firstArrival;
    }

    private static bool IsPreRingingSidelobeOf(
        IReadOnlyList<double> envelope,
        int candidateIndex,
        int peakIndex)
    {
        if (envelope[peakIndex] < envelope[candidateIndex] * SidelobeDominanceRatio)
        {
            return false;
        }

        // The peak's integer index is up to half a sample off the true lobe
        // centre, so read the mirror as the maximum over a small neighbourhood —
        // a deep null one sample off the exact mirror must not disguise a
        // sidelobe as a genuine arrival.
        int mirrorIndex = 2 * peakIndex - candidateIndex;
        double mirrorLevel = 0.0;
        int first = Math.Max(0, mirrorIndex - SidelobeMirrorNeighborhood);
        int last = Math.Min(
            envelope.Count - 1,
            mirrorIndex + SidelobeMirrorNeighborhood);
        for (int i = first; i <= last; i++)
        {
            mirrorLevel = Math.Max(mirrorLevel, envelope[i]);
        }

        return mirrorLevel >= envelope[candidateIndex] * SidelobeSymmetryRatio;
    }

    private static int MeasureHalfHeightLobeWidth(
        IReadOnlyList<double> envelope,
        int peakIndex,
        double peak)
    {
        double halfHeight = peak * 0.5;
        int left = peakIndex;
        while (left > 0 && envelope[left - 1] >= halfHeight)
        {
            left--;
        }

        int right = peakIndex;
        while (right < envelope.Count - 1 && envelope[right + 1] >= halfHeight)
        {
            right++;
        }

        return Math.Max(1, right - left + 1);
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
        return Math.Clamp(requestedSamples, 3, Math.Max(3, envelopeLength / 2));
    }

    private static double EstimateEnvelopeNoiseRms(
        IReadOnlyList<double> envelope,
        int peakIndex)
    {
        int exclusionRadius = Math.Max(8, envelope.Count / 50);
        double sumSquares = 0;
        int count = 0;
        for (int i = 0; i < envelope.Count; i++)
        {
            if (CircularDistance(i, peakIndex, envelope.Count) <= exclusionRadius)
            {
                continue;
            }

            sumSquares += envelope[i] * envelope[i];
            count++;
        }

        return count > 0
            ? Math.Sqrt(sumSquares / count)
            : 0;
    }

    private static int CircularDistance(
        int index,
        int centerIndex,
        int length)
    {
        int distance = Math.Abs(index - centerIndex);
        return Math.Min(distance, length - distance);
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
}

public readonly record struct PeakSearchResult(
    int SelectedIndex,
    int StrongestIndex,
    double StrongestPeak,
    bool FallbackUsed);
