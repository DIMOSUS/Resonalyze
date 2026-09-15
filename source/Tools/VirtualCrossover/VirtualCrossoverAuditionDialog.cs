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
    // Duration cap plus a separate projected-bytes cap (memory scales with rate).
    private const int MaximumTrackMinutes = 10;
    private const long MaximumPipelineBytes = 1_000_000_000;

    private const double DecodeShare = 0.06;
    private const double RenderShare = 0.86;

    private readonly VirtualCrossoverAuditionContext context;

    private string? sourcePath;
    private string? targetPath;

    // Overwrite consent granted in this dialog only (OverwritePrompt on an existing file, or the render-time question); otherwise an A/B pair could collapse.
    private bool targetOverwriteConfirmed;
    private string trackSection = string.Empty;
    private string resultSection = string.Empty;

    private CancellationTokenSource? activeRender;
    private bool closeRequested;

    // Remembered per process, not persisted: a session renders one track through several tunes. Re-probed on restore.
    private static string? lastSourcePath;
    private static string? lastTargetPath;

    private static bool lastSpatialAverage = true;

    private static CabinBodyStyle? lastCabinStyle = CabinBodyStyle.Sedan;

    // "off" first: index 0 is the fallback everywhere.
    private static readonly CabinOption[] CabinOptions =
    [
        new(null, "off (as measured)"),
        new(CabinBodyStyle.Sedan, "average sedan"),
        new(CabinBodyStyle.CompactSedan, "compact sedan"),
        new(CabinBodyStyle.Hatchback, "hatchback"),
        new(CabinBodyStyle.Wagon, "wagon"),
        new(CabinBodyStyle.Suv, "SUV / crossover"),
        new(CabinBodyStyle.BmwF30SkiHatch, "BMW F30, ski hatch open")
    ];

    public VirtualCrossoverAuditionDialog(VirtualCrossoverAuditionContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        InitializeComponent();

        // The panel decides the calibration each opening; a local copy could only disagree with the project.
        MicrophoneCalibrationComboHelper.Configure(
            comboBoxCalibration,
            context.InitialCalibrationId,
            context.CalibrationEntries);

        comboBoxCabin.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (CabinOption option in CabinOptions)
        {
            comboBoxCabin.Items.Add(option);
        }

        comboBoxCabin.SelectedIndex = Math.Max(
            0, Array.FindIndex(CabinOptions, option => option.Style == lastCabinStyle));

        // Muted by hand, not disabled (WinForms disabled grey is unreadable on this theme).
        checkBoxSpatialAverage.Checked =
            context.SpatialAverage != null && lastSpatialAverage;
        UiStyle.SetTextEnabledLook(
            checkBoxSpatialAverage, context.SpatialAverage != null, interactive: true);
        checkBoxSpatialAverage.CheckedChanged += (_, _) => RefreshReport();
        // Wired after Configure, whose SelectedIndex assignment would fire this too early.
        comboBoxCalibration.SelectedIndexChanged += (_, _) =>
        {
            RefreshRenderEnabled();
            RefreshReport();
        };

        buttonChooseSource.Click += (_, _) => ChooseSource();
        buttonChooseTarget.Click += (_, _) => ChooseTarget();
        buttonRender.Click += async (_, _) => await OnRenderClickedAsync();

        if (lastSourcePath != null)
        {
            ApplySourceSelection(lastSourcePath);
        }
        if (lastTargetPath != null)
        {
            // Restored without overwrite consent: the file is the previous render.
            targetPath = lastTargetPath;
            labelTargetFile.Text = lastTargetPath;
            targetOverwriteConfirmed = false;
        }

        RefreshRenderEnabled();
        RefreshReport();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (activeRender != null)
        {
            e.Cancel = true;
            closeRequested = true;
            RequestCancel();
            return;
        }

        base.OnFormClosing(e);
    }

    // Unconditional: saving null stops the next opening retrying a dead path.
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        lastSourcePath = sourcePath;
        lastTargetPath = targetPath;
        lastCabinStyle = SelectedCabinStyle;
        // Remember only a real choice: an unticked box with no averages is not a preference.
        if (context.SpatialAverage != null)
        {
            lastSpatialAverage = checkBoxSpatialAverage.Checked;
        }

        base.OnFormClosed(e);
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

        if (PathsEqual(dialog.FileName, targetPath))
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

        ApplySourceSelection(dialog.FileName);
    }

    // Probes at pick time so an unreadable, too long or too large file is refused before Render.
    private void ApplySourceSelection(string fileName)
    {
        try
        {
            AudioFileInfo info = AudioFileCodec.Probe(fileName);
            long projectedBytes = ProjectedPipelineBytes(info, context.SampleRate);
            var section = new StringBuilder();
            section.AppendLine("== Track ==");
            section.AppendLine(Path.GetFileName(fileName));
            section.AppendLine(
                $"{info.ChannelCount} channel(s), {info.SampleRate} Hz, " +
                $"{FormatDuration(info.Duration)}");
            if (info.Duration > TimeSpan.FromMinutes(MaximumTrackMinutes))
            {
                section.Append(
                    $"REFUSED: longer than {MaximumTrackMinutes} minutes — " +
                    "use a shorter excerpt.");
                sourcePath = null;
                labelSourceFile.Text = "no file chosen";
            }
            else if (projectedBytes > MaximumPipelineBytes)
            {
                double allowedMinutes = MaximumPipelineBytes
                    / (ProjectedPipelineBytes(
                        info with { Duration = TimeSpan.FromMinutes(1) },
                        context.SampleRate) * 1.0);
                section.Append(
                    $"REFUSED: rendering this would hold ~" +
                    $"{projectedBytes / 1_000_000} MB of audio in memory " +
                    $"(bound {MaximumPipelineBytes / 1_000_000} MB). At these " +
                    $"rates keep the excerpt under ~{allowedMinutes:0} minutes.");
                sourcePath = null;
                labelSourceFile.Text = "no file chosen";
            }
            else
            {
                if (info.SampleRate != context.SampleRate)
                {
                    section.AppendLine(
                        $"Will be converted to the project's {context.SampleRate} Hz " +
                        "(the measured responses are never resampled).");
                }
                if (info.ChannelCount == 1)
                {
                    section.AppendLine("Mono: the same signal will feed both sides.");
                }
                else if (info.ChannelCount > 2)
                {
                    section.AppendLine(
                        "Only the first two channels will feed the two sides.");
                }

                sourcePath = fileName;
                labelSourceFile.Text = fileName;
            }

            trackSection = section.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            sourcePath = null;
            labelSourceFile.Text = "no file chosen";
            trackSection =
                "== Track ==\r\n" +
                $"{Path.GetFileName(fileName)}\r\n" +
                $"UNREADABLE: {exception.Message}";
        }

        resultSection = string.Empty;
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
            FileName = sourcePath == null
                ? "audition_processed.wav"
                : Path.GetFileNameWithoutExtension(sourcePath) + "_processed.wav",
            Filter = "WAV audio (*.wav)|*.wav",
            InitialDirectory = sourcePath == null
                ? null
                : Path.GetDirectoryName(sourcePath),
            OverwritePrompt = true,
            Title = "Save the auditioned track"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Writing onto the source would destroy it and make the A/B re-render process the processed file.
        if (PathsEqual(dialog.FileName, sourcePath))
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

        // An existing file was just confirmed by OverwritePrompt; a new one will ask on the next render.
        targetOverwriteConfirmed = File.Exists(dialog.FileName);
        targetPath = dialog.FileName;
        labelTargetFile.Text = dialog.FileName;
        RefreshRenderEnabled();
    }

    private static bool PathsEqual(string? first, string? second) =>
        first != null && second != null &&
        string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            StringComparison.OrdinalIgnoreCase);

    // Peak working set: decoded stereo, resampled copy (if rates differ) and two rendered sides, all float32.
    private static long ProjectedPipelineBytes(
        long sourceFrames, int sourceRate, int projectRate)
    {
        long renderedFrames = (long)Math.Ceiling(
            sourceFrames * (double)projectRate / sourceRate);
        long resampledFrames = sourceRate == projectRate ? 0 : renderedFrames;
        return 4L * 2L * (sourceFrames + resampledFrames + renderedFrames);
    }

    private static long ProjectedPipelineBytes(AudioFileInfo info, int projectRate) =>
        ProjectedPipelineBytes(
            (long)Math.Ceiling(info.Duration.TotalSeconds * info.SampleRate),
            info.SampleRate,
            projectRate);

    private async Task OnRenderClickedAsync()
    {
        if (activeRender != null)
        {
            RequestCancel();
            return;
        }
        if (sourcePath == null || targetPath == null || PathsEqual(sourcePath, targetPath))
        {
            return;
        }

        if (File.Exists(targetPath) && !targetOverwriteConfirmed)
        {
            DialogResult overwrite = MessageBox.Show(
                this,
                $"The output file already exists:\r\n{targetPath}\r\n\r\nIt " +
                "holds the previous render. Overwrite it?\r\n\r\n(Choose No " +
                "and Save as... to keep both variants for an A/B.)",
                "Audition render",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (overwrite != DialogResult.Yes)
            {
                return;
            }

            targetOverwriteConfirmed = true;
        }

        // A configured but unreadable file degrades to Off, reported in the result.
        string? calibrationId =
            MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration);
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
                    ? comboBoxCalibration.GetItemText(comboBoxCalibration.SelectedItem)
                    : "off (the calibration file could not be read)";
        if (calibration is not { HasData: true })
        {
            calibration = null;
        }

        CabinTransferFunction? cabin = SelectedCabinStyle is { } cabinStyle
            ? CabinTransferFunction.FromBodyStyle(cabinStyle)
            : null;
        string cabinLabel = cabin == null
            ? "off"
            : comboBoxCabin.GetItemText(comboBoxCabin.SelectedItem);

        VirtualCrossoverAuditionSpatialAverage? spatialAverage = RequestedSpatialAverage;
        string magnitudeLabel = spatialAverage != null
            ? "spatial averages (MMM / array)"
            : "impulse responses (one microphone position)";

        var cancellation = new CancellationTokenSource();
        activeRender = cancellation;
        SetRunning(true);
        resultSection = string.Empty;
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

        string source = sourcePath;
        string target = targetPath;
        try
        {
            RenderOutcome outcome = await Task.Run(
                () => ExecuteRender(
                    context, source, target, calibration, calibrationLabel,
                    cabin, cabinLabel, spatialAverage, magnitudeLabel, progress,
                    cancellation.Token),
                cancellation.Token);
            resultSection = FormatResult(outcome, target);
            labelStatus.Text = $"Finished — wrote {Path.GetFileName(target)}";
        }
        catch (OperationCanceledException)
        {
            resultSection = "== Result ==\r\nCancelled; nothing was written.";
            labelStatus.Text = "Cancelled.";
            progressBar.Value = 0;
        }
        catch (Exception exception)
        {
            resultSection = $"== Result ==\r\nFAILED: {exception.Message}";
            labelStatus.Text = "Failed.";
            progressBar.Value = 0;
        }
        finally
        {
            activeRender = null;
            cancellation.Dispose();
            SetRunning(false);
            RefreshReport();
            if (closeRequested)
            {
                Close();
            }
        }
    }

    private void RequestCancel()
    {
        if (activeRender is { IsCancellationRequested: false } cancellation)
        {
            cancellation.Cancel();
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
        buttonRender.Enabled = activeRender != null ||
            (sourcePath != null && targetPath != null && CalibrationNote()?.Refused != true);

    /// <summary>Calibration note for the report; only "Own (as measured)" has anything to say.</summary>
    private (string Text, bool Refused)? CalibrationNote()
    {
        if (!VirtualCrossoverCalibrationSelection.IsOwn(
            MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration)))
        {
            return null;
        }

        if (context.OwnCalibration.Conflict is { } conflict)
        {
            return ($"REFUSED: {conflict}. Choose one of the calibrations above, or Off.",
                true);
        }

        return (context.OwnCalibration.Name is { } name
            ? $"Own (as measured): every channel was read through '{name}', and the " +
                "render carries it."
            : "Own (as measured): the measurements recorded no calibration, so the " +
                "render carries none.",
            false);
    }

    private CabinBodyStyle? SelectedCabinStyle =>
        comboBoxCabin.SelectedItem is CabinOption option ? option.Style : null;

    private VirtualCrossoverAuditionSpatialAverage? RequestedSpatialAverage =>
        checkBoxSpatialAverage.Checked ? context.SpatialAverage : null;

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
            TimeSpan.FromMinutes(MaximumTrackMinutes),
            channelLimit: 2,
            MaximumPipelineBytes,
            cancellationToken);

        // Re-check the budget on the actual frame count: the header may lie or the file may have changed.
        long actualBytes = ProjectedPipelineBytes(
            material.FrameCount, material.SampleRate, context.SampleRate);
        if (actualBytes > MaximumPipelineBytes)
        {
            throw new InvalidOperationException(
                $"The decoded track is larger than its header promised: " +
                $"rendering would hold ~{actualBytes / 1_000_000} MB of audio " +
                $"in memory (bound {MaximumPipelineBytes / 1_000_000} MB). " +
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
            context,
            checkBoxSpatialAverage.Checked,
            CalibrationNote()?.Text,
            trackSection,
            resultSection);

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
            $"{FormatDuration(TimeSpan.FromSeconds(durationSeconds))}");
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

    // Total minutes, so over-an-hour durations do not show only the remainder.
    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

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

    private sealed record CabinOption(CabinBodyStyle? Style, string Label)
    {
        public override string ToString() => Label;
    }
}
