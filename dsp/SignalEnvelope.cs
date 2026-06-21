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

        for (int i = 1; i < searchEnd - 1; i++)
        {
            if (envelope[i] < threshold)
            {
                continue;
            }

            if (envelope[i] >= envelope[i - 1] &&
                envelope[i] >= envelope[i + 1])
            {
                return new PeakSearchResult(
                    i,
                    strongestIndex,
                    strongestPeak,
                    false);
            }
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

        double noiseRms = count > 0
            ? Math.Sqrt(sumSquares / count)
            : 0;
        return DataHelper.AmplitudeToDecibels(peak / Math.Max(noiseRms, 1e-12));
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
