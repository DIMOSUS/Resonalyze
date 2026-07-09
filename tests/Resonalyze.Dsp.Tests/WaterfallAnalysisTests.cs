using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class WaterfallAnalysisTests
{
    private const int SampleRate = 48_000;
    private const int Window = 4096;

    // A decaying sinusoid at a known frequency, laid straight into the impulse
    // response so ExtractWindow (offset 0) reads exactly it.
    private static SyntheticMeasurement DecayingTone(double frequencyHz, double tauSamples)
    {
        var ir = new Complex[Window];
        for (int n = 0; n < Window; n++)
        {
            double envelope = Math.Exp(-n / tauSamples);
            ir[n] = new Complex(envelope * Math.Sin(2.0 * Math.PI * frequencyHz * n / SampleRate), 0.0);
        }

        return new SyntheticMeasurement(ir, SampleRate, maxMagnitudeIndex: 0);
    }

    private static double[] Hann(int length)
    {
        double[] w = new double[length];
        for (int n = 0; n < length; n++)
        {
            w[n] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * n / (length - 1));
        }

        return w;
    }

    private static BurstDecaySlice ClosestSlice(IReadOnlyList<BurstDecaySlice> slices, double frequencyHz)
    {
        return slices.OrderBy(s => Math.Abs(Math.Log(s.Frequency / frequencyHz))).First();
    }

    private static double PeakMagnitude(BurstDecaySlice slice) => slice.Data.Max(p => p.Y);

    [Fact]
    public void BuildBurstDecayRawSlices_LocalizesEnergyToTheToneFrequency()
    {
        const double toneHz = 1_000.0;
        IReadOnlyList<BurstDecaySlice> slices = WaterfallAnalysis.BuildBurstDecayRawSlices(
            DecayingTone(toneHz, tauSamples: 1_500.0),
            offset: 0,
            window: Window,
            windowFunction: Hann(Window),
            smoothingOctaves: 1.0);

        // The slice at the tone must be the global energy maximum and dominate slices
        // two octaves away: a wrong Morlet centre (w0), a dropped negative-frequency
        // wrap, or a broken normalization would smear or misplace this and fail.
        double atTone = PeakMagnitude(ClosestSlice(slices, toneHz));
        double twoOctavesUp = PeakMagnitude(ClosestSlice(slices, toneHz * 4.0));
        double twoOctavesDown = PeakMagnitude(ClosestSlice(slices, toneHz / 4.0));

        Assert.Equal(atTone, slices.Max(PeakMagnitude), precision: 12);
        Assert.True(
            atTone > 4.0 * twoOctavesUp,
            $"Tone slice {atTone:e} not dominant over +2oct {twoOctavesUp:e}.");
        Assert.True(
            atTone > 4.0 * twoOctavesDown,
            $"Tone slice {atTone:e} not dominant over -2oct {twoOctavesDown:e}.");
    }

    [Fact]
    public void BuildBurstDecayRawSlices_EnvelopeDecaysForADecayingTone()
    {
        const double toneHz = 1_000.0;
        IReadOnlyList<BurstDecaySlice> slices = WaterfallAnalysis.BuildBurstDecayRawSlices(
            DecayingTone(toneHz, tauSamples: 800.0),
            offset: 0,
            window: Window,
            windowFunction: Hann(Window),
            smoothingOctaves: 1.0);

        IReadOnlyList<SignalPoint> envelope = ClosestSlice(slices, toneHz).Data;
        int quarter = envelope.Count / 4;
        double firstQuarter = envelope.Take(quarter).Average(p => p.Y);
        double lastQuarter = envelope.Skip(envelope.Count - quarter).Average(p => p.Y);

        Assert.True(
            firstQuarter > lastQuarter,
            $"Envelope did not decay: first quarter {firstQuarter:e}, last {lastQuarter:e}.");
    }

    [Fact]
    public void BuildBurstDecayRawSlices_FrequencyGridIsAscendingBoundedAndOctaveSpaced()
    {
        const double smoothingOctaves = 1.0;
        IReadOnlyList<BurstDecaySlice> slices = WaterfallAnalysis.BuildBurstDecayRawSlices(
            DecayingTone(1_000.0, tauSamples: 1_000.0),
            offset: 0,
            window: Window,
            windowFunction: Hann(Window),
            smoothingOctaves: smoothingOctaves);

        double frequencyStep = (double)SampleRate / (Window * 4);
        double expectedRatio = Math.Pow(2.0, 0.5 * smoothingOctaves);

        double[] freqs = slices.Select(s => s.Frequency).ToArray();
        Assert.NotEmpty(freqs);
        for (int i = 0; i < freqs.Length; i++)
        {
            Assert.InRange(freqs[i], Math.Max(20.0, frequencyStep * 4), 20_000.0);
            if (i > 0)
            {
                Assert.True(freqs[i] > freqs[i - 1], "Frequencies must be strictly ascending.");
                Assert.Equal(expectedRatio, freqs[i] / freqs[i - 1], precision: 9);
            }
        }
    }

    [Fact]
    public void BuildBurstDecayRawSlices_MoreSmoothingYieldsFewerSlices()
    {
        SyntheticMeasurement tone = DecayingTone(1_000.0, tauSamples: 1_000.0);

        int narrow = WaterfallAnalysis.BuildBurstDecayRawSlices(
            tone, 0, Window, Hann(Window), smoothingOctaves: 1.0).Count;
        int wide = WaterfallAnalysis.BuildBurstDecayRawSlices(
            tone, 0, Window, Hann(Window), smoothingOctaves: 2.0).Count;

        Assert.True(wide < narrow, $"Expected fewer slices at 2 oct ({wide}) than 1 oct ({narrow}).");
    }

    [Fact]
    public void ResampleBurstDecaySlice_MapsConstantAmplitudeToItsDecibelLevelOnAPeriodsAxis()
    {
        // A constant-amplitude raw envelope: every in-range sample smooths to the same
        // amplitude, so the dB value is analytic and the X axis is the periods ramp.
        const double amplitude = 0.5;
        var rawData = Enumerable.Range(0, 200)
            .Select(i => new SignalPoint(i, amplitude))
            .ToList();
        const int width = 50;
        const double periods = 5.0;

        IReadOnlyList<SignalPoint> resampled = WaterfallAnalysis.ResampleBurstDecaySlice(
            rawData, frequency: 1_000.0, sampleRate: SampleRate, width: width, periods: periods);

        Assert.Equal(width, resampled.Count);
        Assert.Equal(0.0, resampled[0].X, precision: 12);
        Assert.Equal((width - 1) / (double)width * periods, resampled[^1].X, precision: 12);
        // periodsSamples = 48000 * 5 / 1000 = 240; index 5 -> samplePosition 24, well in range.
        Assert.Equal(DataHelper.AmplitudeToDecibels(amplitude), resampled[5].Y, precision: 6);
    }

    [Fact]
    public void ResampleBurstDecaySlice_ReturnsTheFloorBeyondTheRawDataRange()
    {
        // periodsSamples = 48000 * 10 / 1000 = 480; the raw envelope is only 20 samples
        // long, so late output points fall past it and must read the -160 dB floor
        // rather than NaN/garbage.
        var rawData = Enumerable.Range(0, 20)
            .Select(i => new SignalPoint(i, 0.5))
            .ToList();

        IReadOnlyList<SignalPoint> resampled = WaterfallAnalysis.ResampleBurstDecaySlice(
            rawData, frequency: 1_000.0, sampleRate: SampleRate, width: 50, periods: 10.0);

        Assert.Equal(DataHelper.AmplitudeToDecibels(0.0), resampled[^1].Y, precision: 6);
    }

    [Fact]
    public void ResampleBurstDecaySlice_EmptyRawDataReturnsEmpty()
    {
        IReadOnlyList<SignalPoint> resampled = WaterfallAnalysis.ResampleBurstDecaySlice(
            Array.Empty<SignalPoint>(), frequency: 1_000.0, sampleRate: SampleRate, width: 50, periods: 5.0);

        Assert.Empty(resampled);
    }
}
