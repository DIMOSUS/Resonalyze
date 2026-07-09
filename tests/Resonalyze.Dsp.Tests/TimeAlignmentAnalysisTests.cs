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
}
