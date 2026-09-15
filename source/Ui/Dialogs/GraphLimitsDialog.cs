using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;

namespace Resonalyze;

/// <summary>REW-style graph limits dialog. Typed limits persist through the same viewport carry-over as a dragged zoom
/// (<see cref="PlotAxisViewport"/>), since the model is rebuilt on every change.</summary>
internal sealed partial class GraphLimitsDialog : Form
{
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

        buttonFitY.Enabled = verticalAxis != null;
        LoadValues();
    }

    /// <summary>Nothing opens when every axis is pinned (waterfall, burst decay).</summary>
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
            editor.DecimalPlaces = logarithmic ? 0 : 2;
            editor.Increment = logarithmic ? 10 : 1;
            editor.Minimum = EditorLimit(axis.AbsoluteMinimum, -DefaultEditorLimit, logarithmic);
            editor.Maximum = EditorLimit(axis.AbsoluteMaximum, DefaultEditorLimit, logarithmic);
        }
    }

    // Clamped in double before the cast: unbounded OxyPlot axes carry double.MinValue/MaxValue, which overflowed decimal.
    // A log axis gets a positive floor.
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

        // Read back the clamped values so the user learns where the axis limit is.
        LoadValues();
    }

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

        // ActualMinimum/Maximum settle only on render; update in place to read the applied range now.
        if (view.ActualModel is IPlotModel model)
        {
            model.Update(false);
        }
    }
}
