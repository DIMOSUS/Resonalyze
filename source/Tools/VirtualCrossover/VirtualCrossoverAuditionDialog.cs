using System.Numerics;
using System.Text;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>One step of a running audition render, for the progress read-out.</summary>
internal readonly record struct AuditionProgress(string Status, double Fraction);

/// <summary>
/// Everything the audition dialog needs from the panel, captured at the moment
/// the button was pressed: the two sides' summed responses (immutable
/// snapshots), the project rate, and the microphone-calibration sources the
/// panel itself uses for its curves.
/// </summary>
internal sealed record VirtualCrossoverAuditionContext(
    Complex[] LeftSum,
    Complex[] RightSum,
    int SampleRate,
    int LeftChannelCount,
    int RightChannelCount,
    string? BorrowedSide,
    Func<MicrophoneCalibrationMode, CalibrationFile?>? CalibrationResolver,
    bool HasZeroDegreeCalibration,
    bool HasNinetyDegreeCalibration,
    MicrophoneCalibrationMode InitialCalibrationMode);

/// <summary>
/// The Virtual DSP audition dialog: pick a track and a destination, optionally a
/// microphone calibration, render, and read what happened. The render runs on a
/// worker task behind the progress bar and stays cancellable; the report block
/// accumulates the input file's shape, the tune's responses and the result.
/// <para>
/// The chosen calibration becomes a linear-phase FIR baked into BOTH side
/// kernels before the track convolution — one filter, both sides, so the
/// magnitude matches the calibrated on-screen curves while the inter-side
/// timing (the thing being auditioned) shifts by exactly the same constant on
/// each channel.
/// </para>
/// </summary>
internal sealed partial class VirtualCrossoverAuditionDialog : Form
{
    // A render holds the decoded material, its resampled copy and the result in
    // memory at once, so length is bounded rather than left to fail as an
    // out-of-memory crash deep in a background task. The duration cap alone is
    // not a memory cap — ten minutes at 192 kHz is six times ten minutes at
    // 32 kHz — so the pipeline's projected bytes are bounded separately.
    private const int MaximumTrackMinutes = 10;
    private const long MaximumPipelineBytes = 1_000_000_000;

    // Progress budget: decoding and writing are single passes over the material
    // against the render's block transforms, so they get the ends of the bar.
    private const double DecodeShare = 0.06;
    private const double RenderShare = 0.86;

    private readonly VirtualCrossoverAuditionContext context;

    private string? sourcePath;
    private string? targetPath;
    private string trackSection = string.Empty;
    private string resultSection = string.Empty;

    private CancellationTokenSource? activeRender;
    private bool closeRequested;

    public VirtualCrossoverAuditionDialog(VirtualCrossoverAuditionContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        InitializeComponent();

        MicrophoneCalibrationComboHelper.Configure(
            comboBoxCalibration,
            context.InitialCalibrationMode,
            context.HasZeroDegreeCalibration,
            context.HasNinetyDegreeCalibration);

        buttonChooseSource.Click += (_, _) => ChooseSource();
        buttonChooseTarget.Click += (_, _) => ChooseTarget();
        buttonRender.Click += async (_, _) => await OnRenderClickedAsync();

        RefreshRenderEnabled();
        RefreshReport();
    }

    // The dialog cannot close while a render is writing: cancel it and let the
    // completion path below close the form once the worker has unwound.
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

    // ---------------------------------------------------------------- pickers

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

        // Probe now, not at render time: an unreadable, over-long or over-sized
        // file should say so the moment it is picked, in the report, with
        // Render disabled.
        try
        {
            AudioFileInfo info = AudioFileCodec.Probe(dialog.FileName);
            long projectedBytes = ProjectedPipelineBytes(info, context.SampleRate);
            var section = new StringBuilder();
            section.AppendLine("== Track ==");
            section.AppendLine(Path.GetFileName(dialog.FileName));
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
                // The duration alone does not bound memory: the render keeps the
                // decoded stereo source, its resampled copy and both rendered
                // sides alive at its peak, and that scales with the rates.
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

                sourcePath = dialog.FileName;
                labelSourceFile.Text = dialog.FileName;
            }

            trackSection = section.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            sourcePath = null;
            labelSourceFile.Text = "no file chosen";
            trackSection =
                "== Track ==\r\n" +
                $"{Path.GetFileName(dialog.FileName)}\r\n" +
                $"UNREADABLE: {exception.Message}";
        }

        resultSection = string.Empty;
        RefreshRenderEnabled();
        RefreshReport();
    }

    // WAV only, and deliberately so: re-encoding to a lossy format would put
    // codec artifacts inside the very thing being auditioned, and Windows does
    // not guarantee an MP3 encoder is installed at all.
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

        // Writing onto the source would destroy it on the first render — and
        // the advertised A/B re-render would then process the processed file.
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

    // The render's peak working set: the decoded stereo source, its resampled
    // copy (absent when the rates already match) and the two rendered sides,
    // all float32 and all alive at once. Channels beyond two are never stored
    // (the decoder is told to keep two), so two is the multiplier even for
    // multichannel files.
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

    // ----------------------------------------------------------------- render

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

        // Resolved on the UI thread: the combo and the resolver belong here.
        // A configured-but-unreadable file degrades to Off, called out in the
        // result rather than silently.
        MicrophoneCalibrationMode mode =
            MicrophoneCalibrationComboHelper.GetSelectedMode(comboBoxCalibration);
        CalibrationFile? calibration = mode == MicrophoneCalibrationMode.Off
            ? null
            : context.CalibrationResolver?.Invoke(mode);
        string calibrationLabel = mode == MicrophoneCalibrationMode.Off
            ? "off"
            : calibration is { HasData: true }
                ? comboBoxCalibration.GetItemText(comboBoxCalibration.SelectedItem)
                : "off (the calibration file could not be read)";
        if (calibration is not { HasData: true })
        {
            calibration = null;
        }

        var cancellation = new CancellationTokenSource();
        activeRender = cancellation;
        SetRunning(true);
        resultSection = string.Empty;
        RefreshReport();

        // The ONE asynchronous hop in the whole progress chain: created here on
        // the UI thread, so Progress<T> posts to the UI context — in order.
        // Every layer below it relays synchronously (SynchronousProgress); a
        // worker-created Progress<T> would post to the thread pool and reorder.
        // The IsDisposed guard covers reports still queued when the form goes.
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
                    progress, cancellation.Token),
                cancellation.Token);
            resultSection = FormatResult(outcome, target);
            labelStatus.Text = "Finished.";
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
        buttonRender.Enabled =
            activeRender != null || (sourcePath != null && targetPath != null);

    // The whole pipeline on the worker thread: trim, calibrate, decode, render,
    // write. Static and argument-fed so it cannot touch a control.
    private static RenderOutcome ExecuteRender(
        VirtualCrossoverAuditionContext context,
        string sourcePath,
        string targetPath,
        CalibrationFile? calibration,
        string calibrationLabel,
        IProgress<AuditionProgress> progress,
        CancellationToken cancellationToken)
    {
        progress.Report(new AuditionProgress("Preparing the responses…", 0));
        double[] leftKernel = Auralization.TrimResponse(
            context.LeftSum, context.SampleRate, out AuralizationTrim leftTrim);
        double[] rightKernel = Auralization.TrimResponse(
            context.RightSum, context.SampleRate, out AuralizationTrim rightTrim);

        // The calibration FIR is baked into the KERNELS, not run over the track:
        // two short double-precision convolutions instead of a third full-track
        // pass, and the normalization below then reads the calibrated response.
        int firTaps = 0;
        if (calibration != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double[] fir = CalibrationFirFilter.Design(
                calibration.GetDecibelCorrection, context.SampleRate);
            firTaps = fir.Length;
            leftKernel = FastConvolution.Convolve(leftKernel, fir);
            rightKernel = FastConvolution.Convolve(rightKernel, fir);
        }

        progress.Report(new AuditionProgress("Decoding the track…", 0.01));
        // Only the first two channels are kept — the render feeds channel 1 to
        // the left side and channel 2 to the right, and storing a 7.1 layout
        // would quadruple the decoded footprint for nothing.
        AudioFileContent material = AudioFileCodec.Read(
            sourcePath,
            TimeSpan.FromMinutes(MaximumTrackMinutes),
            channelLimit: 2,
            cancellationToken);

        // The pick-time budget was a preflight over the container's CLAIMED
        // duration; the decode is the truth — the file may have been replaced
        // since the pick, or the header may simply lie — so the same bound is
        // enforced again on the actual frame count, before the resampled and
        // rendered buffers come into existence.
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
            firTaps,
            calibrationLabel);
    }

    // Through a temporary file beside the target: a cancel or a failure part-way
    // through a multi-hundred-megabyte write must not leave a truncated WAV
    // where the user's chosen file — possibly one they asked to overwrite —
    // used to be.
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
                // Losing the temporary file matters less than the original error.
            }

            throw;
        }
    }

    // ----------------------------------------------------------------- report

    private void RefreshReport()
    {
        var report = new StringBuilder();
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

        if (trackSection.Length > 0)
        {
            report.AppendLine();
            report.AppendLine(trackSection);
        }

        if (resultSection.Length > 0)
        {
            report.AppendLine();
            report.AppendLine(resultSection);
        }

        textBoxReport.Text = report.ToString().TrimEnd();
    }

    private static string FormatResult(RenderOutcome outcome, string targetPath)
    {
        AuralizationResult rendered = outcome.Rendered;
        double durationSeconds =
            rendered.Channels[0].Length / (double)rendered.SampleRate;
        var section = new StringBuilder();
        section.AppendLine("== Result ==");
        section.AppendLine($"Calibration: {outcome.CalibrationLabel}" +
            (outcome.FirTaps > 0 ? $" (FIR {outcome.FirTaps} taps)" : string.Empty));
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
            $"channels (peak at {Auralization.DefaultPeakTarget:0.0} dBFS)");
        section.AppendLine(
            $"Written: {targetPath}");
        section.AppendLine(
            $"Stereo, {rendered.SampleRate} Hz, 24-bit, " +
            $"{FormatDuration(TimeSpan.FromSeconds(durationSeconds))}");
        section.AppendLine();
        section.Append(
            "Listen through headphones only. The left and right channels are " +
            "the measured acoustic response of the corresponding side at the " +
            "microphone position — drivers, cabin and capsule included, not a " +
            "binaural head simulation. Playing it back through the same system " +
            "would convolve the car twice.");
        return section.ToString();
    }

    // Total minutes, not the minutes component: a refused over-an-hour file
    // must not display as its remainder ("1:02:03" as "2:03").
    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

    private sealed record RenderOutcome(
        int SourceSampleRate,
        AuralizationResult Rendered,
        AuralizationTrim LeftTrim,
        AuralizationTrim RightTrim,
        int LeftKernelTaps,
        int RightKernelTaps,
        int FirTaps,
        string CalibrationLabel);
}
