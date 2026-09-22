using System.ComponentModel;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>Virtual DSP audition dialog: renders a track through the tune on a cancellable worker.</summary>
internal sealed partial class VirtualCrossoverAuditionDialog : Form
{
    private readonly VirtualCrossoverAuditionSession session;

    public VirtualCrossoverAuditionDialog(
        VirtualCrossoverAuditionContext context, VirtualCrossoverAuditionMemory? memory = null)
    {
        session = VirtualCrossoverAuditionSession.Restore(context, memory ?? VirtualCrossoverAuditionMemory.Process);
        ShowFileDialog = dialog => dialog.ShowDialog(this);
        Ask = (text, buttons, icon) => MessageBox.Show(this, text, "Audition render", buttons, icon);
        InitializeComponent();

        // The panel decides the calibration each opening; a local copy could only disagree with the project.
        MicrophoneCalibrationComboHelper.Configure(
            comboBoxCalibration,
            context.InitialCalibrationId,
            context.CalibrationEntries);
        ReadCalibration();

        comboBoxCabin.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (AuditionCabinOption option in VirtualCrossoverAuditionSession.CabinOptions)
        {
            comboBoxCabin.Items.Add(option);
        }

        comboBoxCabin.SelectedIndex = Math.Max(
            0,
            VirtualCrossoverAuditionSession.CabinOptions.ToList()
                .FindIndex(option => option.Style == session.CabinStyle));
        comboBoxCabin.SelectedIndexChanged += (_, _) => session.CabinStyle =
            comboBoxCabin.SelectedItem is AuditionCabinOption option ? option.Style : null;

        // Muted by hand, not disabled (WinForms disabled grey is unreadable on this theme).
        checkBoxSpatialAverage.Checked = session.SpatialAverageRequested;
        UiStyle.SetTextEnabledLook(
            checkBoxSpatialAverage, context.SpatialAverage != null, interactive: true);
        checkBoxSpatialAverage.CheckedChanged += (_, _) =>
        {
            session.SpatialAverageRequested = checkBoxSpatialAverage.Checked;
            RefreshReport();
        };
        // Wired after Configure, whose SelectedIndex assignment would fire this too early.
        comboBoxCalibration.SelectedIndexChanged += (_, _) =>
        {
            ReadCalibration();
            RefreshRenderEnabled();
            RefreshReport();
        };

        buttonChooseSource.Click += (_, _) => ChooseSource();
        buttonChooseTarget.Click += (_, _) => ChooseTarget();
        buttonRender.Click += async (_, _) => await OnRenderClickedAsync();

        ShowFiles();
        RefreshRenderEnabled();
        RefreshReport();
    }

    /// <summary>Tests answer the file dialogs and the questions here: a real one waits for a person.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<FileDialog, DialogResult> ShowFileDialog { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> Ask { get; set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (session.Rendering)
        {
            e.Cancel = true;
            session.CloseRequested = true;
            RequestCancel();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        session.Remember();
        base.OnFormClosed(e);
    }

    private void ReadCalibration() =>
        session.SelectCalibration(
            MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration),
            comboBoxCalibration.GetItemText(comboBoxCalibration.SelectedItem));

    private void ShowFiles()
    {
        labelSourceFile.Text = session.SourcePath ?? "no file chosen";
        labelTargetFile.Text = session.TargetPath ?? "no file chosen";
    }

    private void ChooseSource()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = AudioFileCodec.ReadableFilesFilter,
            Multiselect = false,
            RestoreDirectory = true,
            Title = "Choose a track to audition"
        };
        if (ShowFileDialog(dialog) != DialogResult.OK)
        {
            return;
        }

        if (session.IsTarget(dialog.FileName))
        {
            Ask(
                "This file is already chosen as the OUTPUT. Rendering a file " +
                "onto itself would destroy the source.",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        session.SelectSource(dialog.FileName);
        ShowFiles();
        RefreshRenderEnabled();
        RefreshReport();
    }

    // WAV only: lossy encoding would add codec artifacts to what is being auditioned.
    private void ChooseTarget()
    {
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "wav",
            FileName = session.SourcePath == null
                ? "audition_processed.wav"
                : Path.GetFileNameWithoutExtension(session.SourcePath) + "_processed.wav",
            Filter = "WAV audio (*.wav)|*.wav",
            InitialDirectory = session.SourcePath == null
                ? null
                : Path.GetDirectoryName(session.SourcePath),
            OverwritePrompt = true,
            Title = "Save the auditioned track"
        };
        if (ShowFileDialog(dialog) != DialogResult.OK)
        {
            return;
        }

        // Writing onto the source would destroy it and make the A/B re-render process the processed file.
        if (session.IsSource(dialog.FileName))
        {
            Ask(
                "This is the source track itself. Choose a different output " +
                "file — rendering onto the source would destroy it.",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        session.SelectTarget(dialog.FileName);
        ShowFiles();
        RefreshRenderEnabled();
    }

    private async Task OnRenderClickedAsync()
    {
        if (session.Rendering)
        {
            RequestCancel();
            return;
        }
        if (!session.HasDistinctFiles)
        {
            return;
        }

        if (session.TargetNeedsConsent)
        {
            DialogResult overwrite = Ask(
                $"The output file already exists:\r\n{session.TargetPath}\r\n\r\nIt " +
                "holds the previous render. Overwrite it?\r\n\r\n(Choose No " +
                "and Save as... to keep both variants for an A/B.)",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (overwrite != DialogResult.Yes)
            {
                return;
            }

            session.ConfirmOverwrite();
        }

        AuditionRenderRequest request = VirtualCrossoverAuditionRender.Request(session);
        CancellationToken cancellation = session.BeginRender();
        SetRunning(true);
        session.ResultSection = string.Empty;
        RefreshReport();

        // The only async hop: created on the UI thread so reports post in order. Lower layers relay synchronously.
        var progress = new Progress<AuditionProgress>(update =>
        {
            if (IsDisposed)
            {
                return;
            }

            labelStatus.Text = update.Status;
            progressBar.Value = (int)Math.Clamp(
                Math.Round(update.Fraction * progressBar.Maximum),
                0,
                progressBar.Maximum);
        });

        try
        {
            AuditionRenderOutcome outcome = await Task.Run(
                () => VirtualCrossoverAuditionRender.Run(request, progress, cancellation),
                cancellation);
            session.ResultSection = VirtualCrossoverAuditionReport.Result(outcome, request.TargetPath);
            labelStatus.Text = $"Finished — wrote {Path.GetFileName(request.TargetPath)}";
        }
        catch (OperationCanceledException)
        {
            session.ResultSection = VirtualCrossoverAuditionReport.Cancelled;
            labelStatus.Text = "Cancelled.";
            progressBar.Value = 0;
        }
        catch (Exception exception)
        {
            session.ResultSection = VirtualCrossoverAuditionReport.Failed(exception.Message);
            labelStatus.Text = "Failed.";
            progressBar.Value = 0;
        }
        finally
        {
            session.EndRender();
            SetRunning(false);
            RefreshReport();
            if (session.CloseRequested)
            {
                Close();
            }
        }
    }

    private void RequestCancel()
    {
        if (session.RequestCancel())
        {
            buttonRender.Enabled = false;
            labelStatus.Text = "Cancelling…";
        }
    }

    private void SetRunning(bool running)
    {
        buttonChooseSource.Enabled = !running;
        buttonChooseTarget.Enabled = !running;
        comboBoxCalibration.Enabled = !running && comboBoxCalibration.Items.Count > 1;
        comboBoxCabin.Enabled = !running;
        checkBoxSpatialAverage.Enabled = !running;
        buttonRender.Text = running ? "Cancel" : "Render";
        if (running)
        {
            progressBar.Value = 0;
            buttonRender.Enabled = true;
        }
        else
        {
            RefreshRenderEnabled();
        }
    }

    private void RefreshRenderEnabled() =>
        buttonRender.Enabled = VirtualCrossoverAuditionRender.Available(session);

    private void RefreshReport() =>
        textBoxReport.Text = VirtualCrossoverAuditionReport.Compose(session);
}
