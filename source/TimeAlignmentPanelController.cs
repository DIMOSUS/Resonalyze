using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed class TimeAlignmentPanelController : IDisposable
{
    private const double SpeedOfSoundAt20C = 343.2;

    private readonly Form owner;
    private readonly ExpSweepMeasurement expSweepMeasurement;
    private readonly TimeAlignmentOptions options;
    private readonly Action<string> setRecordButtonText;
    private readonly Action saveSettings;
    private readonly Func<bool> isTimeAlignmentActive;
    private readonly TimeAlignmentMeasurement measurement = new();
    private readonly Panel panel;
    private readonly ComboBox driverComboBox;
    private readonly ComboBox microphoneComboBox;
    private readonly ComboBox loopbackComboBox;
    private readonly ComboBox outputComboBox;
    private readonly CheckBox bandpassCheckBox;
    private readonly NumericUpDown bandpassCenterNumeric;
    private readonly NumericUpDown bandpassPassOctavesNumeric;
    private readonly NumericUpDown bandpassFadeOctavesNumeric;
    private readonly ComboBox peakSearchModeComboBox;
    private readonly PlotView bandpassPlotView;
    private readonly PlotView envelopePlotView;
    private readonly Button startButton;
    private readonly RichTextBox statusTextBox;
    private bool disposed;

    public TimeAlignmentPanelController(
        Form owner,
        ExpSweepMeasurement expSweepMeasurement,
        TimeAlignmentOptions options,
        Action<string> setRecordButtonText,
        Action saveSettings,
        Func<bool> isTimeAlignmentActive)
    {
        this.owner = owner;
        this.expSweepMeasurement = expSweepMeasurement;
        this.options = options;
        this.setRecordButtonText = setRecordButtonText;
        this.saveSettings = saveSettings;
        this.isTimeAlignmentActive = isTimeAlignmentActive;

        (
            panel,
            driverComboBox,
            microphoneComboBox,
            loopbackComboBox,
            outputComboBox,
            bandpassCheckBox,
            bandpassCenterNumeric,
            bandpassPassOctavesNumeric,
            bandpassFadeOctavesNumeric,
            peakSearchModeComboBox,
            bandpassPlotView,
            envelopePlotView,
            startButton,
            statusTextBox) = CreatePanel();

        measurement.Completed += MeasurementCompleted;
        ApplyOptionsToControls();
        UpdateBandpassPreview();
    }

    public bool InProgress => measurement.InProgress;

    public void SetVisible(bool visible)
    {
        panel.Visible = visible;
        if (visible)
        {
            RefreshDrivers();
        }
    }

    public async Task ToggleAsync()
    {
        if (measurement.InProgress)
        {
            await measurement.AbortAsync();
            return;
        }

        if (driverComboBox.SelectedItem is not AsioDeviceInfo driver ||
            microphoneComboBox.SelectedItem is not AsioChannelInfo microphone ||
            loopbackComboBox.SelectedItem is not AsioChannelInfo loopback ||
            outputComboBox.SelectedItem is not AsioChannelInfo output)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        try
        {
            UpdateOptionsFromControls();
            saveSettings();
            double duration = expSweepMeasurement.Sweep?.RequestedDuration ?? 1.0;
            measurement.Init(
                expSweepMeasurement.Octaves,
                expSweepMeasurement.SampleRate,
                expSweepMeasurement.Bits,
                duration,
                driver.DriverName,
                microphone.Offset,
                loopback.Offset,
                output.Offset,
                options);

            startButton.Text = "Stop";
            setRecordButtonText("Running...");
            SetStatusText("Measuring time alignment...");
            ClearEnvelopePreview();
            _ = measurement.RunAsync();
            SetControlsEnabled(false);
        }
        catch (Exception exception)
        {
            SetStatusText(exception.Message);
            MessageBox.Show(
                owner,
                exception.Message,
                "Time Alignment",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            SetControlsEnabled(true);
        }
    }

    public Task AbortAsync() => measurement.AbortAsync();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        measurement.Dispose();
    }

    private void MeasurementCompleted(bool success)
    {
        if (owner.IsDisposed || !owner.IsHandleCreated)
        {
            return;
        }

        owner.BeginInvoke((MethodInvoker)delegate
        {
            startButton.Text = "Start";
            setRecordButtonText(success ? "Ready" : measurement.LastError == null ? "Aborted" : "Error");

            if (isTimeAlignmentActive())
            {
                UpdateChannels();
            }

            if (success)
            {
                double delaySamples = measurement.PeakSample;
                double delayMilliseconds = measurement.DelayMilliseconds;
                double delayMeters = Math.Abs(delayMilliseconds) * SpeedOfSoundAt20C / 1000.0;
                SetMeasurementResultStatus(
                    delayMilliseconds,
                    delayMeters,
                    delaySamples);
                UpdateEnvelopePreview();
            }
            else if (measurement.LastError != null)
            {
                SetStatusText(measurement.LastError.Message);
                ClearEnvelopePreview();
            }
            else
            {
                SetStatusText("Time-alignment measurement aborted.");
                ClearEnvelopePreview();
            }        });
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
        RichTextBox Status) CreatePanel()
    {
        int top = GetScaledTitleBarHeight() + 12;
        var newPanel = new Panel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AutoScroll = true,
            BackColor = Color.FromArgb(40, 44, 54),
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(12, top),
            Size = new Size(1096, 706),
            Visible = false
        };

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(owner.Font, FontStyle.Bold),
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
            Location = new Point(560, 24),
            Size = new Size(380, 200),
            Text =
                "What it is for\r\n" +
                "Measures acoustic delay relative to the audio interface reference path.\r\n\r\n" +
                "How it works\r\n" +
                "Resonalyze plays the same mono sweep, records the microphone and ASIO loopback at the same time, then compares the two impulse-response peaks.\r\n\r\n" +
                "Why ASIO + loopback\r\n" +
                "Both channels must be captured by the same low-latency driver clock. Windows Wave devices do not provide a reliable hardware reference input."
        };

        ComboBox driver = CreateComboBox(180, 78);
        ComboBox microphone = CreateComboBox(180, 112);
        ComboBox loopback = CreateComboBox(180, 146);
        ComboBox output = CreateComboBox(180, 180);
        CheckBox bandpassEnabled = new()
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(210, 214, 222),
            Location = new Point(18, 252),
            Text = "Use bandpass window"
        };
        NumericUpDown bandpassCenter =
            CreateNumericUpDown(180, 282, 20, 20_000, 1000, 10);
        NumericUpDown bandpassPassOctaves =
            CreateNumericUpDown(180, 316, 0, 8, 1, 0.1M);
        NumericUpDown bandpassFadeOctaves =
            CreateNumericUpDown(180, 350, 0, 8, 0.5M, 0.1M);
        PlotView bandpassPreview = new()
        {
            BackColor = Color.FromArgb(32, 36, 46),
            Location = new Point(18, 388),
            Size = new Size(520, 145),
            Visible = false
        };
        ComboBox peakSearchMode = CreateComboBox(180, 218);
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
        StatusRichTextBox status = new()
        {
            BackColor = Color.FromArgb(40, 44, 54),
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
            ForeColor = Color.FromArgb(190, 195, 205),
            Font = new Font(owner.Font.FontFamily, 11, FontStyle.Bold),
            Location = new Point(560, 290),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.None,
            Size = new Size(400, 235),
            Text = "Select an ASIO driver with loopback input channels."
        };
        status.UseHandCursorAt = point =>
            TryGetCopyableStatusLine(point, out _);
        status.MouseClick += StatusTextBoxMouseClick;

        newPanel.Controls.AddRange(
        [
            title,
            description,
            help,
            CreateLabel("ASIO driver", 82),
            driver,
            CreateLabel("Microphone input", 116),
            microphone,
            CreateLabel("Loopback input", 150),
            loopback,
            CreateLabel("Output pair", 184),
            output,
            CreateLabel("Peak detection", 222),
            peakSearchMode,
            bandpassEnabled,
            CreateLabel("Center frequency, Hz", 286),
            bandpassCenter,
            CreateLabel("Pass width, oct", 320),
            bandpassPassOctaves,
            CreateLabel("Fade width, oct", 354),
            bandpassFadeOctaves,
            bandpassPreview,
            envelopePreview,
            start,
            status
        ]);
        ScaleRuntimeControlTree(newPanel);
        owner.Controls.Add(newPanel);
        newPanel.BringToFront();

        driver.SelectedIndexChanged += (_, _) =>
        {
            UpdateChannels();
            SaveSettingsFromControls();
        };
        microphone.SelectedIndexChanged += (_, _) => SaveSettingsFromControls();
        loopback.SelectedIndexChanged += (_, _) => SaveSettingsFromControls();
        output.SelectedIndexChanged += (_, _) => SaveSettingsFromControls();
        bandpassEnabled.CheckedChanged += (_, _) =>
        {
            UpdateOptionsFromControls();
            UpdateBandpassPreview();
            bandpassCenter.Enabled = bandpassEnabled.Checked && !measurement.InProgress;
            bandpassPassOctaves.Enabled = bandpassEnabled.Checked && !measurement.InProgress;
            bandpassFadeOctaves.Enabled = bandpassEnabled.Checked && !measurement.InProgress;
            saveSettings();
        };
        bandpassCenter.ValueChanged += (_, _) =>
        {
            UpdateOptionsFromControls();
            UpdateBandpassPreview();
            saveSettings();
        };
        bandpassPassOctaves.ValueChanged += (_, _) =>
        {
            UpdateOptionsFromControls();
            UpdateBandpassPreview();
            saveSettings();
        };
        bandpassFadeOctaves.ValueChanged += (_, _) =>
        {
            UpdateOptionsFromControls();
            UpdateBandpassPreview();
            saveSettings();
        };
        peakSearchMode.SelectedIndexChanged += (_, _) =>
        {
            UpdateOptionsFromControls();
            saveSettings();
        };
        start.Click += async (_, _) => await ToggleAsync();

        return (
            newPanel,
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

    private void ApplyOptionsToControls()
    {
        bandpassCheckBox.Checked = options.UseBandpassWindow;
        bandpassCenterNumeric.Value =
            ClampDecimal(options.BandpassCenterHz, bandpassCenterNumeric);
        bandpassPassOctavesNumeric.Value =
            ClampDecimal(options.BandpassPassOctaves, bandpassPassOctavesNumeric);
        bandpassFadeOctavesNumeric.Value =
            ClampDecimal(options.BandpassFadeOctaves, bandpassFadeOctavesNumeric);
        peakSearchModeComboBox.SelectedIndex =
            options.PeakSearchMode == TimeAlignmentPeakSearchMode.StrongestPeak
                ? 1
                : 0;
    }

    private void UpdateOptionsFromControls()
    {
        options.UseBandpassWindow = bandpassCheckBox.Checked;
        options.BandpassCenterHz = (double)bandpassCenterNumeric.Value;
        options.BandpassPassOctaves = (double)bandpassPassOctavesNumeric.Value;
        options.BandpassFadeOctaves = (double)bandpassFadeOctavesNumeric.Value;
        options.PeakSearchMode =
            peakSearchModeComboBox.SelectedIndex == 1
                ? TimeAlignmentPeakSearchMode.StrongestPeak
                : TimeAlignmentPeakSearchMode.FirstArrival;

        if (driverComboBox.SelectedItem is AsioDeviceInfo driver)
        {
            options.AsioDriverName = driver.DriverName;
        }
        if (microphoneComboBox.SelectedItem is AsioChannelInfo microphone)
        {
            options.MicrophoneInputChannelOffset = microphone.Offset;
        }
        if (loopbackComboBox.SelectedItem is AsioChannelInfo loopback)
        {
            options.LoopbackInputChannelOffset = loopback.Offset;
        }
        if (outputComboBox.SelectedItem is AsioChannelInfo output)
        {
            options.AsioOutputChannelOffset = output.Offset;
        }
    }

    private void RefreshDrivers()
    {
        string? preferredDriver =
            (driverComboBox.SelectedItem as AsioDeviceInfo)?.DriverName ??
            options.AsioDriverName ??
            expSweepMeasurement.AsioDriverName;
        IReadOnlyList<AsioDeviceInfo> drivers = AsioDeviceCatalog.GetDrivers();
        driverComboBox.Items.Clear();
        driverComboBox.Items.AddRange(drivers.Cast<object>().ToArray());
        driverComboBox.SelectedIndex =
            AsioDeviceCatalog.FindDriverIndex(drivers, preferredDriver);

        if (drivers.Count == 0)
        {
            SetStatusText("ASIO is not available on this system.");
            SetControlsEnabled(false);
        }
    }

    private void UpdateChannels()
    {
        if (driverComboBox.SelectedItem is not AsioDeviceInfo driver)
        {
            SetStatusText("Select an ASIO driver.");
            SetControlsEnabled(false);
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
            microphoneComboBox,
            microphoneInputs,
            options.MicrophoneInputChannelOffset);
        FillChannelComboBox(
            loopbackComboBox,
            loopbackInputs,
            options.LoopbackInputChannelOffset);
        FillChannelComboBox(
            outputComboBox,
            info.OutputChannels,
            options.AsioOutputChannelOffset);

        bool canRun =
            string.IsNullOrWhiteSpace(info.ErrorMessage) &&
            info.SupportsSampleRate &&
            microphoneInputs.Length > 0 &&
            loopbackInputs.Length > 0 &&
            info.OutputChannels.Count > 0;
        SetControlsEnabled(canRun);
        SetStatusText(GetStatus(info, loopbackInputs.Length, canRun));
    }

    private string GetStatus(
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

    private void SetControlsEnabled(bool enabled)
    {
        driverComboBox.Enabled = !measurement.InProgress;
        microphoneComboBox.Enabled = enabled && !measurement.InProgress;
        loopbackComboBox.Enabled = enabled && !measurement.InProgress;
        outputComboBox.Enabled = enabled && !measurement.InProgress;
        bandpassCheckBox.Enabled = !measurement.InProgress;
        bandpassCenterNumeric.Enabled = bandpassCheckBox.Checked && !measurement.InProgress;
        bandpassPassOctavesNumeric.Enabled = bandpassCheckBox.Checked && !measurement.InProgress;
        bandpassFadeOctavesNumeric.Enabled = bandpassCheckBox.Checked && !measurement.InProgress;
        startButton.Enabled = enabled || measurement.InProgress;
        startButton.BackColor = startButton.Enabled
            ? Color.FromArgb(50, 55, 80)
            : Color.FromArgb(55, 60, 70);
        startButton.ForeColor = startButton.Enabled
            ? Color.White
            : Color.FromArgb(120, 125, 135);
    }

    private void UpdateBandpassPreview()
    {
        bandpassPlotView.Visible = bandpassCheckBox.Checked;
        if (!bandpassCheckBox.Checked)
        {
            bandpassPlotView.Model = null;
            return;
        }

        var model = CreatePreviewPlotModel("Bandpass Window");
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
        model.Axes.Add(CreateDecibelAxis());

        var series = new LineSeries
        {
            Color = OxyColor.FromRgb(255, 210, 80),
            StrokeThickness = 2
        };
        (double f1, double f2, double f3, double f4) = BandpassWindow.BandAround(
            (double)bandpassCenterNumeric.Value,
            (double)bandpassPassOctavesNumeric.Value,
            (double)bandpassFadeOctavesNumeric.Value);
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
        bandpassPlotView.Model = model;
    }

    private void UpdateEnvelopePreview()
    {
        double[]? envelope = measurement.EnvelopeSamples;
        if (envelope == null ||
            envelope.Length == 0 ||
            measurement.EnvelopePeak <= 0 ||
            measurement.SampleRate <= 0)
        {
            ClearEnvelopePreview();
            return;
        }

        var model = CreatePreviewPlotModel("Envelope Around Peak");
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
        model.Axes.Add(CreateDecibelAxis());

        var series = new LineSeries
        {
            Color = OxyColor.FromRgb(255, 210, 80),
            StrokeThickness = 2
        };
        int radius = Math.Min(
            envelope.Length / 2,
            Math.Max(1, (int)Math.Round(measurement.SampleRate * 0.05)));
        int step = Math.Max(1, radius * 2 / 600);
        for (int offset = -radius; offset <= radius; offset += step)
        {
            int index = WrapIndex(measurement.EnvelopePeakIndex + offset, envelope.Length);
            double milliseconds = offset * 1000.0 / measurement.SampleRate;
            double relativeAmplitude = envelope[index] / measurement.EnvelopePeak;
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
            measurement.StrongestEnvelopePeakIndex - measurement.EnvelopePeakIndex,
            envelope.Length);
        double strongestMilliseconds =
            strongestOffset * 1000.0 / measurement.SampleRate;
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
        envelopePlotView.Model = model;
        envelopePlotView.Visible = true;
    }

    private void ClearEnvelopePreview()
    {
        envelopePlotView.Model = null;
        envelopePlotView.Visible = false;
    }

    private void SaveSettingsFromControls()
    {
        UpdateOptionsFromControls();
        saveSettings();
    }

    private void SetStatusText(string text)
    {
        statusTextBox.Clear();
        AppendStatusText(text, Color.FromArgb(190, 195, 205));
    }

    private void SetMeasurementResultStatus(
        double delayMilliseconds,
        double delayMeters,
        double delaySamples)
    {
        string confidence = FormatConfidence(measurement.ConfidenceDecibels);
        Color confidenceColor = GetConfidenceColor(confidence);

        statusTextBox.Clear();
        AppendStatusText("Measured delay:\r\n", Color.FromArgb(220, 225, 235));
        AppendStatusText($"{delayMilliseconds:0.000} ms\r\n", Color.FromArgb(220, 225, 235));
        AppendStatusText($"{delayMeters:0.000} m (at 20 °C)\r\n", Color.FromArgb(220, 225, 235));
        AppendStatusText($"{delaySamples:0.0} samples\r\n\r\n", Color.FromArgb(220, 225, 235));
        AppendStatusText("Signal Quality:\r\n", Color.FromArgb(220, 225, 235));
        AppendStatusText(
            $"{confidence} ({measurement.ConfidenceDecibels:0.0} dB)\r\n",
            confidenceColor);
        AppendStatusText(FormatPeakDetection() + "\r\n", Color.FromArgb(220, 225, 235));
        AppendStatusText(
            FormatLevel(
                "Mic",
                measurement.MicrophonePeakDbFs,
                measurement.MicrophoneRmsDbFs,
                measurement.MicrophoneClipped,
                fullScaleIsNormal: false) + "\r\n",
            Color.FromArgb(220, 225, 235));
        AppendStatusText(
            FormatLevel(
                "Loopback",
                measurement.LoopbackPeakDbFs,
                measurement.LoopbackRmsDbFs,
                measurement.LoopbackClipped,
                fullScaleIsNormal: true),
            Color.FromArgb(220, 225, 235));
        statusTextBox.SelectionStart = 0;
        statusTextBox.SelectionLength = 0;
    }

    private void AppendStatusText(string text, Color color)
    {
        statusTextBox.SelectionStart = statusTextBox.TextLength;
        statusTextBox.SelectionLength = 0;
        statusTextBox.SelectionColor = color;
        statusTextBox.AppendText(text);
        statusTextBox.SelectionColor = statusTextBox.ForeColor;
    }

    private void StatusTextBoxMouseClick(object? sender, MouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left ||
            !TryGetCopyableStatusLine(args.Location, out string value))
        {
            return;
        }

        Clipboard.SetText(value);
        System.Media.SystemSounds.Asterisk.Play();
    }

    private bool TryGetCopyableStatusLine(Point location, out string value)
    {
        value = string.Empty;
        if (!statusTextBox.Text.StartsWith("Measured delay:", StringComparison.Ordinal))
        {
            return false;
        }

        int index = statusTextBox.GetCharIndexFromPosition(location);
        int line = statusTextBox.GetLineFromCharIndex(index);
        if (line is < 1 or > 3 ||
            line >= statusTextBox.Lines.Length)
        {
            return false;
        }

        value = GetFirstToken(statusTextBox.Lines[line]);
        return value.Length > 0;
    }

    private static string GetFirstToken(string line)
    {
        string trimmed = line.Trim();
        int separatorIndex = trimmed.IndexOf(' ');
        return separatorIndex < 0
            ? trimmed
            : trimmed[..separatorIndex];
    }

    private string FormatPeakDetection()
    {
        string mode = options.PeakSearchMode == TimeAlignmentPeakSearchMode.FirstArrival
            ? "First arrival"
            : "Strongest peak";
        return measurement.PeakSearchFallbackUsed
            ? "Peak: First arrival fallback to strongest"
            : $"Peak: {mode}";
    }

    private static string FormatLevel(
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

    private static string FormatConfidence(double confidenceDecibels)
    {
        if (confidenceDecibels >= 45)
        {
            return "Excellent";
        }
        if (confidenceDecibels >= 34)
        {
            return "Good";
        }
        if (confidenceDecibels >= 23)
        {
            return "Fair";
        }

        return "Poor";
    }

    private static Color GetConfidenceColor(string confidence) =>
        confidence switch
        {
            "Excellent" => Color.FromArgb(90, 220, 120),
            "Good" => Color.FromArgb(170, 220, 95),
            "Fair" => Color.FromArgb(255, 190, 80),
            _ => Color.FromArgb(255, 110, 110)
        };

    private static PlotModel CreatePreviewPlotModel(string title) =>
        new()
        {
            Background = OxyColor.FromRgb(32, 36, 46),
            PlotAreaBackground = OxyColor.FromRgb(32, 36, 46),
            TextColor = OxyColors.White,
            Title = title,
            TitleColor = OxyColors.White,
            TitleFontSize = 10
        };

    private static LinearAxis CreateDecibelAxis() =>
        new()
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
        };

    private static Label CreateLabel(string text, int y) =>
        new()
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(210, 214, 222),
            Location = new Point(18, y),
            Text = text
        };

    private static ComboBox CreateComboBox(int x, int y) =>
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

    private static NumericUpDown CreateNumericUpDown(
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

    private static decimal ClampDecimal(double value, NumericUpDown numeric)
    {
        decimal decimalValue = (decimal)value;
        return Math.Min(numeric.Maximum, Math.Max(numeric.Minimum, decimalValue));
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

    private void ScaleRuntimeControlTree(System.Windows.Forms.Control root)
    {
        float factor = GetRuntimeDpiScale();
        if (factor <= 1.01f)
        {
            return;
        }

        root.SuspendLayout();
        ScaleRuntimeBounds(root, factor, scaleLocation: false);
        root.ResumeLayout(false);
    }

    private static void ScaleRuntimeBounds(
        System.Windows.Forms.Control control,
        float factor,
        bool scaleLocation)
    {
        int left = scaleLocation
            ? (int)Math.Round(control.Left * factor)
            : control.Left;
        int top = scaleLocation
            ? (int)Math.Round(control.Top * factor)
            : control.Top;
        control.Bounds = new Rectangle(
            left,
            top,
            Math.Max(1, (int)Math.Round(control.Width * factor)),
            Math.Max(1, (int)Math.Round(control.Height * factor)));

        foreach (System.Windows.Forms.Control child in control.Controls)
        {
            ScaleRuntimeBounds(child, factor, scaleLocation: true);
        }
    }

    private float GetRuntimeDpiScale()
    {
        using Graphics graphics = owner.CreateGraphics();
        return Math.Max(owner.DeviceDpi / 96.0f, graphics.DpiX / 96.0f);
    }

    private int GetScaledTitleBarHeight() =>
        (int)Math.Round(ChromeTitleBarController.Height * GetRuntimeDpiScale());
}

internal sealed class StatusRichTextBox : RichTextBox
{
    private const int WmSetCursor = 0x20;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<Point, bool>? UseHandCursorAt { get; set; }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmSetCursor)
        {
            Point point = PointToClient(Cursor.Position);
            Cursor.Current = UseHandCursorAt?.Invoke(point) == true
                ? Cursors.Hand
                : Cursors.Default;
            message.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref message);
    }
}
