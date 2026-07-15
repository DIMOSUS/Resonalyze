# TODO — tech debt from the code reviews

Open items found during the review passes that were deliberately reported
instead of fixed. Completed items were removed (they live in git history);
the residual Windows live-checks they left behind are consolidated below.
Grouped by area, highest-value items marked ★.

## Windows live-checks pending (verification of already-merged DSP work)

The DSP side of these is done and unit-tested on CI; each needs a hands-on
check in the running app (`source/` is `net10.0-windows`, not buildable on the
Linux dev env where the work was done).

- [ ] **GCC-PHAT alignment confidence line** renders in the Time Alignment
  panel ("Alignment: NN% (GCC-PHAT / envelope fallback)"); re-eyeball the fixed
  0.2 PHAT trust gate on real captures (coherence weighting shifts the peak).
- [ ] **Excitation-masked H1** (relative regularization + explicit sweep-start
  edge in `TransferFunction.ComputeAveragedRelativeIr`): re-measure and confirm
  the transfer FR/IR look right; on reconstructed field captures the infrasonic
  rumble that used to bias the loopback-referenced sub timing by up to ~0.34 ms
  is gone, so a fresh sub measurement is the interesting one.
- [ ] **Butterworth auto-delay polarity**: re-run the user's tune and confirm no
  channel is inverted on one side of a pair alone.
- [ ] **Honest dBr/dBc axis + tracker labels** (FR plot title, distortion tracker
  text) render correctly.
- [ ] **HD curves at 1/12 smoothing** look right (were over-smoothed); the grey
  **Noise-floor trace** + legend renders; **harmonic-overlap warning text**
  renders next to the plot.
- [ ] **Auto crossover wizard** on a real swept measurement: slope-deviation
  penalty, tweeter resonance floor, distortion-aware bounds (dirty-low tweeter
  held above the floor, clean one still at the class floor), coherence-gated
  band read, and the async Apply path (freeze-during-ranking). Re-eyeball
  `AchievabilityWeight = 0.5` if field ranking disagrees.
- [ ] **Virtual DSP load lock**: the disable/"Loading previous session…" note
  appears immediately on open/import and controls re-enable exactly when curves
  land.
- [ ] **Target-curve gains dialog** ("Sub level over mid/treble" field): layout,
  lazy default fill, freeze-during-ranking.
- [ ] **Sweep run-quality retry flow** ("Retrying x/y", per-run reject modal,
  all-runs-failed path) on real hardware.

## DSP library (`dsp/`)

- [ ] **`EqAutoTuner` greedy fit has no polish pass.** Band gain is fixed from
  the residual at the peak before Q is chosen (no joint gain/Q optimization),
  there is no final coordinate-descent pass (`CrossoverAutoSetup` already has
  one to borrow), and the preamp is rounded to integer dB *before* the fit.
- [ ] **`CrossoverAutoSetup.Optimizer.Score()` recomputes every channel** on
  each junction/gain trial (~300–1500 calls per junction). Filter magnitudes
  are cached, but the amplitudes of untouched channels are not.

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
- [ ] **Stereo scene diagnostics (Δ per band)**: the stereo Auto delay cascade
  is in; the complementary *verification* layer is not — per-band L−R arrival
  differences of the FINAL tune, compared across bands (0.06 ms repeatability on
  real measurements), with a warning when a band's Δ walks off the top pair's by
  a sizable fraction of its junction period (a period slip that survived the
  per-side sum optimization), and optionally a candidate-list re-pick to fix it
  for free. Also: L/R polarity consistency per band.
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
  independent polarity witness (`EstimatePolarity` on the band-passed arrivals).
- [ ] **Promotion / latch constants are field-anchored, not modelled.**
  `WideWindowPromotionMarginDb` (1.6), `PromotionReachPeriods` (2.5) and
  `MaxInterSideDirectPathMs` (8) in `AutoAlignmentEngine.cs` are set between a
  handful of field observations with thin headroom (the false-hop 1.46 dB vs
  genuine-recovery 1.87 dB split has only ~0.2 dB either side). Minimum: log a
  diagnostic when a junction lands in the gray zone (currently it silently
  picks). Better: derive the threshold from comb statistics of the junction.
- [ ] **Time Alignment analysis is not cached** — `RefreshAnalysis`
  (`TimeAlignmentPanelController`) recomputes Hilbert + GCC-PHAT on every tab
  show even when inputs are unchanged. Needs a live-app check to avoid stale
  display.
- [ ] **Crossover wizard breaks at 150 %+ DPI**
  (`VirtualCrossoverAutoSetupDialog`): extra channel rows use hardcoded pixel
  offsets (`RowTop = 42`, `RowStep = 28`), so rows 4–8 can overlap scaled
  designer controls. Verify on Windows and switch to layout-panel positioning.
- [ ] **Uniform-sample-rate assumption in the Virtual DSP plots.**
  `DrawImpulseCurves`/`DrawPhaseCurves`/`DrawMagnitudeCurves` take
  `processed[0].SampleRate` for every trace, while `CrossSideTargetMs` already
  uses per-channel `SampleRate` — so mixed rates in one project are possible.
  Either enforce a single rate or map each trace by its own.
- [ ] **`VirtualCrossoverPanel` remains a large view-controller.** Interactive
  redraw revision/cancellation, processed-response caching and background DSP
  scheduling now live in `VirtualCrossoverProcessingCoordinator`; its source
  snapshots own write-once IR copies and stale results cannot enter the cache or
  reach the view. The panel still owns source selection/loading, left/right
  session state, auto-alignment orchestration, project persistence, metric-data
  preparation and OxyPlot construction. Continue with one tested vertical slice
  at a time (the source-loading coordinator or session model are the next useful
  boundaries); do not describe the remaining code as inherently UI-bound.
- [ ] **`DelayTableText` parses rendered fixed-width columns** (18/37 chars) for
  copy-values instead of holding a value model (format+parse are co-located and
  tested). Deferred: a value model pushes the change into the panel's
  click-position→cell mapping, best verified on Windows, for low payoff.
- [ ] **PDF images still go through temp files.** The shared `PdfSheet` helper
  centralised the temp-file dance, but MigraDoc 6 supports
  `AddImage("base64:...")`, which would remove it (needs a Windows render check
  that the sheets stay pixel-identical).

## Measurement orchestrators

- [ ] **Verify the reused ASIO session on hardware.** Averaged sweeps now keep
  one open ASIO session across runs (the driver plays silence between runs and
  only the capture accumulator restarts). Software lifecycle guards are covered
  deterministically: callback pools are allocated before playback, reset advances
  a capture epoch and drains queued old blocks, an in-flight old block is rejected
  at the final locked append, overflow/worker failure can recover on reset, and
  successful capture completion atomically surfaces a terminal pump failure before
  clearing that epoch, while ASIO stop drains already accepted work before snapshot;
  final capture atomically detaches its accumulator before copying the completed
  samples outside the session lock (so the worker can keep returning queue slots).
  The PCM device-to-pump handoff uses a value packet rather than allocating an
  application-owned EventArgs per callback; disposal drops queued work and detaches
  subscribers before waiting for an already in-flight worker block. Sweep baselines
  use pump-accepted frames (including queued work), so worker latency cannot shorten
  the requested capture tail.
  The remaining item is device/driver integration: run an averaged ASIO measurement
  on real hardware (ideally a slow driver).
- [ ] **Sweep-run quality: unambiguous checks only, by decision.** The
  statistical outlier layer (peak-delay vs median, IR correlation vs a reference
  run) and the run pre-alignment rework were **rejected by the user
  (2026-07-11), do not resurrect** — the unambiguous checks (clipping / silent /
  undersized) plus one retry cover the real field failure mode. Cosmetic tails
  left as-is: the stored raw samples are only the last run's; the Wave RMS meter
  integrates the lead-in/tail silence.

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
- [ ] **`LiveSpectrumOpt` shadow fields update only on
  `SelectionChangeCommitted`.** No current path loses a change (the R reset
  button raises it too) — fragility for future programmatic writes, not an active
  bug.

## UI chrome

- [ ] **`ChromeTitleBar` caches the DPI scale once at `Initialize`.** No
  `DpiChanged` handling: moving the window to a monitor with different DPI
  (PerMonitorV2) leaves the bar height, button widths and tab layout at the old
  scale. Refresh the cached metrics and re-run layout on DPI change.
- [ ] **Top-edge resize grip still dead over child controls.** The HTTRANSPARENT
  fix covers the title-bar panel itself; points over the tab buttons and window
  buttons (which reach y=0) still hit-test as client. Low value; document or fix
  later.

## Audio capture layer

- [ ] **`SoundRecorder` / `AsioFullDuplexSession` remain separate classes.** The
  waiter registry, stop-timeout, accumulator core and metering math are shared;
  what is left duplicated is the thin device glue (start/first-buffer/stopped
  choreography, the event triple). A full merge needs a common device
  abstraction — low value until the WASAPI migration forces one.
- [ ] **ASIO converts channels `0..offset+count` instead of a window from
  `InputChannelOffset`** (`AsioFullDuplexSession`): a mic on input 7 converts all
  8 channels per callback. Possibly a NAudio `SetChannelOffset` workaround —
  needs hardware to verify.
- [ ] **Wave backend still uses legacy MME** (`WaveInEvent`/`WaveOutEvent`) with
  hidden mixer resampling and extra latency; migrate to WASAPI
  (exclusive/shared) with a device-compatibility pass.

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

- [ ] ★ **`Form1` is still the concurrency hub** (largely mitigated). Mutable
  state on one object is touched by the UI thread, `Task.Run` plot builds and
  audio-callback events. The load-bearing carve-outs are done
  (`MicrophoneCalibrationService`, `StartupAudioWarmup`, `DebouncedSaver`,
  `CompareSelection`, `ButtonLongPressBehavior`, `MeasurementSessionTracker`,
  `ActiveOverlaySlotTracker`). What remains on Form1 is the lifecycle/close flags
  and transient UI bits — inherently form-bound; further slicing is optional
  polish, not a concurrency risk.
- [ ] **`WireLiveApply` covers only dialog-open controls.** Controls created
  after wiring never get live-apply behavior. Deferred to a Windows session: the
  fix hooks `ControlAdded` recursively and re-enters the apply debounce, so it
  needs a live check that dynamically-added rows apply exactly once.

## EQ Wizard

- [ ] ★ **No boostability/reliability mask**: the fitter sees only dB curves and
  will boost a deep interference null (wasting headroom and blocking an octave).
  Fix: mask from coherence + null depth/width + driver band; a cuts-only mode
  (sensible default for car tuning). (The clipping-profile half is fixed:
  `TotalGainMaxDb` caps preamp + band peak.)
- [ ] **Band spacing ignores the chosen Q** (fixed ±0.33/±1 oct blocks); **gain
  is fixed before Q is searched**; **the objective treats boosts and cuts
  symmetrically**. All three fold into one redesign of the greedy loop:
  frequency × Q × gain search with width-based spacing and a boost-penalized
  score, then a coordinate-descent polish over all bands + preamp.
- [ ] **miniDSP export needs a target-device profile**: the sample rate is a
  constructor parameter now, but device biquad limits are not checked and the
  preamp burns a biquad slot instead of mapping to the device's gain control.

## Time Alignment / unwrap

- [ ] **GCC-PHAT confidence is peak height, not uniqueness**: a single spectral
  line or a narrowband subwoofer reads ~100% while the delay is poorly
  conditioned. Fix: fold RMS bandwidth / peak curvature / peak-to-second-peak
  into the confidence, or rename the figure. Needs a validation pass on real
  measurements. (Related to the flagship sub group-delay-by-frequency work in the
  memory follow-ups.)
- [ ] **`WrapPeakPositions` cannot actually produce negative delays** — the peak
  search is capped at length/2, so the upper-half branch of `ToSignedDelaySamples`
  is unreachable (harmless for the loopback workflow; the API promises more than
  it does).
- [ ] **Display smoothing includes low-reliability bins** (unwrap blanks long
  garbage stretches, but short noisy nulls still enter `SmoothLinear` at full
  weight; magnitude curves behave the same). Optional: reliability-weighted
  smoothing.

## Live Spectrum / coherence

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
- [ ] **Measurement files validate only after full deserialization**: a crafted
  file can declare hundreds of millions of samples and hit OOM before
  `Validate()` runs. Add a file-size cap before parsing, a max-samples cap, and
  `OutOfMemoryException` handling.
- [ ] **Uninstaller leaves settings behind.** Offer (or document) removal of the
  settings/history files on uninstall.
- [ ] **Release toolchain is unpinned** (`choco install innosetup`, latest
  NetSparkle appcast tool, actions by major tag): pin exact versions (and SHAs
  for actions) once the current-good versions are confirmed. (The shell-injection
  surface, branch-vs-tag build mismatch and auto-published AI notes are fixed.)
