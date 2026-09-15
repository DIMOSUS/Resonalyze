using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>Linear convolution by overlap-add FFT (the direct sum would be ~10¹² multiply-adds for a track).
/// Each block leaves <c>kernel.Length − 1</c> samples of room, so its circular convolution is linear: nothing wraps.</summary>
public static class FastConvolution
{
    private const int BlockLengthFactor = 4;

    private const int MinimumFftLength = 1024;

    /// <summary>Returns <c>signal.Length + kernel.Length − 1</c> samples.</summary>
    /// <param name="progress">Receives 0..1 completion after each block.</param>
    public static float[] Convolve(
        float[] signal,
        double[] kernel,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(kernel);
        if (kernel.Length == 0)
        {
            throw new ArgumentException("The kernel is empty.", nameof(kernel));
        }
        if (signal.Length == 0)
        {
            return Array.Empty<float>();
        }

        int fftLength = DspMath.NextPowerOfTwo(
            Math.Max(MinimumFftLength, (int)Math.Min(
                int.MaxValue / 2, (long)kernel.Length * BlockLengthFactor)));
        int hop = fftLength - kernel.Length + 1;
        if (hop <= 0)
        {
            throw new ArgumentException(
                "The kernel is too long for the transform length.", nameof(kernel));
        }

        Complex[] kernelSpectrum = Transform(kernel, fftLength);
        var output = new float[(long)signal.Length + kernel.Length - 1];
        var block = new Complex[fftLength];
        int blockCount = (signal.Length + hop - 1) / hop;
        for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int start = blockIndex * hop;
            int count = Math.Min(hop, signal.Length - start);
            Array.Clear(block);
            for (int i = 0; i < count; i++)
            {
                block[i] = signal[start + i];
            }

            Fourier.Forward(block, FourierOptions.Matlab);
            for (int bin = 0; bin < fftLength; bin++)
            {
                block[bin] *= kernelSpectrum[bin];
            }
            Fourier.Inverse(block, FourierOptions.Matlab);

            int span = Math.Min(fftLength, output.Length - start);
            for (int i = 0; i < span; i++)
            {
                output[start + i] += (float)block[i].Real;
            }

            progress?.Report((blockIndex + 1) / (double)blockCount);
        }

        return output;
    }

    /// <summary>Double-precision convolution of two kernels in one transform pair.</summary>
    public static double[] Convolve(double[] first, double[] second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Length == 0 || second.Length == 0)
        {
            throw new ArgumentException("Both kernels must be non-empty.");
        }

        int resultLength = first.Length + second.Length - 1;
        int fftLength = DspMath.NextPowerOfTwo(resultLength);
        Complex[] a = Transform(first, fftLength);
        Complex[] b = Transform(second, fftLength);
        for (int bin = 0; bin < fftLength; bin++)
        {
            a[bin] *= b[bin];
        }

        Fourier.Inverse(a, FourierOptions.Matlab);
        var result = new double[resultLength];
        for (int i = 0; i < resultLength; i++)
        {
            result[i] = a[i].Real;
        }

        return result;
    }

    private static Complex[] Transform(double[] kernel, int fftLength)
    {
        var spectrum = new Complex[fftLength];
        for (int i = 0; i < kernel.Length; i++)
        {
            spectrum[i] = kernel[i];
        }

        Fourier.Forward(spectrum, FourierOptions.Matlab);
        return spectrum;
    }
}
