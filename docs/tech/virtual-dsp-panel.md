# Virtual DSP panel

The Virtual DSP tool runs up to twelve measured transfer IRs (left/right pairs) through per-channel
DSP chains (gain, delay, polarity, crossover, all-pass, PEQ, FIR) and sums them as complex responses,
predicting the combined output before anything is dialled into hardware. The upper (acoustic) plot shows
raw/processed channels, their complex sum and the sum loss in one of five views (magnitude, phase, group
delay, impulse, step); the lower plot shows each chain's own response or a junction analysis
(correlation, coherence). The whole state persists as a project file (autosave) and can be exported as a
session.

The heavy processing lives in the processing coordinator and in `Resonalyze.Dsp` (`AutoAlignmentEngine`,
`AlignmentSelection`, `GainBalanceEngine`).

## Code map

Code: `source/Tools/VirtualCrossover/`. The tune lives in a UI-free `VirtualCrossoverSession`: the project, the
blocks (`VirtualCrossoverChannel`, one per L/R pair, with `VirtualCrossoverChannelSettings` per side), the
calibration policy, the Gate dialog's preview, the magnitude gate snapshot, the project generation and the last
redraw's processed channels and hybrid offset. The project is the one owner of the target level: the Level field
shows it and hands back the user's edit. The ranges a restored file or an AI reply can state too (the target level,
the Auto delay fields) are `VirtualCrossoverLimits`, which the fields and the AI review both read. Whatever reads the tune takes the
session. `VirtualCrossoverPanel` is its only writer: it binds the controls to it and presents what the readers
return, and only its binding methods look a block's `VirtualCrossoverChannelControl` up. Its partials are named
for what they bind (`.Project`, `.Calibration`, `.Channels`, `.Sources`, `.Peq`, `.Fir`, `.Views`, `.DspPlot`,
`.Target`, `.SpatialAverage`, `.AutoDelay`, `.AutoCrossover`, `.JunctionTune`, `.Audition`, `.AgentBridge`,
`.Export`, `.ToolTips`, `.Layout`); the main file holds the constructor, the host's API and the redraw loop.

| Reads the session | For |
| --- | --- |
| `VirtualCrossoverPhaseGate`, `MagnitudeGateSnapshot` | the phase gate in effect and the magnitude window the workers read |
| `AcousticViewBuilder`, `VirtualCrossoverFrame`, `JunctionViews` | the upper plot's curves and the junction views |
| `VirtualCrossoverHybrid` | the hybrid view: captures, set offset, level read-outs |
| `GatePlacementVerdict`, `VirtualCrossoverWarnings` | the warning line |
| `VirtualCrossoverAutoDelay`, `StagedGroupPlacement` | Auto delay |
| `VirtualCrossoverAutoSetup` | what the crossover wizard reads and writes |
| `VirtualCrossoverEqHandoff` | what a channel side hands the EQ Wizard, and whether its bank may come back ([EQ handoff](#eq-handoff-code-map)) |
| `VirtualCrossoverJunctionTuneSearch`, `VirtualCrossoverJunctionTuneApply` | Tune junction: its search, its verdict, Apply and Undo; the dialog's question is in [its code map](#junction-tune-code-map) |
| `DspProcessorSession`, `DspProcessorStatus`, `DspProcessorApply` | the processor dialog ([DSP processor](#dsp-processor-code-map)) |
| `VirtualCrossoverGateEstimate` | the Gate dialog's τ estimate and auto detrend line ([gate estimate](#gate-estimate)) |
| `VirtualCrossoverAudition` | what the audition renders; the dialog's own state is in [its code map](#audition-code-map) |
| `AgentSessionReader`, `AgentProbeReader`, `AgentJunctionTune`, `AgentEngineRequests` | the Agent Bridge |
| `AgentImportRunner`, `AgentImportUndo` | an AI import once its review is answered, and its undo ([AI import runner](#ai-import-runner-code-map)) |

The shown side has one owner, the project's `ActiveSideRight`. A block's shorthand members (`Settings`,
`TransferImpulseResponse`, ...) read the side it shows; a block the panel creates reads that side from the session
(`ActiveRightProvider`) and refuses to be set apart from it, so the two cannot disagree. Readers that have the session
name the side explicitly.

Tests of a rule build a session directly or read a panel's through `panel.Session`. The wiring between the controls
and the readers, which only the panel path exercises, is pinned by `VirtualCrossoverPanelWiringTests`: a live panel on
synthetic measurements (the right side 6 dB below the left, so every reading names its side), driven through its
controls and read by what it draws and reports.

### Crossover wizard code map

The wizard has state of its own, apart from the tune, in a UI-free `AutoSetupWizardSession`: the channels in chain
order with their confirmed driver types (`AutoSetupWizardRow`), what the user set on each junction
(`AutoSetupJunctionEdits`, kept by the pair, so a reorder elsewhere keeps it and a pair that comes back finds it),
the filter families, the system band, the slope and reorder options and the bass elevation, each number held as its
field shows it.

| Reads the wizard's session | For |
| --- | --- |
| `AutoSetupWizardPlan` | each group's sources and options, and the window each junction resolves to |
| `AutoSetupWizardFit` | the fit (the primary group first, the others levelled onto it) and the preview, over `AutoSetupPreviewInputs` read before the run leaves the UI thread |
| `AutoSetupWizardReport` | band texts, the preview card's lines, each junction's verdict |
| `AutoSetupWizardChainOrder` | pairs the measurements do not confirm, the rows' marks, the question Apply asks |

`VirtualCrossoverAutoSetupDialog` binds the controls in partials (`.Channels`, `.Junctions`, `.Preview`, `.Layout`,
`.Apply`) and runs the preview and the ranked search off the UI thread; `VirtualCrossoverAutoSetup` reads the blocks
it opens on and writes its proposal back. A field the user has not touched shows the resolved window, and writing
it is not an edit. `VirtualCrossoverAutoSetupDialogBoundaryTests` keeps statics and nested types off the dialog, and
`VirtualCrossoverAutoSetupDialogWiringTests` drives a shown dialog through its controls beside a session the test
changes the same way.

### Junction tune code map

The dialog's question is a `VirtualCrossoverJunctionTuneQuestion`: the junction and its corner window, remembered
per junction and kept only while it still holds the junction's corner; the families, which only the first junction
shown without memory takes from the cards, with the goal; the slope window, read either way round; the mode and the
budget; the status and the report. Every change retires the standing answer, and a search that returns to a changed
question lands nothing. `CrossoverFamilyChoice.GoalSlope` is the slope a goal box opens on, shared with the goal
dialog. `VirtualCrossoverJunctionTuneDialog` binds the controls and writes the question back into them.

`VirtualCrossoverJunctionTuneSearch` states what the dialog opens a junction with, turns a request into the tuner's
plan (or the refusal that stands in for a search) and the result into the report and verdict.
`VirtualCrossoverJunctionTuneRun` runs the tuner off the UI thread with the session's fingerprint taken on both sides
and drops a result the session moved under, for the dialog and the AI import alike.
`VirtualCrossoverJunctionTuneApply` holds the result the open dialog shows, writes it on Apply through
`AgentJunctionTune.Write`, and keeps the one step of Undo: the session before the Apply (`AgentImportUndo`, which
Undo AI import shares), the project generation it belongs to and the fingerprint after it, by which Undo knows that
later changes would go too and asks. The panel's `.JunctionTune` refreshes the cards and restores through
`RestoreChannels`. `VirtualCrossoverJunctionTuneQuestionTests` test the question; `VirtualCrossoverJunctionTuneDialogTests`
drive a shown dialog, and `VirtualCrossoverJunctionTuneWiringTests` the real dialog from a live panel.

### EQ handoff code map

`VirtualCrossoverEqHandoff` builds the request a PEQ menu hands the EQ Wizard (see [PEQ and FIR
handoffs](#peq-and-fir-handoffs)) and guards the bank that comes back, over the session and its last redraw; the
panel raises `EditPeqInWizardRequested` and shows the level and bank that land. The AI import's Auto-tune builds its
request and lands its fit through the same type.

### AI import runner code map

`AgentImportRunner` runs an import once the review is answered (see [agent-bridge.md](agent-bridge.md#import-flow)):
the probes, the re-check, the rows, the engines in their order and the one step of undo. It reads the view through
`IAgentImportHost`, which the panel implements with what only a control can do: the crossover wizard, the Auto delay
commit, showing a channel or a bank, the target level, the side lock, the wait cursor. `AgentImportUndo.Restore` puts
the session back, block order included (`VirtualCrossoverSession.Reorder`); the panel refreshes the cards. The menu, the
clipboard, the review and every message stay in the panel's `.AgentBridge`, the messages and menus through
`ShowMessage` and `ShowMenu`, which a test answers. `AgentImportRunnerTests` drive the runner over a bare session;
`VirtualCrossoverPanelDialogWiringTests` drive the menu on a live panel.

### DSP processor code map

`DspProcessorSession` holds the processor dialog's choice: a catalog model fixes rate and Q convention, Custom keeps
its own (a stated rate or following the measurements) while the user looks at models, a new model proposes the
phase-control answer afresh and the FIR answer only one way, and an unlisted rate joins the list.
`DspProcessorStatus` reads the status line off it; `DspProcessorApply` writes the notes and the processor (see
[processor rate](#processor-rate)) and words the notice for what a device cannot run. `DspProcessorDialog` binds the
controls; the panel shows the blocks and the notice.

### Gate estimate

`VirtualCrossoverGateEstimate` reads the Gate dialog's curves: the earliest trace as the phase reference, the τ the
Slope and Peak buttons put there (none without a trace, and the dialog beeps), and the auto detrend line. The dialog
reports its gate as one `VirtualCrossoverGatePreview`, live and after Save, to both the Virtual DSP panel and the EQ
Wizard's phase view.

### Audition code map

The audition dialog keeps its own state in a UI-free `VirtualCrossoverAuditionSession`: the track and the output with
the consent to replace an existing one, the calibration, cabin and magnitude choices, the report's sections and the
render in flight. What one dialog leaves for the next (the track, the output, the cabin, the tick) is a
`VirtualCrossoverAuditionMemory`, kept per process. The context the panel prepares (`VirtualCrossoverAudition`, records
in `VirtualCrossoverAuditionContext.cs`) is read only.

| Reads the audition's session | For |
| --- | --- |
| `VirtualCrossoverAuditionBudget` | the duration and projected-bytes caps: a picked track's refusal, the decoded track's re-check |
| `VirtualCrossoverAuditionCalibration` | the note on "Own (as measured)", and the curve and label a render carries |
| `VirtualCrossoverAuditionRender` | when Render can be pressed, the request read once before the first await, the worker that writes the WAV |
| `VirtualCrossoverAuditionReport` | the report: the result, the tune, the magnitudes, the calibration note, the track |

`VirtualCrossoverAuditionDialog` binds the controls and runs the render; its file dialogs and questions go through
`ShowFileDialog` and `Ask`, which a test answers. `VirtualCrossoverAuditionDialogBoundaryTests` keeps statics and nested
types off the dialog, and `VirtualCrossoverAuditionDialogWiringTests` drives a shown dialog beside a session the test
changes the same way, down to the bytes a render writes.

### Channel block code map

A block (`VirtualCrossoverChannelControl`) shows one side of a channel: the panel writes the session from its fields and
pushes the session's values back. What the block shows beside its fields comes from readers that take the values it
holds, so a field the user is typing into and a value the panel pushed read the same way.

| Reader | For |
| --- | --- |
| `VirtualCrossoverChannelAvailability` | which crossover fields take input |
| `VirtualCrossoverChannelTotalGain` | the gain with the PEQ preamp folded in |
| `VirtualCrossoverChannelDelayReadout` | the delay as a distance in air |
| `VirtualCrossoverChannelFirReadout` | the FIR row, its tooltips and the conflict that turns it red |
| `VirtualCrossoverChannelPhaseReadout` | what the Phase angle builds, and its tooltip |
| `VirtualCrossoverChannelGoalReadout` | the acoustic goal button |
| `VirtualCrossoverChannelAverageReadout` | the spatial-average button |

The block binds them in partials (`.Readouts`, `.Crossover`, `.Layout`, `.ToolTips`).
`VirtualCrossoverChannelControlBoundaryTests` keeps statics and nested types off it, and
`VirtualCrossoverChannelControlWiringTests` drives its fields and setters against the readers.

## Redraw scheduling

- `RedrawAll` calls `RefreshProcessorRowAvailability` (cheap and idempotent, so a project rate that follows
  replaced measurements reaches the blocks without every source path remembering it) and then
  `RequestRedraw`.
- `RequestRedraw` runs only on the UI thread, so its pending flag and task handle need no locking. It starts
  `RunRedrawLoopAsync`, or, if a pass is already running, marks it stale so exactly one more pass runs with
  the latest settings. One redraw is in flight at a time and it always ends on the latest state.
- Every change calls `processingCoordinator.Invalidate()`. A running FFT may still finish, but the
  coordinator neither caches nor publishes a stale result. Revision, cancellation and cache ownership live
  in the coordinator; the loop only applies a current result to OxyPlot.
- `ProcessChannelsAsync` snapshots the channel set on the UI thread (`SourceSnapshot` is a write-once copy
  made at load, `ChannelSnapshot` deep-copies PEQ), so worker threads never read controls or the mutable
  project. Old curves stay on screen until the new frame is ready (no clear-then-fill flicker).
- A failed redraw is logged and the last good frame stays.
- Tracy zones are thread-bound and strictly LIFO, so no zone may span an `await`. Only synchronous
  sections are zoned in the panel; heavy per-channel DSP is zoned inside the coordinator's workers. The
  frame build and the OxyPlot draw are zoned separately so a profile can tell them apart.
- Work that is too slow for the UI frame is computed in the background task of the redraw: the junction
  phase entries and the direct-sound loss, the stereo and group deltas, and the Hilbert envelopes of the
  impulse view (a Hilbert transform over the whole processed record: 2^17 samples a channel on an ordinary
  sweep capture, many times that when a late arrival kept the source uncropped or a high-Q/FIR tail
  stretched `ApplyChain`'s padding). A drag hands the edited channel a new array every frame, so memoization
  alone does not help; the envelopes are memoized per array and warmed here.
- The correlation/coherence rebuild uses the same single-flight pattern (`correlationRebuildTask`):
  stamping alone would hide stale results while stacked tasks each burned a full sweep of inverse FFTs.
- The Auto crossover and Auto delay commands read the processed set, so they are disabled until the
  redraw has settled (`RefreshAutoActionsEnabled`). The audition sums both sides through the coordinator at
  one revision and is disabled mid-redraw for the same reason; the AI package is gathered at one revision
  too, and an import would be overwritten by a load in progress.

## Gate snapshot

`MagnitudeGateSnapshot` is an immutable record carrying the gate template, the active and opposite
side's pinned offsets and the smoothing. `RequestRedraw` refreshes it on the UI thread (every redraw path
funnels through there); PLINQ magnitude builds on worker threads read it by reference, never live controls,
the project or the gate preview. The template's offset is a placeholder: each build stamps its own
(`MagnitudeGateSnapshot.Channel`).

`ResolveGateOffsetMs` is the single place the pinned-vs-anchor choice is made. Each side stores its own
pinned offset because the two sides' drivers arrive at different times; the active side's pin must never
window the opposite side's sum (that uses its own pin, or its own anchor when unpinned). It is internal so a
unit test pins the rule without a panel.

While the gate dialog is open, the session's `GatePreview` holds its candidate values so all gated plots track it live;
Save commits them, Cancel just drops them. `AutoOffset` makes the preview gate per curve exactly as Save
will, while `OffsetMs` still says where the dialog's window is drawn. Only the placement is per side; window
lengths and analysis modes are project-wide so both sides read phase at the same resolution.

## Magnitude window

The magnitude view does not use the gate dialog's window. It always reads a FIXED steady-state window
(`FrequencyResponseOptions.SteadyState*`); only the dialog's offset (the pin, or the shared front anchor when
unpinned) says where it opens.

- Mode: FDW cannot hold a summed response. Its high-frequency windows are shorter than the channels'
  arrival spread, so no single window keeps every channel's treble inside one summed IR and the drawn Sum
  and the loss read-out collapse. The Sum loss selector's FDW-8 option does hold, because it windows each
  channel separately and sums the spectra (`BuildDirectLossCurve`); it never touches these curves.
- Length: tonal balance is a steady-state question. A short junction gate cannot contain a bass EQ band's
  own ringing, so a Q 5 cut at 100 Hz would draw at a fraction of its real depth.

In Auto, the magnitude window anchors at the sample the caller passes: the shared earliest front for
channels and sums. One shared window is what keeps the drawn Sum the exact vector sum of the drawn channels
and the sum loss under its 0 dB ceiling (`VirtualCrossoverMetrics.BuildCurves`).

### Raw curve anchor

A raw channel curve lives in its own time: its arrival precedes the processed gate by the channel's delay,
so even a pinned processed offset would clip it into the left fade. Raw curves use the same durations and
mode, anchored on the raw response's own START. They used to anchor on their own peak, which was harmless
with the short junction gate but a defect with the steady-state window: a woofer's peak trails its onset by
more than the 2 ms fade-in (5.4 ms on the archived Passat woofer), so the window opened after the response
began and octave bands read 10+ dB off the same IR read from the front.

## Measured sum

`MagnitudeGateSnapshot.MeasuredSum` builds the gated magnitude of the SUM of the channels, each contributing only where
it measured anything (`GatedMagnitude.MeasuredBySomeChannel`). One gated build yields both widths: the
smoothed curve the plot draws and the unsmoothed one the sum loss divides (one gate, one FFT, two
resamples).

This is not the same as gating the summed IR, even though one shared window makes the transform linear and
the totals agree bin for bin. They agree including the energy a window smears out of a channel's own band
into a range that channel never measured. On two brick-walled bands an octave apart that phantom reached
1.4 dB at 900 Hz and 2.5 dB at 990 Hz above the only channel that measured there, and nothing on the plot
showed it (the leaking channel's curve is masked exactly there), so the loss divided a total carrying it by
operands that did not. Where no channel measured, the total is masked rather than reported as a level; a
hole between two bands cannot be expressed by their outer edges.

Each channel brings its OWN microphone correction into the sum. Under one microphone that equals applying
it once at the end; under "Own (as measured)" with different microphones the pressure is HᵢCᵢ and the total
is Σ HᵢCᵢ. A single correction outside the sum cannot undo two microphones, and leaving it out drew a raw
total beside corrected channels: a gap that read as summation loss and fed the average and minimum loss
read-outs. `DescribeOwnCalibrationMismatch` now only informs that the plot reads more than one microphone.

The entry point used by the metric resolves the active side's placement itself, so the drawn Sum and the
metric's cannot be built under different windows.

### Sum loss curve

The sum loss is the signed dB gap between the complex sum and the phase-blind magnitude sum of the processed
channels (≤ 0 by the triangle inequality). It is built once in `BuildCurves` from the unsmoothed magnitudes
and smoothed as a ratio afterwards, the very list the read-out averages, so curve and figure cannot drift.
It sits on the plot's right-hand loss axis. Under the selector's Disable no curve is drawn but the column
keeps the full read; under FDW-8 the curve is the direct-sound loss, read through the junction phase
block's windows and labelled "Sum loss (direct)".

## Phase view

One shared absolute τ (the earliest arrival) keeps the curves' relative phase, which is what the view is
for. Windows may still follow each channel's own arrival: `BuildMeasuredPhase` re-references every
extraction to the common τ, which is exact as long as no window cuts into its own channel, the condition
`VirtualCrossoverPhaseGate.PerCurveOffsets` enforces before handing out per-curve placements. The placement arithmetic itself
lives in `PhaseGatePlacement`, which the EQ Wizard's phase view uses too, so a tune made in one holds in the
other. In Auto the phase curves follow their arrival START so FDW's short high-frequency windows land on
the right channel's first cycles.

- Gated spectra are built ONCE per redraw and feed both the channel curves and the Sum. The per-impulse
  cache serializes lookup and insert but not the bank computation, so a channel job and the Sum job racing
  on a cold cache could run the same FFTs twice.
- The Sum uses every SUMMING channel, hidden or not, matching the magnitude Sum; with the Sum off, hidden
  channels are skipped. Gate and project state are read once on the UI thread, and placements are resolved
  over the gated set only, so hiding a curve cannot move the others' windows.
- The Sum is the vector sum of individually gated SPECTRA, not a gate over the summed IR: under Auto the
  windows follow each channel's arrival and no single window can hold every channel's treble. Summing
  spectra keeps superposition exact, and with one shared window reduces to the gated summed IR.
- The τ detrend of an unconfigured project references the earliest arrival.
- ±360° wrap verticals are drawn faded, dashed and under the curve, with an empty title to keep them out of
  the plot-labels panel.

## Group delay view

`AcousticViewBuilder.GroupDelayCurves` draws each drawn channel's processed group delay and the Sum's,
through the SAME window the phase view reads (project gate, Fixed or FDW with its cycles, pinned or
per-curve placement, previewed by the open Gate dialog), so the two views are one window. It is absolute (ms
from the record's start, the impulse view's clock) with no detrend; a common τ would only shift every curve
equally. Under FDW the curve reads the arrival of the energy inside the window at each frequency, i.e. the
direct sound at mid and high frequencies, which cut the seat-to-seat scatter of this curve by three to five
times on the reference car. Only the plain group delay and the Sum are drawn; the minimum/excess split stays
the AI probe's.

- Smoothing follows the plot selector, but psychoacoustic smoothing is a hearing model for levels, not
  time, so it reads as 1/12 octave (the Group Delay mode's default and what the AI diagnostic reads at).
  The width is derived from `SmoothingCode`, because the project stores psychoacoustic as base width plus
  a flag for older builds.
- Operand pairs (the bank and its time-weighted twin, `GroupDelaySpectra`) are built once per redraw; the
  Sum is the sum of the individually gated pairs re-referenced to one extraction start with the time weight
  carried across, masked where no channel measured.
- The Group Delay mode's validity gate blanks a crossover's stop band on purpose.

## Impulse and step views

The impulse view is the gate dialog's IR preview promoted to the main plot: every processed IR on the shared
absolute timeline, wrapped in its envelope and normalized to that envelope's in-window peak, with the Tukey
window drawn where it sits. Only shown traces set the gate offset and the axis window, so an auto gate never
centres on a hidden channel.

### Step view

The step view draws the impulse view's traces as step responses on the same timeline and gate, plus the
Sum's. `ImpulseWindowPreview.AddStepTraceSeries` integrates and scales; the panel decides which responses go
in. The Sum is the sample-wise sum of the SUMMING channels' IRs (hidden or not, the set the magnitude Sum
adds), and by linearity its step is the sum of the steps, so drivers' contributions can be read off the
total. All curves share one scale because every processed response is in the one calibrated level the
magnitude Sum adds. The opposite side's Sum is drawn thin, dashed and translucent so both tunes' fronts
compare on one clock; it must use the shown side's sample rate or its samples land on the wrong milliseconds.

## Gate placement verdict

The gate offset is an ABSOLUTE time. A placement that belonged to one set of measurements windows the
reverberant tail of the next set, and nothing in a curve says so because a tail has a magnitude too.
`GatePlacementVerdict.Judge` judges the magnitude view's window against every processed channel and the
panel keeps the verdict (offset, plateau start and end, pinned or Auto, failed channels). The magnitude
placement is the one judged because the curves, the Sum and the loss read-out are built from it, and the
magnitude and phase shared placements both open at the earliest estimated START, so one verdict answers for
both. (It once anchored on the earliest peak, which is never earlier and so also answered; the rules were
unified when the junction gate moved to fronts.) Per-curve phase placements have their own guard in
`VirtualCrossoverPhaseGate.PerCurveOffsets`.

`JudgeCut` distinguishes two failures (`GateCutKind`):

- `ClosesBeforeArrival`, judged by geometry. The leading-edge figure (`DataHelper.GateLeadingEdgeLossDb`, the
  ratio of what the window discards ahead of its plateau to what it keeps) cannot detect it: a channel that
  lands past the window has nothing ahead of the plateau and reads EXCELLENT. Measured: a tweeter delayed
  20 ms out of a 4 ms plateau read −282 dB, the best figure in its session, with its curve holding none of
  the channel. (The +∞ reserved for "kept nothing" needs a bit-silent window, which a measured record never
  gives.) So the channel's front must lie inside the window: plateau AND fade-out, since the fade starts at
  unity and a front just past the plateau is attenuated, not missing. How deep into the fade a front may land
  is a continuum with no measured line, so only the window's end is judged.
- `OpensAfterArrival`, judged on the leading-edge loss: over the ceiling AND worse by
  `MisplacementMarginDb` (3 dB, twice the discarded energy that moving the window would give) than the
  same gate placed on the channel's own arrival. The comparison keeps a merely short gate from reading as
  misplaced: the project default cannot hold one period of a 55 Hz subwoofer anywhere, and the field
  session read −19.4 dB at the channel's own arrival and −19.4 dB at the shared one; a gate the user cannot
  fix by moving must not stop them. Field misplacements are 44–70 dB apart on this comparison and short gates
  0.0 dB, so the margin only needs to clear arithmetic noise. `PhaseGatePlacement.AllowsPerCurveGate` makes
  the same comparison with no margin, which is right there because its penalty is one curve falling back to
  the shared window; here the penalty is an amber note and two refused commands.

The one-line warning says which miss the reader is looking at (the response's tail, or none of it);
`FormatDetail` is both the tooltip and the body of the refusals, so they cannot disagree. A misplaced
gate refuses Auto crossover and Auto delay (`GateIsMisplaced`, `RefuseOnMisplacedGate`): neither search reads
the gate, but both are verified on what it produces (curves, loss read-out, the outcome metric in the
alignment log). An AI import quotes the refusal phrase instead of showing a dialog. The verdict describes
only the side on screen.

## Warnings

The host shows one warning line (`WarningChanged`), chosen in this order:

1. Gate placement (amber): a window that opens after the drivers turns every curve into a tail.
2. Hybrid disagreement (`DescribeHybridDisagreement`), only while the hybrid is drawn.
3. Array composition mismatch, a warning rather than a refusal.
4. A chosen calibration not applied to part of the plot.
5. A chosen calibration, or Off, that is not the one a drawn channel was measured through
   (`DescribeForeignCalibration`; amber, an information line under Off). The list offers every entry on the
   machine, and a named slot can hold another microphone's file, so nothing else says the plot is no longer
   read as measured. Under a hybrid the drawn curve is the capture, which the selection re-reads through its
   own swap, so the capture's file is compared (an array channel without one is drawn from its IR, whose
   file is compared); an aggregate keeps its own files under a named curve and loses them only under Off.
6. Own-calibration mismatch between channels.
7. Point-measured fallbacks inside a hybrid (neutral info colour; nothing is wrong).
8. Crossover spread (red, see below); with none of these the line is hidden.

Warnings are hidden on an empty group view,
since they would describe channels the user can no longer see. For the same reason the calibration notes
(4 to 6) read only the group on screen.

### Hybrid spread thresholds

`DescribeHybridDisagreement` judges the two families differently, because they are tethered differently: a
moving-microphone set by how far its channels' datums disagree, an array by how far any one stands off its
own impulse response.

- Moving microphone (`MovingMicSpreadWarningDb` = 3 dB): calibrated on a known-good seven-capture set
  (`HybridOffsetDatumMeasurement` reports it from the archived cabins) at 1.4 dB on one side and 0.6 on the
  other, the residue of the two measurement families differing in shape, as they should. It must catch
  per-capture failures several times larger: a changed input gain, a frame length or window that moves the
  noise-slope compensation (a curve, not a constant), a capture from an unrelated session. The threshold was
  5 dB while the datum was read on processed curves, where the same set read 2.4 and 2.7, mostly the chain
  failing to cancel; reading on the raw pair removed that and the threshold kept the same margin.
- Array (`ArrayDatumWarningDb` = 1.5 dB, on `WorstDatumDb`): an array is levelled by the same loopback as the
  IRs, so the families are one measurement and each array should sit on its IR. The archived arrays read
  −0.9 to +0.1 dB off theirs (tweeters lowest), and the synthetic control with a capsule error +1.7. The
  check is absolute because a spread cannot see a shift every channel shares: the
  12-microphone set of #214, whose loopback ran on a second interface, read −14 to −30 dB.

The read-out shows the hybrid's health figure (`VirtualCrossoverHybrid.ReadOut`): the moving-microphone
set's offset, or the worst array's stand-off. A large one means a different input, calibration or driver.

### Array composition and calibration notes

Arrays differ by position count and by the calibration read through. Neither breaks the level (the loopback
does that per measurement), so neither is a refusal. The composition check covers every array in the
project, both sides and muted channels included, because composition is a property of the measurements, not
of what is drawn. The cross-side case is the important one: a left averaged over seven positions and a right
over five are each consistent, yet the dashed opposite sum compares two listening volumes as if the
difference were the car, and `LiveCaptureDocument`'s array rule rightly does not object (it is about
levelling). A mono pair is listed once.

`SameArrayCorrection` compares aggregates band by band (tolerance 0.01 dB): an array whose positions carried
different calibration files declares no single curve (`CalibrationIsAggregate`), and comparing only the named
curve made two aggregates and an uncalibrated array all "equal".

`DescribeUnappliedCalibration` speaks only when the user chose a microphone: a capture with mixed
per-position calibrations keeps its own aggregate correction, since there is no single curve to swap.

The datum list and `ChannelOffsetsDb` are positional over the whole set, muted included, so a mute cannot
hide the outlier and figures cannot shift onto the wrong driver's name.

### Crossover spread

`CrossoverGroupDelayWarningMs` (15 ms): a driver whose crossover has pathological group delay (a narrow or
steep low-frequency band-pass) arrives so late that Auto delay pushes every other driver out to match. The
warning reads the applied delays directly rather than a group-delay proxy, because a narrow LF band-pass
peaks late in its own band and only its arrival across the overlap tells the truth. Bypassed channels are
excluded.

Only the front chain counts (`VirtualCrossoverAlignmentStages.Split`): only its junctions are searched, so only its members
drag each other. Later stages are placed against the settled chain; a rear fill deliberately sits the rear
fill offset behind (default 15 ms, the threshold itself) and a centre carries its own path, so counting them
would make Auto delay's own output trip the warning. A rear-only project is walked as its own chain and
keeps the warning. `ExcludedGroupsNote` names the groups the figure left out, only those the project has.

## Sum loss and group views

The group view selector (`SelectedGroupView`, read from the control so a mid-edit redraw draws the new pick)
decides which part of the installation the frame is about. The channels are filtered once per frame, so the
curves, Sum, loss and read-out describe one set. Drawn and summed sets differ where a centre is shown: it is
drawn to be compared, not added (`VirtualCrossoverGroupViews.ParticipatesInTotalSum`), so junction read-outs
use the summed set (pairing a drawn-only centre with a front driver would invent a crossover).

- A view spanning more than one listening group withholds the loss entirely. Front against rear combs
  however well either is tuned and no filter hands a band from one to the other, so the figure would report
  damage nothing can repair. Those views quote cross-group arrival and level instead (group deltas, placed
  directly under the loss column where they stand in for it).
- The same silence applies where a single-group chain holds no junction: Rear + Sub on the reference car is
  subwoofers to 110 Hz and a rear fill from 290 Hz with nothing crossing. `GetAdjacentPairs` declines that
  pair, but the loss total would still be computed over a crossover that is not in the car.
- `VirtualCrossoverFrame.QuotesJunctions` is decided before the frame's awaits because the junction phase block is withheld under
  the same condition.
- An empty view (a rear view of a front-only car) says so; with nothing resolved at all the no-sources hint
  is shown instead.
- The stereo Δ block follows the Show selector through a filter, never a shortened list, because a block's
  list position is its identity in the coordinator's cache.

### Groups view

`AcousticViewBuilder.GroupSumCurves` draws one summed line per zone and nothing else: a dozen driver traces would bury the
only relation this view is for, and that relation is between the groups' sums. All lines are gated on ONE
anchor across all shown channels; per-group anchors would each hide their own group's delay. With the hybrid
on, a group line is built like the Sum (`VirtualCrossoverHybrid.Sum`) over the group's members; a group that cannot
produce one falls back to its measured sum rather than disappearing. `HybridMagnitudes.Subset` narrows the per-channel
lists positionally but keeps the set offset and datums, which describe the capture set and keep every line
on one axis; it is pure and pinned by tests because an off-by-one slice would draw a plausible wrong curve. Colours
are semantic per zone. The target is drawn here too (a rear fill's level against the house curve). Picking
Groups moves the view radio to Magnitude visibly (there is no group phase or impulse), and the
phase, group delay, impulse and step radios are muted while it is selected.

### Junction phase read-out

Informative only; it feeds nothing back into alignment. It is computed off the UI thread because it reads
through the phase gate: an 8-cycle FDW is one transform per distinct window length per channel, 50–100 ms for
four channels the first time a response set is seen (2 ms afterwards, since `DataHelper` memoizes each gated
spectrum per impulse array and the phase view warms the same entries). A delay drag makes every frame the
first time. The gate is read on the UI thread and only plain numbers cross over. It reads the SUMMING
channels; in a grouped view a spectator failing the leading-edge guard may send the curves to a shared window
while these figures keep per-curve placements, which is the right way round. The direct-sound loss (FDW-8) is
the same block's spectra summed, so it is built in the same task from the same windows.

## One scale for both sides

The magnitude view's dB axis and the loss axis take one range for both sides, so flipping the side selector
changes the curves and nothing else: autoscaled per side, a quieter right side redrew on a lower axis and read as a
level jump even where the tune was identical (#214). `VirtualCrossoverSharedScale` keeps each side's extent
(`ScaleExtent`: lowest and highest level, deepest loss), the plot draws the union, and the value axis rounds outward
to 5 dB so the small differences between how the two views read one curve (a sum drawn solid on its own side and
dashed, through `OppositeSum`, on the other) land on the same limits. The loss axis takes the deeper loss of the two.
A user's zoom lives in the axis view range and survives; only Minimum/Maximum are set.

The redraw path pays nothing for it. The shown side's extent is read from the curves just drawn and remembered; the
side not shown contributes its last known extent: remembered from its own drawing when it was shown, and re-read
(`MeasureOtherSideAsync`: its channel, raw and group curves through its own gate placement, its sum outside the
hybrid, the loss the selector draws, Direct included) only once edits have paused for 250 ms. Reading it on every
frame would double the curve work and add a junction read (50–100 ms under Direct) to each step of a drag. An extent
is kept only under the view options it was taken with (view, group view, hybrid, sum, loss window, target, gate
template, smoothing, calibration, spatial-average method; the gate's pins stay out, as they swap with the sides), and a
project load forgets both. The hybrid sum of the side not shown is not re-read: it is the dashed curve the shown side
already draws.

## Opposite-side sum

The opposite side's sum comes from the metrics (shared coordinator cache), but its curve is built by
`MagnitudeGateSnapshot.OppositeSum` so it windows through the OPPOSITE side's own gate placement. Both sides
must be drawn by the same method, or the comparison is between methods rather than tunes; with the hybrid on
and the opposite side short of a capture, the curve is dropped.

### Opposite-side hybrid sum

`VirtualCrossoverHybrid.OppositeSum` uses that side's own channels, captures, loss and gate placement, and the
offset both sides share (none for arrays; one median over both sides for a moving mic, see
[spatial-average.md](spatial-average.md#set-offset-and-spread)). Separate offsets would erase exactly the L/R
level difference the captures measured, and moved a mono channel by their difference whenever the side
selector flipped (1.7 dB on #214's set). Sharing an offset holds only if both sides' captures are one set,
which `CanDrawOppositeSum` checks (per-side checks cannot: two relative capture runs are each consistent
but say nothing about their relative level). One anchor and offset serve that side's channels and its sum,
and the loss is smoothed only at the end of the reconstruction.

## Hybrid handoff to the EQ Wizard

`VirtualCrossoverEqHandoff.HybridCapture` is the decision whether a side's hybrid is being handed over. It is cheap and reads
only live state, and both the handoff and the return guard ask it, so they cannot disagree. An earlier
version derived it through the offset resolution, which failed mid-redraw: a target edit in the wizard
invalidated the panel and a Return clicked before the redraw was refused as if the hybrid were off.

`VirtualCrossoverEqHandoff.SpatialAverage` supplies the capture and its offset onto the IR axis. The offset belongs to
the capture SET (the session's `LastHybridOffset`), so it is normally the last magnitude render's; the phase and impulse views never
build one, so it is resolved on demand, since a stale height would hang the curve tens of dB from the Target
Level. If it cannot be resolved the handoff falls back to the IR and the token records it; the return guard
then refuses the bank if the panel meanwhile draws a hybrid.

## PEQ and FIR handoffs

- The PEQ handoff carries the active side, the gate snapshot, the active pin and the last redraw's anchor so
  an unpinned gate opens where the plot's did. The render anchor is used only when the session's `LastRender`
  still describes the current settings; otherwise the builder reads the channel's own front. The calibration
  the channel was rendered with (per channel under Own) and the spatial-average mode travel along, and the
  return compares against them per side. The session's `ProjectGeneration` is bumped on every bind because channel objects
  are reused across projects; a return into a replaced project is refused and the host keeps the wizard open.
- `VirtualCrossoverEqHandoff.PhaseContext` freezes the other drivers as processed IRs, not drawn curves, because the wizard
  has its own gate and a curve gated at this panel's window could not be re-read. It is resolved over the set
  the wizard draws (`ProcessedChannels.PhaseNeighbourhood`) so a hidden driver cannot move visible windows,
  and is null when the render is stale. The gate travels as the user has it (detrend mode included) while the
  curves render as Manual against one τ; a pinned gate stays pinned; the source responses travel so the wizard
  can re-resolve placements when its own window changes; the channel colour travels.
- PEQ export reuses `EqWizardImportExportCoordinator` so there is one exporter; sheets state the processor's
  rate and Q convention, not the measurement rate, and carry no invented fit statistics.
- An imported FIR file clears `FirDesign` (taps only); export writes at the processor's rate.

## Source loading

- Reference resolution: the history entry first (it survives file moves), then the stored path, then the
  same file beside the imported session, then the relink folder (`VirtualCrossoverSourceLocator.Locate`).
  A missing source leaves the side unresolved rather than failing the load.
- Interactive picks capture the concrete slot, settings and a revision (`BeginSourceLoad`) before awaiting:
  the side selector, Mono (which reroutes `SideState`) or a session import can change during the load. A
  `Clear()` or a newer pick refuses the landing.
- `TryAssignSource` is shared by interactive picks and the silent restore; it enforces the revision, the
  loopback transfer IR requirement and the sample rate. A project runs at one rate: mixed rates are refused,
  checked against every resolved side of every pair. The interactive policy can prompt; the silent restore
  leaves the side unresolved with a warning glyph.
- A relocated path is pinned only if the measurement landed: once a stored path exists it always wins, so
  pinning a refused file would stop the next relink from searching the folder the user pointed at, and the
  autosave has no session file beside it to search again.
- The spatial average reference is resolved before the measurement's early exit (it may return while the
  source is missing); clearing a source drops it.
- `SetChannelCount` invalidates a removed channel's slots before disposing its control, so a pending load
  refuses to write back (otherwise `KeyNotFoundException`).

## Project restore order

`VirtualCrossoverSession.RestoreSourcesAsync` takes the resolve as a delegate, so the order is unit-testable
without a panel:

1. Wipe BOTH physical slots of EVERY channel before the first source resolves. Per slot, because through the
   effective accessor a mono pair's right slot is unreachable, and a stale measurement from the previous
   project would resurface when the pair stops being mono. Across all channels first, because
   `TryAssignSource`'s rate guard scans every resolved side: cleared one channel at a time, an imported session
   at a different rate lost the vote against the previous project's channels and only the last channel
   resolved (a field bug).
2. Resolve both sides of each channel together (stereo Auto delay needs them); a mono pair resolves once.

Around it, `ApplyProjectAsync` binds in order: channel count, view flags (each newer view flag is written
beside the older one it falls back to, so older builds open the nearest view), the per-view Sum toggle,
`RefreshProcessorRowAvailability` before filling blocks (it re-pins their height), `sideLock.Remember`,
`UpdateViewDependentControls` (selector events were suppressed, so a session saved in Groups otherwise
reopened with muted controls bright), sources, `SettleSpatialAverageMode` and the processor rows again (a
project without its own rate takes the measurements'). The loading state (whole control tree disabled, since
blocks are rebuilt, plus a loading note and wait cursor) is cleared before the final redraw so the last frame
is the real plot rather than the note.

## Saving

Saves run on a debounce (`SaveDebounceMilliseconds`, 2 s) from `ScheduleSave`, which every change passes
through. A failure (a read-only install directory, for example) is reported once per session and re-armed by
the next success: silently failing saves once lost whole tuning sessions. Nothing is stored before the stored
project has loaded; the host pushes the shared target while the form is built, and saving then would write
the default project over the real one.

## Side lock

The Lock beside the side radios is read in `ScheduleSave`: `sideLock.Follow` carries whatever moved on the
shown side since the previous save onto the hidden side, ahead of the redraw and save. Engaging copies
nothing; the pairs are remembered as they stand (rules in `VirtualCrossoverSideLock`). It is on by default
because car tunes are usually symmetric, and it is not stored: unticking is for working one side alone, and
the next opening starts symmetric. The designer ticks the box before the handler exists, so the constructor
engages it by hand. `Remember` is called after a bind, after Auto delay commits (a per-side polarity decision
is invisible to the lock as a difference) and after the crossover wizard (which writes both sides and may
carry only one edge).

## Copying between sides

The L→R / R→L commands copy parts of one side's chain chosen in `VirtualCrossoverCopySideDialog`. Crossover
and PEQ (the magnitude shape, which describes the driver) are ticked by default; gain, delay, polarity and
all-pass are opt-in because each aligns a driver against its own side's level and geometry. The source is
never copied and mono pairs are not offered.

- The phase angle has its own tick (a timing decision) and is copied as the number; its reference follows the
  target side's crossover, as the device would.
- FIR kernels are immutable and shared by reference.
- All-pass filters live in the PEQ bank as bands; the Peq and AllPass scopes split that list by band type and
  the uncopied kind survives on the target.
- Over `EqualizationCurve.MaxBandCount` the copied kind gives way, because an unticked scope promised the
  target's bands stay. With both copied, the all-pass stays: it sits on a junction this side was aligned on.

## View-dependent controls

`UpdateViewDependentControls` mutes curve toggles on views that cannot draw their curve. The Sum exists on
magnitude, phase, group delay and step but not impulse; sum loss and target are magnitude-only (a dB shape,
the rule `OverlayTargets.SupportsMode` applies too); smoothing is dead in impulse and step. The Target button
stays live and switches to the magnitude view. The loss selector also picks the read-out column's window, so it
stays live in phase and impulse, and is muted only where no loss is quoted. Muting reads the intent, not a
child's `Enabled`, which reads false through a parent disabled during a load. The Sum toggle keeps one answer
per view (the impulse view writes none). The Target and Hybrid toggles are muted by hand, not through
`UiStyle.SetTextEnabledLook`, because that helper memorizes the colour it muted and these toggles are
recoloured (with the target, or as an unused-capture reminder). The DSP-mode radios span two containers, so
exclusivity is wired by hand and the other container is cleared first so `OnDspPlotModeChanged` never sees
two checked radios.

## Processor rate

`ProcessorSampleRateHz` is the rate every simulated filter is designed at, not the measurement rate
(`PreparedDspResponse`). A named processor answers from the catalog (a corrected preset corrects every
project); Custom without a stored rate follows the measurements. The coordinator keys its cache on this rate,
so a change re-runs every channel; notes alone are just a save. The dialog compares intent ("follow
measurements" vs "48 kHz" differ once measurements change). Confirming stores the phase-control answer; a
device without phase control or FIR drops rotations and kernels, which would otherwise bend curves with no
field on screen. `ProcessorMaxDelayMs` bounds automatic proposals only; manual delay fields keep a wider range.

## Reset

`ResetChannelsAsync` binds a fresh project, the same path as a first run, so there is one definition of
"default". The selected microphone calibration (a property of the rig) and the shared EQ target curve (owned
by the EQ Wizard) survive; the target level resets. The user is asked first; after confirmation the project IN
MEMORY is backed up (`SaveResetBackup`), because the autosave lags a debounce and may not exist. The backup is
one deep, and the next reset overwrites it, which is why the question points at Save session for a tune worth
keeping. The reset awaits the stored project load, like an import.

## Target curve

The session stores the target it was tuned against, but hands it to the host because the EQ Wizard owns the
one app target; it comes back through `SetTargetCurve`. A session without a stored target starts carrying the
current one. The target is hung at the user's Target Level rather than fitted, because transfer-function dB
has no absolute reference. The target dialog is opened as the EQ Wizard's (`Mode.EqWizard`) so the smoothing
vocabulary is identical from both buttons. Its live preview updates memory and plot only: the autosave timer
keeps ticking inside a modal loop, and a few seconds of dragging would write an uncommitted preview (with a
stale preset name) to disk. Save stores; Cancel has nothing to undo.

## Calibration selection

The selector offers the configured calibrations plus the session's own curve when no configured entry matches
it (`VirtualCrossoverCalibrationSelection`). A bound project's curve decides; its id is only a hint, and a
legacy id that resolves to nothing keeps the panel's selection. A session curve hands over to a configured
entry as soon as one holds the same curve, and the persisted form is re-derived through the same path the
curves use. Under "Own (as measured)" each channel is drawn through the calibration its measurement recorded,
null if none (never the panel's), and a spatial average uses the capture's own correction; `Calibration` is
null because one field cannot hold a per-channel answer. An imported session carrying its curve starts on it
and offers to add it to this machine's list (right for the author's measurements, wrong for measurements taken
here with another microphone).

## Staged Auto delay

The alignment stages, tuning constants and tie-breaks live in `AutoAlignmentEngine` / `AlignmentSelection`
(unit-tested in `Resonalyze.Dsp`). Each run is an absolute proposal from sources, crossovers, gains and PEQ;
previous delays and polarities are ignored.

- `VirtualCrossoverAutoDelay.Prepare` returns a launch or a refusal. Interactively refusals are shown and the broad-window
  question (no crossovers configured, so the search falls back to a broad midband window) is asked; headless
  (AI import) that question is a refusal quoted in the summary.
- Stereo runs whenever some non-mono pair has both sides resolved (the highest such front-chain pair is the
  L/R bridge; tied at a rear pair the scene would anchor to the fill). The bridge band is the intersection of
  both sides' bands; no overlap refuses.
- Bypassed participants are refused: the identity chain ignores overrides, yet the channel would join the walk
  and receive a delay applied later when bypass is switched off.
- No DSP runs on the UI thread; the direct-sound crop and every `ApplyChain` happen in
  `AlignmentReprocessor`, whose run-scoped FFT cache re-FFTs only the channels whose overrides changed. The
  crop gives identical final delays to a full-length run (validated) at a fraction of the cost.
- Records may carry a playback-crosstalk click at one fixed early sample (an electrical copy ahead of any
  acoustic arrival, seen in every record of a field session). It biases GCC-PHAT by a sub-sample on most
  configs and picks a wrong branch on gentle slopes, so `CleanCrosstalkHeads` head-gates convicted records and
  names them in the log.
- The dialog edits layout-neutral magnitudes; the project stores scene offset and level difference
  layout-signed so older builds read and resave the file. They and the rear fill offset are persisted only on
  Apply. `CommitAutoDelayResult` is synchronous so Apply lands fully; the outcome metric is appended
  best-effort afterwards and its failure is not reported as a failed Apply. The diagnostic log is written at
  the proposal stage so a discarded run can still be shared.
- A launch-time warning reports drivers whose left and right measured polarities disagree (from the raw
  transfer IRs): alignment can mask a swapped wire by inverting one side.

Stages:

1. The front chain, walked by the engine exactly as an unstaged project always was (junctions by band
   centre, `VirtualCrossoverJunctions`). A project without rear fill or centre, including every project from
   before zones, puts everything in the chain and takes the old call unchanged. A rear-only project is its own
   chain.
2. The rear fill, one delay per group (a rigid body). In stereo it is placed per cabin side against that
   side's own front stage (the listener's comparison), so the rear inherits the front's L/R relation. Each side
   uses its own band, since sides may carry different corners. A two-way group first settles its own junction
   (`SettleWithinGroup`): placed one driver at a time, each landed near its answer while their mutual,
   possibly hand-tuned alignment was overwritten. The rear fill is wanted behind the front (precedence effect
   keeps the image on the dash); the centre takes no offset.
3. The centre, which has no side: read against both front sides over the same band (intersection of both
   references, narrowed to the centre) and placed at the midpoint. The two readings should differ by the scene
   offset; beyond `CentreWitnessToleranceMs` (0.35 ms, the difference between an envelope arrival and a phase
   extremum on two paths, narrower than a lobe) the placement is reported as uncorroborated.
   `VirtualCrossoverGroupPlacement.ChooseCentreReferences` picks one front driver per side where both offer the
   same block, each side's own content otherwise, and refuses when they share everything (the manual recipe:
   mute the rest of the front and match the centre to the voice-band driver). A refusal is final: a midpoint
   between readings sharing no content is not a placement.

Details:

- References are ONE front driver (`ChooseReference`), not the summed stage: a sum's band-limited arrival
  belongs to whatever plays earliest in the band, for a rear fill a tweeter it barely overlaps.
- The engine works in reference/far ROLES. The driver's side is the reference (`IsFarSide`); right-hand drive
  hands the plan mirrored so a positive scene offset makes the left side lead. Reading cabin sides instead once
  timed every rear driver against the opposite front. Pair links aim the far-side descent's prior at the
  cross-side-consistent delay; a link band must satisfy `VirtualCrossoverAnalysis.MinimumArrivalBandRatio`.
- Mono channels handed to the stereo engine come from the walked left side, not the union: a staged union keeps
  a mono centre or rear for the reprocessor, which tripped the engine's "every mono must be in the left walk"
  guard on the reference car.
- The engine's override maps are sparse: the reference channel has no entry. Never index them;
  `ApplyInnerSettlement` and the normalization both treat absence as zero.
- A group that cannot be measured has its current delay written explicitly, because an absent override means
  zero to the reprocessor.
- Placements the run does not trust are marked in the report (`PlacementDecision`), so a doubtful centre is not
  applied on looks. `StrongPlacementCoefficient` (0.6) is below a junction's bar because two groups playing one
  band from different places never correlate like a crossover. `HaasPolarityIrrelevantMs` (5 ms): beyond that
  intended offset the groups no longer sum audibly and a polarity sign describes the measurement only.
- Gains (`GainBalanceEngine`) use bands from crossover corners and levels from the run's final snapshots; the
  current gain is baked into the chain and subtracted, so the proposal is absolute. In stereo, right channels
  are judged against their left peers tilted by the entered L−R level difference.

### Delay normalization

`NormalizeStagedDelays` slides every participant together until the earliest is at zero. The stages ignore what
a processor can dial (a rear fill pushed past the front asks the front to go negative), so nothing is dialable
before this pass and all relations survive it. It iterates the scope, not the map's keys: the map omits the
engine's reference (`AutoAlignmentEngine.NormalizeAndVerifyFeasibility`), and shifting only keys would leave the
reference at zero while its siblings moved.

The ceiling check after the shift is unconditional, since a rear fill can push the latest channel past the
processor's range without anything going negative. Because the fill is the one preference in the proposal, a
refusal reports the largest fill that would fit (`LargestFittingRearFill`). That is found by walking down from
the requested fill on the DSP's 0.01 ms grid, not in closed form: the dialable span is not monotone in the fill
(a rear group whose co-arrival placement came out negative first closes the span as the fill grows, then widens
it). It returns null when no fill was in play or even zero fill does not fit. The raw placements are kept from
before the shift because the scan folds `min(0, minimum)` in itself.

## Junction views

The lower plot's correlation and coherence modes share the pair selector, the rebuild loop and the processed
inputs; a result is dropped if the user switched mode mid-compute. Pairs come from the last processed render,
narrowed to the view's summing chain (`ProcessedChannels.JunctionsInView`; band order alone is not a chain once
rear fills and centres exist, and a cross-group view lists nothing). The chain plot is drawn without the delay
term, since a bulk delay wraps the phase into a sawtooth and swamps the filter group delay.

`BuildCorrelationView` (internal for the correlation-view harness):

- Both channels enter PROCESSED, so lag 0 is the current alignment and each reading is a correction to the upper
  channel.
- No gate anchor is passed: each channel is windowed at its own band-limited front, as every junction
  measurement of Auto delay is (`BuildAlignmentBins`). The sweep rotates the windowed cut with the same bins as
  `SumLossEvaluator`, so the drawn score IS the surface Auto delay searches (re-gating each probe through a
  stationary window drew something else, see `JunctionLossSweep`). The read-out beside the plot keeps its
  shared placement: it measures the whole sum inside the pair band, this view measures the pair.
- The crop spans the whole side (a shared offset keeps relative timing) but decides nothing the score reads;
  valid ranges are shifted into the crop frame so front detections match the search's, which matters on
  delayed or glitch-headed records.
- The window spans 1.5 crossover periods each side (floor 3 ms), so neighbouring comb lobes are visible even at
  80 Hz. The step is `min(window/60, period/10)` floored at `window/300`: a fixed points-per-window count aliased
  at high junctions (at a 20 kHz-class split window/60 is a whole period and the comb sampled flat); the floor
  bounds the sweep at about 600 points per polarity.
- Four independent reads run in parallel: the whitened full-record comb (deliberately untrimmed; reflections
  are its subject, the honest read at bass junctions), the whitened direct-sound twin (the cut the engine's
  direct-coherence witness reads, answering where the drivers align), both polarities of the summation
  score from one bin set with the search's own settings (per-channel windows, search-side level match, whose
  absence reshapes the lobes when channel gains differ), and the band-limited arrival lag.
- `arrivalLagMs` for the agent package — lower arrival minus upper, the band-limited envelope fronts of the
  processed pair — is computed here as well; the plot does not draw it. A dashed *arrival* marker that stood
  for the search's re-anchored read (`AutoAlignmentEngine.ReadJunctionArrivals` on a frozen render snapshot)
  was tried and dropped: the read is ten band-limited arrival analyses and six chain renders, ~270 ms of a
  ~320 ms build at 96 kHz, and the owner found the marker uninformative. The frozen chain and source on
  `ProcessedChannel` and the search snapshot went with it; the engine still walks that read itself.

`BuildCoherenceView` hands the same cropped processed pair to `VirtualCrossoverAnalysis.ArrivalCoherenceLadder`.

## Crossover wizard

`OpenAutoSetupWizard` detects each channel's usable band and driver type from the raw magnitude (fixed 1/3-octave
smoothing), lets the user confirm the types, and writes LR24 splits and cut-only gains; delay and polarity are
left to Auto delay. The band read is gate-independent but its result is checked on gated views, so a misplaced
gate refuses it. The read is bounded by the measured band (or the window's leakage would put a low corner an
octave too low), discounts frequencies with poor coherence (γ² averaged per 1/3-octave point) and is bounded by
the distortion-clean band. Existing corners (FIR ones where the IIR crossover is off) decide which of two similar
drivers plays lower. Both sides of a stereo pair get the same crossover and gain. Because a phase rotation is
stated at the crossover, the wizard resets rotations it would otherwise leave meaning a different filter, and
says so. `ReorderIntoSlots` reuses only the reordered members' slots.

## Session import and relink

A dropped session arrives via a tab switch that has just started the stored project load, so
`ImportSessionFileAsync` awaits that load; otherwise it would land on top of the import and restore the replaced
session. The import immediately becomes the autosave. Stored paths come from the measuring machine, so
`RelinkMissingSourcesAsync` asks for one folder that answers for every side naming a missing file (history-only
references are not fixable this way); it stays this session's extra search root until another project binds.
