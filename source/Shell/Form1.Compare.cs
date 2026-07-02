using System.Numerics;
using Resonalyze.History;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
    private void buttonCompare_Click(object? sender, EventArgs e)
    {
        ShowCompareMenu();
    }

    private void ShowCompareMenu()
    {
        compareMenuStrip?.Dispose();
        compareMenuStrip = new ContextMenuStrip();

        ToolStripMenuItem chooseFileItem = new("Choose file...");
        chooseFileItem.Click += async (_, _) => await ChooseCompareFileAsync();
        compareMenuStrip.Items.Add(chooseFileItem);

        ToolStripMenuItem historyItem = new("History");
        PopulateCompareHistoryMenu(historyItem);
        compareMenuStrip.Items.Add(historyItem);

        compareMenuStrip.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem clearItem = new("Clear");
        clearItem.Enabled = compareMeasurement != null;
        clearItem.Click += (_, _) => ClearCompareMeasurement();
        compareMenuStrip.Items.Add(clearItem);

        compareMenuStrip.Show(buttonCompare, new Point(0, buttonCompare.Height));
    }

    private void PopulateCompareHistoryMenu(ToolStripMenuItem historyItem)
    {
        IReadOnlyList<MeasurementHistoryEntry> entries = measurementHistoryService.Entries;
        if (entries.Count == 0)
        {
            historyItem.Enabled = false;
            return;
        }

        foreach (MeasurementHistoryEntry entry in entries)
        {
            ToolStripMenuItem entryItem = new(BuildCompareHistoryItemText(entry))
            {
                Tag = entry.Id,
                ToolTipText = entry.Metadata.BuildToolTipText(entry.Timestamp)
            };
            entryItem.Click += async (_, _) =>
            {
                if (entryItem.Tag is Guid entryId)
                {
                    await SelectCompareHistoryEntryAsync(entryId);
                }
            };
            historyItem.DropDownItems.Add(entryItem);
        }
    }

    private async Task ChooseCompareFileAsync()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = GetImpulseResponseDialogDirectory(),
            Multiselect = false,
            RestoreDirectory = true,
            Title = "Choose compare impulse response"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(dialog.FileName);
            MeasurementHistorySnapshot snapshot =
                MeasurementHistoryService.CreateSnapshot(file);
            SetCompareMeasurement(
                Path.GetFileName(dialog.FileName),
                dialog.FileName,
                snapshot);
            UpdateLastImpulseResponseDirectory(dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to load the compare impulse response.\r\n\r\n{exception.Message}",
                "Compare",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task SelectCompareHistoryEntryAsync(Guid entryId)
    {
        try
        {
            MeasurementHistoryEntry? entry = measurementHistoryService.FindById(entryId);
            MeasurementHistorySnapshot? snapshot =
                await measurementHistoryService.GetSnapshotAsync(entryId);
            if (entry == null || snapshot == null)
            {
                return;
            }

            SetCompareMeasurement(
                entry.FileNameOrDisplayName,
                entry.SourceFilePath,
                snapshot);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Failed to load the compare history entry.\r\n\r\n{exception.Message}",
                "Compare",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SetCompareMeasurement(
        string displayName,
        string? sourceFilePath,
        MeasurementHistorySnapshot snapshot)
    {
        compareMeasurement = new CompareMeasurementSelection(
            displayName,
            sourceFilePath,
            snapshot);
        OnCompareMeasurementChanged();
    }

    private void ClearCompareMeasurement()
    {
        compareMeasurement = null;
        OnCompareMeasurementChanged();
    }

    // Compare drives Time Alignment, the Phase / Group Delay plots, and the gated IR
    // preview inside their docked settings, so refresh whichever of those is live.
    private void OnCompareMeasurementChanged()
    {
        UpdateCompareButton();
        timeAlignmentController.RefreshConfiguration();
        RefreshCurrentModePlot();
        dockedModeSettingsHost.InvokeIfOpen<PROpt>(dialog => dialog.RefreshComparePreview());
        dockedModeSettingsHost.InvokeIfOpen<GDOpt>(dialog => dialog.RefreshComparePreview());
    }

    // Exposes the Compare measurement's impulse responses for the mode plots: the
    // transfer IR (Phase / Group Delay / Impulse) and the sweep-deconvolution IR
    // (Frequency Response). Each consumer checks the response it needs, so a Compare
    // without a transfer IR still contributes a Frequency Response curve.
    internal CompareAnalysisSource? GetCompareAnalysisSource()
    {
        if (compareMeasurement is not { } selection ||
            selection.Snapshot.SweepDeconvolutionImpulseResponse is not { Length: > 0 } sweepIr)
        {
            return null;
        }

        return new CompareAnalysisSource(
            selection.DisplayName,
            selection.Snapshot.SampleRate,
            selection.Snapshot.TransferImpulseResponse ?? Array.Empty<Complex>(),
            selection.Snapshot.TransferPeakIndex ?? 0,
            sweepIr,
            selection.Snapshot.SweepDeconvolutionPeakIndex);
    }

    // The complex (vector) sum of the Main and Compare transfer responses for the
    // ComplexSum calculated overlay in Frequency Response. Null while unavailable
    // (no Compare, either side lacks a transfer IR, or the sample rates differ);
    // the overlay stays armed and draws once the data appears. The delay and
    // polarity flip apply to the Compare response, mirroring a DSP channel setup.
    // When showLoss is set, returns the signed dB gap of the complex sum relative to the
    // magnitude sum (<= 0: how much the real phase-aware sum falls short of the phase-blind
    // addition) instead of the sum itself.
    internal OverlayPoint[]? BuildComplexSumOverlayPoints(
        double compareDelayMs,
        bool invertComparePolarity,
        bool showLoss = false)
    {
        Resonalyze.Dsp.AnalysisCurve? curve = showLoss
            ? plotModelFactory.TryBuildComplexSumLossCurve(compareDelayMs, invertComparePolarity)
            : plotModelFactory.TryBuildComplexSumCurve(compareDelayMs, invertComparePolarity);
        return curve?.Points
            .Select(point => new OverlayPoint(point.X, point.Y))
            .ToArray();
    }

    private TimeAlignmentCompareMeasurement? GetTimeAlignmentCompareMeasurement() =>
        compareMeasurement == null
            ? null
            : new TimeAlignmentCompareMeasurement(
                compareMeasurement.DisplayName,
                compareMeasurement.Snapshot);

    private void UpdateCompareButton()
    {
        buttonCompare.Text = compareMeasurement?.DisplayName ?? "Compare";
        toolTip1.SetToolTip(
            buttonCompare,
            compareMeasurement == null
                ? "Choose a second impulse response for comparison"
                : BuildCompareButtonToolTip(compareMeasurement));
    }

    private static string BuildCompareHistoryItemText(MeasurementHistoryEntry entry)
    {
        string text = entry.FileNameOrDisplayName;
        if (text.Length <= 48)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, 45), "...");
    }

    private static string BuildCompareButtonToolTip(CompareMeasurementSelection selection)
    {
        if (!string.IsNullOrWhiteSpace(selection.SourceFilePath))
        {
            return selection.SourceFilePath;
        }

        return selection.DisplayName;
    }

    private sealed record CompareMeasurementSelection(
        string DisplayName,
        string? SourceFilePath,
        MeasurementHistorySnapshot Snapshot);
}

// The Compare measurement's impulse responses used by the mode plots: the transfer IR
// (Phase / Group Delay / Impulse and the gated IR preview) and the sweep-deconvolution
// IR (Frequency Response magnitude). Matching sample rate is validated by the consumers.
public readonly record struct CompareAnalysisSource(
    string DisplayName,
    int SampleRate,
    Complex[] TransferImpulseResponse,
    int TransferPeakIndex,
    Complex[] SweepDeconvolutionImpulseResponse,
    int SweepDeconvolutionPeakIndex);
