using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>Which curve set the acoustic plot shows.</summary>
internal enum AcousticView
{
    Magnitude,
    Phase,
    Impulse
}

/// <summary>One line on the acoustic plot: its label, points, color and stroke.</summary>
internal sealed record AcousticCurve(
    string Title,
    IReadOnlyList<SignalPoint> Points,
    OxyColor Color,
    double Thickness,
    LineStyle Style);

/// <summary>
/// The impulse view's payload: the processed traces to draw and the gate window
/// they are gated to (the presenter draws the Tukey window and re-arms the static
/// ms axis to the returned bounds).
/// </summary>
internal sealed record AcousticImpulseRender(
    IReadOnlyList<IrPreviewTrace> Traces,
    int SampleRate,
    double GateOffsetMs,
    double LeftMs,
    double PlateauMs,
    double RightMs);

/// <summary>
/// A ready-to-draw frame for the acoustic plot: the hint text plus either a set
/// of curves (magnitude / phase) or the impulse payload. The panel prepares this
/// from the processed channels; the presenter owns the OxyPlot mechanics.
/// </summary>
internal sealed record AcousticRender(
    string HintText,
    IReadOnlyList<AcousticCurve> Curves,
    AcousticImpulseRender? Impulse);

/// <summary>
/// The Virtual DSP main (acoustic) plot: the raw/processed channel magnitudes or
/// phases, their complex sum and the sum loss, or the gated impulse view. Owns
/// the plot model, its three axes (the shared log-frequency axis, the magnitude/
/// phase value axis and the impulse-only linear ms axis), the watermark and hint
/// annotations, the curve series and the axis-range preservation across view
/// switches. The panel hands it a ready <see cref="AcousticRender"/>; it never
/// builds a LineSeries itself.
/// </summary>
internal sealed class VirtualCrossoverAcousticPlot
{
    private const string SeriesTag = "virtual-crossover:curve";
    private const string TrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.00}";

    private readonly PlotView view;
    private readonly PlotLabelsPanelController plotLabels;
    private readonly PlotWatermarkAnnotation hintAnnotation;
    private readonly LinearAxis valueAxis;
    // The two bottom axes: the shared log-frequency axis for the magnitude/phase
    // views and a linear ms axis for the impulse view. Only one is in the model
    // at a time (ConfigureBottomAxis swaps them), so the untagged curve series
    // always bind to the active one.
    private readonly LogarithmicAxis frequencyAxis;
    private readonly LinearAxis timeAxis;

    // The display window the impulse time axis was last armed to, so a redraw
    // that lands on the same window leaves the user's zoom alone. A move smaller
    // than this is recomputation noise rather than a new window — it is orders
    // of magnitude below one sample at any rate the app records at.
    private const double WindowMoveEpsilonMs = 1e-6;
    private (double StartMs, double EndMs)? impulseWindow;

    public VirtualCrossoverAcousticPlot(PlotView view, string hint, AcousticView initialView)
    {
        ArgumentNullException.ThrowIfNull(view);
        this.view = view;

        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        frequencyAxis = (LogarithmicAxis)model.Axes[^1];
        // The impulse view runs on an absolute-time axis. It zooms and pans like
        // any other: the interesting part of an impulse view is the millisecond
        // around each front, and the gate window it opens on is far wider than
        // that. The window stays its hard limit — outside it the
        // traces hold no data — and DrawImpulse only re-arms the range when the
        // window itself moves, so a zoom survives the constant redraws.
        timeAxis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "ms",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        };
        // The absolute pan/zoom limits live in ConfigureForView: they differ
        // between the magnitude (dB), phase (deg) and impulse (normalized) views.
        valueAxis = new LinearAxis
        {
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        };
        model.Axes.Add(valueAxis);

        model.Annotations.Add(new PlotWatermarkAnnotation
        {
            Text = "Virtual DSP",
            TextColor = OxyColor.FromAColor(10, OxyColors.White),
            FontSize = 70,
            FontWeight = FontWeights.Bold
        });
        hintAnnotation = new PlotWatermarkAnnotation
        {
            Text = hint,
            VerticalPosition = 0.66,
            TextColor = OxyColor.FromRgb(230, 184, 0),
            FontSize = 15,
            FontWeight = FontWeights.Bold
        };
        model.Annotations.Add(hintAnnotation);

        view.Model = model;
        ConfigureForView(initialView);
        PlotInteraction.Enable(view);
        plotLabels = new PlotLabelsPanelController(view, () => Mode.VirtualCrossover);
    }

    // Magnitude and phase reuse one value axis object so pan/zoom of the frequency
    // axis survives the toggle; only the value scale and its zoom lock are
    // re-armed. The impulse view additionally swaps the bottom axis to the linear
    // ms one.
    public void ConfigureForView(AcousticView acousticView)
    {
        if (acousticView == AcousticView.Impulse)
        {
            // Every trace is normalized to its own peak, exactly like the IR
            // Gate preview, so the scale is unitless.
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
        else
        {
            valueAxis.Title = "dB";
            valueAxis.AbsoluteMinimum = -90;
            // These traces are the measured, loopback-referenced magnitudes, so
            // the pan ceiling is the shared one: attenuating the reference lifts
            // every curve here by the same amount (see PlotModelStyle).
            valueAxis.AbsoluteMaximum = PlotModelStyle.RelativeDecibelAbsoluteMaximum;
            valueAxis.Minimum = double.NaN;
            valueAxis.Maximum = double.NaN;
            valueAxis.MajorStep = double.NaN;
        }

        // ±180° is the ENTIRE range a wrapped phase can occupy, so there is
        // nothing above or below to travel to: zooming the height would only cut
        // curves off screen while implying the rest is somewhere out of view.
        // Locked rather than merely bounded, which also drops the axis from the
        // double-click limits dialog (it offers the zoomable axes only) and
        // leaves a drag panning the frequency axis alone.
        bool phase = acousticView == AcousticView.Phase;
        valueAxis.IsZoomEnabled = !phase;
        valueAxis.IsPanEnabled = !phase;

        ConfigureBottomAxis(acousticView);
        valueAxis.Reset();
        view.InvalidatePlot(false);
    }

    // Directly updates the hint annotation outside a full redraw (the session
    // load shows a note before the sources resolve).
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
            DrawImpulse(model, impulse);
        }
        else
        {
            foreach (AcousticCurve curve in render.Curves)
            {
                AddCurve(model, curve.Title, curve.Points, curve.Color, curve.Thickness, curve.Style);
            }
        }

        plotLabels.Refresh();
        model.InvalidatePlot(true);
    }

    // Keeps exactly one bottom axis in the model: the log-frequency axis for the
    // magnitude/phase views, the linear ms axis for the impulse view. Swapping
    // whole axis objects (instead of reconfiguring one) preserves each view's own
    // range across toggles.
    private void ConfigureBottomAxis(AcousticView acousticView)
    {
        if (view.Model is not { } model)
        {
            return;
        }

        bool impulse = acousticView == AcousticView.Impulse;
        Axis wanted = impulse ? timeAxis : frequencyAxis;
        Axis retired = impulse ? frequencyAxis : timeAxis;
        if (!model.Axes.Contains(wanted))
        {
            model.Axes.Remove(retired);
            model.Axes.Add(wanted);
        }
    }

    private void DrawImpulse(PlotModel model, AcousticImpulseRender impulse)
    {
        // The impulse view is the gate dialog's IR preview promoted to the main
        // plot: every processed channel IR on the shared absolute timeline, each
        // normalized to its own in-window peak, with the phase-gate Tukey window
        // drawn where it sits.
        (double StartMs, double EndMs)? window =
            ImpulseWindowPreview.AddGatedTraceSeries(
                model,
                impulse.Traces,
                impulse.SampleRate,
                impulse.GateOffsetMs,
                impulse.LeftMs,
                impulse.PlateauMs,
                impulse.RightMs,
                SeriesTag);
        if (window is not { } bounds)
        {
            return;
        }

        // The window the series were built for is the axis's hard limit either
        // way. The RANGE, though, is re-armed only when that window actually
        // moves: the axis zooms now, and every chain edit redraws this view, so
        // re-arming each time would throw away the zoom the user just set.
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

        // The impulse view marks its gate-offset annotation with the same tag,
        // so a redraw sweeps it together with the curves.
        for (int index = model.Annotations.Count - 1; index >= 0; index--)
        {
            if (Equals(model.Annotations[index].Tag, SeriesTag))
            {
                model.Annotations.RemoveAt(index);
            }
        }
    }

    private static void AddCurve(
        PlotModel model,
        string title,
        IReadOnlyList<SignalPoint> points,
        OxyColor color,
        double thickness,
        LineStyle lineStyle)
    {
        var series = new LineSeries
        {
            Color = color,
            StrokeThickness = thickness,
            LineStyle = lineStyle,
            Title = title,
            Tag = SeriesTag,
            TrackerFormatString = TrackerFormat
        };
        foreach (SignalPoint point in points)
        {
            series.Points.Add(new DataPoint(point.X, point.Y));
        }

        model.Series.Add(series);
    }
}
