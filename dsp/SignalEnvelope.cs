using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>Hilbert envelopes and first-arrival search. See docs/tech/dsp-envelope-peak-search.md.</summary>
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
        return AnalyticMagnitude(spectrum);
    }

    /// <summary>Envelope from an already-computed forward spectrum (saves two full-length transforms). Does not modify the argument.</summary>
    internal static double[] EnvelopeFromSpectrum(Complex[] spectrum)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        if (spectrum.Length == 0)
        {
            throw new ArgumentException(
                "Spectrum must not be empty.",
                nameof(spectrum));
        }

        return AnalyticMagnitude((Complex[])spectrum.Clone());
    }

    // Consumes the array it is given.
    private static double[] AnalyticMagnitude(Complex[] spectrum)
    {
        int length = spectrum.Length;
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

        int searchEnd = GetSearchEndIndex(
            envelope.Count,
            sampleRate,
            options.SearchWindowMilliseconds);
        double noiseRms = EstimateEnvelopeNoiseRms(envelope);
        int rotation = FindSearchAnchorRotation(
            envelope,
            searchEnd,
            options.FirstPeakThresholdBelowMaxDb,
            options.FirstPeakMinimumSnrDb,
            noiseRms);
        IReadOnlyList<double> view = rotation == 0
            ? envelope
            : RotateView(envelope, rotation);
        int Original(int viewIndex) => rotation == 0
            ? viewIndex
            : (viewIndex + rotation) % envelope.Count;

        (int strongestIndex, double strongestPeak) = FindStrongestPeak(
            view,
            sampleRate,
            options.SearchWindowMilliseconds);
        if (options.Mode == PeakSearchMode.StrongestPeak)
        {
            return new PeakSearchResult(
                Original(strongestIndex),
                Original(strongestIndex),
                strongestPeak,
                false,
                rotation);
        }

        double thresholdFromMax = strongestPeak *
            Math.Pow(10.0, -Math.Abs(options.FirstPeakThresholdBelowMaxDb) / 20.0);
        double thresholdFromNoise = noiseRms *
            Math.Pow(10.0, Math.Max(0, options.FirstPeakMinimumSnrDb) / 20.0);
        double threshold = Math.Max(thresholdFromMax, thresholdFromNoise);

        var candidates = new List<int>();
        for (int i = 1; i < searchEnd - 1; i++)
        {
            if (view[i] >= threshold &&
                view[i] >= view[i - 1] &&
                view[i] >= view[i + 1])
            {
                candidates.Add(i);
            }
        }

        int packetSpanSamples = (int)Math.Round(
            ArrivalPacketMilliseconds * sampleRate / 1000.0);
        int firstArrivalIndex = EliminatePreRingingSidelobes(
            view,
            candidates,
            options.AnalysisKernelEnvelope,
            threshold,
            strongestPeak,
            packetSpanSamples);
        if (firstArrivalIndex >= 0)
        {
            return new PeakSearchResult(
                Original(firstArrivalIndex),
                Original(strongestIndex),
                strongestPeak,
                false,
                rotation);
        }

        return new PeakSearchResult(
            Original(strongestIndex),
            Original(strongestIndex),
            strongestPeak,
            true,
            rotation);
    }

    // Re-anchor on the global max when chain latency parks the IR beyond the window.
    // See docs/tech/dsp-envelope-peak-search.md#search-anchor-on-chain-latency.
    private static int FindSearchAnchorRotation(
        IReadOnlyList<double> envelope,
        int searchEnd,
        double firstPeakThresholdBelowMaxDb,
        double firstPeakMinimumSnrDb,
        double noiseRms)
    {
        int globalIndex = 0;
        double windowMax = 0.0;
        for (int i = 0; i < envelope.Count; i++)
        {
            if (envelope[i] > envelope[globalIndex])
            {
                globalIndex = i;
            }
            if (i < searchEnd && envelope[i] > windowMax)
            {
                windowMax = envelope[i];
            }
        }

        if (globalIndex < searchEnd)
        {
            return 0;
        }

        double reachFloor = envelope[globalIndex] *
            Math.Pow(10.0, -Math.Abs(firstPeakThresholdBelowMaxDb) / 20.0);
        double noiseFloor = noiseRms *
            Math.Pow(10.0, Math.Max(0, firstPeakMinimumSnrDb) / 20.0);
        return windowMax >= reachFloor && windowMax >= noiseFloor
            ? 0
            : globalIndex - (searchEnd - 2);
    }

    private static double[] RotateView(IReadOnlyList<double> envelope, int rotation)
    {
        var view = new double[envelope.Count];
        for (int i = 0; i < view.Length; i++)
        {
            view[i] = envelope[(i + rotation) % envelope.Count];
        }

        return view;
    }

    // Reported SNR only; see docs/tech/dsp-envelope-peak-search.md#noise-floor.
    private const double RayleighLowestQuartileRmsRatio = 0.370;

    // Reported SNR ignores the deconvolution's numerical-silence tail (FindPeak's gate is unaffected).
    private const double DeconvolutionFloorDropDb = 100.0;

    public static double EstimatePeakConfidenceDecibels(
        IReadOnlyList<double> envelope,
        double peak)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        double validFloor = peak * Math.Pow(10.0, -DeconvolutionFloorDropDb / 20.0);
        var valid = new List<double>(envelope.Count);
        foreach (double sample in envelope)
        {
            if (sample >= validFloor)
            {
                valid.Add(sample);
            }
        }

        double noiseRms =
            EstimateEnvelopeNoiseRms(valid.Count > 0 ? valid : envelope)
            / RayleighLowestQuartileRmsRatio;
        return DataHelper.AmplitudeToDecibels(peak / Math.Max(noiseRms, 1e-12));
    }

    // Kernel ceiling margin (6 dB). See docs/tech/dsp-envelope-peak-search.md#pre-ringing-sidelobes.
    private const double SidelobeLevelMarginRatio = 2.0;
    private const double SidelobeSymmetryRatio = 0.5;
    private const int SidelobeMirrorNeighborhood = 2;

    // 3 dB: pedestal ripples rise by hundredths of a dB. See docs/tech/dsp-envelope-peak-search.md#front-tests.
    private const double FrontApproachRiseRatio = 1.41;
    private const double ApproachWindowKernelLevel = 0.1;

    // Same 1 ms as TimeAlignmentAnalysis's separate-arrival rule, so the two agree on what one arrival is.
    internal const double ArrivalPacketMilliseconds = 1.0;

    // -12 dB: foot ripples measured 19-21 dB under their packet, fronts within 7 dB.
    private const double ArrivalPacketRiseRatio = 0.25;

    // Same resolved-valley depth as TimeAlignmentAnalysis; real foot ripples dip at most 14.5 dB.
    internal const double ArrivalPacketResolvedValleyDb = 20.0;

    // Latest to earliest; every non-sidelobe candidate, even a dwarfed one, becomes a sidelobe reference.
    private static int EliminatePreRingingSidelobes(
        IReadOnlyList<double> envelope,
        IReadOnlyList<int> candidates,
        IReadOnlyList<double>? kernelEnvelope,
        double threshold,
        double strongestPeak,
        int packetSpanSamples)
    {
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

        int approachSpanSamples = ApproachSpanSamples(
            kernelEnvelope, packetSpanSamples, reachCap);
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
                if (envelope[candidate] >= strongestPeak ||
                    (RisesWithinItsPacket(envelope, candidate, packetSpanSamples) &&
                     RisesOutOfItsApproach(envelope, candidate, approachSpanSamples)))
                {
                    firstArrival = candidate;
                }
            }
        }

        return firstArrival;
    }

    // Rejects micro-ripples on the zero-phase kernel's acausal shelf ahead of the first arrival.
    private static bool RisesOutOfItsApproach(
        IReadOnlyList<double> envelope,
        int candidateIndex,
        int approachSpanSamples)
    {
        if (candidateIndex <= 0)
        {
            return true;
        }

        int first = Math.Max(0, candidateIndex - approachSpanSamples);
        double floor = double.MaxValue;
        for (int i = first; i < candidateIndex; i++)
        {
            floor = Math.Min(floor, envelope[i]);
        }

        return envelope[candidateIndex] >= floor * FrontApproachRiseRatio;
    }

    private static int ApproachSpanSamples(
        IReadOnlyList<double>? kernelEnvelope,
        int packetSpanSamples,
        int reachCap)
    {
        if (kernelEnvelope == null || kernelEnvelope.Count == 0 ||
            kernelEnvelope[0] <= 0.0)
        {
            return packetSpanSamples;
        }

        double coreLevel = kernelEnvelope[0] * ApproachWindowKernelLevel;
        int span = packetSpanSamples;
        int last = Math.Min(kernelEnvelope.Count - 1, reachCap);
        for (int d = 1; d <= last; d++)
        {
            if (kernelEnvelope[d] >= coreLevel)
            {
                span = Math.Max(span, d);
            }
        }

        return span;
    }

    private static bool RisesWithinItsPacket(
        IReadOnlyList<double> envelope,
        int candidateIndex,
        int packetSpanSamples)
    {
        double candidate = envelope[candidateIndex];
        double resolvedFloor =
            candidate * Math.Pow(10.0, -ArrivalPacketResolvedValleyDb / 20.0);
        double packetPeak = candidate;
        int last = Math.Min(envelope.Count - 1, candidateIndex + packetSpanSamples);
        for (int i = candidateIndex + 1; i <= last; i++)
        {
            if (envelope[i] < resolvedFloor)
            {
                break;
            }

            packetPeak = Math.Max(packetPeak, envelope[i]);
        }

        return candidate >= packetPeak * ArrivalPacketRiseRatio;
    }

    // Without a kernel, the Hilbert skirt 2/(pi*n) is the delta worst case.
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
        int distance = peakIndex - candidateIndex;
        double ringCeiling = envelope[peakIndex] *
            KernelRingLevel(kernelEnvelope, distance) *
            SidelobeLevelMarginRatio;
        if (envelope[candidateIndex] > ringCeiling)
        {
            return false;
        }

        // Mirror read over a neighbourhood: the integer peak index is up to half a sample off.
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
        // Floor of 3 is for parabolic refinement; never read past a 1-2 sample envelope.
        int cap = Math.Min(envelopeLength, Math.Max(3, envelopeLength / 2));
        return Math.Clamp(requestedSamples, Math.Min(3, cap), cap);
    }

    private const double NoiseFloorQuantile = 0.25;

    // Quietest-quartile RMS: an IR's remainder is reverb, not noise.
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

    /// <summary>Envelope of the zero-phase kernel that filtered the signal, by |offset| from its peak (arbitrary scale); null = Hilbert skirt only.</summary>
    public IReadOnlyList<double>? AnalysisKernelEnvelope { get; init; }
}

/// <summary>With non-zero <see cref="SearchRotation"/>, measure index distances as <c>(index - SearchRotation) mod length</c>.</summary>
public readonly record struct PeakSearchResult(
    int SelectedIndex,
    int StrongestIndex,
    double StrongestPeak,
    bool FallbackUsed,
    int SearchRotation = 0);


