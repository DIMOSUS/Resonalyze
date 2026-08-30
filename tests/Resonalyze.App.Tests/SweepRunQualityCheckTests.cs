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

    // Quiet-but-present is ACCEPTED per run, however far down it sits:
    // transfer estimation is scale-invariant, so a cleanly attenuated wire
    // (the readme itself says to turn the playback level well down) measures
    // fine. Whether the reference was USABLE is judged by the transfer IR's
    // shape after the runs — a bleed-fed capture cannot pass that gate.
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
    public void Report_NamesTheRunThatStoppedTheMeasurement()
    {
        // There is no retry any more, so there is no "recovered" case to separate: a
        // bad run stops the measurement, and the report says which one and why. The
        // retry never recovered anything in the field — a gain set wrong or a cable in
        // the wrong socket is reproduced exactly by the next sweep.
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

    // A ring on a band wide enough to recognise one: the reference is the thing to
    // check, and the notice says so.
    [Fact]
    public void ResultCautionBlamesTheReferenceOnlyWhenTheBandCanShowIt()
    {
        string text = new SweepResultCaution(
            PreArrivalDb: -19.0,
            CrestDb: 12.8,
            CanDiagnoseCause: true).Describe();

        Assert.Contains("-19.0 dB", text);
        Assert.Contains("divided BY", text);
        Assert.Contains("loopback carries the excitation itself", text);
    }

    // The same reading on a band too narrow to tell a ring from one arrival. The
    // measurement is kept and reported, but nothing is blamed: at this width a
    // single arrival smears until it reads like a ring, and sending the tuner to
    // check a reference that is fine is the failure the distortion diagnosis
    // exists to avoid.
    [Fact]
    public void ResultCautionNamesNoCauseWhenTheBandCannotSeparateThem()
    {
        string text = new SweepResultCaution(
            PreArrivalDb: -19.0,
            CrestDb: 12.8,
            CanDiagnoseCause: false).Describe();

        Assert.Contains("-19.0 dB", text);
        Assert.Contains("cannot be told from this measurement", text);
        Assert.Contains("Nothing is being blamed", text);
        Assert.DoesNotContain("loopback carries the excitation itself", text);
        Assert.DoesNotContain("divided BY", text);
    }

    // A discrete event on a band that can show it: the reference is exonerated
    // instead, and the reader is sent to the microphone.
    [Fact]
    public void ResultCautionNamesTheArrivalWhenTheWindowHoldsOneEvent()
    {
        string text = new SweepResultCaution(
            PreArrivalDb: -17.0,
            CrestDb: 21.0,
            CanDiagnoseCause: true).Describe();

        Assert.Contains("one discrete event", text);
        Assert.Contains("The reference is probably fine", text);
        Assert.DoesNotContain("loopback carries the excitation itself", text);
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
