namespace Resonalyze.Dsp;

/// <summary>
/// Rational-ratio sample-rate conversion by polyphase windowed-sinc filtering.
/// <para>
/// Every pair of integer rates is a rational ratio (44100 → 48000 is 160/147), so
/// one anti-imaging/anti-aliasing kernel designed at the interpolated rate
/// <c>up · fromRate</c> serves both directions: its cutoff sits at the LOWER of the
/// two Nyquist limits, which removes the upsampler's images and the downsampler's
/// aliases with the same taps. The zero-stuffed samples are never materialized —
/// each output reads only the taps that land on real input samples, so the cost is
/// ~2·<see cref="HalfLengthPerPhase"/> multiply-adds per output regardless of the
/// ratio.
/// </para>
/// <para>
/// The kernel is symmetric and evaluated with its own group delay compensated, so
/// the output is time-aligned with the input to a fraction of a sample. That
/// matters here: the caller resamples MUSIC against measured impulse responses,
/// and a converter that shifted the signal would move the very arrivals the
/// measurement exists to place.
/// </para>
/// </summary>
public static class SampleRateConverter
{
    // Taps per polyphase branch, each side of the centre. Sixteen puts the
    // transition band inside the last percent of the passband and the stopband
    // under the Kaiser window's own floor — inaudible against any source
    // material, and cheap enough to run over a whole track.
    private const int HalfLengthPerPhase = 16;

    // Kaiser β ≈ 8.6 gives roughly −90 dB stopband attenuation: below 16-bit
    // material's own noise floor, so conversion artifacts cannot be the thing
    // the listener hears.
    private const double KaiserBeta = 8.6;

    // A sanity bound on pathological rate pairs. Rates that share no useful
    // divisor (44100 → 48001) blow the kernel up to the interpolation factor
    // itself; refuse rather than allocate hundreds of megabytes.
    private const int MaximumKernelLength = 4 << 20;

    /// <summary>
    /// Resamples <paramref name="samples"/> from <paramref name="fromRate"/> to
    /// <paramref name="toRate"/>. Equal rates return a copy.
    /// </summary>
    public static float[] Resample(float[] samples, int fromRate, int toRate)
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

        return output;
    }

    // The shared low-pass, designed in the interpolated domain: cutoff at the
    // lower of the two Nyquist limits, DC gain `up` over the whole kernel so the
    // every-up-th taps one output actually reads sum to unity.
    private static double[] BuildKernel(int up, int down, out int center)
    {
        int factor = Math.Max(up, down);
        center = HalfLengthPerPhase * factor;
        long length = 2L * center + 1;
        if (length > MaximumKernelLength)
        {
            throw new NotSupportedException(
                $"Resampling by the ratio {up}/{down} needs a {length}-tap " +
                "kernel; the two rates share too small a common divisor to " +
                "resample efficiently.");
        }

        double cutoff = 0.5 / factor;
        double windowDenominator = MathNet.Numerics.SpecialFunctions.BesselI0(KaiserBeta);
        var kernel = new double[length];
        for (int i = 0; i < kernel.Length; i++)
        {
            double offset = i - center;
            double sinc = offset == 0
                ? 2.0 * cutoff
                : Math.Sin(2.0 * Math.PI * cutoff * offset) / (Math.PI * offset);
            double ratio = offset / center;
            double window = MathNet.Numerics.SpecialFunctions.BesselI0(
                KaiserBeta * Math.Sqrt(Math.Max(0.0, 1.0 - ratio * ratio)))
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
