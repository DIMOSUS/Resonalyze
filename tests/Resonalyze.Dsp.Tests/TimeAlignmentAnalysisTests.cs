namespace Resonalyze.Dsp.Tests;

public sealed class TimeAlignmentAnalysisTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public void Analyze_FlagsALaterStrongestPeakAsASeparateArrival()
    {
        // A weak direct arrival followed by a much stronger, much later peak — the
        // narrowband-subwoofer trap where the strongest peak is a room mode.
        var impulseResponse = new double[8_192];
        impulseResponse[100] = 0.3;
        impulseResponse[500] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.True(result.StrongestPeakIsSeparateArrival);
        // (500 - 100) samples at 48 kHz ≈ 8.33 ms.
        Assert.InRange(result.StrongestPeakSeparationMilliseconds, 8.0, 8.7);
    }

    [Fact]
    public void Analyze_DoesNotFlagACleanSingleArrival()
    {
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.False(result.StrongestPeakIsSeparateArrival);
        Assert.True(result.StrongestPeakSeparationMilliseconds < 1.0);
    }

    [Fact]
    public void Analyze_ReportsTheStrongestArrivalSampleAndDelayForACleanImpulse()
    {
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        // The strongest peak (the analytic-envelope main lobe) lands on the arrival:
        // 300 samples at 48 kHz = 6.25 ms. The refined sample and its ms twin agree.
        Assert.Equal(300, result.StrongestEnvelopePeakIndex);
        Assert.InRange(result.StrongestPeakSample, 299.5, 300.5);
        Assert.InRange(result.StrongestDelayMilliseconds, 6.24, 6.26);
        Assert.Equal(
            result.StrongestPeakSample * 1000.0 / SampleRate,
            result.StrongestDelayMilliseconds,
            precision: 9);
        // The first arrival is at or before the strongest, never after it.
        Assert.True(result.FirstArrivalPeakSample <= result.StrongestPeakSample + 0.5);
    }

    [Fact]
    public void Analyze_LocatesTheStrongArrivalAndAnEarlierFirstArrivalInTheTwoPeakTrap()
    {
        var impulseResponse = new double[8_192];
        impulseResponse[100] = 0.3; // weak direct arrival
        impulseResponse[500] = 1.0; // strong late arrival (room mode)

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        // The strongest peak main lobe sits on the 1.0 arrival (500 samples, 10.417 ms).
        Assert.Equal(500, result.StrongestEnvelopePeakIndex);
        Assert.InRange(result.StrongestPeakSample, 499.0, 501.0);
        Assert.InRange(result.StrongestDelayMilliseconds, 10.38, 10.44);
        // The first arrival is detected near the weak 100-sample pulse — hundreds of
        // samples earlier than the strongest, i.e. the module did not collapse both
        // onto the dominant peak.
        Assert.InRange(result.FirstArrivalPeakSample, 90.0, 110.0);
        Assert.True(result.StrongestPeakSample - result.FirstArrivalPeakSample > 300.0);
    }

    [Fact]
    public void Analyze_WrapPeakPositionsLeavesASubHalfArrivalUnchanged()
    {
        // The peak search is capped at length/2, so a real arrival always lands in
        // the lower half of the buffer and ToSignedDelaySamples' pivot (length*0.5)
        // must leave it untouched. A flipped comparison would wrap this normal
        // arrival to a large negative delay; running with and without the flag and
        // requiring identical output pins the pivot and its direction.
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult unwrapped = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());
        TimeAlignmentAnalysisResult wrapped = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions { WrapPeakPositions = true });

        Assert.Equal(unwrapped.FirstArrivalPeakSample, wrapped.FirstArrivalPeakSample, precision: 12);
        Assert.Equal(unwrapped.StrongestPeakSample, wrapped.StrongestPeakSample, precision: 12);
        Assert.True(wrapped.FirstArrivalPeakSample > 0);
    }

    [Fact]
    public void Analyze_DoesNotFlagACloseSecondPeakBelowTheThreshold()
    {
        // Two peaks only ~0.4 ms apart: too close to matter for alignment, so no
        // warning even though the strongest is technically a different index.
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 0.6;
        impulseResponse[320] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.False(result.StrongestPeakIsSeparateArrival);
    }

    [Fact]
    public void Analyze_ReportsHighConfidenceAndPhatRefinementForTheStrongestArrival()
    {
        // A flat-spectrum delta whitens to a sharp GCC-PHAT peak at its arrival, which
        // coincides with the strongest envelope peak, so that arrival refines by PHAT
        // with a strong, in-range confidence — the number the UI shows to say "trust
        // this alignment". Sidelobe rejection puts the first arrival on the same
        // sample (the Hilbert skirt's own bumps are mirror-symmetric and read as
        // pre-ringing), so it refines by PHAT just as well.
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.InRange(result.FirstArrivalConfidence, 0.0, 1.0);
        Assert.InRange(result.StrongestConfidence, 0.0, 1.0);
        Assert.True(
            result.StrongestConfidence > 0.2,
            $"Strongest confidence {result.StrongestConfidence:0.000} should clear the trust gate.");
        Assert.True(result.StrongestRefinedByPhat);
    }

    [Fact]
    public void Analyze_CleanImpulseHasZeroProminenceGapAndHighSnr()
    {
        // A clean single arrival: the first arrival IS the strongest peak, so
        // the prominence gap is exactly 0 dB, and the signal grade reflects the
        // recording's SNR (peak vs the Hilbert-skirt-plus-silence remainder) —
        // the two figures a single folded "quality" number used to conflate.
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.Equal(0.0, result.FirstArrivalProminenceDecibels, precision: 12);
        Assert.True(
            result.SignalToNoiseDecibels > 30,
            $"SNR {result.SignalToNoiseDecibels:0.0} dB should be high for a clean impulse.");
    }

    [Fact]
    public void Analyze_FirstArrivalDoesNotSitOnTheHilbertSkirtOfACleanImpulse()
    {
        // The discrete Hilbert envelope of a delta has a 1/t skirt whose odd-offset
        // bumps are local maxima; the first one above the -25 dB threshold sits
        // ~11 samples early and used to be reported as the first arrival on every
        // clean, high-SNR measurement. The skirt is mirror-symmetric, so sidelobe
        // rejection must put the first arrival on the true peak.
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());

        Assert.InRange(result.FirstArrivalPeakSample, 299.5, 300.5);
        Assert.Equal(result.StrongestEnvelopePeakIndex, result.EnvelopePeakIndex);
    }

    [Fact]
    public void Analyze_BandpassPreRingingDoesNotPullTheFirstArrivalEarly()
    {
        // The zero-phase bandpass window rings symmetrically around the arrival;
        // at 1 kHz / 1 octave its -24 dB pre-lobe clears the -25 dB threshold
        // ~2.1 ms before the true peak and used to be reported as the first
        // arrival — a ~72 cm alignment error on a clean measurement. The mirror
        // test must reject the whole pre-ring train.
        var impulseResponse = new double[32_768];
        impulseResponse[300] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = 1000,
                BandpassPassOctaves = 1,
                BandpassFadeOctaves = 0.5
            });

        Assert.InRange(result.FirstArrivalPeakSample, 299.5, 300.5);
        Assert.True(result.FirstArrivalRefinedByPhat);
    }

    [Fact]
    public void Analyze_ReverberantBassKeepsTheGenuineDirectArrival()
    {
        // Field regression from the crossover Auto delay: a midbass direct sound
        // ~9 dB below a reverberant reflection cluster, analyzed in a narrow low
        // band (88-350 Hz, gentle fades, 15 dB threshold). In a room the
        // mirrored position after the cluster is always energized, so a
        // mirror-symmetry test alone read the direct sound as pre-ringing and
        // shifted the first arrival ~8 ms late. The kernel-level ceiling must
        // keep it: at 7.6 ms distance the analysis window cannot ring at -9 dB.
        var impulseResponse = new double[65_536];
        void Add(double ms, double amplitude) =>
            impulseResponse[(int)Math.Round(ms * SampleRate / 1000.0)] += amplitude;
        Add(11.466, 0.35); // direct sound
        Add(14.8, 0.25);
        Add(16.5, 0.4);
        Add(17.9, 0.55);
        Add(19.41, 1.0);   // strongest reflection
        Add(20.8, 0.7);
        Add(22.6, 0.55);
        Add(24.9, 0.45);
        Add(27.5, 0.35);   // keeps the direct sound's mirror position hot
        Add(30.4, 0.3);
        Add(33.8, 0.22);
        Add(38.0, 0.15);

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = Math.Sqrt(88.0 * 350.0),
                BandpassPassOctaves = Math.Log2(350.0 / 88.0),
                BandpassFadeOctaves = 1.0,
                FirstPeakThresholdBelowMaxDb = 15
            });

        double firstArrivalMs = result.FirstArrivalPeakSample * 1000.0 / SampleRate;
        Assert.InRange(firstArrivalMs, 11.0, 12.5);
    }

    [Fact]
    public void Analyze_AGenuineWeakEarlyArrivalSurvivesSidelobeRejection()
    {
        // A -10 dB direct arrival 5 ms before a strong reflection, analyzed in
        // the same band that rings: the early arrival has no mirror counterpart
        // in the reflection's tail, so it must be kept as the first arrival while
        // its own pre-ring (and the reflection's) is still rejected.
        var impulseResponse = new double[32_768];
        impulseResponse[300] = 0.316;
        impulseResponse[540] = 1.0;

        TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions
            {
                UseBandpassWindow = true,
                BandpassCenterHz = 1000,
                BandpassPassOctaves = 1,
                BandpassFadeOctaves = 0.5
            });

        Assert.InRange(result.FirstArrivalPeakSample, 297.0, 302.0);
        Assert.InRange(result.StrongestPeakSample, 539.0, 541.0);
    }

    [Fact]
    public void Analyze_FlatUnityCoherence_ReproducesTheNullResultExactly()
    {
        // Threading coherence must be a no-op when it is flat/unity: an all-ones γ² of
        // the correct half-spectrum length must reproduce the null-coherence samples
        // and confidences bit-for-bit, proving the plumbing does not perturb the path.
        var impulseResponse = new double[8_192];
        impulseResponse[300] = 1.0;
        // fftLength = NextPowerOfTwo(8192) = 8192 -> half spectrum length 4097.
        double[] ones = Enumerable.Repeat(1.0, 8_192 / 2 + 1).ToArray();

        TimeAlignmentAnalysisResult baseline = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions());
        TimeAlignmentAnalysisResult weighted = TimeAlignmentAnalysis.Analyze(
            impulseResponse, SampleRate, new TimeAlignmentAnalysisOptions(), ones);

        Assert.Equal(baseline.FirstArrivalPeakSample, weighted.FirstArrivalPeakSample);
        Assert.Equal(baseline.StrongestPeakSample, weighted.StrongestPeakSample);
        Assert.Equal(baseline.FirstArrivalConfidence, weighted.FirstArrivalConfidence);
        Assert.Equal(baseline.StrongestConfidence, weighted.StrongestConfidence);
    }
}
