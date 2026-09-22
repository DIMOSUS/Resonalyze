using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

internal enum AcousticView
{
    Magnitude,
    Phase,
    Impulse,
    GroupDelay,
    Step
}

/// <summary><paramref name="OnLossAxis"/> binds the curve to the right-hand sum-loss axis.</summary>
internal sealed record AcousticCurve(
    string Title,
    IReadOnlyList<SignalPoint> Points,
    OxyColor Color,
    double Thickness,
    LineStyle Style,
    bool OnLossAxis = false);

/// <summary>Time-domain payload framed by the gate window. <paramref name="Step"/> draws step responses on one common scale, Sum included.</summary>
internal sealed record AcousticImpulseRender(
    IReadOnlyList<IrPreviewTrace> Traces,
    int SampleRate,
    double GateOffsetMs,
    double LeftMs,
    double PlateauMs,
    double RightMs,
    bool Step = false);

internal sealed record AcousticRender(
    string HintText,
    IReadOnlyList<AcousticCurve> Curves,
    AcousticImpulseRender? Impulse);

/// <summary>Presenter of the Virtual DSP main (acoustic) plot: owns the model, its four axes and range preservation; never builds curves.</summary>
internal sealed class VirtualCrossoverAcousticPlot
{
    private const string SeriesTag = "virtual-crossover:curve";
    private const string LossAxisKey = "virtual-crossover:loss";
    private const string TrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.00}";
    private const string GroupDelayTrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.000} ms";
    private string curveTrackerFormat = TrackerFormat;

    // Loss axis: 6 dB steps, top just above 0 dB, nominal depth extended in whole steps down to a floor.
    private const double LossAxisStepDb = 6;
    private const double LossAxisTopDb = 3;
    private const double LossAxisNominalBottomDb = -24;
    private const double LossAxisFloorDb = -60;

    /// <summary>The magnitude view's pan floor; the ceiling is <see cref="PlotModelStyle.RelativeDecibelAbsoluteMaximum"/>.</summary>
    public const double MagnitudeFloorDb = -90;

    public static readonly OxyColor LossAxisColor = UiPalette.CurveTarget.ToOxy();

    private readonly PlotView view;
    private readonly PlotLabelsPanelController plotLabels;
    private readonly PlotWatermarkAnnotation hintAnnotation;
    private readonly LinearAxis valueAxis;
    // The loss is a dB gap (≤ 0), not a level: on the shared axis it flattened into the floor. Visible only while drawn.
    private readonly LinearAxis lossAxis;
    // Last armed nominal range, to tell our range from the user's zoom.
    private (double Lower, double Upper)? lossAxisNominal;
    // Only one bottom axis is in the model at a time, so untagged series bind to the active one.
    private readonly LogarithmicAxis frequencyAxis;
    private readonly LinearAxis timeAxis;

    // Below this a window move is recomputation noise, not a new window.
    private const double WindowMoveEpsilonMs = 1e-6;
    private (double StartMs, double EndMs)? impulseWindow;

    public VirtualCrossoverAcousticPlot(PlotView view, string hint, AcousticView initialView)
    {
        ArgumentNullException.ThrowIfNull(view);
        this.view = view;

        var model = new PlotModel();
        PlotModelStyle.ApplyChrome(model);
        PlotModelStyle.AddFrequencyAxis(model);
        frequencyAxis = (LogarithmicAxis)model.Axes[^1];
        // Zoomable within the gate window; the range is re-armed only when the window moves, so zoom survives redraws.
        timeAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "ms",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        };
        valueAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        };
        PlotModelStyle.AddAxis(model, valueAxis);
        // Added after the value axis: PlotAxisZoom.FindZoomableAxis takes the first zoomable vertical axis.
        lossAxis = new LinearAxis
        {
            Key = LossAxisKey,
            Position = AxisPosition.Right,
            Title = "Sum loss (dB)",
            MajorStep = LossAxisStepDb,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None,
            TextColor = LossAxisColor,
            TitleColor = LossAxisColor,
            TicklineColor = LossAxisColor,
            ExtraGridlines = new[] { 0.0 },
            ExtraGridlineColor = OxyColor.FromAColor(60, LossAxisColor),
            ExtraGridlineStyle = LineStyle.Solid,
            IsAxisVisible = false
        };
        PlotModelStyle.AddAxis(model, lossAxis);

        model.Annotations.Add(new PlotWatermarkAnnotation
        {
            Text = "Virtual DSP",
            TextColor = OxyColor.FromAColor(10, UiPalette.GraphAxisText.ToOxy()),
            FontSize = 70,
            FontWeight = FontWeights.Bold
        });
        hintAnnotation = new PlotWatermarkAnnotation
        {
            Text = hint,
            VerticalPosition = 0.66,
            TextColor = UiPalette.CurveTarget.ToOxy(),
            FontSize = 15,
            FontWeight = FontWeights.Bold
        };
        model.Annotations.Add(hintAnnotation);

        view.Model = model;
        ConfigureForView(initialView);
        PlotInteraction.Enable(view);
        plotLabels = new PlotLabelsPanelController(view, () => Mode.VirtualCrossover);
    }

    // One value axis reused across views so frequency zoom survives toggles.
    public void ConfigureForView(AcousticView acousticView)
    {
        if (IsTimeDomain(acousticView))
        {
            // Impulse traces are normalized to their own envelope peak, step traces to the largest; unitless.
            valueAxis.Title = string.Empty;
            valueAxis.AbsoluteMinimum = -1.05;
            valueAxis.AbsoluteMaximum = 1.05;
            valueAxis.Minimum = -1.05;
            valueAxis.Maximum = 1.05;
            valueAxis.MajorStep = 0.5;
        }
        else if (acousticView == AcousticView.Phase)
        {
            valueAxis.Title = "deg";
            valueAxis.AbsoluteMinimum = -180;
            valueAxis.AbsoluteMaximum = 180;
            valueAxis.Minimum = -180;
            valueAxis.Maximum = 180;
            valueAxis.MajorStep = 45;
        }
        else if (acousticView == AcousticView.GroupDelay)
        {
            valueAxis.Title = "ms";
            valueAxis.AbsoluteMinimum = double.MinValue;
            valueAxis.AbsoluteMaximum = double.MaxValue;
            valueAxis.Minimum = double.NaN;
            valueAxis.Maximum = double.NaN;
            valueAxis.MajorStep = double.NaN;
        }
        else
        {
            valueAxis.Title = "dB";
            valueAxis.AbsoluteMinimum = MagnitudeFloorDb;
            // Loopback-referenced magnitudes share the pan ceiling (see PlotModelStyle).
            valueAxis.AbsoluteMaximum = PlotModelStyle.RelativeDecibelAbsoluteMaximum;
            valueAxis.Minimum = double.NaN;
            valueAxis.Maximum = double.NaN;
            valueAxis.MajorStep = double.NaN;
        }

        // Locked: ±180° is the whole wrapped range (also drops it from the limits dialog).
        bool phase = acousticView == AcousticView.Phase;
        valueAxis.IsZoomEnabled = !phase;
        valueAxis.IsPanEnabled = !phase;
        bool groupDelay = acousticView == AcousticView.GroupDelay;
        valueAxis.MinimumPadding = groupDelay ? 0.1 : 0.01;
        valueAxis.MaximumPadding = groupDelay ? 0.1 : 0.01;
        curveTrackerFormat = groupDelay ? GroupDelayTrackerFormat : TrackerFormat;

        // The loss is magnitude-only; other views drop its axis immediately.
        if (acousticView != AcousticView.Magnitude)
        {
            lossAxis.IsAxisVisible = false;
        }

        ConfigureBottomAxis(acousticView);
        valueAxis.Reset();
        view.InvalidatePlot(false);
    }

    public void ShowHint(string hint)
    {
        hintAnnotation.Text = hint;
        view.InvalidatePlot(true);
    }

    public void Draw(AcousticRender render)
    {
        ArgumentNullException.ThrowIfNull(render);
        if (view.Model is not { } model)
        {
            return;
        }

        RemoveCurveSeries(model);
        hintAnnotation.Text = render.HintText;

        if (render.Impulse is { } impulse)
        {
            lossAxis.IsAxisVisible = false;
            DrawImpulse(model, impulse);
        }
        else
        {
            bool lossDrawn = false;
            foreach (AcousticCurve curve in render.Curves)
            {
                AddCurve(model, curve, curveTrackerFormat);
                lossDrawn |= curve.OnLossAxis;
            }

            lossAxis.IsAxisVisible = lossDrawn;
            if (lossDrawn)
            {
                UpdateLossAxisRange(render.Curves);
            }
        }

        plotLabels.Refresh();
        model.InvalidatePlot(true);
    }

    private static bool IsTimeDomain(AcousticView acousticView) =>
        acousticView is AcousticView.Impulse or AcousticView.Step;

    // Swapping whole axis objects preserves each view's own range across toggles.
    private void ConfigureBottomAxis(AcousticView acousticView)
    {
        if (view.Model is not { } model)
        {
            return;
        }

        bool timeDomain = IsTimeDomain(acousticView);
        Axis wanted = timeDomain ? timeAxis : frequencyAxis;
        Axis retired = timeDomain ? frequencyAxis : timeAxis;
        if (!model.Axes.Contains(wanted))
        {
            model.Axes.Remove(retired);
            PlotModelStyle.AddAxis(model, wanted);
        }
    }

    private void DrawImpulse(PlotModel model, AcousticImpulseRender impulse)
    {
        (double StartMs, double EndMs)? window = impulse.Step
            ? ImpulseWindowPreview.AddStepTraceSeries(
                model,
                impulse.Traces,
                impulse.SampleRate,
                impulse.GateOffsetMs,
                impulse.LeftMs,
                impulse.PlateauMs,
                impulse.RightMs,
                SeriesTag)
            : ImpulseWindowPreview.AddGatedTraceSeries(
                model,
                impulse.Traces,
                impulse.SampleRate,
                impulse.GateOffsetMs,
                impulse.LeftMs,
                impulse.PlateauMs,
                impulse.RightMs,
                SeriesTag,
                envelopes: true);
        if (window is not { } bounds)
        {
            return;
        }

        // Range re-armed only when the window moves: every chain edit redraws, and re-arming would discard the user's zoom.
        timeAxis.AbsoluteMinimum = bounds.StartMs;
        timeAxis.AbsoluteMaximum = bounds.EndMs;
        if (impulseWindow is { } previous &&
            Math.Abs(previous.StartMs - bounds.StartMs) < WindowMoveEpsilonMs &&
            Math.Abs(previous.EndMs - bounds.EndMs) < WindowMoveEpsilonMs)
        {
            return;
        }

        impulseWindow = bounds;
        timeAxis.Minimum = bounds.StartMs;
        timeAxis.Maximum = bounds.EndMs;
        timeAxis.Reset();
    }

    private static void RemoveCurveSeries(PlotModel model)
    {
        for (int index = model.Series.Count - 1; index >= 0; index--)
        {
            if (Equals(model.Series[index].Tag, SeriesTag))
            {
                model.Series.RemoveAt(index);
            }
        }

        for (int index = model.Annotations.Count - 1; index >= 0; index--)
        {
            if (Equals(model.Annotations[index].Tag, SeriesTag))
            {
                model.Annotations.RemoveAt(index);
            }
        }
    }

    // The nominal range is the hard limit; the range is re-armed only when the nominal moves (as for the time axis).
    private void UpdateLossAxisRange(IReadOnlyList<AcousticCurve> curves)
    {
        double deepest = 0;
        foreach (AcousticCurve curve in curves)
        {
            if (!curve.OnLossAxis)
            {
                continue;
            }

            foreach (SignalPoint point in curve.Points)
            {
                if (double.IsFinite(point.Y))
                {
                    deepest = Math.Min(deepest, point.Y);
                }
            }
        }

        (double lower, double upper) = LossAxisRange(deepest);
        lossAxis.AbsoluteMinimum = lower;
        lossAxis.AbsoluteMaximum = upper;
        if (lossAxisNominal is { } previous &&
            Math.Abs(previous.Lower - lower) < 1e-9 &&
            Math.Abs(previous.Upper - upper) < 1e-9)
        {
            return;
        }

        lossAxisNominal = (lower, upper);
        lossAxis.Minimum = lower;
        lossAxis.Maximum = upper;
        lossAxis.Reset();
    }

    internal static (double Lower, double Upper) LossAxisRange(double deepestDb)
    {
        double lower = Math.Min(
            LossAxisNominalBottomDb,
            Math.Floor(deepestDb / LossAxisStepDb) * LossAxisStepDb);
        return (Math.Max(lower, LossAxisFloorDb), LossAxisTopDb);
    }

    private static void AddCurve(PlotModel model, AcousticCurve curve, string trackerFormat)
    {
        var series = new LineSeries
        {
            Color = curve.Color,
            StrokeThickness = curve.Thickness,
            LineStyle = curve.Style,
            Title = curve.Title,
            Tag = SeriesTag,
            TrackerFormatString = trackerFormat
        };
        if (curve.OnLossAxis)
        {
            series.YAxisKey = LossAxisKey;
        }

        foreach (SignalPoint point in curve.Points)
        {
            series.Points.Add(new DataPoint(point.X, point.Y));
        }

        model.Series.Add(series);
    }
}
