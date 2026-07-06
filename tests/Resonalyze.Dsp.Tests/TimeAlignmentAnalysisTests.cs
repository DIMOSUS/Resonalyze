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
