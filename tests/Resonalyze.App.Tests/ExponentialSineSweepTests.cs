using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class ExponentialSineSweepTests
{
    [Fact]
    public void ComputeSpec_RoundsTheBandOutwardToEncloseTheRequest()
    {
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(30, 18_000, 1.0, 48_000);

        Assert.True(spec.IsValid);
        Assert.True(spec.LowFrequencyHz <= 30.0);
        Assert.True(spec.HighFrequencyHz >= 18_000.0);
        Assert.True(spec.HighFrequencyHz < 24_000.0);
    }

    [Fact]
    public void ComputeSpec_EndpointsLandOnWholeCycles()
    {
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(20, 20_000, 1.0, 48_000);

        // phi(0) = 2πp and phi(N) = 2πq with integer p, q: both endpoints are zero crossings.
        Assert.True(spec.StartCycles >= 1);
        Assert.True(spec.EndCycles > spec.StartCycles);
        double startPhase = 2.0 * Math.PI * spec.StartCycles;
        double endPhase = startPhase * ((double)spec.EndCycles / spec.StartCycles);
        Assert.Equal(0.0, Math.Sin(startPhase), 6);
        Assert.Equal(0.0, Math.Sin(endPhase), 6);
    }

    [Fact]
    public void ComputeSpec_FadesSitInGuardBandsOutsideTheRequestedRange()
    {
        double low = 30;
        double high = 18_000;
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(low, high, 1.0, 48_000);

        Assert.True(spec.FadeInSamples > 0);
        Assert.True(spec.FadeOutSamples > 0);
        Assert.True(spec.FadeInSamples + spec.FadeOutSamples < spec.SampleCount);

        double beta = Math.Log((double)spec.EndCycles / spec.StartCycles);
        double freqAtFadeInEnd =
            spec.LowFrequencyHz * Math.Exp(spec.FadeInSamples / (double)spec.SampleCount * beta);
        double freqAtFadeOutStart =
            spec.LowFrequencyHz * Math.Exp(
                (spec.SampleCount - spec.FadeOutSamples) / (double)spec.SampleCount * beta);
        Assert.True(Math.Abs(freqAtFadeInEnd - low) < 2.0, $"fade-in ends at {freqAtFadeInEnd:0.0} Hz");
        Assert.True(Math.Abs(freqAtFadeOutStart - high) < 25.0, $"fade-out starts at {freqAtFadeOutStart:0} Hz");
    }

    [Fact]
    public void ComputeSpec_AchievedSpanCoversTheRequestedOctaves()
    {
        double low = 20;
        double high = 20_000;
        double requestedOctaves = Math.Log2(high / low);

        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(low, high, 1.0, 48_000);

        Assert.True(spec.IsValid);
        Assert.True(spec.OctaveSpan >= requestedOctaves);
        Assert.True(spec.OctaveSpan < requestedOctaves + 2.0);
    }

    [Fact]
    public void ComputeSpec_NarrowBandDoesNotBlowUp()
    {
        // The endpoint search is direct, so a sub-octave request must not widen to multiple octaves.
        double low = 1000;
        double high = 1200;
        double requestedOctaves = Math.Log2(high / low);
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(low, high, 1.0, 48_000);

        Assert.True(spec.IsValid);
        Assert.True(spec.LowFrequencyHz <= low);
        Assert.True(spec.HighFrequencyHz >= high);
        Assert.True(
            spec.OctaveSpan < requestedOctaves + 2.0,
            $"achieved span {spec.OctaveSpan:0.00} oct is far wider than the {requestedOctaves:0.00} oct request");
    }

    [Fact]
    public void ComputeSpec_LongHighRateSweepCoversTheTopEdge()
    {
        // sampleRate*q in double: at 192 kHz over 20 s an int product overflows.
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(20, 20_000, 20.0, 192_000);

        Assert.True(spec.IsValid);
        Assert.True(
            spec.HighFrequencyHz >= 20_000.0,
            $"high edge {spec.HighFrequencyHz:0} Hz does not cover the requested 20 kHz");
        Assert.True(spec.HighFrequencyHz < 96_000.0);
        Assert.True(spec.LowFrequencyHz <= 20.0);
    }

    [Fact]
    public void OctavePace_TotalDurationPacesEachAchievedOctave()
    {
        double low = 20;
        double high = 20_000;
        int sampleRate = 48_000;
        double perOctaveSeconds = 0.2;

        double total = ExponentialSineSweep.TotalDurationForOctavePace(
            low, high, perOctaveSeconds, sampleRate);
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(low, high, total, sampleRate);

        Assert.True(spec.IsValid);
        Assert.Equal(perOctaveSeconds, total / spec.OctaveSpan, 2);
        Assert.Equal(
            perOctaveSeconds,
            ExponentialSineSweep.OctavePaceForTotalDuration(low, high, total, sampleRate),
            2);
    }

    [Fact]
    public void OctavePace_NarrowerBandGivesProportionallyShorterTotal()
    {
        int sampleRate = 48_000;
        double perOctaveSeconds = 0.2;

        double wide = ExponentialSineSweep.TotalDurationForOctavePace(
            20, 20_000, perOctaveSeconds, sampleRate);
        double narrow = ExponentialSineSweep.TotalDurationForOctavePace(
            1000, 1200, perOctaveSeconds, sampleRate);

        Assert.True(narrow > 0);
        Assert.True(narrow < wide);
    }

    [Fact]
    public void FillData_HonoursDurationToSampleResolution()
    {
        using var sweep = new ExponentialSineSweep();
        sweep.FillData(20, 20_000, 1.0, 24, 48_000);

        Assert.Equal(48_000, sweep.SweepSamples);
        Assert.Equal(1.0, sweep.ComputedDuration, 6);
    }

    [Fact]
    public void Deconvolution_OfGeneratedSweep_YieldsASharpImpulse()
    {
        using var sweep = new ExponentialSineSweep();
        sweep.FillData(30, 18_000, 0.5, 24, 48_000);
        float[] samples = sweep.SweepData;
        float[] inverse = sweep.InverseFilter;

        SweepDeconvolutionResult result = SweepAnalysis.DeconvolveWithInverseFilter(
            samples, inverse, 2.0 / inverse.Length);

        double[] ir = result.ImpulseResponse;
        double peak = Math.Abs(ir[result.PeakIndex]);
        double sumSquares = 0.0;
        for (int i = 0; i < ir.Length; i++)
        {
            sumSquares += ir[i] * ir[i];
        }
        double rms = Math.Sqrt(sumSquares / ir.Length);

        Assert.True(peak > 0);
        Assert.True(peak / rms > 5.0, $"peak/rms = {peak / rms:0.0}");
    }

    [Fact]
    public void SweepData_KeepsSixDecibelsOfExcitationHeadroom()
    {
        using var sweep = new ExponentialSineSweep();
        sweep.FillData(30, 18_000, 1.0, 24, 48_000);

        double peak = 0.0;
        foreach (float sample in sweep.SweepData)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        // A full-scale excitation clipped the field rig's output stage where the -6 dBFS tone played cleanly.
        Assert.True(
            peak <= ExponentialSineSweep.PlaybackAmplitude,
            $"peak {peak:0.####} exceeded {ExponentialSineSweep.PlaybackAmplitude:0.####}");
        Assert.True(
            peak > ExponentialSineSweep.PlaybackAmplitude * 0.999,
            $"peak {peak:0.####} fell short of {ExponentialSineSweep.PlaybackAmplitude:0.####}");
    }

    // Narrow bands too: a scale that is unity only for a sweep reaching Nyquist reads 20 Hz-2 kHz 18.7 dB hot.
    [Theory]
    [InlineData(30, 18_000)]
    [InlineData(20, 10_000)]
    [InlineData(100, 5_000)]
    [InlineData(20, 2_000)]
    public void Deconvolution_RecoversUnityGain_DespiteTheExcitationHeadroom(double lowHz, double highHz)
    {
        using var sweep = new ExponentialSineSweep();
        sweep.FillData(lowHz, highHz, 1.0, 24, 48_000);
        float[] samples = sweep.SweepData;
        float[] inverse = sweep.InverseFilter;

        // The inverse filter carries the reciprocal scale, or every result would sit 6 dB low.
        SweepDeconvolutionResult result = SweepAnalysis.DeconvolveWithInverseFilter(
            samples, inverse, 2.0 / inverse.Length);

        double meanInBandDb = MeanInBandMagnitudeDb(
            result.ImpulseResponse,
            48_000,
            lowHz * 2.0,
            highHz * 0.5);

        Assert.InRange(meanInBandDb, -0.2, 0.2);
    }

    private static double MeanInBandMagnitudeDb(
        double[] impulseResponse,
        int sampleRate,
        double lowHz,
        double highHz)
    {
        int fftLength = DspMath.NextPowerOfTwo(impulseResponse.Length);
        var spectrum = new System.Numerics.Complex[fftLength];
        for (int i = 0; i < impulseResponse.Length; i++)
        {
            spectrum[i] = new System.Numerics.Complex(impulseResponse[i], 0.0);
        }
        MathNet.Numerics.IntegralTransforms.Fourier.Forward(
            spectrum, MathNet.Numerics.IntegralTransforms.FourierOptions.Matlab);

        double binToHz = sampleRate / (double)fftLength;
        var inBand = new List<double>();
        for (int bin = 1; bin <= fftLength / 2; bin++)
        {
            double frequency = bin * binToHz;
            if (frequency >= lowHz && frequency <= highHz)
            {
                inBand.Add(20.0 * Math.Log10(Math.Max(spectrum[bin].Magnitude, 1e-12)));
            }
        }

        Assert.NotEmpty(inBand);
        return inBand.Average();
    }
}
