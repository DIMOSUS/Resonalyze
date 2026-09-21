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

    private readonly Form owner;
    private readonly TimeAlignmentSession session;
    private readonly Action saveSettings;
    private readonly DeferredRefresh sourcesChanged;
    // Reads only while shown: a hidden panel reads when SetVisible shows it.
    private bool shown;
    private readonly TimeAlignmentPanel panel;
    private readonly Label sourceSummaryLabel;
    private readonly Label compareLabel;
    private readonly RadioButton bandModeFullRadio;
    private readonly RadioButton bandModeAutoRadio;
    private readonly RadioButton bandModeManualRadio;
    private readonly Label autoBandLabel;
    private readonly ThemedNumericUpDown bandpassCenterNumeric;
    private readonly ThemedNumericUpDown bandpassPassOctavesNumeric;
    private readonly ThemedNumericUpDown bandpassFadeOctavesNumeric;
    private readonly PlotView bandpassPlotView;
    private readonly PlotView envelopePlotView;
    // Previews are rebuilt on every configuration change; without these a zoom would not survive the next edit.
    private readonly PlotViewportMemory bandpassViewports;
    private readonly PlotViewportMemory envelopeViewports;
    private readonly StatusRichTextBox statusTextBox;
    private readonly Font resultTableFont;
    private bool disposed;

    private const string EnvelopeDecibelAxisTitle = "dB re Main peak";

    // Each curve is floored CurveFloorDb under its own max; the plot opens EnvelopeOpeningSpanDb tall so a quiet Compare record does not squeeze the arrivals (the axis still pans the full range).
    private const double CurveFloorDb = 80.0;
    private const double EnvelopeOpeningSpanDb = 100.0;

    public TimeAlignmentPanelController(
        Form owner,
        TimeAlignmentPanel panel,
        TimeAlignmentOptions options,
        AnalyzerDocument document,
        Action saveSettings,
        CompareSelection compareSelection)
    {
        this.owner = owner;
        this.panel = panel;
        session = new TimeAlignmentSession(options, document, compareSelection);
        this.saveSettings = saveSettings;
        sourcesChanged = new DeferredRefresh(owner, RefreshChangedSources);
        // +1 over the panel font, not +4: at +4 the status box wrapped the meters cell.
        resultTableFont = new Font(
            FontFamily.GenericMonospace,
            owner.Font.Size + 1.0f,
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
        PlotInteraction.Enable(bandpassPlotView);
        PlotInteraction.Enable(envelopePlotView);
        bandpassViewports = new PlotViewportMemory(bandpassPlotView);
        envelopeViewports = new PlotViewportMemory(envelopePlotView);
        statusTextBox = panel.StatusTextBox;
        statusTextBox.UseHandCursorAt = point => TryGetCopyableStatusLine(point, out _);
        statusTextBox.MouseClick += StatusTextBoxMouseClick;

        ApplyOptionsToControls();
        WireEvents();
        RefreshAnalysis();
        session.SourcesChanged += sourcesChanged.Request;
    }

    public bool InProgress => false;

    public void SetLayoutBounds(Rectangle bounds)
    {
        panel.Bounds = bounds;
    }

    public void SetVisible(bool visible)
    {
        shown = visible;
        panel.Visible = visible;
        if (visible)
        {
            RefreshConfiguration();
        }
    }

    /// <summary>Puts the options on the controls, and reads the sources while shown; showing reads them.</summary>
    public void RefreshConfiguration()
    {
        sourcesChanged.Refreshed();
        // The shared options object is written behind this panel (persisted settings, history restore) without touching controls; re-read it or the radios lie.
        ApplyOptionsToControls();
        if (shown)
        {
            RefreshAnalysis();
        }
    }

    public Task AbortAsync() => Task.CompletedTask;

    // While a run or an import holds the document the panel keeps what it read; the end of the hold reads again.
    private void RefreshChangedSources()
    {
        if (!session.SourcesBusy)
        {
            RefreshConfiguration();
        }
    }

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
        // Both radios fire CheckedChanged; react to the arriving one only.
        void OnRadio(object? sender, EventArgs _)
        {
            if (!applyingOptions && sender is RadioButton { Checked: true })
            {
                ApplyBandpassOptionChange();
            }
        }
        void OnNumeric(object? sender, EventArgs _)
        {
            if (!applyingOptions)
            {
                ApplyBandpassOptionChange();
            }
        }
        bandModeFullRadio.CheckedChanged += OnRadio;
        bandModeAutoRadio.CheckedChanged += OnRadio;
        bandModeManualRadio.CheckedChanged += OnRadio;
        bandpassCenterNumeric.ValueChanged += OnNumeric;
        bandpassPassOctavesNumeric.ValueChanged += OnNumeric;
        bandpassFadeOctavesNumeric.ValueChanged += OnNumeric;
    }

    private void ApplyBandpassOptionChange()
    {
        UpdateOptionsFromControls();
        RefreshAnalysis();
        saveSettings();
    }

    private void RefreshAnalysis()
    {
        sourceSummaryLabel.Text = TimeAlignmentSources.MainSummary(session);
        compareLabel.Text = TimeAlignmentSources.CompareSummary(session);

        if (!TimeAlignmentSources.TryGetMain(session, out TimeAlignmentAnalysisSource mainSource, out string noDataMessage))
        {
            session.ForgetReads();
            UpdateAutoBandLabel();
            UpdateBandpassPreview();
            ShowReport(TimeAlignmentReport.ForMessage(noDataMessage), fromTop: false);
            ClearEnvelopePreview();
            return;
        }

        // Preview and caption follow the controls immediately; the Auto caption still names the last finished read's band.
        UpdateAutoBandLabel();
        UpdateBandpassPreview();

        TimeAlignmentRequest request = session.CreateRequest(mainSource);
        if (session.Reads.Submit(request) is { } version)
        {
            StartAnalysis(request, version);
        }
    }

    // Off the UI thread when there is a message loop: a read of a megabyte transfer IR takes a few hundred ms.
    private void StartAnalysis(TimeAlignmentRequest request, int version)
    {
        if (!owner.IsHandleCreated || owner.IsDisposed || owner.InvokeRequired)
        {
            // No handle yet (controllers refresh before the shell has a window; panel tests never open one).
            CompleteAnalysis(request, TimeAlignmentRead.Run(request, session.Records), version);
            return;
        }

        _ = RunAnalysisAsync(request, version);
    }

    private async Task RunAnalysisAsync(TimeAlignmentRequest request, int version)
    {
        TimeAlignmentOutcome outcome;
        try
        {
            outcome = await Task.Run(() => TimeAlignmentRead.Run(request, session.Records));
        }
        catch (Exception exception)
        {
            outcome = TimeAlignmentOutcome.Failed(exception.Message);
        }

        if (!disposed && !owner.IsDisposed)
        {
            CompleteAnalysis(request, outcome, version);
        }
    }

    // Stale reads are not drawn: they would put the previous band's numbers under the current band.
    private void CompleteAnalysis(
        TimeAlignmentRequest request,
        TimeAlignmentOutcome outcome,
        int version)
    {
        if (disposed)
        {
            return;
        }

        // Not drawn, but its pool slot frees here so the wanted read still starts.
        if (!session.Reads.Complete(request, version))
        {
            StartDesiredAnalysis();
            return;
        }

        session.Land(outcome);
        UpdateAutoBandLabel();
        UpdateBandpassPreview();
        if (outcome.Message is { } message)
        {
            ShowReport(TimeAlignmentReport.ForMessage(message), fromTop: false);
            ClearEnvelopePreview();
            StartDesiredAnalysis();
            return;
        }

        ShowReport(TimeAlignmentReport.ForResult(request.BandMode, outcome), fromTop: true);
        UpdateEnvelopePreview(
            outcome.MainResult,
            outcome.MainSource.SampleRate,
            outcome.Compare?.Result);
        StartDesiredAnalysis();
    }

    private void StartDesiredAnalysis()
    {
        if (session.Reads.TakeDesired(out TimeAlignmentRequest desired) is { } version)
        {
            StartAnalysis(desired, version);
        }
    }

    // Writing controls raises the user-edit events, which would save the controls back into the options.
    private bool applyingOptions;

    private void ApplyOptionsToControls()
    {
        applyingOptions = true;
        try
        {
            bandModeFullRadio.Checked =
                session.Options.BandMode == TimeAlignmentBandMode.FullBand;
            bandModeAutoRadio.Checked =
                session.Options.BandMode == TimeAlignmentBandMode.AutoBand;
            bandModeManualRadio.Checked =
                session.Options.BandMode == TimeAlignmentBandMode.ManualBand;
            bandpassCenterNumeric.Value =
                bandpassCenterNumeric.ClampValue(session.Options.BandpassCenterHz);
            bandpassPassOctavesNumeric.Value =
                bandpassPassOctavesNumeric.ClampValue(session.Options.BandpassPassOctaves);
            bandpassFadeOctavesNumeric.Value =
                bandpassFadeOctavesNumeric.ClampValue(session.Options.BandpassFadeOctaves);
        }
        finally
        {
            applyingOptions = false;
        }

        UpdateBandpassControlStates();
    }

    private void UpdateOptionsFromControls()
    {
        session.Options.BandMode =
            bandModeAutoRadio.Checked ? TimeAlignmentBandMode.AutoBand
            : bandModeManualRadio.Checked ? TimeAlignmentBandMode.ManualBand
            : TimeAlignmentBandMode.FullBand;
        session.Options.BandpassCenterHz = (double)bandpassCenterNumeric.Value;
        session.Options.BandpassPassOctaves = (double)bandpassPassOctavesNumeric.Value;
        session.Options.BandpassFadeOctaves = (double)bandpassFadeOctavesNumeric.Value;
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
        autoBandLabel.Text = TimeAlignmentBand.AutoBandCaption(session);
    }

    private void UpdateBandpassPreview()
    {
        bool addCurve = session.Options.BandMode == TimeAlignmentBandMode.ManualBand ||
            (session.Options.BandMode == TimeAlignmentBandMode.AutoBand && session.AutoBand != null);
        PlotModel model = CreateBandpassPreviewModel(addCurve);
        bandpassViewports.Show(model, Mode.TimeAlignment);
    }

    private PlotModel CreateBandpassPreviewModel(bool addCurve)
    {
        int sampleRate = session.Main?.SampleRate ?? 0;
        double maxFrequency = Math.Min(20_000, sampleRate > 0
            ? sampleRate * 0.5
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
            Color = UiPalette.CurveEnvelope.ToOxy(),
            StrokeThickness = 2
        };
        (double f1, double f2, double f3, double f4) =
            session.Options.BandMode == TimeAlignmentBandMode.AutoBand && session.AutoBand is { } band
                ? BandpassWindow.BandAround(
                    Math.Sqrt(band.LowHz * band.HighHz),
                    Math.Log2(band.HighHz / band.LowHz),
                    TimeAlignmentBand.AutoBandFadeOctaves)
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

    private void UpdateEnvelopePreview(
        TimeAlignmentAnalysisResult result,
        int sampleRate,
        TimeAlignmentAnalysisResult? compareResult = null)
    {
        double[] envelope = result.EnvelopeSamples;
        if (envelope.Length == 0 || result.StrongestEnvelopePeak <= 0 || sampleRate <= 0)
        {
            ClearEnvelopePreview();
            return;
        }

        // ONE reference for both curves (Main's strongest peak): per-curve first-arrival normalization drew equal levels 19 dB apart when picks sat 6 and 25 dB under their peaks.
        double referenceAmplitude = result.StrongestEnvelopePeak;

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
            referenceAmplitude,
            sampleRate,
            radius,
            step,
            xOffsetMilliseconds: 0.0,
            UiPalette.CurveEnvelope.ToOxy(),
            strokeThickness: 2,
            out double maxDb,
            out double minDb);

        var model = CreatePreviewPlotModel("Envelope Around Peak");
        model.Axes.Add(CreateMillisecondsAxis(minMilliseconds, maxMilliseconds));
        var dbAxis = CreateDecibelAxis();
        dbAxis.Title = EnvelopeDecibelAxisTitle;
        ApplyEnvelopeDecibelRange(dbAxis, maxDb, minDb);
        model.Axes.Add(dbAxis);

        model.Series.Add(mainSeries);
        if (compareResult.HasValue)
        {
            LineSeries compareSeries = CreateEnvelopeSeries(
                compareResult.Value,
                referenceAmplitude,
                sampleRate,
                radius,
                step,
                compareOffsetMilliseconds,
                OxyColor.FromAColor(155, UiPalette.CurveCompare.ToOxy()),
                strokeThickness: 1.75,
                out double compareMaxDb,
                out double compareMinDb);
            maxDb = Math.Max(maxDb, compareMaxDb);
            minDb = Math.Min(minDb, compareMinDb);
            ApplyEnvelopeDecibelRange(dbAxis, maxDb, minDb);
            model.Series.Add(compareSeries);
        }

        if (compareResult.HasValue)
        {
            AddComparePeakMarkers(
                model,
                result,
                compareResult.Value,
                referenceAmplitude,
                compareOffsetMilliseconds);
        }
        else
        {
            AddMainPeakMarkers(model, result, referenceAmplitude);
        }
        envelopeViewports.Show(model, Mode.TimeAlignment);
    }

    private static void ApplyEnvelopeDecibelRange(
        LinearAxis axis,
        double maxDb,
        double minDb)
    {
        axis.AbsoluteMaximum = maxDb + 30;
        axis.AbsoluteMinimum = minDb - 10;
        axis.Maximum = maxDb + 2;
        axis.Minimum = Math.Max(minDb - 2, maxDb - EnvelopeOpeningSpanDb);
    }

    internal static LineSeries CreateEnvelopeSeries(
        TimeAlignmentAnalysisResult result,
        double referenceAmplitude,
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
            int index = DspMath.WrapIndex(result.EnvelopePeakIndex + offset, envelope.Length);
            double milliseconds = offset * 1000.0 / sampleRate + xOffsetMilliseconds;
            double relativeAmplitude = envelope[index] / referenceAmplitude;
            double decibels = DataHelper.AmplitudeToDecibels(relativeAmplitude);
            series.Points.Add(new DataPoint(milliseconds, decibels));
            localMaxDb = Math.Max(localMaxDb, decibels);
            localMinDb = Math.Min(localMinDb, decibels);
        }

        // Min/max pooling: every-Nth sampling would skip the narrow reflection peaks the markers point at.
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
                    DspMath.WrapIndex(result.EnvelopePeakIndex + offset, envelope.Length)];
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

        // Floor under THIS curve's max, so a genuinely quieter Compare record is drawn whole, not flattened.
        double floorDb = localMaxDb - CurveFloorDb;
        for (int i = 0; i < series.Points.Count; i++)
        {
            DataPoint point = series.Points[i];
            if (point.Y < floorDb)
            {
                series.Points[i] = new DataPoint(point.X, floorDb);
            }
        }

        maxDb = localMaxDb;
        minDb = Math.Max(localMinDb, floorDb);
        return series;
    }

    internal static void AddMainPeakMarkers(
        PlotModel model,
        TimeAlignmentAnalysisResult mainResult,
        double referenceAmplitude)
    {
        double strongestMilliseconds =
            mainResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        AddCalloutMarker(
            model,
            "M First",
            0.0,
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.EnvelopePeakIndex),
            UiPalette.MarkerFirstArrival.ToOxy(),
            PlotCalloutDirection.LeftUp);
        if (Math.Abs(strongestMilliseconds) > 0.001)
        {
            AddCalloutMarker(
                model,
                "M Peak",
                strongestMilliseconds,
                GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.StrongestEnvelopePeakIndex),
                UiPalette.MarkerStrongestPeak.ToOxy(),
                PlotCalloutDirection.RightUp);
        }

        AddCalloutMarker(
            model,
            "M Onset",
            mainResult.EnergyOnsetDelayMilliseconds - mainResult.FirstArrivalDelayMilliseconds,
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, GetEnergyOnsetIndex(mainResult)),
            UiPalette.MarkerEnergyOnset.ToOxy(),
            PlotCalloutDirection.LeftDown);
    }

    // Wrapped into the circular envelope: complete records report positions as signed delays.
    internal static int GetEnergyOnsetIndex(TimeAlignmentAnalysisResult result)
    {
        int length = result.EnvelopeSamples.Length;
        if (length == 0)
        {
            return 0;
        }

        long rounded = (long)Math.Round(result.EnergyOnsetSample);
        return (int)(((rounded % length) + length) % length);
    }

    internal static void AddComparePeakMarkers(
        PlotModel model,
        TimeAlignmentAnalysisResult mainResult,
        TimeAlignmentAnalysisResult compareResult,
        double referenceAmplitude,
        double compareFirstArrivalMilliseconds)
    {
        double mainFirstArrivalDecibels =
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.EnvelopePeakIndex);
        double compareFirstArrivalDecibels =
            GetPeakMarkerDecibels(compareResult, referenceAmplitude, compareResult.EnvelopePeakIndex);
        double mainStrongestMilliseconds =
            mainResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        double compareStrongestMilliseconds =
            compareResult.StrongestDelayMilliseconds -
            mainResult.FirstArrivalDelayMilliseconds;
        double mainStrongestDecibels =
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, mainResult.StrongestEnvelopePeakIndex);
        double compareStrongestDecibels =
            GetPeakMarkerDecibels(compareResult, referenceAmplitude, compareResult.StrongestEnvelopePeakIndex);

        AddCalloutMarker(
            model,
            "M First",
            0.0,
            mainFirstArrivalDecibels,
            UiPalette.MarkerFirstArrival.ToOxy(),
            mainFirstArrivalDecibels >= compareFirstArrivalDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);
        AddCalloutMarker(
            model,
            "C First",
            compareFirstArrivalMilliseconds,
            compareFirstArrivalDecibels,
            OxyColor.FromAColor(145, UiPalette.MarkerFirstArrival.ToOxy()),
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
                UiPalette.MarkerStrongestPeak.ToOxy(),
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
                OxyColor.FromAColor(145, UiPalette.MarkerStrongestPeak.ToOxy()),
                compareStrongestDecibels > mainStrongestDecibels
                    ? PlotCalloutDirection.RightUp
                    : PlotCalloutDirection.RightDown);
        }

        double mainOnsetDecibels =
            GetPeakMarkerDecibels(mainResult, referenceAmplitude, GetEnergyOnsetIndex(mainResult));
        double compareOnsetDecibels =
            GetPeakMarkerDecibels(compareResult, referenceAmplitude, GetEnergyOnsetIndex(compareResult));
        AddCalloutMarker(
            model,
            "M Onset",
            mainResult.EnergyOnsetDelayMilliseconds - mainResult.FirstArrivalDelayMilliseconds,
            mainOnsetDecibels,
            UiPalette.MarkerEnergyOnset.ToOxy(),
            mainOnsetDecibels >= compareOnsetDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);
        AddCalloutMarker(
            model,
            "C Onset",
            compareResult.EnergyOnsetDelayMilliseconds - mainResult.FirstArrivalDelayMilliseconds,
            compareOnsetDecibels,
            OxyColor.FromAColor(145, UiPalette.MarkerEnergyOnset.ToOxy()),
            compareOnsetDecibels > mainOnsetDecibels
                ? PlotCalloutDirection.LeftUp
                : PlotCalloutDirection.LeftDown);
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

    internal static double GetPeakMarkerDecibels(
        TimeAlignmentAnalysisResult result,
        double referenceAmplitude,
        int peakIndex)
    {
        if ((uint)peakIndex >= (uint)result.EnvelopeSamples.Length ||
            referenceAmplitude <= 0)
        {
            return 0.0;
        }

        // Same floor as the curve, so a marker never parks under its own line.
        double peakDecibels = DataHelper.AmplitudeToDecibels(
            result.StrongestEnvelopePeak / referenceAmplitude);
        double relativeAmplitude = result.EnvelopeSamples[peakIndex] / referenceAmplitude;
        return Math.Max(
            peakDecibels - CurveFloorDb,
            DataHelper.AmplitudeToDecibels(relativeAmplitude));
    }

    private void ClearEnvelopePreview()
    {
        envelopeViewports.Show(CreateEmptyEnvelopePreviewModel(), Mode.TimeAlignment);
    }

    private PlotModel CreateEmptyEnvelopePreviewModel()
    {
        var model = CreatePreviewPlotModel("Envelope Around Peak");
        model.Axes.Add(CreateMillisecondsAxis(-50, 50));
        var dbAxis = CreateDecibelAxis();
        dbAxis.Title = EnvelopeDecibelAxisTitle;
        dbAxis.AbsoluteMaximum = 0;
        dbAxis.AbsoluteMinimum = -80;
        dbAxis.Maximum = 0;
        dbAxis.Minimum = -80;
        model.Axes.Add(dbAxis);
        return model;
    }

    private void ShowReport(IReadOnlyList<TimeAlignmentReportSegment> segments, bool fromTop)
    {
        statusTextBox.BeginUpdate();
        try
        {
            statusTextBox.Clear();
            foreach (TimeAlignmentReportSegment segment in segments)
            {
                AppendStatusText(segment.Text, segment.Color, segment.Table ? resultTableFont : null);
            }

            if (fromTop)
            {
                statusTextBox.SelectionStart = 0;
                statusTextBox.SelectionLength = 0;
            }
        }
        finally
        {
            statusTextBox.EndUpdate();
        }
    }

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
            // Another process may hold the clipboard (RDP, clipboard managers); a lost copy must not crash.
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
        if (!DelayTableText.IsDelayRow(lineText))
        {
            return false;
        }

        int lineStart = statusTextBox.GetFirstCharIndexFromLine(line);
        int column = Math.Max(0, index - lineStart);
        value = DelayTableText.CellAt(column) is { } cellStart
            ? GetDelayTableValue(lineText, cellStart)
            : string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string GetDelayTableValue(string line, int startColumn) =>
        DelayTableText.GetValue(line, startColumn);

    private static PlotModel CreatePreviewPlotModel(string title) =>
        PlotModelStyle.CreatePreviewModel(title);

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

    private static void ApplyPreviewAxisStyle(Axis axis)
    {
        axis.MajorGridlineStyle = LineStyle.Solid;
        axis.MinorGridlineStyle = LineStyle.Dot;
        PlotModelStyle.StyleAxis(axis);
    }

}

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
