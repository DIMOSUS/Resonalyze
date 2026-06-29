using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Estimates the excess (all-pass) delay of a measured response: the timing of the
/// part that remains after the minimum-phase component is removed.
/// </summary>
/// <remarks>
/// Two estimators are returned because they answer different questions and only
/// coincide when the excess is essentially a pure delay:
/// <list type="bullet">
/// <item><b>Peak</b> — the dominant arrival (the mode of the excess energy). Robust
/// against reflections; the right reference for a "bulk delay" readout.</item>
/// <item><b>Slope</b> — the energy-weighted mean group delay, i.e. the temporal
/// centroid of the excess energy. By the group-delay/centroid theorem this is the τ
/// that defines the linear trend of the excess phase, so it is the value to subtract
/// when detrending an excess-phase curve.</item>
/// </list>
/// Both are computed from one construction: the excess response
/// <c>g = IFFT(H · e^{-jφ_min}) = IFFT(|H|·e^{jφ_exc})</c>. This is real (the inputs
/// come from a real impulse response) and weighted by the measured magnitude, so
/// noise-floor and null bins contribute little. The method is pure.
/// </remarks>
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

        // g = IFFT(H · e^{-jφ_min}). Magnitude stays |H|; phase becomes the excess
        // phase φ_meas − φ_min. The result is real (conjugate-symmetric spectrum).
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

        double peakSamples = EstimatePeakLag(excessResponse);
        double slopeSamples = EstimateCentroidLag(excessResponse);

        return new ExcessDelayResult(
            peakSamples,
            peakSamples * 1000.0 / sampleRate,
            slopeSamples,
            slopeSamples * 1000.0 / sampleRate);
    }

    // Dominant arrival: the envelope's global maximum, refined to sub-sample with a
    // parabolic fit over circular neighbours, expressed as a signed lag.
    private static double EstimatePeakLag(double[] signal)
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

        int previous = (peakIndex - 1 + n) % n;
        int next = (peakIndex + 1) % n;
        double fractional = SignalEnvelope.FindFractionalPeakOffset(
            envelope[previous],
            envelope[peakIndex],
            envelope[next]);

        return ToSignedLag(peakIndex + fractional, n);
    }

    // Energy-weighted temporal centroid (signed lags), equal to the energy-weighted
    // mean group delay of the excess phase.
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

    // Maps a circular index to a signed lag: indices past the midpoint represent
    // negative (leading) delays.
    private static double ToSignedLag(double lag, int n) =>
        lag <= n * 0.5 ? lag : lag - n;
}

public readonly record struct ExcessDelayResult(
    double PeakDelaySamples,
    double PeakDelayMilliseconds,
    double SlopeDelaySamples,
    double SlopeDelayMilliseconds);
