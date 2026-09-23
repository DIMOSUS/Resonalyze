namespace Resonalyze;

public partial class FirConstructorPanel
{
    private void ImportFile()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = FirFilterFiles.ImportFileDialogFilter,
            Title = "Open FIR filter"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ShowBareKernel(FirFilterFiles.Load(dialog.FileName), Path.GetFileName(dialog.FileName));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                FindForm(),
                "FIR filter could not be opened." + Environment.NewLine + Environment.NewLine + exception.Message,
                "FIR Constructor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ExportFile()
    {
        if (FirConstructorExport.Request(session) is not { } request)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "wav",
            Filter = FirFilterFiles.ExportFileDialogFilter,
            FileName = request.SuggestedFileName,
            OverwritePrompt = true,
            Title = "Export FIR filter"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            FirConstructorExport.Save(request, dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                FindForm(),
                "FIR filter could not be exported." + Environment.NewLine + Environment.NewLine + exception.Message,
                "FIR Constructor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
