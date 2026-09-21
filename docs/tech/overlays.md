# Overlay slots

The twelve overlay slots beside the main plot keep a curve across measurements: a captured curve, a calculated
overlay (an operation between curves) or a target. Frequency Response, Live Spectrum and the EQ Wizard share one set of
slots and files (`OverlayModes.SlotModeFor`); every other plot mode has its own.

## Code map

| Area | Code |
| --- | --- |
| One slot's content as its file stores it: title, offset, appearance, smoothing and exactly one of a captured curve, an operation or a target | `OverlaySlotState`, `CapturedCurve`, `OverlayOperationSettings`, `OverlayTargetSettings`, `OverlayAppearance` |
| The slots, what each shows and whether it can; capture, import, settings, clear, offset and its deferred save, dialog previews | `OverlaySession`, `OverlaySlot` |
| What the slots read outside themselves: the plot's live curves and the analyzer's raw curves, impulse framing and complex sum | `OverlayPlotSources` |
| The points a slot draws and exports; operand resolution and the scale/axis rules | `OverlayCurves` |
| The series a slot puts on the plot, tagged so it finds its own | `OverlaySeries` |
| A plot curve or a text file turned into a captured curve; export metadata | `OverlayCapture` |
| Slot controls, menu, long press, settings dialogs, text files; binding only | `OverlayPanel`, `OverlaySlotView` partials |

The session is UI-free and is what the tests build (`OverlaySessionTests`); it takes the folder its files live in, so
a test never touches the application's slots. The view writes the session from its controls and shows each slot again
when the session raises `SlotChanged`, without raising the controls' handlers. Storage failures come back as
`StorageFailed`, which the panel shows. `OverlayPanelWiringTests` drive shown controls through the menu, the checkbox
and the offset field.

The offset is held as its field shows it: a file's offset is clamped to the field's range and rounded half away from
zero, as the field itself would (`NumericFieldRange.Assign`). A change is drawn at once and written to the file when
the field has been still for half a second, on a mode switch, or when the window closes.

A slot's dialog opens with the slot's own settings; a slot holding something else opens the dialog with defaults.
