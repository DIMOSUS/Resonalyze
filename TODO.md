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

- [ ] ★ **Dual-device Wave loopback is an untrusted timing reference but
  produces an indistinguishable `LoopbackTransfer` measurement.** Independent
  clocks mean an arbitrary inter-device offset (changes per run by a buffer)
  plus drift as a frequency-dependent phase error; Time Alignment still offers
  fractional-millisecond read-outs. Fix: a timing-quality field
  (sample-synchronous / shared-device / independent-devices) through
  `ExpSweepMeasurement`, history snapshots and the IR file format, with the
  Time Alignment panel warning or refusing on independent devices, and phase/GD
  marked timing-uncertain.
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

- [ ] ★ **The scalar summation model is physically wrong in three ways**:
  family-based amplitude-vs-power summation (the real sum depends on the
  complex phase at each frequency, not the family name), sequential mixed
  summation that is non-associative for 3+ channels, and a magnitude-only
  objective that ignores the measured phase entirely (Auto delay afterwards
  cannot fix mismatched slopes/orders with one delay). The optimizer's live
  preview shares the model, so it confirms its own objective; the flatness
  tests call the same `SummedResponseDb` and are tautological. Fix: complex
  per-channel responses (measured IR × exact digital filter response) with at
  least a final complex-sum check per candidate, plus an independent
  complex-sum oracle in the tests.
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
