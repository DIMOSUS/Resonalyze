using System.Numerics;

namespace Resonalyze.Dsp.Tests;

public sealed class EnergyOnsetTests
{
    private const int SampleRate = 48_000;

    // The field midbass pair's band, analyzed exactly as the engine analyzes a
    // 65-200 Hz junction or link band.
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

    // Deterministic Gaussian noise added to a copy of the signal.
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

    // A midbass front as the field measures it: an impulse through the pair's
    // own crossover (BW36 high-pass 65 Hz, BW48 low-pass 200 Hz), whose band
    // envelope climbs for milliseconds rather than peaking at once.
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

    // The front plus a stronger arrival behind it (a reflection, a mode).
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

        // A zero-phase band-limited pulse is symmetric around its centre, so
        // the first-arrival peak sits ON the pulse and a tenth of its energy
        // has arrived before the centre — inside the kernel's own rise, which
        // is ~1/bandwidth (7 ms for 65-200 Hz).
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

    // The field coin, in the small: the same midbass front in both channels,
    // each followed by the same stronger arrival — one 8 ms behind the front,
    // the other 7 ms. At 8 ms the front's hump stays a local maximum of the
    // band envelope and the first-peak read sits on the front; at 7 ms the
    // hump melts into the climb toward the later arrival, and the first peak
    // jumps 5 ms to that arrival. The fronts are identical, so the true split
    // is zero: the running energy reads it within a fraction of the band's
    // rise time, the first peak reads a split that no geometry produced.
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
        // ... and the onset is a front, not the later arrival: before the
        // front's own peak.
        Assert.True(humped.EnergyOnsetDelayMilliseconds < humped.FirstArrivalDelayMilliseconds);
    }

    // The onset is a property of the signal, not of the record's noise: two
    // identical drivers measured with different noise floors must read the
    // same front. One channel is clean, the other carries a floor ~45 dB
    // down — above the engine's admission for the onset — and 60 ms of noisy
    // pre-roll ahead of the front, where an ungated integral would already
    // have collected a share of the total.
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

    // ... and below the admission the onset is NOT trusted, because at that
    // SNR the noise ahead of the front does reach the gate and the read drifts
    // — this is what EnergyOnsetMinimumSnrDb exists for.
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
        // The field link and junction bands of the low end...
        Assert.True(AutoAlignmentEngine.LinkBandReadsEnergyOnset(65, 200));
        Assert.True(AutoAlignmentEngine.LinkBandReadsEnergyOnset(33, 130));
        Assert.True(AutoAlignmentEngine.LinkBandReadsEnergyOnset(100, 400));
        Assert.True(AutoAlignmentEngine.LinkBandReadsEnergyOnset(70, 180));
        // ... against a mid pair's link band, whose low edge sits under the
        // centre rule's figure but whose 0.7 ms rise makes the peak the better
        // instrument, and the bands above the localization edge.
        Assert.False(AutoAlignmentEngine.LinkBandReadsEnergyOnset(200, 1610));
        Assert.False(AutoAlignmentEngine.LinkBandReadsEnergyOnset(300, 1610));
        Assert.False(AutoAlignmentEngine.LinkBandReadsEnergyOnset(1800, 20_000));

        // The SNR guard is for BOTH sides: one noisy side sends the whole link
        // back to first peaks, never one side each.
        Assert.True(AutoAlignmentEngine.LinkReadsEnergyOnset(65, 200, 60, 45));
        Assert.False(AutoAlignmentEngine.LinkReadsEnergyOnset(65, 200, 60, 30));
        Assert.False(AutoAlignmentEngine.LinkReadsEnergyOnset(65, 200, 30, 60));
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

    // The onset decision is taken from the full-band reads; the upper-half
    // probe that certifies them can be far noisier (a steep low-pass leaves
    // little above the corner). Under the onset admission such a probe is no
    // witness either way: it must not convict a clean onset as a latch, nor
    // certify it — the read stays uncertified, as with an unmeasurable half.
    [Fact]
    public void ClassifyLinkArrival_LeavesAnOnsetReadUncertifiedWhenItsProbeIsTooNoisy()
    {
        TimeAlignmentAnalysisResult full = TimeAlignmentAnalysis.Analyze(
            Pulses((60.0, 1.0)), SampleRate, MidbassBand);
        // A probe read that would CONVICT (it sits 10 ms ahead of the full
        // read) but carries only 25 dB of SNR.
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
        // A peak read keeps the ordinary rule: 25 dB is a measurable half.
        Assert.Equal(
            AutoAlignmentEngine.ArrivalCertificate.Latched,
            AutoAlignmentEngine.ClassifyLinkArrival(full, noisyProbe, 1.0, energyOnset: false));
    }
}
