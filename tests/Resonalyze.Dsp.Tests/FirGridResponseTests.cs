using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class FirGridResponseTests
{
    private const int Rate = 48_000;

    [Fact]
    public void Responses_AreTheDirectSumsBitForBit_ComputedAndCached()
    {
        FirFilter fir = Kernel(65, seed: 1);
        List<double> grid = [.. EqualizationCurve.LogFrequencyGrid(20, 20_000, 40)];

        IReadOnlyList<Complex> first = fir.Responses(grid, Rate);
        IReadOnlyList<Complex> second = fir.Responses([.. grid], Rate);

        Assert.Same(first, second);
        for (int i = 0; i < grid.Count; i++)
        {
            AssertSameBits(fir.Response(grid[i], Rate), first[i]);
        }

        Assert.Throws<NotSupportedException>(() => ((IList<Complex>)first)[0] = Complex.One);
        Assert.Throws<NotSupportedException>(() => ((IList<double>)fir.GroupDelaysSamples(grid, Rate))[0] = 0);
    }

    [Fact]
    public void Responses_FollowTheRateAndEveryPointOfTheGrid()
    {
        FirFilter fir = Kernel(33, seed: 2);
        List<double> grid = [.. EqualizationCurve.LogFrequencyGrid(20, 20_000, 24)];
        IReadOnlyList<Complex> at48 = fir.Responses(grid, Rate);

        IReadOnlyList<Complex> at96 = fir.Responses(grid, 96_000);
        List<double> firstMoved = [Math.BitIncrement(grid[0]), .. grid.Skip(1)];
        List<double> lastMoved = [.. grid.SkipLast(1), Math.BitIncrement(grid[^1])];

        Assert.NotSame(at48, at96);
        foreach (List<double> moved in new[] { firstMoved, lastMoved })
        {
            IReadOnlyList<Complex> responses = fir.Responses(moved, Rate);
            Assert.NotSame(at48, responses);
            for (int i = 0; i < grid.Count; i++)
            {
                AssertSameBits(fir.Response(grid[i], 96_000), at96[i]);
                AssertSameBits(fir.Response(moved[i], Rate), responses[i]);
            }
        }
    }

    [Fact]
    public void Responses_ReadAgainAfterEviction_AreStillTheDirectSums()
    {
        FirFilter fir = Kernel(33, seed: 3);
        List<double> grid = [.. EqualizationCurve.LogFrequencyGrid(20, 20_000, 24)];
        IReadOnlyList<Complex> first = fir.Responses(grid, Rate);
        for (int other = 0; other < 20; other++)
        {
            fir.Responses([.. EqualizationCurve.LogFrequencyGrid(30 + other, 18_000, 24)], Rate);
        }

        IReadOnlyList<Complex> again = fir.Responses(grid, Rate);

        Assert.NotSame(first, again);
        for (int i = 0; i < grid.Count; i++)
        {
            AssertSameBits(first[i], again[i]);
        }
    }

    [Fact]
    public void GroupDelays_AreTheDirectReadsBitForBit_NullIncluded()
    {
        // [1, 1] has a true null at Nyquist, where the group delay is NaN.
        FirFilter nulling = new([1.0, 1.0]);
        FirFilter fir = Kernel(65, seed: 4);
        List<double> grid = [.. EqualizationCurve.LogFrequencyGrid(20, 20_000, 40), Rate / 2.0];

        foreach (FirFilter kernel in new[] { nulling, fir })
        {
            IReadOnlyList<double> delays = kernel.GroupDelaysSamples(grid, Rate);
            Assert.Same(delays, kernel.GroupDelaysSamples(grid, Rate));
            for (int i = 0; i < grid.Count; i++)
            {
                double direct = kernel.GroupDelaySamples(
                    Complex.Exp(new Complex(0, -Math.Tau * grid[i] / Rate)));
                AssertSameBits(direct, delays[i]);
            }
        }

        Assert.True(double.IsNaN(nulling.GroupDelaysSamples(grid, Rate)[^1]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AChainOnAGrid_IsItsPointReadsBitForBit(bool withFir)
    {
        var chain = new DspChannelChain(
            GainDb: -3.5,
            DelayMs: 1.37,
            InvertPolarity: true,
            Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_000, 24),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 90, 12)),
            Peq: new EqualizationCurve([new PeqBand(1_000, 2.5, -4)], -1),
            PhaseRotation: new PhaseRotationSpec(60, 3_000),
            Fir: withFir ? Kernel(129, seed: 5) : null);
        List<double> grid = [.. EqualizationCurve.LogFrequencyGrid(20, 20_000, 60), 30_000];

        foreach (int rate in new[] { 48_000, 96_000 })
        {
            PreparedDspResponse prepared = PreparedDspResponse.Create(chain, rate);
            for (int pass = 0; pass < 2; pass++)
            {
                Complex[] responses = prepared.Responses(grid);
                double[] delays = prepared.GroupDelaysMs(grid);
                for (int i = 0; i < grid.Count; i++)
                {
                    AssertSameBits(prepared.Response(grid[i]), responses[i]);
                    AssertSameBits(prepared.GroupDelayMs(grid[i]), delays[i]);
                }
            }
        }
    }

    [Fact]
    public void RecordBins_AreWhatAColdKernelGives_ForEveryLengthAndRatePair()
    {
        FirFilter shared = Kernel(33, seed: 6);
        (int Length, double RateRatio)[] reads =
        [
            (256, 1.0), (512, 1.0), (256, 0.5), (256, 44_100.0 / 48_000), (256, 1.0), (512, 2.0),
        ];

        foreach ((int length, double rateRatio) in reads)
        {
            Complex[] cold = new FirFilter(shared.Taps.ToArray()).RecordBins(length, rateRatio);
            Complex[] cached = shared.RecordBins(length, rateRatio);
            Assert.Equal(cold.Length, cached.Length);
            for (int i = 0; i < cold.Length; i++)
            {
                AssertSameBits(cold[i], cached[i]);
            }
        }
    }

    private static FirFilter Kernel(int taps, int seed)
    {
        var random = new Random(seed);
        return new FirFilter([.. Enumerable.Range(0, taps).Select(_ => random.NextDouble() - 0.5)]);
    }

    private static void AssertSameBits(Complex expected, Complex actual)
    {
        AssertSameBits(expected.Real, actual.Real);
        AssertSameBits(expected.Imaginary, actual.Imaginary);
    }

    private static void AssertSameBits(double expected, double actual) =>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
}
