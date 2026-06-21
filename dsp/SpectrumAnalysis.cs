using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Shared FFT helpers for spectrum-based measurements.
/// </summary>
public static class SpectrumAnalysis
{
    public static double[] ComputeMagnitudeSpectrum(IReadOnlyList<float> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("Samples must not be empty.", nameof(samples));
        }

        var spectrum = new Complex[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            spectrum[i] = new Complex(samples[i], 0.0);
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);

        var magnitudes = new double[samples.Count / 2];
        for (int i = 0; i < magnitudes.Length; i++)
        {
            magnitudes[i] = spectrum[i].Magnitude;
        }

        return magnitudes;
    }
}
