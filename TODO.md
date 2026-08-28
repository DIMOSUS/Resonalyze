# TODO — tech debt from the code reviews

Open items found during the review passes that were deliberately reported
instead of fixed. Completed items are removed (they live in git history), and an
item describing work already done keeps only the residual. Grouped by area,
highest-value items marked ★. `[✗]` marks a settled decision kept on purpose, so
the same idea does not get re-proposed — those are not open work.

Last audited against the code on 2026-07-26. That pass ran a duplication /
dead-code sweep and landed the mechanical half of it (dead `UiStyle` factories,
five clamp helpers collapsed into `ClampValue`, the options-panel gate/Tukey
boilerplate hoisted into `ImpulsePreviewOptionsForm`, four `IsWasapiBackend`
copies replaced by `AudioBackend.IsWasapi()`, and the two capture pumps put on a
shared `CapturePump<TSlot, TBlock>`). The two structural findings it did NOT
land are filed below, marked ★. That pass dropped
the "Windows live-checks pending" section: every item in it shipped in v0.5.3,
and only the person with the car and the microphone can tick them, so they
belong in the next field session rather than in a register nobody else can
close.

## DSP library (`dsp/`)

- [ ] **`CrossoverAutoSetup.Optimizer.Score()` recomputes every channel** on
  each junction/gain trial (~300–1500 calls per junction). Filter magnitudes
  are cached, but the amplitudes of untouched channels are not. **Unprofiled:**
  this is a reading of the code, not a measurement — build `-c Tracy`, profile a
  real ranking run and confirm `Score` dominates before paying for cache
  invalidation inside an optimizer (a stale score silently degrades
  convergence).
- [ ] **The DSP chain is assembled twice.** `DspChannelChain.Response` evaluates
  gain · polarity · delay · crossover · PEQ analytically per
  frequency, while `PreparedDspResponse.Create` builds the same chain as a
  cached biquad cascade. The two evaluation strategies are both wanted (one for
  a single plot point, one for FFT-bin processing), but the assembly order and
  the gain/preamp folding are copied, so a new stage has to be added in both
  places or the two quietly disagree. The rest of the review's dedup work is
  merged; this is the survivor.

## Virtual DSP / Time Alignment

- [✗] **Phase-slope (residual group delay) as an Auto delay score prior —
  REFUTED on real measurements** (2026-07-10, do not re-propose). At the true
  alignment the inter-channel phase slope is NOT flat — it carries the honest
  driver + non-matched-filter group-delay difference, which reaches a FULL
  period at real junctions (left woof/mid true lobe 1.02 T from the flat-slope
  point; right mid/twr 1.82 T). Flat phase slope is mathematically the GCC-PHAT
  peak, so this prior would re-trust exactly the lobe `PhatSeedMinDominance`
  exists to distrust; any weight strong enough to separate lobes (≥0.5 dB/T)
  flips the right mid/twr winner to a wrong inverted candidate. Re-probed at the
  final cascade state: the two sides want OPPOSITE corrections (left +0.53,
  right −1.84 ms), so no scene-preserving pair move satisfies it on both sides.
  At most surface the per-candidate residual GD as a log diagnostic.
  Re-refuted independently on the v4 cabin (2026-08-06, PR #69) after the owner
  proposed it again with FDW gating: measured at 4 and 6 cycles, with the DFT
  taken exactly at each centre rather than at the nearest bin, the slope
  prefers the half-period FLIP PARTNER (~11.9 ms non-inverted, RMS 12.3°)
  over the correct lobe (9.47 inverted, 18.0°) — it cannot break precisely the
  ambiguity it would be asked to break. FDW 6 cycles does not discriminate at
  all (30-33° across every candidate).
- [ ] ★ **The predicted-arrival probe has no applicability gate.** Its chain
  term is measured on a flat reference impulse, so it transfers only while the
  source still radiates the junction band: measured across a source matrix
  (PR #69) the error stays inside 0.24 allowances for realistic driver
  roll-offs but reaches 1.20 where the source has strong structure INSIDE the
  band — a steep low-pass leaving the channel barely present, or an all-pass
  twisting its phase. Today the only guard is the margin: a conviction needs
  2.0 allowances and shaping error has not reached it, so shaping pushes a read
  to `Inconsistent` (which withdraws the pair) rather than to `Latched`. That
  is a measured bound over a finite matrix, not a proof for an arbitrary source
  response. The review's suggestion, and the right shape: gate applicability on
  a measured property of the bypassed band — enough genuinely radiated
  bandwidth, no excessively narrow resonance or phase rotation — rather than
  relying on the margin alone. Derive the threshold from field data, not from
  a guess.
  Two formulations were built and measured (2026-08-06), both rejected — do
  not re-try them blind. (a) *The bypassed front must BE its strongest
  feature.* The hazard needs two comparable components, so the absence of a
  second one would be a real precondition; but a woofer in a cabin ALWAYS has
  its strongest envelope peak well after its front, so this refuses 100% of
  field channels and reverts the branch to its pre-PR behaviour outright
  (v4 mid back to 6.92, matrix back to 14.82/3.09/8.09/2.70/2.57). It is a
  blanket disable, not a discriminator. (b) *Nested-band stability of the
  bypassed arrival.* Correct in principle — reweighting two components needs
  them to differ spectrally, which makes the bypassed read band-dependent —
  and it preserves every field conviction. But the nested band the analysis
  admission ratio allows is only ~12% narrower than the full one, which is
  too little to detect the ambiguity: no fixture could be built that it
  refuses. Widening the inner band far enough collides with the upper-half
  probe's own band, where disagreement is the modal-latch SIGNAL rather than
  a disqualification. A workable gate therefore needs a different observable
  than either, or a way to separate those two meanings.
- [ ] **No integration test drives the predictor through the real
  `AlignmentReprocessor`.** The DSP tests now reproduce the production
  padding/range semantics by hand (`ShapedFrontProbe`, and a dedicated
  regression asserting the bypassed array is longer than its content range),
  which is what caught the window mismatch. What is still unexercised is the
  app-side assembly of those snapshots — `AlignmentReprocessor` fills the
  chain, the bypassed response and both ranges, and only its ordering and cache
  are covered. Needs a seam, since `Resonalyze.App.Tests` cannot see DSP
  internals; low value against the hand-built equivalent, so it is filed rather
  than blocked on.
- [ ] **Nothing judges the per-band stereo Δ.** The read-out exists
  (`ComputeStereoDeltasAsync` + the metric panel's Δ L−R and level columns), but
  the numbers are only presented: no warning when a band's Δ walks off the top
  pair's by a sizable fraction of its junction period — a period slip that
  survived the per-side sum optimization — and no candidate-list re-pick to
  correct it for free.
- [ ] **`SceneLockToleranceMs = 0.05` is aggressive for pairs whose localizable
  band is narrow and low** (e.g. reaching only ~300–400 Hz): the band passes the
  minimum-width admission, but the temporal certainty of such a narrow-band
  envelope arrival is typically worse than 0.05 ms, so the lock can pin the
  channel inside the measurement noise. Acceptable today thanks to the guards
  around it (minimum lock-band width, SNR gate, invalid-arrival refusal, the
  scene-preserving co-move). Follow-up: make the tolerance a function of the lock
  band's width/center (a fraction of the band-center period, floored at
  0.05 ms), or of an arrival-uncertainty estimate (envelope rise time /
  FirstArrivalProminence), so a wide tweeter band keeps the tight pin while a
  barely-localizable pair gets honest slack.
- [ ] **`AlignmentSelection` polarity margin as a model, not a constant.** The
  invert-preference margin was raised 0.25 → 0.5 dB in the #32 work (a real
  left woof/mid junction had the inverted impostor out-scoring the true normal
  candidate by 0.23 dB — the old 0.25 saved it by 0.02). The broader fix stands:
  make the margin a function of junction frequency / band coherence, or add an
  independent polarity witness (`EstimatePolarity` on the band-passed arrivals —
  it exists, but only as the launch-time L/R mismatch warning, not inside the
  per-candidate decision).
- [ ] **Promotion constants are field-anchored, not modelled.**
  `WideWindowPromotionMarginDb` (1.6) and `PromotionReachPeriods` (2.5) in
  `AutoAlignmentEngine.cs` are set between a handful of field observations with
  thin headroom (the false-hop 1.40 dB vs genuine-recovery 1.91 dB split has only
  ~0.2 dB either side). The gray-zone diagnostic is DONE: a declined promotion
  worth more than `PromotionNoteworthyGainDb` (0.2) logs which lobe was refused
  and why (past the reach vs under the margin), and the wide-seed lobe gate logs
  its own kept pick. Remaining: derive the threshold from comb statistics of the
  junction. (`MaxInterSideDirectPathMs` is gone — the cross-side work replaced it
  with the donor-corroborated geometry in #47.)
- [ ] ★ **`AutoAlignmentEngine.ComputeStereo` is 744 lines**, and it nests a
  **343-line local function** (`CrossSideTargetMs`) plus an 88-line `AlignRight`.
  The method has five clear phases (validate → left cascade → bridge fit → right
  cascade → rebalance/mono/normalize/polarity), but a local function that long
  closes over every local in the method, so the cross-side target logic cannot be
  tested on its own — the thing most worth testing after the #52 saga. Lift
  `CrossSideTargetMs` into a type carrying the state it needs. Do it on its own
  branch against the frozen validated session, not alongside other work.
- [ ] **Time Alignment analysis is not cached** — `RefreshAnalysis`
  (`TimeAlignmentPanelController`) recomputes Hilbert + GCC-PHAT on every tab
  show even when inputs are unchanged. Needs a live-app check to avoid stale
  display.
- [ ] **`VirtualCrossoverPanel` decomposition — residual boundaries.** The bulk
  is done: the UI-free runtime session model (`VirtualCrossoverChannel`/`State`),
  the source-loading pipeline (`ResolvedVirtualDspSource` + `TryAssignSource`),
  both OxyPlot presenters (`VirtualCrossoverAcousticPlot` / `DspChainPlot`), the
  metric computation (`VirtualCrossoverMetrics` + shared `ProcessedChannels`) and
  the shared Auto delay `AlignmentReprocessor` are extracted; the panel dropped
  ~4250 → ~3060 lines, and the Auto delay work since (#43–#50) has grown it back
  to ~3900 — the boundaries below are worth more than they were. Remaining,
  lower-value slices: a full source resolver/assignment
  boundary (the panel still orchestrates the file/History/
  restore flow around the shared core), splitting `VirtualCrossoverMetrics` into
  curve building vs side-processing orchestration, and moving `ProcessedChannel`'s
  `OxyColor` out into the render binding. Persistence, calibration and control
  binding are inherently UI-bound — leave them.
- [ ] **PDF images still go through temp files.** The shared `PdfSheet` helper
  centralised the temp-file dance, but MigraDoc 6 supports
  `AddImage("base64:...")`, which would remove it (needs a Windows render check
  that the sheets stay pixel-identical).

## Measurement orchestrators

- [ ] **Array input levels reach the audio layer and stop there.** The capture
  session reports a level per array channel and `AudioLevelResolver` fills them in
  (an absent one meters as −∞ dBFS, not as full scale), but `InputLevelMapping.Map`
  passes only the measurement microphone and the loopback to the UI, so a meter for
  the array positions is data that exists and is never shown. Not a correctness
  gap — nothing claims it is a safeguard, and a compromised position fails its run
  either way — but it is the one thing that would let a user see a bad position
  BEFORE spending four sweeps on it.
- [ ] **Nothing records where an array STOOD.** A set of measurements is judged
  compatible on what the arrays were made of — the number of positions and their
  calibrations — and that cannot see the case worth seeing: the same seven
  microphones, the same files, the rig lifted and set down somewhere else between
  two channels. Both measurements are then honest averages of different listening
  volumes, and every check passes. Raised in review with a persisted
  `ArrayLayoutId` as the cure, shared by a series and renewed when the rig moves.
  The obstacle is that nothing in the app can DERIVE it: moving the array does not
  change its configuration, so the id can only come from the user saying so, which
  makes this a new concept in the record settings and the file format rather than a
  check that was left out. File-format work, and it should carry the placement
  itself (where the rig was) rather than an opaque id, so a session opened later
  says something a human can read.
  Two things bound how much this is worth. It is NOT specific to arrays: a
  moving-microphone capture records no placement either, and never has, so the array
  inherits the gap rather than introducing it. And what the app can do without
  inventing a fact, it now does — every capture's measurement DATE is shown on the
  channel's average button and in the composition warning, because two captures from
  one sitting are one volume and two from different days may not be, and that date is
  the only evidence of it which exists.
- [ ] **ASIO device identity is the driver NAME and nothing else.** That is all ASIO
  exposes: there is no endpoint id, so the array's device stamp cannot be finer. For
  a vendor driver bound to its own interface the name is an identity; for a wrapper
  (ASIO4ALL, FlexASIO, a multi-device aggregate) it is not — the same name can front
  different hardware, and an array carried across that swap passes the stamp and
  points at inputs nobody chose, the very case the stamp exists to stop. Adding the
  driver's channel COUNT was considered and rejected: some drivers report different
  counts at different sample rates, so it would invalidate working setups to catch a
  case it would only sometimes catch. Wants either a probe that says something about
  the hardware behind the wrapper, or an explicit "this array belongs to this rig"
  the user confirms.
- [ ] ★ **An averaged measurement holds every run's raw capture in memory,
  and an array multiplies that by the number of positions.**
  `SweepAverageAccumulator` keeps a `TransferFunctionFrame` per microphone per
  accepted run, and a frame is a *view* over the recorded `float[]` rather than a
  reduction of it — so the whole capture of every array microphone, of the
  measurement microphone and of the loopback (shared within a run) stays live
  until the last run has been analysed. Retained ≈
  `(2 + microphones) × samples × 4` bytes per run: **0.28 GiB** at 96 kHz / 20 s /
  7 microphones / 4 runs, **1.46 GiB** at 48 kHz / 100 s / 8 microphones / 8 runs,
  0.02 GiB for a modest 48 kHz / 10 s / 3 microphones / 2 runs.
  **Streaming `Gxy/Gxx/Gyy` is NOT the fix for the memory** — that was this item's
  first answer and the arithmetic refutes it. A running accumulation is sized by the
  TRANSFORM, and the transform is the next power of two above twice the capture:
  4 194 304 bins for a 96 kHz / 20 s take, at 16 + 8 + 8 bytes a bin, is **134 MiB
  per microphone** and does not shrink with the run count. Eight of those is 1.07 GiB
  against today's 0.28, and on the 48 kHz / 100 s case it is ~4.8 GiB against 1.46.
  Streaming only wins past about **18 runs**, where `runs × capture × 4` finally
  overtakes `2.2 × capture × 32`. It IS the fix for the duplicated CPU — the per-run
  credibility verdict costs its own H1 and inverse transform, measured at **529 ms**
  per microphone per run at 96 kHz / 20 s (~4.2 s a run for seven positions plus the
  measurement microphone), and every one of those frames is transformed again for the
  final result. So the two halves of this item pull in opposite directions and want
  separate answers: bounded, run-count-independent state on one side, and not paying
  twice for the same transform on the other.
- [ ] **Run an averaged ASIO measurement on real hardware** (ideally a slow
  driver). Averaged sweeps keep one open ASIO session across runs; every software
  lifecycle guard around that — callback pools, capture epochs, in-flight block
  rejection, overflow recovery, terminal-failure surfacing, stop draining,
  detach-before-copy — is covered deterministically by the test suite (see the
  commits behind `AsioFullDuplexSession` / `AsioCapturePump`). What no test can
  reach is device/driver integration.
- [✗] **Sweep-run quality: unambiguous checks only — DECIDED, do not
  resurrect.** The statistical outlier layer (peak-delay vs median, IR
  correlation vs a reference run) and the run pre-alignment rework were rejected
  by the user (2026-07-11): the unambiguous checks (clipping / silent /
  undersized) plus one retry cover the real field failure mode. Cosmetic tails
  accepted with it: the stored raw samples are only the last run's; the Wave RMS
  meter integrates the lead-in/tail silence.

## Options panels

- [ ] **Loopback channel can still persist as `null` across a restart.** The
  in-session loss is fixed (a shadow field restores the choice when a stereo
  device is selected again), but applying while a mono/missing device is
  selected persists "None"; after a restart there is nothing to restore. Would
  need the preferred offset persisted separately from the effective one.
- [ ] **The ASIO driver probe still runs on the UI thread.** `GetDriverInfo` now
  reads the channels, the buffer figures and the supported rates in a single
  `AsioOut` open, but that open is a synchronous COM call that can take seconds
  and still blocks the UI while the measurement panel opens.
- [ ] **`TukeyWindowControlHelper` clamps are irreversible.** Shrinking the
  window length clamps the fade values (semantically required, visible in the
  controls), but growing it back does not restore them; a shadow-value restore
  like the loopback-channel one would make the clamp reversible. Deferred to a
  Windows session: control-value re-entrancy across three panels
  (FR/Waterfall/BurstDecay) needs a live render check.

## UI chrome

- [ ] **A disabled panel leaves its check boxes as the brightest thing on it.**
  `VirtualCrossoverPanel.SetProjectLoading` disables the whole panel while a
  session loads, and WinForms then paints the standard `CheckBox`/`RadioButton`
  glyphs itself: measured over the real panel, the label text drops
  227,227,227 → 140,149,168 and every fill stays within 2 of where it was, but
  the check box KEEPS its white box (255,255,255 → 240,240,240). Everything
  fades except a dozen white squares, and the channel cards — five check boxes
  each — carry most of them. Not a loading bug and not new: the white box is
  the theme's normal look, it is simply the only thing left bright once the
  text goes. `FlatStyle.Flat` fixes it (the box then follows `BackColor`:
  147,154,172 live, 55,60,72 disabled) and five controls in the app already use
  it — `GDOpt.checkAutoFit`, `PROpt.checkAutoFit`, `Form1.checkBox1`, the
  Q-convention dialog's radios — but the other 52 check boxes and 22 radios
  would change appearance everywhere, not just while loading. Owner looked at a
  rendered comparison on 2026-08-24 and chose to leave it; take it as its own
  change with its own visual pass, not as a rider on something else.
- [ ] **A light theme is wanted eventually, and 626 colour assignments do not go
  through `UiPalette`** — 487 `Color.FromArgb` literals across the 34
  `.Designer.cs` files plus 139 `SystemColors.ControlLight` label foregrounds.
  The contrast work (#116) named the roles the app paints with — `AccentFill`,
  `TextDisabled`, the `Graph*` chrome — and put the graph surface and the accent
  buttons' fill under the palette, so the SEAMS now exist:
  `PlotModelStyle.ApplyChrome` is the one place a plot's colours are decided,
  and `UiPaletteContrastTests` re-measures whatever values a second theme
  brings. What is left for the theme itself is the designer sweep, and it is
  smaller than the count suggests: only **37 distinct values** appear in those
  files and four of them cover 313 of the 487, so a dark→light map plus a
  runtime pass over the control tree covers most of it. The judgment part is
  not the chrome but the CURVES: `OxyColors.White` sums, white THD traces,
  light-grey source curves and the user's own overlay slot colours (persisted
  as `ColorArgb` in overlay files, so they cannot be rewritten) all need a
  second palette or a luminance-adaptive fallback before a light plot is
  readable. Do not start this as a colour swap; start it as a curve-palette
  design. Half the palette also still carries PHYSICAL names (`AccentBlueSoft`,
  `TextSecondaryAlt`, `SuccessGreenSoft`): rename them to their roles as they
  are touched rather than in one sweep — `AccentBlueSoft` is the accent MARK
  (links, focus borders, selection markers), which is the one that matters.
- [ ] **Do not let the shell re-assign a docked panel's `Padding`.** Both tool
  panels declare `Padding = new Padding(6)` in their OWN designer, where it scales
  with the rest of the arrangement; `Form1.Designer.cs` used to set the same
  literal on them a second time, after the panel had already scaled its own to 12
  at 192 DPI. The raw 6 won, and every anchored control — the channel column, the
  buttons under it — landed 6 px off, since anchoring places them against the
  padded rectangle (#120). Those two lines are gone, but the WinForms designer
  will happily write them back if someone edits `Form1` in it: if a
  `<panel>.Padding = new Padding(6)` line reappears in `Form1.Designer.cs`, that is
  this defect returning. `ItsPadding_ScalesWithTheArrangement` in both layout
  suites pins the panel's own half of it; nothing can pin the shell's.

- [ ] **The app paints plots on FOUR different surfaces, three of them designer
  literals.** `UiPalette.GraphSurface` (50,55,100) covers the main plot and the
  EQ wizard; Virtual DSP's two views sit on (40,44,80)
  (`VirtualCrossoverPanel.Designer.cs`), the option/Time Alignment/history
  previews on (32,36,46), and the target preview on (55,58,65). Whether that is
  intentional or drift, nobody decided it recently — and it is not cosmetic:
  a FIXED chrome colour reads at a different strength on each (the first grid
  measured 1.33:1 on the main plots and 1.59:1 on Virtual DSP, which is how it
  looked). The grid and the plot border are white-with-alpha now, so they no
  longer care; anything else added to a plot has to make the same choice, and a
  light theme has to reach all four surfaces. Decide the count first: one
  surface token, or a named few.
- [ ] **The satellite plots still carry their own chrome literals.** The Time
  Alignment previews, `AngleCalibrationDialog`, `ImpulseWindowPreview`,
  `OverlayTargetSettingsDialog` and `MeasurementHistoryWindow` set their own
  white text and grid colours rather than going through
  `PlotModelStyle.ApplyChrome`. They are readable as they are, so this is
  tidiness, not a defect — but they are the reason a plot colour still has more
  than one home.
- [ ] **`WarningRed` on a dark surface measures 4.3:1 as text.** It is a FILL
  today (meter bars, the fader groove), where no text threshold applies, so
  nothing is wrong now — but it reads as the palette's "red" and the next
  status line that reaches for it would land under the floor. `ErrorSoft`
  (already lifted to 4.6:1) is the text-carrying red; keep them apart, or give
  `WarningRed` a text-safe sibling if it is ever needed for one.
- [ ] **`ChromeTitleBar` caches the DPI scale once at `Initialize`.** No
  `DpiChanged` handling: moving the window to a monitor with different DPI
  (PerMonitorV2) leaves the bar height, button widths and tab layout at the old
  scale. Refresh the cached metrics and re-run layout on DPI change.
- [ ] ★ **The app is `HighDpiMode.SystemAware`; a mixed-DPI desktop wants
  `PerMonitorV2`.** The generated `ApplicationConfiguration.Initialize` asks for
  `SystemAware`, so the process takes the PRIMARY monitor's DPI once at startup
  and never re-scales: dragging the window to a monitor at a different scale
  leaves Windows stretching the bitmap and the text goes soft. Layout does not
  break (nothing re-lays-out), so this is a sharpness problem, not a clipping
  one — the clipping half was the `AutoScaleMode.Font` → `Dpi` switch, already
  done. Switching means every control that CACHES a `DeviceDpi`-derived layout
  must refresh on `DpiChanged` / `OnDpiChangedAfterParent`: `ChromeTitleBar` (the
  item above is the blocker), the `Dark*` inner-layout controls
  (`DarkNumericUpDown`, `DarkComboBox` already handle `OnHandleCreated` /
  `OnDpiChangedAfterParent`, so they may be ready) and `VirtualCrossoverPanel`'s
  layout baseline (it scales in `ScaleControl`, which a DPI move does call —
  worth a live check rather than an assumption). Needs a two-monitor desktop at
  different scales to verify; the owner has one (left 125%, right 100%, as of
  2026-08-23).

## Audio capture layer

- [ ] **ASIO converts channels `0..offset+count` instead of a window from
  `InputChannelOffset`** (`AsioFullDuplexSession`): a mic on input 7 converts all
  8 channels per callback. Possibly a NAudio `SetChannelOffset` workaround —
  needs hardware to verify.

## Overlays

- [ ] ★ **`Overlay` is a God object** — ~2230 lines, ~95 members in one class:
  runtime control creation, the capture menu and its long-press behaviour, text
  import/export, three settings dialogs, persistence, preview/restore and the
  plot series. The render-path caching and the pure-math extraction are done;
  what remains is a real split (capture-menu behaviour, text import/export and
  the dialog orchestration are each separable without touching the draw path).
  Bigger than one sitting — it wants its own branch.
- [ ] **Introduce an `OverlaySlotState` record** to replace the triple
  field-mapping between overlay, slot file and UI state (the render-path caching
  and the pure-math extraction from `Overlay.cs` are done; this structural half
  remains).
- [ ] **Overlay curves are assumed sorted/unique/finite in X**
  (`CalculateOperation`'s forward-only cursor): normalize imported overlays once
  (drop non-finite, sort, merge duplicate frequencies).

## Plotting

- [ ] **The graph limits dialog and the on-graph zoom buttons see one vertical
  axis per plot.** `PlotAxisZoom.FindZoomableAxis` returns the first visible,
  zoomable axis of an orientation, which is all any analysis plot has — except
  the Virtual DSP correlation view, which carries `corr-r` on the left and
  `corr-score` on the right. The wheel still zooms the right-hand axis (hovering
  it routes the gesture there), but the buttons and the dialog only ever reach
  the left one. Fixing it means letting both surfaces enumerate the vertical
  axes rather than picking one, and deciding what a "Top/Bottom" pair means with
  two of them.

- [ ] ★ **Snapshot read-model instead of the two live measurement objects in
  `PlotModelFactory`.** The factory and `MeasurementPlotContext` are constructed
  with `ExpSweepMeasurement` and `NoiseMeasurement` themselves and read 22
  members between them, two of which (`InProgress`, `CurrentLevels`) mutate
  during capture — so the replacement must be a read-model interface re-read per
  plot build, NOT a value snapshot taken at construction, or Live Spectrum and
  the in-progress guards change behaviour. This was the extraction audit's
  top finding (2026-07-26): it is what a "plotting layer" split was really
  after, and it needs no new project.
  The reason it is not done yet is honest scope: the payoff — plot tests no
  longer needing `FakeAudioSessionFactory` — only lands if the ~1030-line
  `PlotModelFactoryTests` is rewritten too, because it builds state through
  `measurement.RestoreImpulseResponse(...)` and therefore needs a live
  measurement regardless of what the factory accepts. Interface + adapter
  without that rewrite is pure addition. Do it as one piece, its own branch.
  (The 13-argument constructor half is DONE — `PlotPresentationOptions`.)
- [ ] **`LogarithmicClipAxis` label trim.** Edge tick labels can be trimmed at
  the plot boundary. Purely visual; needs a Windows render to reproduce.
- [ ] **Waterfall renders nothing silently below 8 slices** (`RawSlices.Count <
  8` guard in `WaterfallSeries.Render`): corrupted settings or narrow ranges show
  an empty plot with no explanation. Show a message (or clamp the controls).
- [ ] **Wavelet time-support validity is not tracked**: at low frequencies the
  Morlet kernel outlasts the analysis window and the envelope is window-shaped.
  `Slice.SliceMinValidFrequency` exists but always receives 0 — compute the
  frequency below which the kernel's support exceeds the window and mark/limit
  slices there.

## Shell

- [ ] **`WireLiveApply` covers only dialog-open controls.** Controls created
  after wiring never get live-apply behavior. Deferred to a Windows session: the
  fix hooks `ControlAdded` recursively and re-enters the apply debounce, so it
  needs a live check that dynamically-added rows apply exactly once.

## EQ Wizard (car DSP tuning)

This mode does magnitude correction toward a car target AFTER the Virtual DSP
tool has set crossovers, delays and polarity — so crossovers, phase/time and
convolution are deliberately out of its scope (see the note at the end). The
items below are what a car DSP tune actually needs, roughly in priority order.

- [ ] **The boostability mask has no notion of a driver band.** The mask itself
  is in (`EqBoostabilityMask`: boosts refused in low-coherence bins and narrow
  deep nulls, cuts always allowed, Auto Tune cuts-only by default), but the
  "driver band" it works inside is just the user's From/To window. Derive each
  driver's usable band from the measured roll-off or the crossover so the mask
  also blocks boosts outside it.
- [ ] ★ **Auto Tune still fits peaking bands only.** The bank, the preview and
  the parsers carry shelves and all-pass bands now; the tuner does not. Car
  targets are shelved (bass boost + downward tilt), which a stack of peaking
  bands approximates poorly — wasted slots and ringing. Teach the greedy fit to
  propose a low/high shelf where the residual is a slope rather than a bump.
  (All-pass stays out of the FIT — it is flat, so the magnitude error can never
  ask for one; a bank holding all-pass bands is instead offered to be kept, and
  their count comes off the fit's budget.) HP/LP/notch are NOT needed here: the
  Virtual DSP tool owns crossovers and time alignment.
- [ ] **Spatial averaging of several measurements.** A single mic point
  over-corrects for that seat's position-specific nulls; car tuning averages a
  handful of positions around the headrest. The mode loads ONE IR — add
  multi-measurement (moving-mic / N-position) averaging before the fit, working
  together with the reliability mask above.
- [ ] **Source from History / the Virtual DSP channels, not just a file.** Car
  audio is multi-channel and the tune iterates "measure a channel → EQ it". The
  mode only loads a saved `.json` (deliberately decoupled per AGENTS.md); a
  History / per-channel source picker would remove the round-trip and enable
  per-channel EQ of the just-measured driver.
- [ ] **Greedy fit redesign.** Band spacing ignores the chosen Q (fixed ±0.33/±1
  oct blocks); band gain is fixed from the peak residual before Q is searched; the
  preamp is rounded to integer dB *before* the fit; and the objective treats
  boosts and cuts symmetrically. Fold into one redesign: frequency × Q × gain
  search with width-based spacing and a boost-penalized score, then a
  coordinate-descent polish over all bands + preamp (borrow the one in
  `CrossoverAutoSetup`).
- [ ] **Device export needs a target-device profile.** The export sample rate is
  a constructor parameter now, but device biquad limits are not checked and the
  preamp burns a biquad slot instead of mapping to the device's gain control. Car
  DSPs (Helix / Audison / miniDSP) have a fixed per-channel band budget and a
  separate master gain the profile must respect. Residual after the
  Audiotec-Fischer bank format: that one format enforces its 30-slot budget
  (export refuses a longer curve, and an import that is not a complete 30-slot
  table is refused rather than read as an empty bank), never writes the preamp
  (`IEqProfileFormat.CarriesPreamp` is false) and the wizard now warns about the
  gain left behind, naming the dB to enter on the device — but the miniDSP /
  generic paths still have no budget or gain profile, and the warning is
  per-format metadata, not a device profile.

Deliberately out of scope for car DSP tuning (do not add here): FIR/convolution
export (car DSPs are biquad), the DECOMPOSED phase views — minimum phase, excess
phase and group delay, which are the analysis tabs' subject — real-time PC audio
preview (you listen in the car after loading the profile), arbitrary target-curve
import (the Car / CarMild / XCurve presets cover it), and HP/LP filter types
(crossover tool). The wizard's Phase mode is not one of these and is IN scope: it
draws the measured phase of the channel being tuned against the neighbours it was
handed, which is the only way to see what an all-pass band did — a magnitude plot
shows it as flat by construction.

## Time Alignment / unwrap

- [ ] **GCC-PHAT confidence is peak height, not uniqueness**: a single spectral
  line or a narrowband subwoofer reads ~100% while the delay is poorly
  conditioned. Fix: fold RMS bandwidth / peak curvature / peak-to-second-peak
  into the confidence, or rename the figure. Needs a validation pass on real
  measurements. (Related to the flagship sub group-delay-by-frequency work in the
  memory follow-ups.)
- [ ] **Display smoothing includes low-reliability bins** (unwrap blanks long
  garbage stretches, but short noisy nulls still enter `SmoothLinear` at full
  weight; magnitude curves behave the same). Optional: reliability-weighted
  smoothing.

## Live Spectrum / coherence

- [✗] **RTA tone level is only accurate with a Flat Top window — RESOLVED for
  the general case; the periodic-pink residual is conditional.**
  Flat Top is a selectable Live Spectrum window and reads a tone at its true,
  FFT-length-independent amplitude. Validated against the SPL calibrator on white
  noise + Flat Top + smoothing OFF: the RTA read the 94 dB tone at −12.63 dB, the
  flat-top calibration at −13 dBFS — 0.37 dB agreement, confirming the calibration
  and the RTA are consistent end-to-end. (Two gotchas seen while validating, both
  expected: a rectangular window scallops an off-bin tone ~2.4 dB low; and smoothing
  dilutes a pure-tone spike more at finer resolution — 1024→−14.8, 2048→−19.6,
  4096→−25.2 dB — so smoothing must be OFF to read a tone level.)
  Residual: periodic pink pins the window to rectangular (leakage-free and correct
  for the transfer function), and the RTA shares that windowed FFT, so it cannot use
  Flat Top in that mode. If Live Spectrum ever gets its own dB SPL scale (as
  Frequency Response now has) AND periodic-pink tone accuracy is wanted, decouple the
  RTA window from the transfer: a separate flat-top mic FFT for the input magnitude
  (computed only when the RTA is shown), leaving the transfer/coherence on
  rectangular. Real swept measurements are unaffected either way (the deconvolved
  transfer has no single-tone scalloping).
- [ ] **EMA coherence has no effective average count** (overlap-correlated
  frames, alpha-dependent memory): expose K_eff ≈ (2−α)/α (reduced for overlap)
  alongside the curve and feed it to the same debias the sweep path uses.
- [ ] **First live plot frame is still heavy on the UI thread** (snapshot clones
  + first resample + OxyPlot series/capacity growth). Hidden RTA computation is
  now skipped; profile whether pre-building series before playback starts is
  worthwhile.

## History

- [ ] **An unreachable entry cannot be forgotten.** Entries whose measurement
  file is missing are hidden from `MeasurementHistoryService` and written back
  on every save, which is what stops an unmounted drive from truncating the
  history. The cost is that a file the user deleted or moved away for good stays
  in the JSON forever, warns on every launch, and cannot be removed through the
  UI — `Delete` only sees the visible list. Options: an action on the warning
  ("Forget missing entries"), or listing them disabled but deletable. Not
  urgent; the alternative was losing them silently.
- [ ] **History entries reference LIVE overlay slots** (`ActiveOverlaySlots`
  numbers into mutable global storage): restoring an old session shows whatever
  the slots hold TODAY. Store immutable overlay snapshots (content-addressed
  revisions) in the history entry.

## Signal Generator / files / calibration / release

- [ ] **Signal Generator materializes whole signals in memory** (mono array +
  full playback copy; ASIO always a stereo float copy): 600 s at 192 kHz is
  ~1.3 GiB. Needs a streaming IWaveProvider generating blocks.
- [ ] **Autocorrelation windows are sample-count-fixed** (offset 64, length 2048,
  3 ms display): the physical window shrinks 4× at 192 kHz and the promised 3 ms
  does not exist at 768 kHz. Parametrize in milliseconds. (The /correlation[0]
  normalization is the standard biased estimator — fine for display.)
- [ ] **Measurement files validate only after full deserialization**: a file
  declaring hundreds of millions of samples hits OOM before `Validate()` runs.
  Not a security hole (the user opens files from their own disk; the worst case
  is a crash), but a truncated or corrupted `.json` — or one shared between users
  now that measurements travel — should fail with a message. Add a file-size cap
  before parsing, a max-samples cap, and `OutOfMemoryException` handling.
- [ ] **Uninstaller leaves settings behind.** Keeping them across a reinstall is
  the defensible default, so the closing move is a line in the docs saying where
  they live; an opt-in "remove my settings" checkbox only if it is free.
- [ ] **Release toolchain is unpinned** (`choco install innosetup`, latest
  NetSparkle appcast tool, actions by major tag): pin exact versions (and SHAs
  for actions) once the current-good versions are confirmed. (The shell-injection
  surface, branch-vs-tag build mismatch and auto-published AI notes are fixed.)
