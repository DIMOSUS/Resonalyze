using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// A finite impulse response — the convolution kernel a processor's FIR stage runs
/// on a channel — with the numbers a simulation needs to read off it.
/// </summary>
/// <remarks>
/// <para>
/// The taps are AT THE PROCESSOR'S RATE, whatever the file they came from claimed:
/// a device convolves the samples it is given at the rate it runs, and a kernel
/// designed for another rate is simply a different filter on it. That is the same
/// rule the biquads follow (see <see cref="PreparedDspResponse"/>), and it is why
/// the rate a file declares is carried only as <see cref="DeclaredSampleRateHz"/>,
/// for the UI to warn with, never resampled to.
/// </para>
/// <para>
/// Reference equality, on purpose: two kernels are the same filter only when they
/// are the same loaded instance, which is what the render cache keys on. Re-loading
/// a file makes a new instance and so a fresh render — the cheap and honest answer
/// when the file may have changed.
/// </para>
/// </remarks>
public sealed class FirFilter
{
    /// <summary>
    /// The longest kernel accepted. The processed record is padded by the kernel's
    /// length so the convolution stays linear (see
    /// <see cref="PreparedDspResponse.RequiredTailSamples"/>), and that padding is
    /// what bounds the FFT — 131072 taps is a 1.4 s kernel at 96 kHz, beyond what
    /// any car processor runs and still one power of two on the render.
    /// </summary>
    public const int MaximumTaps = 131_072;

    private readonly double[] taps;

    /// <param name="taps">The kernel, first tap first. Copied.</param>
    /// <param name="declaredSampleRateHz">
    /// The rate the source FILE stated for these taps, or null when it stated none
    /// (a plain text file). Informational only — see the type remarks.
    /// </param>
    public FirFilter(IReadOnlyList<double> taps, int? declaredSampleRateHz = null)
    {
        ArgumentNullException.ThrowIfNull(taps);
        if (taps.Count == 0)
        {
            throw new ArgumentException("A FIR filter needs at least one tap.", nameof(taps));
        }
        if (taps.Count > MaximumTaps)
        {
            throw new ArgumentException(
                $"A FIR filter may hold at most {MaximumTaps} taps; this one has {taps.Count}.",
                nameof(taps));
        }
        if (declaredSampleRateHz is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(declaredSampleRateHz));
        }

        this.taps = new double[taps.Count];
        int peak = 0;
        int leadingZeros = 0;
        bool seenNonZero = false;
        for (int index = 0; index < taps.Count; index++)
        {
            double tap = taps[index];
            if (!double.IsFinite(tap))
            {
                throw new ArgumentException(
                    $"Tap {index} of the FIR filter is not a finite number.", nameof(taps));
            }

            this.taps[index] = tap;
            if (tap != 0 && !seenNonZero)
            {
                seenNonZero = true;
                leadingZeros = index;
            }
            if (Math.Abs(tap) > Math.Abs(this.taps[peak]))
            {
                peak = index;
            }
        }

        DeclaredSampleRateHz = declaredSampleRateHz;
        LeadingZeroCount = seenNonZero ? leadingZeros : taps.Count;
        PeakIndex = peak;
    }

    /// <summary>The kernel, first tap first.</summary>
    public ReadOnlySpan<double> Taps => taps;

    /// <summary>How many taps the kernel has.</summary>
    public int Length => taps.Length;

    /// <summary>The rate the source file stated, or null (see the type remarks).</summary>
    public int? DeclaredSampleRateHz { get; }

    /// <summary>
    /// How many EXACT zeros open the kernel. Convolving with such a kernel shifts the
    /// content by that many samples and nothing else, so this is the part of the
    /// kernel's latency that manufactures silence rather than filtering — the figure
    /// a valid-range computation subtracts (see
    /// <see cref="VirtualCrossoverAnalysis.ChainValidRange"/>). A kernel that is
    /// nothing but zeros answers with its whole length.
    /// </summary>
    public int LeadingZeroCount { get; }

    /// <summary>
    /// The tap of largest magnitude. For a linear-phase kernel this is its centre and
    /// its group delay; for a minimum-phase one it sits near the front. Shown as the
    /// kernel's delay in the editors because it is the one figure both kinds share.
    /// </summary>
    public int PeakIndex { get; }

    /// <summary>The whole kernel's energy is in its zeros: a filter that mutes the channel.</summary>
    public bool IsSilent => LeadingZeroCount == taps.Length;

    /// <summary>
    /// The kernel's response at <paramref name="z1"/> = e^{-jω}: Σ h[n]·z1^n, evaluated
    /// by Horner's rule so a long kernel costs one complex multiply per tap.
    /// </summary>
    public Complex Response(Complex z1)
    {
        Complex accumulator = Complex.Zero;
        for (int index = taps.Length - 1; index >= 0; index--)
        {
            accumulator = accumulator * z1 + taps[index];
        }

        return accumulator;
    }

    /// <summary>
    /// The kernel's response at <paramref name="frequencyHz"/> on a processor running
    /// at <paramref name="sampleRateHz"/>.
    /// </summary>
    public Complex Response(double frequencyHz, double sampleRateHz) =>
        Response(Complex.Exp(new Complex(0, -Math.Tau * frequencyHz / sampleRateHz)));

    /// <summary>
    /// Group delay τ_g = -dφ/dω of the kernel at <paramref name="z1"/> = e^{-jω}, in
    /// samples. Closed form: with H = Σ h[n]·e^{-jωn} the derivative is
    /// -j·Σ n·h[n]·e^{-jωn}, so τ_g = Re(Σ n·h[n]·e^{-jωn} / H) — exact, and never
    /// wrapped, the same way the biquad cascade's is. Undefined (NaN) where the kernel
    /// has a true zero, which is the honest answer at a null.
    /// </summary>
    public double GroupDelaySamples(Complex z1)
    {
        // Two Horner recurrences in one pass: H and Σ n·h[n]·z1^n.
        Complex response = Complex.Zero;
        Complex weighted = Complex.Zero;
        for (int index = taps.Length - 1; index >= 0; index--)
        {
            response = response * z1 + taps[index];
            weighted = weighted * z1 + index * taps[index];
        }

        return response == Complex.Zero
            ? double.NaN
            : (weighted / response).Real;
    }

    /// <summary>
    /// The kernel's spectrum on an <paramref name="length"/>-point DFT grid: bin k is
    /// the response at ω = 2πk/length. Zero-padded when the grid is longer than the
    /// kernel; refused when it is shorter, because an undersized DFT would alias the
    /// kernel in time and answer for a filter nobody loaded.
    /// </summary>
    public Complex[] Spectrum(int length)
    {
        if (length < taps.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), "The DFT grid is shorter than the kernel.");
        }

        var spectrum = new Complex[length];
        for (int index = 0; index < taps.Length; index++)
        {
            spectrum[index] = taps[index];
        }

        MathNet.Numerics.IntegralTransforms.Fourier.Forward(
            spectrum, MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);
        return spectrum;
    }
}
