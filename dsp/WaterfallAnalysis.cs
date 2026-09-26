using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

public static class WaterfallAnalysis
{
    public static IReadOnlyList<BurstDecaySlice> BuildBurstDecayRawSlices(
        IImpulseMeasurement measurement,
        int offset,
        int window,
        double[] windowFunction,
        double smoothingOctaves,
        CancellationToken cancellationToken = default)
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

        // A window opening before sample 0 reads the circular pre-roll from the record's end, as the magnitude window does.
        Complex[] spectrum = DataHelper.ExtractWindow(measurement, offset, window, windowFunction, wrapPreRoll: true);
        // A power of two: the window field takes any length, and a Bluestein transform of 4 x 5000 points costs about
        // seven times a radix-2 one of 16384. The longer pad only reduces circular wrap; what the window resolves is
        // set by the window, below.
        Array.Resize(ref spectrum, DspMath.NextPowerOfTwo(checked(window * 4)));
        Fourier.Forward(spectrum, FourierOptions.Matlab);

        double frequencyStep = (double)measurement.SampleRate / spectrum.Length;
        double frequencyRatio = Math.Pow(2.0, 0.5 * smoothingOctaves);

        // Below 40 kHz sample rate a fixed 20 kHz start would exceed Nyquist.
        double initFrequency = Math.Min(20_000.0, measurement.SampleRate * 0.49);
        // One cycle within the window: below it the slice has nothing to resolve.
        double lowestFrequency = (double)measurement.SampleRate / window;
        var frequencies = new List<double>(100);
        while (initFrequency >= lowestFrequency && initFrequency >= 20)
        {
            frequencies.Add(initFrequency);
            initFrequency /= frequencyRatio;
        }
        frequencies.Reverse();

        var result = new List<BurstDecaySlice>(frequencies.Count);
        foreach (double frequency in frequencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double w0 = (frequency / frequencyStep) * Math.PI * 2.0;
            double fWin = Math.Pow(2.0, smoothingOctaves);
            double t = 2.3548 / (w0 * (fWin - 1.0));

            Complex[] morlet = new Complex[spectrum.Length];
            double kernelSum = 0;
            for (int i = 0; i < morlet.Length; i++)
            {
                double w = (i <= morlet.Length / 2 ? i : i - morlet.Length) * Math.PI * 2.0;
                double exponent = (w - w0) * (w - w0) * t * t * 0.25;
                // Past this the exponential underflows to zero: most bins of a narrow kernel.
                if (exponent < 746.0)
                {
                    double weight = Math.Exp(-exponent);
                    morlet[i] = new Complex(weight, 0.0);
                    kernelSum += weight;
                }
            }

            double normalization = morlet.Length / kernelSum;
            for (int i = 0; i < morlet.Length; i++)
            {
                morlet[i] *= spectrum[i] * normalization;
            }

            Fourier.Inverse(morlet, FourierOptions.Matlab);

            // The window's own samples: past them is zero-padding, which a slice never shows.
            int measured = Math.Min(window, morlet.Length / 2);
            var data = new List<SignalPoint>(measured);
            for (int i = 0; i < measured; i++)
            {
                data.Add(new SignalPoint(i, morlet[i].Magnitude));
            }

            result.Add(new BurstDecaySlice(frequency, data));
        }

        return result;
    }

    /// <summary>Points past <paramref name="measuredSamples"/> (FFT zero-padding) read NaN, not a fabricated decay.
    /// <paramref name="peakOffsetSamples"/> anchors period 0 on the IR peak.</summary>
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
        // Taps clamped to the last real sample so zero-padding never blends in.
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
