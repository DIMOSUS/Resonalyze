# Plot interaction

Every OxyPlot `PlotView` in the app gets the same mouse and keyboard behaviour through
`PlotInteraction.Enable`, which installs a `PlotGestureController`. The map is shaped after
REW's graph panel so that someone tuning with REW open on another laptop does not have to
relearn the mouse. The user-facing copy is the "Graph Zoom and Limits" table in
`REFERENCE.md`; the F1 card is `PlotGestureHelp` (shown by `GraphHelpDialog`). A gesture that
changes has to change in all three places.

Code lives in `source/Plotting/`:

- `PlotGestureController` - bindings, the waiting zoom box, the undo stack, on-graph buttons, tooltips.
- `PlotAxisZoom` - view-free axis arithmetic (wheel factor, axis-end hit test, single-limit zoom).
- `PlotVariableZoomManipulator`, `PlotZoomRectangleManipulator` - the two drag gestures.
- `PlotZoomRectangleReadout` (`PlotZoomBox`, `PlotZoomRectangleAnnotation`) - zoom box geometry, readout text, drawing.
- `PlotZoomButtons` / `PlotZoomButtonsAnnotation` - on-graph plus/minus buttons.
- `PlotAxisFit` - Fit to data / Fit Y to data.
- `PlotAxisViewport`, `PlotViewportMemory`, `PlotAxisIdentity` - carrying zoom across model rebuilds.

## Gesture map

Bindings that mirror REW:

- Wheel zooms both axes around the pointer; Alt gives a fine step (`PlotAxisZoom.FineWheelFactor`,
  equal to OxyPlot's `ZoomWheelFine`). Ctrl + wheel previously held OxyPlot's fine step; the fine
  step now lives on Alt, where REW keeps it.
- Shift + wheel, or the pointer over an axis, zooms that one axis. The second half is OxyPlot's own
  `PlotModel.GetAxesFromPoint` behaviour, which only works because the frequency axis allows zoom.
- Wheel over the end of an axis moves that single limit (see [Axis zoom arithmetic](#axis-zoom-arithmetic)).
- x / Shift+X and y / Shift+Y zoom one axis out / in by about two (`StepZoomInScale = 2`).
- Middle drag is variable zoom. OxyPlot binds its zoom rectangle to the middle button by default;
  REW puts the box on Ctrl + right drag, where OxyPlot also has it, so the middle binding is replaced.
- Ctrl + right drag draws a zoom box (see [Zoom box](#zoom-box)). Right drag pans.
- Ctrl+Alt+F / Ctrl+Alt+Y fit to data / fit Y to data. Double click opens `GraphLimitsDialog`.
- F1 opens the help card. The controller handles the key itself; leaving it to Windows lets
  `DefWindowProc` turn it into a second, empty help request.

Two bindings have no REW counterpart and are kept from before the REW remap: Ctrl + wheel zooms the
vertical axis only, and Home / A resets an axis to the model's own scale. Neither shadows a REW gesture.

Keyboard zoom centres on the pointer, but OxyPlot key events carry no position, so the controller
tracks the last pointer position (`PlotAxisZoom.ClampToPlotArea` falls back to the plot centre).
Pointer tracking invalidates the view only when the zoom buttons appear, disappear or change hover
state - a plain move across the plot must not repaint a waterfall.

A plain left press is answered in order: a waiting zoom box first, then an on-graph zoom button,
otherwise OxyPlot's snapping tracker. Two double-click traps are handled explicitly:

- A second quick click on a zoom button arrives as a double click; it is answered as another zoom
  step instead of opening the limits dialog.
- The second press of a double tap inside a zoom box (the first already zoomed) sets
  `zoomBoxJustClicked` so the limits dialog does not open on top.

OxyPlot's element tooltips are not wired up in its WinForms view, so hints (which axis a button
zooms, why a box is too small) use a plain WinForms `ToolTip` on the control, shown for 4 s.

## Axis zoom arithmetic

`PlotAxisZoom` is kept out of the controller so it can be tested without a view.

- `ScaleFromWheelDelta` uses OxyPlot's own formula (`ZoomStepManipulator`), so a gesture handled here
  and one handled by a stock manipulator move the axis equally.
- `TryGetAxisEnd`: the outer quarter of an axis strip at each end (`EndZoneFraction = 0.25`) counts as
  "the end"; the middle half zooms the whole axis around the pointer. Points inside the plot area or in
  a corner where two strips overlap are ambiguous and ignored. The position along the axis is read
  through the axis transform, so logarithmic and reversed axes behave like linear ones.
- `ZoomEnd` moves one limit by zooming at the opposite end, which pins that end in place.
- `FindAxis` falls back to the axis under the plot centre, because over one axis's strip
  `GetAxesFromPoint` reports only that axis and a keyboard step for the other orientation would find nothing.
- `FindZoomableAxis` returns the first visible, zoomable, non-colour axis. The waterfall's colour scale
  sits like a left axis but is not a scale anyone pans. Modes that pin their scale (waterfall, burst decay)
  have none, and that is what hides the limits dialog and the on-graph buttons for them.

Variable zoom (`PlotVariableZoomManipulator`) doubles or halves an axis per 150 px of drag - short enough
to cross two octaves in one gesture, long enough to land on a decade. The anchor is held in screen
coordinates and re-read through the axis each step, which keeps the pressed point still.

`PlotAxisFit` leaves axes that refuse zoom alone (those are deliberately pinned by a mode, e.g. the EQ
wizard's gain axis). Value axes get a 5 % margin, applied in the axis's own scale (a ratio on log axes);
the frequency axis gets none, because 20 Hz-20 kHz is the data and padding would open on empty decades.

## Zoom box

REW's zoom box is drawn first and applied second, which makes it a measuring tape as well as a selection.

- **Held in axis values** (`PlotZoomBox`), not pixels. Between drawing and the zooming click the plot can
  be panned, wheeled or redrawn; a pixel box would then frame a different piece of curve. A box panned
  off the graph is still remembered and comes back with the data. The label keeps the drag-end corner
  (`AnchorRight`/`AnchorBottom`) so it stays on the side the pointer left it.
- **Owned by the controller**, not the manipulator. `PlotZoomRectangleManipulator` only draws and measures
  during the drag; the wait and the click arrive long after it is gone.
- **Frames both directions regardless of zoomability.** The Virtual DSP phase view locks its height to
  ±180°, and a phase difference across a crossover is exactly worth measuring. `Zoom()` then moves only
  the axes with `IsZoomEnabled`. A direction with no measurable axis (hidden, or a colour axis such as the
  waterfall's ±1 placeholder) is left null and spans the plot area.
- **Undo is recorded by the zooming click, not the drag**: a box that is only read must not leave anything
  on the undo stack.
- **Release rules.** Pointer travel under 3 px (`MinimumDragSize`) is a Ctrl + right click, silently
  forgotten. Every drawn box is kept however thin - a millimetre-tall box still measures a fraction of a dB.
  At the click, a side under 10 px (`MinimumZoomSize`), judged on the box's current screen rectangle along
  the zoomable sides, is refused with a message naming that side, and the box stays. A click outside the
  box releases it and then proceeds as a normal click.
- **Hint line** "click inside to zoom" appears only while the box waits and can zoom; over locked scales
  the box is only a ruler and inviting a click that does nothing is worse. The hint is a second line so the
  label stays narrow enough to sit beside the box. The waiting box shows a hover state so it looks clickable.
- **Label placement**: outside the box at the drag-end corner (the framed area is what the user is looking
  at), clamped into the plot area.

Readout text (`PlotZoomRectangleReadout`):

- Three significant figures - a box is framed by eye.
- Frequency spans switch to kHz at 1000 Hz, as REW writes them.
- The unit comes from the axis title: trailing parentheses ("Sum loss (dB)"), or a title that is (or starts
  with) a known unit ("dB", "ms from peak"). Units are an explicit list (`KnownUnits`), not a "short word"
  rule, because short titles like "step" or "r" name dimensionless quantities. Frequency and phase axes are
  usually untitled and are recognized by key; a keyless bottom logarithmic axis (the Time Alignment
  previews build theirs by hand) is taken as Hz. A degree sign is set against the number, word units apart.

## Axis identity

A model reference alone does not say whether the plot still shows the same quantities. The Virtual DSP
acoustic view re-arms one value axis object between dB, degrees and a unitless impulse scale and swaps its
bottom axis between frequency and time without replacing the model; the EQ wizard re-arms its dB axis for a
new source. `PlotAxisIdentity` records an axis's key and the hard limits it is armed with, and
`PlotAxisIdentities.Match` compares a model against a recorded list.

Everything that remembers a range stores the identity next to it and drops what it holds on mismatch:

- The zoom box. `DropZoomBoxOfAnotherView` runs on every pointer move, before the zoom buttons attach
  (their attach returns early for an unchanged model, which is exactly the re-arm case), and again at the
  click, since a view can be re-armed without the mouse moving. `PlotZoomRectangleAnnotation` also checks
  `StillDescribesItsPlot` on every paint: the VDSP view switch re-arms and repaints in one go, and the
  pointer may never move again, so a dB reading must not stay over a scale that became degrees.
- The undo stack (below).

The zoom box annotation and the zoom-button annotation belong to whichever model is on screen, so a rebuild
(new measurement, mode switch) takes the box with it; the controller re-attaches the buttons to each new model.

## Undo stack

Zoom gestures push `PlotAxisViewport` snapshots (depth 32 - enough to undo a hunt around a resonance,
shallow enough not to become a session memory). Entries name axes by key, and a key's quantity varies
between builds ("decibel" is dBr in one model and dB SPL in the next). The stack is therefore replayed only
onto the same model with the same axis identities, and forgotten as soon as either changes. Carrying zoom
across rebuilds is `PlotViewportMemory`'s job, which knows the mode and is told when an axis changes meaning.

## Axis snapshots

`PlotAxisViewport` is one axis's visible range. Axes are matched by `Key` when they have one - two modes both
put a left `LinearAxis` in the same place, and position matching would restore a phase range onto a
group-delay axis. Unnamed axes (EQ wizard dB axis, VDSP value axis) fall back to position plus axis type.

`Capture` calls `IPlotModel.Update(false)` first: `ActualMinimum/ActualMaximum` only refresh on render, and a
capture taken before the previous paint settled (common with Compare, whose model builds slower) would read
the nominal range and drop the zoom.

`SameRange` tolerates 1e-6 of the span. It only has to absorb arithmetic between a build-time range and the
same range read back; the smallest wheel step moves an axis by percents.

## Axis override capture

`PlotAxisViewport.CaptureOverrides` returns only the ranges a user forced on the axes. OxyPlot tracks that
distinction in `Axis.ViewMinimum/ViewMaximum`, which are protected, so it is read through the public API:

1. `Update(false)` and record every axis's actual range.
2. `Reset()` every axis (which drops overrides) and `Update(false)` so the model recomputes its own ranges.
3. For each axis whose recorded range differs from the recomputed one, `Zoom` it back and report it.

The plot is left showing exactly what it showed.

Asking the axes, instead of comparing against a baseline captured when the model was shown, makes the answer
independent of when it is asked. Overlays join a plot after it is drawn - a mode switch restores its slots
after `ModeController` has drawn, Show All and slot check boxes act later still - and on an auto-scaled axis
they widen the range through the data rather than through an override. A baseline comparison reads that as a zoom.

## Viewport memory

Plot models are rebuilt from scratch on every settings change, measurement and overlay toggle, so without
help a zoom lasts until the user's next action. REW holds limits until changed; `PlotViewportMemory` keeps the
same contract per `Mode` (the main plot) so the frequency and impulse plots do not fight over one range. The two
Time Alignment previews each have their own memory.

- `Show(model, mode)` first captures the outgoing model's overrides into its mode's slot, then applies the
  incoming mode's saved ranges to the new model before assigning it to the view. Applying afterwards would flash
  the default scale, because overlays force a synchronous repaint while drawing.
- A capture with no overrides removes the mode's entry, so the next model scales itself freely.
- Untouched axes keep whatever the new model computes, so automatic behaviours still work: the dB ceiling that
  `PlotModelStyle.RaiseDecibelViewCeiling` lifts for a padded loopback, the group-delay auto-fit, auto-scaled
  axes widening for overlays.
- `Forget(mode)` is for settings that change what an axis means (linear vs logarithmic, dBr vs dB SPL, impulse
  unit or origin). Mode descriptors declare a `viewResetKey` for such settings. `Forget` also resets the axes of
  the model on screen; otherwise the capture on the next redraw would save the stale range straight back.
