using System.Numerics;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze;

internal sealed class TimeAlignmentPanelController : IDisposable
{

    private readonly Form owner;
    private readonly TimeAlignmentOptions options;
    private readonly ExpSweepMeasurement measurement;
    private readonly Action saveSettings;
    private readonly Func<string?> getImpulseResponseFileName;
    private readonly Func<TimeAlignmentCompareMeasurement?> getCompareMeasurement;
    private readonly TimeAlignmentPanel panel;
    private readonly Label sourceSummaryLabel;
    private readonly Label compareLabel;
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
        Func<string?> getImpulseResponseFileName,
        Func<TimeAlignmentCompareMeasurement?> getCompareMeasurement)
    {
        this.owner = owner;
        this.panel = panel;
        this.options = options;
        this.measurement = measurement;
        this.saveSettings = saveSettings;
        this.getImpulseResponseFileName = getImpulseResponseFileName;
        this.getCompareMeasurement = getCompareMeasurement;
        resultTableFont = new Font(
            FontFamily.GenericMonospace,
            owner.Font.Size + 4.0f,
            FontStyle.Bold);

        sourceSummaryLabel = panel.SourceSummaryLabel;
        compareLabel = panel.CompareLabel;
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
        compareLabel.Text = CreateCompareSummary();

        if (!TryGetMainSource(out TimeAlignmentAnalysisSource mainSource, out string noDataMessage))
        {
            SetStatusText(noDataMessage);
            ClearEnvelopePreview();
            return;
        }

        try
        {
            TimeAlignmentAnalysisResult mainResult = TimeAlignmentAnalysis.Analyze(
                mainSource.TransferImpulseResponse,
                mainSource.SampleRate,
                CreateAnalysisOptions(wrapPeakPositions: true),
                mainSource.TransferCoherence);
            if (!mainResult.IsValid)
            {
                SetStatusText(
                    "No signal in the analysis band.\r\n" +
                    "The transfer IR carries no energy inside the current " +
                    "band-pass window — widen or move the band, or check " +
                    "that the measurement actually captured the driver.");
                ClearEnvelopePreview();
                return;
            }

            TimeAlignmentCompareAnalysis? compareAnalysis =
                AnalyzeCompare(mainSource, out string? compareWarning);
            SetMeasurementResultStatus(
                mainSource,
                mainResult,
                compareAnalysis,
                compareWarning);
            UpdateEnvelopePreview(
                mainResult,
                mainSource.SampleRate,
                compareAnalysis?.Result);
        }
        catch (Exception exception)
        {
            SetStatusText(exception.Message);
            ClearEnvelopePreview();
        }
    }

    private bool TryGetMainSource(
        out TimeAlignmentAnalysisSource source,
        out string message)
    {
        if (measurement.TransferImpulseResponse is { Length: > 0 } transferImpulseResponse)
        {
            source = new TimeAlignmentAnalysisSource(
                "Main",
                getImpulseResponseFileName() ?? "Transfer IR",
                measurement.SampleRate,
                measurement.Bits,
                measurement.Octaves,
                measurement.Sweep?.ComputedDuration ?? 0.0,
                measurement.PlaybackChannel,
                measurement.MeasurementMode,
                Array.ConvertAll(transferImpulseResponse, sample => sample.Real),
                measurement.TransferCoherence,
                measurement.CurrentLevels);
            message = string.Empty;
            return true;
        }

        if (measurement.SweepDeconvolutionImpulseResponse is { Length: > 0 })
        {
            source = default;
            message =
                "This record was captured without loopback.\r\n" +
                "Time Alignment requires a transfer IR.\r\n" +
                "Run a new measurement with loopback enabled or load a file that contains transfer IR.";
            return false;
        }

        source = default;
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
            return $"Source: {source}, {measurement.SampleRate} Hz, {measurement.Bits} bit.";
        }

        if (measurement.SweepDeconvolutionImpulseResponse is { Length: > 0 })
        {
            return
                $"Source: Sweep deconvolution IR only, {measurement.SampleRate} Hz, {measurement.Bits} bit.\r\n" +
                "Loopback was not recorded for this entry.";
        }

        return "Source: waiting for a loopback measurement or file with transfer IR.";
    }

    private string CreateCompareSummary()
    {
        TimeAlignmentCompareMeasurement? compare = getCompareMeasurement();
        if (compare == null)
        {
            return "Compare: -";
        }

        MeasurementHistorySnapshot snapshot = compare.Value.Snapshot;
        return $"Compare: {compare.Value.DisplayName}, {snapshot.SampleRate} Hz, {snapshot.Bits} bit.";
    }

    private static TimeAlignmentAnalysisSource CreateCompareSource(
        TimeAlignmentCompareMeasurement compare,
        MeasurementHistorySnapshot snapshot) =>
        new(
            "Compare",
            compare.DisplayName,
            snapshot.SampleRate,
            snapshot.Bits,
            snapshot.Octaves,
            snapshot.SweepDurationSeconds,
            snapshot.PlayChannel,
            snapshot.MeasurementMode,
            Array.ConvertAll(
                snapshot.TransferImpulseResponse!,
                sample => sample.Real),
            snapshot.TransferCoherence,
            snapshot.MeterSnapshot);

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
        var frequencyAxis = new LogarithmicAxis
        {
            Position = AxisPosition.Bottom,
            Minimum = 20,
            Maximum = maxFrequency,
            AbsoluteMaximum = 20_000,
            AbsoluteMinimum = 20
        };
        ApplyPreviewAxisStyle(frequencyAxis);
        model.Axes.Add(frequencyAxis);
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

    private TimeAlignmentCompareAnalysis? AnalyzeCompare(
        TimeAlignmentAnalysisSource mainSource,
        out string? warning)
    {
        warning = null;
        TimeAlignmentCompareMeasurement? compare = getCompareMeasurement();
        if (compare == null)
        {
            return null;
        }

        TimeAlignmentCompareMeasurement compareValue = compare.Value;
        MeasurementHistorySnapshot snapshot = compareValue.Snapshot;
        if (snapshot.SampleRate != mainSource.SampleRate)
        {
            warning =
                $"Sample rate mismatch: Main is {mainSource.SampleRate} Hz, " +
                $"Compare is {snapshot.SampleRate} Hz.";
            return null;
        }

        if (snapshot.TransferImpulseResponse is not { Length: > 0 })
        {
            warning = "Compare impulse response has no transfer IR.";
            return null;
        }

        try
        {
            TimeAlignmentAnalysisSource compareSource =
                CreateCompareSource(compareValue, snapshot);
            TimeAlignmentAnalysisResult compareResult = TimeAlignmentAnalysis.Analyze(
                compareSource.TransferImpulseResponse,
                compareSource.SampleRate,
                CreateAnalysisOptions(wrapPeakPositions: true),
                compareSource.TransferCoherence);
            if (!compareResult.IsValid)
            {
                warning = "Compare: no signal in the analysis band.";
                return null;
            }

            return new TimeAlignmentCompareAnalysis(compareSource, compareResult);
        }
        catch (Exception exception)
        {
            warning = exception.Message;
            return null;
        }
    }

    private void UpdateEnvelopePreview(
        TimeAlignmentAnalysisResult result,
        int sampleRate,
        TimeAlignmentAnalysisResult? compareResult = null)
    {
        double[] envelope = result.EnvelopeSamples;
        if (envelope.Length == 0 || result.EnvelopePeak <= 0 || sampleRate <= 0)
        {
            ClearEnvelopePreview();
            return;
        }

        int radius = Math.Min(
            envelope.Length / 2,
            Math.Max(1, (int)Math.Round(sampleRate * 0.025)));
        double minMilliseconds = -radius * 1000.0 / sampleRate;
        double maxMilliseconds = radius * 1000.0 / sampleRate;
        double compareOffsetMilliseconds = 0.0;
        if (compareResult.HasValue)
        {
            compareOffsetMilliseconds =
                compareResult.Value.FirstArrivalDelayMilliseconds -
                result.FirstArrivalDelayMilliseconds;
            minMilliseconds = Math.Min(
                minMilliseconds,
                compareOffsetMilliseconds - radius * 1000.0 / sampleRate);
            maxMilliseconds = Math.Max(
                maxMilliseconds,
                compareOffsetMilliseconds + radius * 1000.0 / sampleRate);
        }

        int step = Math.Max(1, radius * 2 / 600);
        LineSeries mainSeries = CreateEnvelopeSeries(
            result,
            sampleRate,
            radius,
            step,
            xOffsetMilliseconds: 0.0,
            OxyColor.FromRgb(255, 210, 80),
            strokeThickness: 2,
            out double maxDb,
            out double minDb);

        var model = CreatePreviewPlotModel("Envelope Around Peak");
        model.Axes.Add(CreateMillisecondsAxis(minMilliseconds, maxMilliseconds));
        var dbAxis = CreateDecibelAxis();
        dbAxis.AbsoluteMaximum = maxDb + 30;
        dbAxis.AbsoluteMinimum = minDb - 10;
        dbAxis.Maximum = maxDb + 2;
        dbAxis.Minimum = minDb - 2;
        model.Axes.Add(dbAxis);

        model.Series.Add(mainSeries);
        if (compareResult.HasValue)
        {
            LineSeries compareSeries = CreateEnvelopeSeries(
                compareResult.Value,
                sampleRate,
                radius,
                step,
                compareOffsetMilliseconds,
                OxyColor.FromArgb(155, 80, 210, 255),
                strokeThickness: 1.75,
                out double compareMaxDb,
                out double compareMinDb);
            maxDb = Math.Max(maxDb, compareMaxDb);
            minDb = Math.Min(minDb, compareMinDb);
            dbAxis.AbsoluteMaximum = maxDb + 30;
            dbAxis.AbsoluteMinimum = minDb - 10;
            dbAxis.Maximum = maxDb + 2;
            dbAxis.Minimum = minDb - 2;
            model.Series.Add(compareSeries);
        }

        if (compareResult.HasValue)
        {
            AddComparePeakMarkers(
                model,
                result,
                compareResult.Value,
                compareOffsetMilliseconds);
        }
        else
        {
            AddMainPeakMarkers(model, result);
        }
        envelopePlotView.Model = model;
    }

    private static LineSeries CreateEnvelopeSeries(
        TimeAlignmentAnalysisResult result,
        int sampleRate,
        int radius,
        int step,
        double xOffsetMilliseconds,
        OxyColor color,
        double strokeThickness,
        out double maxDb,
        out double minDb)
    {
        double[] envelope = result.EnvelopeSamples;
        maxDb = -10000;
        minDb = +10000;
        var series = new LineSeries
        {
            Color = color,
            StrokeThickness = strokeThickness
        };
        double localMaxDb = maxDb;
        double localMinDb = minDb;
        void AddPoint(int offset)
        {
            int index = WrapIndex(result.EnvelopePeakIndex + offset, envelope.Length);
            double milliseconds = offset * 1000.0 / sampleRate + xOffsetMilliseconds;
            double relativeAmplitude = envelope[index] / result.EnvelopePeak;
            double decibels = DataHelper.AmplitudeToDecibels(relativeAmplitude);
            double clampedDecibels = Math.Max(-80, decibels);
            series.Points.Add(new DataPoint(milliseconds, clampedDecibels));
            localMaxDb = Math.Max(localMaxDb, clampedDecibels);
            localMinDb = Math.Min(localMinDb, clampedDecibels);
        }

        // Min/max pooling per decimation bucket: sampling every Nth value would
        // skip a narrow reflection peak entirely, hiding the very feature the
        // markers point at.
        for (int bucketStart = -radius; bucketStart <= radius; bucketStart += step)
        {
            int bucketEnd = Math.Min(radius, bucketStart + step - 1);
            int minOffset = bucketStart;
            int maxOffset = bucketStart;
            double minValue = double.PositiveInfinity;
            double maxValue = double.NegativeInfinity;
            for (int offset = bucketStart; offset <= bucketEnd; offset++)
            {
                double value = envelope[
                    WrapIndex(result.EnvelopePeakIndex + offset, envelope.Length)];
                if (value < minValue)
                {
                    minValue = value;
                    minOffset = offset;
                }
                if (value > maxValue)
                {
                    maxValue = value;
                    maxOffset = offset;
                }
            }

            AddPoint(Math.Min(minOffset, maxOffset));
            if (minOffset != maxOffset)
            {
                AddPoint(Math.Max(minOffset, maxOffset));
            }
        }

        maxDb = localMaxDb;
        minDb = localMinDb;
        return series;
    }

    private static void AddMainPeakMarkers(
        PlotModel model,
        TimeAlignmentAnalysisResult mainResult)
    {
        double strongestMilliseconds =
            mainResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        AddCalloutMarker(
            model,
            "M First",
            0.0,
            GetPeakMarkerDecibels(mainResult, mainResult.EnvelopePeakIndex),
            OxyColor.FromRgb(255, 96, 96),
            PlotCalloutDirection.LeftUp);
        if (Math.Abs(strongestMilliseconds) > 0.001)
        {
            AddCalloutMarker(
                model,
                "M Peak",
                strongestMilliseconds,
                GetPeakMarkerDecibels(mainResult, mainResult.StrongestEnvelopePeakIndex),
                OxyColor.FromRgb(140, 170, 255),
                PlotCalloutDirection.RightUp);
        }
    }

    private static void AddComparePeakMarkers(
        PlotModel model,
        TimeAlignmentAnalysisResult mainResult,
        TimeAlignmentAnalysisResult compareResult,
        double compareFirstArrivalMilliseconds)
    {
        double mainFirstArrivalDecibels =
            GetPeakMarkerDecibels(mainResult, mainResult.EnvelopePeakIndex);
        double compareFirstArrivalDecibels =
            GetPeakMarkerDecibels(compareResult, compareResult.EnvelopePeakIndex);
        double mainStrongestMilliseconds =
            mainResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        double compareStrongestMilliseconds =
            compareResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        double mainStrongestDecibels =
            GetPeakMarkerDecibels(mainResult, mainResult.StrongestEnvelopePeakIndex);
        double compareStrongestDecibels =
            GetPeakMarkerDecibels(compareResult, compareResult.StrongestEnvelopePeakIndex);

        AddCalloutMarker(
            model,
            "M First",
            0.0,
            mainFirstArrivalDecibels,
            OxyColor.FromRgb(255, 96, 96),
            mainFirstArrivalDecibels >= compareFirstArrivalDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);
        AddCalloutMarker(
            model,
            "C First",
            compareFirstArrivalMilliseconds,
            compareFirstArrivalDecibels,
            OxyColor.FromArgb(145, 255, 96, 96),
            compareFirstArrivalDecibels > mainFirstArrivalDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);

        if (Math.Abs(mainStrongestMilliseconds) > 0.001)
        {
            AddCalloutMarker(
                model,
                "M Peak",
                mainStrongestMilliseconds,
                mainStrongestDecibels,
                OxyColor.FromRgb(140, 170, 255),
                mainStrongestDecibels >= compareStrongestDecibels
                    ? PlotCalloutDirection.RightUp
                    : PlotCalloutDirection.RightDown);
        }

        if (Math.Abs(compareStrongestMilliseconds - compareFirstArrivalMilliseconds) > 0.001)
        {
            AddCalloutMarker(
                model,
                "C Peak",
                compareStrongestMilliseconds,
                compareStrongestDecibels,
                OxyColor.FromArgb(145, 140, 170, 255),
                compareStrongestDecibels > mainStrongestDecibels
                    ? PlotCalloutDirection.RightUp
                    : PlotCalloutDirection.RightDown);
        }
    }

    private static void AddCalloutMarker(
        PlotModel model,
        string label,
        double milliseconds,
        double decibels,
        OxyColor color,
        PlotCalloutDirection direction)
    {
        model.Annotations.Add(new PlotCalloutMarkerAnnotation
        {
            Text = label,
            AnchorPoint = new DataPoint(milliseconds, decibels),
            Color = color,
            Direction = direction,
            Layer = AnnotationLayer.AboveSeries
        });
    }

    private static double GetPeakMarkerDecibels(
        TimeAlignmentAnalysisResult result,
        int peakIndex)
    {
        if ((uint)peakIndex >= (uint)result.EnvelopeSamples.Length ||
            result.EnvelopePeak <= 0)
        {
            return 0.0;
        }

        double relativeAmplitude = result.EnvelopeSamples[peakIndex] / result.EnvelopePeak;
        return Math.Max(-80, DataHelper.AmplitudeToDecibels(relativeAmplitude));
    }

    private void ClearEnvelopePreview()
    {
        envelopePlotView.Model = CreateEmptyEnvelopePreviewModel();
    }

    private PlotModel CreateEmptyEnvelopePreviewModel()
    {
        var model = CreatePreviewPlotModel("Envelope Around Peak");
        model.Axes.Add(CreateMillisecondsAxis(-50, 50));
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
        statusTextBox.BeginUpdate();
        try
        {
            statusTextBox.Clear();
            AppendStatusText(text, UiPalette.TextSecondarySoft);
        }
        finally
        {
            statusTextBox.EndUpdate();
        }
    }

    private void SetMeasurementResultStatus(
        TimeAlignmentAnalysisSource mainSource,
        TimeAlignmentAnalysisResult mainResult,
        TimeAlignmentCompareAnalysis? compareAnalysis,
        string? compareWarning)
    {
        statusTextBox.BeginUpdate();
        try
        {
            statusTextBox.Clear();
            AppendMeasurementResult("Main", mainSource.Levels, mainResult);
            AppendCompareResult(mainResult, compareAnalysis, compareWarning);
            statusTextBox.SelectionStart = 0;
            statusTextBox.SelectionLength = 0;
        }
        finally
        {
            statusTextBox.EndUpdate();
        }
    }

    private void AppendCompareResult(
        TimeAlignmentAnalysisResult mainResult,
        TimeAlignmentCompareAnalysis? compareAnalysis,
        string? warning)
    {
        if (compareAnalysis == null && warning == null)
        {
            return;
        }

        if (warning != null)
        {
            AppendStatusText("\r\nCompare: ", UiPalette.TextPrimarySoft, resultTableFont);
            AppendStatusText(warning + "\r\n", UiPalette.WarningAmber);
            return;
        }

        AppendStatusText("\r\n", UiPalette.TextPrimarySoft);
        // Passing the Main result makes the Compare delay table show each value's delta
        // against Source in parentheses.
        AppendMeasurementResult(
            "Compare",
            compareAnalysis!.Value.Source.Levels,
            compareAnalysis.Value.Result,
            mainResult);
    }

    private void AppendMeasurementResult(
        string title,
        InputLevelMeterSnapshot levels,
        TimeAlignmentAnalysisResult result,
        TimeAlignmentAnalysisResult? reference = null)
    {
        AppendSignalQuality(title, result);
        AppendAlignmentConfidence(result);
        AppendLevelLine("Mic", levels.Microphone);
        AppendLevelLine("Loopback", levels.Loopback);
        AppendSeparator();
        AppendDelayTable(result, reference);
        AppendStrongestPeakHint(result);
    }

    // A subwoofer or any narrowband/modal measurement can leave the strongest peak
    // a room mode or reflection well after the direct sound, so the two columns
    // disagree. Point the reader at the first arrival instead of the misleading
    // strongest peak.
    private void AppendStrongestPeakHint(TimeAlignmentAnalysisResult result)
    {
        if (!result.StrongestPeakIsSeparateArrival)
        {
            return;
        }

        AppendStatusText(
            $"⚠ Strongest peak is ~{result.StrongestPeakSeparationMilliseconds:0.0} ms " +
            "after first arrival — likely a room mode or reflection.\r\n" +
            "Use First Arrival for alignment.\r\n",
            UiPalette.WarningAmber);
    }

    // Two separate figures that a single "quality" number used to conflate:
    // the recording's SNR (strongest envelope peak vs the rest of the record)
    // grades the measurement, while the first-arrival prominence (its level
    // relative to the strongest peak) grades how sharply defined the pick is.
    // A woofer's broad leading edge gives a low prominence on an excellent
    // recording — that is physics, not a bad measurement, and it must not
    // drag the signal grade down.
    private void AppendSignalQuality(string title, TimeAlignmentAnalysisResult result)
    {
        string signalGrade = FormatConfidence(result.SignalToNoiseDecibels);
        AppendStatusText($"{title} Signal: ", UiPalette.TextPrimarySoft);
        AppendStatusText(
            $"{signalGrade} ({result.SignalToNoiseDecibels:0.0} dB SNR)\r\n",
            GetConfidenceColor(signalGrade));

        double prominence = result.FirstArrivalProminenceDecibels;
        AppendStatusText("First arrival: ", UiPalette.TextPrimarySoft);
        if (prominence >= -1.0)
        {
            AppendStatusText(
                "coincides with the strongest peak\r\n",
                UiPalette.SuccessGreen);
            return;
        }

        string hint = prominence <= BroadRiseProminenceDb
            ? " — broad rise, normal for low-frequency drivers"
            : string.Empty;
        Color color = prominence >= BroadRiseProminenceDb
            ? UiPalette.SuccessGreenSoft
            : UiPalette.TextSecondarySoft;
        AppendStatusText(
            $"{prominence:0.0} dB re strongest peak{hint}\r\n",
            color);
    }

    // Below this the first arrival sits far down a slow leading edge; typical
    // for band-limited low-frequency drivers, where the envelope rises over
    // milliseconds before the in-room energy peaks.
    private const double BroadRiseProminenceDb = -12.0;

    // The GCC-PHAT trust for the first arrival: how sharply the whitened correlation
    // located the sub-sample delay. RefinedByPhat=false means the whitened peak was
    // too weak and the envelope parabola set the position, so the figure is the
    // honest "coarse alignment" signal rather than a trustworthy sub-sample number.
    private void AppendAlignmentConfidence(TimeAlignmentAnalysisResult result)
    {
        int percent = (int)Math.Round(
            Math.Clamp(result.FirstArrivalConfidence, 0.0, 1.0) * 100.0);
        string method = result.FirstArrivalRefinedByPhat
            ? "GCC-PHAT"
            : "envelope fallback";
        Color color = !result.FirstArrivalRefinedByPhat
            ? UiPalette.TextSecondarySoft
            : result.FirstArrivalConfidence >= 0.6
                ? UiPalette.SuccessGreen
                : result.FirstArrivalConfidence >= 0.4
                    ? UiPalette.SuccessGreenSoft
                    : UiPalette.WarningAmber;
        AppendStatusText("Alignment: ", UiPalette.TextPrimarySoft);
        AppendStatusText($"{percent}% ({method})\r\n", color);
    }

    private void AppendSeparator()
    {
        AppendStatusText(
            new string('_', 54) + "\r\n",
            UiPalette.TextSecondarySoft,
            resultTableFont);
    }

    // When reference is supplied (the Compare table), each value shows its delta against
    // the Source value in parentheses, e.g. "1.006 (+0.010)".
    private void AppendDelayTable(
        TimeAlignmentAnalysisResult result,
        TimeAlignmentAnalysisResult? reference = null)
    {
        double firstArrivalMeters = DelayMeters(result.FirstArrivalDelayMilliseconds);
        double strongestMeters = DelayMeters(result.StrongestDelayMilliseconds);

        // Header split into segments so the two peak labels carry a light colour accent
        // matching their envelope markers, while keeping the column alignment.
        AppendStatusText(
            "Measured delay:".PadRight(DelayTableText.FirstColumn),
            UiPalette.TextPrimarySoft,
            resultTableFont);
        AppendStatusText(
            "First Arrival".PadRight(DelayTableText.SecondColumn - DelayTableText.FirstColumn),
            UiPalette.TimeAlignmentFirstArrival,
            resultTableFont);
        AppendStatusText(
            "Strongest Peak\r\n",
            UiPalette.TimeAlignmentStrongestPeak,
            resultTableFont);
        AppendStatusText(
            FormatDelayTableLine(
                "ms",
                FormatValueWithDelta(
                    result.FirstArrivalDelayMilliseconds,
                    reference?.FirstArrivalDelayMilliseconds,
                    "0.000"),
                FormatValueWithDelta(
                    result.StrongestDelayMilliseconds,
                    reference?.StrongestDelayMilliseconds,
                    "0.000")) + "\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
        AppendStatusText(
            FormatDelayTableLine(
                "meters (20\u00B0C)",
                FormatValueWithDelta(
                    firstArrivalMeters,
                    reference == null
                        ? null
                        : DelayMeters(reference.Value.FirstArrivalDelayMilliseconds),
                    "0.000"),
                FormatValueWithDelta(
                    strongestMeters,
                    reference == null
                        ? null
                        : DelayMeters(reference.Value.StrongestDelayMilliseconds),
                    "0.000")) + "\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
        AppendStatusText(
            FormatDelayTableLine(
                "samples",
                FormatValueWithDelta(
                    result.FirstArrivalPeakSample,
                    reference?.FirstArrivalPeakSample,
                    "0.0"),
                FormatValueWithDelta(
                    result.StrongestPeakSample,
                    reference?.StrongestPeakSample,
                    "0.0")) + "\r\n",
            UiPalette.TextPrimarySoft,
            resultTableFont);
    }

    private static double DelayMeters(double delayMilliseconds) =>
        Math.Abs(delayMilliseconds) * Acoustics.SpeedOfSoundAt20CMetersPerSecond / 1000.0;

    private static string FormatValueWithDelta(
        double value,
        double? reference,
        string valueFormat) =>
        DelayTableText.FormatValueWithDelta(value, reference, valueFormat);

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
        DelayTableText.FormatLine(label, firstArrival, strongestPeak);

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

        try
        {
            Clipboard.SetText(value);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard is a shared resource; another process may hold it
            // (remote desktop, clipboard managers). Losing one copy click must
            // not crash the app.
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        System.Media.SystemSounds.Asterisk.Play();
    }

    private bool TryGetCopyableStatusLine(Point location, out string value)
    {
        value = string.Empty;
        int index = statusTextBox.GetCharIndexFromPosition(location);
        int line = statusTextBox.GetLineFromCharIndex(index);
        if (line >= statusTextBox.Lines.Length)
        {
            return false;
        }

        string lineText = statusTextBox.Lines[line];
        if (!lineText.StartsWith("ms", StringComparison.Ordinal) &&
            !lineText.StartsWith("meters", StringComparison.Ordinal) &&
            !lineText.StartsWith("samples", StringComparison.Ordinal))
        {
            return false;
        }

        int lineStart = statusTextBox.GetFirstCharIndexFromLine(line);
        int column = Math.Max(0, index - lineStart);
        value = column >= DelayTableText.SecondColumn
            ? GetDelayTableValue(lineText, DelayTableText.SecondColumn)
            : column >= DelayTableText.FirstColumn
                ? GetDelayTableValue(lineText, DelayTableText.FirstColumn)
                : string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetDelayTableValue(string line, int startColumn) =>
        DelayTableText.GetValue(line, startColumn);

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

    private static LinearAxis CreateDecibelAxis()
    {
        var axis = new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = -80,
            Maximum = 3,
            MajorStep = 20,
            Title = "dB"
        };
        ApplyPreviewAxisStyle(axis);
        return axis;
    }

    private static LinearAxis CreateMillisecondsAxis(double minimum, double maximum)
    {
        var axis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            AbsoluteMinimum = minimum,
            AbsoluteMaximum = maximum,
            Minimum = minimum,
            Maximum = maximum,
            MajorStep = 25,
            Title = "ms from peak"
        };
        ApplyPreviewAxisStyle(axis);
        return axis;
    }

    // The shared dark-preview look of every axis on the two side plots.
    private static void ApplyPreviewAxisStyle(Axis axis)
    {
        axis.MajorGridlineColor = OxyColor.FromRgb(55, 62, 78);
        axis.MajorGridlineStyle = LineStyle.Solid;
        axis.MinorGridlineColor = OxyColor.FromRgb(48, 54, 70);
        axis.MinorGridlineStyle = LineStyle.Dot;
        axis.TextColor = OxyColors.White;
        axis.TicklineColor = OxyColors.White;
    }

    private static decimal ClampDecimal(double value, DarkNumericUpDown numeric) =>
        numeric.ClampValue(value);

    private static int WrapIndex(int index, int length) =>
        Resonalyze.Dsp.DspMath.WrapIndex(index, length);


}

internal readonly record struct TimeAlignmentCompareMeasurement(
    string DisplayName,
    MeasurementHistorySnapshot Snapshot);

internal readonly record struct TimeAlignmentCompareAnalysis(
    TimeAlignmentAnalysisSource Source,
    TimeAlignmentAnalysisResult Result);

internal readonly record struct TimeAlignmentAnalysisSource(
    string Kind,
    string DisplayName,
    int SampleRate,
    int Bits,
    int Octaves,
    double SweepDurationSeconds,
    PlaybackChannel PlayChannel,
    SweepMeasurementMode MeasurementMode,
    double[] TransferImpulseResponse,
    // The γ² half spectrum that produced TransferImpulseResponse (null for <2
    // averages or a snapshot without it). Fed to the GCC-PHAT refinement so
    // low-coherence bins carry less weight in the sub-sample alignment.
    double[]? TransferCoherence,
    InputLevelMeterSnapshot Levels);

internal sealed class StatusRichTextBox : RichTextBox
{
    private const int WmSetCursor = 0x20;
    private const int WmSetRedraw = 0x0B;
    private int updateDepth;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<Point, bool>? UseHandCursorAt { get; set; }

    public void BeginUpdate()
    {
        if (updateDepth++ == 0 && IsHandleCreated)
        {
            SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
        }
    }

    public void EndUpdate()
    {
        if (updateDepth == 0)
        {
            return;
        }

        updateDepth--;
        if (updateDepth == 0 && IsHandleCreated)
        {
            SendMessage(Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            Invalidate();
        }
    }

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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);
}
