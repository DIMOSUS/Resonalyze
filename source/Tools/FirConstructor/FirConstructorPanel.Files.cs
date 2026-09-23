using System.ComponentModel;

namespace Resonalyze;

public partial class FirConstructorPanel
{
    /// <summary>Shows a file dialog over the panel's form; a test answers it instead.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<FileDialog, DialogResult> ShowFileDialog { get; set; }

    /// <summary>Shows a warning over the panel's form; a test reads it instead.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<string> Warn { get; set; }

    private void ImportFile()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = FirFilterFiles.ImportFileDialogFilter,
            Title = "Open FIR filter"
        };
        if (ShowFileDialog(dialog) != DialogResult.OK)
        {
            return;
        }

        try
        {
            ShowBareKernel(FirFilterFiles.Load(dialog.FileName), Path.GetFileName(dialog.FileName));
        }
        catch (Exception exception)
        {
            Warn("FIR filter could not be opened." + Environment.NewLine + Environment.NewLine + exception.Message);
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
        if (ShowFileDialog(dialog) != DialogResult.OK)
        {
            return;
        }

        try
        {
            FirConstructorExport.Save(request, dialog.FileName);
        }
        catch (Exception exception)
        {
            Warn("FIR filter could not be exported." + Environment.NewLine + Environment.NewLine + exception.Message);
        }
    }

    private DialogResult ShowOverForm(FileDialog dialog) => dialog.ShowDialog(FindForm());

    private void WarnOverForm(string text) =>
        MessageBox.Show(FindForm(), text, "FIR Constructor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
