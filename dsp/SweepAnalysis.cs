using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Performs sweep-specific signal processing that belongs in the DSP layer.
/// </summary>
public static class SweepAnalysis
{
    public static SweepDeconvolutionResult DeconvolveWithInverseFilter(
        IReadOnlyList<float> recorded,
        IReadOnlyList<float> inverseFilter,
        double normalization = 2.0)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(inverseFilter);

        double[] recordedSamples = new double[recorded.Count];
        double[] inverseFilterSamples = new double[inverseFilter.Count];
        for (int i = 0; i < recorded.Count; i++)
        {
            recordedSamples[i] = recorded[i];
        }
        for (int i = 0; i < inverseFilter.Count; i++)
        {
            inverseFilterSamples[i] = inverseFilter[i];
        }

        return DeconvolveWithInverseFilter(recordedSamples, inverseFilterSamples, normalization);
    }

    public static SweepDeconvolutionResult DeconvolveWithInverseFilter(
        IReadOnlyList<float> recorded,
        IReadOnlyList<double> inverseFilter,
        double normalization = 2.0)
    {
        ArgumentNullException.ThrowIfNull(recorded);

        double[] recordedSamples = new double[recorded.Count];
        for (int i = 0; i < recorded.Count; i++)
        {
            recordedSamples[i] = recorded[i];
        }

        return DeconvolveWithInverseFilter(recordedSamples, inverseFilter, normalization);
    }

    public static SweepDeconvolutionResult DeconvolveWithInverseFilter(
        IReadOnlyList<double> recorded,
        IReadOnlyList<double> inverseFilter,
        double normalization = 2.0)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(inverseFilter);
        if (recorded.Count == 0)
        {
            throw new ArgumentException("Recorded signal must not be empty.", nameof(recorded));
        }
        if (inverseFilter.Count == 0)
        {
            throw new ArgumentException("Inverse filter must not be empty.", nameof(inverseFilter));
        }
        if (!double.IsFinite(normalization) || normalization <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(normalization));
        }

        int convolutionLength = checked(recorded.Count + inverseFilter.Count - 1);
        int fftLength = DspMath.NextPowerOfTwo(convolutionLength);

        var signalSpectrum = new Complex[fftLength];
        var filterSpectrum = new Complex[fftLength];

        for (int i = 0; i < recorded.Count; i++)
        {
            signalSpectrum[i] = new Complex(recorded[i], 0.0);
        }

        for (int i = 0; i < inverseFilter.Count; i++)
        {
            filterSpectrum[i] = new Complex(inverseFilter[i], 0.0);
        }

        Fourier.Forward(signalSpectrum, FourierOptions.Matlab);
        Fourier.Forward(filterSpectrum, FourierOptions.Matlab);

        for (int i = 0; i < signalSpectrum.Length; i++)
        {
            signalSpectrum[i] *= filterSpectrum[i];
        }

        Fourier.Inverse(signalSpectrum, FourierOptions.Matlab);

        var impulseResponse = new double[convolutionLength];
        double peakMagnitude = 0;
        int peakIndex = 0;

        for (int i = 0; i < impulseResponse.Length; i++)
        {
            double value = signalSpectrum[i].Real * normalization;
            impulseResponse[i] = value;

            double magnitude = Math.Abs(value);
            if (magnitude > peakMagnitude)
            {
                peakMagnitude = magnitude;
                peakIndex = i;
            }
        }

        return new SweepDeconvolutionResult(impulseResponse, peakIndex);
    }
}

public readonly record struct SweepDeconvolutionResult(
    double[] ImpulseResponse,
    int PeakIndex);
