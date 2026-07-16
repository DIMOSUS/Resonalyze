namespace Resonalyze.Dsp.Tests;

/// <summary>
/// SmoothBinsHann evaluates its Hann-weighted fractional-octave average on log-spaced
/// anchors and interpolates between them, refining a span until the chord agrees with the
/// exact curve at its midpoint. Its contract is therefore an error BOUND, and nothing on
/// the public surface pins that — so these tests keep the exact convolution as their own
/// reference and hold the real implementation against it.
/// <para>
/// The inputs are deliberately hostile. "Smoothed" does not mean "linear": across a band
/// edge or a steep stopband the average falls exponentially, and a chord over it reads
/// high. A seeded grid alone was 10 dB out at the sweep's edge; the midpoint refinement is
/// what these tests exist to hold in place.
/// </para>
/// </summary>
public sealed class SmoothBinsHannTests
{
    private const int BinCount = 16_384;              // a 32768-point gated FFT's half
    private const double BinWidthHz = 48_000.0 / 32_768.0;
    private const double EnvelopeOctaves = 1.0;       // the reliability gate's envelope
    private const double GroupDelayOctaves = 1.0 / 6.0;

    // The bound the implementation promises: its tolerance is 0.005 relative, i.e.
    // ~0.043 dB. A little headroom for the exactly-evaluated stretches around it.
    private const double MaximumErrorDb = 0.1;

    /// <summary>
    /// The original implementation, kept here and only here: the exact Hann-weighted
    /// average at every bin. It is what the shipped smoother approximates, and the only
    /// honest reference for "how far off is the approximation".
    /// </summary>
    private static double[] Exact(
        double[] source, double smoothingOctaves, double binWidthHz, double minHalfWidthHz)
    {
        int count = source.Length;
        var result = new double[count];
        double frequencyRatio = Math.Pow(2.0, smoothingOctaves * 0.5);
        double halfWidthFloor = Math.Max(minHalfWidthHz, binWidthHz * 2.0);
        for (int i = 1; i < count; i++)
        {
            double halfDelta = Math.Max(
                i * binWidthHz * (frequencyRatio - 1.0), halfWidthFloor);
            int window = (int)Math.Ceiling(halfDelta / binWidthHz);
            double weightedSum = 0.0;
            double weightSum = 0.0;
            for (int j = Math.Max(i - window, 1);
                j <= Math.Min(i + window, count - 1);
                j++)
            {
                double x = (j - i) * binWidthHz / halfDelta;
                if (Math.Abs(x) >= 1.0)
                {
                    continue;
                }

                double weight = 0.5 * (1.0 + Math.Cos(Math.PI * x));
                weightedSum += source[j] * weight;
                weightSum += weight;
            }

            result[i] = weightSum > 0.0 ? weightedSum / weightSum : source[i];
        }

        return result;
    }

    public static TheoryData<string, double> HostileInputs() => new()
    {
        { "smooth broadband", EnvelopeOctaves },
        { "smooth broadband", GroupDelayOctaves },
        { "step 40 dB", EnvelopeOctaves },
        { "step 40 dB", GroupDelayOctaves },
        { "step 80 dB", EnvelopeOctaves },
        { "step 80 dB", GroupDelayOctaves },
        { "band edge", EnvelopeOctaves },
        { "band edge", GroupDelayOctaves },
        { "narrow peak", EnvelopeOctaves },
        { "narrow peak", GroupDelayOctaves },
        { "deep narrow notch", EnvelopeOctaves },
        { "deep narrow notch", GroupDelayOctaves },
        { "cabin response", EnvelopeOctaves },
        { "cabin response", GroupDelayOctaves }
    };

    [Theory]
    [MemberData(nameof(HostileInputs))]
    public void Smoothing_StaysWithinItsErrorBound(string shape, double octaves)
    {
        double[] source = Build(shape);

        double[] actual = DataHelper.SmoothBinsHann(source, octaves, BinWidthHz, 0.0);
        double[] exact = Exact(source, octaves, BinWidthHz, 0.0);

        double worst = 0;
        int worstBin = 0;
        for (int i = 1; i < BinCount; i++)
        {
            if (exact[i] <= 0 || actual[i] <= 0)
            {
                continue;
            }

            double error = Math.Abs(20.0 * Math.Log10(actual[i] / exact[i]));
            if (error > worst)
            {
                worst = error;
                worstBin = i;
            }
        }

        Assert.True(
            worst <= MaximumErrorDb,
            $"{shape} at 1/{1 / octaves:0.#} oct: {worst:0.000} dB at bin {worstBin} " +
            $"({worstBin * BinWidthHz:0} Hz).");
    }

    [Theory]
    [MemberData(nameof(HostileInputs))]
    public void Smoothing_DoesNotMoveTheReliabilityGate(string shape, double octaves)
    {
        // What the envelope is FOR. A single bin sitting exactly on the threshold may
        // flip for any non-zero error and nobody could see it; a flipped RUN is a visible
        // band of phase appearing or vanishing, and that must not happen.
        double[] source = Build(shape);
        double[] actual = DataHelper.SmoothBinsHann(source, octaves, BinWidthHz, 0.0);
        double[] exact = Exact(source, octaves, BinWidthHz, 0.0);

        double peak = source.Max();
        double absoluteFloor = peak * Math.Pow(10.0, -60.0 / 20.0);
        double localGateRatio = Math.Pow(10.0, -30.0 / 20.0);

        int run = 0;
        int longestRun = 0;
        int flips = 0;
        for (int i = 1; i < BinCount; i++)
        {
            bool byExact = source[i] >= absoluteFloor && source[i] >= exact[i] * localGateRatio;
            bool byActual = source[i] >= absoluteFloor && source[i] >= actual[i] * localGateRatio;
            if (byExact != byActual)
            {
                flips++;
                run++;
                longestRun = Math.Max(longestRun, run);
            }
            else
            {
                run = 0;
            }
        }

        Assert.True(
            longestRun <= 2,
            $"{shape} at 1/{1 / octaves:0.#} oct: {flips} gate decisions flipped, " +
            $"longest run {longestRun} bins.");
    }

    [Fact]
    public void Smoothing_KeepsASmoothedEnergyPositive()
    {
        // The group delay divides the smoothed numerator by the smoothed energy. A
        // negative or zero energy there would blow the ratio up, so the kernel is
        // non-negative and the interpolation between two positive anchors must stay
        // positive too.
        double[] energy = Build("cabin response").Select(x => x * x).ToArray();

        double[] smoothed = DataHelper.SmoothBinsHann(
            energy, GroupDelayOctaves, BinWidthHz, 0.0);

        Assert.All(smoothed.Skip(1), value => Assert.True(value > 0, $"energy {value}"));
    }

    [Fact]
    public void Smoothing_HandlesASignedSource()
    {
        // The group-delay numerator is signed (a dot product, not a magnitude), so the
        // bound has to hold through zero crossings — where a relative tolerance alone
        // would be meaningless and the peak-referenced floor carries it.
        var signed = new double[BinCount];
        for (int i = 1; i < BinCount; i++)
        {
            signed[i] = Math.Sin(i * 0.002) * Math.Exp(-i / 8_000.0);
        }

        double[] actual = DataHelper.SmoothBinsHann(
            signed, GroupDelayOctaves, BinWidthHz, 0.0);
        double[] exact = Exact(signed, GroupDelayOctaves, BinWidthHz, 0.0);

        double peak = signed.Max(Math.Abs);
        double worst = 0;
        for (int i = 1; i < BinCount; i++)
        {
            worst = Math.Max(worst, Math.Abs(actual[i] - exact[i]));
        }

        Assert.True(worst <= peak * 0.01, $"signed source off by {worst / peak:P2} of peak.");
    }

    [Fact]
    public void Smoothing_LeavesBinZeroAloneAndReachesNyquist()
    {
        // Bin 0 is excluded by contract (DC carries no phase); the last bin is an anchor
        // in its own right, so it must hold the exact value rather than an extrapolation.
        double[] source = Build("cabin response");

        double[] actual = DataHelper.SmoothBinsHann(source, EnvelopeOctaves, BinWidthHz, 0.0);
        double[] exact = Exact(source, EnvelopeOctaves, BinWidthHz, 0.0);

        Assert.Equal(0.0, actual[0]);
        Assert.Equal(exact[^1], actual[^1], 9);
    }

    [Fact]
    public void Smoothing_HonoursAMinimumHalfWidth()
    {
        // The group-delay path floors the kernel in Hz, which widens it at the low end
        // where a frequency-proportional width would collapse to a couple of bins.
        double[] source = Build("cabin response");
        const double minHalfWidthHz = 114.0;

        double[] actual = DataHelper.SmoothBinsHann(
            source, GroupDelayOctaves, BinWidthHz, minHalfWidthHz);
        double[] exact = Exact(source, GroupDelayOctaves, BinWidthHz, minHalfWidthHz);

        double worst = 0;
        for (int i = 1; i < BinCount; i++)
        {
            if (exact[i] > 0 && actual[i] > 0)
            {
                worst = Math.Max(worst, Math.Abs(20.0 * Math.Log10(actual[i] / exact[i])));
            }
        }

        Assert.True(worst <= MaximumErrorDb, $"{worst:0.000} dB with a {minHalfWidthHz} Hz floor.");
    }

    private static double[] Build(string shape)
    {
        var source = new double[BinCount];
        var random = new Random(7);
        for (int i = 1; i < BinCount; i++)
        {
            double f = i * BinWidthHz;
            source[i] = shape switch
            {
                "smooth broadband" => 1.0 / Math.Sqrt(1 + Math.Pow(80.0 / f, 4)),
                // The chord's worst enemy: an instant 40 / 80 dB wall.
                "step 40 dB" => i < BinCount / 2 ? 1.0 : 100.0,
                "step 80 dB" => i < BinCount / 2 ? 1e-4 : 1.0,
                // A sweep dies past its bandwidth; the envelope falls off a cliff.
                "band edge" => f > 20_000 ? 1e-4 : 1.0,
                "narrow peak" => 1.0 + 999.0 * Math.Exp(-Math.Pow((f - 1_000) / 20.0, 2)),
                "deep narrow notch" =>
                    Math.Abs(1.0 - 0.9999 * Math.Exp(-Math.Pow((f - 1_500) / 15.0, 2))),
                _ => (1.0 / Math.Sqrt(1 + Math.Pow(300.0 / f, 8)) /
                        Math.Sqrt(1 + Math.Pow(f / 3_000.0, 8)) *
                        Math.Abs(1.0 - 0.999 * Math.Exp(-Math.Pow((f - 1_500) / 25.0, 2)))) +
                     (1e-5 * random.NextDouble())
            };
        }

        return source;
    }
}
