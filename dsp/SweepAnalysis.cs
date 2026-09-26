using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

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

        Fourier.Forward(filterSpectrum, FourierOptions.Matlab);
        return Deconvolve(signalSpectrum, filterSpectrum, convolutionLength, normalization);
    }

    /// <summary>Against a filter transformed once per FFT length: the runs and channels of one measurement share it.</summary>
    public static SweepDeconvolutionResult DeconvolveWithInverseFilter(
        IReadOnlyList<float> recorded,
        InverseFilterSpectrum inverseFilter,
        double normalization = 2.0)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(inverseFilter);
        ValidateInputs(recorded.Count, inverseFilter.Samples.Count, normalization);

        int convolutionLength = checked(recorded.Count + inverseFilter.Samples.Count - 1);
        int fftLength = DspMath.NextPowerOfTwo(convolutionLength);

        var signalSpectrum = new Complex[fftLength];
        for (int i = 0; i < recorded.Count; i++)
        {
            signalSpectrum[i] = new Complex(recorded[i], 0.0);
        }

        return Deconvolve(signalSpectrum, inverseFilter.At(fftLength), convolutionLength, normalization);
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

        Fourier.Forward(filterSpectrum, FourierOptions.Matlab);
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

    /// <summary>Circular convolution with a transformed filter, which is only read; callers fill the signal from native samples.</summary>
    private static SweepDeconvolutionResult Deconvolve(
        Complex[] signalSpectrum,
        Complex[] filterSpectrum,
        int convolutionLength,
        double normalization)
    {
        Fourier.Forward(signalSpectrum, FourierOptions.Matlab);

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

/// <summary>An inverse filter's spectrum at the last FFT length asked for. 16 bytes a bin, 32–128 MB for a long sweep, so it
/// lives no longer than the measurement that shares it.</summary>
public sealed class InverseFilterSpectrum(IReadOnlyList<float> samples)
{
    private readonly object sync = new();
    private Complex[]? spectrum;

    public IReadOnlyList<float> Samples { get; } = samples ?? throw new ArgumentNullException(nameof(samples));

    internal Complex[] At(int fftLength)
    {
        lock (sync)
        {
            if (spectrum?.Length != fftLength)
            {
                var transformed = new Complex[fftLength];
                for (int i = 0; i < Samples.Count; i++)
                {
                    transformed[i] = new Complex(Samples[i], 0.0);
                }

                Fourier.Forward(transformed, FourierOptions.Matlab);
                spectrum = transformed;
            }

            return spectrum;
        }
    }
}

public readonly record struct SweepDeconvolutionResult(
    double[] ImpulseResponse,
    int PeakIndex);
