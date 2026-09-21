using System.Globalization;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

public sealed class TimeAlignmentReportTests
{
    private static TimeAlignmentAnalysisResult Result(double snrDb = 40, double firstArrivalMs = 2.0) =>
        new(
            EnvelopeSamples: [],
            EnvelopePeakIndex: 0,
            EnvelopePeak: 1.0,
            StrongestEnvelopePeakIndex: 0,
            StrongestEnvelopePeak: 1.0,
            SignalToNoiseDecibels: snrDb,
            FirstArrivalProminenceDecibels: -10.0,
            FirstArrivalPeakSample: 96.0,
            FirstArrivalDelayMilliseconds: firstArrivalMs,
            StrongestPeakSample: 96.0,
            StrongestDelayMilliseconds: firstArrivalMs,
            StrongestPeakSeparationMilliseconds: 0.0,
            StrongestPeakIsSeparateArrival: false,
            FirstArrivalConfidence: 0.8,
            FirstArrivalRefinedByPhat: true,
            StrongestConfidence: 0.8,
            StrongestRefinedByPhat: true);

    private static TimeAlignmentOutcome Outcome(
        TimeAlignmentAnalysisResult main,
        CrosstalkHeadGate? crosstalk = null,
        TimeAlignmentCompareAnalysis? compare = null,
        string? compareWarning = null) =>
        new(
            default,
            null,
            false,
            main,
            null,
            crosstalk,
            compare,
            null,
            null,
            compareWarning,
            Message: null);

    private static string Text(IReadOnlyList<TimeAlignmentReportSegment> segments) =>
        string.Concat(segments.Select(segment => segment.Text));

    // The report formats numbers in the current culture, as the panel shows them.
    private static IReadOnlyList<TimeAlignmentReportSegment> Report(
        TimeAlignmentBandMode bandMode, TimeAlignmentOutcome outcome)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            return TimeAlignmentReport.ForResult(bandMode, outcome);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AMessageIsOneDimSegment()
    {
        IReadOnlyList<TimeAlignmentReportSegment> segments = TimeAlignmentReport.ForMessage("No impulse response is loaded.");

        TimeAlignmentReportSegment only = Assert.Single(segments);
        Assert.Equal("No impulse response is loaded.", only.Text);
        Assert.Equal(UiPalette.TextSecondary, only.Color);
        Assert.False(only.Table);
    }

    [Fact]
    public void TheDelayTableIsWrittenInTableSegments_WithTheRecommendedRowMarked()
    {
        IReadOnlyList<TimeAlignmentReportSegment> segments =
            Report(TimeAlignmentBandMode.AutoBand, Outcome(Result()));

        Assert.Contains(segments, segment => segment.Table && segment.Text == DelayTableText.FormatHeader() + "\r\n");
        Assert.Contains(
            segments,
            segment => segment.Table && segment.Text == DelayTableText.RecommendedMarker && segment.Color == UiPalette.Success);
        Assert.Contains("Recommended for alignment: First Arrival", Text(segments), StringComparison.Ordinal);
    }

    [Fact]
    public void AFullBandReadWithCrosstalk_WarnsAndRecommendsNothing()
    {
        IReadOnlyList<TimeAlignmentReportSegment> segments = Report(
            TimeAlignmentBandMode.FullBand,
            Outcome(Result(), new CrosstalkHeadGate(40, 0.57, -18.1)));
        string text = Text(segments);

        Assert.Equal(
            UiPalette.Error,
            segments.Single(segment => segment.Text.Contains("Playback crosstalk", StringComparison.Ordinal)).Color);
        Assert.Contains("Playback crosstalk at 0.57 ms", text, StringComparison.Ordinal);
        Assert.Contains("Switch to Auto band", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Recommended for alignment", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareCellsCarryTheirDeltaAgainstMain()
    {
        var compare = new TimeAlignmentCompareAnalysis(default, Result(firstArrivalMs: 2.5));

        string text = Text(Report(
            TimeAlignmentBandMode.AutoBand, Outcome(Result(firstArrivalMs: 2.0), compare: compare)));

        Assert.Contains("Compare Signal: ", text, StringComparison.Ordinal);
        Assert.Contains("2.500 (+0.500)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACompareWarningReplacesItsReport()
    {
        string text = Text(Report(
            TimeAlignmentBandMode.AutoBand,
            Outcome(Result(), compareWarning: "Compare impulse response has no transfer IR.")));

        Assert.Contains("\r\nCompare: Compare impulse response has no transfer IR.\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Compare Signal", text, StringComparison.Ordinal);
    }
}
