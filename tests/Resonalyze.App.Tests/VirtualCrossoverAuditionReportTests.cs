using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverAuditionReportTests
{
    /// <summary>A finished render leads: trailing the briefing it starts below the box and nothing says the file was written.</summary>
    [Fact]
    public void AFinishedRenderLeadsTheReport()
    {
        string report = VirtualCrossoverAuditionReport.Compose(
            Context(Spatial()),
            spatialAverageRequested: true,
            calibrationNote: null,
            trackSection: "== Track ==\r\nsong.wav",
            resultSection: "== Result ==\r\nWritten: out.wav");

        Assert.StartsWith("== Result ==", report);
        Assert.Contains("== Tune ==", report);
        Assert.Contains("== Track ==", report);
    }

    [Fact]
    public void WithNothingRenderedYet_TheBriefingLeads()
    {
        string report = VirtualCrossoverAuditionReport.Compose(
            Context(Spatial()),
            spatialAverageRequested: true,
            calibrationNote: null,
            trackSection: "== Track ==\r\nsong.wav",
            resultSection: string.Empty);

        Assert.StartsWith("== Tune ==", report);
    }

    [Fact]
    public void TheMagnitudeSectionSaysWhereTheLevelsComeFrom()
    {
        string requested = VirtualCrossoverAuditionReport.Compose(
            Context(Spatial()), true, null, string.Empty, string.Empty);
        Assert.Contains("Set offset +1.0 dB", requested);

        string declined = VirtualCrossoverAuditionReport.Compose(
            Context(Spatial()), false, null, string.Empty, string.Empty);
        Assert.Contains("one microphone position", declined);
        Assert.DoesNotContain("Set offset +1.0 dB", declined);

        string unavailable = VirtualCrossoverAuditionReport.Compose(
            Context(spatial: null, reason: "the two sides are not one set."),
            true,
            null,
            string.Empty,
            string.Empty);
        Assert.Contains("the two sides are not one set.", unavailable);
    }

    [Fact]
    public void TheCalibrationBlockAppearsOnlyWhenItHasSomethingToSay()
    {
        string silent = VirtualCrossoverAuditionReport.Compose(
            Context(null), false, null, string.Empty, string.Empty);
        Assert.DoesNotContain("== Calibration ==", silent);

        string spoken = VirtualCrossoverAuditionReport.Compose(
            Context(null), false, "Own (as measured): every channel through 'XREF'.",
            string.Empty, string.Empty);
        Assert.Contains("== Calibration ==", spoken);
        Assert.Contains("XREF", spoken);
    }

    [Fact]
    public void ATrack_SaysWhatTheRenderWillDoToIt()
    {
        string mono = VirtualCrossoverAuditionReport.Track(
            @"C:\music\voice.wav", new AudioFileInfo(1, 44_100, TimeSpan.FromSeconds(75)), 48_000, refusal: null);
        Assert.Equal(
            "== Track ==\r\nvoice.wav\r\n1 channel(s), 44100 Hz, 1:15\r\n" +
            "Will be converted to the project's 48000 Hz (the measured responses are never resampled).\r\n" +
            "Mono: the same signal will feed both sides.",
            mono);

        string quad = VirtualCrossoverAuditionReport.Track(
            "quad.wav", new AudioFileInfo(4, 48_000, TimeSpan.FromSeconds(5)), 48_000, refusal: null);
        Assert.EndsWith("0:05\r\nOnly the first two channels will feed the two sides.", quad);

        string refused = VirtualCrossoverAuditionReport.Track(
            "long.wav", new AudioFileInfo(2, 44_100, TimeSpan.FromMinutes(70)), 48_000, "REFUSED: too long.");
        Assert.EndsWith("70:00\r\nREFUSED: too long.", refused);
    }

    [Fact]
    public void AnUnreadableTrack_IsNamedWithTheReason()
    {
        Assert.Equal(
            "== Track ==\r\nbad.wav\r\nUNREADABLE: no RIFF header",
            VirtualCrossoverAuditionReport.UnreadableTrack(@"D:\bad.wav", "no RIFF header"));
    }

    [Fact]
    public void TheResult_NamesWhatWasApplied_AndWhereItWent()
    {
        var rendered = new AuralizationResult([new float[96_000], new float[96_000]], 48_000, -3.0, true);
        var plain = new AuditionRenderOutcome(
            44_100, rendered, default, default, 100, 90, 0, "off", false, "off", 0, "impulse responses");

        string result = VirtualCrossoverAuditionReport.Result(plain, "out.wav");

        Assert.StartsWith("== Result ==\r\nMagnitudes: impulse responses\r\nCalibration: off\r\nCabin subtracted: off\r\n", result);
        Assert.DoesNotContain("Correction FIR", result);
        Assert.Contains("Track converted 44100 → 48000 Hz", result);
        Assert.Contains("Written: out.wav", result);
        Assert.Contains("24-bit, 0:02", result);
        Assert.DoesNotContain("bass rise", result);

        string cabin = VirtualCrossoverAuditionReport.Result(
            plain with { CorrectionFirTaps = 16_385, CabinApplied = true, CabinLabel = "wagon", CabinTwentyHzDb = 12 },
            "out.wav");
        Assert.Contains("Cabin subtracted: wagon (−12 dB at 20 Hz)", cabin);
        Assert.Contains("Correction FIR: 16385 taps", cabin);
        Assert.Contains("matched to the no-cabin render", cabin);
        Assert.Contains("The typical wagon bass rise was subtracted", cabin);
    }

    [Fact]
    public void ACancelAndAFailure_EachLeaveOneLine()
    {
        Assert.Equal("== Result ==\r\nCancelled; nothing was written.", VirtualCrossoverAuditionReport.Cancelled);
        Assert.Equal("== Result ==\r\nFAILED: disk full", VirtualCrossoverAuditionReport.Failed("disk full"));
    }

    private static VirtualCrossoverAuditionSpatialAverage Spatial() =>
        new([], [], ["Set offset +1.0 dB, channels disagree by 0.4 dB.", "  Sub: +0.0 … +1.0 dB"]);

    private static VirtualCrossoverAuditionContext Context(
        VirtualCrossoverAuditionSpatialAverage? spatial,
        string? reason = null) =>
        new([], [], 48_000, 2, 2, null, null, [], null, new(null, null, null), spatial, reason);
}
