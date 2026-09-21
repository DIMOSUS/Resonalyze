using OxyPlot.WindowsForms;

namespace Resonalyze;

internal sealed partial class TimeAlignmentPanelController : IDisposable
{
    private readonly Form owner;
    private readonly TimeAlignmentSession session;
    private readonly Action saveSettings;
    private readonly DeferredRefresh sourcesChanged;
    // Reads only while shown: a hidden panel reads when SetVisible shows it.
    private bool shown;
    private readonly TimeAlignmentPanel panel;
    private readonly Label sourceSummaryLabel;
    private readonly Label compareLabel;
    private readonly RadioButton bandModeFullRadio;
    private readonly RadioButton bandModeAutoRadio;
    private readonly RadioButton bandModeManualRadio;
    private readonly Label autoBandLabel;
    private readonly ThemedNumericUpDown bandpassCenterNumeric;
    private readonly ThemedNumericUpDown bandpassPassOctavesNumeric;
    private readonly ThemedNumericUpDown bandpassFadeOctavesNumeric;
    private readonly PlotView bandpassPlotView;
    private readonly PlotView envelopePlotView;
    // Previews are rebuilt on every configuration change; without these a zoom would not survive the next edit.
    private readonly PlotViewportMemory bandpassViewports;
    private readonly PlotViewportMemory envelopeViewports;
    private readonly StatusRichTextBox statusTextBox;
    private readonly Font resultTableFont;
    private bool disposed;

    public TimeAlignmentPanelController(
        Form owner,
        TimeAlignmentPanel panel,
        TimeAlignmentOptions options,
        AnalyzerDocument document,
        Action saveSettings,
        CompareSelection compareSelection)
    {
        this.owner = owner;
        this.panel = panel;
        session = new TimeAlignmentSession(options, document, compareSelection);
        this.saveSettings = saveSettings;
        sourcesChanged = new DeferredRefresh(owner, RefreshChangedSources);
        // +1 over the panel font, not +4: at +4 the status box wrapped the meters cell.
        resultTableFont = new Font(
            FontFamily.GenericMonospace,
            owner.Font.Size + 1.0f,
            FontStyle.Bold);

        sourceSummaryLabel = panel.SourceSummaryLabel;
        compareLabel = panel.CompareLabel;
        bandModeFullRadio = panel.BandModeFullRadio;
        bandModeAutoRadio = panel.BandModeAutoRadio;
        bandModeManualRadio = panel.BandModeManualRadio;
        autoBandLabel = panel.AutoBandLabel;
        bandpassCenterNumeric = panel.BandpassCenterNumeric;
        bandpassPassOctavesNumeric = panel.BandpassPassOctavesNumeric;
        bandpassFadeOctavesNumeric = panel.BandpassFadeOctavesNumeric;
        bandpassPlotView = panel.BandpassPlotView;
        envelopePlotView = panel.EnvelopePlotView;
        PlotInteraction.Enable(bandpassPlotView);
        PlotInteraction.Enable(envelopePlotView);
        bandpassViewports = new PlotViewportMemory(bandpassPlotView);
        envelopeViewports = new PlotViewportMemory(envelopePlotView);
        statusTextBox = panel.StatusTextBox;
        statusTextBox.UseHandCursorAt = point => TryGetCopyableStatusLine(point, out _);
        statusTextBox.MouseClick += StatusTextBoxMouseClick;

        ApplyOptionsToControls();
        WireEvents();
        RefreshAnalysis();
        session.SourcesChanged += sourcesChanged.Request;
    }

    public bool InProgress => false;

    public void SetLayoutBounds(Rectangle bounds)
    {
        panel.Bounds = bounds;
    }

    public void SetVisible(bool visible)
    {
        shown = visible;
        panel.Visible = visible;
        if (visible)
        {
            RefreshConfiguration();
        }
    }

    /// <summary>Puts the options on the controls, and reads the sources while shown; showing reads them.</summary>
    public void RefreshConfiguration()
    {
        sourcesChanged.Refreshed();
        // The shared options object is written behind this panel (persisted settings, history restore) without touching controls; re-read it or the radios lie.
        ApplyOptionsToControls();
        if (shown)
        {
            RefreshAnalysis();
        }
    }

    public Task AbortAsync() => Task.CompletedTask;

    // While a run or an import holds the document the panel keeps what it read; the end of the hold reads again.
    private void RefreshChangedSources()
    {
        if (!session.SourcesBusy)
        {
            RefreshConfiguration();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        resultTableFont.Dispose();
    }

    private void RefreshAnalysis()
    {
        sourceSummaryLabel.Text = TimeAlignmentSources.MainSummary(session);
        compareLabel.Text = TimeAlignmentSources.CompareSummary(session);

        if (!TimeAlignmentSources.TryGetMain(session, out TimeAlignmentAnalysisSource mainSource, out string noDataMessage))
        {
            session.ForgetReads();
            UpdateAutoBandLabel();
            UpdateBandpassPreview();
            ShowReport(TimeAlignmentReport.ForMessage(noDataMessage), fromTop: false);
            ClearEnvelopePreview();
            return;
        }

        // Preview and caption follow the controls immediately; the Auto caption still names the last finished read's band.
        UpdateAutoBandLabel();
        UpdateBandpassPreview();

        TimeAlignmentRequest request = session.CreateRequest(mainSource);
        if (session.Reads.Submit(request) is { } version)
        {
            StartAnalysis(request, version);
        }
    }

    // Off the UI thread when there is a message loop: a read of a megabyte transfer IR takes a few hundred ms.
    private void StartAnalysis(TimeAlignmentRequest request, int version)
    {
        if (!owner.IsHandleCreated || owner.IsDisposed || owner.InvokeRequired)
        {
            // No handle yet (controllers refresh before the shell has a window; panel tests never open one).
            CompleteAnalysis(request, TimeAlignmentRead.Run(request, session.Records), version);
            return;
        }

        _ = RunAnalysisAsync(request, version);
    }

    private async Task RunAnalysisAsync(TimeAlignmentRequest request, int version)
    {
        TimeAlignmentOutcome outcome;
        try
        {
            outcome = await Task.Run(() => TimeAlignmentRead.Run(request, session.Records));
        }
        catch (Exception exception)
        {
            outcome = TimeAlignmentOutcome.Failed(exception.Message);
        }

        if (!disposed && !owner.IsDisposed)
        {
            CompleteAnalysis(request, outcome, version);
        }
    }

    // Stale reads are not drawn: they would put the previous band's numbers under the current band.
    private void CompleteAnalysis(
        TimeAlignmentRequest request,
        TimeAlignmentOutcome outcome,
        int version)
    {
        if (disposed)
        {
            return;
        }

        // Not drawn, but its pool slot frees here so the wanted read still starts.
        if (!session.Reads.Complete(request, version))
        {
            StartDesiredAnalysis();
            return;
        }

        session.Land(outcome);
        UpdateAutoBandLabel();
        UpdateBandpassPreview();
        if (outcome.Message is { } message)
        {
            ShowReport(TimeAlignmentReport.ForMessage(message), fromTop: false);
            ClearEnvelopePreview();
            StartDesiredAnalysis();
            return;
        }

        ShowReport(TimeAlignmentReport.ForResult(request.BandMode, outcome), fromTop: true);
        envelopeViewports.Show(
            TimeAlignmentPreviews.Envelope(
                outcome.MainResult,
                outcome.MainSource.SampleRate,
                outcome.Compare?.Result),
            Mode.TimeAlignment);
        StartDesiredAnalysis();
    }

    private void StartDesiredAnalysis()
    {
        if (session.Reads.TakeDesired(out TimeAlignmentRequest desired) is { } version)
        {
            StartAnalysis(desired, version);
        }
    }

    private void ClearEnvelopePreview()
    {
        envelopeViewports.Show(TimeAlignmentPreviews.EmptyEnvelope(), Mode.TimeAlignment);
    }
}
