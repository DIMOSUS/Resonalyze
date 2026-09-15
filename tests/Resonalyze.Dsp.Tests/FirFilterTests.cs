using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

/// <summary>A shifted unit-sample kernel IS a delay: FIR and delay paths must match at every rate pairing.</summary>
public sealed class FirFilterTests
{
    private const int Length = 16_384;
    private const int ArrivalSample = 64;

    [Fact]
    public void Kernel_ReadsItsOwnShape()
    {
        var fir = new FirFilter([0, 0, 0, 1.0, -0.5, 0.25], 48_000);

        Assert.Equal(6, fir.Length);
        Assert.Equal(3, fir.LeadingZeroCount);
        Assert.Equal(3, fir.PeakIndex);
        Assert.Equal(48_000, fir.DeclaredSampleRateHz);
        Assert.False(fir.IsSilent);

        var silent = new FirFilter([0.0, 0.0, 0.0]);
        Assert.True(silent.IsSilent);
        Assert.Equal(3, silent.LeadingZeroCount);
        Assert.Null(silent.DeclaredSampleRateHz);
    }

    [Fact]
    public void Kernel_RefusesWhatItCannotConvolve()
    {
        Assert.Throws<ArgumentException>(() => new FirFilter(Array.Empty<double>()));
        Assert.Throws<ArgumentException>(() => new FirFilter([1.0, double.NaN]));
        Assert.Throws<ArgumentException>(
            () => new FirFilter(new double[FirFilter.MaximumTaps + 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FirFilter([1.0], 0));
    }

    [Fact]
    public void AShiftedUnitSample_HasTheDelaysResponseAndGroupDelay()
    {
        var fir = new FirFilter([0, 0, 0, 1.0]);
        foreach (double frequencyHz in new[] { 100.0, 1_000.0, 12_000.0 })
        {
            double omega = Math.Tau * frequencyHz / 48_000;
            Complex z1 = Complex.Exp(new Complex(0, -omega));
            Complex response = fir.Response(z1);

            Assert.Equal(1.0, response.Magnitude, 12);
            Assert.Equal(WrapToPi(-3 * omega), WrapToPi(response.Phase), 12);
            Assert.Equal(3.0, fir.GroupDelaySamples(z1), 9);
        }
    }

    [Fact]
    public void ALinearPhaseKernel_HasAConstantGroupDelayOfHalfItsLength()
    {
        double[] taps = Enumerable.Range(0, 9)
            .Select(n => 0.5 * (1 - Math.Cos(Math.Tau * (n + 1) / 10.0)))
            .ToArray();
        var fir = new FirFilter(taps);

        Assert.Equal(4, fir.PeakIndex);
        foreach (double frequencyHz in new[] { 50.0, 500.0, 2_000.0 })
        {
            Complex z1 = Complex.Exp(new Complex(0, -Math.Tau * frequencyHz / 48_000));
            Assert.Equal(4.0, fir.GroupDelaySamples(z1), 9);
        }
    }

    [Fact]
    public void Spectrum_IsTheKernelsDft_AndRefusesAGridShorterThanTheKernel()
    {
        var fir = new FirFilter([1.0, 0.5, 0.25]);
        Complex[] spectrum = fir.Spectrum(8);

        for (int k = 0; k < 8; k++)
        {
            Complex expected = fir.Response(Complex.Exp(new Complex(0, -Math.Tau * k / 8)));
            Assert.Equal(expected.Real, spectrum[k].Real, 12);
            Assert.Equal(expected.Imaginary, spectrum[k].Imaginary, 12);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => fir.Spectrum(2));
    }

    [Fact]
    public void ChirpSpectrum_MatchesTheDirectSum_OnAGridNoDftLandsOn()
    {
        // 44.1 kHz bins on a 48 kHz processor: chirp-z must equal the direct sum at every bin.
        var random = new Random(1234);
        double[] taps = Enumerable.Range(0, 1_000).Select(_ => random.NextDouble() * 2 - 1).ToArray();
        var fir = new FirFilter(taps);
        double scale = taps.Sum(Math.Abs);
        double omegaStep = Math.Tau * (44_100.0 / 48_000.0) / 4_096;

        Complex[] chirp = fir.ChirpSpectrum(2_049, omegaStep);

        Assert.Equal(2_049, chirp.Length);
        double worst = 0;
        for (int k = 0; k < chirp.Length; k++)
        {
            Complex direct = fir.Response(Complex.Exp(new Complex(0, -omegaStep * k)));
            worst = Math.Max(worst, (chirp[k] - direct).Magnitude);
        }

        Assert.True(worst < 1e-10 * scale, $"chirp-z off the direct sum by {worst / scale:E2} of Σ|h|");
    }

    [Fact]
    public void GroupDelay_IsUndefinedAtAKernelsNull_EvenWhereRoundingLeavesADust()
    {
        // e^{-jπ} is not exactly −1, so H(π) lands near 1e-17; the answer is still NaN.
        var fir = new FirFilter([0.5, 0.5]);
        Complex z1 = Complex.Exp(new Complex(0, -Math.PI));

        Assert.NotEqual(Complex.Zero, fir.Response(z1));
        Assert.True(double.IsNaN(fir.GroupDelaySamples(z1)));
        Assert.Equal(0.5, fir.GroupDelaySamples(Complex.Exp(new Complex(0, -0.3))), 9);
    }

    [Fact]
    public void TextFile_ReadsOneCoefficientPerLine_SkippingHeadersAndComments()
    {
        string text =
            "* Impulse Response data saved by REW V5.31\r\n" +
            "* Sample rate: 48000\r\n" +
            "// a designer's remark\r\n" +
            "\r\n" +
            "0\r\n" +
            "1.5e-3\r\n" +
            "  -0.25  \r\n" +
            "1\r\n";

        FirFilter fir = FirFilterTextFile.Parse(text);

        Assert.Equal(new[] { 0.0, 1.5e-3, -0.25, 1.0 }, fir.Taps.ToArray());
        Assert.Equal(48_000, fir.DeclaredSampleRateHz);
        Assert.Equal(1, fir.LeadingZeroCount);
        Assert.Equal(3, fir.PeakIndex);
    }

    [Fact]
    public void TextFile_WithoutAHeader_DeclaresNoRate()
    {
        FirFilter fir = FirFilterTextFile.Parse("0.5\n0.5\n");

        Assert.Equal(2, fir.Length);
        Assert.Null(fir.DeclaredSampleRateHz);
    }

    [Fact]
    public void TextFile_RefusesAFileWithNoCoefficients_AndNamesATwoColumnFile()
    {
        InvalidDataException empty = Assert.Throws<InvalidDataException>(
            () => FirFilterTextFile.Parse("* header only\n"));
        Assert.Contains("one coefficient per line", empty.Message);

        InvalidDataException columns = Assert.Throws<InvalidDataException>(
            () => FirFilterTextFile.Parse("0.000, 0.5\n0.001, 0.25\n"));
        Assert.Contains("several numbers per line", columns.Message);
    }

    [Fact]
    public void TextFile_ReadsADecimalCommaAsADecimalSeparator()
    {
        // A locale writing 0,5: every line is a tap, no holes.
        FirFilter fir = FirFilterTextFile.Parse("0\r\n0\r\n1\r\n0,5\r\n-0,25\r\n1,5e-3\r\n0\r\n");

        Assert.Equal(new[] { 0.0, 0.0, 1.0, 0.5, -0.25, 1.5e-3, 0.0 }, fir.Taps.ToArray());
        Assert.Equal(2, fir.LeadingZeroCount);
    }

    [Fact]
    public void TextFile_RefusesAFileThatMixesDecimalPointsAndCommas()
    {
        // Mixed separators are ambiguous: refuse rather than load a kernel with a hole.
        InvalidDataException mixed = Assert.Throws<InvalidDataException>(
            () => FirFilterTextFile.Parse("0.5\n0,25\n0.125\n"));
        Assert.Contains("decimal comma", mixed.Message);
    }

    [Fact]
    public void TextFile_RefusesAMalformedLineInsideTheKernel_ButTakesAComment()
    {
        // A skipped tap shifts every later one: a different filter.
        InvalidDataException columns = Assert.Throws<InvalidDataException>(
            () => FirFilterTextFile.Parse("0.1\n0.2\n0.3 0.4\n0.5\n"));
        Assert.Contains("Line 3", columns.Message);
        Assert.Contains("0.3 0.4", columns.Message);

        InvalidDataException garbage = Assert.Throws<InvalidDataException>(
            () => FirFilterTextFile.Parse("* header\r\n\r\n0.1\r\n0.25 garbage\r\n0.5\r\n"));
        Assert.Contains("Line 4", garbage.Message);

        FirFilter fir = FirFilterTextFile.Parse(
            "// rePhase\n0.1\n\n# the centre tap\n0.2\n; and a trailing remark\n0.3\n");
        Assert.Equal(new[] { 0.1, 0.2, 0.3 }, fir.Taps.ToArray());
    }

    [Fact]
    public void TextFile_RefusesAKernelPastTheTapCeiling()
    {
        string text = string.Join('\n', Enumerable.Repeat("0.001", FirFilter.MaximumTaps + 1));

        Assert.Throws<InvalidDataException>(() => FirFilterTextFile.Parse(text));
    }

    [Fact]
    public void ChainResponse_CarriesTheKernel()
    {
        var fir = new FirFilter([0, 0, 0, 1.0]);
        var chain = new DspChannelChain(Fir: fir);
        const double frequencyHz = 1_000;

        Complex response = chain.Response(frequencyHz, 48_000);
        Complex prepared = PreparedDspResponse.Create(chain, 48_000).Response(frequencyHz);

        double expectedPhase = -Math.Tau * frequencyHz * 3 / 48_000;
        Assert.Equal(expectedPhase, WrapToPi(response.Phase), 12);
        Assert.Equal(expectedPhase, WrapToPi(prepared.Phase), 12);
        Assert.Equal(1.0, prepared.Magnitude, 12);
        Assert.Equal(
            3.0 / 48_000 * 1_000,
            PreparedDspResponse.Create(chain, 48_000).GroupDelayMs(frequencyHz),
            9);
    }

    [Fact]
    public void AUnitSampleKernel_LeavesTheRecordExactlyAlone()
    {
        Complex[] record = BandLimitedArrival(48_000, Length);
        var identity = new DspChannelChain(Fir: new FirFilter([1.0]));

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(record, identity, 48_000, 48_000);
        Complex[] bare = VirtualCrossoverAnalysis.ApplyChain(
            record, DspChannelChain.Identity, 48_000, 48_000);

        AssertSameRecord(bare, processed, 1e-9);
    }

    [Theory]
    // Same rate, 2x grid, 0.5x grid, and 44.1 vs 48 (no common grid: bin by bin).
    [InlineData(48_000, 48_000)]
    [InlineData(48_000, 96_000)]
    [InlineData(96_000, 48_000)]
    [InlineData(44_100, 48_000)]
    public void AShiftedUnitSampleKernel_IsTheSameRecordAsTheDelay(
        int recordRate,
        int processorRate)
    {
        // δ[n − k] at the PROCESSOR rate is k processor samples; any grid mismatch shows as a rate error.
        const int shift = 37;
        var taps = new double[shift + 1];
        taps[shift] = 1.0;
        Complex[] record = BandLimitedArrival(recordRate, Length);
        var viaFir = new DspChannelChain(Fir: new FirFilter(taps));
        var viaDelay = new DspChannelChain(DelayMs: shift * 1_000.0 / processorRate);

        Complex[] firRecord = VirtualCrossoverAnalysis.ApplyChain(
            record, viaFir, recordRate, processorRate);
        Complex[] delayRecord = VirtualCrossoverAnalysis.ApplyChain(
            record, viaDelay, recordRate, processorRate);

        AssertSameRecord(delayRecord, firRecord, 1e-9);
    }

    [Fact]
    public void AKernelBesideBiquads_MultipliesIntoTheSameCascade()
    {
        const int shift = 11;
        var taps = new double[shift + 1];
        taps[shift] = 1.0;
        CrossoverSpec lowPass = new(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_000, 24));
        Complex[] record = BandLimitedArrival(48_000, Length);

        Complex[] firRecord = VirtualCrossoverAnalysis.ApplyChain(
            record,
            new DspChannelChain(Crossover: lowPass, Fir: new FirFilter(taps)),
            48_000,
            96_000);
        Complex[] delayRecord = VirtualCrossoverAnalysis.ApplyChain(
            record,
            new DspChannelChain(DelayMs: shift * 1_000.0 / 96_000, Crossover: lowPass),
            48_000,
            96_000);

        AssertSameRecord(delayRecord, firRecord, 1e-9);
    }

    [Fact]
    public void ARealKernel_LeavesARealRecord()
    {
        // Conjugate mirroring must hold for FIR bins, or the inverse FFT leaks into the imaginary half.
        double[] taps = Enumerable.Range(0, 64)
            .Select(n => Math.Sin(n * 0.37) * Math.Exp(-n / 20.0))
            .ToArray();
        Complex[] record = BandLimitedArrival(48_000, Length);

        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            record, new DspChannelChain(Fir: new FirFilter(taps)), 48_000, 48_000);

        double peak = processed.Max(sample => sample.Magnitude);
        Assert.True(
            processed.Max(sample => Math.Abs(sample.Imaginary)) < peak * 1e-9,
            "the processed record has an imaginary part");
    }

    [Fact]
    public void TailPadding_GrowsByTheKernelInTheRecordsSamples()
    {
        // Added outside the clamp: a convolution's tail does not decay away.
        PreparedDspResponse bare = PreparedDspResponse.Create(DspChannelChain.Identity, 96_000);
        PreparedDspResponse withFir = PreparedDspResponse.Create(
            new DspChannelChain(Fir: new FirFilter(new double[1_000])), 96_000);

        int bareTail = bare.RequiredTailSamples(120, 8_192, 262_144, 48_000);
        int firTail = withFir.RequiredTailSamples(120, 8_192, 262_144, 48_000);

        Assert.Equal(8_192, bareTail);
        Assert.Equal(8_192 + 500, firTail);
        Assert.False(withFir.IsTimeDomainScaleOnly);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(1_000, 999)]
    public void TailPadding_IsTheKernelsLengthLessOne_AtEqualRates(int taps, int expectedExtra)
    {
        // N − 1 samples, not N: the caller rounds up to a power of two, so one extra sample can double it.
        PreparedDspResponse withFir = PreparedDspResponse.Create(
            new DspChannelChain(Fir: new FirFilter(new double[taps])), 48_000);

        Assert.Equal(8_192 + expectedExtra, withFir.RequiredTailSamples(120, 8_192, 262_144, 48_000));
    }

    [Fact]
    public void ValidRange_StartsAfterTheKernelsLeadingZeros_AndEndsAfterItsTail()
    {
        // Three leading zeros shift the start by 3 processor samples, the 7 taps extend the end by N − 1 = 6; doubled at the 2x record rate.
        var chain = new DspChannelChain(Fir: new FirFilter([0, 0, 0, 1.0, 0.5, 0.25, 0.125]));

        ValidSampleRange range = VirtualCrossoverAnalysis.ChainValidRange(
            inputLength: 100, chain, sampleRate: 96_000, processorSampleRate: 48_000, outputLength: 1_024);

        Assert.Equal(6, range.StartSample);
        Assert.Equal(100 + 12, range.EndSample);
    }

    private static Complex[] BandLimitedArrival(int sampleRate, int length)
    {
        var record = new Complex[length];
        double widthSeconds = 1.0 / 12_000.0;
        int half = (int)Math.Round(widthSeconds * sampleRate);
        int center = (int)Math.Round(ArrivalSample / 48_000.0 * sampleRate);
        for (int i = -half; i <= half; i++)
        {
            double phase = Math.PI * i / half;
            record[center + i] = 0.5 * (1.0 + Math.Cos(phase));
        }

        return record;
    }

    private static void AssertSameRecord(Complex[] expected, Complex[] actual, double relativeTolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        double peak = expected.Max(sample => sample.Magnitude);
        double worst = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            worst = Math.Max(worst, (expected[i] - actual[i]).Magnitude);
        }

        Assert.True(
            worst <= peak * relativeTolerance,
            $"records differ by {worst:E2} against a peak of {peak:E2}");
    }

    private static double WrapToPi(double radians)
    {
        double wrapped = Math.IEEERemainder(radians, Math.Tau);
        return wrapped <= -Math.PI ? wrapped + Math.Tau : wrapped;
    }
}
