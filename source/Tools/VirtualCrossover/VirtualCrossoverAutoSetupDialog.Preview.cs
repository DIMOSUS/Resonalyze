namespace Resonalyze;

internal sealed partial class VirtualCrossoverAutoSetupDialog
{
    private int previewGeneration;
    private CancellationTokenSource? previewWork;

    // The ranked run owns Apply and the inputs from the moment it snapshots them. A preview already in flight when
    // Apply was pressed lands afterwards and would otherwise hand the button back mid-ranking.
    private bool rankingInProgress;

    // Long enough to swallow a held spinner arrow, short enough that a single click still feels immediate.
    private const int PreviewDebounceMilliseconds = 120;

    /// <summary>Called by every control that changes the proposal. Junction windows refresh synchronously — a couple
    /// of milliseconds, and a row must never show a window the search is not using — while the fit itself is hundreds
    /// of milliseconds on a four-way, so it runs off the UI thread with the progress bar up.</summary>
    private void SchedulePreview()
    {
        if (!initialized || presenting || rankingInProgress)
        {
            return;
        }

        // Before the early exits: a moved row must not keep its old colour.
        MarkChainOrder();
        RefreshJunctionWindows();
        if (session.SelectedFamilies().Count == 0)
        {
            CancelPreviewWork();
            SetPreviewBusy(false);
            buttonApply.Enabled = false;
            labelPreview.Text = "Enable at least one filter family.";
            return;
        }

        CancelPreviewWork();
        previewWork = new CancellationTokenSource();
        PendingPreview = RunPreviewAsync(previewWork.Token);
    }

    /// <summary>The preview run in flight. The dialog itself never waits on it — the point of the rework is that it
    /// does not — but a test has to know when the late half of the row has landed.</summary>
    internal Task? PendingPreview { get; private set; }

    private void CancelPreviewWork()
    {
        previewWork?.Cancel();
        previewWork?.Dispose();
        previewWork = null;
    }

    private async Task RunPreviewAsync(CancellationToken token)
    {
        int generation = ++previewGeneration;
        SetPreviewBusy(true);
        buttonApply.Enabled = false;
        try
        {
            await Task.Delay(PreviewDebounceMilliseconds, token);
            AutoSetupPreviewInputs inputs = AutoSetupPreviewInputs.Of(session);
            AutoSetupPreview? computed = await Task.Run(() => AutoSetupWizardFit.Preview(inputs), token);
            if (token.IsCancellationRequested || generation != previewGeneration || IsDisposed)
            {
                return;
            }

            ApplyPreview(computed);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change; the newer run owns the UI from here.
        }
        finally
        {
            if (!IsDisposed && generation == previewGeneration)
            {
                SetPreviewBusy(false);
            }
        }
    }

    private void ApplyPreview(AutoSetupPreview? computed)
    {
        buttonApply.Enabled = computed != null && !rankingInProgress;
        if (computed == null)
        {
            labelPreview.Text = "No proposal fits these channels and settings.";
            return;
        }

        session.TakeElevation(computed.ElevationCeiling, computed.ElevationValue);
        presenting = true;
        try
        {
            subElevation.Maximum = session.ElevationRange.Maximum;
            subElevation.Value = session.SubElevationDb;
        }
        finally
        {
            presenting = false;
        }

        labelPreview.Text = string.Join(
            Environment.NewLine, AutoSetupWizardReport.PreviewLines(session, computed));
        UpdateJunctionVerdicts(computed.Fits);
        GrowToFitContents();
    }

    private void SetPreviewBusy(bool busy)
    {
        progressPreview.Visible = busy;
        UiStyle.SetTextEnabledLook(labelPreview, !busy);
    }
}
