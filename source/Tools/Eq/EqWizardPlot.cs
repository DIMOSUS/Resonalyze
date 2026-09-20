using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The EQ Wizard's plot model: magnitudes on the left axis, the bank and its handles on their own right-hand axis
/// (which phase takes over), the Auto Tune window and the selected band. Built from the session and the render set;
/// no WinForms.
/// </summary>
internal sealed class EqWizardPlot
{
    // Own right-hand axis so the EQ reads around 0 dB even when a dB SPL source puts the left axis far from 0.
    public const string EqGainAxisKey = "eq-wizard:gain";

    private const string WizardSeriesTag = "eq-wizard:curve";
    private const string WizardTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.00} dB";
    private const string PhaseTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.0} °";

    private static readonly OxyColor BandCurveColor = OxyColor.FromAColor(150, UiPalette.CurveBandOverlay.ToOxy());
    private static readonly OxyColor EqAxisColor = UiPalette.GraphAxisText.ToOxy();
    private static readonly OxyColor AboveTargetFill = OxyColor.FromAColor(72, UiPalette.CurveAboveTarget.ToOxy());
    private static readonly OxyColor BelowTargetFill = OxyColor.FromAColor(104, UiPalette.CurveBelowTarget.ToOxy());
    private static readonly OxyColor BandShapeFill = OxyColor.FromAColor(36, UiPalette.CurveBandOverlay.ToOxy());

    private readonly PlotWatermarkAnnotation hint;
    private readonly LineAnnotation fromMarker;
    private readonly LineAnnotation toMarker;
    private readonly LineAnnotation bandMarker;
    private readonly RectangleAnnotation rangeFill;
    private readonly EqBandHandlesAnnotation handles = new() { YAxisKey = EqGainAxisKey };

    // Last nominal range armed on the EQ axis, to tell the wizard's range from the user's zoom.
    private (double Lower, double Upper)? eqAxisNominal;

    public EqWizardPlot()
    {
        Model = new PlotModel();
        PlotModelStyle.ApplyChrome(Model);
        PlotModelStyle.AddFrequencyAxis(Model);
        // The IR bounds themselves, not copied literals that could drift from the ones a loaded source re-arms.
        EqWizardAxisRange initialRange = EqWizardPlotFit.ImpulseResponseRange;
        PlotModelStyle.AddAxis(Model, new LinearAxis
        {
            Position = AxisPosition.Left,
            AbsoluteMinimum = initialRange.AbsoluteMinimum,
            AbsoluteMaximum = initialRange.AbsoluteMaximum,
            MajorStep = 10,
            Minimum = initialRange.Minimum,
            Maximum = initialRange.Maximum,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            Title = "dB",
        });

        // EQ axis centred on 0 dB; no gridlines. Nominal range follows the boost/cut budget and is the hard pan/zoom limit.
        PlotModelStyle.AddAxis(Model, new LinearAxis
        {
            Key = EqGainAxisKey,
            Position = AxisPosition.Right,
            MajorStep = 6,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TextColor = EqAxisColor,
            TitleColor = EqAxisColor,
            TicklineColor = EqAxisColor,
            ExtraGridlines = new[] { 0.0 },
            ExtraGridlineColor = UiPalette.GraphAreaBorder.ToOxy(),
            ExtraGridlineStyle = LineStyle.Solid,
            Title = "EQ (dB)"
        });

        Model.Annotations.Add(new PlotWatermarkAnnotation
        {
            Text = "EQ Wizard",
            TextColor = OxyColor.FromAColor(10, UiPalette.GraphAxisText.ToOxy()),
            FontSize = 80,
            FontWeight = FontWeights.Bold
        });

        hint = new PlotWatermarkAnnotation
        {
            Text = string.Empty,
            VerticalPosition = 0.66,
            TextColor = UiPalette.CurveTarget.ToOxy(),
            FontSize = 15,
            FontWeight = FontWeights.Bold
        };
        Model.Annotations.Add(hint);

        rangeFill = new RectangleAnnotation
        {
            Fill = OxyColor.FromAColor(10, UiPalette.CurveWindowFill.ToOxy()),
            StrokeThickness = 0,
            Layer = AnnotationLayer.BelowSeries
        };
        Model.Annotations.Add(rangeFill);
        fromMarker = CreateRangeMarker();
        toMarker = CreateRangeMarker();
        Model.Annotations.Add(fromMarker);
        Model.Annotations.Add(toMarker);

        // Marks where the selected band sits (a low-Q bell's summit is guesswork; shelves have none).
        // No Visible on OxyPlot annotations here, so model membership is the switch.
        bandMarker = new LineAnnotation
        {
            Type = LineAnnotationType.Vertical,
            Color = BandCurveColor,
            StrokeThickness = 1,
            LineStyle = LineStyle.Dot,
            Layer = AnnotationLayer.AboveSeries
        };
        Model.Annotations.Add(handles.FaintLayer);
        Model.Annotations.Add(handles);
    }

    public PlotModel Model { get; }

    /// <summary>The bank's handles; the panel turns what they report into edits.</summary>
    public EqBandHandlesAnnotation Handles => handles;

    /// <summary>Marks the Auto Tune window, which also bounds the error metrics.</summary>
    public void ShowWindow(EqWizardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        fromMarker.X = (double)session.WindowFromHz;
        toMarker.X = (double)session.WindowToHz;
        rangeFill.MinimumX = fromMarker.X;
        rangeFill.MaximumX = toMarker.X;
    }

    /// <summary>Re-arms the magnitude axis for a newly loaded source; panning stays inside its absolute limits.</summary>
    public void FitSourceAxis(EqWizardSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Model.Axes.FirstOrDefault(axis => axis.Position == AxisPosition.Left) is not { } axis)
        {
            return;
        }

        (EqWizardAxisRange range, bool soundPressureLevel) = EqWizardRender.SourceAxis(session);
        axis.AbsoluteMinimum = double.NegativeInfinity;
        axis.AbsoluteMaximum = double.PositiveInfinity;
        axis.Minimum = range.Minimum;
        axis.Maximum = range.Maximum;
        axis.AbsoluteMinimum = range.AbsoluteMinimum;
        axis.AbsoluteMaximum = range.AbsoluteMaximum;
        axis.Title = soundPressureLevel ? "dB SPL" : "dB";
        axis.Reset();
    }

    /// <summary>
    /// Budget range extended to contain the drawn curve (overlapping bands can exceed one band's limit); 0/0 = no curve.
    /// Phase owns the whole axis at a fixed ±180°.
    /// </summary>
    public void RefreshEqAxis(EqWizardSession session, double curveMinDb = 0, double curveMaxDb = 0)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Model.Axes.FirstOrDefault(axis => axis.Key == EqGainAxisKey) is not LinearAxis eqAxis)
        {
            return;
        }

        bool phase = session.PhaseMode;
        (double lower, double upper) = phase
            ? (-180.0, 180.0)
            : EqWizardPlotFit.EqGainAxisRange(
                (double)session.GainMinDb,
                (double)session.GainMaxDb,
                curveMinDb,
                curveMaxDb);
        eqAxis.Title = phase ? "Phase (°)" : "EQ (dB)";
        eqAxis.MajorStep = phase ? 90 : 6;
        // Nominal is always the hard limit, but the range is re-armed only when the nominal moved, so redraws keep the user's zoom.
        eqAxis.AbsoluteMinimum = lower;
        eqAxis.AbsoluteMaximum = upper;
        if (eqAxisNominal is { } previous &&
            Math.Abs(previous.Lower - lower) < 1e-9 &&
            Math.Abs(previous.Upper - upper) < 1e-9)
        {
            return;
        }

        eqAxisNominal = (lower, upper);
        eqAxis.Minimum = lower;
        eqAxis.Maximum = upper;
        eqAxis.Reset();
    }

    /// <summary>
    /// Replaces the wizard's curves: magnitudes with their deviation shading, or in phase mode the measured phase curves;
    /// then the bank's response and the selected band's on the right-hand axis.
    /// </summary>
    /// <param name="eq">The bank as drawn (<see cref="EqWizardRender.DisplayedEq"/>).</param>
    /// <param name="selectedBand">Index into the session's bank, or null.</param>
    /// <param name="phase">The measured phase curves; null without them or outside phase mode.</param>
    public void Draw(
        EqWizardSession session,
        EqualizationCurve eq,
        EqWizardRenderSet render,
        int? selectedBand,
        EqWizardPhaseCurves? phase)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(eq);
        ArgumentNullException.ThrowIfNull(render);
        RemoveWizardSeries();
        hint.Text = EqWizardRender.Hint(session);

        // In phase mode the empty dB axis would read as a scale for the degree curves.
        SetMagnitudeAxisVisible(!session.PhaseMode);
        // First, so every curve draws over it.
        bool showHandles = ShowsHandles(session);
        if (showHandles)
        {
            AddSelectedBandShape(session, selectedBand, render.Target);
        }

        if (session.PhaseMode)
        {
            if (phase != null)
            {
                AddPhaseCurves(phase);
            }
        }
        else
        {
            bool showEqCurves = render.SourcePlusEq != null;
            if (showEqCurves)
            {
                AddDeviationFill(render.SourcePlusEq!, render.Target);
            }

            if (render.Source != null)
            {
                AddSeries(render.Source);
            }

            AddSeries(render.Target);
            if (showEqCurves)
            {
                AddSeries(render.SourcePlusEq!);
            }
        }

        AddEqCurve(session, eq, render.Target);
        AddSelectedBandCurve(session, selectedBand, render.Target);
        handles.Show(showHandles ? session.Bank.Bands : Array.Empty<PeqBand>(), selectedBand);
        UpdateSelectedBandMarker(session, showHandles ? null : selectedBand);
        // In magnitude the right axis holds only the EQ curve; in phase every measured curve is on it.
        if (Model.Axes.FirstOrDefault(axis => axis.Key == EqGainAxisKey) is { } eqAxis)
        {
            eqAxis.IsAxisVisible = Model.Series.Any(series =>
                series is XYAxisSeries { YAxisKey: EqGainAxisKey });
        }
    }

    // With the bank's curve, and like it not under Bypass, whose plot shows no EQ.
    private static bool ShowsHandles(EqWizardSession session) =>
        !session.PhaseMode && session.ShowEqCurve && !session.Bypass;

    private static LineAnnotation CreateRangeMarker() => new()
    {
        Type = LineAnnotationType.Vertical,
        Color = OxyColor.FromAColor(100, UiPalette.CurveWindowFill.ToOxy()),
        StrokeThickness = 1,
        LineStyle = LineStyle.Dash,
        Layer = AnnotationLayer.AboveSeries
    };

    private void SetMagnitudeAxisVisible(bool visible)
    {
        if (Model.Axes.FirstOrDefault(axis =>
                axis.Position == AxisPosition.Left && axis.Key == null) is { } left)
        {
            left.IsAxisVisible = visible;
            left.MajorGridlineStyle = visible ? LineStyle.Solid : LineStyle.None;
            left.MinorGridlineStyle = visible ? LineStyle.Dot : LineStyle.None;
        }
    }

    private void RemoveWizardSeries()
    {
        for (int index = Model.Series.Count - 1; index >= 0; index--)
        {
            if (Equals(Model.Series[index].Tag, WizardSeriesTag))
            {
                Model.Series.RemoveAt(index);
            }
        }
    }

    private void AddSeries(
        EqWizardCurve curve,
        string? yAxisKey = null,
        string trackerFormat = WizardTrackerFormat)
    {
        var series = new LineSeries
        {
            Color = curve.Color,
            StrokeThickness = curve.StrokeThickness,
            LineStyle = curve.LineStyle,
            Title = curve.Title,
            Tag = WizardSeriesTag,
            TrackerFormatString = trackerFormat
        };
        if (!string.IsNullOrEmpty(yAxisKey))
        {
            series.YAxisKey = yAxisKey;
        }

        series.Points.AddRange(curve.Points);
        Model.Series.Add(series);
    }

    // Neighbours first so the edited curve is never hidden under a reference.
    private void AddPhaseCurves(EqWizardPhaseCurves phase)
    {
        foreach (GatedPhaseCurve neighbour in phase.Neighbours)
        {
            AddPhaseSeries(neighbour, dimmed: true);
        }

        if (phase.Bare is { } bare)
        {
            AddPhaseSeries(bare, dimmed: true, LineStyle.Dash);
        }

        if (phase.Edited is { } edited)
        {
            AddPhaseSeries(edited, dimmed: false, markWraps: true);
        }
    }

    /// <remarks>Only the edited curve marks wraps: every curve marking them turns the HF into a picket fence.</remarks>
    private void AddPhaseSeries(
        GatedPhaseCurve curve,
        bool dimmed,
        LineStyle style = LineStyle.Solid,
        bool markWraps = false)
    {
        OxyColor color = dimmed
            ? OxyColor.FromAColor(170, curve.Color)
            : curve.Color;
        if (markWraps && curve.WrapSegments.Count > 0)
        {
            // Empty title keeps them out of the labels panel, as in Virtual DSP.
            AddSeries(
                new EqWizardCurve(
                    string.Empty,
                    OxyColor.FromAColor(90, curve.Color),
                    curve.Thickness * 0.4,
                    LineStyle.Dash,
                    curve.WrapSegments
                        .Select(point => new DataPoint(point.X, point.Y))
                        .ToArray()),
                EqGainAxisKey,
                PhaseTrackerFormat);
        }

        AddSeries(
            new EqWizardCurve(
                curve.Title,
                color,
                curve.Thickness,
                style,
                curve.Points
                    .Select(point => new DataPoint(point.X, point.Y))
                    .ToArray()),
            EqGainAxisKey,
            PhaseTrackerFormat);
    }

    // The bank's response (no preamp) on the right axis: gain in dB, or wrapped phase in degrees in phase view.
    private void AddEqCurve(EqWizardSession session, EqualizationCurve eq, EqWizardCurve? baseline)
    {
        if (baseline is not { Points.Count: >= 2 })
        {
            return;
        }

        if (session.PhaseMode)
        {
            if (session.ShowEqCurve)
            {
                AddSeries(
                    new EqWizardCurve(
                        "EQ phase",
                        UiPalette.CurveEqBank.ToOxy(),
                        1.5,
                        LineStyle.Solid,
                        EqWizardRender.PhasePoints(session, eq.Bands, baseline)),
                    EqGainAxisKey,
                    PhaseTrackerFormat);
            }

            // Re-armed even when not drawn: measured phase curves share this axis.
            RefreshEqAxis(session);
            return;
        }

        if (!session.ShowEqCurve)
        {
            RefreshEqAxis(session);
            return;
        }

        IReadOnlyList<DataPoint> points = EqWizardRender.EqGainPoints(session, eq, baseline);
        AddSeries(
            new EqWizardCurve("EQ", UiPalette.CurveEqBank.ToOxy(), 1.5, LineStyle.Solid, points),
            EqGainAxisKey);

        double curveMin = 0;
        double curveMax = 0;
        foreach (DataPoint point in points)
        {
            if (!double.IsFinite(point.Y))
            {
                continue;
            }

            curveMin = Math.Min(curveMin, point.Y);
            curveMax = Math.Max(curveMax, point.Y);
        }

        RefreshEqAxis(session, curveMin, curveMax);
    }

    // Selected band's contribution on the target (target + that band), or its own phase on the right axis in phase view.
    private void AddSelectedBandCurve(EqWizardSession session, int? selectedBand, EqWizardCurve? baseline)
    {
        if (selectedBand is not { } index || baseline is not { Points.Count: >= 2 })
        {
            return;
        }

        PeqBand band = session.Bank.Bands[index];
        int slotNumber = index + 1;
        if (session.PhaseMode)
        {
            AddSeries(
                new EqWizardCurve(
                    $"Band {slotNumber} phase",
                    BandCurveColor,
                    2,
                    LineStyle.Dash,
                    EqWizardRender.PhasePoints(session, new[] { band }, baseline)),
                EqGainAxisKey,
                PhaseTrackerFormat);
            return;
        }

        AddSeries(
            new EqWizardCurve(
                $"Band {slotNumber}",
                BandCurveColor,
                2,
                LineStyle.Dash,
                EqWizardRender.BandPoints(session, band, baseline)));
    }

    // The selected band's own gain filled down to 0 dB on the EQ axis, joining its handle to the bank's curve.
    private void AddSelectedBandShape(EqWizardSession session, int? selectedBand, EqWizardCurve baseline)
    {
        if (selectedBand is not { } index ||
            baseline.Points.Count < 2 ||
            session.Bank.Bands[index].Type.IsAllPass())
        {
            return;
        }

        var area = new AreaSeries
        {
            Color = OxyColors.Transparent,
            Fill = BandShapeFill,
            StrokeThickness = 0,
            YAxisKey = EqGainAxisKey,
            Tag = WizardSeriesTag
        };
        foreach (DataPoint point in EqWizardRender.BandGainPoints(session, session.Bank.Bands[index], baseline))
        {
            area.Points.Add(point);
            area.Points2.Add(new DataPoint(point.X, 0));
        }

        Model.Series.Add(area);
    }

    // Where the handles are hidden (phase, no EQ curve, Bypass); the frequency needs no baseline curve.
    private void UpdateSelectedBandMarker(EqWizardSession session, int? selectedBand)
    {
        Model.Annotations.Remove(bandMarker);
        if (selectedBand is not { } index)
        {
            return;
        }

        // No guard: a band's frequency cannot go below 10 Hz, so the log axis never sees zero.
        bandMarker.X = session.Bank.Bands[index].FrequencyHz;
        Model.Annotations.Add(bandMarker);
    }

    // TwoColorAreaSeries splits only on a horizontal limit, so two AreaSeries clamp to the target (curves index-aligned),
    // with exact crossings inserted so neither colour bleeds past the target.
    private void AddDeviationFill(EqWizardCurve curve, EqWizardCurve target)
    {
        IReadOnlyList<DataPoint> c = curve.Points;
        IReadOnlyList<DataPoint> t = target.Points;
        int n = Math.Min(c.Count, t.Count);
        if (n < 2)
        {
            return;
        }

        var curveAug = new List<DataPoint>(n + 8);
        var targetAug = new List<DataPoint>(n + 8);
        for (int i = 0; i < n; i++)
        {
            curveAug.Add(c[i]);
            targetAug.Add(t[i]);
            if (i + 1 >= n)
            {
                continue;
            }

            double d0 = c[i].Y - t[i].Y;
            double d1 = c[i + 1].Y - t[i + 1].Y;
            if (double.IsFinite(d0) && double.IsFinite(d1) && d0 * d1 < 0)
            {
                double f = d0 / (d0 - d1);
                double crossX = InterpolateLogX(c[i].X, c[i + 1].X, f);
                double crossY = t[i].Y + f * (t[i + 1].Y - t[i].Y);
                curveAug.Add(new DataPoint(crossX, crossY));
                targetAug.Add(new DataPoint(crossX, crossY));
            }
        }

        AddClampedFill(curveAug, targetAug, above: true, AboveTargetFill);
        AddClampedFill(curveAug, targetAug, above: false, BelowTargetFill);
    }

    // One area per run of finite points: a NaN vertex would make the renderer close the shape across unmeasured octaves.
    private void AddClampedFill(
        IReadOnlyList<DataPoint> curve,
        IReadOnlyList<DataPoint> target,
        bool above,
        OxyColor fill)
    {
        AreaSeries? area = null;
        for (int i = 0; i < curve.Count; i++)
        {
            double clamped = above
                ? Math.Max(curve[i].Y, target[i].Y)
                : Math.Min(curve[i].Y, target[i].Y);
            if (!double.IsFinite(clamped) || !double.IsFinite(target[i].Y))
            {
                area = null;
                continue;
            }

            if (area == null)
            {
                area = new AreaSeries
                {
                    Color = OxyColors.Transparent,
                    Fill = fill,
                    StrokeThickness = 0,
                    Tag = WizardSeriesTag
                };
                Model.Series.Add(area);
            }

            area.Points.Add(new DataPoint(curve[i].X, clamped));
            area.Points2.Add(target[i]);
        }
    }

    // Log-domain interpolation, matching the plot's log X axis.
    private static double InterpolateLogX(double x0, double x1, double f)
    {
        if (x0 > 0 && x1 > 0)
        {
            return Math.Exp(Math.Log(x0) + f * (Math.Log(x1) - Math.Log(x0)));
        }

        return x0 + f * (x1 - x0);
    }
}
