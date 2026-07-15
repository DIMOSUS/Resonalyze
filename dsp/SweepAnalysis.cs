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
        ValidateInputs(recorded.Count, inverseFilter.Count, normalization);

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

        return Deconvolve(signalSpectrum, filterSpectrum, convolutionLength, normalization);
    }

    public static SweepDeconvolutionResult DeconvolveWithInverseFilter(
        IReadOnlyList<double> recorded,
        IReadOnlyList<double> inverseFilter,
        double normalization = 2.0)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(inverseFilter);
        ValidateInputs(recorded.Count, inverseFilter.Count, normalization);

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

        return Deconvolve(signalSpectrum, filterSpectrum, convolutionLength, normalization);
    }

    private static void ValidateInputs(int recordedCount, int inverseFilterCount, double normalization)
    {
        if (recordedCount == 0)
        {
            throw new ArgumentException("Recorded signal must not be empty.", "recorded");
        }
        if (inverseFilterCount == 0)
        {
            throw new ArgumentException("Inverse filter must not be empty.", "inverseFilter");
        }
        if (!double.IsFinite(normalization) || normalization <= 0)
        {
            throw new ArgumentOutOfRangeException("normalization");
        }
    }

    /// <summary>
    /// Shared FFT core: circular-convolves two full-length <see cref="Complex"/>
    /// spectra already zero-padded to the FFT length, then extracts the first
    /// <paramref name="convolutionLength"/> real samples and the peak index.
    /// Callers fill the spectra directly from their native sample type, so no
    /// intermediate real-valued copy is materialized.
    /// </summary>
    private static SweepDeconvolutionResult Deconvolve(
        Complex[] signalSpectrum,
        Complex[] filterSpectrum,
        int convolutionLength,
        double normalization)
    {
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
