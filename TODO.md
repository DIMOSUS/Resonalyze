# TODO — tech debt from the full code review

Items found during the two review passes (PR #1, PR #2) that were deliberately
reported instead of fixed. Grouped by area, highest-value items marked ★.

## DSP library (`dsp/`)

- [x] **`CalibrationFile` does filesystem I/O in the dsp layer** — done.
  Added `CalibrationFile.Parse(text, sourceName?)`, a pure parser matching the
  EQ formats' "parser accepts text" shape; the shared `ParseText` does all line
  parsing/sorting and the `CalibrationFile(string file)` constructor stays a
  thin I/O wrapper (`LoadFromFile` owns the two filesystem `LoadError`
  messages), so every caller is untouched and the content-error message is
  unchanged. Tests can now parse from text without temp files.
- [ ] **`EqAutoTuner` greedy fit has no polish pass.** Band gain is fixed from
  the residual at the peak before Q is chosen (no joint gain/Q optimization),
  there is no final coordinate-descent pass (`CrossoverAutoSetup` already has
  one to borrow), and the preamp is rounded to integer dB *before* the fit.
- [ ] **`CrossoverAutoSetup.Optimizer.Score()` recomputes every channel** on
  each junction/gain trial (~300–1500 calls per junction). Filter magnitudes
  are cached, but the amplitudes of untouched channels are not.
- [ ] **Duplication (partly resolved, partly deliberate):** the coherence
  formula no longer lives twice — `TransferFunction.ComputeAveragedRelativeIr`
  now calls `SpectrumAnalysis.ComputeCoherence`. The unused
  `SweepAnalysis.DeconvolveWithInverseFilter(float, double)` overload was
  removed; the remaining two share the single `ToDoubles` converter.
  **Kept by design:** `DspChannelChain.Response` looks like a re-implementation
  of `PreparedDspResponse` but is the independent per-frequency reference the
  DSP tests use as an oracle (`PreparedDspResponse_MatchesDspChannelChainResponse`,
  `ApplyChain_MatchesReference`) — merging it would make those checks tautological.
  The single-frame `TransferFunction.ComputeRelativeIr` is unused in production
  but shares all its internals with the averaged path (no code is duplicated) and
  is the directly-tested single-frame H1 primitive with a `filter` parameter, so
  it stays as a tested entry point.
- [x] **`FrequencyResponseOptions` is a grab-bag** — done. The ten `Show*`
  visibility flags were moved off the DSP `FrequencyResponseOptions` into a new
  source-side `CurveVisibilityOptions` (one per mode). `DataHelper.GetSpectrum`
  now takes an explicit `SpectrumCurves` flags enum instead of reading `Show*`;
  `PlotModelFactory` reads the phase/GD/coherence flags from the visibility
  object. The persisted settings DTO keeps its own `Show*` block, so the on-disk
  YAML is unchanged (no migration). Coverage was extended so the move is
  CI-verifiable: a dsp `SpectrumCurves`→curves test, a `ToSpectrumCurves()`
  translation test, and PlotModelFactory flag-gating tests.
- [x] **`DataHelper` is a ~1160-line god-object** — done (move-only). Split
  into a `partial` static class across `DataHelper.cs` (core: dB conversion,
  `ExtractWindow`, the option types), `DataHelper.Spectrum.cs`,
  `DataHelper.Phase.cs` (phase + group delay), `DataHelper.Impulse.cs`
  (impulse + autocorrelation) and `DataHelper.Resampling.cs`. Verified
  byte-identical: the sorted non-blank member lines of the union equal the
  original exactly, and the 287 dsp tests pass untouched.
- [x] **Undocumented magic numbers in harmonic analysis** (`DataHelper`) —
  done. Named and documented: `HarmonicWindowUpperGuard`/`HarmonicWindowLowerReach`
  (the `h+0.03`/`h−0.5` isolation window), `ThdWindowUpperHarmonic`/
  `ThdWindowLowerHarmonic` (`5.5…1.5`) and `HarmonicSmoothingWidthFactor` (the
  `2×` smoothing width harmonic/THD curves use over the primary). No behavior
  change.

- [x] **DSP test-coverage quality pass** — done. An audit of the dsp suite (real
  cobertura line/branch numbers plus a per-module adversarial review of test
  *meaningfulness*, not just presence) found one entirely-untested module and a
  systemic "constant-input" smell (resampler, calibration, HD/THD isolation
  exercised only on flat data, so the interesting math ran but was never pinned).
  Closed with ~60 new tests, all cross-platform and CI-verifiable:
  `WaterfallAnalysis` (was 0%: Morlet localisation/decay, frequency-grid geometry,
  resample dB/floor); the degrees-domain phase API (`GetPhase`/`GetMinimumPhase`/
  `GetExcessPhase`/`EstimatePhaseDetrend`); `LogarithmicResample`/`CalibrationFile`
  known-answer tests on *non-constant* data; `DspMath.LanczosKernel`; window
  coefficients; the `SignalEnvelope` SNR gate; `TransferFunction` coherence<1 and
  the PHAT degenerate guard; the `AutoAlignmentEngine` downward walk;
  `PeakingBiquad` digital-vs-analog off-centre; `CrossoverAutoSetup` Midbass;
  `VirtualCrossover` degenerate window + loss diagnostics; `EqAutoTuner` NaN mask /
  clamp / Q fallback; and MiniDSP/GraphicEQ/EasyEffects/Calibration numeric
  payloads. Overall dsp line coverage 89% → 95%, branch 83% → 89%.
  **Deliberate residuals (not shipped as tests, would have asserted unreachable or
  tautological behaviour):**
  - `TimeAlignmentAnalysis.ToSignedDelaySamples` subtraction branch: the peak
    search is capped at `length/2`, so a detected arrival never lands above the
    wrap pivot through the public `Analyze`. Only the pivot and comparison
    direction are pinned (a no-op test); the negative-arrival arithmetic is not
    reachable at the API boundary.
  - `FindBandLimitedCorrelationDelay` collapse fallback (`highHz <= lowHz`): only
    reachable when `centerFrequencyHz > Nyquist`, which the app never passes, and
    the fallback does not actually reorder the band when `lowHz > 0.95·Nyquist`.
    Latent robustness gap, not a live bug; the reachable Nyquist clamp is tested.
  - `SearchAlignmentCandidatesByLoss` exact 1/f-weighted `LossDb`/`DipDb` values:
    a byte-exact known-answer would have to re-derive the 1/6-octave-smoothed
    log-weighted kernel (brittle). Covered instead by a relative test (aligned ~0
    vs offset cancellation) that pins the diagnostics as non-vacuous.

- [x] **GCC-PHAT coherence weighting + alignment confidence** — done. The
  time-alignment refinement whitened every in-band bin to unit magnitude, giving a
  noisy/low-SNR bin exactly as much say in the sub-sample delay as a clean one (the
  classic PHAT weakness). It now folds the measured γ² (already computed by
  `ComputeAveragedRelativeIr`, previously unused here) into the whitening: each bin
  is scaled by a floored-linear weight `1 − (1−0.25)·(1−γ²)`, so bins whose phase
  does not repeat across averages carry ≤ their share while every in-band bin keeps
  ≥ 25% of its weight (bandwidth preserved — no spectral hole to ring back as side
  lobes). Design + invariants were run through a multi-agent design panel and
  adversarial verification; the two load-bearing invariants were also re-proved by
  hand: (1) the complement form makes flat/unit γ² a **bit-exact** no-op for any
  floor, and null/wrong-length γ² is ignored, so every existing caller and all 356
  prior dsp tests stay green; (2) γ² is folded to both Hermitian halves identically,
  keeping the whitened spectrum conjugate-symmetric (real IFFT). Also surfaced the
  refinement's own trust: `TimeAlignmentAnalysisResult` now carries per-arrival
  `…Confidence` ([0,1] PHAT peak height) + `…RefinedByPhat`, shown in the panel as an
  "Alignment: NN% (GCC-PHAT / envelope fallback)" line. 9 new cross-platform dsp
  tests (no-op bit-identical, flat-non-unity invariance, length-mismatch degrade,
  Hermitian/real guard, corrupted-band improvement, distortion caveat, confidence);
  365 dsp tests green.
  **Honest limitations (documented, not hidden):**
  - Coherence weighting suppresses only non-repeatable content. Repeatable harmonic
    distortion reports γ²≈1 and is **not** suppressed — pinned by a caveat test.
  - The weight shifts the absolute PHAT peak correlation slightly (usually up, as
    noise bins are demoted), so the fixed 0.2 trust gate should be re-eyeballed on
    real captures; a fully low-coherence capture legitimately drops below it and
    falls back to the envelope parabola (acceptable).
  - The strict `Count == fftLength/2+1` gate rejects wrong-length arrays but cannot
    detect a same-length *stale* γ²; the contract (γ² must come from the same
    transfer FFT as the IR) is documented on the API.
  - **App wiring is unbuilt on this Linux env** (`source/` is `net10.0-windows`):
    the `TimeAlignmentPanelController` plumbing (source record + two `Analyze`
    calls + the confidence line) is mechanical and verified against real signatures,
    but needs a Windows build + a live sanity check of the new "Alignment:" line.

## Virtual DSP / Time Alignment

- [✗] **Phase-slope (residual group delay) as an Auto delay score prior —
  REFUTED on the user's real measurements** (probed 2026-07-10, five junctions
  of the left 4-way + right mid/twr from `assets/test_data`). The idea:
  penalize candidates whose inter-channel phase slope across the pair band is
  non-flat, since a lobe impostor differs by exactly one period of residual
  delay. The probe measured the flat-slope delay per junction (wrap-safe
  adjacent-bin phase increments of the gated cross-spectrum, two weightings —
  both agree) and compared it with the summation winner and the user's manual
  tune. Result: at the true alignment the inter-channel phase slope is NOT
  flat — it carries the honest group-delay difference of drivers plus
  non-matched crossover filters, and that reaches a FULL period at real
  junctions: left woof/mid true lobe sits 1.02 T from the flat-slope point;
  right mid/twr (the gapped BW24 LP 1300 / HP 1800 junction) true lobe sits
  1.82 T away while the flat-slope point lands on the old field-failure
  answer (~mid +2.0 ms) — flat phase slope is mathematically the GCC-PHAT
  peak, so this prior would re-trust exactly the PHAT lobe position the
  `PhatSeedMinDominance` gate was added to distrust. Any weight strong enough
  to separate lobes (≥0.5 dB/T) flips the right mid/twr winner to a wrong
  inverted candidate. Do not implement as a score term; at most surface the
  per-candidate residual GD as a log diagnostic. Side finding, positive: on
  every junction with a known manual reference the current score+selection
  already reproduces the user's tune (mid/twr left −0.154 vs manual −0.16 ms;
  woof/mid left 3.878 vs manual 3.84 — via the normal-polarity margin, which
  won by only 0.02 dB there, see next item; right mid/twr −0.635 vs manual
  ~0.68 on the mid side).
  **Follow-up (same day, after the scene-preserving co-move shifted the D
  pair −0.52 ms):** re-probed at the final cascade state, asking whether the
  flat-slope point now agrees with the validated tune. It does not — the
  verdict got stronger: the C/D flat-slope residual reads −1.36 T on the left
  and +1.71 T on the right (B/C: −1.06 / −0.87 T), i.e. the two sides want
  OPPOSITE corrections (left +0.53 ms, right −1.84 ms), so no scene-preserving
  pair move can satisfy the slope criterion on both sides even in principle.
  On the left the loss-optimal co-move went in exactly the opposite direction
  from the flat-slope point's wish while landing on the user's hand tune. The
  per-side filter+driver group-delay asymmetry is real and side-asymmetric;
  the refutation stands.
- [ ] **Stereo scene diagnostics (Δ per band)**: the stereo Auto delay cascade
  (left walk → arrival bridge with the scene offset → right descent) is in;
  the complementary *verification* layer is not — per-band L−R arrival
  differences of the FINAL tune, compared across bands (probe showed 0.06 ms
  repeatability on real measurements), with a warning when a band's Δ walks
  off the top pair's by a sizable fraction of its junction period (a period
  slip that survived the per-side sum optimization), and optionally a
  candidate-list re-pick to fix it for free. Also: L/R polarity consistency
  per band.
- [ ] **`SceneLockToleranceMs = 0.05` is aggressive for pairs whose
  localizable band is narrow and low** (e.g. reaching only ~300–400 Hz):
  the band passes the minimum-width admission, but the temporal certainty of
  such a narrow-band envelope arrival is typically worse than 0.05 ms, so the
  lock can pin the channel inside the measurement noise. Acceptable today
  thanks to the guards around it — the minimum lock-band width, the SNR gate,
  the lock refusing an invalid arrival, and the scene-preserving co-move that
  recovers junction quality afterwards — but non-blocking risk, recorded
  2026-07-10. Follow-up: make the tolerance a function of the lock band's
  width/center (roughly: a fraction of the band-center period, floored at
  0.05 ms), or of an explicit arrival-uncertainty estimate (envelope rise
  time / FirstArrivalProminence), so a wide tweeter band keeps the tight pin
  while a barely-localizable pair gets an honest slack.
- [x] **Stereo level (ILD) read-out next to Δ** — done: the metric block
  gained a "Level Δ L−R (dB)" row per pair (`MeasureBandLevelDb`: gated,
  log-frequency-weighted band level of each processed side; gated by the
  same per-side arrival reliability; tooltip explains the sign and the
  single-mic-vs-binaural gap). Deliberately a diagnostic, no auto gain trim.
  Original note — field-confirmed follow-up
  (2026-07-10): with the cascade's delays in the car the user reported the
  scene "dead center" and same-channel drivers indistinguishable by ear, but
  full centering ALSO needed a manual −3…−4 dB trim of the LEFT mid+tweeter:
  level steers the image alongside timing. The same asymmetry shows in the
  measurements — gated band level L−R of the final processed sides on
  `assets/test_data`: mid +1.6 dB (175–1300; +1.2 in 300–1300), twr +0.6 dB,
  woof +4.3 dB — same sign, smaller magnitude than perceived, as expected:
  one omni mic at the head position sees no head shadow, so the effective
  binaural ILD is larger than the measured single-point figure. Feature: a
  per-pair L−R level column in the metric block (measured in the pair's
  shared band, same gate as the sum loss) as a *diagnostic* — do NOT auto-trim
  gains from it (it under-corrects vs binaural perception and taste); at most
  a gentle hint when the localization-band level asymmetry exceeds ~2 dB.
- [ ] **`AlignmentSelection` normal-polarity margin near-miss on real data**:
  at the user's left woof/mid junction the inverted impostor out-scores the
  true normal candidate by 0.23 dB — the 0.25 dB
  `DefaultInvertPreferenceMarginDb` saves the pick by just 0.02 dB. One noisier
  measurement could flip it. Worth revisiting with more field measurements
  (margin as a function of junction frequency / band coherence, or an
  independent polarity witness such as `EstimatePolarity` on the band-passed
  arrivals) before touching the constant.
- [ ] **Time Alignment analysis is not cached** — `RefreshAnalysis`
  (`TimeAlignmentPanelController`) recomputes Hilbert + GCC-PHAT on every tab
  show even when inputs are unchanged. Needs a live-app check to avoid stale
  display.
- [ ] **Crossover wizard breaks at 150 %+ DPI**
  (`VirtualCrossoverAutoSetupDialog`): extra channel rows use hardcoded pixel
  offsets (`RowTop = 42`, `RowStep = 28`), so rows 4–8 can overlap scaled
  designer controls. Verify on Windows and switch to layout-panel positioning.
- [x] **The project-file `.backup` rename is silent** — done.
  `LoadOrDefault` now surfaces the created backup path through
  `BackupNoticePath`, and the Virtual DSP panel shows a one-time notice telling
  the user their unreadable session was moved aside and a `.backup` exists to
  recover from.
- [~] **`VirtualCrossoverPanel` (~2.6k lines) — correctness kernels extracted;
  a cosmetic partial-split was deliberately rejected.** A move-only partial
  split would not reduce coupling — it just spreads the God-object across files.
  Instead the genuinely non-UI, correctness-critical logic was pulled out into
  tested units: `VirtualCrossoverSourceRules` (the source-compatibility decision,
  previously hand-rolled as two inverse predicates that could drift),
  `VirtualCrossoverAnalysis.SumLossCurve` (the one per-point sum-loss definition
  now shared by the drawn curve, `AverageSumLossDb` and `MinimumSumLossDb` — the
  drawn "Sum loss" curve had reimplemented the tested formula on an untested
  path), `PreparedDspResponse.GroupDelayMs` (a pure τ_g routine that lived in the
  panel), and the band arithmetic (`GetCrossoverWindow`/`BandCenterHz`/
  `OverlapBand`) into `VirtualCrossoverJunctions`. All are unit-tested (dsp on
  Linux, junction/source rules on Windows CI). The panel remains a large
  view-controller by nature (~60-70% is irreducible WinForms/OxyPlot wiring);
  deeper extractions (decouple `ChannelRuntime` from its control, then the
  process/alignment cores) are deferred to a Windows session where the
  interactive paths can be exercised.
- [ ] **`DelayTableText` parses rendered fixed-width columns** (18/37 chars)
  for copy-values instead of holding a value model (format+parse are at least
  co-located and tested now). *Deferred:* the current form is clean and tested;
  a value model would push the change into the panel's click-position→cell
  mapping, which is UI interaction best verified on Windows, for low payoff.

## Measurement orchestrators

- [ ] **Verify the reused ASIO session on hardware.** Averaged sweeps now keep
  one open ASIO session across runs (the driver plays silence between runs and
  only the capture accumulator restarts). Verified by construction only — run
  an averaged ASIO measurement on real hardware (ideally with a slow driver)
  before relying on it.

## Options panels

- [ ] **Loopback channel can still persist as `null` across a restart.** The
  in-session loss is fixed (a shadow field restores the choice when a stereo
  device is selected again), but applying while a mono/missing device is
  selected persists "None"; after a restart there is nothing to restore. Would
  need the preferred offset persisted separately from the effective one.
- [ ] **ASIO driver opened twice when the measurement panel opens.**
  `Init` runs `RefreshSampleRateOptions` and `RefreshAsioDriverInfo`, each of
  which instantiates `AsioOut` (a synchronous COM driver open that can take
  seconds). Fetch the driver info and supported rates in one open, ideally off
  the UI thread.
- [x] **`SetOptions` reads bits from the measurement, not the control** — done.
  `MeasurementOptions` now reads the bit depth from `numericUpDownBits` (the
  single UI source of truth, matching `GetSupportedSampleRates`). Behavior is
  unchanged while the control is read-only; it stops silently ignoring the
  control the day it becomes editable.
- [ ] **`TukeyWindowControlHelper` clamps are irreversible.** Shrinking the
  window length clamps the fade values (semantically required, and visible in
  the controls), but growing it back does not restore them; a shadow-value
  restore like the loopback-channel one would make the clamp reversible.
  *Deferred to a Windows session:* it is a deliberate behavior change with
  control-value re-entrancy across three panels (FR/Waterfall/BurstDecay) that
  needs a live render check, not a Linux compile.
- [ ] **`LiveSpectrumOpt` shadow fields update only on
  `SelectionChangeCommitted`.** Re-verified: the R reset button raises
  `SelectionChangeCommitted` too, so no current path loses a change — this is
  fragility for future programmatic writes, not an active bug. (The three
  identical floor-index loops are now one shared `FloorIndex` helper; the
  shadow-on-committed behavior is left as-is by design.)

## UI chrome

- [ ] **`ChromeTitleBar` caches the DPI scale once at `Initialize`.** No
  `DpiChanged` handling: moving the window to a monitor with different DPI
  (PerMonitorV2) leaves the bar height, button widths and tab layout at the
  old scale. Refresh the cached metrics and re-run layout on DPI change.
- [ ] **Top-edge resize grip still dead over child controls.** The
  HTTRANSPARENT fix covers the title-bar panel itself; points over the tab
  buttons and window buttons (which reach y=0) still hit-test as client.
  Standard chrome resolves this per child; low value, document or fix later.
- [x] **`ColorPickerDialog` hue slider repaints its full rainbow per
  mouse-move** — done. The rainbow (which depends only on size) is cached in a
  bitmap, rebuilt only on resize and disposed with the slider; the preview panel
  already sets `DoubleBuffered`.

## PDF export

- [ ] **The PDF images still go through temp files.** The shared `PdfSheet`
  helper centralised the temp-file dance, but MigraDoc 6 supports
  `AddImage("base64:...")`, which would remove it entirely (needs a Windows
  render check that the sheets stay pixel-identical).
- [x] **No deconvolution flatness test** — done.
  `SweepDeconvolutionFlatnessTests` regenerates an exponential sine sweep and
  its inverse filter, deconvolves the unity round-trip, and asserts the in-band
  magnitude is flat (measured ripple 0.04 dB around a 0.01 dB mean), pinning the
  `2/N` scaling and the inverse-filter envelope compensation.

## Audio capture layer

- [ ] **`SoundRecorder` / `AsioFullDuplexSession` remain separate classes.**
  The waiter registry, stop-timeout machinery, accumulator core and metering
  math are shared now; what is left duplicated is the thin device glue (the
  start/first-buffer/stopped signal choreography and the event triple). A full
  merge would need a common device abstraction — low value until the WASAPI
  migration below forces one.
- [ ] **ASIO converts channels `0..offset+count` instead of a window from
  `InputChannelOffset`** (`AsioFullDuplexSession`): a mic on input 7 converts
  all 8 channels per callback. Possibly a NAudio `SetChannelOffset` workaround
  — needs hardware to verify before changing.
- [ ] **Wave backend still uses legacy MME** (`WaveInEvent`/`WaveOutEvent`)
  with hidden mixer resampling and extra latency; migrate to WASAPI
  (exclusive/shared) with a device-compatibility pass.
- [ ] **Synchronous `LevelsAvailable` subscribers can still stall the ASIO
  callback** if a subscriber does a blocking `Invoke` to the UI thread; verify
  live (the meter itself now coalesces, but the contract isn't enforced).
- [ ] **`GetSamplesSnapshot` copies the whole buffer under the callback
  lock**; safe only because it's called after `StopAsync`, which the contract
  doesn't enforce.

## Overlays

- [ ] **Introduce an `OverlaySlotState` record** to replace the triple
  field-mapping between overlay, slot file and UI state (the render-path
  caching and the pure-math extraction from `Overlay.cs` are done; this
  structural half remains).

## Plotting

- [ ] **`LogarithmicClipAxis` label trim.** Edge tick labels can be trimmed at
  the plot boundary. Purely visual; needs a Windows render to reproduce before
  a fix can be verified — deferred with the other live-app checks.

## Custom controls (dark theme)

- [ ] **Per-paint Pen/Brush churn** — verified low value, left as is. Most of
  the dark-control paint brushes are genuinely state-dependent (Enabled / hover
  / pressed / focus), so only ~2 constant-palette pens per control could be
  cached; trading two allocations per (non-hot) repaint for app-lifetime static
  GDI handles isn't worth it. Revisit only if the palette becomes dynamic.
- [x] **Tools menu rebuilt on every open** — done. `ChromeTitleBar` builds the
  Tools drop-down once (the tab actions are fixed at wire-up) and re-shows it;
  the single menu is disposed with the control.

## Shell

- [ ] ★ **`Form1` is still the concurrency hub.** Mutable state on one object
  is touched by the UI thread, `Task.Run` plot builds and audio-callback
  events, guarded case by case with ad-hoc flags (`closingInProgress`,
  `closingPrepared`, `shutdownFastClose`). The first carve-outs are done: the
  calibration cache + warn-once bookkeeping (`MicrophoneCalibrationService`),
  the startup warm-up task/cancellation pair (`StartupAudioWarmup`), the
  settings-save debounce (`DebouncedSaver`), the Compare selection read by
  plot-build workers (`CompareSelection`), the record-button long-press
  state machine (`ButtonLongPressBehavior`), the current-measurement/history
  identity (`MeasurementSessionTracker`) and the per-mode overlay-slot memory
  (`ActiveOverlaySlotTracker`) own their state now. What remains on Form1 is
  the lifecycle/close flags (`closingPrepared`, `closingInProgress`,
  `resourcesDisposed`, `shutdownFastClose`, `updateCheckStarted`) and the
  transient UI bits (compare menu strip) — inherently form-bound; further
  slicing is optional polish rather than a concurrency risk.
- [ ] **`WireLiveApply` covers only dialog-open controls.** Controls created
  after wiring never get live-apply behavior. *Deferred to a Windows session:*
  the fix hooks `ControlAdded` recursively and re-enters the same apply
  debounce, so it needs a live check that dynamically-added rows (e.g. the
  crossover auto-setup channels) apply exactly once.

## Update path / installer

- [ ] **Uninstaller leaves settings behind.** Offer (or document) removal of
  the settings/history files on uninstall.

## External review tails (verified, deliberately deferred — designed changes, not patches)

Items from the four external review batches (July 2026, PR #24) whose claims
were verified against the code but whose fixes need their own design and
validation. Each entry records the verdict so the claim does not have to be
re-verified.

### Measurement / transfer layer

- [x] ★ **Dual-device Wave loopback is an untrusted timing reference but
  produces an indistinguishable `LoopbackTransfer` measurement** — closed
  radically (2026-07-11): the separate-loopback-device capability was REMOVED
  outright instead of being annotated. The microphone and loopback are now
  always channels of ONE input device (Wave stereo or ASIO), so every capture
  is sample-synchronous by construction and no timing-quality field is needed
  for this distinction. Deleted: `DualDeviceCapture`,
  `LoopbackSequencePairer`, `DualDeviceLevelCombiner`, the second recorder in
  both measurements, `WaveLoopbackDeviceNumber` end-to-end (Init params,
  settings DTO — old JSON files load fine, the unknown field is ignored and
  the loopback falls back to the shared device), and the "Wave loopback
  device" combo in the settings panel. Historical measurements captured with
  two devices by older versions remain indistinguishable in old files — noted
  under the provenance item below.
- [ ] **H1 has no excitation mask and an absolute epsilon.** Below the sweep
  band `Gxy/Gxx` divides noise by noise and the garbage bins ring back through
  the IFFT; the absolute epsilon's strength also varies with the average count.
  Fix: relative regularization + a soft excitation taper at the known sweep
  edges. Changes every measured transfer IR — needs validation against fresh
  raw captures (the test-data submodule stores only final IRs).

### Harmonics / THD

- [ ] ★ **THD+N is not THD+N: one FFT over the HD2..HD5 region sums the
  packets complex-wise** (phase-dependent, shifts change the result without
  changing energy). Fix: per-harmonic windows → per-harmonic spectra → move
  each onto the fundamental axis → sum energies; estimate noise separately and
  add it in energy. (The HDn display axis itself is fixed: curves now draw at
  the excitation frequency with calibration applied at the product frequency.)
- [ ] ★ **Harmonic curves and the primary curve have different reference
  levels** (sweep-deconvolution amplitude vs mic/loopback transfer), so their
  vertical distance is not a distortion percentage. Fix: HDn relative to the
  H1 linear packet of the *same* ESS decomposition; the transfer H1 stays as
  the primary display curve.
- [ ] **No overlap check between harmonic packets.** Long decay (bass, short
  sweeps, car cabins) leaks one packet's tail into the next window; the curves
  stay confident-looking. Fix: compute the available separation and warn when
  energy near the window edge has not fallen by a set margin.
- [ ] **No end-to-end ESS nonlinearity test.** The isolation test pins window
  geometry only. Fix: y = x + a2·x² + a3·x³ through a real ESS +
  deconvolution, assert HD2/HD3 frequency and level against the known a2/a3.

### Auto Crossover

- [x] ★ **The scalar summation model is physically wrong in three ways** —
  resolved (2026-07-11) after a design discussion with the user. Verdict: the
  magnitude/delay DECOMPOSITION is intentional and stays (a joint delay ×
  crossover search was rejected as intractable; the wizard assumes ideal
  alignment and Auto delay realizes it) — but the three inconsistencies were
  real and are fixed:
  (1) family-based amplitude-vs-power summation and the non-associative mixed
  sum are gone — channels now combine as a plain amplitude sum everywhere,
  the consistent expression of the ideal-alignment assumption;
  (2) the "magnitude-only objective cannot see which candidates Auto delay
  can actually fix" gap is closed by `ProposeRanked`: ~50 near-optimal
  candidates (per-junction top options crossed, plus a mandatory conventional
  all-LR24 run) are re-ranked by the junction loss ACHIEVABLE after the best
  per-junction delay, measured on the channels' IRs with the production
  alignment search (`FindAlignmentCandidates`) on a shared 32k direct-sound
  crop, in parallel; the conventional-24 candidate wins ties
  (`Conventional24PreferenceDb = 0.25`). Real-data cost: 50 candidates ≈ 3.8 s
  on a 4-core container (pool alone 0.4 s); the two cost cliffs found and
  fixed on the way: per-candidate arrival analyses (now computed once from
  the raw channels) and a frequency-independent search window (now
  ±clamp(1200/fc, 2, 12) ms — the filter group delay it must absorb scales
  as 1/fc);
  (3) the tests now include an independent amplitude-sum oracle (exact filter
  magnitudes, not `SummedResponseDb`'s internals).
  Also shipped in the same rework, per the user's spec: crossover frequencies
  search directly on a rounded lattice (5 Hz < 100 Hz, 10 Hz < 1 kHz, 50 Hz
  above — which also made the magnitude-cache hit, pool of 50 in ~0.4 s), and
  junctions below 300 Hz never get slopes steeper than 24 dB/oct (group
  delay). Deliberately NOT revisited: `AchievabilityWeight = 0.5` was chosen
  on one dataset (the left 4-way) — re-eyeball if field results disagree with
  the ranking. The dialog's async Apply path needs a live Windows check.
  **PR #27 review follow-up (fixed):** the first post-check draft (1)
  compared band-limited arrivals measured in DIFFERENT bands (each driver's
  own band) — the exact mistake the stereo Δ metric and the engine already
  warn about; the centers are now per-junction shared-band arrivals of the
  raw channels, cached by (channel, band) so the Hilbert cost stays bounded;
  (2) trusted the raw search winner — now the pick goes through
  `AlignmentSelection.Select` with the arrival anchor, the engine's
  arrival-anchored prior (sigma = window/4) and a widened edge retry, so an
  inverted half-period impostor cannot fake achievability the real Auto
  delay would refuse. Accepted simplifications vs the full engine (recorded,
  not hidden): no PHAT-seeded timeline, no cascade reprocessing of settled
  neighbors, no guarded wide-window promotion — junction deltas of a mono
  N-way compose independently, and the post-check only RANKS. Notable
  real-data effect of the honesty fix: on the left 4-way the conventional
  LR24 candidate's sub/woof junction penalty rose ~1 dB (its previous low
  loss came from an impostor lobe the selection now rejects) and LR12 at
  40 Hz wins the junction — consistent with the very group-delay argument
  that capped steep low slopes. The dialog also now catches ALL ranking
  exceptions (async void + WinForms context would otherwise kill the
  process), reporting them in a message box.
  **Second review round (fixed):** (1) per-junction pool options are each
  bounded against the descent optimum's neighbours, so combining them
  independently could jointly break the half-octave minimum separation —
  reproduced with a peaked middle driver (fc pair at ratio 1.375 < √2 in
  the ranked list); combinations now pass a joint separation check before
  the gain pass. (2) `IsConventional24` was derived from slopes alone, so
  any all-24 pool candidate (16 flagged in one config, pure Butterworth in
  another) could soak up the 0.25 dB tie preference; the flag now matches
  the dedicated conventional run's signature — exactly one candidate, LR24
  when LR is allowed. (3) The dialog freezes every ranking-input control
  while the async ranking runs, so stale settings can't be applied.
  **Matched slopes = one system slope (user decision 2026-07-12):** with
  "Independent slopes per side" off the whole system uses ONE dB/oct —
  every junction, both sides (families may still differ per junction). The
  old semantics (sides matched per junction, junctions free) put a 12 dB
  high-pass next to an 18 dB low-pass on one channel and read as broken.
  Implemented as one pinned (forcedSlope) descent+pool per practical slope,
  merged; the <300 Hz cap now also binds pinned runs, so a system-wide
  36/48 is infeasible while a low junction exists. Real-data winner:
  all-24 system (BW24@45 / LR24@250 / LR24@3850), pool 0.52 s, full ranked
  3.6 s. Preview and applied result can no longer disagree on slopes;
  the ranking may still move a crossover frequency by a lattice step
  relative to the preview (two-phase Apply remains an option if that
  still bothers in the field).
  **Target-curve gains (user request 2026-07-12):** gains no longer flatten
  the sum — they follow a car target curve. (1) midrange & tweeter levelled to
  each other (louder attenuated); (2) the subwoofer anchors the bass at a
  chosen elevation over the reference — a new **Sub level over mid/treble**
  dialog field, default & max = the measured elevation (sub at its raw level),
  trimmable down to flatten; (3) the remaining drivers fit cut-only onto the
  log-frequency slope between the sub anchor and the reference (a driver below
  the target keeps its level — dips are never boosted). Reference level = the
  quietest driver apart from the sub, so a hot sub can't drag it up and a
  sub-less 2-way still levels its two drivers. `ApplyTargetCurveGains` /
  `MeasuredSubElevationDb` replace the emitted gains after the crossover search
  (which still uses the optimizer's flattening gains to CHOOSE crossovers).
  Validated on the real left 4-way: default gives sub 0, midbass 0, mid 0,
  tweeter −1.5 (= the user's in-car manual tune, whose −4 on mid/tweeter is the
  separate L/R scene offset). Needs a live Windows check of the dialog field
  (layout shift, lazy default fill, freeze-during-ranking). Not yet done: a
  treble down-tilt knob (the preview "predicted sum span" is now honestly large
  because the bass is intentionally lifted).
- [ ] **`EstimateBand` merges disjoint islands into one band** (first/last bin
  above a global threshold): an isolated resonance above a dead gap extends
  HighHz and misclassifies the driver. Fix: the most significant contiguous
  segment above threshold with bounded gap tolerance.

### EQ Wizard

- [ ] ★ **No boostability/reliability mask**: the fitter sees only dB curves
  and will boost a deep interference null (wasting headroom and blocking an
  octave). Fix: mask from coherence + null depth/width + driver band; a
  cuts-only mode (sensible default for car tuning). (The clipping-profile
  half of this batch is fixed: `TotalGainMaxDb` caps preamp + band peak, the
  wizard passes 0.)
- [ ] **Band spacing ignores the chosen Q** (fixed ±0.33/±1 oct blocks);
  **gain is fixed before Q is searched** (over-corrects broad areas; already
  noted above with the missing polish pass); **the objective treats boosts and
  cuts symmetrically** (no boost/high-Q/band-count regularization). All three
  fold into the same redesign of the greedy loop: frequency × Q × gain search
  with width-based spacing and a boost-penalized score, then a
  coordinate-descent polish over all bands + preamp.

### Waterfall / Burst Decay

- [ ] **Wavelet time-support validity is not tracked**: at low frequencies the
  Morlet kernel outlasts the analysis window and the envelope is window-shaped,
  not system-shaped. The `Slice.SliceMinValidFrequency` metadata path already
  exists but always receives 0 — compute the frequency below which the kernel's
  effective support exceeds the window and mark/limit slices there. (The
  fabricated post-window decay, the pre-peak axis zero and the above-Nyquist
  grid are fixed.)
- [ ] **Waterfall renders nothing silently below 8 slices** (`RawSlices.Count
  < 8` guard in `WaterfallSeries.Render`): corrupted settings or narrow ranges
  show an empty plot with no explanation. Show a message (or clamp the
  controls so the state is unreachable).

### Time Alignment / unwrap (deferred from earlier batches)

- [ ] **GCC-PHAT confidence is peak height, not uniqueness**: a single
  spectral line or a narrowband subwoofer reads ~100% while the delay is
  poorly conditioned (bounded in practice by the ±0.1 ms anchored refinement
  window). Fix: fold RMS bandwidth / peak curvature / peak-to-second-peak into
  the confidence, or rename the figure. Needs its own validation pass on real
  measurements.
- [ ] **`WrapPeakPositions` cannot actually produce negative delays** — the
  peak search is capped at length/2, so the upper-half branch of
  `ToSignedDelaySamples` is unreachable (harmless for the loopback workflow,
  where delays are non-negative; the API promises more than it does).
- [ ] **Display smoothing includes low-reliability bins** (unwrap blanks long
  garbage stretches now, but short noisy nulls still enter `SmoothLinear` at
  full weight; magnitude curves behave the same). Optional: reliability-
  weighted smoothing.

### Batch 5 tails (Live Spectrum / sweep / EQ export / overlays / history / provenance)

- [ ] ★ **Measurement provenance model.** Files store no backend, timing
  quality, dropped-frame/clipping history, excitation range, effective average
  count or calibration id, and Time Alignment/Virtual DSP cannot see when a
  loaded measurement's absolute phase and delay are suspect. The sharpest
  offender (dual-device Wave capture) is gone — every NEW capture is
  sample-synchronous by construction — but files written by older versions
  with two devices still load indistinguishably, and the other provenance
  gaps stand. Consumers should declare their requirements (Time Alignment →
  synchronous timing required, HD → valid packet separation, ...).
- [x] ★ **Sweep runs are accepted unconditionally** — done (2026-07-11):
  every captured run now passes `SweepRunQualityCheck` BEFORE
  `accumulator.Add()`, judging only the unambiguous failures — microphone
  clipping (shared `FullScaleThreshold`), a silent microphone or loopback
  (peak < ~-80 dBFS; full-scale loopback stays the reference by the metering
  convention), and an undersized capture. The check judges the ENTIRE
  capture: both recorders reset per run (`StartRecordingAsync` →
  `ResetBuffers`), and the whole snapshot — including the pre-playback
  roll — feeds the deconvolution, so the checked range and the analyzed
  range match (PR #26 review catch: an earlier draft skipped the pre-roll
  and would have accepted a knock that still contaminated the IR). Policy: one
  automatic retry per bad run (record button shows "Retrying x/y"); a second
  failure skips the run. At the end, if the average holds fewer runs than
  requested, an informational modal lists per-run reasons
  (`SweepRunQualityReport.Describe`); if EVERY run failed, the measurement
  fails with the aggregated reasons instead of publishing garbage. Unit
  tests in `SweepRunQualityCheckTests` (Windows CI); the live retry flow
  needs a hands-on Windows check.
  **Rejected by the user (2026-07-11), do not resurrect:** the once-planned
  statistical outlier layer (peak-delay vs median, IR correlation vs a
  reference run) and the run pre-alignment rework (bounded shift +
  cross-correlation instead of the global sweep-IR peak) — verdict: the
  problem is contrived; the unambiguous checks plus the single retry cover
  the real field failure mode. Minor cosmetic tails left as-is, low value:
  the stored raw samples are only the LAST run's, and the Wave RMS meter
  integrates the lead-in/tail silence.
- [x] **Wave dual-device pairing is callback-ordered** — obsolete (2026-07-11):
  `LoopbackSequencePairer` and the whole dual-device live path were removed
  with the separate-loopback-device feature; the live transfer function now
  only ever sees channels of one device. (Live-spectrum drop-glue, cold-start
  γ²=1 and the mode-switch statistics mix were fixed earlier and stand.)
- [ ] **EMA coherence has no effective average count** (overlap-correlated
  frames, alpha-dependent memory): expose K_eff ≈ (2−α)/α (reduced for
  overlap) alongside the curve and feed it to the same debias the sweep path
  uses.
- [ ] **EQ Wizard fits the ANALOG peaking prototype while Virtual DSP and the
  exports run RBJ digital biquads** — ~3–4 dB apart at 18–20 kHz (48 kHz
  rate). Fit and preview should evaluate `PeakingBiquad.Compute` +
  `BiquadResponse` at the measurement rate; the analog model stays as an
  explicit choice. Needs its own validation (fits change near Nyquist).
- [ ] **miniDSP export needs a target-device profile**: the sample rate is now
  a constructor parameter surfaced in the format name (48 kHz default), but
  device biquad limits are not checked and the preamp burns a biquad slot
  instead of mapping to the device's gain control.
- [ ] **Overlay curves are assumed sorted/unique/finite in X**
  (`CalculateOperation`'s forward-only cursor): normalize imported overlays
  once (drop non-finite, sort, merge duplicate frequencies). (The branch-cut
  phase interpolation and the linear-Hz→log-f interpolation are fixed.)
- [ ] **History entries reference LIVE overlay slots** (`ActiveOverlaySlots`
  numbers into mutable global storage): restoring an old session shows
  whatever the slots hold TODAY. Store immutable overlay snapshots
  (content-addressed revisions) in the history entry.
- [ ] **History index lives beside the executable and dies silently**: no
  write-permission handling (`Save()` throws unguarded in Program Files-style
  installs), and any schema-version mismatch or parse error loads as an EMPTY
  list with no backup/notification — mirror the project-file
  backup-on-invalid policy, distinguish empty/unsupported/corrupted, and
  consider %LocalAppData%.
- [ ] **Virtual DSP history-source loading has the same stale-async race the
  EQ Auto Tune and history restore just got guards for**
  (`SelectHistoryEntryAsync` applies a slow snapshot over a newer selection):
  per-channel source revision or CancellationTokenSource.

### Batch 6 tails (calibration / generator / device lifecycle / autocorrelation / files / release)

- [ ] **Estimated 90° calibration looks real**: `Has(Degrees90)` is true when
  the 90° curve is approximated from the 0° file, and the UI labels it plainly
  "90 degrees". Label it "estimated from 0°" and record the fact in the
  measurement provenance (ties into the provenance model above).
- [ ] **Signal Generator materializes whole signals in memory** (mono array +
  full playback copy; ASIO always a stereo float copy): 600 s at 192 kHz is
  ~1.3 GiB, 384+ kHz worse. Needs a streaming IWaveProvider generating blocks.
  (The above-Nyquist sine refusal and the sample-rate-independent brown-noise
  corner are fixed.)
- [ ] **Subscriber exceptions escape into the real-time audio callbacks**
  (`LevelsAvailable`/`SequenceReady`/... invoked directly from ASIO/Wave
  callbacks): one throwing UI subscriber can kill the driver. Route through a
  bounded dispatcher or isolate each subscriber.
- [ ] **Autocorrelation windows are sample-count-fixed** (offset 64, length
  2048, 3 ms display): the physical analysis window shrinks 4× at 192 kHz and
  the promised 3 ms does not even exist at 768 kHz. Parametrize in
  milliseconds. The /correlation[0] normalization is the standard BIASED
  estimator — fine for display, but note it under-reads long-lag periodicity;
  an N−k (or overlap-energy) normalization is the alternative if the mode is
  ever used for periodicity detection.
- [ ] **Measurement files validate only after full deserialization**: a
  crafted file can declare hundreds of millions of samples and hit OOM before
  `Validate()` runs. Add a file-size cap before parsing, a max-samples cap,
  and `OutOfMemoryException` handling. (The coherence-length ↔ transfer-IR
  consistency check is in.)
- [ ] **Release toolchain is unpinned** (`choco install innosetup`, latest
  NetSparkle appcast tool, actions by major tag): pin exact versions (and
  SHAs for actions) once the current-good versions are confirmed — an
  unverified pin would break the release instead. (The shell-injection
  surface, the branch-vs-tag build mismatch and auto-published AI notes are
  fixed: inputs go through env + strict regex, every job checks out the tag,
  the release is created as a draft.)

### Live Spectrum cold-start (batch 7 tails)

- [ ] ★ **The ASIO/Wave capture callback still allocates on the audio thread**:
  sequence extraction builds jagged float arrays + a List and invokes
  subscribers inline; the first pass through that branch (JIT + allocation)
  can overrun a 64–128-sample ASIO budget. Target shape: callback → convert
  into a preallocated SPSC ring slot → return; a background thread reframes/
  FFTs from the ring. (`ArrayPool` is the halfway option but the sequence
  arrays flow into the Channel with unclear return discipline.)
- [ ] **Level meter allocates a fresh `AudioChannelLevel[]` per callback**
  (up to ~750/s at 64-sample buffers): accumulate peak/sumSquares in the
  callback and snapshot at 20–30 Hz.
- [ ] **First live plot frame is heavy on the UI thread** (snapshot clones +
  RTA computed even when hidden + first resample + OxyPlot series/capacity
  growth): compute the RTA magnitude only when `ShowInputMagnitude`, and
  consider pre-building the series before playback starts. A lock-free
  callback probe ring (duration vs ASIO budget + allocated bytes) is the
  measurement tool if hitches persist after the one-period buffer and the
  DSP warm-up.
