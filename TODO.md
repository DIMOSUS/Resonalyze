# TODO — tech debt from the code reviews

Open items found during the review passes that were deliberately reported
instead of fixed. Completed items are removed (they live in git history), and an
item describing work already done keeps only the residual. Grouped by area,
highest-value items marked ★. `[✗]` marks a settled decision kept on purpose, so
the same idea does not get re-proposed — those are not open work.

Last audited against the code on 2026-08-29 — the first pass since the
moving-microphone, microphone-array, calibration-home and audition work landed
(#124, #126, #128, #130, #135, #137, #138, #139).

Two EQ Wizard items closed as DONE and were removed: spatial averaging before
the fit (the wizard takes a spatially averaged capture as a source kind of its
own and gates its boosts on the array's per-band spread,
`ArraySpreadBoostLimitDb`), and sourcing from History (an "Impulse response from
history" menu item; the per-channel half of that same item is the Virtual DSP
handoff). Everything else was re-checked against the code and stands.

What HAD drifted is the figures, and they are corrected in place. TWO of the
three structural items nearly doubled while they sat still, which raises the
price of the split rather than lowering it: `VirtualCrossoverPanel.cs` was 7061
lines against the ~3900 last recorded (split since; see its item below) and
`PlotModelFactoryTests` 2560 against ~1035. The third moved
far less: the `Overlay` CLASS is 2541 lines against ~2230. This audit first gave
it 3219, which is the FILE, `OverlayCollection` and two small types included —
measure the class when the item is about splitting a class, and say which when
the two differ by a quarter. `AutoAlignmentEngine.ComputeStereo` grew 744 → 759
lines and its nested local 343 → 348.

An earlier pass dropped the "Windows live-checks pending" section: only the
person with the car and the microphone can close those, so they belong in the
next field session rather than in a register nobody else can tick.

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

- [ ] **Parallel driver groups ("Subwoofer group 1..4")** — two drivers covering
  the SAME band from different places in the car, sharing one crossover but
  free to take their own delay, gain, PEQ and polarity. The owner has no such
  car yet (2026-09-17); the SERIES case — an infra-bass under a subwoofer,
  handing over in frequency — is already handled and is not this. Three things
  the design has to answer, worked out before it was shelved:
  - The settings cut is already proven on another axis. `VirtualCrossoverSideLock`
    links exactly crossover + polarity + FIR across L/R and deliberately leaves
    gain, delay and PEQ free, and `Copy side…` does the same once with a scope.
    A group is the standing version of that on a second, named axis.
  - **Polarity must NOT be linked**, which is where a group differs from the side
    lock. The wizard derives polarity from the crossover, so "one crossover"
    drags it along — but the whole point of a separate delay is that the members
    stand at different distances, and at different distances the right polarity
    can differ. L and R are symmetric; two subwoofers in one car are not.
  - The real cost is not the settings link, it is that **everything is a chain**:
    `CrossoverAutoSetup.Propose` states it in its contract ("channel i hands over
    to i+1 only"), and the junction rows, Sum loss, the phase read-out and Auto
    delay's seeds all stand on it. Members of a parallel group have no junction
    with each other and share one junction with the driver above. The group has
    to reach the chain as ONE member. The wizard half of that is cheap and
    principled — a group's acoustic contribution is the sum of its members'
    curves, and the wizard already works on curves — the read-out half is a
    re-labelling of a lot of places that say "channel" today.
- [ ] **Auto delay has no criterion for two radiators covering one band.** It
  leads every channel from its junction with a neighbour, and parallel members
  have no junction between them. Aligning both to the same reference makes them
  coherent only where that reference plays: below the crossover, which is the
  band two subwoofers are there for, nothing holds them together. This needs its
  own criterion (maximum mutual sum in the shared band) and its own battery, and
  must NOT be bundled with the grouping above — the grouping has nothing to
  calibrate and this has a great deal.
- [ ] **`CrossoverJunctionTuner` is not reachable from the panel.** The measured
  junction refinement exists and is exercised by the AI assistant only
  (MANUAL.md). It belongs in the PANEL on a finished tune rather than in the
  wizard — the tuner needs the current chains, which the wizard has not built
  yet — so it is a new dialog plus a re-layout of an already packed button row.
  Split corners are not searched inside the tuner either; they exist in the
  wizard only.

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
- [ ] ★ **Two direct-sound witnesses read one curve.** The direct-coherence witness
  (tie within `DirectCoherenceTieMarginDb` 0.3, flip partner only) and
  `DirectLobeWitness` (final pick, a period either way, a probe outside the
  window) both read the whitened direct-sound correlation, each with its own
  thresholds (0.3 / 0.25 / 0.10, `MinimumR` 0.6 shared). Fold them into one
  witness with one window (a period). Measure on the session battery and on the
  v6 session-12 / session-13 anchors; the v6 200 Hz split is the case the second
  witness exists for.
- [ ] **Fine-search window that holds both flip partners.** On the v6 200 Hz
  split the right lobe sat 0.08 ms past the fine window's edge (−8.193 at
  −8.17) and only the lobe witness's probe reached it. A window of half a period
  plus a quarter either side of the anchor would hold a lobe and its
  half-period-plus-inversion twin whole, and may retire the probe.
- [ ] **Stereo-branch thresholds sit on the data's edge.** `FarGainDb` 0.30: the
  v3 90 Hz branch is adopted at exactly 0.30 dB after quantization (and the panel
  reads that side 0.11 dB better WITHOUT it). `ReferenceLossDb` 0.10: the v6
  reference side reads +0.02. Hundredths decide; give the rule hysteresis or
  derive it from the comb statistics of the junction.
- [ ] **The pair and mono co-moves round after they score.** Both scan from an
  off-grid bound in 0.02 ms steps and round the winner to the DSP's 0.01 ms grid
  afterwards, so the delay scored is up to 0.005 ms from the one written (about
  4° at 2.5 kHz). The far-side polish was moved to absolute ticks in #197
  (`PolishFarSideJunctions_ScoresTheExactMoveToADspTickFromAnOffGridDelay`); give
  the co-moves the same walk.
- [ ] **The engine's junction sum and the panel's are not the same read.**
  `JunctionSum` and `VirtualCrossoverAnalysis.MeasureJunctionSpectrum` share
  `BuildAlignmentBins`, but the panel (and the battery that judges the engine)
  measures under the panel's gate, the engine under the alignment gate. "The
  physical sum the panel reads" is therefore approximate: on v3 the right mono
  co-move lands at +0.77 ms where the panel prefers +1.13. Either score the
  post-descent passes under the panel's gate or state the difference where the
  docs claim equality.
- [ ] **The v6 right 200 Hz split is still 0.20 ms off the owner's tune** (7.83
  against 7.63) after the polish/mono rounds closed it from 0.44: the polish
  refuses the last +0.04 ms for the sub junction's 70-140 Hz half, 0.02 dB
  against a 0.01 dB gain. A question for the trim veto's scale at gains this
  small, not for the pass order.
- [ ] **Saved tunes the engine disagrees with by more than a lobe, unexplained.**
  v2 left: the mid at 11.2 ms against a saved 5.41 (5.8 ms); v4: the 750 Hz split's
  polarity against the saved one, a tie by the metric. Nobody has measured which
  is right; until then those rows are noise in the battery's totals.
- [ ] **No synthetic for the trim veto.** The half-band veto in the far-side polish
  and in the pair co-move is proven in the field only (the Passat's right 250 Hz
  split). The mono hop has `ComoveMonoChannels_SubBandInconsistentHop_IsVetoed`;
  the trims need a fixture where a trim wins the full band and loses a half by
  more than it gains.
- [ ] **`FirConstructorTests.AHandoffOfAnImportedKernel…` failed once in CI**
  (#195) and passes locally 3/3; unexplained. Look for shared state or a timing
  assumption before it costs a release.
- [✗] **One half-band veto rule for every post-descent move** — measured in #197 and
  declined both ways. "No cell may lose more than the move gains" adopts the
  impostor the mono veto's synthetic exists for; the mono hop's flat 0.1 dB margin
  on the stereo branch refuses the v6 200 Hz lobe the owner tuned (0.26 dB on one
  half for 0.53 on the far side). One helper, one rule per kind of move.
- [✗] **Re-rendering every co-move pick** — measured in #197: the rotation scan and
  the re-render agreed within 0.01 dB on every adopted move of the archive and no
  stereo row changed. Only the stereo branch keeps its re-render (a stack moving
  half a period and flipping, where the two disagreed by 2.8 dB).
- [✗] **Removing `LowJunctionPolarity`** — measured in #197: one session changes
  (v3), whose sub drops to the inverted twin for 0.03-0.13 dB on its two junctions.
  A sub inverted against its woofer on a tie is what the owner asked to avoid.
- [✗] **An arrival marker on the correlation view** — the search's re-anchored read
  cost ~270 ms of a ~320 ms build at 96 kHz and the owner found it uninformative
  (#197). A marker for "where the sum and the direct sound agreed" would be a new
  feature, not this one back.
- [ ] ★ **`AutoAlignmentEngine.ComputeStereo` is 759 lines**, and it nests a
  **348-line local function** (`CrossSideTargetMs`) plus an 88-line `AlignRight`.
  The method has five clear phases (validate → left cascade → bridge fit → right
  cascade → rebalance/mono/normalize/polarity), but a local function that long
  closes over every local in the method, so the cross-side target logic cannot be
  tested on its own — the thing most worth testing after the #52 saga. Lift
  `CrossSideTargetMs` into a type carrying the state it needs. Do it on its own
  branch against the frozen validated session, not alongside other work.
- [ ] **Time Alignment analysis is not cached** — `RefreshAnalysis`
  (`TimeAlignmentPanelController`) recomputes Hilbert + GCC-PHAT on every tab
  show even when inputs are unchanged. It no longer reads while hidden, and a
  tab switch reads once, not twice, but every show still reads. Needs a
  live-app check to avoid stale display.
- [ ] **Virtual DSP — residual boundaries.** The tune lives in a UI-free
  `VirtualCrossoverSession` and whatever reads it takes the session
  (docs/tech/virtual-dsp-panel.md#code-map); the panel is binding code in
  partials named for what they bind, the largest the Agent Bridge's import flow
  (~1000 lines) and the blocks (~600). Remaining, lower-value slices: the EQ
  Wizard handoff request (`BuildPeqHandoffRequest`, `CapturePhaseContext`,
  `HandoffSpatialAverage`) still reads the last render and the target level off
  the panel, so the AI import's Auto-tune runs in the panel too; a full source
  resolver/assignment boundary (the panel still orchestrates the file/History/
  restore flow around the shared core); splitting `VirtualCrossoverMetrics` into
  curve building vs side-processing orchestration; and moving `ProcessedChannel`'s
  `OxyColor` out into the render binding. Persistence, calibration and control
  binding are inherently UI-bound — leave them.
- [ ] ★ **The Virtual DSP autosave trusts whoever built the panel.** `ScheduleSave`
  and `FlushProject` have no guard and `Dispose` flushes, so any host that builds
  a panel it never shows replaces the stored session with the panel's empty
  three-block project. Until #203 the App test host did exactly that on every run,
  which is why sessions kept resetting to default; it now runs portable, but a
  scratchpad harness without `portable.flag` still would. Only two call sites
  check `initialized` (`ReconcileCalibrationSelection`, `StoreTargetInProject`).
  Guard the save itself: write only once `OnPanelShown` has started the stored
  load. Nothing real saves earlier: a dropped session file shows the tool before
  importing (`Form1.OpenDroppedFileAsync`), and Load lives on the panel. Pin it
  with a test that builds, edits and disposes a panel that was never shown.
- [ ] **The audition's "Own (as measured)" refuses more than the render needs.**
  A car whose two SIDES were measured through different microphones is refused
  along with one whose own channels disagree, though only the second is
  impossible: the ears have their own kernels, so left could carry microphone A's
  correction and right microphone B's. What blocks it is that the render designs
  ONE calibration FIR and convolves both kernels with it — and it is combined
  with the cabin subtraction into that single filter, with a reference pair built
  from it for the level match, so splitting it per side touches three places in
  `VirtualCrossoverAuditionDialog.ExecuteRender` rather than one. Raised in the
  review of #139 and deliberately left: a refusal is the safe side of it, and the
  message and the docs now name the real limit instead of claiming it is per
  side. A side whose OWN channels were measured through different microphones
  stays refused whatever happens here — they are summed before the filter meets
  them.
- [ ] **PDF images still go through temp files.** The shared
  `Tools/Sheets/PdfSheet` helper centralised the temp-file dance, but MigraDoc 6
  supports `AddImage("base64:...")`, which would remove it (needs a Windows
  render check
  that the sheets stay pixel-identical).

## Measurement orchestrators

- [✗] **A bad run stops the measurement, transient stream faults included —
  DECIDED, do not resurrect.** There used to be one automatic retry per rejected
  run; it was removed in this PR because the field answer is that it never
  recovered anything — what the checks catch is a gain set wrong, a cable in the
  wrong socket, a channel that is not there, all of which the next sweep
  reproduces exactly. Review then pointed out, correctly, that the same list also
  carries `CaptureDiscontinuity`, `CaptureTimestampError` and `RenderUnderrun`,
  which are transient by nature: one WASAPI underrun on the last of four runs
  now costs the three good ones. The owner weighed that and kept it (2026-08-28):
  losing a measurement and re-running it beats publishing an average nobody can
  vouch for, and splitting the list into fatal and retryable buys back machinery
  for a fault that is rare. Nothing is published either way — an average of three
  runs where four were asked for is a different measurement wearing the same name.

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
  overtakes `2.2 × capture × 32`. It IS the fix for the duplicated CPU — every frame the
  per-run credibility verdict transforms is transformed again for the final result —
  but that half is now half-solved without it: the per-run verdict transforms the
  loopback ONCE for all of a run's microphones instead of once each, measured at
  3993 → 2093 ms for eight channels of a 96 kHz / 20 s take. What remains is the
  second pass at the end, and buying that back costs the memory above. So the two
  halves of this item pull in opposite directions and want separate answers: bounded,
  run-count-independent state on one side, and not paying twice on the other.
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

- [ ] **Five dark curves sit under the 3:1 floor on the plot surface.** The
  light theme is measured at 3:1 by `UiPaletteContrastTests`; the dark curve set
  predates that floor and is held where the owner tuned it, so the same test
  uses 1.55:1 there — only a regression guard. The weakest are `CurveHarmonic3`
  (1.58), `CurveHarmonic4` (1.70), `CurveArrayMicrophone` (2.52), `CurveMuted`
  (2.85) and `CurveLiveTransfer` (2.98) against `GraphSurface`. Lifting them is a
  visual pass on the dark theme with the owner, not a rider on other work.
- [ ] **`Danger` on a dark surface measures 4.3:1 as text.** It is a FILL today
  (meter bars, the fader groove), where no text threshold applies, so nothing is
  wrong now — but it reads as the palette's "red" and the next status line that
  reaches for it would land under the floor. `Error` (4.6:1) is the
  text-carrying red; keep them apart, or give `Danger` a text-safe sibling if it
  is ever needed for one.
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
- [✗] **Dark controls allocate `Pen`/`SolidBrush` on every paint — DEFERRED
  until a profile shows it** (decided 2026-07-22; moved here 2026-08-31 from
  the retired HANDOFF.md so it is not rediscovered as an oversight).
  `DarkComboBox.OnPaint` makes ten per repaint (`GainFader` and
  `DarkNumericUpDown` six each), every one in a `using`, so nothing leaks —
  the cost is pure allocation churn. Caching them per control is easy but
  drags in palette/DPI invalidation, and nobody has ever profiled a repaint
  that shows up: Tracy first, and only then the cache.

## Audio capture layer

- [ ] **ASIO converts channels `0..offset+count` instead of a window from
  `InputChannelOffset`** (`AsioFullDuplexSession`): a mic on input 7 converts all
  8 channels per callback. Possibly a NAudio `SetChannelOffset` workaround —
  needs hardware to verify.

## Overlays

- [ ] ★ **`Overlay` is a God object** — 2541 lines in one class (line 669 to
  3209; the file is 3219, with `OverlayCollection` and two small types beside it):
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

- [ ] **Two settings are still read off the sweep engine.** `PlotModelFactory`
  takes the rate a plot has when nothing is open from `ExpSweepMeasurement`, and
  `LiveSpectrumSession` takes its SPL anchor from it (a provider the form passes).
  Both are measurement settings. The rate on the engine lags an edit until the
  next run pushes the settings, and the anchor is copied onto it by hand in three
  places (`PersistCalibration`, the settings file's `ApplyTo`,
  `MeasurementOptions.SetOptions`). Reading the settings would leave one owner,
  but it moves an empty plot's rate to the edited value at once: decide that
  before changing it. Neither factory reads the live analyzer any more
  (`LiveCaptureSetup`, docs/tech/live-spectrum.md#code-map).
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
- [ ] **The imported target trusts the file to be a magnitude.**
  `TargetCurveImport` refuses only the two roles that are differences
  (`Deviation`, `EqCorrection`); it never looks at `AnalysisCurveKind`, so a
  phase or group-delay export loads as a target and its degrees or milliseconds
  are read as dB. Borrowing `EqWizardSourceResolver.IsEqualizableResponse` would
  close it for our OWN labelled exports and nothing more: a file from another
  tool declares no kind at all, and `null` is permitted on purpose, so a
  spreadsheet column of group delay still passes. `Primary` alone does not say
  "magnitude" either. The real fix is in what the text header can state, not in
  a pair of `if`s at the import — which is why this is a note rather than a
  patch (raised in the #143 review).
- [ ] **Decide whether the shelf stage should default on.** Auto Tune fits
  low/high shelves now (`EqAutoTuner.Options.AllowShelves`, the wizard's
  **Shelves** box), gated on finishing the whole fit both ways and landing
  closer with the shelf than without. It is OFF by default because turning it on
  changes the curve a fit returns, and the evidence so far is one-sided but
  synthetic: a uniformly hot top end took one shelf where four bells had been
  spent, at a third of the residual (0.04 dB RMS against 0.13); a car target with
  bass lift and tilt came out at 0.34 dB RMS against 1.28 with the same ten
  filters, at the cost of a 10.48 dB peak boost against 6.79 (a shelf is not
  counted against the bells' cumulative boost ceiling — deliberate, and the
  Headroom read-out is where it shows). Flip the default
  once it has been run against real cabin measurements — there is no code left to
  write for it, only the field check. (All-pass stays out of the FIT — it is
  flat, so the magnitude error can never ask for one; a bank holding all-pass
  bands is instead offered to be kept, and their count comes off the fit's
  budget. HP/LP/notch are NOT needed here: the Virtual DSP tool owns crossovers
  and time alignment.)
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
preview (you listen in the car after loading the profile), and HP/LP filter types
(crossover tool). Arbitrary target-curve import used to be on this list, on the
grounds that the Car / CarMild / XCurve presets cover it; it is now shipped
(**Target Curve… → Import from file…**), because a tuner who already has a house
curve of their own has nothing to gain from a preset that resembles it. The wizard's Phase mode is not one of these and is IN scope: it
draws the measured phase of the channel being tuned against the neighbours it was
handed, which is the only way to see what an all-pass band did — a magnitude plot
shows it as flat by construction.

## Time Alignment / unwrap

- [ ] **`TimeAlignmentPanelController` holds its rules as `internal static`
  members** (ten of them, 1,856 lines): band detection (`TryDetectDominantBand`,
  `SharedBand`), the onset (`GetEnergyOnsetIndex`), the recommendation
  (`RecommendedRow`, `IsArrivalRecommendable`, `RowLabel`) and plot markers. They
  are static only so tests can reach them; move the rules to a type of their own
  (AGENTS.md › Where logic lives) and leave the controller the binding.
- [ ] ★ **The panel reads the WHOLE record to answer a question about its first
  80 ms.** A transfer IR is `NextPow2(2 x capture)` — a 2.2 s sweep at 96 kHz
  reads a 10.9 s buffer — and every transform is sized by it, so one read costs
  465 ms where a window around the arrival costs about 90 (measured 2026-08-31
  on the field records `tw center.json` and `l mid.json`, after the
  one-transform pass; a 65536-sample cut at 96 kHz places the arrival within
  0.1 us of the full read). Deliberately NOT taken with that pass: it moves
  two numbers, and both need a decision rather than a default.
  - **SNR** is the peak against the record's quietest quarter, and a short
    cut's quietest quarter is still decay, not noise: 87.0 -> 82.0 dB and
    80.4 -> 76.7 dB on those two records.
  - **Coherence** weights the GCC-PHAT refinement and lives on the FULL
    record's half spectrum; a cut is a different bin grid, so the weight would
    have to be dropped (losing what #19 added) or resampled onto it.
  Merge bar is the session battery, as for anything that moves an arrival.
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
- [✗] **`DelayTableText` reads cells back from its own rendered text — KEPT**
  (decided 2026-07-22; moved here 2026-08-31 from the retired HANDOFF.md).
  Click-to-copy extracts a cell from the fixed-column text rather than the
  table being a data model that renders. The structured-model alternative was
  dropped for low payoff: formatting and the reverse extraction live in one
  unit-tested class exactly so the two cannot disagree on the layout — the
  file's own header says as much.

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
