namespace Resonalyze.App.Tests;

/// <summary>
/// The audition dialog's report: what it says, and — because it is the only thing
/// that reports a finished render — what it says FIRST.
/// </summary>
public sealed class VirtualCrossoverAuditionReportTests
{
    /// <summary>
    /// A finished render leads. It used to trail three growing blocks, and on a tune
    /// with a spatial average per channel it started below the bottom of the box: the
    /// progress bar read 100% over a report that looked exactly like the one before
    /// the render, and nothing said the file had been written.
    /// </summary>
    [Fact]
    public void AFinishedRenderLeadsTheReport()
    {
        string report = VirtualCrossoverAuditionDialog.ComposeReport(
            Context(Spatial()),
            spatialAverageRequested: true,
            trackSection: "== Track ==\r\nsong.wav",
            resultSection: "== Result ==\r\nWritten: out.wav");

        Assert.StartsWith("== Result ==", report);
        // And the briefing for the next render is still there, under it.
        Assert.Contains("== Tune ==", report);
        Assert.Contains("== Track ==", report);
    }

    /// <summary>
    /// Before anything has been rendered the briefing leads, which is what those
    /// blocks are for while a render is being set up.
    /// </summary>
    [Fact]
    public void WithNothingRenderedYet_TheBriefingLeads()
    {
        string report = VirtualCrossoverAuditionDialog.ComposeReport(
            Context(Spatial()),
            spatialAverageRequested: true,
            trackSection: "== Track ==\r\nsong.wav",
            resultSection: string.Empty);

        Assert.StartsWith("== Tune ==", report);
    }

    /// <summary>
    /// The spatial-average section states what will be done — or, where it cannot be
    /// done, why. A muted checkbox with no explanation beside it is a control the user
    /// has no way to satisfy.
    /// </summary>
    [Fact]
    public void TheMagnitudeSectionSaysWhereTheLevelsComeFrom()
    {
        string requested = VirtualCrossoverAuditionDialog.ComposeReport(
            Context(Spatial()), true, string.Empty, string.Empty);
        Assert.Contains("Set offset +1.0 dB", requested);

        string declined = VirtualCrossoverAuditionDialog.ComposeReport(
            Context(Spatial()), false, string.Empty, string.Empty);
        Assert.Contains("one microphone position", declined);
        Assert.DoesNotContain("Set offset +1.0 dB", declined);

        string unavailable = VirtualCrossoverAuditionDialog.ComposeReport(
            Context(spatial: null, reason: "the two sides are not one set."),
            true,
            string.Empty,
            string.Empty);
        Assert.Contains("the two sides are not one set.", unavailable);
    }

    private static VirtualCrossoverAuditionSpatialAverage Spatial() =>
        new([], [], ["Set offset +1.0 dB, channels disagree by 0.4 dB.", "  Sub: +0.0 … +1.0 dB"]);

    private static VirtualCrossoverAuditionContext Context(
        VirtualCrossoverAuditionSpatialAverage? spatial,
        string? reason = null) =>
        new([], [], 48_000, 2, 2, null, null, [], null, spatial, reason);
}
