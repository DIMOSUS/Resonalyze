using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;

namespace Resonalyze;

/// <summary>
/// The input bindings for every plot in the app, laid out to match REW's graph
/// panel: someone who tunes cars with REW open on the other laptop should not have
/// to relearn the mouse here.
///
/// What REW does, and where each gesture lands:
/// <list type="bullet">
/// <item>wheel — zooms both axes around the pointer; Alt for a fine step.</item>
/// <item>Shift + wheel, or the pointer over an axis — zooms that one axis. The
/// second half is OxyPlot's own behaviour (<see cref="PlotModel.GetAxesFromPoint"/>
/// reports only the axis under the pointer), which is why the frequency axis had to
/// stop refusing zoom for it to work.</item>
/// <item>wheel over the END of an axis — moves that single limit, leaving the
/// opposite end where it is.</item>
/// <item>x / Shift+X and y / Shift+Y — zoom one axis out / in by about two.</item>
/// <item>middle drag — variable zoom (<see cref="PlotVariableZoomManipulator"/>);
/// Ctrl + right drag — zoom to area. Both are undoable with Ctrl+Z.</item>
/// <item>right drag — pan. Ctrl+Alt+F / Ctrl+Alt+Y — fit to data / fit Y to
/// data. Double click — the graph limits dialog.</item>
/// <item>the plus/minus buttons drawn against each axis while the pointer is over
/// the plot (<see cref="PlotZoomButtons"/>).</item>
/// </list>
/// Two bindings have no REW counterpart and are kept because they are what this app
/// did before: Ctrl + wheel zooms the vertical axis only (the plain wheel used to,
/// since the frequency axis refused zoom), and Home / A resets an axis to the
/// model's own scale (the double click used to, before REW's limits dialog took
/// it). Neither shadows a REW gesture.
/// </summary>
internal sealed class PlotGestureController : PlotController
{
    /// <summary>
    /// How many zoom gestures can be walked back. Deep enough to undo a hunt around
    /// a resonance, shallow enough that the stack is not a memory of the session.
    /// </summary>
    private const int UndoDepth = 32;

    /// <summary>How long the zoom buttons' hint stays up once shown, in milliseconds.</summary>
    private const int ZoomButtonTipDurationMs = 4000;

    private readonly PlotView view;
    private readonly LinkedList<IReadOnlyList<PlotAxisViewport>> zoomUndo = new();
    private readonly PlotZoomButtonsAnnotation zoomButtons = new();

    // A plus and a minus against an axis do not say WHICH axis, or that they zoom
    // at all, so the hovered one names itself. OxyPlot's own element tooltips are
    // not wired up in its WinForms view, hence a plain ToolTip on the control.
    private readonly ToolTip zoomButtonTip = new() { ShowAlways = true };

    // Where the pointer last was, in the view's coordinates. The keyboard zoom
    // commands need it: OxyPlot's key events carry no position, and REW zooms the
    // axis around the pointer rather than around the centre of the plot.
    private ScreenPoint pointer;
    private PlotModel? buttonsModel;
    private PlotZoomButton? hoveredButton;

    public PlotGestureController(PlotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        this.view = view;
        view.MouseMove += (_, e) => TrackPointer(new ScreenPoint(e.X, e.Y));
        view.MouseLeave += (_, _) => HideZoomButtons();
        view.Disposed += (_, _) => zoomButtonTip.Dispose();

        BindWheelGestures();
        BindMouseGestures();
        BindKeyboardGestures();
    }

    private void BindWheelGestures()
    {
        this.BindMouseWheel(WheelCommand(AxisPreference.None, factor: 1));
        this.BindMouseWheel(
            OxyModifierKeys.Alt,
            WheelCommand(AxisPreference.None, PlotAxisZoom.FineWheelFactor));
        this.BindMouseWheel(OxyModifierKeys.Shift, WheelCommand(AxisPreference.X, factor: 1));
        // Replaces OxyPlot's default Ctrl + wheel fine step, which now lives on Alt
        // where REW keeps it.
        this.BindMouseWheel(OxyModifierKeys.Control, WheelCommand(AxisPreference.Y, factor: 1));
    }

    private void BindMouseGestures()
    {
        // REW's variable zoom. OxyPlot has the zoom rectangle on the middle button by
        // default; REW puts that on Ctrl + right drag, where OxyPlot binds it too.
        this.BindMouseDown(
            OxyMouseButton.Middle,
            new DelegatePlotCommand<OxyMouseDownEventArgs>((target, controller, args) =>
            {
                PushZoomUndo();
                controller.AddMouseManipulator(
                    target,
                    new PlotVariableZoomManipulator(target),
                    args);
            }));

        // Same manipulator OxyPlot binds here; rebound only so the gesture records an
        // undo step first.
        this.BindMouseDown(
            OxyMouseButton.Right,
            OxyModifierKeys.Control,
            new DelegatePlotCommand<OxyMouseDownEventArgs>((target, controller, args) =>
            {
                PushZoomUndo();
                controller.AddMouseManipulator(
                    target,
                    new ZoomRectangleManipulator(target),
                    args);
            }));

        // A second click on a zoom button arrives here as a DOUBLE click, so this
        // binding has to answer it as another zoom step: clicking a button twice is
        // how anyone zooms twice, and it must not turn into the limits dialog.
        this.BindMouseDown(
            OxyMouseButton.Left,
            OxyModifierKeys.None,
            clickCount: 2,
            new DelegatePlotCommand<OxyMouseDownEventArgs>((target, _, args) =>
            {
                if (TryClickZoomButton(target, args.Position))
                {
                    return;
                }

                GraphLimitsDialog.ShowFor(view);
            }));

        // A plain left press either hits an on-graph zoom button or does what
        // OxyPlot binds it to — the snapping tracker.
        this.BindMouseDown(
            OxyMouseButton.Left,
            new DelegatePlotCommand<OxyMouseDownEventArgs>((target, controller, args) =>
            {
                if (TryClickZoomButton(target, args.Position))
                {
                    return;
                }

                controller.AddMouseManipulator(
                    target,
                    new TrackerManipulator(target) { Snap = true, PointsOnly = false },
                    args);
            }));
    }

    private bool TryClickZoomButton(IPlotView target, ScreenPoint position)
    {
        if (target.ActualModel is not PlotModel model ||
            !PlotZoomButtons.TryHit(model, position, out PlotZoomButton button))
        {
            return false;
        }

        PushZoomUndo();
        if (PlotAxisZoom.ZoomAxisAt(
            model,
            button.Center,
            button.Horizontal,
            button.ZoomIn ? PlotAxisZoom.StepZoomInScale : PlotAxisZoom.StepZoomOutScale))
        {
            target.InvalidatePlot(false);
        }

        return true;
    }

    /// <summary>
    /// Follows the pointer for the keyboard zoom commands and for the on-graph zoom
    /// buttons, which REW only shows while the pointer is over the graph. Only the
    /// buttons' presence and which one is hovered change any pixels, so the view is
    /// invalidated on those transitions alone — a plain move across the plot must
    /// not repaint a waterfall.
    /// </summary>
    private void TrackPointer(ScreenPoint position)
    {
        pointer = position;
        if (view.ActualModel is not PlotModel model)
        {
            return;
        }

        AttachZoomButtons(model);
        ScreenPoint? shown = model.PlotArea.Contains(position.X, position.Y)
            ? position
            : null;
        PlotZoomButton? hovered =
            shown != null && PlotZoomButtons.TryHit(model, position, out PlotZoomButton hit)
                ? hit
                : null;

        bool visibilityChanged = zoomButtons.Pointer.HasValue != shown.HasValue;
        bool hoverChanged = !EqualityComparer<PlotZoomButton?>.Default.Equals(hovered, hoveredButton);
        zoomButtons.Pointer = shown;
        if (hoverChanged)
        {
            hoveredButton = hovered;
            ShowZoomButtonTip(model, hovered, position);
        }

        if (visibilityChanged || hoverChanged)
        {
            view.InvalidatePlot(false);
        }
    }

    private void ShowZoomButtonTip(PlotModel model, PlotZoomButton? hovered, ScreenPoint position)
    {
        if (hovered is not PlotZoomButton button)
        {
            zoomButtonTip.Hide(view);
            return;
        }

        Axis? axis = PlotAxisZoom.FindZoomableAxis(model, button.Horizontal);
        string name = axis == null
            ? (button.Horizontal ? "horizontal axis" : "vertical axis")
            : PlotAxisZoom.DescribeAxis(axis);
        zoomButtonTip.Show(
            $"{(button.ZoomIn ? "Zoom in" : "Zoom out")} — {name}",
            view,
            (int)position.X + 16,
            (int)position.Y + 20,
            ZoomButtonTipDurationMs);
    }

    private void HideZoomButtons()
    {
        hoveredButton = null;
        zoomButtonTip.Hide(view);
        if (zoomButtons.Pointer == null)
        {
            return;
        }

        zoomButtons.Pointer = null;
        view.InvalidatePlot(false);
    }

    // The models are rebuilt constantly and the annotation belongs to whichever one
    // is on screen, so it moves with the view rather than being added by every
    // factory that builds a plot.
    private void AttachZoomButtons(PlotModel model)
    {
        if (ReferenceEquals(buttonsModel, model))
        {
            return;
        }

        buttonsModel?.Annotations.Remove(zoomButtons);
        buttonsModel = model;
        if (!model.Annotations.Contains(zoomButtons))
        {
            model.Annotations.Add(zoomButtons);
        }
    }

    private void BindKeyboardGestures()
    {
        this.BindKeyDown(OxyKey.X, KeyZoomCommand(horizontal: true, PlotAxisZoom.StepZoomOutScale));
        this.BindKeyDown(
            OxyKey.X,
            OxyModifierKeys.Shift,
            KeyZoomCommand(horizontal: true, PlotAxisZoom.StepZoomInScale));
        this.BindKeyDown(OxyKey.Y, KeyZoomCommand(horizontal: false, PlotAxisZoom.StepZoomOutScale));
        this.BindKeyDown(
            OxyKey.Y,
            OxyModifierKeys.Shift,
            KeyZoomCommand(horizontal: false, PlotAxisZoom.StepZoomInScale));

        this.BindKeyDown(
            OxyKey.Z,
            OxyModifierKeys.Control,
            new DelegatePlotCommand<OxyKeyEventArgs>((_, _, _) => UndoZoom()));
        this.BindKeyDown(
            OxyKey.F,
            OxyModifierKeys.Control | OxyModifierKeys.Alt,
            FitCommand(verticalOnly: false));
        this.BindKeyDown(
            OxyKey.Y,
            OxyModifierKeys.Control | OxyModifierKeys.Alt,
            FitCommand(verticalOnly: true));
    }

    private IViewCommand<OxyMouseWheelEventArgs> WheelCommand(
        AxisPreference preference,
        double factor) =>
        new DelegatePlotCommand<OxyMouseWheelEventArgs>((target, _, args) =>
            HandleWheel(target, args, preference, factor));

    private static void HandleWheel(
        IPlotView target,
        OxyMouseWheelEventArgs args,
        AxisPreference preference,
        double factor)
    {
        PlotModel? model = target.ActualModel;
        if (model != null &&
            PlotAxisZoom.TryGetAxisEnd(model, args.Position, out Axis? axis, out bool maximumEnd) &&
            axis != null)
        {
            PlotAxisZoom.ZoomEnd(
                axis,
                maximumEnd,
                PlotAxisZoom.ScaleFromWheelDelta(args.Delta, factor));
            target.InvalidatePlot(false);
            return;
        }

        new ZoomStepManipulator(target)
        {
            AxisPreference = preference,
            Step = args.Delta * 0.001 * factor,
        }.Started(args);
    }

    private IViewCommand<OxyKeyEventArgs> KeyZoomCommand(bool horizontal, double scale) =>
        new DelegatePlotCommand<OxyKeyEventArgs>((target, _, _) =>
        {
            PlotModel? model = target.ActualModel;
            if (model == null ||
                !PlotAxisZoom.ZoomAxisAt(
                    model,
                    PlotAxisZoom.ClampToPlotArea(model, pointer),
                    horizontal,
                    scale))
            {
                return;
            }

            target.InvalidatePlot(false);
        });

    private IViewCommand<OxyKeyEventArgs> FitCommand(bool verticalOnly) =>
        new DelegatePlotCommand<OxyKeyEventArgs>((target, _, _) =>
        {
            PushZoomUndo();
            if (PlotAxisFit.FitToData(target.ActualModel, verticalOnly))
            {
                target.InvalidatePlot(false);
            }
        });

    private void PushZoomUndo()
    {
        IReadOnlyList<PlotAxisViewport> viewports = PlotAxisViewport.Capture(view.ActualModel);
        if (viewports.Count == 0)
        {
            return;
        }

        zoomUndo.AddLast(viewports);
        while (zoomUndo.Count > UndoDepth)
        {
            zoomUndo.RemoveFirst();
        }
    }

    private void UndoZoom()
    {
        if (zoomUndo.Last == null)
        {
            return;
        }

        IReadOnlyList<PlotAxisViewport> viewports = zoomUndo.Last.Value;
        zoomUndo.RemoveLast();
        PlotAxisViewport.Apply(view.ActualModel, viewports);
        view.InvalidatePlot(false);
    }
}
