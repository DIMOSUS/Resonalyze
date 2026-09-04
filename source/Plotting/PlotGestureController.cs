using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.WindowsForms;
using Resonalyze.Ui.Dialogs;

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
/// Ctrl + right drag — a zoom box (<see cref="PlotZoomRectangleManipulator"/>), which
/// like REW's is drawn first and applied second: the box stays on the graph with the
/// size of the area it frames written beside it, a click inside it zooms there, any
/// other click lets it go. It is a ruler before it is a selection, so it is drawn and
/// measured over locked scales too — only the zoom is conditional. Both are undoable
/// with Ctrl+Z.</item>
/// <item>right drag — pan. Ctrl+Alt+F / Ctrl+Alt+Y — fit to data / fit Y to
/// data. Double click — the graph limits dialog.</item>
/// <item>the plus/minus buttons drawn against each axis while the pointer is over
/// the plot (<see cref="PlotZoomButtons"/>).</item>
/// <item>F1 — the whole map on a card (<see cref="GraphHelpDialog"/>). Every gesture
/// here is one nobody is told about anywhere else in the window.</item>
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

    /// <summary>How long a hint over the graph stays up once shown, in milliseconds.</summary>
    private const int TipDurationMs = 4000;

    private readonly PlotView view;
    private readonly LinkedList<IReadOnlyList<PlotAxisViewport>> zoomUndo = new();
    private readonly PlotZoomButtonsAnnotation zoomButtons = new();

    // A plus and a minus against an axis do not say WHICH axis, or that they zoom
    // at all, so the hovered one names itself, and a box too small to zoom to says so
    // rather than being ignored. OxyPlot's own element tooltips are not wired up in
    // its WinForms view, hence a plain ToolTip on the control.
    private readonly ToolTip graphTip = new() { ShowAlways = true };

    // REW's zoom box outlives the drag that draws it, so the controller owns it
    // rather than the manipulator: the box waits on the graph, and the click that
    // zooms to it (or lets it go) arrives long after that manipulator is gone.
    private readonly PlotZoomRectangleAnnotation zoomBox = new();
    private PlotModel? zoomBoxModel;
    private bool zoomBoxPending;
    private bool zoomBoxHovered;

    // Where the pointer last was, in the view's coordinates. The keyboard zoom
    // commands need it: OxyPlot's key events carry no position, and REW zooms the
    // axis around the pointer rather than around the centre of the plot.
    private ScreenPoint pointer;
    private PlotModel? buttonsModel;
    private PlotZoomButton? hoveredButton;

    // What the undo stack was recorded against. A snapshot names its axes by key,
    // and the same key means a different quantity from one build to the next — the
    // "decibel" axis is dBr in one and dB SPL in the next — so an entry is only ever
    // replayed onto the same model showing the same quantities. The identity is
    // needed on top of the model reference because the Virtual DSP acoustic view
    // re-arms ONE axis object between dB, degrees and milliseconds without
    // replacing the model, and the EQ wizard re-arms its dB axis for a new source.
    private PlotModel? undoModel;
    private IReadOnlyList<AxisIdentity> undoAxes = Array.Empty<AxisIdentity>();

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

        // Replaces OxyPlot's zoom rectangle, which zooms the moment the button comes
        // up. REW draws the box first and zooms on a click inside it, and the undo
        // step is therefore recorded by that click rather than here — a box that is
        // only read must not leave anything on the undo stack.
        this.BindMouseDown(
            OxyMouseButton.Right,
            OxyModifierKeys.Control,
            new DelegatePlotCommand<OxyMouseDownEventArgs>((target, controller, args) =>
                controller.AddMouseManipulator(
                    target,
                    new PlotZoomRectangleManipulator(target, this),
                    args)));

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

        // A plain left press answers a waiting zoom box first, then an on-graph zoom
        // button, and otherwise does what OxyPlot binds it to — the snapping tracker.
        this.BindMouseDown(
            OxyMouseButton.Left,
            new DelegatePlotCommand<OxyMouseDownEventArgs>((target, controller, args) =>
            {
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

    /// <summary>
    /// Starts a box, dropping whatever was waiting: one box at a time, like one
    /// selection at a time.
    /// </summary>
    public void BeginZoomBox()
    {
        DismissZoomBox();
        if (view.ActualModel is not PlotModel model)
        {
            return;
        }

        zoomBoxModel = model;
        model.Annotations.Add(zoomBox);
    }

    /// <summary>Redraws the box as the drag grows; <c>null</c> while it is still a dot.</summary>
    public void UpdateZoomBox(PlotZoomBox? box, ScreenPoint start, ScreenPoint current) =>
        ShowZoomBox(box, start, current, pending: false);

    /// <summary>
    /// Leaves the finished box on the graph, waiting to be read and perhaps clicked.
    /// Every box that was actually drawn is kept, however thin: a box a millimetre
    /// tall still measures a fraction of a decibel, and whether it can also be zoomed
    /// to is a question for the click, not for the release. A Ctrl + right CLICK that
    /// drew no box at all is simply forgotten.
    /// </summary>
    public void FinishZoomBox(PlotZoomBox box, ScreenPoint start, ScreenPoint end)
    {
        if (PlotZoomRectangleReadout.WasDrawn(start, end))
        {
            ShowZoomBox(box, start, end, pending: true);
            return;
        }

        DismissZoomBox();
    }

    /// <summary>
    /// The click REW zooms on. Inside the waiting box it takes the view there — and
    /// only here is an undo step recorded, because only here does anything move. A
    /// box too thin to zoom to keeps its place and says which side is too small, so
    /// the reading survives the misfire. A click anywhere else lets the box go and
    /// then goes on to do whatever it would have done: the box is an overlay, not a
    /// state the graph is stuck in.
    /// </summary>
    private bool TryClickZoomBox(IPlotView target, ScreenPoint position)
    {
        if (!zoomBoxPending || zoomBox.Box is not PlotZoomBox box)
        {
            return false;
        }

        if (zoomBoxModel is not PlotModel model ||
            !ReferenceEquals(model, target.ActualModel) ||
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
        zoomBox.Box = null;
        zoomBox.Text = string.Empty;
        zoomBox.Hint = string.Empty;
        view.InvalidatePlot(false);
    }

    // A box that is waiting to be clicked has to look clickable, or the instruction
    // beside it is the only thing saying so.
    private void TrackZoomBoxHover(PlotModel model, ScreenPoint position) =>
        SetZoomBoxHovered(
            zoomBoxPending &&
            ReferenceEquals(zoomBoxModel, model) &&
            zoomBox.Box is PlotZoomBox box &&
            box.CanZoom &&
            box.Contains(model.PlotArea, position));

    private void SetZoomBoxHovered(bool hovered)
    {
        if (hovered == zoomBoxHovered)
        {
            return;
        }

        zoomBoxHovered = hovered;
        view.Cursor = hovered ? Cursors.Hand : Cursors.Default;
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

        // The box belongs to the model it was drawn over: a rebuild takes it away, so
        // the waiting state goes with it rather than surviving onto another graph.
        // Guarded on the box's OWN model, not on this one having changed: the first
        // pointer event of all can arrive during the drag that draws the box, and
        // that must not drop the box being drawn.
        if (!ReferenceEquals(zoomBoxModel, model))
        {
            DismissZoomBox();
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

        // Handled here rather than left to Windows: answering the key ourselves is
        // also what stops it reaching DefWindowProc, which would turn it into a
        // second, empty help request.
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

    /// <summary>
    /// Forgets the zoom history as soon as the plot is not showing what it was
    /// recorded from: a different model (a mode switch, a new measurement, a
    /// rebuild), or the same model with an axis re-armed to a different quantity.
    /// What carries a zoom across a rebuild is <see cref="PlotViewportMemory"/>,
    /// which knows the mode and is told when an axis changes meaning; the undo
    /// stack knows neither, so it does not try to outlive either change.
    /// </summary>
    private void DropUndoOfAnotherModel()
    {
        IReadOnlyList<AxisIdentity> axes = DescribeAxes(view.ActualModel);
        if (ReferenceEquals(undoModel, view.ActualModel) && axes.SequenceEqual(undoAxes))
        {
            return;
        }

        zoomUndo.Clear();
        undoModel = view.ActualModel;
        undoAxes = axes;
    }

    private static IReadOnlyList<AxisIdentity> DescribeAxes(PlotModel? model) =>
        model == null
            ? Array.Empty<AxisIdentity>()
            : model.Axes
                .Select(axis => new AxisIdentity(
                    axis.Key,
                    axis.Title,
                    axis.GetType(),
                    axis.AbsoluteMinimum,
                    axis.AbsoluteMaximum))
                .ToList();

    /// <summary>
    /// What an axis MEANS, as far as a stored range is concerned: what it is called
    /// and the hard limits it is armed with. Re-arming those is how the app says
    /// "this axis now shows something else".
    /// </summary>
    private readonly record struct AxisIdentity(
        string? Key,
        string? Title,
        Type AxisType,
        double AbsoluteMinimum,
        double AbsoluteMaximum);
}
