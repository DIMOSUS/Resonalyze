using System.Numerics;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed class TimeAlignmentPanelController : IDisposable
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

    private void WireEvents()
    {
        // Both radios fire CheckedChanged; react to the arriving one only.
        void OnRadio(object? sender, EventArgs _)
        {
            if (!applyingOptions && sender is RadioButton { Checked: true })
            {
                ApplyBandpassOptionChange();
            }
        }
        void OnNumeric(object? sender, EventArgs _)
        {
            if (!applyingOptions)
            {
                ApplyBandpassOptionChange();
            }
        }
        bandModeFullRadio.CheckedChanged += OnRadio;
        bandModeAutoRadio.CheckedChanged += OnRadio;
        bandModeManualRadio.CheckedChanged += OnRadio;
        bandpassCenterNumeric.ValueChanged += OnNumeric;
        bandpassPassOctavesNumeric.ValueChanged += OnNumeric;
        bandpassFadeOctavesNumeric.ValueChanged += OnNumeric;
    }

    private void ApplyBandpassOptionChange()
    {
        UpdateOptionsFromControls();
        RefreshAnalysis();
        saveSettings();
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

    // Writing controls raises the user-edit events, which would save the controls back into the options.
    private bool applyingOptions;

    private void ApplyOptionsToControls()
    {
        applyingOptions = true;
        try
        {
            bandModeFullRadio.Checked =
                session.Options.BandMode == TimeAlignmentBandMode.FullBand;
            bandModeAutoRadio.Checked =
                session.Options.BandMode == TimeAlignmentBandMode.AutoBand;
            bandModeManualRadio.Checked =
                session.Options.BandMode == TimeAlignmentBandMode.ManualBand;
            bandpassCenterNumeric.Value =
                bandpassCenterNumeric.ClampValue(session.Options.BandpassCenterHz);
            bandpassPassOctavesNumeric.Value =
                bandpassPassOctavesNumeric.ClampValue(session.Options.BandpassPassOctaves);
            bandpassFadeOctavesNumeric.Value =
                bandpassFadeOctavesNumeric.ClampValue(session.Options.BandpassFadeOctaves);
        }
        finally
        {
            applyingOptions = false;
        }

        UpdateBandpassControlStates();
    }

    private void UpdateOptionsFromControls()
    {
        session.Options.BandMode =
            bandModeAutoRadio.Checked ? TimeAlignmentBandMode.AutoBand
            : bandModeManualRadio.Checked ? TimeAlignmentBandMode.ManualBand
            : TimeAlignmentBandMode.FullBand;
        session.Options.BandpassCenterHz = (double)bandpassCenterNumeric.Value;
        session.Options.BandpassPassOctaves = (double)bandpassPassOctavesNumeric.Value;
        session.Options.BandpassFadeOctaves = (double)bandpassFadeOctavesNumeric.Value;
        UpdateBandpassControlStates();
    }

    private void UpdateBandpassControlStates()
    {
        bool manual = bandModeManualRadio.Checked;
        bandpassCenterNumeric.Enabled = manual;
        bandpassPassOctavesNumeric.Enabled = manual;
        bandpassFadeOctavesNumeric.Enabled = manual;
    }

    private void UpdateAutoBandLabel()
    {
        autoBandLabel.Text = TimeAlignmentBand.AutoBandCaption(session);
    }

    private void UpdateBandpassPreview()
    {
        PlotModel model = TimeAlignmentPreviews.Bandpass(
            session,
            (double)bandpassCenterNumeric.Value,
            (double)bandpassPassOctavesNumeric.Value,
            (double)bandpassFadeOctavesNumeric.Value);
        bandpassViewports.Show(model, Mode.TimeAlignment);
    }

    private void ClearEnvelopePreview()
    {
        envelopeViewports.Show(TimeAlignmentPreviews.EmptyEnvelope(), Mode.TimeAlignment);
    }

    private void ShowReport(IReadOnlyList<TimeAlignmentReportSegment> segments, bool fromTop)
    {
        statusTextBox.BeginUpdate();
        try
        {
            statusTextBox.Clear();
            foreach (TimeAlignmentReportSegment segment in segments)
            {
                AppendStatusText(segment.Text, segment.Color, segment.Table ? resultTableFont : null);
            }

            if (fromTop)
            {
                statusTextBox.SelectionStart = 0;
                statusTextBox.SelectionLength = 0;
            }
        }
        finally
        {
            statusTextBox.EndUpdate();
        }
    }

    private void AppendStatusText(string text, Color color, Font? font = null)
    {
        statusTextBox.SelectionStart = statusTextBox.TextLength;
        statusTextBox.SelectionLength = 0;
        statusTextBox.SelectionColor = color;
        statusTextBox.SelectionFont = font ?? statusTextBox.Font;
        statusTextBox.AppendText(text);
        statusTextBox.SelectionFont = statusTextBox.Font;
        statusTextBox.SelectionColor = statusTextBox.ForeColor;
    }

    private void StatusTextBoxMouseClick(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left ||
            !TryGetCopyableStatusLine(args.Location, out string value))
        {
            return;
        }

        try
        {
            Clipboard.SetText(value);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another process may hold the clipboard (RDP, clipboard managers); a lost copy must not crash.
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        System.Media.SystemSounds.Asterisk.Play();
    }

    private bool TryGetCopyableStatusLine(Point location, out string value)
    {
        value = string.Empty;
        int index = statusTextBox.GetCharIndexFromPosition(location);
        int line = statusTextBox.GetLineFromCharIndex(index);
        if (line >= statusTextBox.Lines.Length)
        {
            return false;
        }

        string lineText = statusTextBox.Lines[line];
        if (!DelayTableText.IsDelayRow(lineText))
        {
            return false;
        }

        int lineStart = statusTextBox.GetFirstCharIndexFromLine(line);
        int column = Math.Max(0, index - lineStart);
        value = DelayTableText.CellAt(column) is { } cellStart
            ? GetDelayTableValue(lineText, cellStart)
            : string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetDelayTableValue(string line, int startColumn) =>
        DelayTableText.GetValue(line, startColumn);

}

internal sealed class StatusRichTextBox : RichTextBox
{
    private const int WmSetCursor = 0x20;
    private const int WmSetRedraw = 0x0B;
    private int updateDepth;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<Point, bool>? UseHandCursorAt { get; set; }

    public void BeginUpdate()
    {
        if (updateDepth++ == 0 && IsHandleCreated)
        {
            SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void EndUpdate()
    {
        if (updateDepth == 0)
        {
            return;
        }

        updateDepth--;
        if (updateDepth == 0 && IsHandleCreated)
        {
            SendMessage(Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            Invalidate();
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmSetCursor)
        {
            Point point = PointToClient(Cursor.Position);
            Cursor.Current = UseHandCursorAt?.Invoke(point) == true
                ? Cursors.Hand
                : Cursors.Default;
            message.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref message);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);
}
