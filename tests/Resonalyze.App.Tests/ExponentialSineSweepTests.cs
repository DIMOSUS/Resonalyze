using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class ExponentialSineSweepTests
{
    [Fact]
    public void ComputeSpec_RoundsTheBandOutwardToEncloseTheRequest()
    {
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(30, 18_000, 1.0, 48_000);

        Assert.True(spec.IsValid);
        // Low rounds down, high rounds up: the achieved band contains the request.
        Assert.True(spec.LowFrequencyHz <= 30.0);
        Assert.True(spec.HighFrequencyHz >= 18_000.0);
        // ...and never past Nyquist.
        Assert.True(spec.HighFrequencyHz < 24_000.0);
    }

    [Fact]
    public void ComputeSpec_EndpointsLandOnWholeCycles()
    {
        ExpSweepSpec spec = ExponentialSineSweep.ComputeSpec(20, 20_000, 1.0, 48_000);

        // phi(0) = 2*pi*p and phi(N) = 2*pi*q with integer p, q, so both endpoints
        // are zero crossings whatever the achieved band — phase alignment is kept.
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
        // The fade-in ends exactly where the sweep reaches the requested low edge,
        // and the fade-out starts at the requested high edge, so the whole
        // [low, high] band is excited at full amplitude.
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
        // The guard bands stay modest — a couple of octaves at most.
        Assert.True(spec.OctaveSpan < requestedOctaves + 2.0);
    }

    [Fact]
    public void ComputeSpec_NarrowBandDoesNotBlowUp()
    {
        // Regression: a sub-octave request must not run away to a multi-octave
        // sweep (the endpoint search is direct, not iterative widening).
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
        // Regression: sampleRate*q must be evaluated in double — at 192 kHz over
        // 20 s q reaches tens of thousands and an int product would overflow,
        // corrupting the band so the requested top is not covered.
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
        // The sweep runs ~perOctaveSeconds per achieved octave, and the inverse
        // recovers the pace.
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
        // Fewer octaves to sweep at the same pace → a shorter total.
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

        // A matched sweep/inverse pair deconvolves to a sharp peak that towers over
        // the residual energy floor.
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

        // The sweep must never leave the interface less than the headroom it was
        // given: a full-scale excitation clipped the output stage of the field rig
        // at a level the Signal Generator's own tone (-6 dBFS) played cleanly.
        Assert.True(
            peak <= ExponentialSineSweep.PlaybackAmplitude,
            $"peak {peak:0.####} exceeded {ExponentialSineSweep.PlaybackAmplitude:0.####}");
        // ...and it does reach it — the crest of a sweep this long lands on a
        // sample, so a silently attenuated excitation is caught too.
        Assert.True(
            peak > ExponentialSineSweep.PlaybackAmplitude * 0.999,
            $"peak {peak:0.####} fell short of {ExponentialSineSweep.PlaybackAmplitude:0.####}");
    }

    [Fact]
    public void Deconvolution_RecoversUnityGain_DespiteTheExcitationHeadroom()
    {
        using var sweep = new ExponentialSineSweep();
        sweep.FillData(30, 18_000, 1.0, 24, 48_000);
        float[] samples = sweep.SweepData;
        float[] inverse = sweep.InverseFilter;

        // Playing the attenuated sweep through a unity ("wire") system records the
        // attenuated sweep itself; its own inverse filter must still hand back a
        // 0 dB impulse response, because the filter inverts the excitation that is
        // actually played. Without the reciprocal scale the whole result would sit
        // 6 dB low and every stored measurement would step down with this change.
        SweepDeconvolutionResult result = SweepAnalysis.DeconvolveWithInverseFilter(
            samples, inverse, 2.0 / inverse.Length);

        double meanInBandDb = MeanInBandMagnitudeDb(
            result.ImpulseResponse,
            48_000,
            sweep.LowFrequencyHz * 4.0,
            sweep.HighFrequencyHz * 0.5);

        Assert.InRange(meanInBandDb, -1.0, 1.0);
    }

    // Mean magnitude (dB) over the reliable interior of the swept band. Mirrors
    // SweepDeconvolutionFlatnessTests, which pins the same unity round trip
    // against a locally regenerated sweep; this one runs the real generator.
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
