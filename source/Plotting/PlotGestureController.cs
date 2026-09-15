using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

/// <summary>The single mouse/keyboard map for every plot, shaped after REW's graph panel (user copy: REFERENCE.md, Graph Zoom and Limits).
/// See docs/tech/plot-interaction.md#gesture-map.</summary>
internal sealed class PlotGestureController : PlotController
{
    private const int UndoDepth = 32;

    private const int TipDurationMs = 4000;

    private readonly PlotView view;
    private readonly LinkedList<IReadOnlyList<PlotAxisViewport>> zoomUndo = new();
    private readonly PlotZoomButtonsAnnotation zoomButtons = new();

    // OxyPlot's element tooltips are not wired in its WinForms view, hence a plain ToolTip.
    private readonly ToolTip graphTip = new() { ShowAlways = true };

    // Owned here, not by the manipulator: the box outlives the drag. See docs/tech/plot-interaction.md#zoom-box.
    private readonly PlotZoomRectangleAnnotation zoomBox = new();
    private PlotModel? zoomBoxModel;
    private bool zoomBoxPending;
    private bool zoomBoxHovered;

    // Model reference is not enough: VDSP re-arms axes to other quantities inside one model.
    private IReadOnlyList<PlotAxisIdentity> zoomBoxAxes = Array.Empty<PlotAxisIdentity>();

    // The second press of a double tap arrives as a double click; must not open the limits dialog.
    private bool zoomBoxJustClicked;

    // OxyPlot key events carry no position, and keyboard zoom centres on the pointer.
    private ScreenPoint pointer;
    private PlotModel? buttonsModel;
    private PlotZoomButton? hoveredButton;

    // Undo entries name axes by key, whose meaning varies across builds and re-arms. See docs/tech/plot-interaction.md#undo-stack.
    private PlotModel? undoModel;
    private IReadOnlyList<PlotAxisIdentity> undoAxes = Array.Empty<PlotAxisIdentity>();

    public PlotGestureController(PlotView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        this.view = view;
        view.MouseMove += (_, e) => TrackPointer(new ScreenPoint(e.X, e.Y));
        view.MouseLeave += (_, _) => HideZoomButtons();
        view.Disposed += (_, _) => graphTip.Dispose();

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
        this.BindMouseWheel(OxyModifierKeys.Control, WheelCommand(AxisPreference.Y, factor: 1));
    }

    private void BindMouseGestures()
    {
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

        // Undo is recorded by the zooming click, not here: a box only read must not touch the stack.
        this.BindMouseDown(
            OxyMouseButton.Right,
            OxyModifierKeys.Control,
            new DelegatePlotCommand<OxyMouseDownEventArgs>((target, controller, args) =>
                controller.AddMouseManipulator(
                    target,
                    new PlotZoomRectangleManipulator(target, this),
                    args)));

        // A second click on a zoom button arrives as a double click and must zoom again, not open the dialog.
        this.BindMouseDown(
            OxyMouseButton.Left,
            OxyModifierKeys.None,
            clickCount: 2,
            new DelegatePlotCommand<OxyMouseDownEventArgs>((target, _, args) =>
            {
                if (zoomBoxJustClicked)
                {
                    zoomBoxJustClicked = false;
                    return;
                }

                if (TryClickZoomButton(target, args.Position))
                {
                    return;
                }

                GraphLimitsDialog.ShowFor(view);
            }));

        // Order: waiting zoom box, then zoom button, then OxyPlot's tracker.
        this.BindMouseDown(
            OxyMouseButton.Left,
            new DelegatePlotCommand<OxyMouseDownEventArgs>((target, controller, args) =>
            {
                zoomBoxJustClicked = false;
                if (TryClickZoomBox(target, args.Position) ||
                    TryClickZoomButton(target, args.Position))
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

    /// <summary>Invalidates only on zoom-button presence/hover transitions, so a plain move does not repaint a waterfall.</summary>
    private void TrackPointer(ScreenPoint position)
    {
        pointer = position;
        if (view.ActualModel is not PlotModel model)
        {
            return;
        }

        // Before the buttons: their attach returns early for an unchanged model, which is exactly the re-arm case.
        DropZoomBoxOfAnotherView(model);
        AttachZoomButtons(model);
        TrackZoomBoxHover(model, position);
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
            graphTip.Hide(view);
            return;
        }

        Axis? axis = PlotAxisZoom.FindZoomableAxis(model, button.Horizontal);
        string name = axis == null
            ? (button.Horizontal ? "horizontal axis" : "vertical axis")
            : PlotAxisZoom.DescribeAxis(axis);
        ShowTip($"{(button.ZoomIn ? "Zoom in" : "Zoom out")} — {name}", position);
    }

    private void ShowTip(string text, ScreenPoint position) =>
        graphTip.Show(text, view, (int)position.X + 16, (int)position.Y + 20, TipDurationMs);

    private void HideZoomButtons()
    {
        hoveredButton = null;
        graphTip.Hide(view);
        if (zoomButtons.Pointer == null)
        {
            return;
        }

        zoomButtons.Pointer = null;
        view.InvalidatePlot(false);
    }

    public void BeginZoomBox()
    {
        DismissZoomBox();
        if (view.ActualModel is not PlotModel model)
        {
            return;
        }

        zoomBoxModel = model;
        zoomBoxAxes = PlotAxisIdentities.Describe(model);
        model.Annotations.Add(zoomBox);
    }

    public void UpdateZoomBox(PlotZoomBox? box, ScreenPoint start, ScreenPoint current) =>
        ShowZoomBox(box, start, current, pending: false);

    /// <summary>Every drawn box is kept however thin (it measures); zoomability is decided at the click.</summary>
    public void FinishZoomBox(PlotZoomBox box, ScreenPoint start, ScreenPoint end)
    {
        if (PlotZoomRectangleReadout.WasDrawn(start, end))
        {
            ShowZoomBox(box, start, end, pending: true);
            return;
        }

        DismissZoomBox();
    }

    /// <summary>Inside: zoom and record undo. Too thin: keep the box and name the side. Elsewhere: release and continue normally.</summary>
    private bool TryClickZoomBox(IPlotView target, ScreenPoint position)
    {
        if (!zoomBoxPending || zoomBox.Box is not PlotZoomBox box)
        {
            return false;
        }

        // Also checked here: a view can be re-armed without the mouse moving.
        if (zoomBoxModel is not PlotModel model ||
            !PlotAxisIdentities.Match(target.ActualModel, model, zoomBoxAxes) ||
            !box.CanZoom ||
            !box.Contains(model.PlotArea, position))
        {
            DismissZoomBox();
            return false;
        }

        if (PlotZoomRectangleReadout.RefusalFor(box, box.Screen(model.PlotArea)) is string refusal)
        {
            ShowTip(refusal, position);
            return true;
        }

        PushZoomUndo();
        box.Zoom();
        DismissZoomBox();
        zoomBoxJustClicked = true;
        target.InvalidatePlot(false);
        return true;
    }

    private void ShowZoomBox(
        PlotZoomBox? box,
        ScreenPoint start,
        ScreenPoint current,
        bool pending)
    {
        if (zoomBoxModel == null)
        {
            return;
        }

        zoomBox.Box = box;
        zoomBox.Axes = zoomBoxAxes;
        zoomBox.AnchorRight = current.X >= start.X;
        zoomBox.AnchorBottom = current.Y >= start.Y;
        zoomBox.Text = box?.Describe() ?? string.Empty;
        zoomBox.Hint = box is PlotZoomBox drawn
            ? PlotZoomRectangleReadout.HintFor(drawn, pending)
            : string.Empty;
        zoomBoxPending = pending && box != null;
        view.InvalidatePlot(false);
    }

    private void DismissZoomBox()
    {
        zoomBoxPending = false;
        SetZoomBoxHovered(false);
        if (zoomBoxModel == null)
        {
            return;
        }

        zoomBoxModel.Annotations.Remove(zoomBox);
        zoomBoxModel = null;
        zoomBoxAxes = Array.Empty<PlotAxisIdentity>();
        zoomBox.Axes = zoomBoxAxes;
        zoomBox.Box = null;
        zoomBox.Text = string.Empty;
        zoomBox.Hint = string.Empty;
        view.InvalidatePlot(false);
    }

    private void TrackZoomBoxHover(PlotModel model, ScreenPoint position) =>
        SetZoomBoxHovered(
            zoomBoxPending &&
            zoomBox.Box is PlotZoomBox box &&
            box.CanZoom &&
            box.Contains(model.PlotArea, position));

    /// <summary>The box holds axis values, meaningless once another model or a re-armed axis is under it.</summary>
    private void DropZoomBoxOfAnotherView(PlotModel model)
    {
        if (zoomBoxModel == null || PlotAxisIdentities.Match(model, zoomBoxModel, zoomBoxAxes))
        {
            return;
        }

        DismissZoomBox();
    }

    private void SetZoomBoxHovered(bool hovered)
    {
        if (hovered == zoomBoxHovered)
        {
            return;
        }

        zoomBoxHovered = hovered;
        view.Cursor = hovered ? Cursors.Hand : Cursors.Default;
    }

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
            OxyKey.Escape,
            new DelegatePlotCommand<OxyKeyEventArgs>((_, _, _) => DismissZoomBox()));

        // Handling F1 stops DefWindowProc raising a second, empty help request.
        this.BindKeyDown(
            OxyKey.F1,
            new DelegatePlotCommand<OxyKeyEventArgs>((_, _, args) =>
            {
                GraphHelpDialog.ShowFor(view.FindForm());
                args.Handled = true;
            }));
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
        DropUndoOfAnotherModel();
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
        DropUndoOfAnotherModel();
        if (zoomUndo.Last == null)
        {
            return;
        }

        IReadOnlyList<PlotAxisViewport> viewports = zoomUndo.Last.Value;
        zoomUndo.RemoveLast();
        PlotAxisViewport.Apply(view.ActualModel, viewports);
        view.InvalidatePlot(false);
    }

    /// <summary>Undo does not outlive a model change or axis re-arm; <see cref="PlotViewportMemory"/> carries zoom across rebuilds.</summary>
    private void DropUndoOfAnotherModel()
    {
        if (PlotAxisIdentities.Match(view.ActualModel, undoModel, undoAxes))
        {
            return;
        }

        zoomUndo.Clear();
        undoModel = view.ActualModel;
        undoAxes = PlotAxisIdentities.Describe(view.ActualModel);
    }
}
