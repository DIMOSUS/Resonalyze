using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// DSP helpers for waterfall and burst-decay generation.
/// </summary>
public static class WaterfallAnalysis
{
    public static IReadOnlyList<BurstDecaySlice> BuildBurstDecayRawSlices(
        IImpulseMeasurement measurement,
        int offset,
        int window,
        double[] windowFunction,
        double smoothingOctaves)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentNullException.ThrowIfNull(windowFunction);
        if (window <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }
        if (!double.IsFinite(smoothingOctaves) || smoothingOctaves <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(smoothingOctaves));
        }

        Complex[] spectrum = DataHelper.ExtractWindow(measurement, offset, window, windowFunction);
        Array.Resize(ref spectrum, window * 4);
        Fourier.Forward(spectrum, FourierOptions.Matlab);

        double frequencyStep = (double)measurement.SampleRate / spectrum.Length;
        double frequencyRatio = Math.Pow(2.0, 0.5 * smoothingOctaves);

        // The grid never reaches past Nyquist: at sample rates below 40 kHz a
        // fixed 20 kHz start would generate wavelet slices for frequencies the
        // capture physically cannot contain.
        double initFrequency = Math.Min(20_000.0, measurement.SampleRate * 0.49);
        var frequencies = new List<double>(100);
        while (initFrequency >= frequencyStep * 4 && initFrequency >= 20)
        {
            frequencies.Add(initFrequency);
            initFrequency /= frequencyRatio;
        }
        frequencies.Reverse();

        var result = new List<BurstDecaySlice>(frequencies.Count);
        foreach (double frequency in frequencies)
        {
            double w0 = (frequency / frequencyStep) * Math.PI * 2.0;
            double fWin = Math.Pow(2.0, smoothingOctaves);
            double t = 2.3548 / (w0 * (fWin - 1.0));

            Complex[] morlet = new Complex[spectrum.Length];
            double kernelSum = 0;
            for (int i = 0; i < morlet.Length; i++)
            {
                double w = (i <= morlet.Length / 2 ? i : i - morlet.Length) * Math.PI * 2.0;
                morlet[i] = new Complex(Math.Exp(-Math.Pow((w - w0), 2.0) * t * t * 0.25), 0.0);
                kernelSum += morlet[i].Magnitude;
            }

            double normalization = morlet.Length / kernelSum;
            for (int i = 0; i < morlet.Length; i++)
            {
                morlet[i] *= spectrum[i] * normalization;
            }

            Fourier.Inverse(morlet, FourierOptions.Matlab);

            var data = new List<SignalPoint>(morlet.Length / 2);
            for (int i = 0; i < morlet.Length / 2; i++)
            {
                data.Add(new SignalPoint(i, morlet[i].Magnitude));
            }

            result.Add(new BurstDecaySlice(frequency, data));
        }

        return result;
    }

    /// <summary>
    /// Resamples one burst-decay envelope onto a periods axis.
    /// <paramref name="measuredSamples"/> is how much of the raw envelope is
    /// real data (the analysis window length): everything past it is the
    /// zero-padding of the FFT, not an observed decay, so those points read
    /// NaN instead of a fabricated cliff to the floor — at 20 Hz a 30-period
    /// axis spans 1.5 s while a default window measures only 85 ms.
    /// <paramref name="peakOffsetSamples"/> anchors period 0 on the IR peak
    /// (the raw envelope starts a left-fade earlier, at the gate start).
    /// </summary>
    public static IReadOnlyList<SignalPoint> ResampleBurstDecaySlice(
        IReadOnlyList<SignalPoint> rawData,
        double frequency,
        int sampleRate,
        int width,
        double periods,
        int measuredSamples = int.MaxValue,
        int peakOffsetSamples = 0)
    {
        ArgumentNullException.ThrowIfNull(rawData);
        if (rawData.Count == 0)
        {
            return Array.Empty<SignalPoint>();
        }
        if (!double.IsFinite(frequency) || frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequency));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (!double.IsFinite(periods) || periods <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periods));
        }
        if (peakOffsetSamples < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(peakOffsetSamples));
        }

        double periodsTime = periods / frequency;
        double periodsSamples = sampleRate * periodsTime;
        // measuredSamples counts REAL samples, so the last measured index is
        // measuredSamples − 1 — and the interpolation taps are clamped to it
        // too, so points just inside the boundary do not blend the FFT's
        // zero-padding into the last measured readings.
        int lastMeasuredIndex = (int)Math.Min(
            Math.Min((long)measuredSamples - 1, rawData.Count - 1),
            int.MaxValue);
        var data = new List<SignalPoint>(width);

        for (int i = 0; i < width; i++)
        {
            double interp = (double)i / width;
            double samplePosition = peakOffsetSamples + interp * periodsSamples;
            data.Add(new SignalPoint(
                interp * periods,
                samplePosition <= lastMeasuredIndex
                    ? DataHelper.AmplitudeToDecibels(
                        SmoothSample(rawData, samplePosition, lastMeasuredIndex))
                    : double.NaN));
        }

        return data;
    }

    private static double SmoothSample(
        IReadOnlyList<SignalPoint> rawData,
        double index,
        int maxIndex)
    {
        const int radius = 2;
        int centerIndex = (int)Math.Round(index);
        int limit = Math.Min(maxIndex, rawData.Count - 1);

        double weightSum = 0;
        double weightedSum = 0;

        for (int sampleIndex = Math.Max(centerIndex - radius, 0);
            sampleIndex <= Math.Min(centerIndex + radius, limit);
            sampleIndex++)
        {
            double weight = DataHelper.LanczosKernel(index - sampleIndex, radius);
            weightedSum += rawData[sampleIndex].Y * weight;
            weightSum += weight;
        }

        return weightSum < 1e-5 ? 0.0 : weightedSum / weightSum;
    }
}

public readonly record struct BurstDecaySlice(
    double Frequency,
    IReadOnlyList<SignalPoint> Data);
