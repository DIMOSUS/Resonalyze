using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;

namespace Resonalyze;

/// <summary>
/// REW's graph limits dialog: the four numbers behind the zoom gestures, so a range
/// can be typed instead of dragged and two measurements can be framed identically.
/// Opened by double-clicking the plot, which is where REW users reach for it.
///
/// It edits the axes of the model that is on screen. That model is rebuilt on every
/// settings change and every new measurement, so what makes typed limits stick is
/// the same viewport carry-over that keeps a dragged zoom
/// (<see cref="PlotAxisViewport"/>), not anything stored here.
/// </summary>
internal sealed partial class GraphLimitsDialog : Form
{
    // Fallback range for the numeric editors when an axis sets no absolute limit.
    // Wide enough for every quantity the app plots (dB, degrees, milliseconds,
    // hertz), finite so the editors have something to clamp to.
    private const decimal DefaultEditorLimit = 1_000_000;

    private readonly PlotView view;
    private readonly Axis? horizontalAxis;
    private readonly Axis? verticalAxis;

    private GraphLimitsDialog(PlotView view, Axis? horizontalAxis, Axis? verticalAxis)
    {
        InitializeComponent();
        this.view = view;
        this.horizontalAxis = horizontalAxis;
        this.verticalAxis = verticalAxis;

        AcceptButton = buttonApply;
        buttonApply.Click += (_, _) => Apply();
        buttonFit.Click += (_, _) => Fit(verticalOnly: false);
        buttonFitY.Click += (_, _) => Fit(verticalOnly: true);
        buttonDefaults.Click += (_, _) => RestoreDefaults();

        ConfigureAxisRow(
            verticalAxis,
            "Vertical axis",
            labelVerticalAxis,
            labelTop,
            numericTop,
            labelBottom,
            numericBottom);
        ConfigureAxisRow(
            horizontalAxis,
            "Horizontal axis",
            labelHorizontalAxis,
            labelRight,
            numericRight,
            labelLeft,
            numericLeft);

        // "Fit Y to data" is only meaningful while there is a vertical axis to fit.
        buttonFitY.Enabled = verticalAxis != null;
        LoadValues();
    }

    /// <summary>
    /// Opens the dialog for the plot's own axes. A plot whose axes are all pinned
    /// (the waterfall and burst decay, which own their scale) has nothing to edit,
    /// so nothing opens.
    /// </summary>
    public static void ShowFor(PlotView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        PlotModel? model = view.ActualModel;
        if (model == null)
        {
            return;
        }

        Axis? horizontal = PlotAxisZoom.FindZoomableAxis(model, horizontal: true);
        Axis? vertical = PlotAxisZoom.FindZoomableAxis(model, horizontal: false);
        if (horizontal == null && vertical == null)
        {
            return;
        }

        using var dialog = new GraphLimitsDialog(view, horizontal, vertical);
        dialog.ShowDialog(view.FindForm());
    }

    private static void ConfigureAxisRow(
        Axis? axis,
        string fallbackHeader,
        Label header,
        Label maximumLabel,
        DarkNumericUpDown maximumEditor,
        Label minimumLabel,
        DarkNumericUpDown minimumEditor)
    {
        header.Text = axis == null ? fallbackHeader : $"{fallbackHeader} — {PlotAxisZoom.DescribeAxis(axis)}";
        bool enabled = axis != null;
        maximumEditor.Enabled = enabled;
        minimumEditor.Enabled = enabled;
        UiStyle.SetTextEnabledLook(header, enabled);
        UiStyle.SetTextEnabledLook(maximumLabel, enabled);
        UiStyle.SetTextEnabledLook(minimumLabel, enabled);
        if (axis == null)
        {
            return;
        }

        bool logarithmic = axis is LogarithmicAxis;
        foreach (DarkNumericUpDown editor in new[] { maximumEditor, minimumEditor })
        {
            // A logarithmic axis is read in hertz, where a decimal place is noise and
            // a step of one is a rounding error at the top of the range.
            editor.DecimalPlaces = logarithmic ? 0 : 2;
            editor.Increment = logarithmic ? 10 : 1;
            editor.Minimum = EditorLimit(axis.AbsoluteMinimum, -DefaultEditorLimit, logarithmic);
            editor.Maximum = EditorLimit(axis.AbsoluteMaximum, DefaultEditorLimit, logarithmic);
        }
    }

    // A logarithmic axis has no meaning at or below zero, so an unbounded one still
    // gets a positive floor.
    //
    // The clamp happens in DOUBLE, before the cast. An axis that was never given absolute
    // bounds carries OxyPlot's own defaults — double.MinValue and double.MaxValue — which
    // are perfectly finite and some 290 orders of magnitude outside what a decimal can
    // hold, so casting first threw OverflowException and took the double click down with
    // it. Every mode that leaves an axis unbounded reaches this, the impulse view's level
    // axis and the autocorrelation plot among them.
    internal static decimal EditorLimit(
        double absoluteLimit, decimal fallback, bool logarithmic)
    {
        decimal limit = double.IsFinite(absoluteLimit)
            ? (decimal)Math.Clamp(
                absoluteLimit,
                (double)-DefaultEditorLimit,
                (double)DefaultEditorLimit)
            : fallback;
        return logarithmic ? Math.Max(limit, 0.01m) : limit;
    }

    private void LoadValues()
    {
        if (verticalAxis != null)
        {
            numericTop.Value = numericTop.ClampValue(verticalAxis.ActualMaximum);
            numericBottom.Value = numericBottom.ClampValue(verticalAxis.ActualMinimum);
        }

        if (horizontalAxis != null)
        {
            numericLeft.Value = numericLeft.ClampValue(horizontalAxis.ActualMinimum);
            numericRight.Value = numericRight.ClampValue(horizontalAxis.ActualMaximum);
        }
    }

    private void Apply()
    {
        ApplyAxis(verticalAxis, numericBottom.Value, numericTop.Value);
        ApplyAxis(horizontalAxis, numericLeft.Value, numericRight.Value);
        RefreshView(view);

        // Read back what the axes accepted: they clamp to their own absolute limits,
        // and showing the clamped numbers is how the user learns where the wall is.
        LoadValues();
    }

    /// <summary>
    /// Hands the axes back to the mode that built them — the same thing Home and
    /// <c>A</c> do on the plot itself. It lives here because this dialog is where a
    /// user goes looking for the scale, and because the double click that opens it
    /// used to be the reset.
    /// </summary>
    private void RestoreDefaults()
    {
        if (view.ActualModel is not PlotModel model)
        {
            return;
        }

        foreach (Axis axis in model.Axes)
        {
            axis.Reset();
        }

        RefreshView(view);
        LoadValues();
    }

    private void Fit(bool verticalOnly)
    {
        if (!PlotAxisFit.FitToData(view.ActualModel, verticalOnly))
        {
            return;
        }

        RefreshView(view);
        LoadValues();
    }

    private static void ApplyAxis(Axis? axis, decimal minimum, decimal maximum)
    {
        if (axis == null || maximum <= minimum)
        {
            return;
        }

        axis.Zoom((double)minimum, (double)maximum);
    }

    private static void RefreshView(PlotView view)
    {
        view.InvalidatePlot(false);

        // ActualMinimum/ActualMaximum only settle on render; update the model in
        // place so the dialog can read the applied range without waiting for a paint.
        if (view.ActualModel is IPlotModel model)
        {
            model.Update(false);
        }
    }
}
