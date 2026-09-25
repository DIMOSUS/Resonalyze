using System;
using System.Collections.Generic;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public readonly record struct NoiseInterval(int Start, int End);

/// <summary>Per-bin noise level at resolution sampleRate / window, raw-magnitude convention like the harmonic packets.
/// Scales with resolution and drive level: a diagnostic trace, not a bandwidth-invariant number.</summary>
public sealed record NoiseEstimate(
    double[] BinFrequenciesHz,
    double[] Magnitude,
    double EquivalentNoiseBandwidthHz,
    IReadOnlyList<NoiseInterval> SourceRanges,
    double Confidence);

/// <summary>Noise read from the tail AFTER the linear packet and its reverb (the pre-peak region holds harmonics),
/// as a bias-corrected median of per-bin powers over equal windows.</summary>
public static class EssNoise
{
    /// <summary>Shortest window a full-overlap region is split into before the whole tail is read instead.</summary>
    private const int MinClippedNoiseWindowLength = 1_024;

    public static NoiseEstimate EstimateNoise(
        ReadOnlySpan<double> deconvolvedImpulse,
        EssHarmonicDecomposition decomposition,
        DistortionOptions options)
    {
        ArgumentNullException.ThrowIfNull(decomposition);
        ArgumentNullException.ThrowIfNull(options);

        double sampleRate = decomposition.Sweep.SampleRateHz;
        HarmonicWindowDefinition linearWindow = decomposition.Linear.Window;
        int linearLength = Math.Max(1, decomposition.Linear.Spectrum.SourceWindowLength);

        int guard = Math.Max(linearLength, linearLength / 2 + 1);
        int regionStart = Math.Min(
            Math.Max(0, linearWindow.EndSample) + guard,
            deconvolvedImpulse.Length);
        // Only where the inverse filter overlaps the recording in full: past it the noise fades, highs first. The median
        // correction wants every window it counts on, so a short overlap shortens the windows rather than dropping them.
        int minimumClipped = options.NoiseWindowCount * MinClippedNoiseWindowLength;
        int regionEnd = EssHarmonicAnalysis.TailNoiseRegionEnd(
            deconvolvedImpulse.Length, regionStart, decomposition.Sweep, minimumClipped);
        int regionLength = regionEnd - regionStart;
        int windowCeiling = regionEnd < deconvolvedImpulse.Length
            ? regionLength / Math.Max(1, options.NoiseWindowCount)
            : regionLength;

        int windowLength = LargestPowerOfTwoAtMost(
            Math.Min(options.NoiseWindowLength, Math.Max(1, windowCeiling)));
        int available = windowLength > 0 ? regionLength / windowLength : 0;
        int windowCount = Math.Min(options.NoiseWindowCount, available);

        int usableBins = windowLength / 2;
        double[] binFrequencies = new double[usableBins];
        for (int bin = 0; bin < usableBins; bin++)
        {
            binFrequencies[bin] = bin * sampleRate / windowLength;
        }

        double enbwHz = windowLength > 0 ? sampleRate / windowLength : 0.0;
        if (windowCount < 1 || usableBins == 0)
        {
            return new NoiseEstimate(
                binFrequencies,
                new double[usableBins],
                enbwHz,
                Array.Empty<NoiseInterval>(),
                0.0);
        }

        _ = linearLength;
        var perBin = new double[usableBins][];
        for (int bin = 0; bin < usableBins; bin++)
        {
            perBin[bin] = new double[windowCount];
        }

        var ranges = new List<NoiseInterval>(windowCount);
        var buffer = new Complex[windowLength];
        for (int w = 0; w < windowCount; w++)
        {
            int start = regionStart + w * windowLength;
            ranges.Add(new NoiseInterval(start, start + windowLength));
            for (int i = 0; i < windowLength; i++)
            {
                buffer[i] = new Complex(deconvolvedImpulse[start + i], 0.0);
            }

            Fourier.Forward(buffer, FourierOptions.Matlab);
            for (int bin = 0; bin < usableBins; bin++)
            {
                double real = buffer[bin].Real;
                double imaginary = buffer[bin].Imaginary;
                perBin[bin][w] = real * real + imaginary * imaginary;
            }
        }

        // A bin's power is exponential, so its median over the windows reads low; dividing by the median's expected
        // value for this many windows gives the mean power back. ln 2 is only the many-window limit, and read the
        // floor 0.5 dB high at the default six windows (1.6 dB at two).
        double expectedMedian = ExpectedExponentialMedian(windowCount);
        double[] magnitude = new double[usableBins];
        for (int bin = 0; bin < usableBins; bin++)
        {
            magnitude[bin] = Math.Sqrt(Median(perBin[bin]) / expectedMedian);
        }

        double confidence = Math.Clamp(
            windowCount / (double)Math.Max(1, options.NoiseWindowCount),
            0.0,
            1.0);

        return new NoiseEstimate(binFrequencies, magnitude, enbwHz, ranges, confidence);
    }

    // E[X(k)] of n unit exponentials is 1/n + ... + 1/(n - k + 1); an even count averages the middle two.
    private static double ExpectedExponentialMedian(int count)
    {
        double OrderStatistic(int k)
        {
            double sum = 0.0;
            for (int i = count - k + 1; i <= count; i++)
            {
                sum += 1.0 / i;
            }

            return sum;
        }

        return count % 2 == 1
            ? OrderStatistic((count + 1) / 2)
            : 0.5 * (OrderStatistic(count / 2) + OrderStatistic(count / 2 + 1));
    }

    private static double Median(double[] values)
    {
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : 0.5 * (sorted[middle - 1] + sorted[middle]);
    }

    private static int LargestPowerOfTwoAtMost(int value)
    {
        if (value < 1)
        {
            return 0;
        }

        int result = 1;
        while (result * 2 <= value)
        {
            result *= 2;
        }

        return result;
    }
}
