namespace Resonalyze.App.Tests;

public sealed class VirtualCrossoverAuditionReportTests
{
    /// <summary>A finished render leads: trailing the briefing it starts below the box and nothing says the file was written.</summary>
    [Fact]
    public void AFinishedRenderLeadsTheReport()
    {
        string report = VirtualCrossoverAuditionDialog.ComposeReport(
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
        string report = VirtualCrossoverAuditionDialog.ComposeReport(
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
        string requested = VirtualCrossoverAuditionDialog.ComposeReport(
            Context(Spatial()), true, null, string.Empty, string.Empty);
        Assert.Contains("Set offset +1.0 dB", requested);

        string declined = VirtualCrossoverAuditionDialog.ComposeReport(
            Context(Spatial()), false, null, string.Empty, string.Empty);
        Assert.Contains("one microphone position", declined);
        Assert.DoesNotContain("Set offset +1.0 dB", declined);

        string unavailable = VirtualCrossoverAuditionDialog.ComposeReport(
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
        string silent = VirtualCrossoverAuditionDialog.ComposeReport(
            Context(null), false, null, string.Empty, string.Empty);
        Assert.DoesNotContain("== Calibration ==", silent);

        string spoken = VirtualCrossoverAuditionDialog.ComposeReport(
            Context(null), false, "Own (as measured): every channel through 'XREF'.",
            string.Empty, string.Empty);
        Assert.Contains("== Calibration ==", spoken);
        Assert.Contains("XREF", spoken);
    }

    private static VirtualCrossoverAuditionSpatialAverage Spatial() =>
        new([], [], ["Set offset +1.0 dB, channels disagree by 0.4 dB.", "  Sub: +0.0 … +1.0 dB"]);

    private static VirtualCrossoverAuditionContext Context(
        VirtualCrossoverAuditionSpatialAverage? spatial,
        string? reason = null) =>
        new([], [], 48_000, 2, 2, null, null, [], null, new(null, null, null), spatial, reason);
}
