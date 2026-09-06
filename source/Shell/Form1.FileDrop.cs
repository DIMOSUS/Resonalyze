namespace Resonalyze;

// Files dragged onto the window from Explorer. The file says which of the
// application's documents it is, so a drop lands where that document belongs
// rather than where the user happened to be standing: an impulse response goes to
// the analyzers, a spatial average to the live analyzer, a session to Virtual DSP.
// It is the Load button's own routing (see Form1.FileOperations), reached without
// the dialog.
//
// The one place where WHERE the file lands does matter is the Compare button: an
// impulse response dropped on it is the reference, not the measurement — the
// button's own Choose file... reached without the dialog (see Form1.Compare).
public partial class Form1
{
    // One at a time. Two files opening at once would each install a measurement,
    // and the one the user sees would be whichever finished last — while the drag
    // that started it looked accepted either way.
    private bool openingDroppedFile;

    private void EnableFileDrop() =>
        FileDropTarget.Attach(this, CanOpenDroppedFiles, OpenDroppedFiles);

    // Answered on every drag move, so it stops at the extension: what the file
    // holds is read once, on the drop. The Compare button takes only what its
    // dialog offers — a .json — so a sweep recording or a REW export hovering over
    // it is refused there, while the rest of the window still takes it.
    private bool CanOpenDroppedFiles(Control over, IReadOnlyList<string> files) =>
        files.Count == 1 &&
        !openingDroppedFile &&
        !expSweepMeasurement.InProgress &&
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
                // The tool has to be on screen before it can be given a session:
                // showing it is what loads its own stored project, and that load
                // would otherwise land on top of the imported one.
                await SelectModeAsync(ModeTab.ToolsVirtualCrossover);
                await virtualCrossoverPanel.ImportSessionFileAsync(path);
                break;

            // The impulse response, the capture and the two imports all take the
            // route the Load button takes, which already sends each one to its own
            // mode and reports its own failures.
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
