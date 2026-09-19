namespace Resonalyze;

// Explorer drops take the Load button's routing by document kind; a drop on the Compare button is the reference instead.
public partial class Form1
{
    // One at a time: concurrent opens would each install a measurement.
    private bool openingDroppedFile;

    private void EnableFileDrop() =>
        FileDropTarget.Attach(this, CanOpenDroppedFiles, OpenDroppedFiles);

    // Runs on every drag move, so extension only; the Compare button takes only .json.
    private bool CanOpenDroppedFiles(Control over, IReadOnlyList<string> files) =>
        files.Count == 1 &&
        !openingDroppedFile &&
        !analyzerDocument.IsBusy &&
        (over == buttonCompare
            ? DroppedFile.HasJsonExtension(files[0])
            : DroppedFile.HasOpenableExtension(files[0]));

    private async void OpenDroppedFiles(Control over, IReadOnlyList<string> files)
    {
        if (!CanOpenDroppedFiles(over, files))
        {
            return;
        }

        openingDroppedFile = true;
        try
        {
            if (over == buttonCompare)
            {
                await OpenDroppedCompareFileAsync(files[0]);
            }
            else
            {
                await OpenDroppedFileAsync(files[0]);
            }
        }
        finally
        {
            openingDroppedFile = false;
        }
    }

    private async Task OpenDroppedFileAsync(string path)
    {
        switch (DroppedFile.Classify(path))
        {
            case DroppedFileKind.VirtualDspSession:
                // Showing the tool loads its stored project, which would otherwise land on top of the import.
                await SelectModeAsync(ModeTab.ToolsVirtualCrossover);
                await virtualCrossoverPanel.ImportSessionFileAsync(path);
                break;

            case DroppedFileKind.ImpulseResponse:
            case DroppedFileKind.SpatialAverageCapture:
            case DroppedFileKind.RecordedSweep:
            case DroppedFileKind.RewImpulseResponseExport:
                await OpenMeasurementFileAsync(path);
                break;

            case DroppedFileKind.OverlaySlot:
                MessageBox.Show(
                    this,
                    "That is an overlay slot file — this application's own storage " +
                    "for one slot of one mode.\r\n\r\nSlots come back with their mode " +
                    "on their own, so there is nothing here to open. To bring a curve " +
                    "in from elsewhere, use Import from text… on the slot it belongs " +
                    "in.",
                    "Open file",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                break;

            default:
                MessageBox.Show(
                    this,
                    $"Resonalyze cannot open '{Path.GetFileName(path)}'.\r\n\r\n" +
                    "Drop an impulse response, a moving-mic capture or a Virtual DSP " +
                    "session (.json), a recorded sweep (.wav), or a REW impulse " +
                    "response export (.txt).",
                    "Open file",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                break;
        }
    }
}
