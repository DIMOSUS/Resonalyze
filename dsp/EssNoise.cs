using System;
using System.Collections.Generic;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public readonly record struct NoiseInterval(int Start, int End);

/// <summary>
/// The broadband noise floor of a deconvolved ESS measurement, estimated from
/// time regions that hold neither the linear response nor a harmonic packet.
/// <see cref="Magnitude"/> is expressed in the same units as the linear packet
/// |H1| (ENBW-compensated to the linear window), so it can be added in energy to
/// the harmonics to form THD+N.
/// </summary>
public sealed record NoiseEstimate(
    double[] BinFrequenciesHz,
    double[] Magnitude,
    double EquivalentNoiseBandwidthHz,
    IReadOnlyList<NoiseInterval> SourceRanges,
    double Confidence);

/// <summary>
/// Estimates the noise floor of a deconvolved exponential-sweep impulse response.
/// The harmonic packets sit before the linear peak and the linear response decays
/// after it, so the clean noise-only region is the tail AFTER the linear packet
/// (and its reverb) has died away — never the pre-harmonic region, which still
/// carries the higher-order harmonics. Several equal windows are taken there and
/// their per-bin magnitudes are combined by median (robust to a stray transient),
/// then ENBW-compensated to the linear window so the level is comparable to |H1|.
/// </summary>
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

        // Start past the linear packet plus a guard for its reverb tail, so a slow
        // room decay does not masquerade as noise.
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

        // Rectangular windows: per-bin white-noise variance is sigma^2 * windowLength,
        // so scaling by sqrt(linearLength / windowLength) expresses the noise at the
        // linear window's bandwidth — the compensation that makes the result
        // independent of the noise window length.
        double compensation = Math.Sqrt(linearLength / (double)windowLength);

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
                perBin[bin][w] = buffer[bin].Magnitude;
            }
        }

        double[] magnitude = new double[usableBins];
        for (int bin = 0; bin < usableBins; bin++)
        {
            magnitude[bin] = Median(perBin[bin]) * compensation;
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
