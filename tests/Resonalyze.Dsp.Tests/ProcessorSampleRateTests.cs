using System.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The measuring rate and the DSP's processing rate are independent. A user with a
/// 48 kHz sound card and a 96 kHz processor must get the filters that processor
/// builds, not the ones the measurement rate would imply — the bilinear transform
/// warps every corner by the rate it was designed at, and the two answers part company
/// well inside the audible band (an LR4 low-pass at 8 kHz: 1.5 dB at 10 kHz, 4.1 dB at
/// 12 kHz, 10.3 dB at 15 kHz).
/// </summary>
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

        // What the same measurement would have produced before the rates were told
        // apart, i.e. filters designed at the sound card's rate.
        double atRecordRate = MagnitudeDb(
            VirtualCrossoverAnalysis.ApplyChain(
                Impulse(), chain, MeasurementRate, MeasurementRate),
            probeHz);
        double reference = 20.0 * Math.Log10(
            CrossoverFilter
                .Response(LowPassSpec(8_000), probeHz, ProcessorRate)
                .Magnitude);

        // The realized response follows the PROCESSOR's analytic filter to a fraction
        // of a dB, and is a whole dB or more away from the record-rate one.
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
        // The equivalence the feature rests on: a 48 kHz record through a 96 kHz chain
        // is the SAME answer as upsampling that record, filtering at 96 kHz, and
        // reading the result back — because a chain is LTI and invents no frequency
        // its input lacks. Here the "upsampled" record is built directly at 96 kHz
        // (a band-limited arrival that both rates represent exactly), so the
        // comparison isolates the filtering rather than a resampler.
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

        // Compared as SHAPES, referenced to a frequency the crossover passes: the two
        // records hold the same pulse over the same time, so the 96 kHz one carries
        // twice the samples and its spectrum twice the amplitude — a scale, not a
        // difference in what the filter did.
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
        // The delay travels as milliseconds through both rates: the phase ramp runs on
        // the RECORD's grid while the filters run on the processor's.
        Complex[] shifted = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), new DspChannelChain(DelayMs: 2.0), MeasurementRate, ProcessorRate);

        int expected = ArrivalSample + (int)Math.Round(2.0 / 1_000.0 * MeasurementRate);
        Assert.Equal(expected, VirtualCrossoverAnalysis.FindPeakIndex(shifted));
    }

    [Fact]
    public void TheFilterTailIsSizedInTime_NotInProcessorSamples()
    {
        // A 96 kHz biquad decays over the same number of MILLISECONDS whichever record
        // holds it, so the padding a 48 kHz record needs is half the processor's count.
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
        // A 192 kHz measurement of a system driven by a 96 kHz processor: above
        // 48 kHz the device reconstructs nothing, and the periodic continuation of H
        // would otherwise filter that band with a mirrored response no device
        // produces — leaving the same setup measured at 96 and at 192 kHz simulating
        // differently.
        const int fastRecord = 192_000;
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            Impulse(), LowPass(3_000), fastRecord, ProcessorRate);

        Assert.True(MagnitudeDb(processed, 50_000, fastRecord) < -120.0);
        Assert.True(MagnitudeDb(processed, 90_000, fastRecord) < -120.0);
        // The band the processor DOES emit is untouched by the gate.
        Assert.True(MagnitudeDb(processed, 1_000, fastRecord) > -1.0);
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

    // A raised-cosine pulse whose content dies well below either Nyquist, so the two
    // rates hold the same signal rather than two different band limits.
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
