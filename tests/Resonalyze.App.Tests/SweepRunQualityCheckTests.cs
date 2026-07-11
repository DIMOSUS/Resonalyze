namespace Resonalyze.App.Tests;

public sealed class SweepRunQualityCheckTests
{
    private const int SweepSamples = 1000;

    [Fact]
    public void Assess_CleanRunHasNoIssues()
    {
        IReadOnlyList<string> issues = SweepRunQualityCheck.Assess(
            Tone(SweepSamples, 0.5f),
            Tone(SweepSamples, 0.9f),
            SweepSamples);

        Assert.Empty(issues);
    }

    [Fact]
    public void Assess_ClippedMicrophoneIsRejected()
    {
        float[] microphone = Tone(SweepSamples, 0.5f);
        microphone[123] = 1.0f;

        IReadOnlyList<string> issues = SweepRunQualityCheck.Assess(
            microphone,
            Tone(SweepSamples, 0.9f),
            SweepSamples);

        Assert.Contains("the microphone signal clipped", issues);
    }

    [Fact]
    public void Assess_FullScaleLoopbackIsTheReferenceNotClipping()
    {
        IReadOnlyList<string> issues = SweepRunQualityCheck.Assess(
            Tone(SweepSamples, 0.5f),
            Tone(SweepSamples, 1.0f),
            SweepSamples);

        Assert.Empty(issues);
    }

    [Fact]
    public void Assess_SilentMicrophoneIsRejected()
    {
        IReadOnlyList<string> issues = SweepRunQualityCheck.Assess(
            Tone(SweepSamples, 1e-5f),
            Tone(SweepSamples, 0.9f),
            SweepSamples);

        Assert.Contains("the microphone signal is silent", issues);
    }

    [Fact]
    public void Assess_SilentLoopbackIsRejected()
    {
        IReadOnlyList<string> issues = SweepRunQualityCheck.Assess(
            Tone(SweepSamples, 0.5f),
            new float[SweepSamples],
            SweepSamples);

        Assert.Contains("the loopback reference signal is silent", issues);
    }

    [Fact]
    public void Assess_MissingLoopbackSkipsTheLoopbackCheck()
    {
        IReadOnlyList<string> issues = SweepRunQualityCheck.Assess(
            Tone(SweepSamples, 0.5f),
            loopback: null,
            SweepSamples);

        Assert.Empty(issues);
    }

    [Fact]
    public void Assess_UndersizedCaptureIsRejected()
    {
        IReadOnlyList<string> issues = SweepRunQualityCheck.Assess(
            Tone(SweepSamples / 2, 0.5f),
            Tone(SweepSamples / 2, 0.9f),
            SweepSamples);

        Assert.Contains(
            issues,
            issue => issue.StartsWith("the capture is shorter than the sweep"));
    }

    // Both recorders reset per run and the whole snapshot (including the
    // pre-playback roll) feeds the analysis, so a knock BEFORE the sweep
    // started must be caught too - the checked and analyzed ranges match.
    [Fact]
    public void Assess_ClipInThePrePlaybackRollIsCaught()
    {
        float[] microphone = Tone(SweepSamples * 2, 0.5f);
        microphone[10] = 1.0f;

        IReadOnlyList<string> issues = SweepRunQualityCheck.Assess(
            microphone,
            Tone(SweepSamples * 2, 0.9f),
            SweepSamples);

        Assert.Contains("the microphone signal clipped", issues);
    }

    [Fact]
    public void Report_IsDegradedOnlyWhenRunsAreMissing()
    {
        Assert.False(new SweepRunQualityReport(8, 8, []).IsDegraded);
        Assert.True(new SweepRunQualityReport(8, 5, []).IsDegraded);
    }

    [Fact]
    public void Report_DescribeListsCountsAndPerRunReasons()
    {
        var report = new SweepRunQualityReport(
            RequestedRuns: 4,
            AcceptedRuns: 3,
            Rejections:
            [
                new SweepRunRejection(2, Retried: false, ["the microphone signal clipped"]),
                new SweepRunRejection(
                    2,
                    Retried: true,
                    ["the microphone signal clipped", "the loopback reference signal is silent"])
            ]);

        string text = report.Describe();

        Assert.Contains("used 3 of the 4 requested sweep runs", text);
        Assert.Contains("Run 2: the microphone signal clipped", text);
        Assert.Contains(
            "Run 2 (retry): the microphone signal clipped, " +
            "the loopback reference signal is silent",
            text);
    }

    private static float[] Tone(int length, float amplitude)
    {
        var samples = new float[length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = amplitude * MathF.Sin(2 * MathF.PI * i / 64f);
        }

        return samples;
    }
}
