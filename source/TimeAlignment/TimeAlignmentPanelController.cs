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
            SetStatusText(noDataMessage);
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
            SetStatusText(message);
            ClearEnvelopePreview();
            StartDesiredAnalysis();
            return;
        }

        SetMeasurementResultStatus(
            request.BandMode,
            outcome.MainSource,
            outcome.MainResult,
            outcome.MainProbe,
            outcome.MainCrosstalk,
            outcome.Compare,
            outcome.CompareProbe,
            outcome.CompareCrosstalk,
            outcome.CompareWarning);
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

    private void SetStatusText(string text)
    {
        statusTextBox.BeginUpdate();
        try
        {
            statusTextBox.Clear();
            AppendStatusText(text, UiPalette.TextSecondary);
        }
        finally
        {
            statusTextBox.EndUpdate();
        }
    }

    private void SetMeasurementResultStatus(
        TimeAlignmentBandMode bandMode,
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
                bandMode, "Main", mainSource.Levels, mainResult, mainProbe, mainCrosstalk);
            AppendCompareResult(
                bandMode,
                mainResult,
                compareAnalysis,
                compareProbe,
                compareCrosstalk,
                compareWarning);
            statusTextBox.SelectionStart = 0;
            statusTextBox.SelectionLength = 0;
        }
        finally
        {
            statusTextBox.EndUpdate();
        }
    }

    private void AppendCompareResult(
        TimeAlignmentBandMode bandMode,
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
            AppendStatusText("\r\nCompare: ", UiPalette.TextDefault, resultTableFont);
            AppendStatusText(warning + "\r\n", UiPalette.Warning);
            return;
        }

        AppendStatusText("\r\n", UiPalette.TextDefault);
        AppendMeasurementResult(
            bandMode,
            "Compare",
            compareAnalysis!.Value.Source.Levels,
            compareAnalysis.Value.Result,
            compareProbe,
            compareCrosstalk,
            mainResult);
    }

    private void AppendMeasurementResult(
        TimeAlignmentBandMode bandMode,
        string title,
        InputLevelMeterSnapshot levels,
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? honestyProbe,
        CrosstalkHeadGate? crosstalk,
        TimeAlignmentAnalysisResult? reference = null)
    {
        AppendSignalQuality(title, result);
        AppendAlignmentConfidence(result);
        AppendArrivalHonesty(bandMode, result, honestyProbe);
        AppendCrosstalkFlag(bandMode, crosstalk);
        AppendLevelsLine(levels);
        AppendSeparator();
        TimeAlignmentDelayRow? recommended = TimeAlignmentRecommendation.RecommendedRow(
            result, honestyProbe, bandMode, crosstalk != null);
        AppendDelayTable(result, reference, recommended);
        if (recommended is { } row)
        {
            AppendStatusText("Recommended for alignment: ", UiPalette.TextDefault);
            AppendStatusText(TimeAlignmentRecommendation.RowLabel(row) + "\r\n", UiPalette.Success);
            AppendStrongestPeakHint(result);
        }
    }

    // Field failure (v3): an electrical playback copy at a fixed early sample is timed by the full-band first arrival instead of the sound.
    private void AppendCrosstalkFlag(
        TimeAlignmentBandMode bandMode,
        CrosstalkHeadGate? crosstalk)
    {
        if (crosstalk is not { } gate)
        {
            return;
        }

        // The mode the READ was taken in, not the controls' current one: a bypass read must not claim a cleaning.
        if (bandMode == TimeAlignmentBandMode.FullBand)
        {
            AppendStatusText(
                $"⚠ Playback crosstalk at {gate.BurstTimeMs:0.00} ms " +
                $"({gate.BurstPeakDbReMax:0.0} dB re max) — an electrical copy of\r\n" +
                "the playback, not the driver's sound; the full-band First Arrival\r\n" +
                "may be timing it. Switch to Auto band (analyzed with it removed).\r\n",
                UiPalette.Error);
            return;
        }

        AppendStatusText(
            $"⚠ Playback crosstalk at {gate.BurstTimeMs:0.00} ms " +
            $"({gate.BurstPeakDbReMax:0.0} dB re max) removed from this analysis\r\n",
            UiPalette.Warning);
    }

    // Engine's arrival honesty probe: a full-band arrival far LATER than its upper half is a modal latch (times a room mode, not the front).
    private void AppendArrivalHonesty(
        TimeAlignmentBandMode bandMode,
        TimeAlignmentAnalysisResult result,
        TimeAlignmentArrivalProbe? probe)
    {
        if (bandMode == TimeAlignmentBandMode.FullBand)
        {
            return;
        }

        AppendStatusText("Arrival probe: ", UiPalette.TextDefault);
        if (probe == null)
        {
            AppendStatusText(
                "pass band too narrow for the upper-half check\r\n",
                UiPalette.TextSecondary);
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
                    UiPalette.Success);
                break;
            case AutoAlignmentEngine.ArrivalCertificate.Latched:
                // Upper-half figure is diagnostic only: in the engine's field case it walked a woofer 6 ms off.
                AppendStatusText(
                    $"MODAL LATCH — full band {result.FirstArrivalDelayMilliseconds:0.000} ms " +
                    $"vs upper half {probeValue.ProbeResult.FirstArrivalDelayMilliseconds:0.000} ms\r\n",
                    UiPalette.Error);
                AppendStatusText(
                    "⚠ Not the direct front (modal build-up) — do not align " +
                    "from this arrival;\r\nchange the analysis band or check " +
                    "the measurement.\r\n",
                    UiPalette.Error);
                break;
            default:
                AppendStatusText(
                    "not certified — the upper half is unmeasurable or does not " +
                    "show the front\r\n",
                    UiPalette.TextSecondary);
                break;
        }
    }

    private void AppendStrongestPeakHint(TimeAlignmentAnalysisResult result)
    {
        if (!result.StrongestPeakIsSeparateArrival)
        {
            return;
        }

        AppendStatusText(
            $"⚠ Strongest peak is ~{result.StrongestPeakSeparationMilliseconds:0.0} ms " +
            "after first arrival — likely a room mode or reflection.\r\n",
            UiPalette.Warning);
    }

    // SNR grades the recording, prominence grades the pick; kept apart because a woofer's broad edge gives low prominence on a good recording.
    private void AppendSignalQuality(string title, TimeAlignmentAnalysisResult result)
    {
        AppendStatusText($"{title} Signal: ", UiPalette.TextDefault);

        // Below the engine's SNR floor the arrival is a noise bump (independent noise reads ~8 dB): shown, but graded not-evidence.
        if (result.SignalToNoiseDecibels < AutoAlignmentEngine.MinimumArrivalSnrDb)
        {
            AppendStatusText(
                $"Unmeasurable ({result.SignalToNoiseDecibels:0.0} dB SNR, below " +
                $"the {AutoAlignmentEngine.MinimumArrivalSnrDb:0} dB floor)\r\n",
                UiPalette.Error);
            AppendStatusText(
                "⚠ The arrival is not distinguishable from the record's noise\r\n" +
                "floor — the delay figures below are noise, not measurements.\r\n",
                UiPalette.Error);
            return;
        }

        string signalGrade = FormatConfidence(result.SignalToNoiseDecibels);
        AppendStatusText(
            $"{signalGrade} ({result.SignalToNoiseDecibels:0.0} dB SNR)\r\n",
            GetConfidenceColor(signalGrade));

        double prominence = result.FirstArrivalProminenceDecibels;
        AppendStatusText("First arrival: ", UiPalette.TextDefault);
        if (prominence >= -1.0)
        {
            AppendStatusText(
                "coincides with the strongest peak\r\n",
                UiPalette.Success);
            return;
        }

        string hint = prominence <= BroadRiseProminenceDb
            ? " — broad rise, normal for low-frequency drivers"
            : string.Empty;
        Color color = prominence >= BroadRiseProminenceDb
            ? UiPalette.Success
            : UiPalette.TextSecondary;
        AppendStatusText(
            $"{prominence:0.0} dB re strongest peak{hint}\r\n",
            color);
    }

    // Typical of band-limited LF drivers whose envelope rises over milliseconds.
    private const double BroadRiseProminenceDb = -12.0;

    // RefinedByPhat=false: whitened peak too weak, envelope parabola set the position (coarse alignment only).
    private void AppendAlignmentConfidence(TimeAlignmentAnalysisResult result)
    {
        // Near-noise already declared non-evidence; a confident percentage would contradict it.
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
            ? UiPalette.TextSecondary
            : result.FirstArrivalConfidence >= 0.6
                ? UiPalette.Success
                : result.FirstArrivalConfidence >= 0.4
                    ? UiPalette.Success
                    : UiPalette.Warning;
        AppendStatusText("Alignment: ", UiPalette.TextDefault);
        AppendStatusText($"{percent}% ({method})\r\n", color);
    }

    private void AppendSeparator()
    {
        AppendStatusText(
            new string('_', 54) + "\r\n",
            UiPalette.TextSecondary,
            resultTableFont);
    }

    // Compare cells show the delta against Main in parentheses; the recommended row is bright and named, others dimmed.
    private void AppendDelayTable(
        TimeAlignmentAnalysisResult result,
        TimeAlignmentAnalysisResult? reference,
        TimeAlignmentDelayRow? recommended)
    {
        AppendStatusText(
            DelayTableText.FormatHeader() + "\r\n",
            UiPalette.TextDefault,
            resultTableFont);
        AppendDelayRow(
            TimeAlignmentDelayRow.FirstArrival,
            UiPalette.MarkerFirstArrival,
            result.FirstArrivalDelayMilliseconds,
            result.FirstArrivalPeakSample,
            reference?.FirstArrivalDelayMilliseconds,
            reference?.FirstArrivalPeakSample,
            recommended);
        AppendDelayRow(
            TimeAlignmentDelayRow.StrongestPeak,
            UiPalette.MarkerStrongestPeak,
            result.StrongestDelayMilliseconds,
            result.StrongestPeakSample,
            reference?.StrongestDelayMilliseconds,
            reference?.StrongestPeakSample,
            recommended);
        AppendDelayRow(
            TimeAlignmentDelayRow.EnergyOnset,
            UiPalette.MarkerEnergyOnset,
            result.EnergyOnsetDelayMilliseconds,
            result.EnergyOnsetSample,
            reference?.EnergyOnsetDelayMilliseconds,
            reference?.EnergyOnsetSample,
            recommended);
    }

    private void AppendDelayRow(
        TimeAlignmentDelayRow row,
        Color labelColor,
        double milliseconds,
        double samples,
        double? referenceMilliseconds,
        double? referenceSamples,
        TimeAlignmentDelayRow? recommended)
    {
        bool isRecommended = recommended == row;
        // Cells stay one segment so click-to-copy columns remain exact.
        AppendStatusText(
            TimeAlignmentRecommendation.RowLabel(row).PadRight(DelayTableText.MillisecondsColumn),
            labelColor,
            resultTableFont);
        AppendStatusText(
            DelayTableText.FormatCells(
                FormatValueWithDelta(milliseconds, referenceMilliseconds, "0.000"),
                FormatValueWithDelta(samples, referenceSamples, "0.0"),
                FormatValueWithDelta(
                    DelayMeters(milliseconds),
                    referenceMilliseconds is { } referenceMs ? DelayMeters(referenceMs) : null,
                    "0.000")),
            isRecommended ? UiPalette.TextDefault : UiPalette.TextSecondary,
            resultTableFont);
        if (isRecommended)
        {
            AppendStatusText(
                DelayTableText.RecommendedMarker,
                UiPalette.Success,
                resultTableFont);
        }

        AppendStatusText("\r\n", UiPalette.TextDefault, resultTableFont);
    }

    private static double DelayMeters(double delayMilliseconds) =>
        Math.Abs(delayMilliseconds) * Acoustics.SpeedOfSoundAt20CMetersPerSecond / 1000.0;

    private static string FormatValueWithDelta(
        double value,
        double? reference,
        string valueFormat) =>
        DelayTableText.FormatValueWithDelta(value, reference, valueFormat);

    private void AppendLevelsLine(InputLevelMeterSnapshot levels)
    {
        AppendStatusText("Levels (peak/RMS dBFS): ", UiPalette.TextDefault);
        AppendLevelSegment("mic", levels.Microphone);
        AppendStatusText(", ", UiPalette.TextDefault);
        AppendLevelSegment("loop", levels.Loopback);
        AppendStatusText("\r\n", UiPalette.TextDefault);
    }

    private void AppendLevelSegment(string label, InputLevelMeterEntry entry)
    {
        if (!entry.Available)
        {
            AppendStatusText($"{label} unavailable", UiPalette.TextSecondary);
            return;
        }

        AppendStatusText(
            $"{label} {entry.PeakDbFs:0.0}/{entry.RmsDbFs:0.0}",
            UiPalette.TextDefault);
        if (entry.Clipped)
        {
            AppendStatusText(" CLIP", UiPalette.Error);
        }
        else if (entry.FullScaleReference)
        {
            AppendStatusText(" FULL SCALE", UiPalette.TextSecondary);
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
            "Excellent" => UiPalette.Success,
            "Good" => UiPalette.Success,
            "Fair" => UiPalette.Warning,
            _ => UiPalette.Error
        };

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
