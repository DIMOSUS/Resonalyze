using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Excess (all-pass) delay from g = IFFT(|H|·e^{jφ_exc}). Peak = first arrival of excess energy (bulk-delay readout; a louder later reflection does not capture it, a negative-lag dominant keeps the maximum);
/// Slope = energy centroid = mean group delay (the τ to subtract when detrending excess phase). They agree only for a pure delay.
/// </summary>
public static class ExcessDelay
{
    public static ExcessDelayResult Estimate(
        IReadOnlyList<Complex> measuredSpectrum,
        int sampleRate,
        double magnitudeFloor = MinimumPhase.DefaultMagnitudeFloor)
    {
        ArgumentNullException.ThrowIfNull(measuredSpectrum);
        int n = measuredSpectrum.Count;
        if (n < 2)
        {
            throw new ArgumentException(
                "Spectrum must have at least two bins.",
                nameof(measuredSpectrum));
        }
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        double[] magnitude = new double[n];
        for (int k = 0; k < n; k++)
        {
            magnitude[k] = measuredSpectrum[k].Magnitude;
        }

        double[] minimumPhase = MinimumPhase.FromMagnitude(magnitude, magnitudeFloor);

        Complex[] excessSpectrum = new Complex[n];
        for (int k = 0; k < n; k++)
        {
            Complex rotation = new(
                Math.Cos(minimumPhase[k]),
                -Math.Sin(minimumPhase[k]));
            excessSpectrum[k] = measuredSpectrum[k] * rotation;
        }

        Fourier.Inverse(excessSpectrum, FourierOptions.Matlab);

        double[] excessResponse = new double[n];
        for (int i = 0; i < n; i++)
        {
            excessResponse[i] = excessSpectrum[i].Real;
        }

        // An empty spectrum would read as a valid τ = 0: report invalid.
        double totalEnergy = 0.0;
        for (int i = 0; i < n; i++)
        {
            totalEnergy += excessResponse[i] * excessResponse[i];
        }
        if (!(totalEnergy > 0.0) || !double.IsFinite(totalEnergy))
        {
            return new ExcessDelayResult(0, 0, 0, 0, IsValid: false);
        }

        double peakSamples = EstimatePeakLag(excessResponse, sampleRate);
        double slopeSamples = EstimateCentroidLag(excessResponse);

        return new ExcessDelayResult(
            peakSamples,
            peakSamples * 1000.0 / sampleRate,
            slopeSamples,
            slopeSamples * 1000.0 / sampleRate);
    }

    // A reflection can ring louder than the direct sound, so a causal dominant walks back to the first arrival; a negative-lag dominant stays.
    private static double EstimatePeakLag(double[] signal, int sampleRate)
    {
        double[] envelope = SignalEnvelope.Envelope(signal);
        int n = envelope.Length;

        int peakIndex = 0;
        double peak = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (envelope[i] > peak)
            {
                peak = envelope[i];
                peakIndex = i;
            }
        }

        if (peakIndex <= n / 2)
        {
            PeakSearchResult firstArrival = SignalEnvelope.FindPeak(
                envelope,
                sampleRate,
                new PeakSearchOptions
                {
                    Mode = PeakSearchMode.FirstArrival,
                    SearchWindowMilliseconds = n * 500.0 / sampleRate
                });
            // Only a strictly earlier arrival replaces the maximum (after a pure delay the detector offers only a skirt bump).
            if (firstArrival.SelectedIndex < peakIndex)
            {
                peakIndex = firstArrival.SelectedIndex;
            }
        }

        int previous = (peakIndex - 1 + n) % n;
        int next = (peakIndex + 1) % n;
        double fractional = SignalEnvelope.FindFractionalPeakOffset(
            envelope[previous],
            envelope[peakIndex],
            envelope[next]);

        return ToSignedLag(peakIndex + fractional, n);
    }

    private static double EstimateCentroidLag(double[] signal)
    {
        int n = signal.Length;
        double weightSum = 0.0;
        double weightedLagSum = 0.0;

        for (int i = 0; i < n; i++)
        {
            double energy = signal[i] * signal[i];
            double lag = i <= n / 2 ? i : i - n;
            weightedLagSum += lag * energy;
            weightSum += energy;
        }

        return weightSum > 0.0 ? weightedLagSum / weightSum : 0.0;
    }

    private static double ToSignedLag(double lag, int n) =>
        lag <= n * 0.5 ? lag : lag - n;
}

public readonly record struct ExcessDelayResult(
    double PeakDelaySamples,
    double PeakDelayMilliseconds,
    double SlopeDelaySamples,
    double SlopeDelayMilliseconds,
    bool IsValid = true);
