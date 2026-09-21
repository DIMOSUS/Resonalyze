using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What Time Alignment reads (the open measurement and the compare selection), with which band options,
/// and what the last drawn read found. UI-free; the panel controller writes the reads and the Auto band.</summary>
internal sealed class TimeAlignmentSession
{
    private readonly AnalyzerDocument document;
    private readonly CompareSelection compareSelection;

    public TimeAlignmentSession(
        TimeAlignmentOptions options,
        AnalyzerDocument document,
        CompareSelection compareSelection)
    {
        Options = options;
        this.document = document;
        this.compareSelection = compareSelection;
    }

    /// <summary>Shared with the settings file and history, which write it without telling the panel.</summary>
    public TimeAlignmentOptions Options { get; }

    // The panel is asked to refresh more often than anything changes; the schedule skips re-reading an unchanged record.
    public AnalysisReadSchedule<TimeAlignmentRequest> Reads { get; } = new();

    public TimeAlignmentRecordCache Records { get; } = new();

    /// <summary>Band the last drawn Auto read used; null until one lands or when there is nothing to read.</summary>
    public DominantBand? AutoBand { get; private set; }

    // Overlap of Main's and Compare's bands rather than Main's alone; the caption says which.
    public bool AutoBandShared { get; private set; }

    public MeasurementResult? Main => document.Result;

    public string? MainFileName =>
        string.IsNullOrWhiteSpace(document.SourceName) ? null : Path.GetFileName(document.SourceName);

    public TimeAlignmentCompareMeasurement? Compare => compareSelection.GetTimeAlignmentMeasurement();

    /// <summary>A run or an import holds the document: keep what was read until the hold ends.</summary>
    public bool SourcesBusy => document.IsBusy;

    public event Action SourcesChanged
    {
        add
        {
            document.Changed += value;
            compareSelection.Changed += value;
        }
        remove
        {
            document.Changed -= value;
            compareSelection.Changed -= value;
        }
    }

    // Band frozen into the request: the worker must state the band it actually read, whatever the options become.
    public TimeAlignmentRequest CreateRequest(TimeAlignmentAnalysisSource mainSource) =>
        new(
            mainSource,
            Compare,
            Options.BandMode,
            Options.BandpassCenterHz,
            Options.BandpassPassOctaves,
            Options.BandpassFadeOctaves,
            Options.FirstPeakThresholdBelowMaxDb,
            Options.FirstPeakMinimumSnrDb,
            Options.PeakSearchWindowMilliseconds);

    public void ForgetReads()
    {
        Reads.Clear();
        AutoBand = null;
    }

    public void Land(TimeAlignmentOutcome outcome)
    {
        AutoBand = outcome.AutoBand;
        AutoBandShared = outcome.AutoBandShared;
    }
}

// Equal requests describe the same read, so a repeated refresh recognizes the answer already drawn.
internal readonly record struct TimeAlignmentRequest(
    TimeAlignmentAnalysisSource MainSource,
    TimeAlignmentCompareMeasurement? Compare,
    TimeAlignmentBandMode BandMode,
    double BandpassCenterHz,
    double BandpassPassOctaves,
    double BandpassFadeOctaves,
    double FirstPeakThresholdBelowMaxDb,
    double FirstPeakMinimumSnrDb,
    double PeakSearchWindowMilliseconds);

// Message instead of a result: why there is nothing to show (no energy in band, analysis threw).
internal sealed record TimeAlignmentOutcome(
    TimeAlignmentAnalysisSource MainSource,
    DominantBand? AutoBand,
    bool AutoBandShared,
    TimeAlignmentAnalysisResult MainResult,
    TimeAlignmentArrivalProbe? MainProbe,
    CrosstalkHeadGate? MainCrosstalk,
    TimeAlignmentCompareAnalysis? Compare,
    TimeAlignmentArrivalProbe? CompareProbe,
    CrosstalkHeadGate? CompareCrosstalk,
    string? CompareWarning,
    string? Message)
{
    public static TimeAlignmentOutcome Failed(
        string message,
        DominantBand? autoBand = null,
        bool autoBandShared = false) =>
        new(
            default,
            autoBand,
            autoBandShared,
            default,
            null,
            null,
            null,
            null,
            null,
            null,
            message);
}
