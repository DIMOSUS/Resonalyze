namespace Resonalyze;

internal sealed partial class OverlaySlotView
{
    private const string TextFileFilter = "Overlay points (*.txt)|*.txt|All files (*.*)|*.*";

    private void ImportFromText()
    {
        Mode mode = Session.Sources.CurrentOverlayMode;
        if (mode == Mode.None)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = TextFileFilter,
            Title = "Import overlay points"
        };
        if (dialog.ShowDialog(owner.Form) != DialogResult.OK)
        {
            return;
        }

        OverlayTextCurve imported;
        try
        {
            imported = OverlayTextFile.ImportCurve(dialog.FileName);
        }
        catch (Exception exception)
        {
            owner.ShowStorageError("Overlay could not be imported.", exception);
            return;
        }

        if (OverlayCapture.RefusalFor(imported) is { } refusal)
        {
            MessageBox.Show(
                owner.Form,
                refusal,
                "Import from text",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Session.Import(slot, imported, dialog.FileName, mode);
    }

    private void ExportToText()
    {
        OverlayPoint[]? points = Session.Curves.ExportPoints(slot);
        if (points == null || points.Length < 2)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "txt",
            FileName = $"{OverlayCapture.SanitizeFileName(slot.Title)}.txt",
            Filter = TextFileFilter,
            Title = "Export overlay points"
        };
        if (dialog.ShowDialog(owner.Form) != DialogResult.OK)
        {
            return;
        }

        try
        {
            OverlayTextFile.Export(dialog.FileName, points, OverlayCapture.ExportMetadata(slot.State));
        }
        catch (Exception exception)
        {
            owner.ShowStorageError("Overlay could not be exported.", exception);
        }
    }

    private void ExportDeviationToText()
    {
        if (Session.Curves.DeviationExport(slot) is not { } export)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "txt",
            FileName = $"{OverlayCapture.SanitizeFileName(slot.Title)} - {export.Suffix}.txt",
            Filter = TextFileFilter,
            Title = $"Export {export.Suffix}"
        };
        if (dialog.ShowDialog(owner.Form) != DialogResult.OK)
        {
            return;
        }

        try
        {
            OverlayTextFile.Export(dialog.FileName, export.Points, OverlayCapture.DeviationMetadata(slot.State, export));
        }
        catch (Exception exception)
        {
            owner.ShowStorageError("Deviation could not be exported.", exception);
        }
    }
}
