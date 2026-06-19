using System.Diagnostics.Metrics;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.Providers.LinearAlgebra;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze
{
    public enum Mode : int
    {
        None = 0,
        ImpulseResponse,
        FrequencyResponse,
        PhaseResponse,
        GroupDelay,
        CumulativeSpectrumDecay,
        BurstDecay,
        LiveSpectrum,
        Autocorrelation,
        TimeAlignment
    }

    public partial class Form1 : Form
    {
        public Mode CurrentMode { get; private set; }

        private readonly OverlayCollection overlayCollection;
        private readonly ExpSweepMeasurement expSweepMeasurement = new();
        private readonly NoiseMeasurement noiseMeasurement = new();
        private readonly TimeAlignmentMeasurement timeAlignmentMeasurement = new();
        private readonly CalibrationFile calibration = new(
            Path.Combine(AppContext.BaseDirectory, "calibration.txt"));
        private readonly WaterfallGenerateOptions waterfallGenOptions = new()
        {
            WaterfallMode = WaterfallMode.Fourier,
        };
        private readonly WaterfallGenerateOptions burstDecayGenOptions = new()
        {
            WaterfallMode = WaterfallMode.BurstDecay,
            Window = 1024,
            LeftTukeyWindow = 8,
            RightTukeyWindow = 128,
            SmoothingInverseOctaves = 6,
        };

        private readonly FrequencyResponseOptions frequencyResponseOptions = new();
        private readonly FrequencyResponseOptions phaseResponseOptions = new()
        {
            Window = 2048,
            LeftTukeyWindow = 16,
            RightTukeyWindow = 256,
            SmoothingInverseOctaves = 12,
            Offset = 0,
        };
        private readonly FrequencyResponseOptions groupDelayOptions = new()
        {
            Window = 2048,
            LeftTukeyWindow = 16,
            RightTukeyWindow = 256,
            SmoothingInverseOctaves = 12,
            Offset = 0,
        };
        private readonly ImpulseResponseOptions impulseResponseOptions = new();
        private readonly TimeAlignmentOptions timeAlignmentOptions = new();
        private readonly PlotModelFactory plotModelFactory;
        private readonly ModeController modeController;
        private readonly ChromeTitleBarController titleBarController;
        private readonly LiveSpectrumController liveSpectrumController;
        private readonly MeasurementSettingsFile measurementSettings;
        private readonly Panel timeAlignmentPanel;
        private readonly ComboBox timeAlignmentDriverComboBox;
        private readonly ComboBox timeAlignmentMicrophoneComboBox;
        private readonly ComboBox timeAlignmentLoopbackComboBox;
        private readonly ComboBox timeAlignmentOutputComboBox;
        private readonly CheckBox timeAlignmentBandpassCheckBox;
        private readonly NumericUpDown timeAlignmentBandpassCenterNumeric;
        private readonly NumericUpDown timeAlignmentBandpassPassOctavesNumeric;
        private readonly NumericUpDown timeAlignmentBandpassFadeOctavesNumeric;
        private readonly ComboBox timeAlignmentPeakSearchModeComboBox;
        private readonly PlotView timeAlignmentBandpassPlotView;
        private readonly PlotView timeAlignmentEnvelopePlotView;
        private readonly Button timeAlignmentStartButton;
        private readonly Label timeAlignmentStatusLabel;
        private bool hasCurrentImpulseResponse;
        private bool closingPrepared;
        private bool resourcesDisposed;

        public Form1()
        {
            InitializeComponent();
            (
                timeAlignmentPanel,
                timeAlignmentDriverComboBox,
                timeAlignmentMicrophoneComboBox,
                timeAlignmentLoopbackComboBox,
                timeAlignmentOutputComboBox,
                timeAlignmentBandpassCheckBox,
                timeAlignmentBandpassCenterNumeric,
                timeAlignmentBandpassPassOctavesNumeric,
                timeAlignmentBandpassFadeOctavesNumeric,
                timeAlignmentPeakSearchModeComboBox,
                timeAlignmentBandpassPlotView,
                timeAlignmentEnvelopePlotView,
                timeAlignmentStartButton,
                timeAlignmentStatusLabel) = CreateTimeAlignmentPanel();
            measurementSettings = MeasurementSettingsFile.LoadOrDefault();
            titleBarController = new ChromeTitleBarController(
                this,
                plotView1,
                UpdateMaximizedBounds,
                CreateModeTabActions());
            overlayCollection = new OverlayCollection(this, overlays, plotView1, toolTip1);
            plotModelFactory = new PlotModelFactory(
                expSweepMeasurement,
                noiseMeasurement,
                calibration,
                frequencyResponseOptions,
                phaseResponseOptions,
                groupDelayOptions,
                impulseResponseOptions,
                waterfallGenOptions,
                burstDecayGenOptions);
            liveSpectrumController = new LiveSpectrumController(
                this,
                noiseMeasurement,
                plotView1,
                plotModelFactory,
                overlays,
                overlayCollection,
                () => CurrentMode,
                () => SelectModeAsync(ModeTab.LiveSpectrum),
                UpdateOverlayAvailability,
                UpdateDrawButtonText,
                UpdateClearButtonState);
            modeController = new ModeController(
                ChangeModeAsync,
                SetActiveModeTab,
                overlayCollection.HideAll,
                DrawSelectedMode,
                CanDrawCurrentMeasurement,
                UpdateDrawButtonText);

            measurementSettings.ApplyTo(
                expSweepMeasurement,
                frequencyResponseOptions,
                phaseResponseOptions,
                groupDelayOptions,
                impulseResponseOptions,
                waterfallGenOptions,
                burstDecayGenOptions,
                timeAlignmentOptions);
            ApplyTimeAlignmentOptionsToControls();
            UpdateTimeAlignmentBandpassPreview();
            liveSpectrumController.ConfigureFrom(expSweepMeasurement);
            SetButtonFrozen(buttonSave, true);
            SetButtonFrozen(buttonLoad, false);
            SetButtonFrozen(buttonDraw, true);
            timeAlignmentMeasurement.Completed += TimeAlignmentMeasurementCompleted;

            expSweepMeasurement.Completed += (bool success) =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }
                BeginInvoke((MethodInvoker)delegate
                {
                    if (success)
                    {
                        buttonRecord.Text = "Ready";
                        //buttonRecord.BackColor = Color.FromArgb(192, 255, 192);
                        hasCurrentImpulseResponse = true;
                        SetButtonFrozen(buttonSave, false);
                        SetButtonFrozen(buttonLoad, false);
                    }
                    else
                    {
                        buttonRecord.Text = expSweepMeasurement.LastError == null ? "Aborted" : "Error";
                        //buttonRecord.BackColor = Color.FromArgb(255, 192, 192);
                        hasCurrentImpulseResponse = false;
                        SetButtonFrozen(buttonSave, true);
                        SetButtonFrozen(buttonLoad, false);
                    }
                    UpdateDrawButtonText();

                    if (success && CurrentMode != Mode.LiveSpectrum)
                    {
                        DrawSelectedMode(true);
                    }

                    UpdateClearButtonState();
                });
            };

            FormClosing += Form1_FormClosing;
            _ = SelectModeAsync(ModeTab.Frequency);
        }

        private void TimeAlignmentMeasurementCompleted(bool success)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke((MethodInvoker)delegate
            {
                timeAlignmentStartButton.Text = "Start";
                buttonRecord.Text = success ? "Ready" : timeAlignmentMeasurement.LastError == null ? "Aborted" : "Error";

                if (modeController.ActiveTab == ModeTab.TimeAlignment)
                {
                    UpdateTimeAlignmentChannels();
                }

                if (success)
                {
                    double delaySamples = timeAlignmentMeasurement.PeakSample;
                    double delayMilliseconds = timeAlignmentMeasurement.DelayMilliseconds;
                    double delayMeters = Math.Abs(delayMilliseconds) * SpeedOfSoundAt20C / 1000.0;
                    timeAlignmentStatusLabel.Text =
                        "Measured delay:\r\n" +
                        $"{delayMilliseconds:0.000} ms\r\n" +
                        $"{delayMeters:0.000} m (at 20 °C)\r\n" +
                        $"{delaySamples:0.0} samples\r\n\r\n" +
                        "Signal Quality:\r\n" +
                        $"{FormatTimeAlignmentConfidence(timeAlignmentMeasurement.ConfidenceDecibels)} " +
                        $"({timeAlignmentMeasurement.ConfidenceDecibels:0.0} dB)\r\n" +
                        FormatTimeAlignmentPeakDetection() +
                        "\r\n" +
                        FormatTimeAlignmentLevel(
                            "Mic",
                            timeAlignmentMeasurement.MicrophonePeakDbFs,
                            timeAlignmentMeasurement.MicrophoneRmsDbFs,
                            timeAlignmentMeasurement.MicrophoneClipped,
                            fullScaleIsNormal: false) +
                        "\r\n" +
                        FormatTimeAlignmentLevel(
                            "Loopback",
                            timeAlignmentMeasurement.LoopbackPeakDbFs,
                            timeAlignmentMeasurement.LoopbackRmsDbFs,
                            timeAlignmentMeasurement.LoopbackClipped,
                            fullScaleIsNormal: true);
                    UpdateTimeAlignmentEnvelopePreview();
                }
                else if (timeAlignmentMeasurement.LastError != null)
                {
                    timeAlignmentStatusLabel.Text = timeAlignmentMeasurement.LastError.Message;
                    ClearTimeAlignmentEnvelopePreview();
                }
                else
                {
                    timeAlignmentStatusLabel.Text = "Time-alignment measurement aborted.";
                    ClearTimeAlignmentEnvelopePreview();
                }
            });
        }

        private static string FormatTimeAlignmentLevel(
            string label,
            double peakDbFs,
            double rmsDbFs,
            bool clipped,
            bool fullScaleIsNormal)
        {
            string clipText = clipped
                ? fullScaleIsNormal ? " FULL SCALE" : " CLIP"
                : string.Empty;
            return $"{label}: peak {peakDbFs:0.0} dBFS, RMS {rmsDbFs:0.0} dBFS{clipText}";
        }

        private static string FormatTimeAlignmentConfidence(double confidenceDecibels)
        {
            if (confidenceDecibels >= 30)
            {
                return "Excellent";
            }
            if (confidenceDecibels >= 20)
            {
                return "Good";
            }
            if (confidenceDecibels >= 10)
            {
                return "Fair";
            }

            return "Poor";
        }

        private string FormatTimeAlignmentPeakDetection()
        {
            string mode = timeAlignmentOptions.PeakSearchMode == TimeAlignmentPeakSearchMode.FirstArrival
                ? "First arrival"
                : "Strongest peak";
            return timeAlignmentMeasurement.PeakSearchFallbackUsed
                ? "Peak: First arrival fallback to strongest"
                : $"Peak: {mode}";
        }

        private (
            Panel Panel,
            ComboBox Driver,
            ComboBox Microphone,
            ComboBox Loopback,
            ComboBox Output,
            CheckBox BandpassEnabled,
            NumericUpDown BandpassCenter,
            NumericUpDown BandpassPassOctaves,
            NumericUpDown BandpassFadeOctaves,
            ComboBox PeakSearchMode,
            PlotView BandpassPreview,
            PlotView EnvelopePreview,
            Button Start,
            Label Status) CreateTimeAlignmentPanel()
        {
            var panel = new Panel
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(40, 44, 54),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(24, 24),
                Size = new Size(980, 710),
                Visible = false
            };

            var title = new Label
            {
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(18, 18),
                Text = "Time Alignment"
            };
            var description = new Label
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(190, 195, 205),
                Location = new Point(18, 44),
                Text = "ASIO loopback measurement: microphone input + loopback reference input."
            };
            var help = new Label
            {
                AutoSize = false,
                ForeColor = Color.FromArgb(205, 210, 220),
                Location = new Point(560, 78),
                Size = new Size(380, 164),
                Text =
                    "What it is for\r\n" +
                    "Measures acoustic delay relative to the audio interface reference path.\r\n\r\n" +
                    "How it works\r\n" +
                    "Resonalyze plays the same mono sweep, records the microphone and ASIO loopback at the same time, then compares the two impulse-response peaks.\r\n\r\n" +
                    "Why ASIO + loopback\r\n" +
                    "Both channels must be captured by the same low-latency driver clock. Windows Wave devices do not provide a reliable hardware reference input."
            };

            ComboBox driver = CreateTimeAlignmentComboBox(180, 78);
            ComboBox microphone = CreateTimeAlignmentComboBox(180, 112);
            ComboBox loopback = CreateTimeAlignmentComboBox(180, 146);
            ComboBox output = CreateTimeAlignmentComboBox(180, 180);
            CheckBox bandpassEnabled = new()
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(210, 214, 222),
                Location = new Point(18, 252),
                Text = "Use bandpass window"
            };
            NumericUpDown bandpassCenter =
                CreateTimeAlignmentNumericUpDown(180, 282, 20, 20_000, 1000, 10);
            NumericUpDown bandpassPassOctaves =
                CreateTimeAlignmentNumericUpDown(180, 316, 0, 8, 1, 0.1M);
            NumericUpDown bandpassFadeOctaves =
                CreateTimeAlignmentNumericUpDown(180, 350, 0, 8, 0.5M, 0.1M);
            PlotView bandpassPreview = new()
            {
                BackColor = Color.FromArgb(32, 36, 46),
                Location = new Point(18, 388),
                Size = new Size(520, 145),
                Visible = false
            };
            ComboBox peakSearchMode = CreateTimeAlignmentComboBox(180, 218);
            peakSearchMode.Items.AddRange(["First arrival", "Strongest peak"]);
            PlotView envelopePreview = new()
            {
                BackColor = Color.FromArgb(32, 36, 46),
                Location = new Point(560, 530),
                Size = new Size(400, 155),
                Visible = false
            };
            Button start = new()
            {
                BackColor = Color.FromArgb(50, 55, 80),
                FlatStyle = FlatStyle.Popup,
                ForeColor = Color.White,
                Location = new Point(560, 248),
                Size = new Size(400, 28),
                Text = "Start",
                UseVisualStyleBackColor = false
            };
            Label status = new()
            {
                AutoSize = false,
                ForeColor = Color.FromArgb(190, 195, 205),
                Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
                Location = new Point(560, 290),
                Size = new Size(400, 235),
                Text = "Select an ASIO driver with loopback input channels."
            };

            panel.Controls.AddRange(
            [
                title,
                description,
                help,
                CreateTimeAlignmentLabel("ASIO driver", 82),
                driver,
                CreateTimeAlignmentLabel("Microphone input", 116),
                microphone,
                CreateTimeAlignmentLabel("Loopback input", 150),
                loopback,
                CreateTimeAlignmentLabel("Output pair", 184),
                output,
                CreateTimeAlignmentLabel("Peak detection", 222),
                peakSearchMode,
                bandpassEnabled,
                CreateTimeAlignmentLabel("Center frequency, Hz", 286),
                bandpassCenter,
                CreateTimeAlignmentLabel("Pass width, oct", 320),
                bandpassPassOctaves,
                CreateTimeAlignmentLabel("Fade width, oct", 354),
                bandpassFadeOctaves,
                bandpassPreview,
                envelopePreview,
                start,
                status
            ]);
            Controls.Add(panel);
            panel.BringToFront();

            driver.SelectedIndexChanged += (_, _) =>
            {
                UpdateTimeAlignmentChannels();
                SaveTimeAlignmentSettings();
            };
            microphone.SelectedIndexChanged += (_, _) => SaveTimeAlignmentSettings();
            loopback.SelectedIndexChanged += (_, _) => SaveTimeAlignmentSettings();
            output.SelectedIndexChanged += (_, _) => SaveTimeAlignmentSettings();
            bandpassEnabled.CheckedChanged += (_, _) =>
            {
                UpdateTimeAlignmentOptionsFromControls();
                UpdateTimeAlignmentBandpassPreview();
                timeAlignmentBandpassCenterNumeric.Enabled =
                    bandpassEnabled.Checked && !timeAlignmentMeasurement.InProgress;
                timeAlignmentBandpassPassOctavesNumeric.Enabled =
                    bandpassEnabled.Checked && !timeAlignmentMeasurement.InProgress;
                timeAlignmentBandpassFadeOctavesNumeric.Enabled =
                    bandpassEnabled.Checked && !timeAlignmentMeasurement.InProgress;
                SaveMeasurementSettings();
            };
            bandpassCenter.ValueChanged += (_, _) =>
            {
                UpdateTimeAlignmentOptionsFromControls();
                UpdateTimeAlignmentBandpassPreview();
                SaveMeasurementSettings();
            };
            bandpassPassOctaves.ValueChanged += (_, _) =>
            {
                UpdateTimeAlignmentOptionsFromControls();
                UpdateTimeAlignmentBandpassPreview();
                SaveMeasurementSettings();
            };
            bandpassFadeOctaves.ValueChanged += (_, _) =>
            {
                UpdateTimeAlignmentOptionsFromControls();
                UpdateTimeAlignmentBandpassPreview();
                SaveMeasurementSettings();
            };
            peakSearchMode.SelectedIndexChanged += (_, _) =>
            {
                UpdateTimeAlignmentOptionsFromControls();
                SaveMeasurementSettings();
            };
            start.Click += async (_, _) => await ToggleTimeAlignmentAsync();

            return (
                panel,
                driver,
                microphone,
                loopback,
                output,
                bandpassEnabled,
                bandpassCenter,
                bandpassPassOctaves,
                bandpassFadeOctaves,
                peakSearchMode,
                bandpassPreview,
                envelopePreview,
                start,
                status);
        }

        private static Label CreateTimeAlignmentLabel(string text, int y) =>
            new()
            {
                AutoSize = true,
                ForeColor = Color.FromArgb(210, 214, 222),
                Location = new Point(18, y),
                Text = text
            };

        private const double SpeedOfSoundAt20C = 343.2;

        private ComboBox CreateTimeAlignmentComboBox(int x, int y) =>
            new()
            {
                BackColor = Color.FromArgb(55, 60, 72),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                FormattingEnabled = true,
                Location = new Point(x, y - 4),
                Size = new Size(360, 23)
            };

        private static NumericUpDown CreateTimeAlignmentNumericUpDown(
            int x,
            int y,
            decimal minimum,
            decimal maximum,
            decimal value,
            decimal increment) =>
            new()
            {
                BackColor = Color.FromArgb(55, 60, 72),
                DecimalPlaces = increment < 1 ? 1 : 0,
                ForeColor = Color.White,
                Increment = increment,
                Location = new Point(x, y - 4),
                Maximum = maximum,
                Minimum = minimum,
                Size = new Size(120, 23),
                Value = value
            };

        private void ApplyTimeAlignmentOptionsToControls()
        {
            timeAlignmentBandpassCheckBox.Checked = timeAlignmentOptions.UseBandpassWindow;
            timeAlignmentBandpassCenterNumeric.Value =
                ClampDecimal(timeAlignmentOptions.BandpassCenterHz, timeAlignmentBandpassCenterNumeric);
            timeAlignmentBandpassPassOctavesNumeric.Value =
                ClampDecimal(timeAlignmentOptions.BandpassPassOctaves, timeAlignmentBandpassPassOctavesNumeric);
            timeAlignmentBandpassFadeOctavesNumeric.Value =
                ClampDecimal(timeAlignmentOptions.BandpassFadeOctaves, timeAlignmentBandpassFadeOctavesNumeric);
            timeAlignmentPeakSearchModeComboBox.SelectedIndex =
                timeAlignmentOptions.PeakSearchMode == TimeAlignmentPeakSearchMode.StrongestPeak
                    ? 1
                    : 0;
        }

        private void UpdateTimeAlignmentOptionsFromControls()
        {
            timeAlignmentOptions.UseBandpassWindow = timeAlignmentBandpassCheckBox.Checked;
            timeAlignmentOptions.BandpassCenterHz = (double)timeAlignmentBandpassCenterNumeric.Value;
            timeAlignmentOptions.BandpassPassOctaves = (double)timeAlignmentBandpassPassOctavesNumeric.Value;
            timeAlignmentOptions.BandpassFadeOctaves = (double)timeAlignmentBandpassFadeOctavesNumeric.Value;
            timeAlignmentOptions.PeakSearchMode =
                timeAlignmentPeakSearchModeComboBox.SelectedIndex == 1
                    ? TimeAlignmentPeakSearchMode.StrongestPeak
                    : TimeAlignmentPeakSearchMode.FirstArrival;

            if (timeAlignmentDriverComboBox.SelectedItem is AsioDeviceInfo driver)
            {
                timeAlignmentOptions.AsioDriverName = driver.DriverName;
            }
            if (timeAlignmentMicrophoneComboBox.SelectedItem is AsioChannelInfo microphone)
            {
                timeAlignmentOptions.MicrophoneInputChannelOffset = microphone.Offset;
            }
            if (timeAlignmentLoopbackComboBox.SelectedItem is AsioChannelInfo loopback)
            {
                timeAlignmentOptions.LoopbackInputChannelOffset = loopback.Offset;
            }
            if (timeAlignmentOutputComboBox.SelectedItem is AsioChannelInfo output)
            {
                timeAlignmentOptions.AsioOutputChannelOffset = output.Offset;
            }
        }

        private void UpdateTimeAlignmentBandpassPreview()
        {
            timeAlignmentBandpassPlotView.Visible = timeAlignmentBandpassCheckBox.Checked;
            if (!timeAlignmentBandpassCheckBox.Checked)
            {
                timeAlignmentBandpassPlotView.Model = null;
                return;
            }

            var model = new PlotModel
            {
                Background = OxyColor.FromRgb(32, 36, 46),
                PlotAreaBackground = OxyColor.FromRgb(32, 36, 46),
                TextColor = OxyColors.White,
                Title = "Bandpass Window",
                TitleColor = OxyColors.White,
                TitleFontSize = 10
            };
            model.Axes.Add(new LogarithmicAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 20,
                Maximum = Math.Min(20_000, expSweepMeasurement.SampleRate * 0.5),
                MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
                MinorGridlineStyle = LineStyle.Dot,
                TextColor = OxyColors.White,
                TicklineColor = OxyColors.White
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = -80,
                Maximum = 3,
                MajorStep = 20,
                MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
                MinorGridlineStyle = LineStyle.Dot,
                TextColor = OxyColors.White,
                TicklineColor = OxyColors.White,
                Title = "dB"
            });

            var series = new LineSeries
            {
                Color = OxyColor.FromRgb(255, 210, 80),
                StrokeThickness = 2
            };
            (double f1, double f2, double f3, double f4) = BandpassWindow.BandAround(
                (double)timeAlignmentBandpassCenterNumeric.Value,
                (double)timeAlignmentBandpassPassOctavesNumeric.Value,
                (double)timeAlignmentBandpassFadeOctavesNumeric.Value);
            const int pointCount = 240;
            double minLog = Math.Log10(20);
            double maxLog = Math.Log10(Math.Min(20_000, expSweepMeasurement.SampleRate * 0.5));
            for (int i = 0; i < pointCount; i++)
            {
                double t = i / (double)(pointCount - 1);
                double frequency = Math.Pow(10.0, minLog + (maxLog - minLog) * t);
                double weight = BandpassWindow.Weight(frequency, f1, f2, f3, f4);
                double decibels = weight > 0
                    ? DataHelper.AmplitudeToDecibels(weight)
                    : -80;
                series.Points.Add(new DataPoint(frequency, Math.Max(-80, decibels)));
            }

            model.Series.Add(series);
            timeAlignmentBandpassPlotView.Model = model;
        }

        private void UpdateTimeAlignmentEnvelopePreview()
        {
            double[]? envelope = timeAlignmentMeasurement.EnvelopeSamples;
            if (envelope == null ||
                envelope.Length == 0 ||
                timeAlignmentMeasurement.EnvelopePeak <= 0 ||
                timeAlignmentMeasurement.SampleRate <= 0)
            {
                ClearTimeAlignmentEnvelopePreview();
                return;
            }

            var model = new PlotModel
            {
                Background = OxyColor.FromRgb(32, 36, 46),
                PlotAreaBackground = OxyColor.FromRgb(32, 36, 46),
                TextColor = OxyColors.White,
                Title = "Envelope Around Peak",
                TitleColor = OxyColors.White,
                TitleFontSize = 10
            };
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = -50,
                Maximum = 50,
                MajorStep = 25,
                MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
                MinorGridlineStyle = LineStyle.Dot,
                TextColor = OxyColors.White,
                TicklineColor = OxyColors.White,
                Title = "ms from peak"
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = -80,
                Maximum = 3,
                MajorStep = 20,
                MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
                MinorGridlineStyle = LineStyle.Dot,
                TextColor = OxyColors.White,
                TicklineColor = OxyColors.White,
                Title = "dB"
            });

            var series = new LineSeries
            {
                Color = OxyColor.FromRgb(255, 210, 80),
                StrokeThickness = 2
            };
            int radius = Math.Min(
                envelope.Length / 2,
                Math.Max(1, (int)Math.Round(timeAlignmentMeasurement.SampleRate * 0.05)));
            int step = Math.Max(1, radius * 2 / 600);
            for (int offset = -radius; offset <= radius; offset += step)
            {
                int index = WrapIndex(timeAlignmentMeasurement.EnvelopePeakIndex + offset, envelope.Length);
                double milliseconds = offset * 1000.0 / timeAlignmentMeasurement.SampleRate;
                double relativeAmplitude = envelope[index] / timeAlignmentMeasurement.EnvelopePeak;
                double decibels = DataHelper.AmplitudeToDecibels(relativeAmplitude);
                series.Points.Add(new DataPoint(milliseconds, Math.Max(-80, decibels)));
            }

            model.Series.Add(series);
            model.Annotations.Add(new LineAnnotation
            {
                Color = OxyColor.FromRgb(255, 96, 96),
                LineStyle = LineStyle.Dash,
                StrokeThickness = 1,
                Type = LineAnnotationType.Vertical,
                X = 0
            });
            int strongestOffset = NormalizeWrappedOffset(
                timeAlignmentMeasurement.StrongestEnvelopePeakIndex -
                timeAlignmentMeasurement.EnvelopePeakIndex,
                envelope.Length);
            double strongestMilliseconds =
                strongestOffset * 1000.0 / timeAlignmentMeasurement.SampleRate;
            if (Math.Abs(strongestMilliseconds) <= 50 &&
                Math.Abs(strongestOffset) > 1)
            {
                model.Annotations.Add(new LineAnnotation
                {
                    Color = OxyColor.FromRgb(140, 170, 255),
                    LineStyle = LineStyle.Dot,
                    StrokeThickness = 1,
                    Type = LineAnnotationType.Vertical,
                    X = strongestMilliseconds
                });
            }
            timeAlignmentEnvelopePlotView.Model = model;
            timeAlignmentEnvelopePlotView.Visible = true;
        }

        private void ClearTimeAlignmentEnvelopePreview()
        {
            timeAlignmentEnvelopePlotView.Model = null;
            timeAlignmentEnvelopePlotView.Visible = false;
        }

        private static int WrapIndex(int index, int length)
        {
            int wrapped = index % length;
            return wrapped < 0 ? wrapped + length : wrapped;
        }

        private static int NormalizeWrappedOffset(int offset, int length)
        {
            int halfLength = length / 2;
            if (offset > halfLength)
            {
                return offset - length;
            }
            if (offset < -halfLength)
            {
                return offset + length;
            }

            return offset;
        }

        private void SaveTimeAlignmentSettings()
        {
            if (measurementSettings == null)
            {
                return;
            }

            UpdateTimeAlignmentOptionsFromControls();
            SaveMeasurementSettings();
        }

        private static decimal ClampDecimal(double value, NumericUpDown numeric)
        {
            decimal decimalValue = (decimal)value;
            return Math.Min(numeric.Maximum, Math.Max(numeric.Minimum, decimalValue));
        }

        public async Task ChangeModeAsync(Mode mode)
        {
            if (expSweepMeasurement.InProgress)
            {
                await expSweepMeasurement.AbortAsync();
            }
            if (timeAlignmentMeasurement.InProgress)
            {
                await timeAlignmentMeasurement.AbortAsync();
            }

            await liveSpectrumController.AbortAsync();

            CurrentMode = mode;
            plotView1.Model = null;
            UpdateClearButtonState();

            if (OverlayCollection.SupportsMode(mode))
            {
                overlayCollection.Prepare(mode);
            }

            UpdateOverlayAvailability();
        }

        private async void buttonRecord_Click(object sender, EventArgs e)
        {
            if (CurrentMode == Mode.TimeAlignment)
            {
                await ToggleTimeAlignmentAsync();
                return;
            }

            if (liveSpectrumController.InProgress)
            {
                await liveSpectrumController.AbortAsync();
            }

            if (expSweepMeasurement.InProgress)
            {
                await expSweepMeasurement.AbortAsync();
            }
            else
            {
                buttonRecord.Text = "Running...";
                hasCurrentImpulseResponse = false;
                _ = expSweepMeasurement.RunAsync();
                //buttonRecord.BackColor = Color.FromArgb(192, 255, 255);
                SetButtonFrozen(buttonSave, true);
                SetButtonFrozen(buttonLoad, true);
                UpdateDrawButtonText();
            }
        }

        private void DrawSelectedMode(bool includeCurves)
        {
            switch (modeController.ActiveTab)
            {
                case ModeTab.Impulse:
                    DrawImpulseResponse(includeCurves);
                    break;
                case ModeTab.Frequency:
                    DrawFrequencyResponse(includeCurves);
                    break;
                case ModeTab.Phase:
                    DrawPhaseResponse(includeCurves);
                    break;
                case ModeTab.GroupDelay:
                    DrawGroupDelay(includeCurves);
                    break;
                case ModeTab.Waterfall:
                    DrawWaterfall(includeCurves);
                    break;
                case ModeTab.Burst:
                    DrawBurstDecay(includeCurves);
                    break;
                case ModeTab.LiveSpectrum:
                    plotView1.Model = plotModelFactory.CreateLiveSpectrum();
                    break;
                case ModeTab.Autocorrelation:
                    DrawAutocorrelation(includeCurves);
                    break;
            }

            UpdateClearButtonState();
        }

        private void DrawFrequencyResponse(bool includeCurves)
        {
            plotView1.Model =
                plotModelFactory.CreateFrequencyResponse(includeCurves);

            if (includeCurves)
            {
                overlayCollection.Show(CurrentMode);
            }
        }

        private void DrawPhaseResponse(bool includeCurves)
        {
            plotView1.Model = plotModelFactory.CreatePhaseResponse(includeCurves);

            if (includeCurves)
            {
                overlayCollection.Show(CurrentMode);
            }
        }

        private void DrawWaterfall(bool includeCurves)
        {
            plotView1.Model = plotModelFactory.CreateWaterfall(includeCurves);
        }

        private void DrawGroupDelay(bool includeCurves)
        {
            plotView1.Model = plotModelFactory.CreateGroupDelay(includeCurves);

            if (includeCurves)
            {
                overlayCollection.Show(CurrentMode);
            }
        }

        private void DrawBurstDecay(bool includeCurves)
        {
            plotView1.Model = plotModelFactory.CreateBurstDecay(includeCurves);
        }

        private void DrawImpulseResponse(bool includeCurves)
        {
            plotView1.Model =
                plotModelFactory.CreateImpulseResponse(includeCurves);

            if (includeCurves)
            {
                overlayCollection.Show(CurrentMode);
            }
        }

        private void DrawAutocorrelation(bool includeCurves)
        {
            plotView1.Model =
                plotModelFactory.CreateAutocorrelation(includeCurves);

            if (includeCurves)
            {
                overlayCollection.Show(CurrentMode);
            }
        }

        private void buttonRecordOpt_Click(object sender, EventArgs e)
        {
            using var opt = new MeasurementOptions();
            opt.Init(expSweepMeasurement);

            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                try
                {
                    opt.SetOptions(expSweepMeasurement);
                    liveSpectrumController.ConfigureFrom(expSweepMeasurement);
                    SaveMeasurementSettings();
                }
                catch (InvalidOperationException exception)
                {
                    MessageBox.Show(
                        this,
                        exception.Message,
                        "Measurement Options",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private void buttonWaterfallOpt_Click(object sender, EventArgs e)
        {
            using var opt = new WaterfallOptions();
            opt.Init(expSweepMeasurement, waterfallGenOptions);

            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(waterfallGenOptions);
                SaveMeasurementSettings();
            }
        }

        private void buttonFROpt_Click(object sender, EventArgs e)
        {
            using var opt = new FROptions();
            opt.Init(expSweepMeasurement, frequencyResponseOptions);
            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(frequencyResponseOptions);
                SaveMeasurementSettings();
            }
        }

        private void buttonBurstDecayOpt_Click(object sender, EventArgs e)
        {
            using var opt = new BDOpt();
            opt.Init(expSweepMeasurement, burstDecayGenOptions);

            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(burstDecayGenOptions);
                SaveMeasurementSettings();
            }
        }

        private void buttonGDOpt_Click(object sender, EventArgs e)
        {
            using var opt = new GDOpt();
            opt.Init(expSweepMeasurement, groupDelayOptions);
            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(groupDelayOptions);
                SaveMeasurementSettings();
            }
        }

        private void buttonPROpt_Click(object sender, EventArgs e)
        {
            using var opt = new PROpt();
            opt.Init(expSweepMeasurement, phaseResponseOptions);
            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(phaseResponseOptions);
                SaveMeasurementSettings();
            }
        }

        private void buttonImpOpt_Click(object sender, EventArgs e)
        {
            using var opt = new IROpt();
            opt.Init(expSweepMeasurement, impulseResponseOptions);
            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(impulseResponseOptions);
                SaveMeasurementSettings();
            }
        }

        private void SaveMeasurementSettings()
        {
            measurementSettings.CaptureFrom(
                expSweepMeasurement,
                frequencyResponseOptions,
                phaseResponseOptions,
                groupDelayOptions,
                impulseResponseOptions,
                waterfallGenOptions,
                burstDecayGenOptions,
                timeAlignmentOptions);
            measurementSettings.Save();
        }

        private DialogResult ShowSettingsDialog(Form dialog)
        {
            dialog.StartPosition = FormStartPosition.CenterParent;
            return dialog.ShowDialog(this);
        }

        private async void buttonClear_Click(object sender, EventArgs e)
        {
            if (CurrentMode == Mode.LiveSpectrum &&
                liveSpectrumController.InProgress)
            {
                await liveSpectrumController.AbortAsync();
            }

            overlayCollection.HideAll();

            PlotModel? model = plotView1.Model;
            if (model == null)
            {
                return;
            }

            model.Series.Clear();
            model.InvalidatePlot(true);
            plotView1.Refresh();
            UpdateClearButtonState();
            UpdateOverlayAvailability();
        }

        private void UpdateOverlayAvailability()
        {
            bool available = OverlayCollection.SupportsMode(CurrentMode);
            if (CurrentMode == Mode.LiveSpectrum)
            {
                available &= !liveSpectrumController.InProgress &&
                    !liveSpectrumController.TimerEnabled;
            }

            overlays.Enabled = available;
            if (!available)
            {
                overlayCollection.HideAll();
            }
        }

        private Task SelectModeAsync(ModeTab tab) => modeController.SelectAsync(tab);

        private Dictionary<ModeTab, Action> CreateModeTabActions() =>
            new()
            {
                [ModeTab.Impulse] = () => _ = SelectModeAsync(ModeTab.Impulse),
                [ModeTab.Frequency] = () => _ = SelectModeAsync(ModeTab.Frequency),
                [ModeTab.Phase] = () => _ = SelectModeAsync(ModeTab.Phase),
                [ModeTab.GroupDelay] = () => _ = SelectModeAsync(ModeTab.GroupDelay),
                [ModeTab.Waterfall] = () => _ = SelectModeAsync(ModeTab.Waterfall),
                [ModeTab.Burst] = () => _ = SelectModeAsync(ModeTab.Burst),
                [ModeTab.LiveSpectrum] = () => _ = SelectModeAsync(ModeTab.LiveSpectrum),
                [ModeTab.Autocorrelation] = () => _ = SelectModeAsync(ModeTab.Autocorrelation),
                [ModeTab.TimeAlignment] = () => _ = SelectModeAsync(ModeTab.TimeAlignment)
            };

        private void UpdateMaximizedBounds()
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
        }

        private void SetActiveModeTab(ModeTab activeTab)
        {
            titleBarController.SetActiveModeTab(activeTab);
            UpdateCurrentModeSettingsButton();
            UpdateDrawButtonText();
            PlotViewVisible();
            OverlayVisible();
            TimeAlignmentPanelVisible();
        }

        private void UpdateDrawButtonText()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            buttonDraw.Text = modeController.ActiveTab == ModeTab.LiveSpectrum
                ? liveSpectrumController.InProgress ? "Stop Live" : "Start Live"
                : "Restore Curves";
            SetButtonFrozen(buttonDraw, ShouldFreezeDrawButton());
        }

        private void PlotViewVisible()
        {
            plotView1.Visible = modeController.ActiveTab != ModeTab.TimeAlignment;
        }

        private void OverlayVisible()
        {
            overlays.Visible = 
                modeController.ActiveTab != ModeTab.TimeAlignment &&
                modeController.ActiveTab != ModeTab.Burst &&
                modeController.ActiveTab != ModeTab.Waterfall;
        }

        private void TimeAlignmentPanelVisible()
        {
            bool visible = modeController.ActiveTab == ModeTab.TimeAlignment;
            timeAlignmentPanel.Visible = visible;
            if (visible)
            {
                RefreshTimeAlignmentDrivers();
            }
        }

        private void RefreshTimeAlignmentDrivers()
        {
            string? preferredDriver =
                (timeAlignmentDriverComboBox.SelectedItem as AsioDeviceInfo)?.DriverName ??
                timeAlignmentOptions.AsioDriverName ??
                expSweepMeasurement.AsioDriverName;
            IReadOnlyList<AsioDeviceInfo> drivers = AsioDeviceCatalog.GetDrivers();
            timeAlignmentDriverComboBox.Items.Clear();
            timeAlignmentDriverComboBox.Items.AddRange(drivers.Cast<object>().ToArray());
            timeAlignmentDriverComboBox.SelectedIndex =
                AsioDeviceCatalog.FindDriverIndex(drivers, preferredDriver);

            if (drivers.Count == 0)
            {
                timeAlignmentStatusLabel.Text = "ASIO is not available on this system.";
                SetTimeAlignmentControlsEnabled(false);
            }
        }

        private void UpdateTimeAlignmentChannels()
        {
            if (timeAlignmentDriverComboBox.SelectedItem is not AsioDeviceInfo driver)
            {
                timeAlignmentStatusLabel.Text = "Select an ASIO driver.";
                SetTimeAlignmentControlsEnabled(false);
                return;
            }

            AsioDriverInfo info = AsioDeviceCatalog.GetDriverInfo(
                driver.DriverName,
                expSweepMeasurement.SampleRate);
            AsioChannelInfo[] loopbackInputs = info.InputChannels
                .Where(AsioDeviceCatalog.IsLoopbackChannel)
                .ToArray();
            AsioChannelInfo[] microphoneInputs = info.InputChannels
                .Where(channel => !AsioDeviceCatalog.IsLoopbackChannel(channel))
                .ToArray();
            if (microphoneInputs.Length == 0)
            {
                microphoneInputs = info.InputChannels.ToArray();
            }

            FillChannelComboBox(
                timeAlignmentMicrophoneComboBox,
                microphoneInputs,
                timeAlignmentOptions.MicrophoneInputChannelOffset);
            FillChannelComboBox(
                timeAlignmentLoopbackComboBox,
                loopbackInputs,
                timeAlignmentOptions.LoopbackInputChannelOffset);
            FillChannelComboBox(
                timeAlignmentOutputComboBox,
                info.OutputChannels,
                timeAlignmentOptions.AsioOutputChannelOffset);

            bool canRun =
                string.IsNullOrWhiteSpace(info.ErrorMessage) &&
                info.SupportsSampleRate &&
                microphoneInputs.Length > 0 &&
                loopbackInputs.Length > 0 &&
                info.OutputChannels.Count > 0;
            SetTimeAlignmentControlsEnabled(canRun);
            timeAlignmentStatusLabel.Text = GetTimeAlignmentStatus(info, loopbackInputs.Length, canRun);
        }

        private static void FillChannelComboBox(
            ComboBox comboBox,
            IReadOnlyList<AsioChannelInfo> channels,
            int preferredOffset)
        {
            comboBox.Items.Clear();
            comboBox.Items.AddRange(channels.Cast<object>().ToArray());
            comboBox.SelectedIndex = AsioDeviceCatalog.FindChannelIndex(
                channels,
                preferredOffset);
        }

        private string GetTimeAlignmentStatus(
            AsioDriverInfo info,
            int loopbackChannelCount,
            bool canRun)
        {
            if (!string.IsNullOrWhiteSpace(info.ErrorMessage))
            {
                return info.ErrorMessage;
            }
            if (!info.SupportsSampleRate)
            {
                return $"{info.DriverName} does not support {expSweepMeasurement.SampleRate} Hz.";
            }
            if (loopbackChannelCount == 0)
            {
                return "This ASIO driver does not expose loopback input channels.";
            }
            return canRun
                ? "Ready to measure microphone delay against ASIO loopback."
                : "Select microphone, loopback, and output channels.";
        }

        private void SetTimeAlignmentControlsEnabled(bool enabled)
        {
            timeAlignmentDriverComboBox.Enabled = !timeAlignmentMeasurement.InProgress;
            timeAlignmentMicrophoneComboBox.Enabled = enabled && !timeAlignmentMeasurement.InProgress;
            timeAlignmentLoopbackComboBox.Enabled = enabled && !timeAlignmentMeasurement.InProgress;
            timeAlignmentOutputComboBox.Enabled = enabled && !timeAlignmentMeasurement.InProgress;
            timeAlignmentBandpassCheckBox.Enabled = !timeAlignmentMeasurement.InProgress;
            timeAlignmentBandpassCenterNumeric.Enabled =
                timeAlignmentBandpassCheckBox.Checked && !timeAlignmentMeasurement.InProgress;
            timeAlignmentBandpassPassOctavesNumeric.Enabled =
                timeAlignmentBandpassCheckBox.Checked && !timeAlignmentMeasurement.InProgress;
            timeAlignmentBandpassFadeOctavesNumeric.Enabled =
                timeAlignmentBandpassCheckBox.Checked && !timeAlignmentMeasurement.InProgress;
            timeAlignmentStartButton.Enabled = enabled || timeAlignmentMeasurement.InProgress;
            timeAlignmentStartButton.BackColor = timeAlignmentStartButton.Enabled
                ? Color.FromArgb(50, 55, 80)
                : Color.FromArgb(55, 60, 70);
            timeAlignmentStartButton.ForeColor = timeAlignmentStartButton.Enabled
                ? Color.White
                : Color.FromArgb(120, 125, 135);
        }

        private async Task ToggleTimeAlignmentAsync()
        {
            if (timeAlignmentMeasurement.InProgress)
            {
                await timeAlignmentMeasurement.AbortAsync();
                return;
            }

            if (timeAlignmentDriverComboBox.SelectedItem is not AsioDeviceInfo driver ||
                timeAlignmentMicrophoneComboBox.SelectedItem is not AsioChannelInfo microphone ||
                timeAlignmentLoopbackComboBox.SelectedItem is not AsioChannelInfo loopback ||
                timeAlignmentOutputComboBox.SelectedItem is not AsioChannelInfo output)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            try
            {
                UpdateTimeAlignmentOptionsFromControls();
                SaveMeasurementSettings();
                double duration = expSweepMeasurement.Sweep?.RequestedDuration ?? 1.0;
                timeAlignmentMeasurement.Init(
                    expSweepMeasurement.Octaves,
                    expSweepMeasurement.SampleRate,
                    expSweepMeasurement.Bits,
                    duration,
                    driver.DriverName,
                    microphone.Offset,
                    loopback.Offset,
                    output.Offset,
                    timeAlignmentOptions);

                timeAlignmentStartButton.Text = "Stop";
                buttonRecord.Text = "Running...";
                timeAlignmentStatusLabel.Text = "Measuring time alignment...";
                ClearTimeAlignmentEnvelopePreview();
                _ = timeAlignmentMeasurement.RunAsync();
                SetTimeAlignmentControlsEnabled(false);
            }
            catch (Exception exception)
            {
                timeAlignmentStatusLabel.Text = exception.Message;
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Time Alignment",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SetTimeAlignmentControlsEnabled(true);
            }
        }

        private void UpdateClearButtonState()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            bool hasCurves = plotView1.Model?.Series.Count > 0;
            SetButtonFrozen(buttonClear, !hasCurves);
        }

        private bool CanDrawCurrentMeasurement() =>
            hasCurrentImpulseResponse && !expSweepMeasurement.InProgress;

        private bool ShouldFreezeDrawButton()
        {
            if (modeController.ActiveTab == ModeTab.TimeAlignment)
            {
                return true;
            }

            if (modeController.ActiveTab == ModeTab.LiveSpectrum)
            {
                return false;
            }

            return !CanDrawCurrentMeasurement();
        }

        private void buttonCurrentModeSettings_Click(object sender, EventArgs e)
        {
            switch (modeController.ActiveTab)
            {
                case ModeTab.Impulse:
                    buttonImpOpt_Click(sender, e);
                    break;
                case ModeTab.Frequency:
                    buttonFROpt_Click(sender, e);
                    break;
                case ModeTab.Phase:
                    buttonPROpt_Click(sender, e);
                    break;
                case ModeTab.GroupDelay:
                    buttonGDOpt_Click(sender, e);
                    break;
                case ModeTab.Waterfall:
                    buttonWaterfallOpt_Click(sender, e);
                    break;
                case ModeTab.Burst:
                    buttonBurstDecayOpt_Click(sender, e);
                    break;
                case ModeTab.LiveSpectrum:
                case ModeTab.Autocorrelation:
                    System.Media.SystemSounds.Beep.Play();
                    break;
            }
        }

        private void UpdateCurrentModeSettingsButton()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            bool hasSettings = modeController.ActiveTab is not (
                ModeTab.LiveSpectrum or
                ModeTab.Autocorrelation or
                ModeTab.TimeAlignment);

            SetButtonFrozen(buttonCurrentModeSettings, !hasSettings);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == ChromeTitleBarController.WmNcHitTest &&
                WindowState != FormWindowState.Maximized)
            {
                base.WndProc(ref m);
                if ((int)m.Result == ChromeTitleBarController.HtClient)
                {
                    Point point = PointToClient(
                        ChromeTitleBarController.GetPointFromLParam(m.LParam));
                    m.Result = ChromeTitleBarController.GetResizeHitTest(
                        point,
                        ClientSize);
                }
                return;
            }

            base.WndProc(ref m);
        }

        private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (closingPrepared)
            {
                return;
            }

            e.Cancel = true;
            Enabled = false;
            await Task.WhenAll(
                expSweepMeasurement.AbortAsync(),
                timeAlignmentMeasurement.AbortAsync(),
                liveSpectrumController.AbortAsync());

            DisposeAppResources();
            closingPrepared = true;
            BeginInvoke((MethodInvoker)Close);
        }

        private void DisposeAppResources()
        {
            if (resourcesDisposed)
            {
                return;
            }

            resourcesDisposed = true;
            expSweepMeasurement.Dispose();
            timeAlignmentMeasurement.Dispose();
            liveSpectrumController.Dispose();
        }

        private async void buttonSave_Click(object sender, EventArgs e)
        {
            if (expSweepMeasurement.ImpulseResponse != null && !expSweepMeasurement.InProgress)
            {
                using var dialog = new SaveFileDialog
                {
                    AddExtension = true,
                    DefaultExt = "json",
                    Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
                    FileName = $"Resonalyze-IR-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json",
                    InitialDirectory = Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments),
                    RestoreDirectory = true,
                    Title = "Save impulse response"
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                SetButtonFrozen(buttonSave, true);
                SetButtonFrozen(buttonLoad, true);
                SetButtonFrozen(buttonDraw, true);
                try
                {
                    ImpulseResponseFile file =
                        ImpulseResponseFile.Capture(expSweepMeasurement);
                    await file.SaveAsync(dialog.FileName);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        this,
                        $"Failed to save the impulse response.\r\n\r\n{exception.Message}",
                        "Save failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                finally
                {
                    SetButtonFrozen(buttonSave, false);
                    SetButtonFrozen(buttonLoad, false);
                }
            }
        }

        private async void buttonLoad_Click(object sender, EventArgs e)
        {
            if (!expSweepMeasurement.InProgress)
            {
                using var dialog = new OpenFileDialog
                {
                    CheckFileExists = true,
                    Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
                    InitialDirectory = Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments),
                    Multiselect = false,
                    RestoreDirectory = true,
                    Title = "Load impulse response"
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                SetButtonFrozen(buttonSave, true);
                SetButtonFrozen(buttonLoad, true);
                try
                {
                    ImpulseResponseFile file =
                        await ImpulseResponseFile.LoadAsync(dialog.FileName);
                    expSweepMeasurement.RestoreImpulseResponse(
                        file.Octaves,
                        file.SampleRate,
                        file.Bits,
                        file.SweepDurationSeconds,
                        file.PlayChannel,
                        file.GetImpulseResponse(),
                        file.PeakIndex);

                    buttonRecord.Text = "Loaded";
                    //buttonRecord.BackColor = Color.FromArgb(192, 255, 192);
                    hasCurrentImpulseResponse = true;
                    await SelectModeAsync(ModeTab.Impulse);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        this,
                        $"Failed to load the impulse response.\r\n\r\n{exception.Message}",
                        "Load failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                finally
                {
                    SetButtonFrozen(
                        buttonSave,
                        expSweepMeasurement.ImpulseResponse == null);
                    SetButtonFrozen(buttonLoad, false);
                    UpdateDrawButtonText();
                }
            }
        }

        private static void SetButtonFrozen(Button button, bool frozen)
        {
            if (frozen)
            {
                button.Enabled = false;
                button.BackColor = Color.FromArgb(55, 60, 70);
                button.ForeColor = Color.FromArgb(120, 125, 135);
            }
            else
            {
                button.BackColor = Color.FromArgb(50,  55,  80);
                button.ForeColor = Color.FromArgb(255, 255, 255);
                button.Enabled = true;
            }
        }

        private async void buttonDraw_Click(object sender, EventArgs e)
        {
            if (modeController.ActiveTab == ModeTab.LiveSpectrum)
            {
                await liveSpectrumController.ToggleAsync();
                return;
            }

            if (ShouldFreezeDrawButton())
            {
                return;
            }

            DrawSelectedMode(includeCurves: true);
        }
    }
}
