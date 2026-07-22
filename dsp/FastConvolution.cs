using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp;

/// <summary>
/// Linear convolution of a long signal with a fixed kernel, by overlap-add FFT.
/// <para>
/// The direct sum is not an option at this scale: a five-minute track at 48 kHz is
/// ~14 million samples and an auralization kernel runs to tens of thousands of
/// taps, which is ~10¹² multiply-adds. Overlap-add transforms the kernel once and
/// then costs two transforms per block, bringing the same result to seconds.
/// </para>
/// <para>
/// Blocks are formed so that each one's circular convolution IS its linear
/// convolution — the hop leaves exactly <c>kernel.Length − 1</c> samples of room
/// for the tail, which then adds into the following block. Nothing wraps.
/// </para>
/// </summary>
public static class FastConvolution
{
    // The transform runs over this many times the kernel length. Larger blocks
    // amortize the per-block transform better but waste more of it on the
    // zero-padded tail; four is the usual sweet spot and keeps the working
    // buffers small enough to stay in cache-friendly territory.
    private const int BlockLengthFactor = 4;

    private const int MinimumFftLength = 1024;

    /// <summary>
    /// Convolves <paramref name="signal"/> with <paramref name="kernel"/>,
    /// returning <c>signal.Length + kernel.Length − 1</c> samples.
    /// </summary>
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

            // The block spans count + kernel.Length − 1 output samples, which is
            // exactly what the padding left room for, so this never reads the
            // wrapped region and never writes past the output.
            int span = Math.Min(fftLength, output.Length - start);
            for (int i = 0; i < span; i++)
            {
                output[start + i] += (float)block[i].Real;
            }

            progress?.Report((blockIndex + 1) / (double)blockCount);
        }

        return output;
    }

    /// <summary>
    /// Linear convolution of two kernels, in double precision, by one transform
    /// pair. For combining filter kernels (a trimmed room response with a
    /// calibration FIR) — both fit a single FFT comfortably, and the result
    /// stays in the double domain the analysis side works in.
    /// </summary>
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
