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
    private readonly Func<string?> getImpulseResponseFileName;
    private readonly TimeAlignmentPanel panel;
    private readonly Label sourceSummaryLabel;
    private readonly CheckBox bandpassCheckBox;
    private readonly DarkNumericUpDown bandpassCenterNumeric;
    private readonly DarkNumericUpDown bandpassPassOctavesNumeric;
    private readonly DarkNumericUpDown bandpassFadeOctavesNumeric;
    private readonly PlotView bandpassPlotView;
    private readonly PlotView envelopePlotView;
    private readonly StatusRichTextBox statusTextBox;
    private readonly Font resultTableFont;
    private bool disposed;

    public TimeAlignmentPanelController(
        Form owner,
        TimeAlignmentPanel panel,
        TimeAlignmentOptions options,
        ExpSweepMeasurement measurement,
        Action saveSettings,
        Func<string?> getImpulseResponseFileName)
    {
        this.owner = owner;
        this.panel = panel;
        this.options = options;
        this.measurement = measurement;
        this.saveSettings = saveSettings;
        this.getImpulseResponseFileName = getImpulseResponseFileName;
        resultTableFont = new Font(
            FontFamily.GenericMonospace,
            owner.Font.Size + 4.0f,
            FontStyle.Bold);

        sourceSummaryLabel = panel.SourceSummaryLabel;
        bandpassCheckBox = panel.BandpassCheckBox;
        bandpassCenterNumeric = panel.BandpassCenterNumeric;
        bandpassPassOctavesNumeric = panel.BandpassPassOctavesNumeric;
        bandpassFadeOctavesNumeric = panel.BandpassFadeOctavesNumeric;
        bandpassPlotView = panel.BandpassPlotView;
        envelopePlotView = panel.EnvelopePlotView;
        statusTextBox = panel.StatusTextBox;
        statusTextBox.UseHandCursorAt = point => TryGetCopyableStatusLine(point, out _);
        statusTextBox.MouseClick += StatusTextBoxMouseClick;

        ApplyOptionsToControls();
        WireEvents();
        UpdateBandpassPreview();
        RefreshAnalysis();
    }

    public bool InProgress => false;

    public void SetLayoutBounds(Rectangle bounds)
    {
        panel.Bounds = bounds;
    }

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

    private void WireEvents()
    {
        bandpassCheckBox.CheckedChanged += (_, _) => ApplyBandpassOptionChange();
        bandpassCenterNumeric.ValueChanged += (_, _) => ApplyBandpassOptionChange();
        bandpassPassOctavesNumeric.ValueChanged += (_, _) => ApplyBandpassOptionChange();
        bandpassFadeOctavesNumeric.ValueChanged += (_, _) => ApplyBandpassOptionChange();
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
            string source = getImpulseResponseFileName() ?? "Transfer IR";
            return $"Source: {source}, {measurement.SampleRate} Hz.";
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
        bool addCurve = bandpassCheckBox.Checked;
        PlotModel model = CreateBandpassPreviewModel(addCurve);
        bandpassPlotView.Model = model;
    }

    private PlotModel CreateBandpassPreviewModel(bool addCurve)
    {
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
        dbAxis.AbsoluteMinimum = -80;
        dbAxis.AbsoluteMaximum = 0;
        model.Axes.Add(dbAxis);

        if (!addCurve)
        {
            return model;
        }

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
        return model;
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
    }

    private void ClearEnvelopePreview()
    {
        envelopePlotView.Model = CreateEmptyEnvelopePreviewModel();
    }

    private PlotModel CreateEmptyEnvelopePreviewModel()
    {
        var model = CreatePreviewPlotModel("Envelope Around Peak");
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            AbsoluteMinimum = -50,
            AbsoluteMaximum = 50,
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
        var dbAxis = CreateDecibelAxis();
        dbAxis.AbsoluteMaximum = 0;
        dbAxis.AbsoluteMinimum = -80;
        dbAxis.Maximum = 0;
        dbAxis.Minimum = -80;
        model.Axes.Add(dbAxis);
        return model;
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
