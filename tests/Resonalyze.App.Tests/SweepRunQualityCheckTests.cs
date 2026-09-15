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

    // Transfer estimation is scale-invariant; reference usability is judged later by the transfer IR's shape.
    [Fact]
    public void Assess_QuietButPresentLoopbackIsAccepted()
    {
        IReadOnlyList<string> issues = SweepRunQualityCheck.Assess(
            Tone(SweepSamples, 0.5f),
            Tone(SweepSamples, 0.0089f),
            SweepSamples);

        Assert.Empty(issues);
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

    // The pre-playback roll feeds the analysis too, so a clip there must be caught.
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
    public void Report_NamesTheRunThatStoppedTheMeasurement()
    {
        var report = new SweepRunQualityReport(
            RequestedRuns: 4,
            AcceptedRuns: 2,
            Rejections:
            [
                new SweepRunRejection(3, ["the microphone signal clipped"])
            ]);

        string text = report.Describe();

        Assert.Contains("used 2 of the 4 requested sweep runs", text);
        Assert.Contains(
            "Run 3: stopped the measurement (the microphone signal clipped)", text);
        Assert.DoesNotContain("retry", text);
    }

    // The two causes cannot be separated reliably, so the notice names neither.
    [Fact]
    public void ResultCautionQuotesTheReadingAndNamesNoSingleCause()
    {
        string text = new SweepResultCaution(PreArrivalDb: -19.0).Describe();

        Assert.Contains("-19.0 dB", text);
        Assert.Contains("100 to 600 ms AHEAD of the peak", text);
        Assert.Contains("either the reference", text);
        Assert.Contains("strongest sample is not its direct sound", text);
        Assert.Contains("cannot tell which", text);
        Assert.Contains("loopback carries the excitation itself", text);
        Assert.Contains("still usable away from the affected frequencies", text);
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
