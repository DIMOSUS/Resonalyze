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
    public IReadOnlyList<Complex> Responses(IReadOnlyList<double> frequenciesHz, double sampleRateHz) =>
        GridEntryFor(frequenciesHz, sampleRateHz).Responses.Value;

    /// <summary><see cref="GroupDelaySamples"/> at each frequency, kept like <see cref="Responses"/>.</summary>
    public IReadOnlyList<double> GroupDelaysSamples(IReadOnlyList<double> frequenciesHz, double sampleRateHz) =>
        GridEntryFor(frequenciesHz, sampleRateHz).GroupDelays.Value;

    /// <summary>z1 = e^{-jω} for a frequency at a rate, where every per-frequency read of a chain evaluates.</summary>
    internal static Complex UnitCirclePoint(double frequencyHz, double sampleRateHz) =>
        Complex.Exp(new Complex(0, -Math.Tau * frequencyHz / sampleRateHz));

    /// <summary>The kernel at every bin of a record <paramref name="length"/> long, on the processor's circle
    /// (<paramref name="rateRatio"/> = record rate / processor rate); shared, read it only. See docs/tech/dsp-chain-response.md#fir-bins.</summary>
    internal Complex[] RecordBins(int length, double rateRatio)
    {
        Lazy<Complex[]> bins;
        lock (recordBins)
        {
            int index = recordBins.FindIndex(entry => entry.Length == length && entry.RateRatio == rateRatio);
            if (index < 0)
            {
                // A few: one kernel can sit on channels with different record lengths.
                if (recordBins.Count == RecordBinsCapacity)
                {
                    recordBins.RemoveAt(0);
                }

                recordBins.Add((length, rateRatio, new Lazy<Complex[]>(() => ComputeRecordBins(length, rateRatio))));
                index = recordBins.Count - 1;
            }

            bins = recordBins[index].Bins;
        }

        return bins.Value;
    }

    // Exact DFT when length/rateRatio is whole and no shorter than the kernel, else chirp-z.
    private Complex[] ComputeRecordBins(int length, double rateRatio)
    {
        int half = length / 2;
        var bins = new Complex[half + 1];
        int lastBin = Math.Min(half, (int)Math.Floor(half / rateRatio));

        double grid = length / rateRatio;
        long gridLength = (long)Math.Round(grid);
        if (Math.Abs(grid - gridLength) < 1e-6 && gridLength >= taps.Length &&
            gridLength <= int.MaxValue)
        {
            Complex[] spectrum = Spectrum((int)gridLength);
            for (int i = 0; i <= lastBin; i++)
            {
                bins[i] = spectrum[i];
            }

            return bins;
        }

        Complex[] chirp = ChirpSpectrum(lastBin + 1, Math.Tau * rateRatio / length);
        Array.Copy(chirp, bins, lastBin + 1);
        return bins;
    }

    // Each entry is computed once, by its first reader, outside the lookup lock: a read of one grid never waits for another.
    private const int RecordBinsCapacity = 4;
    private const int GridCapacity = 16;
    private readonly List<(int Length, double RateRatio, Lazy<Complex[]> Bins)> recordBins = [];
    private readonly List<GridEntry> gridEntries = [];

    // Least recently used goes first; grids compare bit for bit.
    private GridEntry GridEntryFor(IReadOnlyList<double> frequenciesHz, double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(frequenciesHz);
        lock (gridEntries)
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

            var entry = new GridEntry(this, sampleRateHz, [.. frequenciesHz]);
            gridEntries.Add(entry);
            return entry;
        }
    }

    private sealed class GridEntry
    {
        private readonly double sampleRateHz;
        private readonly double[] frequenciesHz;

        public GridEntry(FirFilter kernel, double sampleRateHz, double[] frequenciesHz)
        {
            this.sampleRateHz = sampleRateHz;
            this.frequenciesHz = frequenciesHz;
            Responses = new Lazy<Complex[]>(
                () => [.. frequenciesHz.Select(frequency => kernel.Response(frequency, sampleRateHz))]);
            GroupDelays = new Lazy<double[]>(
                () => [.. frequenciesHz.Select(
                    frequency => kernel.GroupDelaySamples(UnitCirclePoint(frequency, sampleRateHz)))]);
        }

        public Lazy<Complex[]> Responses { get; }

        public Lazy<double[]> GroupDelays { get; }

        public bool Matches(IReadOnlyList<double> frequencies, double rate)
        {
            if (BitConverter.DoubleToInt64Bits(rate) != BitConverter.DoubleToInt64Bits(sampleRateHz) ||
                frequencies.Count != frequenciesHz.Length)
            {
                return false;
            }

            for (int i = 0; i < frequenciesHz.Length; i++)
            {
                if (BitConverter.DoubleToInt64Bits(frequencies[i]) !=
                    BitConverter.DoubleToInt64Bits(frequenciesHz[i]))
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
