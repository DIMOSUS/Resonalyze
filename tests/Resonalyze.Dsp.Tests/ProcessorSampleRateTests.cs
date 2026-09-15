using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

/// <summary>Measuring and processing rates are independent: bilinear warping differs by rate (LR4 LP 8 kHz: 1.5 dB at 10 kHz, 10.3 dB at 15 kHz).</summary>
public sealed class ProcessorSampleRateTests
{
    private const int MeasurementRate = 48_000;
    private const int ProcessorRate = 96_000;
    private const int Length = 16_384;
    private const int ArrivalSample = 64;

    [Theory]
    [InlineData(10_000)]
    [InlineData(12_000)]
    [InlineData(15_000)]
    public void AChainRunsAtTheProcessorRate_NotAtTheRecordRate(double probeHz)
    {
        DspChannelChain chain = LowPass(8_000);

        double atProcessorRate = MagnitudeDb(
            VirtualCrossoverAnalysis.ApplyChain(
                Impulse(), chain, MeasurementRate, ProcessorRate),
            probeHz);

        double atRecordRate = MagnitudeDb(
            VirtualCrossoverAnalysis.ApplyChain(
                Impulse(), chain, MeasurementRate, MeasurementRate),
            probeHz);
        double reference = 20.0 * Math.Log10(
            CrossoverFilter
                .Response(LowPassSpec(8_000), probeHz, ProcessorRate)
                .Magnitude);

        Assert.Equal(reference, atProcessorRate, 1);
        Assert.True(
            Math.Abs(atProcessorRate - atRecordRate) > 1.0,
            $"At {probeHz} Hz the two designs differ by only " +
            $"{Math.Abs(atProcessorRate - atRecordRate):0.000} dB — the test probes " +
            "the wrong band if the rates no longer matter here.");
    }

    [Fact]
    public void ALowRateRecordCarriesAHighRateChainExactly()
    {
        // A chain is LTI, so 48 kHz through a 96 kHz chain equals upsample-filter-read; built at 96 kHz to isolate filtering from a resampler.
        DspChannelChain chain = LowPass(3_000);

        Complex[] slow = VirtualCrossoverAnalysis.ApplyChain(
            BandLimitedArrival(MeasurementRate, Length),
            chain,
            MeasurementRate,
            ProcessorRate);
        Complex[] fast = VirtualCrossoverAnalysis.ApplyChain(
            BandLimitedArrival(ProcessorRate, Length * 2),
            chain,
            ProcessorRate,
            ProcessorRate);

        // Compared as shapes: the 96 kHz record's spectrum carries twice the amplitude, a scale only.
        double slowReference = MagnitudeDb(slow, 100.0, MeasurementRate);
        double fastReference = MagnitudeDb(fast, 100.0, ProcessorRate);
        foreach (double probeHz in new[] { 1_000.0, 2_800.0, 3_000.0, 6_000.0, 9_000.0 })
        {
            double slowDb = MagnitudeDb(slow, probeHz, MeasurementRate) - slowReference;
            double fastDb = MagnitudeDb(fast, probeHz, ProcessorRate) - fastReference;
            Assert.Equal(fastDb, slowDb, 1);
        }
    }

    [Fact]
    public void TheDelayIsATime_NotACountOfProcessorSamples()
    {
        Complex[] shifted = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), new DspChannelChain(DelayMs: 2.0), MeasurementRate, ProcessorRate);

        int expected = ArrivalSample + (int)Math.Round(2.0 / 1_000.0 * MeasurementRate);
        Assert.Equal(expected, VirtualCrossoverAnalysis.FindPeakIndex(shifted));
    }

    [Fact]
    public void TheFilterTailIsSizedInTime_NotInProcessorSamples()
    {
        // A biquad decays over the same milliseconds, so padding is half the processor's sample count.
        var chain = new DspChannelChain(
            Peq: new EqualizationCurve([new PeqBand(20, 10, 9)]));
        PreparedDspResponse prepared = PreparedDspResponse.Create(chain, ProcessorRate);

        int atProcessorRate = prepared.RequiredTailSamples(120.0, 1, 4_000_000, ProcessorRate);
        int atRecordRate = prepared.RequiredTailSamples(120.0, 1, 4_000_000, MeasurementRate);

        Assert.Equal(atProcessorRate / 2.0, atRecordRate, 1.0);
    }

    [Fact]
    public void ARecordAboveTheProcessorNyquistKeepsNothingTheDeviceCannotEmit()
    {
        // Above the processor's Nyquist the device reconstructs nothing; without the gate 96 and 192 kHz captures simulate differently.
        const int fastRecord = 192_000;
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), LowPass(3_000), fastRecord, ProcessorRate);

        Assert.True(MagnitudeDb(processed, 50_000, fastRecord) < -120.0);
        Assert.True(MagnitudeDb(processed, 90_000, fastRecord) < -120.0);
        Assert.True(MagnitudeDb(processed, 1_000, fastRecord) > -1.0);
    }

    [Fact]
    public void AScaleOnlyChainIsBandLimitedToo_WhenTheRecordOutrunsTheProcessor()
    {
        // A gain-only channel must lose the same ultrasonic band, or a sum mixes two bandwidths.
        const int fastRecord = 192_000;
        Complex[] bypassed = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), DspChannelChain.Identity, fastRecord, ProcessorRate);
        Complex[] gainOnly = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), new DspChannelChain(GainDb: 6), fastRecord, ProcessorRate);

        foreach (Complex[] response in new[] { bypassed, gainOnly })
        {
            Assert.True(MagnitudeDb(response, 60_000, fastRecord) < -120.0);
            Assert.True(MagnitudeDb(response, 1_000, fastRecord) > -1.0);
        }

        Complex[] cheap = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), new DspChannelChain(GainDb: 6), MeasurementRate, ProcessorRate);
        Assert.Equal(
            Math.Pow(10.0, 6.0 / 20.0), cheap[ArrivalSample].Real, 12);
    }

    private static DspChannelChain LowPass(double frequencyHz) =>
        new(Crossover: LowPassSpec(frequencyHz));

    private static CrossoverSpec LowPassSpec(double frequencyHz) =>
        new(
            CrossoverKind.LowPass,
            LowPassEdge: new CrossoverEdge(
                CrossoverFilterFamily.LinkwitzRiley, frequencyHz, 24));

    private static Complex[] Impulse()
    {
        var impulse = new Complex[Length];
        impulse[ArrivalSample] = Complex.One;
        return impulse;
    }

    private static Complex[] BandLimitedArrival(int sampleRate, int length)
    {
        var record = new Complex[length];
        double widthSeconds = 1.0 / 12_000.0;
        int half = (int)Math.Round(widthSeconds * sampleRate);
        int center = (int)Math.Round(ArrivalSample / (double)MeasurementRate * sampleRate);
        for (int i = -half; i <= half; i++)
        {
            double phase = Math.PI * i / half;
            record[center + i] = 0.5 * (1.0 + Math.Cos(phase));
        }

        return record;
    }

    private static double MagnitudeDb(
        Complex[] response,
        double frequencyHz,
        int sampleRate = MeasurementRate)
    {
        var spectrum = (Complex[])response.Clone();
        Fourier.Forward(spectrum, FourierOptions.Matlab);
        double bin = frequencyHz * spectrum.Length / sampleRate;
        int index = (int)Math.Round(bin);
        return 20.0 * Math.Log10(Math.Max(spectrum[index].Magnitude, double.Epsilon));
    }
}
