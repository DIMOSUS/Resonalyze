using Resonalyze.History;
using Resonalyze.Options;

namespace Resonalyze;

public partial class Form1
{
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
        clearItem.Enabled = compareSelection.Current != null;
        clearItem.Click += (_, _) => compareSelection.Clear();
        compareMenuStrip.Items.Add(clearItem);

        DropDownMenu.ShowUnder(buttonCompare, compareMenuStrip);
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
                ToolTipText = MeasurementHistoryToolTip.Build(entry.Metadata, entry.Timestamp)
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

        await LoadCompareFileAsync(dialog.FileName);
    }

    /// <summary>A drop on the Compare button lands as the reference, not the measurement. Only impulse responses are taken; other documents are named.</summary>
    private async Task OpenDroppedCompareFileAsync(string path)
    {
        DroppedFileKind kind = DroppedFile.Classify(path);
        if (kind == DroppedFileKind.ImpulseResponse)
        {
            await LoadCompareFileAsync(path);
            return;
        }

        string what = kind switch
        {
            DroppedFileKind.SpatialAverageCapture =>
                "a moving-mic capture. Drop it elsewhere on the window to open it in " +
                "Live Spectrum",
            DroppedFileKind.VirtualDspSession =>
                "a Virtual DSP session. Drop it elsewhere on the window to open it in " +
                "Virtual DSP",
            DroppedFileKind.OverlaySlot =>
                "an overlay slot file — this application's own storage for one slot " +
                "of one mode, which comes back with its mode on its own",
            _ => "not one"
        };
        MessageBox.Show(
            this,
            $"Compare takes a Resonalyze impulse response (.json), and " +
            $"'{Path.GetFileName(path)}' is {what}.",
            "Compare",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private async Task LoadCompareFileAsync(string path)
    {
        try
        {
            ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(path);
            compareSelection.Set(Path.GetFileName(path), path, file.ToResult());
            UpdateLastImpulseResponseDirectory(path);
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
            MeasurementResult? result = await measurementHistoryService.GetResultAsync(entryId);
            if (entry == null || result == null)
            {
                return;
            }

            compareSelection.Set(
                entry.FileNameOrDisplayName,
                entry.SourceFilePath,
                result);
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

    private void OnCompareMeasurementChanged()
    {
        UpdateCompareButton();
        timeAlignmentController.RefreshConfiguration();
        RefreshCurrentModePlot();
        dockedModeSettingsHost.InvokeIfOpen<PROpt>(dialog => dialog.RefreshComparePreview());
        dockedModeSettingsHost.InvokeIfOpen<GDOpt>(dialog => dialog.RefreshComparePreview());
    }

    private void UpdateCompareButton()
    {
        CompareMeasurementSelection? selection = compareSelection.Current;
        buttonCompare.Text = selection?.DisplayName ?? "Compare";
        toolTip1.SetToolTip(
            buttonCompare,
            selection == null
                ? "Choose a second impulse response for comparison"
                : BuildCompareButtonToolTip(selection));
    }

    private static string BuildCompareHistoryItemText(MeasurementHistoryEntry entry) =>
        MenuText.Trim(entry.FileNameOrDisplayName);

    private static string BuildCompareButtonToolTip(CompareMeasurementSelection selection)
    {
        if (!string.IsNullOrWhiteSpace(selection.SourceFilePath))
        {
            return selection.SourceFilePath;
        }

        return selection.DisplayName;
    }
}
