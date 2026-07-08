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
- [ ] **Split `VirtualCrossoverPanel` (~2.6k lines)** into partial
  classes/controllers.
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
