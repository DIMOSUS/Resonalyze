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
/// <remarks>Calibration and cabin subtraction are linear-phase FIRs in both side kernels. See docs/tech/spatial-average.md#audition-render.</remarks>
internal sealed partial class VirtualCrossoverAuditionDialog : Form
{
    private const double DecodeShare = 0.06;
    private const double RenderShare = 0.86;

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

        // A configured but unreadable file degrades to Off, reported in the result.
        VirtualCrossoverAuditionContext context = session.Context;
        string? calibrationId = session.CalibrationId;
        // "Own" is a rule the calibration list cannot resolve; the panel already resolved it.
        bool own = VirtualCrossoverCalibrationSelection.IsOwn(calibrationId);
        CalibrationFile? calibration = own
            ? context.OwnCalibration.Curve
            : context.CalibrationResolver?.Invoke(calibrationId);
        string calibrationLabel = own
            ? context.OwnCalibration.Name is { } ownName
                ? $"own (as measured): {ownName}"
                : "own (as measured): the measurements recorded none"
            : MicrophoneCalibrationIds.IsOff(calibrationId)
                ? "off"
                : calibration is { HasData: true }
                    ? session.CalibrationName
                    : "off (the calibration file could not be read)";
        if (calibration is not { HasData: true })
        {
            calibration = null;
        }

        CabinTransferFunction? cabin = session.CabinStyle is { } cabinStyle
            ? CabinTransferFunction.FromBodyStyle(cabinStyle)
            : null;
        string cabinLabel = cabin == null
            ? "off"
            : session.CabinLabel;

        VirtualCrossoverAuditionSpatialAverage? spatialAverage = session.RequestedSpatialAverage;
        string magnitudeLabel = spatialAverage != null
            ? "spatial averages (MMM / array)"
            : "impulse responses (one microphone position)";

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

        string source = session.SourcePath!;
        string target = session.TargetPath!;
        try
        {
            RenderOutcome outcome = await Task.Run(
                () => ExecuteRender(
                    context, source, target, calibration, calibrationLabel,
                    cabin, cabinLabel, spatialAverage, magnitudeLabel, progress,
                    cancellation),
                cancellation);
            session.ResultSection = FormatResult(outcome, target);
            labelStatus.Text = $"Finished — wrote {Path.GetFileName(target)}";
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
        buttonRender.Enabled = session.Rendering ||
            (session.SourcePath != null && session.TargetPath != null && CalibrationNote()?.Refused != true);

    /// <summary>Calibration note for the report; only "Own (as measured)" has anything to say.</summary>
    private (string Text, bool Refused)? CalibrationNote()
    {
        if (!VirtualCrossoverCalibrationSelection.IsOwn(session.CalibrationId))
        {
            return null;
        }

        VirtualCrossoverAuditionOwnCalibration ownCalibration = session.Context.OwnCalibration;
        if (ownCalibration.Conflict is { } conflict)
        {
            return ($"REFUSED: {conflict}. Choose one of the calibrations above, or Off.",
                true);
        }

        return (ownCalibration.Name is { } name
            ? $"Own (as measured): every channel was read through '{name}', and the " +
                "render carries it."
            : "Own (as measured): the measurements recorded no calibration, so the " +
                "render carries none.",
            false);
    }

    // Worker thread; static and argument-fed so it cannot touch a control.
    private static RenderOutcome ExecuteRender(
        VirtualCrossoverAuditionContext context,
        string sourcePath,
        string targetPath,
        CalibrationFile? calibration,
        string calibrationLabel,
        CabinTransferFunction? cabin,
        string cabinLabel,
        VirtualCrossoverAuditionSpatialAverage? spatialAverage,
        string magnitudeLabel,
        IProgress<AuditionProgress> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new AuditionProgress("Preparing the responses…", 0));
        Complex[] leftSum = spatialAverage?.LeftSum ?? context.LeftSum;
        Complex[] rightSum = spatialAverage?.RightSum ?? context.RightSum;
        double[] leftKernel = Auralization.TrimResponse(
            leftSum, context.SampleRate, out AuralizationTrim leftTrim);
        double[] rightKernel = Auralization.TrimResponse(
            rightSum, context.SampleRate, out AuralizationTrim rightTrim);

        // Calibration and cabin combine into one linear-phase FIR in both kernels; with cabin subtraction a calibration-only
        // reference pair is built first for level matching. See docs/tech/spatial-average.md#audition-render.
        double[]? referenceLeftKernel = null;
        double[]? referenceRightKernel = null;
        if (cabin != null)
        {
            if (calibration != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double[] calFir = CalibrationFirFilter.Design(
                    calibration.GetDecibelCorrection, context.SampleRate);
                referenceLeftKernel = FastConvolution.Convolve(leftKernel, calFir);
                referenceRightKernel = FastConvolution.Convolve(rightKernel, calFir);
            }
            else
            {
                referenceLeftKernel = leftKernel;
                referenceRightKernel = rightKernel;
            }
        }

        int correctionFirTaps = 0;
        if (calibration != null || cabin != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double[] fir = CalibrationFirFilter.Design(
                frequencyHz =>
                    (calibration?.GetDecibelCorrection(frequencyHz) ?? 0.0) +
                    (cabin?.Evaluate(frequencyHz) ?? 0.0),
                context.SampleRate);
            correctionFirTaps = fir.Length;
            leftKernel = FastConvolution.Convolve(leftKernel, fir);
            rightKernel = FastConvolution.Convolve(rightKernel, fir);
        }

        progress.Report(new AuditionProgress("Decoding the track…", 0.01));
        // Only two channels are decoded; the byte cap also bounds the decode itself.
        AudioFileContent material = AudioFileCodec.Read(
            sourcePath,
            TimeSpan.FromMinutes(VirtualCrossoverAuditionSession.MaximumTrackMinutes),
            channelLimit: 2,
            VirtualCrossoverAuditionSession.MaximumPipelineBytes,
            cancellationToken);

        // Re-check the budget on the actual frame count: the header may lie or the file may have changed.
        long actualBytes = VirtualCrossoverAuditionSession.ProjectedPipelineBytes(
            material.FrameCount, material.SampleRate, context.SampleRate);
        if (actualBytes > VirtualCrossoverAuditionSession.MaximumPipelineBytes)
        {
            throw new InvalidOperationException(
                $"The decoded track is larger than its header promised: " +
                $"rendering would hold ~{actualBytes / 1_000_000} MB of audio " +
                $"in memory (bound {VirtualCrossoverAuditionSession.MaximumPipelineBytes / 1_000_000} MB). " +
                "Use a shorter excerpt.");
        }

        var renderProgress = new SynchronousProgress<double>(value =>
            progress.Report(new AuditionProgress(
                "Rendering through the tune…",
                DecodeShare + value * RenderShare)));
        AuralizationResult rendered = Auralization.Render(
            new AuralizationRequest
            {
                LeftKernel = leftKernel,
                RightKernel = rightKernel,
                ReferenceLeftKernel = referenceLeftKernel,
                ReferenceRightKernel = referenceRightKernel,
                KernelSampleRate = context.SampleRate,
                SourceChannels = material.Channels,
                SourceSampleRate = material.SampleRate
            },
            renderProgress,
            cancellationToken);

        progress.Report(new AuditionProgress(
            "Writing the WAV file…", DecodeShare + RenderShare));
        WriteRenderedTrack(targetPath, rendered, cancellationToken);
        progress.Report(new AuditionProgress("Finished", 1.0));

        return new RenderOutcome(
            material.SampleRate,
            rendered,
            leftTrim,
            rightTrim,
            leftKernel.Length,
            rightKernel.Length,
            correctionFirTaps,
            calibrationLabel,
            cabin != null,
            cabinLabel,
            cabin?.Evaluate(20.0) ?? 0.0,
            magnitudeLabel);
    }

    // Via a temporary file so a cancel or failure never leaves a truncated WAV in place.
    private static void WriteRenderedTrack(
        string targetPath,
        AuralizationResult result,
        CancellationToken cancellationToken)
    {
        string temporaryPath = targetPath + ".partial";
        try
        {
            AudioFileCodec.WriteWav(
                temporaryPath,
                new AudioFileContent(result.Channels, result.SampleRate),
                cancellationToken);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }

            throw;
        }
    }

    private void RefreshReport() =>
        textBoxReport.Text = ComposeReport(
            session.Context,
            session.SpatialAverageRequested,
            CalibrationNote()?.Text,
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

    private static string FormatResult(RenderOutcome outcome, string targetPath)
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

    private sealed record RenderOutcome(
        int SourceSampleRate,
        AuralizationResult Rendered,
        AuralizationTrim LeftTrim,
        AuralizationTrim RightTrim,
        int LeftKernelTaps,
        int RightKernelTaps,
        int CorrectionFirTaps,
        string CalibrationLabel,
        bool CabinApplied,
        string CabinLabel,
        double CabinTwentyHzDb,
        string MagnitudeLabel);
}
