namespace Resonalyze.Dsp;

/// <summary>
/// Rational-ratio sample-rate conversion by polyphase windowed-sinc filtering.
/// <para>
/// Every pair of integer rates is a rational ratio (44100 → 48000 is 160/147), so
/// one anti-imaging/anti-aliasing kernel designed at the interpolated rate
/// <c>up · fromRate</c> serves both directions. The filter is specified the way
/// any honest SRC must be: a passband reaching <see cref="PassbandFraction"/> of
/// the LOWER of the two Nyquist limits (≈20.1 kHz for 44.1 kHz material), the
/// full <see cref="StopbandAttenuationDb"/> reached AT that Nyquist — where
/// aliasing actually begins — and the cutoff in the middle of the transition
/// band between them. The Kaiser window's β and the kernel length follow from
/// the attenuation and the transition width; a single cutoff at Nyquist would
/// instead put the −6 dB point exactly where folding starts and leave content
/// just above it only ~10 dB down.
/// </para>
/// <para>
/// The zero-stuffed samples are never materialized — each output reads only the
/// taps that land on real input samples. The kernel is symmetric and evaluated
/// with its own group delay compensated, so the output is time-aligned with the
/// input to a fraction of a sample. That matters here: the caller resamples
/// MUSIC against measured impulse responses, and a converter that shifted the
/// signal would move the very arrivals the measurement exists to place.
/// </para>
/// </summary>
public static class SampleRateConverter
{
    // The passband keeps this fraction of the lower Nyquist: 91% is 20.07 kHz
    // against a 44.1 kHz target — the top of the audible band survives, and the
    // transition band that remains is wide enough to keep the kernel practical.
    private const double PassbandFraction = 0.91;

    // Reached AT the lower Nyquist, i.e. at the first frequency that can fold
    // back. Ninety dB is below 16-bit material's own noise floor.
    private const double StopbandAttenuationDb = 90.0;

    // A sanity bound on pathological rate pairs. Rates that share no useful
    // divisor (44100 → 48001) push the transition width toward zero and the
    // kernel toward millions of taps; refuse rather than allocate them.
    private const int MaximumKernelLength = 4 << 20;

    // The inner loop is millions of iterations; the token is polled and the
    // progress reported on power-of-two strides so the checks stay invisible
    // next to the multiply-adds.
    private const int CancellationCheckMask = 4_095;
    private const int ProgressReportMask = (1 << 18) - 1;

    /// <summary>
    /// Resamples <paramref name="samples"/> from <paramref name="fromRate"/> to
    /// <paramref name="toRate"/>. Equal rates return a copy.
    /// </summary>
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

            // Output n represents input time n·down/up, i.e. position n·down in
            // the interpolated domain; the kernel is evaluated centred there,
            // and adding its own centre offset cancels the filter's group delay.
            long position = n * down + center;
            // Tap j = position − i·up must land inside the kernel, which bounds
            // the input samples this output reads.
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

    // The shared low-pass in the interpolated domain, from the spec down: the
    // stopband edge is the lower Nyquist, the passband edge its fraction, the
    // cutoff halfway between, and Kaiser's published estimates turn the
    // attenuation and transition width into β and the tap count
    // (β = 0.1102·(A − 8.7), order ≈ (A − 7.95)/(2.285·Δω)). DC gain is `up`
    // over the whole kernel so the every-up-th taps one output reads sum to
    // unity.
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
