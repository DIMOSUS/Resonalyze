using System.Numerics;
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
    private readonly TimeAlignmentOptions options;
    private readonly ExpSweepMeasurement measurement;
    private readonly Action saveSettings;
    private readonly Panel panel;
    private readonly Label sourceSummaryLabel;
    private readonly CheckBox bandpassCheckBox;
    private readonly DarkNumericUpDown bandpassCenterNumeric;
    private readonly DarkNumericUpDown bandpassPassOctavesNumeric;
    private readonly DarkNumericUpDown bandpassFadeOctavesNumeric;
    private readonly PlotView bandpassPlotView;
    private readonly PlotView envelopePlotView;
    private readonly RichTextBox statusTextBox;
    private readonly Font resultTableFont;
    private bool disposed;

    public TimeAlignmentPanelController(
        Form owner,
        TimeAlignmentOptions options,
        ExpSweepMeasurement measurement,
        Action saveSettings)
    {
        this.owner = owner;
        this.options = options;
        this.measurement = measurement;
        this.saveSettings = saveSettings;
        resultTableFont = new Font(
            FontFamily.GenericMonospace,
            owner.Font.Size + 4.0f,
            FontStyle.Bold);

        (
            panel,
            sourceSummaryLabel,
            bandpassCheckBox,
            bandpassCenterNumeric,
            bandpassPassOctavesNumeric,
            bandpassFadeOctavesNumeric,
            bandpassPlotView,
            envelopePlotView,
            statusTextBox) = CreatePanel();

        ApplyOptionsToControls();
        UpdateBandpassPreview();
        RefreshAnalysis();
    }

    public bool InProgress => false;

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
        UpdateBandpassPreview();
        RefreshAnalysis();
    }

    public Task AbortAsync() => Task.CompletedTask;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        resultTableFont.Dispose();
    }

    private (
        Panel Panel,
        Label SourceSummary,
        CheckBox BandpassEnabled,
        DarkNumericUpDown BandpassCenter,
        DarkNumericUpDown BandpassPassOctaves,
        DarkNumericUpDown BandpassFadeOctaves,
        PlotView BandpassPreview,
        PlotView EnvelopePreview,
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
            "Computes delay from the active transfer impulse response.",
            new Point(18, 44));
        var help = UiStyle.CreateLabel(
            "What it is for\r\n" +
            "Measures arrival time from the currently active loopback-based measurement record.\r\n\r\n" +
            "How it works\r\n" +
            "Resonalyze analyzes the active transfer IR immediately, optionally applies a bandpass window, then reports both First Arrival and Strongest Peak.\r\n\r\n" +
            "Source selection\r\n" +
            "This mode requires a transfer IR recorded with loopback enabled.",
            new Point(560, 24),
            UiPalette.TextSecondaryAlt,
            owner.Font,
            autoSize: false);
        help.Size = new Size(520, 150);

        Label sourceSummary = UiStyle.CreateLabel(
            "Source: waiting for an impulse response.",
            new Point(18, 78),
            UiPalette.TextHighlight,
            owner.Font,
            autoSize: false);
        sourceSummary.Size = new Size(520, 40);

        CheckBox bandpassEnabled = UiStyle.CreateDarkCheckBox("Use bandpass window", new Point(18, 127));
        DarkNumericUpDown bandpassCenter =
            UiStyle.CreateDarkNumericUpDown(new Point(180, 157), new Size(120, 23), 20, 20_000, 1000, 10);
        DarkNumericUpDown bandpassPassOctaves =
            UiStyle.CreateDarkNumericUpDown(new Point(180, 191), new Size(120, 23), 0, 8, 1, 0.1M);
        DarkNumericUpDown bandpassFadeOctaves =
            UiStyle.CreateDarkNumericUpDown(new Point(180, 225), new Size(120, 23), 0, 8, 0.5M, 0.1M);
        PlotView bandpassPreview = UiStyle.CreateDarkPreviewPlotView(new Point(18, 263), new Size(520, 200));
        PlotView envelopePreview = UiStyle.CreateDarkPreviewPlotView(new Point(560, 380), new Size(520, 300));
        StatusRichTextBox status = new()
        {
            BackColor = UiPalette.PlotSurfaceMuted,
            BorderStyle = BorderStyle.None,
            DetectUrls = false,
            ForeColor = UiPalette.TextSecondarySoft,
            Font = new Font(owner.Font.FontFamily, 11, FontStyle.Bold),
            Location = new Point(560, 180),
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.None,
            Size = new Size(560, 200),
            Text = "Run a loopback measurement or load an impulse response file with transfer IR."
        };
        status.UseHandCursorAt = point => TryGetCopyableStatusLine(point, out _);
        status.MouseClick += StatusTextBoxMouseClick;

        newPanel.Controls.AddRange(
        [
            title,
            description,
            help,
            sourceSummary,
            bandpassEnabled,
            UiStyle.CreateLabel("Center frequency, Hz", new Point(18, 161), UiPalette.TextHighlight, owner.Font),
            bandpassCenter,
            UiStyle.CreateLabel("Pass width, oct", new Point(18, 195), UiPalette.TextHighlight, owner.Font),
            bandpassPassOctaves,
            UiStyle.CreateLabel("Fade width, oct", new Point(18, 229), UiPalette.TextHighlight, owner.Font),
            bandpassFadeOctaves,
            bandpassPreview,
            envelopePreview,
            status
        ]);
        ScaleRuntimeControlTree(newPanel);
        owner.Controls.Add(newPanel);
        newPanel.BringToFront();

        bandpassEnabled.CheckedChanged += (_, _) => ApplyBandpassOptionChange();
        bandpassCenter.ValueChanged += (_, _) => ApplyBandpassOptionChange();
        bandpassPassOctaves.ValueChanged += (_, _) => ApplyBandpassOptionChange();
        bandpassFadeOctaves.ValueChanged += (_, _) => ApplyBandpassOptionChange();

        return (
            newPanel,
            sourceSummary,
            bandpassEnabled,
            bandpassCenter,
            bandpassPassOctaves,
            bandpassFadeOctaves,
            bandpassPreview,
            envelopePreview,
            status);
    }

    private void ApplyBandpassOptionChange()
    {
        UpdateOptionsFromControls();
        UpdateBandpassPreview();
        RefreshAnalysis();
        saveSettings();
    }

    private void RefreshAnalysis()
    {
        sourceSummaryLabel.Text = CreateSourceSummary();

        if (!TryGetSourceImpulseResponse(
            out double[]? impulseResponse,
            out bool wrapPeakPositions,
            out string noDataMessage))
        {
            SetStatusText(noDataMessage);
            ClearEnvelopePreview();
            return;
        }

        try
        {
            TimeAlignmentAnalysisResult result = TimeAlignmentAnalysis.Analyze(
                impulseResponse!,
                measurement.SampleRate,
                CreateAnalysisOptions(wrapPeakPositions));
            SetMeasurementResultStatus(result);
            UpdateEnvelopePreview(result);
        }
        catch (Exception exception)
        {
            SetStatusText(exception.Message);
            ClearEnvelopePreview();
        }
    }

    private bool TryGetSourceImpulseResponse(
        out double[]? impulseResponse,
        out bool wrapPeakPositions,
        out string message)
    {
        if (measurement.TransferImpulseResponse is { Length: > 0 } transferImpulseResponse)
        {
            impulseResponse = Array.ConvertAll(transferImpulseResponse, sample => sample.Real);
            wrapPeakPositions = true;
            message = string.Empty;
            return true;
        }

        if (measurement.SweepDeconvolutionImpulseResponse is { Length: > 0 })
        {
            impulseResponse = null;
            wrapPeakPositions = false;
            message =
                "This record was captured without loopback.\r\n" +
                "Time Alignment requires a transfer IR.\r\n" +
                "Run a new measurement with loopback enabled or load a file that contains transfer IR.";
            return false;
        }

        impulseResponse = null;
        wrapPeakPositions = false;
        message =
            "No impulse response is loaded.\r\n" +
            "Run a loopback measurement or load an impulse response file with transfer IR.";
        return false;
    }

    private string CreateSourceSummary()
    {
        if (measurement.TransferImpulseResponse is { Length: > 0 })
        {
            return $"Source: Transfer IR, {measurement.SampleRate} Hz.";
        }

        if (measurement.SweepDeconvolutionImpulseResponse is { Length: > 0 })
        {
            return
                $"Source: Sweep deconvolution IR only, {measurement.SampleRate} Hz.\r\n" +
                "Loopback was not recorded for this entry.";
        }

        return "Source: waiting for a loopback measurement or file with transfer IR.";
    }

    private TimeAlignmentAnalysisOptions CreateAnalysisOptions(bool wrapPeakPositions) =>
        new()
        {
            UseBandpassWindow = options.UseBandpassWindow,
            BandpassCenterHz = options.BandpassCenterHz,
            BandpassPassOctaves = options.BandpassPassOctaves,
            BandpassFadeOctaves = options.BandpassFadeOctaves,
            FirstPeakThresholdBelowMaxDb = options.FirstPeakThresholdBelowMaxDb,
            FirstPeakMinimumSnrDb = options.FirstPeakMinimumSnrDb,
            PeakSearchWindowMilliseconds = options.PeakSearchWindowMilliseconds,
            WrapPeakPositions = wrapPeakPositions
        };

    private void ApplyOptionsToControls()
    {
        bandpassCheckBox.Checked = options.UseBandpassWindow;
        bandpassCenterNumeric.Value =
            ClampDecimal(options.BandpassCenterHz, bandpassCenterNumeric);
        bandpassPassOctavesNumeric.Value =
            ClampDecimal(options.BandpassPassOctaves, bandpassPassOctavesNumeric);
        bandpassFadeOctavesNumeric.Value =
            ClampDecimal(options.BandpassFadeOctaves, bandpassFadeOctavesNumeric);
        UpdateBandpassControlStates();
    }

    private void UpdateOptionsFromControls()
    {
        options.UseBandpassWindow = bandpassCheckBox.Checked;
        options.BandpassCenterHz = (double)bandpassCenterNumeric.Value;
        options.BandpassPassOctaves = (double)bandpassPassOctavesNumeric.Value;
        options.BandpassFadeOctaves = (double)bandpassFadeOctavesNumeric.Value;
        UpdateBandpassControlStates();
    }

    private void UpdateBandpassControlStates()
    {
        bandpassCenterNumeric.Enabled = bandpassCheckBox.Checked;
        bandpassPassOctavesNumeric.Enabled = bandpassCheckBox.Checked;
        bandpassFadeOctavesNumeric.Enabled = bandpassCheckBox.Checked;
    }

    private void UpdateBandpassPreview()
    {
        bandpassPlotView.Visible = bandpassCheckBox.Checked;
        if (!bandpassCheckBox.Checked)
        {
            bandpassPlotView.Model = null;
            return;
        }

        double maxFrequency = Math.Min(20_000, measurement.SampleRate > 0
            ? measurement.SampleRate * 0.5
            : 20_000);
        var model = CreatePreviewPlotModel("Bandpass Window");
        model.Axes.Add(new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 20,
            Maximum = maxFrequency,
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
        double maxLog = Math.Log10(maxFrequency);
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

    private void UpdateEnvelopePreview(TimeAlignmentAnalysisResult result)
    {
        double[] envelope = result.EnvelopeSamples;
        if (envelope.Length == 0 || result.EnvelopePeak <= 0 || measurement.SampleRate <= 0)
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
        double maxDb = -10000;
        double minDb = +10000;
        for (int offset = -radius; offset <= radius; offset += step)
        {
            int index = WrapIndex(result.EnvelopePeakIndex + offset, envelope.Length);
            double milliseconds = offset * 1000.0 / measurement.SampleRate;
            double relativeAmplitude = envelope[index] / result.EnvelopePeak;
            double decibels = DataHelper.AmplitudeToDecibels(relativeAmplitude);
            double clampedDecibels = Math.Max(-80, decibels);
            series.Points.Add(new DataPoint(milliseconds, clampedDecibels));
            maxDb = Math.Max(maxDb, clampedDecibels);
            minDb = Math.Min(minDb, clampedDecibels);
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
        dbAxis.AbsoluteMaximum = maxDb + 10;
        dbAxis.AbsoluteMinimum = minDb - 10;
        dbAxis.Maximum = maxDb + 2;
        dbAxis.Minimum = minDb - 2;
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
            result.StrongestEnvelopePeakIndex - result.EnvelopePeakIndex,
            envelope.Length);
        double strongestMilliseconds =
            strongestOffset * 1000.0 / measurement.SampleRate;
        if (Math.Abs(strongestMilliseconds) <= 50 && Math.Abs(strongestOffset) > 1)
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

    private void SetStatusText(string text)
    {
        statusTextBox.Clear();
        AppendStatusText(text, UiPalette.TextSecondarySoft);
    }

    private void SetMeasurementResultStatus(TimeAlignmentAnalysisResult result)
    {
        string confidence = FormatConfidence(result.ConfidenceDecibels);
        Color confidenceColor = GetConfidenceColor(confidence);
        InputLevelMeterSnapshot levels = measurement.CurrentLevels;

        statusTextBox.Clear();
        AppendDelayTable(result);
        AppendStatusText("Signal Quality: ", UiPalette.TextPrimarySoft);
        AppendStatusText(
            $"{confidence} ({result.ConfidenceDecibels:0.0} dB)\r\n",
            confidenceColor);
        AppendLevelLine("Mic", levels.Microphone);
        AppendLevelLine("Loopback", levels.Loopback);
        statusTextBox.SelectionStart = 0;
        statusTextBox.SelectionLength = 0;
    }

    private void AppendDelayTable(TimeAlignmentAnalysisResult result)
    {
        double firstArrivalMeters =
            Math.Abs(result.FirstArrivalDelayMilliseconds) * SpeedOfSoundAt20C / 1000.0;
        double strongestMeters =
            Math.Abs(result.StrongestDelayMilliseconds) * SpeedOfSoundAt20C / 1000.0;

        AppendStatusText(
            FormatDelayTableLine("Measured delay:", "First Arrival", "Strongest Peak") + "\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
        AppendStatusText(
            FormatDelayTableLine(
                "ms",
                $"{result.FirstArrivalDelayMilliseconds:0.000}",
                $"{result.StrongestDelayMilliseconds:0.000}") + "\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
        AppendStatusText(
            FormatDelayTableLine(
                "meters (20\u00B0C)",
                $"{firstArrivalMeters:0.000}",
                $"{strongestMeters:0.000}") + "\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
        AppendStatusText(
            FormatDelayTableLine(
                "samples",
                $"{result.FirstArrivalPeakSample:0.0}",
                $"{result.StrongestPeakSample:0.0}") + "\r\n\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
    }

    private void AppendLevelLine(string label, InputLevelMeterEntry entry)
    {
        if (!entry.Available)
        {
            AppendStatusText($"{label}: unavailable\r\n", UiPalette.TextSecondarySoft);
            return;
        }

        AppendStatusText(
            $"{label}: peak {entry.PeakDbFs:0.0} dBFS, RMS {entry.RmsDbFs:0.0} dBFS",
            UiPalette.TextPrimarySoft);
        if (entry.Clipped)
        {
            AppendStatusText(" CLIP", UiPalette.ErrorSoft);
        }
        else if (entry.FullScaleReference)
        {
            AppendStatusText(" FULL SCALE", UiPalette.TextSecondarySoft);
        }

        AppendStatusText("\r\n", UiPalette.TextPrimarySoft);
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
        if (line is < 1 or > 3 || line >= statusTextBox.Lines.Length)
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

    private static decimal ClampDecimal(double value, DarkNumericUpDown numeric)
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
