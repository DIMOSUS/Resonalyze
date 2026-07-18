using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Resonalyze.Audio;
using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    /// <summary>
    /// Modal SPL calibration: the user fits an acoustic calibrator over the
    /// microphone, picks its reference level, and listens for the tone. On success
    /// the dialog exposes an <see cref="SplCalibration"/> anchor; the caller is
    /// responsible for persisting it. Capture runs on the same input the caller is
    /// configuring, so the anchor is pinned to that digital tract.
    /// </summary>
    internal sealed partial class SplCalibrationDialog : Form
    {
        // A power-of-two block; ~5.9 Hz bins at 48 kHz, which is ample to isolate
        // the 1 kHz tone while leaving room for enough frames in a few seconds.
        private const int FrameLength = 8_192;
        private static readonly TimeSpan CaptureDuration = TimeSpan.FromSeconds(4);
        private static readonly SplToneCriteria Criteria = SplToneCriteria.Default;

        private readonly IAudioSessionFactory audioSessionFactory;
        private readonly AudioSessionRequest request;

        private CancellationTokenSource? cancellation;
        private bool running;
        private bool pendingClose;

        /// <summary>The captured calibration anchor, or null until one succeeds.</summary>
        public SplCalibration? Result { get; private set; }

        public SplCalibrationDialog(
            IAudioSessionFactory audioSessionFactory,
            AudioSessionRequest request,
            SplCalibration? existing = null)
        {
            this.audioSessionFactory = audioSessionFactory ??
                throw new ArgumentNullException(nameof(audioSessionFactory));
            this.request = request ?? throw new ArgumentNullException(nameof(request));

            InitializeComponent();

            foreach (double level in SplCalibration.StandardReferenceLevelsDb)
            {
                comboBoxReference.Items.Add($"{level:0} dB SPL");
            }

            int preselect = existing != null
                ? Math.Max(0, Array.IndexOf(
                    SplCalibration.StandardReferenceLevelsDb, existing.ReferenceLevelDbSpl))
                : 0;
            comboBoxReference.SelectedIndex = preselect;
        }

        private double SelectedReferenceLevelDbSpl
        {
            get
            {
                int index = Math.Clamp(
                    comboBoxReference.SelectedIndex,
                    0,
                    SplCalibration.StandardReferenceLevelsDb.Length - 1);
                return SplCalibration.StandardReferenceLevelsDb[index];
            }
        }

        private async void buttonStart_Click(object? sender, EventArgs e)
        {
            if (running)
            {
                // The button doubles as Stop while a capture is in flight.
                cancellation?.Cancel();
                return;
            }

            double reference = SelectedReferenceLevelDbSpl;
            BeginRunningState();

            using var runCancellation = new CancellationTokenSource();
            cancellation = runCancellation;
            var progress = new Progress<SplCalibrationProgress>(ReportProgress);

            SplCalibrationCaptureResult? capture = null;
            string? error = null;
            bool cancelled = false;
            try
            {
                capture = await new SplCalibrationListener(audioSessionFactory)
                    .CaptureAsync(request, FrameLength, Criteria, CaptureDuration, progress, runCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }
            finally
            {
                cancellation = null;
                running = false;
            }

            if (IsDisposed)
            {
                return;
            }

            EndRunningState();

            if (cancelled)
            {
                SetStatus("Calibration cancelled.", SystemColors.ControlLight);
            }
            else if (error != null)
            {
                SetStatus(
                    $"Could not open the input for calibration:\r\n{error}",
                    Color.LightSalmon);
            }
            else if (capture is { } result)
            {
                ApplyResult(result, reference);
            }

            if (pendingClose)
            {
                Close();
            }
        }

        private void BeginRunningState()
        {
            running = true;
            Result = null;
            buttonSave.Enabled = false;
            buttonCancel.Enabled = false;
            comboBoxReference.Enabled = false;
            buttonStart.Text = "Stop";
            progressBar.Value = 0;
            progressBar.Visible = true;
            SetStatus("Listening for the calibrator tone…", SystemColors.ControlLight);
        }

        private void EndRunningState()
        {
            buttonStart.Text = "Start calibration";
            progressBar.Visible = false;
            comboBoxReference.Enabled = true;
            buttonCancel.Enabled = true;
        }

        private void ReportProgress(SplCalibrationProgress progress)
        {
            if (IsDisposed)
            {
                return;
            }

            int percent = (int)Math.Clamp(
                progress.ElapsedSeconds / CaptureDuration.TotalSeconds * 100.0, 0, 100);
            progressBar.Value = percent;

            string tone = progress.Reading.PeakFrequencyHz > 0
                ? $"{progress.Reading.PeakFrequencyHz:0} Hz at {progress.Reading.LevelDbFs:0.0} dBFS"
                : "—";
            string clip = progress.Clipped ? "   ⚠ CLIPPING — lower the input gain" : "";
            SetStatus(
                $"Listening…   input peak {progress.InputPeakDbFs:0.0} dBFS\r\n" +
                $"Loudest tone: {tone}\r\n" +
                $"Prominence: {progress.Reading.ProminenceDb:0.0} dB{clip}",
                progress.Clipped ? Color.LightSalmon : SystemColors.ControlLight);
        }

        private void ApplyResult(SplCalibrationCaptureResult result, double reference)
        {
            SplCalibrationFailure failure = SplCalibrationListener.Evaluate(result);
            if (failure != SplCalibrationFailure.None)
            {
                Result = null;
                buttonSave.Enabled = false;
                SetStatus(DescribeFailure(failure, result), Color.LightSalmon);
                return;
            }

            Result = new SplCalibration
            {
                ReferenceLevelDbSpl = reference,
                MeasuredLevelDbFs = result.Reading.LevelDbFs,
                ReferenceFrequencyHz = Criteria.TargetFrequencyHz,
                MeasuredFrequencyHz = result.Reading.PeakFrequencyHz,
                CapturedAtUtc = DateTimeOffset.UtcNow,
                Backend = request.Backend,
                SampleRate = request.SampleRate,
                Bits = request.BitsPerSample,
                MicrophoneChannelOffset = request.Routing.MicrophoneChannel,
                InputDeviceNumber = request.Backend == AudioBackend.Wave
                    ? request.WaveInputDeviceNumber
                    : null,
                WasapiCaptureEndpointId = request.WasapiCaptureEndpointId,
                AsioDriverName = request.AsioDriverName
            };

            buttonSave.Enabled = true;
            SetStatus(
                $"Calibration successful.\r\n" +
                $"{result.Reading.PeakFrequencyHz:0} Hz measured at {result.Reading.LevelDbFs:0.0} dBFS.\r\n" +
                $"Offset {Result.OffsetDb:+0.0;-0.0;0.0} dB at {reference:0} dB SPL reference.",
                Color.LightGreen);
        }

        private static string DescribeFailure(
            SplCalibrationFailure failure,
            SplCalibrationCaptureResult result) => failure switch
        {
            SplCalibrationFailure.TooFewFrames =>
                "The capture did not run long enough. Check the input device and try again.",
            SplCalibrationFailure.Clipped =>
                $"The input clipped (peak {result.InputPeakDbFs:0.0} dBFS). Lower the input " +
                "gain and calibrate again.",
            SplCalibrationFailure.OffFrequency =>
                $"The loudest tone was at {result.Reading.PeakFrequencyHz:0} Hz, not " +
                $"{Criteria.TargetFrequencyHz:0} Hz. Check the calibrator is set to 1 kHz and " +
                "seated on the capsule.",
            SplCalibrationFailure.NoClearPeak =>
                $"No clear {Criteria.TargetFrequencyHz:0} Hz tone stood out from the noise " +
                $"(prominence {result.Reading.ProminenceDb:0.0} dB). Seat the calibrator firmly " +
                "and reduce ambient noise.",
            SplCalibrationFailure.Unstable =>
                $"The level was unsteady ({result.LevelStabilityDb:0.0} dB variation). Make sure " +
                "the calibrator is fully seated and held still.",
            SplCalibrationFailure.CaptureOverrun =>
                "The capture dropped frames (processing overload), so the result cannot be " +
                "trusted. Close other work and calibrate again.",
            _ => "Calibration failed."
        };

        private void SetStatus(string text, Color color)
        {
            labelStatus.ForeColor = color;
            labelStatus.Text = text;
        }

        private void SplCalibrationDialog_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // A capture is in flight: cancel it and defer the close until it
            // unwinds, so the background task never touches a disposed form.
            if (running)
            {
                pendingClose = true;
                cancellation?.Cancel();
                e.Cancel = true;
            }
        }
    }
}
