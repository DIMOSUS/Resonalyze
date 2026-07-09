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
        // this alignment". (The first-arrival envelope index leads the analytic peak by
        // a few samples, so its confidence is reported but not asserted high here.)
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
