using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class EnergyOnsetTests
{
    private const int SampleRate = 48_000;

    private static TimeAlignmentAnalysisOptions MidbassBand => new()
    {
        UseBandpassWindow = true,
        BandpassCenterHz = Math.Sqrt(65.0 * 200.0),
        BandpassPassOctaves = Math.Log2(200.0 / 65.0),
        BandpassFadeOctaves = 1.0
    };

    private static double[] Pulses(params (double Ms, double Amplitude)[] pulses)
    {
        var signal = new double[16_384];
        foreach ((double ms, double amplitude) in pulses)
        {
            signal[(int)Math.Round(ms * SampleRate / 1000.0)] += amplitude;
        }

        return signal;
    }

    private static double[] WithNoise(double[] signal, double sigma, int seed)
    {
        var noisy = (double[])signal.Clone();
        var random = new Random(seed);
        for (int i = 0; i < noisy.Length; i++)
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            noisy[i] += sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        return noisy;
    }

    // Field midbass front: an impulse through BW36 HP 65 Hz / BW48 LP 200 Hz, climbing for milliseconds.
    private static Complex[] MidbassFront(double atMs)
    {
        var impulse = new Complex[16_384];
        impulse[(int)Math.Round(atMs * SampleRate / 1000.0)] = Complex.One;
        return VirtualCrossoverAnalysis.ApplyChain(
            impulse,
            new DspChannelChain(Crossover: new CrossoverSpec(
                CrossoverKind.BandPass,
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 200, 48),
                new CrossoverEdge(CrossoverFilterFamily.Butterworth, 65, 36))),
            SampleRate,
            SampleRate);
    }

    private static double[] FrontWithLaterArrival(double gapMs, double laterAmplitude)
    {
        Complex[] front = MidbassFront(30.0);
        int shift = (int)Math.Round(gapMs * SampleRate / 1000.0);
        var signal = new double[front.Length];
        for (int i = 0; i < signal.Length; i++)
        {
            signal[i] = front[i].Real +
                (i >= shift ? laterAmplitude * front[i - shift].Real : 0.0);
        }

        return signal;
    }

    [Fact]
    public void Analyze_EnergyOnsetSitsOnTheRisingFrontOfABandLimitedPulse()
    {
        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            Pulses((30.0, 1.0)), SampleRate, MidbassBand);

        // A zero-phase pulse is symmetric: a tenth of its energy arrives within the kernel's ~7 ms rise before the centre.
        Assert.InRange(result.FirstArrivalDelayMilliseconds, 29.5, 30.5);
        Assert.True(result.EnergyOnsetDelayMilliseconds < result.FirstArrivalDelayMilliseconds);
        Assert.InRange(result.EnergyOnsetDelayMilliseconds, 22.0, 30.0);
        Assert.Equal(
            result.EnergyOnsetDelayMilliseconds,
            result.EnergyOnsetSample * 1000.0 / SampleRate,
            6);
    }

    [Fact]
    public void Analyze_EnergyOnsetFollowsThePulseWhenItMoves()
    {
        TimeAlignmentAnalysisResult early = TimeAlignmentAnalysis.Analyze(
            Pulses((30.0, 1.0)), SampleRate, MidbassBand);
        TimeAlignmentAnalysisResult late = TimeAlignmentAnalysis.Analyze(
            Pulses((33.0, 1.0)), SampleRate, MidbassBand);

        Assert.InRange(
            late.EnergyOnsetDelayMilliseconds - early.EnergyOnsetDelayMilliseconds,
            2.9, 3.1);
    }

    // Identical fronts, stronger arrival 8 vs 7 ms behind: at 7 ms the first peak jumps 5 ms; the energy onset reads zero split.
    [Fact]
    public void Analyze_EnergyOnsetReadsTheFrontWhereTheFirstPeakMeltsIntoTheLaterArrival()
    {
        TimeAlignmentAnalysisResult humped = TimeAlignmentAnalysis.Analyze(
            FrontWithLaterArrival(8.0, 1.4), SampleRate, MidbassBand);
        TimeAlignmentAnalysisResult melted = TimeAlignmentAnalysis.Analyze(
            FrontWithLaterArrival(7.0, 1.4), SampleRate, MidbassBand);

        Assert.True(
            melted.FirstArrivalDelayMilliseconds - humped.FirstArrivalDelayMilliseconds > 3.0,
            $"the synthetic no longer reproduces the coin: first peaks {humped.FirstArrivalDelayMilliseconds:0.00} / {melted.FirstArrivalDelayMilliseconds:0.00} ms");
        Assert.InRange(
            Math.Abs(melted.EnergyOnsetDelayMilliseconds - humped.EnergyOnsetDelayMilliseconds),
            0.0, 0.5);
        Assert.True(humped.EnergyOnsetDelayMilliseconds < humped.FirstArrivalDelayMilliseconds);
    }

    // A floor ~45 dB down with 60 ms noisy pre-roll must not move the onset.
    [Fact]
    public void Analyze_EnergyOnsetDoesNotMoveWithTheRecordsNoiseFloor()
    {
        double[] clean = Pulses((60.0, 1.0));
        double[] noisy = WithNoise(clean, 0.0002, seed: 42);

        TimeAlignmentAnalysisResult cleanRead = TimeAlignmentAnalysis.Analyze(
            clean, SampleRate, MidbassBand);
        TimeAlignmentAnalysisResult noisyRead = TimeAlignmentAnalysis.Analyze(
            noisy, SampleRate, MidbassBand);

        Assert.InRange(noisyRead.SignalToNoiseDecibels, 40.0, 60.0);
        Assert.InRange(cleanRead.EnergyOnsetDelayMilliseconds, 52.0, 60.0);
        Assert.InRange(
            noisyRead.EnergyOnsetDelayMilliseconds - cleanRead.EnergyOnsetDelayMilliseconds,
            -0.1, 0.1);
    }

    // At ~30 dB SNR the onset holds while the first peak breaks; every dB of admission above that returns the pair to the peaks.
    [Fact]
    public void Analyze_EnergyOnsetHoldsAtTheAdmissionSnrOnAShapedFront()
    {
        double[] clean = FrontWithLaterArrival(7.0, 1.4);
        double peak = clean.Max(Math.Abs);
        double[] noisy = WithNoise(clean, 0.2 * peak, seed: 42);

        TimeAlignmentAnalysisResult cleanRead = TimeAlignmentAnalysis.Analyze(
            clean, SampleRate, MidbassBand);
        TimeAlignmentAnalysisResult noisyRead = TimeAlignmentAnalysis.Analyze(
            noisy, SampleRate, MidbassBand);

        Assert.InRange(noisyRead.SignalToNoiseDecibels, 28.0, 34.0);
        Assert.InRange(
            noisyRead.EnergyOnsetDelayMilliseconds - cleanRead.EnergyOnsetDelayMilliseconds,
            -0.2, 0.2);
        Assert.True(AutoAlignmentEngine.LinkReadsEnergyOnset(
            65, 200, cleanRead.SignalToNoiseDecibels, noisyRead.SignalToNoiseDecibels));
    }

    // Below the admission the pre-front noise reaches the gate: why EnergyOnsetMinimumSnrDb exists.
    [Fact]
    public void Analyze_EnergyOnsetDriftsUnderTheAdmissionSnr_WhichTheLinkGuards()
    {
        double[] clean = Pulses((60.0, 1.0));
        double[] noisy = WithNoise(clean, 0.006, seed: 42);

        TimeAlignmentAnalysisResult cleanRead = TimeAlignmentAnalysis.Analyze(
            clean, SampleRate, MidbassBand);
        TimeAlignmentAnalysisResult noisyRead = TimeAlignmentAnalysis.Analyze(
            noisy, SampleRate, MidbassBand);

        Assert.InRange(noisyRead.SignalToNoiseDecibels, 14.0, 30.0);
        Assert.True(
            Math.Abs(noisyRead.EnergyOnsetDelayMilliseconds - cleanRead.EnergyOnsetDelayMilliseconds) > 0.5,
            "the low-SNR read no longer drifts; the admission floor may be revisited");
        Assert.False(AutoAlignmentEngine.LinkReadsEnergyOnset(
            65, 200, cleanRead.SignalToNoiseDecibels, noisyRead.SignalToNoiseDecibels));
    }

    [Fact]
    public void LinkReadsEnergyOnset_IsDecidedByTheBandCentreAndBothSidesSnr()
    {
        Assert.True(AutoAlignmentEngine.LinkBandReadsEnergyOnset(65, 200));
        Assert.True(AutoAlignmentEngine.LinkBandReadsEnergyOnset(33, 130));
        Assert.True(AutoAlignmentEngine.LinkBandReadsEnergyOnset(100, 400));
        Assert.True(AutoAlignmentEngine.LinkBandReadsEnergyOnset(70, 180));
        // A mid pair's 0.7 ms rise makes the peak the better instrument there.
        Assert.False(AutoAlignmentEngine.LinkBandReadsEnergyOnset(200, 1610));
        Assert.False(AutoAlignmentEngine.LinkBandReadsEnergyOnset(300, 1610));
        Assert.False(AutoAlignmentEngine.LinkBandReadsEnergyOnset(1800, 20_000));

        // One noisy side sends the whole link back to first peaks.
        Assert.True(AutoAlignmentEngine.LinkReadsEnergyOnset(65, 200, 60, 45));
        Assert.True(AutoAlignmentEngine.LinkReadsEnergyOnset(65, 200, 60, 32));
        Assert.False(AutoAlignmentEngine.LinkReadsEnergyOnset(65, 200, 60, 25));
        Assert.False(AutoAlignmentEngine.LinkReadsEnergyOnset(65, 200, 25, 60));
        Assert.False(AutoAlignmentEngine.LinkReadsEnergyOnset(200, 1610, 60, 60));
    }

    [Fact]
    public void AsEnergyOnset_SwapsTheArrivalFieldsAndKeepsThePeakFigures()
    {
        var response = new Complex[16_384];
        response[(int)Math.Round(30.0 * SampleRate / 1000.0)] = Complex.One;
        response[(int)Math.Round(37.0 * SampleRate / 1000.0)] = new Complex(1.4, 0.0);

        TimeAlignmentAnalysisResult plain = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            response, SampleRate, 65, 200);
        TimeAlignmentAnalysisResult onset = AutoAlignmentEngine.AsEnergyOnset(plain);
        Assert.Equal(plain.EnergyOnsetDelayMilliseconds, onset.FirstArrivalDelayMilliseconds, 9);
        Assert.Equal(plain.EnergyOnsetSample, onset.FirstArrivalPeakSample, 9);
        Assert.Equal(plain.StrongestDelayMilliseconds, onset.StrongestDelayMilliseconds, 9);
        Assert.Equal(plain.SignalToNoiseDecibels, onset.SignalToNoiseDecibels, 9);
        Assert.Equal(plain.FirstArrivalProminenceDecibels, onset.FirstArrivalProminenceDecibels, 9);
    }

    [Fact]
    public void AnalyzeBandLimitedArrival_ReportsTheEnergyOnsetInFullRecordCoordinates()
    {
        var response = new Complex[16_384];
        response[(int)Math.Round(30.0 * SampleRate / 1000.0)] = Complex.One;
        var validRange = new ValidSampleRange(
            (int)Math.Round(10.0 * SampleRate / 1000.0), response.Length);

        TimeAlignmentAnalysisResult cropped = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            response, SampleRate, 65, 200, validRange);
        TimeAlignmentAnalysisResult whole = VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            response, SampleRate, 65, 200);

        Assert.InRange(
            cropped.EnergyOnsetDelayMilliseconds - whole.EnergyOnsetDelayMilliseconds,
            -0.2, 0.2);
    }

    // A probe under the onset admission neither convicts nor certifies an onset read.
    [Fact]
    public void ClassifyLinkArrival_LeavesAnOnsetReadUncertifiedWhenItsProbeIsTooNoisy()
    {
        TimeAlignmentAnalysisResult full = TimeAlignmentAnalysis.Analyze(
            Pulses((60.0, 1.0)), SampleRate, MidbassBand);
        TimeAlignmentAnalysisResult noisyProbe = full with
        {
            FirstArrivalDelayMilliseconds = full.FirstArrivalDelayMilliseconds - 10.0,
            SignalToNoiseDecibels = 25.0
        };
        TimeAlignmentAnalysisResult cleanProbe = noisyProbe with { SignalToNoiseDecibels = 60.0 };

        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Latched,
            AutoAlignmentEngine.ClassifyLinkArrival(full, cleanProbe, 1.0, energyOnset: true));
        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Unverified,
            AutoAlignmentEngine.ClassifyLinkArrival(full, noisyProbe, 1.0, energyOnset: true));
        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Latched,
            AutoAlignmentEngine.ClassifyLinkArrival(full, noisyProbe, 1.0, energyOnset: false));
    }
}
