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
    private const int DelayTableFirstColumn = 18;
    private const int DelayTableSecondColumn = 34;

    private readonly Form owner;
    private readonly ExpSweepMeasurement expSweepMeasurement;
    private readonly TimeAlignmentOptions options;
    private readonly Action<string> setRecordButtonText;
    private readonly Action saveSettings;
    private readonly Func<bool> isTimeAlignmentActive;
    private readonly TimeAlignmentMeasurement measurement = new();
    private readonly Panel panel;
    private readonly Label routeSummaryLabel;
    private readonly CheckBox bandpassCheckBox;
    private readonly NumericUpDown bandpassCenterNumeric;
    private readonly NumericUpDown bandpassPassOctavesNumeric;
    private readonly NumericUpDown bandpassFadeOctavesNumeric;
    private readonly PlotView bandpassPlotView;
    private readonly PlotView envelopePlotView;
    private readonly Button startButton;
    private readonly RichTextBox statusTextBox;
    private readonly Font resultTableFont;
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
        resultTableFont = new Font(
            FontFamily.GenericMonospace,
            owner.Font.Size + 4.0f,
            FontStyle.Bold);

        (
            panel,
            routeSummaryLabel,
            bandpassCheckBox,
            bandpassCenterNumeric,
            bandpassPassOctavesNumeric,
            bandpassFadeOctavesNumeric,
            bandpassPlotView,
            envelopePlotView,
            startButton,
            statusTextBox) = CreatePanel();

        measurement.Completed += MeasurementCompleted;
        ApplyOptionsToControls();
        UpdateBandpassPreview();
    }

    public bool InProgress => measurement.InProgress;
    public TimeAlignmentMeasurement Measurement => measurement;

    public void SetVisible(bool visible)
    {
        panel.Visible = visible;
        if (visible)
        {
            RefreshConfiguration();
        }
    }

    public void RefreshConfiguration()
    {
        UpdateRouteSummary();
        UpdateBandpassPreview();
    }

    public async Task ToggleAsync()
    {
        if (measurement.InProgress)
        {
            await measurement.AbortAsync();
            return;
        }

        if (!TryValidateMeasurementRoute(out string routeError))
        {
            SetStatusText(routeError);
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        try
        {
            UpdateOptionsFromControls();
            saveSettings();
            double duration = expSweepMeasurement.Sweep?.RequestedDuration ?? 1.0;
            int microphoneInputChannelOffset = expSweepMeasurement.AudioBackend == AudioBackend.Wave
                ? expSweepMeasurement.WaveInputChannelOffset
                : expSweepMeasurement.AsioInputChannelOffset;
            int loopbackInputChannelOffset = expSweepMeasurement.AudioBackend == AudioBackend.Wave
                ? expSweepMeasurement.WaveLoopbackInputChannelOffset ??
                    throw new InvalidOperationException("Wave loopback input is not configured.")
                : expSweepMeasurement.AsioLoopbackInputChannelOffset ??
                    throw new InvalidOperationException("ASIO loopback input is not configured.");
            measurement.Init(
                expSweepMeasurement.Octaves,
                expSweepMeasurement.SampleRate,
                expSweepMeasurement.Bits,
                duration,
                expSweepMeasurement.AudioBackend,
                expSweepMeasurement.OutputDeviceNumber,
                expSweepMeasurement.InputDeviceNumber,
                expSweepMeasurement.PlaybackChannel,
                expSweepMeasurement.AsioDriverName,
                microphoneInputChannelOffset,
                loopbackInputChannelOffset,
                expSweepMeasurement.AsioOutputChannelOffset,
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
        resultTableFont.Dispose();
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
                UpdateRouteSummary();
            }

            if (success)
            {
                SetMeasurementResultStatus();
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
        Label RouteSummary,
        CheckBox BandpassEnabled,
        NumericUpDown BandpassCenter,
        NumericUpDown BandpassPassOctaves,
        NumericUpDown BandpassFadeOctaves,
        PlotView BandpassPreview,
        PlotView EnvelopePreview,
        Button Start,
        RichTextBox Status) CreatePanel()
    {
        int top = GetScaledTitleBarHeight() + 12;
        var newPanel = new Panel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            AutoScroll = true,
            BackColor = UiPalette.PlotSurfaceMuted,
            BorderStyle = BorderStyle.FixedSingle,
            Location = new Point(12, top),
            Size = new Size(1182, 706),
            Visible = false
        };

        var title = UiStyle.CreateTitleLabel("Time Alignment", new Point(18, 18));
        var description = UiStyle.CreateInfoLabel(
            "Loopback measurement: microphone input + reference input from Record Settings.",
            new Point(18, 44));
        var help = UiStyle.CreateLabel(
            "What it is for\r\n" +
            "Measures acoustic delay relative to the audio interface reference path.\r\n\r\n" +
            "How it works\r\n" +
            "Resonalyze plays the configured sweep, records microphone and loopback at the same time, then compares the two impulse-response peaks.\r\n\r\n" +
            "Accuracy note\r\n" +
            "Wave mode is supported with a stereo input, but ASIO is recommended for best timing accuracy.",
            new Point(560, 24),
            UiPalette.TextSecondaryAlt,
            owner.Font,
            autoSize: false);
        help.Size = new Size(520, 150);

        Label routeSummary = UiStyle.CreateLabel(
            "Configure microphone and loopback channels in Record Settings.",
            new Point(18, 78),
            UiPalette.TextHighlight,
            owner.Font,
            autoSize: false);
        routeSummary.Size = new Size(520, 90);
        CheckBox bandpassEnabled = UiStyle.CreateDarkCheckBox("Use bandpass window", new Point(18, 177));
        NumericUpDown bandpassCenter =
            UiStyle.CreateDarkNumericUpDown(new Point(180, 207), new Size(120, 23), 20, 20_000, 1000, 10);
        NumericUpDown bandpassPassOctaves =
            UiStyle.CreateDarkNumericUpDown(new Point(180, 241), new Size(120, 23), 0, 8, 1, 0.1M);
        NumericUpDown bandpassFadeOctaves =
            UiStyle.CreateDarkNumericUpDown(new Point(180, 275), new Size(120, 23), 0, 8, 0.5M, 0.1M);
        PlotView bandpassPreview = UiStyle.CreateDarkPreviewPlotView(new Point(18, 313), new Size(520, 200));
        PlotView envelopePreview = UiStyle.CreateDarkPreviewPlotView(new Point(560, 380), new Size(520, 300));
        Button start = UiStyle.CreateDarkActionButton("Start", new Point(560, 198), new Size(520, 28));
        StatusRichTextBox status = new()
        {
            BackColor = UiPalette.PlotSurfaceMuted,
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
            ForeColor = UiPalette.TextSecondarySoft,
            Font = new Font(owner.Font.FontFamily, 11, FontStyle.Bold),
            Location = new Point(560, 240),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.None,
            Size = new Size(560, 235),
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
            routeSummary,
            bandpassEnabled,
            UiStyle.CreateLabel("Center frequency, Hz", new Point(18, 211), UiPalette.TextHighlight, owner.Font),
            bandpassCenter,
            UiStyle.CreateLabel("Pass width, oct", new Point(18, 245), UiPalette.TextHighlight, owner.Font),
            bandpassPassOctaves,
            UiStyle.CreateLabel("Fade width, oct", new Point(18, 279), UiPalette.TextHighlight, owner.Font),
            bandpassFadeOctaves,
            bandpassPreview,
            envelopePreview,
            start,
            status
        ]);
        ScaleRuntimeControlTree(newPanel);
        owner.Controls.Add(newPanel);
        newPanel.BringToFront();
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
        start.Click += async (_, _) => await ToggleAsync();

        return (
            newPanel,
            routeSummary,
            bandpassEnabled,
            bandpassCenter,
            bandpassPassOctaves,
            bandpassFadeOctaves,
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
    }

    private void UpdateOptionsFromControls()
    {
        options.UseBandpassWindow = bandpassCheckBox.Checked;
        options.BandpassCenterHz = (double)bandpassCenterNumeric.Value;
        options.BandpassPassOctaves = (double)bandpassPassOctavesNumeric.Value;
        options.BandpassFadeOctaves = (double)bandpassFadeOctavesNumeric.Value;
    }

    private void UpdateRouteSummary()
    {
        bool canMeasure = TryValidateMeasurementRoute(out string message);
        routeSummaryLabel.Text = FormatRouteSummary(message);
        SetControlsEnabled(canMeasure);
        if (!measurement.InProgress &&
            !HasMeasurementResult())
        {
            SetStatusText(canMeasure
                ? "Ready to measure microphone delay against configured loopback."
                : message);
        }
    }

    private bool HasMeasurementResult() =>
        measurement.EnvelopeSamples is { Length: > 0 } &&
        measurement.LastError == null;

    private string FormatRouteSummary(string message)
    {
        if (expSweepMeasurement.AudioBackend == AudioBackend.Wave)
        {
            string playbackDevice = GetWavePlaybackDeviceName(expSweepMeasurement.OutputDeviceNumber);
            string recordingDevice = GetWaveRecordingDeviceName(expSweepMeasurement.InputDeviceNumber);
            string mic = FormatWaveChannel(expSweepMeasurement.WaveInputChannelOffset);
            string loopback = expSweepMeasurement.WaveLoopbackInputChannelOffset.HasValue
                ? FormatWaveChannel(expSweepMeasurement.WaveLoopbackInputChannelOffset.Value)
                : "None";
            return
                $"Backend: Wave\r\n" +
                $"Playback device: {playbackDevice}\r\n" +
                $"Recording device: {recordingDevice}\r\n" +
                $"Mic input: {mic}\r\n" +
                $"Loopback input: {loopback}\r\n" +
                "Wave mode works, but ASIO is recommended for best timing accuracy.";
        }

        string driverName = string.IsNullOrWhiteSpace(expSweepMeasurement.AsioDriverName)
            ? "ASIO"
            : expSweepMeasurement.AsioDriverName;
        string asioMic = "Channel " + (expSweepMeasurement.AsioInputChannelOffset + 1);
        string asioLoopback = expSweepMeasurement.AsioLoopbackInputChannelOffset.HasValue
            ? "Channel " + (expSweepMeasurement.AsioLoopbackInputChannelOffset.Value + 1)
            : "None";
        string asioOutput = "Channel " + (expSweepMeasurement.AsioOutputChannelOffset + 1);
        if (!string.IsNullOrWhiteSpace(expSweepMeasurement.AsioDriverName))
        {
            AsioDriverInfo driverInfo = AsioDeviceCatalog.GetDriverInfo(
                expSweepMeasurement.AsioDriverName,
                expSweepMeasurement.SampleRate);
            asioMic = FormatAsioChannel(
                driverInfo.InputChannels,
                expSweepMeasurement.AsioInputChannelOffset);
            asioLoopback = expSweepMeasurement.AsioLoopbackInputChannelOffset.HasValue
                ? FormatAsioChannel(
                    driverInfo.InputChannels,
                    expSweepMeasurement.AsioLoopbackInputChannelOffset.Value)
                : "None";
            asioOutput = FormatAsioChannel(
                driverInfo.OutputChannels,
                expSweepMeasurement.AsioOutputChannelOffset);
        }
        return
            $"Backend:   ASIO ({driverName})\r\n" +
            $"Output:   {asioOutput}\r\n" +
            $"Mic input:   {asioMic}\r\n" +
            $"Loopback input:   {asioLoopback}";
    }

    private bool TryValidateMeasurementRoute(out string message)
    {
        if (expSweepMeasurement.AudioBackend == AudioBackend.Asio)
        {
            if (string.IsNullOrWhiteSpace(expSweepMeasurement.AsioDriverName))
            {
                message = "Time Alignment requires an ASIO driver or Wave device with loopback.";
                return false;
            }
            if (!expSweepMeasurement.AsioLoopbackInputChannelOffset.HasValue)
            {
                message = "Select an ASIO loopback input in Record Settings.";
                return false;
            }
            if (expSweepMeasurement.AsioLoopbackInputChannelOffset.Value ==
                expSweepMeasurement.AsioInputChannelOffset)
            {
                message = "Microphone and loopback inputs must use different ASIO channels.";
                return false;
            }

            message = "ASIO route is ready.";
            return true;
        }

        if (!expSweepMeasurement.WaveLoopbackInputChannelOffset.HasValue)
        {
            message = "Select a Wave loopback input in Record Settings.";
            return false;
        }
        if (expSweepMeasurement.WaveLoopbackInputChannelOffset.Value ==
            expSweepMeasurement.WaveInputChannelOffset)
        {
            message = "Microphone and loopback inputs must use different Wave channels.";
            return false;
        }
        AudioDeviceInfo? device = AudioDeviceCatalog
            .GetRecordingDevices()
            .FirstOrDefault(candidate =>
                candidate.DeviceNumber == expSweepMeasurement.InputDeviceNumber);
        if (device == null || device.Channels < 2)
        {
            message = "Wave Time Alignment requires a selected stereo recording device.";
            return false;
        }

        message = "Wave route is ready. ASIO is recommended for best timing accuracy.";
        return true;
    }

    private void SetControlsEnabled(bool enabled)
    {
        bandpassCheckBox.Enabled = !measurement.InProgress;
        bandpassCenterNumeric.Enabled = bandpassCheckBox.Checked && !measurement.InProgress;
        bandpassPassOctavesNumeric.Enabled = bandpassCheckBox.Checked && !measurement.InProgress;
        bandpassFadeOctavesNumeric.Enabled = bandpassCheckBox.Checked && !measurement.InProgress;
        startButton.Enabled = enabled || measurement.InProgress;
        startButton.BackColor = startButton.Enabled
            ? UiPalette.ButtonBackground
            : UiPalette.ButtonDisabledBackground;
        startButton.ForeColor = startButton.Enabled
            ? Color.White
            : UiPalette.TextMuted;
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
            AbsoluteMaximum = 20_000,
            AbsoluteMinimum = 20,
            MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
            MinorGridlineStyle = LineStyle.Dot,
            TextColor = OxyColors.White,
            TicklineColor = OxyColors.White
        });
        var dbAxis = CreateDecibelAxis();
        dbAxis.AbsoluteMinimum = -100;
        dbAxis.AbsoluteMaximum = 20;
        model.Axes.Add(dbAxis);

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

        int radius = Math.Min(
            envelope.Length / 2,
            Math.Max(1, (int)Math.Round(measurement.SampleRate * 0.025)));
        double minMilliseconds = -radius * 1000.0 / measurement.SampleRate;
        double maxMilliseconds = radius * 1000.0 / measurement.SampleRate;

        var series = new LineSeries
        {
            Color = OxyColor.FromRgb(255, 210, 80),
            StrokeThickness = 2
        };
        int step = Math.Max(1, radius * 2 / 600);
        double maxDeb = -10000;
        double minDeb = +10000;
        for (int offset = -radius; offset <= radius; offset += step)
        {
            int index = WrapIndex(measurement.EnvelopePeakIndex + offset, envelope.Length);
            double milliseconds = offset * 1000.0 / measurement.SampleRate;
            double relativeAmplitude = envelope[index] / measurement.EnvelopePeak;
            double decibels = DataHelper.AmplitudeToDecibels(relativeAmplitude);
            series.Points.Add(new DataPoint(milliseconds, Math.Max(-80, decibels)));
            maxDeb = Math.Max(maxDeb, decibels);
            minDeb = Math.Min(minDeb, decibels);
        }

        var model = CreatePreviewPlotModel("Envelope Around Peak");
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            AbsoluteMinimum = minMilliseconds,
            AbsoluteMaximum = maxMilliseconds,
            Minimum = minMilliseconds,
            Maximum = maxMilliseconds,
            MajorStep = 25,
            MajorGridlineColor = OxyColor.FromRgb(55, 62, 78),
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineColor = OxyColor.FromRgb(48, 54, 70),
            MinorGridlineStyle = LineStyle.Dot,
            TextColor = OxyColors.White,
            TicklineColor = OxyColors.White,
            Title = "ms from peak"
        });
        var dbAxis = CreateDecibelAxis();
        dbAxis.AbsoluteMaximum = maxDeb + 10;
        dbAxis.AbsoluteMinimum = minDeb - 10;
        dbAxis.Maximum = maxDeb + 2;
        dbAxis.Minimum = minDeb - 2;
        model.Axes.Add(dbAxis);

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
        AppendStatusText(text, UiPalette.TextSecondarySoft);
    }

    private void SetMeasurementResultStatus()
    {
        string confidence = FormatConfidence(measurement.ConfidenceDecibels);
        Color confidenceColor = GetConfidenceColor(confidence);

        statusTextBox.Clear();
        AppendDelayTable();
        AppendStatusText("Signal Quality: ", UiPalette.TextPrimarySoft);
        AppendStatusText(
            $"{confidence} ({measurement.ConfidenceDecibels:0.0} dB)\r\n",
            confidenceColor);
        statusTextBox.SelectionStart = 0;
        statusTextBox.SelectionLength = 0;
    }

    private void AppendDelayTable()
    {
        double firstArrivalMeters =
            Math.Abs(measurement.FirstArrivalDelayMilliseconds) * SpeedOfSoundAt20C / 1000.0;
        double strongestMeters =
            Math.Abs(measurement.StrongestDelayMilliseconds) * SpeedOfSoundAt20C / 1000.0;

        AppendStatusText(
            FormatDelayTableLine("Measured delay:", "First Arrival", "Strongest Peak") + "\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
        AppendStatusText(
            FormatDelayTableLine(
                "ms",
                $"{measurement.FirstArrivalDelayMilliseconds:0.000}",
                $"{measurement.StrongestDelayMilliseconds:0.000}") + "\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
        AppendStatusText(
            FormatDelayTableLine(
                "meters (20\u00B0C)", // u00B0 degree char
                $"{firstArrivalMeters:0.000}",
                $"{strongestMeters:0.000}") + "\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
        AppendStatusText(
            FormatDelayTableLine(
                "samples",
                $"{measurement.FirstArrivalPeakSample:0.0}",
                $"{measurement.StrongestPeakSample:0.0}") + "\r\n\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
    }

    private static string FormatDelayTableLine(
        string label,
        string firstArrival,
        string strongestPeak) =>
        label.PadRight(DelayTableFirstColumn) +
        firstArrival.PadRight(DelayTableSecondColumn - DelayTableFirstColumn) +
        strongestPeak;

    private void AppendStatusText(string text, Color color, Font? font = null)
    {
        statusTextBox.SelectionStart = statusTextBox.TextLength;
        statusTextBox.SelectionLength = 0;
        statusTextBox.SelectionColor = color;
        statusTextBox.SelectionFont = font ?? statusTextBox.Font;
        statusTextBox.AppendText(text);
        statusTextBox.SelectionFont = statusTextBox.Font;
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

        int lineStart = statusTextBox.GetFirstCharIndexFromLine(line);
        int column = Math.Max(0, index - lineStart);
        value = column >= DelayTableSecondColumn
            ? GetDelayTableValue(statusTextBox.Lines[line], DelayTableSecondColumn)
            : column >= DelayTableFirstColumn
                ? GetDelayTableValue(statusTextBox.Lines[line], DelayTableFirstColumn)
                : string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetDelayTableValue(string line, int startColumn)
    {
        if (line.Length <= startColumn)
        {
            return string.Empty;
        }

        int endColumn = startColumn == DelayTableFirstColumn
            ? Math.Min(DelayTableSecondColumn, line.Length)
            : line.Length;
        return line[startColumn..endColumn].Trim();
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
            "Excellent" => UiPalette.SuccessGreen,
            "Good" => UiPalette.SuccessGreenSoft,
            "Fair" => UiPalette.WarningAmber,
            _ => UiPalette.ErrorSoft
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

    private static decimal ClampDecimal(double value, NumericUpDown numeric)
    {
        decimal decimalValue = (decimal)value;
        return Math.Min(numeric.Maximum, Math.Max(numeric.Minimum, decimalValue));
    }

    private static string FormatWaveChannel(int offset) =>
        offset switch
        {
            0 => "1: Left",
            1 => "2: Right",
            _ => $"Channel {offset + 1}"
        };

    private static string FormatAsioChannel(
        IReadOnlyList<AsioChannelInfo> channels,
        int offset)
    {
        AsioChannelInfo? channel = channels.FirstOrDefault(candidate => candidate.Offset == offset);
        return channel?.ToString() ?? $"Channel {offset + 1}";
    }

    private static string GetWavePlaybackDeviceName(int deviceNumber) =>
        AudioDeviceCatalog.GetPlaybackDevices()
            .FirstOrDefault(candidate => candidate.DeviceNumber == deviceNumber)
            ?.Name ?? "Default playback device";

    private static string GetWaveRecordingDeviceName(int deviceNumber) =>
        AudioDeviceCatalog.GetRecordingDevices()
            .FirstOrDefault(candidate => candidate.DeviceNumber == deviceNumber)
            ?.Name ?? "Default recording device";

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
