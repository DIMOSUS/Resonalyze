namespace Resonalyze.Dsp;

/// <summary>Rational-ratio polyphase windowed-sinc SRC. One Kaiser kernel at <c>up · fromRate</c>: passband to 91 % of the lower Nyquist,
/// full attenuation AT that Nyquist. Group delay compensated, so resampled music stays time-aligned with measured IRs.</summary>
public static class SampleRateConverter
{
    private const double PassbandFraction = 0.91;

    // Reached at the lower Nyquist, where folding begins; below 16-bit noise.
    private const double StopbandAttenuationDb = 90.0;

    // Rate pairs with no useful divisor (44100 → 48001) need millions of taps: refuse.
    private const int MaximumKernelLength = 4 << 20;

    private const int CancellationCheckMask = 4_095;
    private const int ProgressReportMask = (1 << 18) - 1;

    public static float[] Resample(
        float[] samples,
        int fromRate,
        int toRate,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (fromRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fromRate));
        }
        if (toRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(toRate));
        }
        if (fromRate == toRate)
        {
            return (float[])samples.Clone();
        }
        if (samples.Length == 0)
        {
            return Array.Empty<float>();
        }

        int divisor = GreatestCommonDivisor(fromRate, toRate);
        int up = toRate / divisor;
        int down = fromRate / divisor;
        double[] kernel = BuildKernel(up, down, out int center);

        long outputLength = ((long)samples.Length * up + down - 1) / down;
        var output = new float[outputLength];
        int kernelLength = kernel.Length;
        int lastInput = samples.Length - 1;
        for (long n = 0; n < outputLength; n++)
        {
            if ((n & CancellationCheckMask) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (n > 0 && (n & ProgressReportMask) == 0)
            {
                progress?.Report(n / (double)outputLength);
            }

            // Adding the kernel centre cancels the filter's group delay.
            long position = n * down + center;
            long lowNumerator = position - kernelLength + 1;
            int firstInput = lowNumerator <= 0
                ? 0
                : (int)((lowNumerator + up - 1) / up);
            int finalInput = (int)Math.Min(lastInput, position / up);
            double sum = 0;
            for (int i = firstInput; i <= finalInput; i++)
            {
                sum += kernel[(int)(position - (long)i * up)] * samples[i];
            }

            output[n] = (float)sum;
        }

        progress?.Report(1.0);
        return output;
    }

    // Kaiser estimates: β = 0.1102·(A − 8.7), order ≈ (A − 7.95)/(2.285·Δω). DC gain `up` so each output's taps sum to unity.
    private static double[] BuildKernel(int up, int down, out int center)
    {
        int factor = Math.Max(up, down);
        double stopbandEdge = 0.5 / factor;
        double transitionWidth = (1.0 - PassbandFraction) * stopbandEdge;
        double cutoff = stopbandEdge - transitionWidth / 2.0;
        double beta = 0.1102 * (StopbandAttenuationDb - 8.7);
        int halfLength = (int)Math.Ceiling(
            (StopbandAttenuationDb - 7.95) /
            (2.0 * 2.285 * 2.0 * Math.PI * transitionWidth));
        center = halfLength;
        long length = 2L * halfLength + 1;
        if (length > MaximumKernelLength)
        {
            throw new NotSupportedException(
                $"Resampling by the ratio {up}/{down} needs a {length}-tap " +
                "kernel; the two rates share too small a common divisor to " +
                "resample efficiently.");
        }

        double windowDenominator = MathNet.Numerics.SpecialFunctions.BesselI0(beta);
        var kernel = new double[length];
        for (int i = 0; i < kernel.Length; i++)
        {
            double offset = i - center;
            double sinc = offset == 0
                ? 2.0 * cutoff
                : Math.Sin(2.0 * Math.PI * cutoff * offset) / (Math.PI * offset);
            double ratio = offset / center;
            double window = MathNet.Numerics.SpecialFunctions.BesselI0(
                beta * Math.Sqrt(Math.Max(0.0, 1.0 - ratio * ratio)))
                / windowDenominator;
            kernel[i] = up * sinc * window;
        }

        return kernel;
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
