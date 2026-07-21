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
    private readonly RadioButton bandModeFullRadio;
    private readonly RadioButton bandModeAutoRadio;
    private readonly RadioButton bandModeManualRadio;
    private readonly Label autoBandLabel;
    private readonly DarkNumericUpDown bandpassCenterNumeric;
    private readonly DarkNumericUpDown bandpassPassOctavesNumeric;
    private readonly DarkNumericUpDown bandpassFadeOctavesNumeric;
    private readonly PlotView bandpassPlotView;
    private readonly PlotView envelopePlotView;
    private readonly StatusRichTextBox statusTextBox;
    private readonly Font resultTableFont;
    // The band detected for the Auto mode on the last refresh (null when no
    // data or another mode is active); feeds the preview plot and the label.
    private DominantBand? lastAutoBand;
    private bool disposed;

    // The fade the Auto mode puts around the detected pass band.
    private const double AutoBandFadeOctaves = 0.5;

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
        bandModeFullRadio = panel.BandModeFullRadio;
        bandModeAutoRadio = panel.BandModeAutoRadio;
        bandModeManualRadio = panel.BandModeManualRadio;
        autoBandLabel = panel.AutoBandLabel;
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
        // A radio switch fires CheckedChanged on both the leaving and the
        // arriving button; reacting to the arriving one alone refreshes once.
        void OnRadio(object? sender, EventArgs _)
        {
            if (sender is RadioButton { Checked: true })
            {
                ApplyBandpassOptionChange();
            }
        }
        bandModeFullRadio.CheckedChanged += OnRadio;
        bandModeAutoRadio.CheckedChanged += OnRadio;
        bandModeManualRadio.CheckedChanged += OnRadio;
        bandpassCenterNumeric.ValueChanged += (_, _) => ApplyBandpassOptionChange();
        bandpassPassOctavesNumeric.ValueChanged += (_, _) => ApplyBandpassOptionChange();
        bandpassFadeOctavesNumeric.ValueChanged += (_, _) => ApplyBandpassOptionChange();
    }

    private void ApplyBandpassOptionChange()
    {
        UpdateOptionsFromControls();
        RefreshAnalysis();
        saveSettings();
    }

    private void RefreshAnalysis()
    {
        sourceSummaryLabel.Text = CreateSourceSummary();
        compareLabel.Text = CreateCompareSummary();

        if (!TryGetMainSource(out TimeAlignmentAnalysisSource mainSource, out string noDataMessage))
        {
            lastAutoBand = null;
            UpdateAutoBandLabel();
            UpdateBandpassPreview();
            SetStatusText(noDataMessage);
            ClearEnvelopePreview();
            return;
        }

        try
        {
            // Crosstalk hygiene first: detection always runs on the RAW
            // record, then the banded modes analyze the CLEANED record —
            // the same order the Auto delay engine uses. A broadband click
            // lands inside the analysis band and the upper-half probe
            // alike, so analyzing the raw record could green-light
            // ("verified") an arrival that times the click. The bypass mode
            // keeps the raw record and flags the contamination instead.
            CrosstalkHeadGate? mainCrosstalk = TransferIrDiagnostics.DetectCrosstalkHead(
                mainSource.TransferImpulseResponse, mainSource.SampleRate);
            TimeAlignmentAnalysisSource mainAnalysisSource =
                CleanForAnalysis(mainSource, mainCrosstalk);

            // One options object per refresh: the Auto mode detects the band
            // from the MAIN record and the Compare measurement is analyzed
            // in the same band, so the delta column compares like with like.
            TimeAlignmentAnalysisOptions analysisOptions =
                CreateAnalysisOptions(mainAnalysisSource, wrapPeakPositions: true);
            UpdateAutoBandLabel();
            UpdateBandpassPreview();

            TimeAlignmentAnalysisResult mainResult = TimeAlignmentAnalysis.Analyze(
                mainAnalysisSource.TransferImpulseResponse,
                mainAnalysisSource.SampleRate,
                analysisOptions,
                mainAnalysisSource.TransferCoherence);
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

            TimeAlignmentArrivalProbe? mainProbe = TimeAlignmentAnalysis.ProbeArrivalHonesty(
                mainAnalysisSource.TransferImpulseResponse,
                mainAnalysisSource.SampleRate,
                analysisOptions,
                mainResult,
                mainAnalysisSource.TransferCoherence);
            TimeAlignmentCompareAnalysis? compareAnalysis = AnalyzeCompare(
                mainSource,
                analysisOptions,
                out string? compareWarning,
                out CrosstalkHeadGate? compareCrosstalk);
            TimeAlignmentArrivalProbe? compareProbe = compareAnalysis == null
                ? null
                : TimeAlignmentAnalysis.ProbeArrivalHonesty(
                    compareAnalysis.Value.Source.TransferImpulseResponse,
                    compareAnalysis.Value.Source.SampleRate,
                    analysisOptions,
                    compareAnalysis.Value.Result,
                    compareAnalysis.Value.Source.TransferCoherence);
            SetMeasurementResultStatus(
                mainSource,
                mainResult,
                mainProbe,
                mainCrosstalk,
                compareAnalysis,
                compareProbe,
                compareCrosstalk,
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

    // The banded modes analyze the record with the convicted click removed
    // (band detection included); the bypass mode shows the record as-is and
    // relies on the red flag instead.
    private TimeAlignmentAnalysisSource CleanForAnalysis(
        TimeAlignmentAnalysisSource source,
        CrosstalkHeadGate? crosstalk) =>
        options.BandMode != TimeAlignmentBandMode.FullBand && crosstalk is { } gate
            ? source with
            {
                TransferImpulseResponse = TransferIrDiagnostics.CleanCrosstalkHead(
                    source.TransferImpulseResponse, source.SampleRate, gate)
            }
            : source;

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

    private TimeAlignmentAnalysisOptions CreateAnalysisOptions(
        TimeAlignmentAnalysisSource source,
        bool wrapPeakPositions)
    {
        double centerHz = options.BandpassCenterHz;
        double passOctaves = options.BandpassPassOctaves;
        double fadeOctaves = options.BandpassFadeOctaves;
        lastAutoBand = null;
        if (options.BandMode == TimeAlignmentBandMode.AutoBand)
        {
            DominantBand band = TransferIrDiagnostics.DetectDominantBand(
                source.TransferImpulseResponse, source.SampleRate);
            lastAutoBand = band;
            centerHz = Math.Sqrt(band.LowHz * band.HighHz);
            passOctaves = Math.Log2(band.HighHz / band.LowHz);
            fadeOctaves = AutoBandFadeOctaves;
        }

        return new TimeAlignmentAnalysisOptions
        {
            UseBandpassWindow = options.BandMode != TimeAlignmentBandMode.FullBand,
            BandpassCenterHz = centerHz,
            BandpassPassOctaves = passOctaves,
            BandpassFadeOctaves = fadeOctaves,
            FirstPeakThresholdBelowMaxDb = options.FirstPeakThresholdBelowMaxDb,
            FirstPeakMinimumSnrDb = options.FirstPeakMinimumSnrDb,
            PeakSearchWindowMilliseconds = options.PeakSearchWindowMilliseconds,
            WrapPeakPositions = wrapPeakPositions
        };
    }

    private void ApplyOptionsToControls()
    {
        bandModeFullRadio.Checked = options.BandMode == TimeAlignmentBandMode.FullBand;
        bandModeAutoRadio.Checked = options.BandMode == TimeAlignmentBandMode.AutoBand;
        bandModeManualRadio.Checked = options.BandMode == TimeAlignmentBandMode.ManualBand;
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
        options.BandMode =
            bandModeAutoRadio.Checked ? TimeAlignmentBandMode.AutoBand
            : bandModeManualRadio.Checked ? TimeAlignmentBandMode.ManualBand
            : TimeAlignmentBandMode.FullBand;
        options.BandpassCenterHz = (double)bandpassCenterNumeric.Value;
        options.BandpassPassOctaves = (double)bandpassPassOctavesNumeric.Value;
        options.BandpassFadeOctaves = (double)bandpassFadeOctavesNumeric.Value;
        UpdateBandpassControlStates();
    }

    private void UpdateBandpassControlStates()
    {
        bool manual = bandModeManualRadio.Checked;
        bandpassCenterNumeric.Enabled = manual;
        bandpassPassOctavesNumeric.Enabled = manual;
        bandpassFadeOctavesNumeric.Enabled = manual;
    }

    private void UpdateAutoBandLabel()
    {
        autoBandLabel.Text = options.BandMode != TimeAlignmentBandMode.AutoBand
            ? "-"
            : lastAutoBand is { } band
                ? $"detected: {band.LowHz:0}-{band.HighHz:0} Hz"
                : "detected: waiting for a record";
    }

    private void UpdateBandpassPreview()
    {
        bool addCurve = options.BandMode == TimeAlignmentBandMode.ManualBand ||
            (options.BandMode == TimeAlignmentBandMode.AutoBand && lastAutoBand != null);
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
        (double f1, double f2, double f3, double f4) =
            options.BandMode == TimeAlignmentBandMode.AutoBand && lastAutoBand is { } band
                ? BandpassWindow.BandAround(
                    Math.Sqrt(band.LowHz * band.HighHz),
                    Math.Log2(band.HighHz / band.LowHz),
                    AutoBandFadeOctaves)
                : BandpassWindow.BandAround(
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
        TimeAlignmentAnalysisOptions analysisOptions,
        out string? warning,
        out CrosstalkHeadGate? crosstalk)
    {
        warning = null;
        crosstalk = null;
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
            // The Compare record gets the same hygiene as Main: its own
            // detection on the raw IR, cleaned analysis in the banded modes.
            crosstalk = TransferIrDiagnostics.DetectCrosstalkHead(
                compareSource.TransferImpulseResponse, compareSource.SampleRate);
            compareSource = CleanForAnalysis(compareSource, crosstalk);
            TimeAlignmentAnalysisResult compareResult = TimeAlignmentAnalysis.Analyze(
                compareSource.TransferImpulseResponse,
                compareSource.SampleRate,
                analysisOptions,
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
        TimeAlignmentArrivalProbe? mainProbe,
        CrosstalkHeadGate? mainCrosstalk,
        TimeAlignmentCompareAnalysis? compareAnalysis,
        TimeAlignmentArrivalProbe? compareProbe,
        CrosstalkHeadGate? compareCrosstalk,
        string? compareWarning)
    {
        statusTextBox.BeginUpdate();
        try
        {
            statusTextBox.Clear();
            AppendMeasurementResult(
                "Main", mainSource.Levels, mainResult, mainProbe, mainCrosstalk);
            AppendCompareResult(
                mainResult, compareAnalysis, compareProbe, compareCrosstalk, compareWarning);
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
        TimeAlignmentArrivalProbe? compareProbe,
        CrosstalkHeadGate? compareCrosstalk,
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
            compareProbe,
            compareCrosstalk,
            mainResult);
    }

    private void AppendMeasurementResult(
        string title,
        InputLevelMeterSnapshot levels,
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? honestyProbe,
        CrosstalkHeadGate? crosstalk,
        TimeAlignmentAnalysisResult? reference = null)
    {
        AppendSignalQuality(title, result);
        AppendAlignmentConfidence(result);
        AppendArrivalHonesty(result, honestyProbe);
        AppendCrosstalkFlag(crosstalk);
        AppendLevelsLine(levels);
        AppendSeparator();
        AppendDelayTable(result, reference);
        if (IsArrivalRecommendable(result, honestyProbe, options.BandMode, crosstalk != null))
        {
            AppendStrongestPeakHint(result);
        }
    }

    // Whether the First Arrival may be RECOMMENDED as the alignment figure.
    // The strongest-peak hint ends with "Use First Arrival for alignment",
    // and that advice must never print next to a verdict that just
    // disqualified the arrival: a modal latch, a near-noise record, or a
    // full-band read over a record with detected crosstalk (the bypass mode
    // analyzes it raw). The states are independent, so without this gate the
    // status box could give two opposite instructions at once.
    internal static bool IsArrivalRecommendable(
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? honestyProbe,
        TimeAlignmentBandMode bandMode,
        bool crosstalkDetected) =>
        result.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb &&
        honestyProbe?.Certificate != AutoAlignmentEngine.ArrivalCertificate.Latched &&
        !(bandMode == TimeAlignmentBandMode.FullBand && crosstalkDetected);

    // Field-proven failure (v3): an electrical copy of the playback lands at
    // a fixed early sample in every record of a session; on band-limited
    // drivers it sits within the first-peak threshold and the FULL-BAND
    // First Arrival confidently times it instead of the sound.
    private void AppendCrosstalkFlag(CrosstalkHeadGate? crosstalk)
    {
        if (crosstalk is not { } gate)
        {
            return;
        }

        if (options.BandMode == TimeAlignmentBandMode.FullBand)
        {
            // Bypass shows the record as-is, so the figures above may time
            // the click; the banded modes analyze it removed.
            AppendStatusText(
                $"⚠ Playback crosstalk at {gate.BurstTimeMs:0.00} ms " +
                $"({gate.BurstPeakDbReMax:0.0} dB re max) — an electrical copy of\r\n" +
                "the playback, not the driver's sound; the full-band First Arrival\r\n" +
                "may be timing it. Switch to Auto band (analyzed with it removed).\r\n",
                UiPalette.ErrorSoft);
            return;
        }

        AppendStatusText(
            $"⚠ Playback crosstalk at {gate.BurstTimeMs:0.00} ms " +
            $"({gate.BurstPeakDbReMax:0.0} dB re max) removed from this analysis\r\n",
            UiPalette.WarningAmber);
    }

    // The auto-alignment engine's arrival honesty probe, surfaced on the
    // manual table: with a bandpass window active, the full-band first
    // arrival is re-checked against the band's upper half. A full-band read
    // far LATER than its own upper half is the modal latch — the read times
    // the band's late build-up (a room mode), not the direct sound — which
    // produces a confident wrong number exactly where this tool is used most
    // (subwoofer and midbass bands).
    private void AppendArrivalHonesty(
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? probe)
    {
        if (options.BandMode == TimeAlignmentBandMode.FullBand)
        {
            return;
        }

        AppendStatusText("Arrival probe: ", UiPalette.TextPrimarySoft);
        if (probe == null)
        {
            AppendStatusText(
                "pass band too narrow for the upper-half check\r\n",
                UiPalette.TextSecondarySoft);
            return;
        }

        TimeAlignmentArrivalProbe probeValue = probe.Value;
        switch (probeValue.Certificate)
        {
            case AutoAlignmentEngine.ArrivalCertificate.Verified:
                AppendStatusText(
                    $"verified — the {probeValue.ProbeLowHz:0}-{probeValue.ProbeHighHz:0} Hz " +
                    "upper half agrees " +
                    $"({probeValue.ProbeResult.FirstArrivalDelayMilliseconds:0.000} ms)\r\n",
                    UiPalette.SuccessGreenSoft);
                break;
            case AutoAlignmentEngine.ArrivalCertificate.Latched:
                // The upper-half figure is DIAGNOSTIC only: it proves the
                // full-band read is not the direct front, but it is no
                // alignment target itself (the engine's field case: an
                // upper-half read walked a woofer 6 ms off).
                AppendStatusText(
                    $"MODAL LATCH — full band {result.FirstArrivalDelayMilliseconds:0.000} ms " +
                    $"vs upper half {probeValue.ProbeResult.FirstArrivalDelayMilliseconds:0.000} ms\r\n",
                    UiPalette.ErrorSoft);
                AppendStatusText(
                    "⚠ Not the direct front (modal build-up) — do not align " +
                    "from this arrival;\r\nchange the analysis band or check " +
                    "the measurement.\r\n",
                    UiPalette.ErrorSoft);
                break;
            default:
                AppendStatusText(
                    "not certified — the upper half is unmeasurable or does not " +
                    "show the front\r\n",
                    UiPalette.TextSecondarySoft);
                break;
        }
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
        AppendStatusText($"{title} Signal: ", UiPalette.TextPrimarySoft);

        // Below the same SNR floor the auto-alignment engine refuses to
        // measure at, the "arrival" is a bump in the noise (independent noise
        // records read ~8 dB): the manual table still shows its figures, but
        // graded as not-evidence rather than as a poor measurement.
        if (result.SignalToNoiseDecibels < AutoAlignmentEngine.MinimumArrivalSnrDb)
        {
            AppendStatusText(
                $"Unmeasurable ({result.SignalToNoiseDecibels:0.0} dB SNR, below " +
                $"the {AutoAlignmentEngine.MinimumArrivalSnrDb:0} dB floor)\r\n",
                UiPalette.ErrorSoft);
            AppendStatusText(
                "⚠ The arrival is not distinguishable from the record's noise\r\n" +
                "floor — the delay figures below are noise, not measurements.\r\n",
                UiPalette.ErrorSoft);
            return;
        }

        string signalGrade = FormatConfidence(result.SignalToNoiseDecibels);
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
        // The near-noise state above already declared the figures
        // non-evidence; a confident-looking percentage next to that verdict
        // would read as a contradiction.
        if (result.SignalToNoiseDecibels < AutoAlignmentEngine.MinimumArrivalSnrDb)
        {
            return;
        }

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

    // Mic and Loopback on one line: the status box holds two full measurement
    // blocks plus their warnings, so every line of vertical budget counts.
    private void AppendLevelsLine(InputLevelMeterSnapshot levels)
    {
        AppendStatusText("Levels (peak/RMS dBFS): ", UiPalette.TextPrimarySoft);
        AppendLevelSegment("mic", levels.Microphone);
        AppendStatusText(", ", UiPalette.TextPrimarySoft);
        AppendLevelSegment("loop", levels.Loopback);
        AppendStatusText("\r\n", UiPalette.TextPrimarySoft);
    }

    private void AppendLevelSegment(string label, InputLevelMeterEntry entry)
    {
        if (!entry.Available)
        {
            AppendStatusText($"{label} unavailable", UiPalette.TextSecondarySoft);
            return;
        }

        AppendStatusText(
            $"{label} {entry.PeakDbFs:0.0}/{entry.RmsDbFs:0.0}",
            UiPalette.TextPrimarySoft);
        if (entry.Clipped)
        {
            AppendStatusText(" CLIP", UiPalette.ErrorSoft);
        }
        else if (entry.FullScaleReference)
        {
            AppendStatusText(" FULL SCALE", UiPalette.TextSecondarySoft);
        }
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
