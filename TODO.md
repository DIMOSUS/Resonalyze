# TODO — tech debt from the code reviews

Open items found during the review passes that were deliberately reported
instead of fixed. Completed items are removed (they live in git history), and an
item describing work already done keeps only the residual. Grouped by area,
highest-value items marked ★. `[✗]` marks a settled decision kept on purpose, so
the same idea does not get re-proposed — those are not open work.

Last audited against the code on 2026-07-22 (tip `255de70`). That pass dropped
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
  gain · polarity · delay · crossover · all-pass · PEQ analytically per
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
- [ ] **ASIO driver opened twice when the measurement panel opens.** `Init` runs
  `RefreshSampleRateOptions` and `RefreshAsioDriverInfo`, each instantiating
  `AsioOut` (a synchronous COM open that can take seconds). Fetch driver info and
  supported rates in one open, ideally off the UI thread.
- [ ] **`TukeyWindowControlHelper` clamps are irreversible.** Shrinking the
  window length clamps the fade values (semantically required, visible in the
  controls), but growing it back does not restore them; a shadow-value restore
  like the loopback-channel one would make the clamp reversible. Deferred to a
  Windows session: control-value re-entrancy across three panels
  (FR/Waterfall/BurstDecay) needs a live render check.

## UI chrome

- [ ] **`ChromeTitleBar` caches the DPI scale once at `Initialize`.** No
  `DpiChanged` handling: moving the window to a monitor with different DPI
  (PerMonitorV2) leaves the bar height, button widths and tab layout at the old
  scale. Refresh the cached metrics and re-run layout on DPI change.

## Audio capture layer

- [ ] **ASIO converts channels `0..offset+count` instead of a window from
  `InputChannelOffset`** (`AsioFullDuplexSession`): a mic on input 7 converts all
  8 channels per callback. Possibly a NAudio `SetChannelOffset` workaround —
  needs hardware to verify.

## Overlays

- [ ] **Introduce an `OverlaySlotState` record** to replace the triple
  field-mapping between overlay, slot file and UI state (the render-path caching
  and the pure-math extraction from `Overlay.cs` are done; this structural half
  remains).
- [ ] **Overlay curves are assumed sorted/unique/finite in X**
  (`CalculateOperation`'s forward-only cursor): normalize imported overlays once
  (drop non-finite, sort, merge duplicate frequencies).

## Plotting

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
- [ ] ★ **Only peaking bands — no shelves.** `PeqBand` is `(Fc, Q, gain)` with no
  filter type, so `EqAutoTuner`, the preview (`DigitalEqualizationResponse`) and
  the parsers are peaking-only. Car targets are shelved (bass boost + downward
  tilt), which a stack of peaking bands approximates poorly (wasted slots,
  ringing). It is also a lossy-import bug: `PeqTextFile`/REW parsing silently
  drops every non-`PK` filter, so a REW/Equalizer APO car tune imported here loses
  its shelves. Add low/high shelf to `PeqBand` + `DigitalEqualizationResponse` and
  make the parsers/formatters round-trip them. (HP/LP/notch/all-pass are NOT
  needed here — the Virtual DSP tool owns crossovers and time alignment.)
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
  separate master gain the profile must respect.

Deliberately out of scope for car DSP tuning (do not add here): FIR/convolution
export (car DSPs are biquad), a phase / min-phase / all-pass view (the Virtual
DSP crossover + delay tool owns phase and time), real-time PC audio preview (you
listen in the car after loading the profile), arbitrary target-curve import (the
Car / CarMild / XCurve presets cover it), and HP/LP filter types (crossover tool).

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

- [ ] **History entries reference LIVE overlay slots** (`ActiveOverlaySlots`
  numbers into mutable global storage): restoring an old session shows whatever
  the slots hold TODAY. Store immutable overlay snapshots (content-addressed
  revisions) in the history entry.

## Signal Generator / files / calibration / release

- [ ] **Estimated 90° calibration looks real**: `Has(Degrees90)` is true when the
  90° curve is approximated from the 0° file, and the UI labels it plainly "90
  degrees". Label it "estimated from 0°" so the user knows the 90° curve is
  inferred, not measured.
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
