using System.Numerics;
using System.Text;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal readonly record struct AuditionProgress(string Status, double Fraction);

/// <summary>Panel state captured when the audition button was pressed.</summary>
internal sealed record VirtualCrossoverAuditionContext(
    Complex[] LeftSum,
    Complex[] RightSum,
    int SampleRate,
    int LeftChannelCount,
    int RightChannelCount,
    string? BorrowedSide,
    Func<string?, CalibrationFile?>? CalibrationResolver,
    IReadOnlyList<MicrophoneCalibrationEntry> CalibrationEntries,
    string? InitialCalibrationId,
    VirtualCrossoverAuditionOwnCalibration OwnCalibration,
    VirtualCrossoverAuditionSpatialAverage? SpatialAverage,
    string? SpatialAverageReason);

/// <summary>What "Own (as measured)" resolves to; <c>Conflict</c> is set when channels used different calibrations (no single filter can be baked into a summed side).</summary>
internal sealed record VirtualCrossoverAuditionOwnCalibration(
    CalibrationFile? Curve,
    string? Name,
    string? Conflict);

/// <summary>The same tune's side sums with magnitudes from spatial averages, prepared by the panel.</summary>
internal sealed record VirtualCrossoverAuditionSpatialAverage(
    Complex[] LeftSum,
    Complex[] RightSum,
    IReadOnlyList<string> ReportLines);

/// <summary>Virtual DSP audition dialog: renders a track through the tune on a cancellable worker.</summary>
internal sealed partial class VirtualCrossoverAuditionDialog : Form
{
    private readonly VirtualCrossoverAuditionSession session;

    public VirtualCrossoverAuditionDialog(VirtualCrossoverAuditionContext context)
    {
        session = VirtualCrossoverAuditionSession.Restore(context);
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
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (session.IsTarget(dialog.FileName))
        {
            MessageBox.Show(
                this,
                "This file is already chosen as the OUTPUT. Rendering a file " +
                "onto itself would destroy the source.",
                "Audition render",
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
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Writing onto the source would destroy it and make the A/B re-render process the processed file.
        if (session.IsSource(dialog.FileName))
        {
            MessageBox.Show(
                this,
                "This is the source track itself. Choose a different output " +
                "file — rendering onto the source would destroy it.",
                "Audition render",
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
            DialogResult overwrite = MessageBox.Show(
                this,
                $"The output file already exists:\r\n{session.TargetPath}\r\n\r\nIt " +
                "holds the previous render. Overwrite it?\r\n\r\n(Choose No " +
                "and Save as... to keep both variants for an A/B.)",
                "Audition render",
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
            session.ResultSection = FormatResult(outcome, request.TargetPath);
            labelStatus.Text = $"Finished — wrote {Path.GetFileName(request.TargetPath)}";
        }
        catch (OperationCanceledException)
        {
            session.ResultSection = "== Result ==\r\nCancelled; nothing was written.";
            labelStatus.Text = "Cancelled.";
            progressBar.Value = 0;
        }
        catch (Exception exception)
        {
            session.ResultSection = $"== Result ==\r\nFAILED: {exception.Message}";
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
        textBoxReport.Text = ComposeReport(
            session.Context,
            session.SpatialAverageRequested,
            VirtualCrossoverAuditionCalibration.Note(session)?.Text,
            session.TrackSection,
            session.ResultSection);

    /// <summary>The whole report; the result leads once present so a finished render is visible without scrolling.</summary>
    internal static string ComposeReport(
        VirtualCrossoverAuditionContext context,
        bool spatialAverageRequested,
        string? calibrationNote,
        string trackSection,
        string resultSection)
    {
        var report = new StringBuilder();
        if (resultSection.Length > 0)
        {
            report.AppendLine(resultSection);
            report.AppendLine();
        }

        report.AppendLine("== Tune ==");
        report.AppendLine($"Project rate: {context.SampleRate} Hz");
        report.AppendLine($"Left side:  {context.LeftChannelCount} channel(s)");
        report.AppendLine($"Right side: {context.RightChannelCount} channel(s)");
        if (context.BorrowedSide != null)
        {
            report.AppendLine(
                $"WARNING: the {context.BorrowedSide} side has no sources — both " +
                "ears will render from the other one and the image will sound " +
                "perfectly centred. That is the missing measurement, not the tune.");
        }

        AppendMagnitudeSection(report, context, spatialAverageRequested);

        if (calibrationNote != null)
        {
            report.AppendLine();
            report.AppendLine("== Calibration ==");
            report.AppendLine(calibrationNote);
        }

        if (trackSection.Length > 0)
        {
            report.AppendLine();
            report.AppendLine(trackSection);
        }

        return report.ToString().TrimEnd();
    }

    private static void AppendMagnitudeSection(
        StringBuilder report,
        VirtualCrossoverAuditionContext context,
        bool spatialAverageRequested)
    {
        report.AppendLine();
        report.AppendLine("== Magnitudes ==");
        if (context.SpatialAverage == null)
        {
            report.AppendLine(
                "From the impulse responses, measured at one microphone position.");
            report.AppendLine(
                "No spatial average is available: " +
                (context.SpatialAverageReason ?? "this tune has none."));
            return;
        }

        if (!spatialAverageRequested)
        {
            report.AppendLine(
                "From the impulse responses, measured at one microphone position — " +
                "tick the box to hear the spatial averages instead.");
            return;
        }

        report.AppendLine(
            "From the spatial averages: every channel is filtered onto its own " +
            "average over the listening volume instead of the one position the " +
            "responses were measured at.");
        foreach (string line in context.SpatialAverage.ReportLines)
        {
            report.AppendLine(line);
        }

        report.AppendLine(
            "Timing, polarity and the interference between channels are unchanged: " +
            "an average carries no phase, so a junction still cancels the way it " +
            "does at that one position.");
    }

    private static string FormatResult(AuditionRenderOutcome outcome, string targetPath)
    {
        AuralizationResult rendered = outcome.Rendered;
        double durationSeconds =
            rendered.Channels[0].Length / (double)rendered.SampleRate;
        var section = new StringBuilder();
        section.AppendLine("== Result ==");
        section.AppendLine($"Magnitudes: {outcome.MagnitudeLabel}");
        section.AppendLine($"Calibration: {outcome.CalibrationLabel}");
        section.AppendLine($"Cabin subtracted: {outcome.CabinLabel}" +
            (outcome.CabinApplied
                ? $" (−{outcome.CabinTwentyHzDb:0.#} dB at 20 Hz)"
                : string.Empty));
        if (outcome.CorrectionFirTaps > 0)
        {
            section.AppendLine(
                $"Correction FIR: {outcome.CorrectionFirTaps} taps, linear " +
                "phase (calibration and cabin combined)");
        }
        section.AppendLine(
            $"Kernels: {outcome.LeftKernelTaps} taps left, " +
            $"{outcome.RightKernelTaps} taps right; decay kept " +
            $"{outcome.LeftTrim.TailMilliseconds:0} / " +
            $"{outcome.RightTrim.TailMilliseconds:0} ms");
        if (rendered.Resampled)
        {
            section.AppendLine(
                $"Track converted {outcome.SourceSampleRate} → " +
                $"{rendered.SampleRate} Hz (the responses were left untouched).");
        }

        section.AppendLine(
            $"Level: {rendered.AppliedGainDb:+0.0;-0.0} dB applied to both " +
            (outcome.CabinApplied
                ? "channels, matched to the no-cabin render (its peak at " +
                    $"{Auralization.DefaultPeakTarget:0.0} dBFS) so the A/B is " +
                    "level-honest"
                : $"channels (peak at {Auralization.DefaultPeakTarget:0.0} dBFS)"));
        section.AppendLine(
            $"Written: {targetPath}");
        section.AppendLine(
            $"Stereo, {rendered.SampleRate} Hz, 24-bit, " +
            $"{VirtualCrossoverAuditionSession.FormatDuration(TimeSpan.FromSeconds(durationSeconds))}");
        section.AppendLine();
        if (outcome.CabinApplied)
        {
            section.AppendLine(
                $"The typical {outcome.CabinLabel} bass rise was subtracted: " +
                "at low frequencies you are hearing this car's deviation from " +
                "that typical curve, not the in-car level.");
            section.AppendLine();
        }

        section.Append(
            "Listen through headphones only. The left and right channels are " +
            "the measured acoustic response of the corresponding side at the " +
            "microphone position — drivers, cabin and capsule included, not a " +
            "binaural head simulation. Playing it back through the same system " +
            "would convolve the car twice.");
        return section.ToString();
    }
}
