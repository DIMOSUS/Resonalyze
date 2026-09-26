using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>A FIR kernel whose taps are at the processor's rate; the file's declared rate is informational only.</summary>
/// <remarks>Reference equality on purpose: the render cache keys on the loaded instance.</remarks>
public sealed class FirFilter
{
    /// <summary>1.4 s at 96 kHz; the record is padded by the kernel tail (up to 4·(N − 1) at 192 kHz record / 48 kHz processor), so this bounds the FFT.</summary>
    public const int MaximumTaps = 131_072;

    private readonly double[] taps;
    // Σ|h|: the largest |H| anywhere, the scale a true zero of H is judged against.
    private readonly double tapMagnitudeSum;

    /// <param name="declaredSampleRateHz">Rate the source file stated, or null. Informational only.</param>
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
        double magnitudeSum = 0;
        for (int index = 0; index < taps.Count; index++)
        {
            double tap = taps[index];
            if (!double.IsFinite(tap))
            {
                throw new ArgumentException(
                    $"Tap {index} of the FIR filter is not a finite number.", nameof(taps));
            }

            this.taps[index] = tap;
            magnitudeSum += Math.Abs(tap);
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
        tapMagnitudeSum = magnitudeSum;
        IsSymmetric = seenNonZero && MirrorsItself(this.taps, Math.Abs(this.taps[peak]) * SymmetryTolerance);
    }

    public ReadOnlySpan<double> Taps => taps;

    public int Length => taps.Length;

    private static bool MirrorsItself(double[] kernel, double tolerance)
    {
        for (int index = 0, mirror = kernel.Length - 1; index < mirror; index++, mirror--)
        {
            if (Math.Abs(kernel[index] - kernel[mirror]) > tolerance)
            {
                return false;
            }
        }

        return true;
    }

    public int? DeclaredSampleRateHz { get; }

    /// <summary>Exact zeros opening the kernel: pure shift, subtracted by <see cref="VirtualCrossoverAnalysis.ChainValidRange"/>. All-zero kernel = whole length.</summary>
    public int LeadingZeroCount { get; }

    /// <summary>Where the largest tap sits, not a group delay (near the front for minimum-phase kernels).</summary>
    public int PeakIndex { get; }

    public bool IsSilent => LeadingZeroCount == taps.Length;

    /// <summary>h[n] = h[N−1−n] within <see cref="SymmetryTolerance"/> and not silent; antisymmetric kernels do not count.</summary>
    public bool IsSymmetric { get; }

    /// <summary>Meaningful only where <see cref="IsSymmetric"/>; half a sample off-grid for even length.</summary>
    public double LinearPhaseDelaySamples => (taps.Length - 1) / 2.0;

    /// <summary>Relative to the largest tap: far above FFT rounding (1e-16), far below audible asymmetry.</summary>
    public const double SymmetryTolerance = 1e-9;

    /// <summary>Σ h[n]·z1^n by Horner's rule, <paramref name="z1"/> = e^{-jω}.</summary>
    public Complex Response(Complex z1)
    {
        Complex accumulator = Complex.Zero;
        for (int index = taps.Length - 1; index >= 0; index--)
        {
            accumulator = accumulator * z1 + taps[index];
        }

        return accumulator;
    }

    public Complex Response(double frequencyHz, double sampleRateHz) =>
        Response(UnitCirclePoint(frequencyHz, sampleRateHz));

    /// <summary><see cref="Response(double, double)"/> at each frequency, kept per rate and grid for the kernel's
    /// lifetime; the list is shared, read it only. See docs/tech/dsp-chain-response.md#fir-on-a-plotted-grid.</summary>
    public IReadOnlyList<Complex> Responses(IReadOnlyList<double> frequenciesHz, double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(frequenciesHz);
        lock (gridEntries)
        {
            GridEntry entry = GridEntryFor(frequenciesHz, sampleRateHz);
            if (entry.Responses == null)
            {
                var responses = new Complex[entry.FrequenciesHz.Length];
                for (int i = 0; i < responses.Length; i++)
                {
                    responses[i] = Response(entry.FrequenciesHz[i], sampleRateHz);
                }

                entry.Responses = responses;
            }

            return entry.Responses;
        }
    }

    /// <summary><see cref="GroupDelaySamples"/> at each frequency, kept like <see cref="Responses"/>.</summary>
    public IReadOnlyList<double> GroupDelaysSamples(IReadOnlyList<double> frequenciesHz, double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(frequenciesHz);
        lock (gridEntries)
        {
            GridEntry entry = GridEntryFor(frequenciesHz, sampleRateHz);
            if (entry.GroupDelays == null)
            {
                var delays = new double[entry.FrequenciesHz.Length];
                for (int i = 0; i < delays.Length; i++)
                {
                    delays[i] = GroupDelaySamples(UnitCirclePoint(entry.FrequenciesHz[i], sampleRateHz));
                }

                entry.GroupDelays = delays;
            }

            return entry.GroupDelays;
        }
    }

    private static Complex UnitCirclePoint(double frequencyHz, double sampleRateHz) =>
        Complex.Exp(new Complex(0, -Math.Tau * frequencyHz / sampleRateHz));

    // A few grids per kernel: a plot, the hybrid's reference grid and the level read-outs' bands.
    private const int GridCapacity = 8;
    private readonly List<GridEntry> gridEntries = [];

    // Caller holds the lock. Least recently used goes first; grids compare bit for bit.
    private GridEntry GridEntryFor(IReadOnlyList<double> frequenciesHz, double sampleRateHz)
    {
        for (int index = 0; index < gridEntries.Count; index++)
        {
            GridEntry cached = gridEntries[index];
            if (cached.Matches(frequenciesHz, sampleRateHz))
            {
                gridEntries.RemoveAt(index);
                gridEntries.Add(cached);
                return cached;
            }
        }

        if (gridEntries.Count == GridCapacity)
        {
            gridEntries.RemoveAt(0);
        }

        var entry = new GridEntry(sampleRateHz, [.. frequenciesHz]);
        gridEntries.Add(entry);
        return entry;
    }

    private sealed class GridEntry(double sampleRateHz, double[] frequenciesHz)
    {
        public double[] FrequenciesHz { get; } = frequenciesHz;

        public Complex[]? Responses { get; set; }

        public double[]? GroupDelays { get; set; }

        public bool Matches(IReadOnlyList<double> frequencies, double rate)
        {
            if (BitConverter.DoubleToInt64Bits(rate) != BitConverter.DoubleToInt64Bits(sampleRateHz) ||
                frequencies.Count != FrequenciesHz.Length)
            {
                return false;
            }

            for (int i = 0; i < FrequenciesHz.Length; i++)
            {
                if (BitConverter.DoubleToInt64Bits(frequencies[i]) !=
                    BitConverter.DoubleToInt64Bits(FrequenciesHz[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Closed-form, unwrapped group delay in samples: Re(Σ n·h[n]·z1^n / H). NaN where |H| is a true zero relative to Σ|h|.</summary>
    public double GroupDelaySamples(Complex z1)
    {
        Complex response = Complex.Zero;
        Complex weighted = Complex.Zero;
        for (int index = taps.Length - 1; index >= 0; index--)
        {
            response = response * z1 + taps[index];
            weighted = weighted * z1 + index * taps[index];
        }

        return response.Magnitude <= NullRelativeMagnitude * tapMagnitudeSum
            ? double.NaN
            : (weighted / response).Real;
    }

    // 1e-12 of Σ|h|: 1e5 × the rounding of a 131072-tap Horner sum.
    private const double NullRelativeMagnitude = 1e-12;

    /// <summary>Bin k at ω = 2πk/length; refused when shorter than the kernel (time aliasing).</summary>
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

    /// <summary>Response at ω_k = k·<paramref name="omegaStep"/> on any grid via chirp-z (Bluestein): three FFTs instead of N·count multiplies.
    /// Exact to a few 1e-11 of Σ|h|.</summary>
    public Complex[] ChirpSpectrum(int count, double omegaStep)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (!double.IsFinite(omegaStep))
        {
            throw new ArgumentOutOfRangeException(nameof(omegaStep));
        }

        int n = taps.Length;
        // L ≥ N + count − 1 keeps the wrapped negative chirp half disjoint from the positive half.
        int convolutionLength = DspMath.NextPowerOfTwo(n + count - 1);
        var weighted = new Complex[convolutionLength];
        var chirp = new Complex[convolutionLength];
        for (int index = 0; index < n; index++)
        {
            weighted[index] = taps[index] * Chirp(index, omegaStep);
        }

        int farthest = Math.Max(n, count) - 1;
        for (int m = 0; m <= farthest; m++)
        {
            Complex inverse = Complex.Conjugate(Chirp(m, omegaStep));
            if (m < count)
            {
                chirp[m] = inverse;
            }
            if (m > 0 && m < n)
            {
                chirp[convolutionLength - m] = inverse;
            }
        }

        MathNet.Numerics.IntegralTransforms.Fourier.Forward(
            weighted, MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);
        MathNet.Numerics.IntegralTransforms.Fourier.Forward(
            chirp, MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);
        for (int index = 0; index < convolutionLength; index++)
        {
            weighted[index] *= chirp[index];
        }

        MathNet.Numerics.IntegralTransforms.Fourier.Inverse(
            weighted, MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);

        var result = new Complex[count];
        for (int k = 0; k < count; k++)
        {
            result[k] = weighted[k] * Chirp(k, omegaStep);
        }

        return result;
    }

    // m² is exact in a double up to 2^26.
    private static Complex Chirp(int m, double omegaStep)
    {
        double square = (double)m * m;
        return Complex.FromPolarCoordinates(1.0, -omegaStep * square / 2.0);
    }
}
