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
/// <item><b>Peak</b> — the first arrival of the excess energy: the earliest
/// prominent envelope peak, found with the same first-arrival detector Time
/// Alignment uses, so a late reflection or room mode that rings louder than the
/// direct sound does not capture the τ reference. The right reference for a
/// "bulk delay" readout. Falls back to the strongest peak when the excess energy
/// leads the window (a negative-lag dominant).</item>
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

        // A spectrum with no energy would "peak" at lag 0 and read as a valid
        // τ = 0; report it as invalid instead so a caller does not silently
        // write a fabricated reference.
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

    // First arrival of the excess energy, refined to sub-sample with a parabolic
    // fit over circular neighbours, expressed as a signed lag. The global
    // envelope maximum alone is NOT the arrival: a room reflection or mode can
    // ring louder than the direct sound (the exact trap Time Alignment handles),
    // and putting the τ reference on it tilts the whole excess-phase curve. So
    // when the dominant energy sits at a causal (positive) lag, the same
    // first-arrival detector walks back to the earliest prominent peak; a
    // negative-lag dominant (excess energy leading the window) keeps the global
    // maximum, which the first-arrival search cannot reach.
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
            // Only a strictly EARLIER prominent arrival replaces the global
            // maximum: when the dominant energy already is the earliest event
            // (a near-pure delay peaking at lag ~0), the detector can only
            // offer a skirt bump after it, never the peak itself.
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
    double SlopeDelayMilliseconds,
    // False when the gated spectrum carried no energy at all: a zero excess
    // response "peaks" at lag 0, and an auto-τ caller would silently write a
    // fabricated 0 ms reference.
    bool IsValid = true);
