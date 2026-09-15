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
        int regionEnd = deconvolvedImpulse.Length;
        int regionLength = regionEnd - regionStart;

        int windowLength = LargestPowerOfTwoAtMost(
            Math.Min(options.NoiseWindowLength, Math.Max(1, regionLength)));
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

        // median(exponential) = ln2 · mean; a plain magnitude median reads ~1.6 dB low.
        double[] magnitude = new double[usableBins];
        for (int bin = 0; bin < usableBins; bin++)
        {
            magnitude[bin] = Math.Sqrt(Median(perBin[bin]) / Math.Log(2.0));
        }

        double confidence = Math.Clamp(
            windowCount / (double)Math.Max(1, options.NoiseWindowCount),
            0.0,
            1.0);

        return new NoiseEstimate(binFrequencies, magnitude, enbwHz, ranges, confidence);
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
