# TODO — tech debt from the full code review

Items found during the two review passes (PR #1, PR #2) that were deliberately
reported instead of fixed. Grouped by area, highest-value items marked ★.

## DSP library (`dsp/`)

- [ ] **`CalibrationFile` does filesystem I/O in the dsp layer.** The silent
  failures are fixed (`LoadError` is surfaced in the options panel and once per
  session at plot time), but aligning the class with the "parser accepts text"
  pattern of the EQ formats would still simplify error handling and tests.
- [ ] **`EqAutoTuner` greedy fit has no polish pass.** Band gain is fixed from
  the residual at the peak before Q is chosen (no joint gain/Q optimization),
  there is no final coordinate-descent pass (`CrossoverAutoSetup` already has
  one to borrow), and the preamp is rounded to integer dB *before* the fit.
- [ ] **`CrossoverAutoSetup.Optimizer.Score()` recomputes every channel** on
  each junction/gain trial (~300–1500 calls per junction). Filter magnitudes
  are cached, but the amplitudes of untouched channels are not.
- [ ] **Naive neighbor-to-neighbor phase unwrap** (`DataHelper`): one noisy
  bin shifts the whole tail by ±2π; the 100 Hz threshold softens but doesn't
  cure it. Consider magnitude/coherence-weighted unwrapping.
- [ ] **Duplication:** the coherence formula lives twice
  (`SpectrumAnalysis.ComputeCoherence` vs inline in
  `TransferFunction.ComputeAveragedRelativeIr`); `DspChannelChain.Response`
  re-implements `PreparedDspResponse.Create`;
  `SweepAnalysis.DeconvolveWithInverseFilter` has three overloads with manual
  float→double conversion; the single-frame `TransferFunction.ComputeRelativeIr`
  is dead outside tests.
- [ ] **`FrequencyResponseOptions` is a grab-bag** mixing FR windows,
  phase/group-delay gates and a dozen UI visibility flags (`ShowHd2`,
  `ShowGroupDelay`, …) — presentation flags leaked into the DSP library.
- [ ] **`DataHelper` is a ~1160-line god-object**; split into
  Spectrum/Phase/Impulse/Resampling modules.
- [ ] **Undocumented magic numbers in harmonic analysis** (`DataHelper`):
  window bounds `h+0.03`/`h−0.5`, THD window `5.5…1.5`, silent doubling of
  harmonic smoothing. Document or name them.

## Virtual DSP / Time Alignment

- [ ] ★ **Extract `ComputeAutoAlignment` into a testable service.** The
  two-stage Auto-delay orchestrator (~250 lines in `VirtualCrossoverPanel`:
  retries, wide-window promotion, candidate tie-breaks) is untested panel
  code. Highest-value extraction left from the PR #1 review.
- [ ] **Time Alignment analysis is not cached** — `RefreshAnalysis`
  (`TimeAlignmentPanelController`) recomputes Hilbert + GCC-PHAT on every tab
  show even when inputs are unchanged. Needs a live-app check to avoid stale
  display.
- [ ] **Crossover wizard breaks at 150 %+ DPI**
  (`VirtualCrossoverAutoSetupDialog`): extra channel rows use hardcoded pixel
  offsets (`RowTop = 42`, `RowStep = 28`), so rows 4–8 can overlap scaled
  designer controls. Verify on Windows and switch to layout-panel positioning.
- [ ] **Project-file version has no migration path**
  (`VirtualCrossoverProjectFile.Validate`): any version mismatch throws, so
  after a future format change the autosave is silently replaced. Decide on a
  migrate-or-backup policy.
- [ ] **Split `VirtualCrossoverPanel` (~2.9k lines)** into partial
  classes/controllers (after the `ComputeAutoAlignment` extraction).
- [ ] **`BuildPhasePoints` duplicates the Tukey gate construction** of
  `DataHelper.ExtractGatedWindowedImpulse` and will drift from Phase mode.
- [ ] **Small duplication:** `ChannelName` exists twice (panel vs
  `VirtualCrossoverSheet`), `Signed`/`Number` formatters twice (Sheet vs
  SheetPdf).
- [ ] **File-format selection by `FilterIndex`** (`LoadPeq`,
  `ExportTuningSheet`) is implicitly coupled to `EqProfileFormats.All` order;
  reordering the list silently breaks the dialogs.
- [ ] **`DelayTableText` parses rendered fixed-width columns** (18/37 chars)
  for copy-values instead of holding a value model (format+parse are at least
  co-located and tested now).

## Measurement orchestrators

- [ ] **`CurrentLevels` torn reads.** `ExpSweepMeasurement.CurrentLevels` (an
  ~50-byte `InputLevelMeterSnapshot` struct) is written from audio worker
  threads and read from the UI without synchronization; a torn read can show a
  momentarily inconsistent meter (display-only; same family as the
  `UpdatePeakInfo` item below).
- [ ] **`RestoreImpulseResponse` regenerates the whole sweep.** Restoring a
  measurement from a file or history calls `Init` → `Sweep.FillData`, which
  synthesizes the full sweep and inverse filter just to satisfy
  `HarmonicIROffset`; playback data isn't needed until the next run. Make the
  generation lazy.
- [ ] **New `AsioFullDuplexSession` per averaging run.** Every run of an
  averaged sweep re-initializes the ASIO driver; slow drivers add seconds per
  run. Reuse one session across the run loop.
- [ ] **Third copy of the level-metering math.** `ChannelLevelAccumulator`
  (peak/RMS/dB + 0.999 full-scale threshold) duplicates
  `AudioLevelMetering` and the recorder metering loops.

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
- [ ] **Five IR-preview panels are one copy-pasted class.** `FROptions`,
  `GDOpt`, `PROpt`, `BDOpt`, `WaterfallOptions` duplicate the
  `ImpulseResponseChanged` subscribe/unsubscribe dance, the `BeginInvoke`
  marshal and the clamping helper — and have already drifted (only `GDOpt`
  suppresses the 3–6 redundant preview renders during `Init`). Extract a
  shared base.
- [ ] **`SetOptions` reads bits from the measurement, not the control.**
  Three sources of truth for the sample size in `MeasurementOptions`; harmless
  while the control is read-only, a silent no-op the day it is enabled.
- [ ] **`TukeyWindowControlHelper` clamps are irreversible.** Shrinking the
  window length clamps the fade values (semantically required, and visible in
  the controls), but growing it back does not restore them; a shadow-value
  restore like the loopback-channel one would make the clamp reversible.
- [ ] **`LiveSpectrumOpt` shadow fields update only on
  `SelectionChangeCommitted`.** Re-verified: the R reset button raises
  `SelectionChangeCommitted` too, so no current path loses a change — this is
  fragility for future programmatic writes, not an active bug. Also three
  identical floor-index loops worth one helper.

## UI chrome

- [ ] **`ChromeTitleBar` caches the DPI scale once at `Initialize`.** No
  `DpiChanged` handling: moving the window to a monitor with different DPI
  (PerMonitorV2) leaves the bar height, button widths and tab layout at the
  old scale. Refresh the cached metrics and re-run layout on DPI change.
- [ ] **Top-edge resize grip still dead over child controls.** The
  HTTRANSPARENT fix covers the title-bar panel itself; points over the tab
  buttons and window buttons (which reach y=0) still hit-test as client.
  Standard chrome resolves this per child; low value, document or fix later.
- [ ] **`ColorPickerDialog` preview panel is not double-buffered** and the
  hue slider repaints its full rainbow per mouse-move; caching the rainbow in
  a bitmap (invalidated on resize) would finish what the pen-reuse fix
  started.

## PDF export

- [ ] **~90 duplicated lines between the two PDF exporters.**
  `LoadBanner`/`AddImage`/`AddFilterCards` are byte-identical in
  `TuningSheetPdf` and `VirtualCrossoverSheetPdf`; extract a shared helper.
  MigraDoc 6 supports `AddImage("base64:...")`, which would also remove the
  temp-file dance entirely.
- [ ] **No deconvolution flatness test.** The sweep + inverse-filter math was
  verified numerically during review (in-band ripple < 0.01 dB), but nothing
  in the test suite pins it; a `SyntheticMeasurement`-style flatness test
  would lock the `2/N` scaling and envelope compensation down.

## Audio capture layer

- [ ] **Merge the `SoundRecorder` / `AsioFullDuplexSession` twins.** The
  accumulator core is shared, but the paired waiter classes, stop-timeout
  machinery and events (~150 lines) are still duplicated.
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
- [ ] **Dual-device level-binding duplication** —
  `HandleMicrophoneOnlyLevels`/`HandleLoopbackOnlyLevels`/
  `RaiseCombinedDualDeviceLevels`/`CreateEntry` nearly verbatim in both
  `ExpSweepMeasurement` and `NoiseMeasurement`.
- [ ] **`GetSamplesSnapshot` copies the whole buffer under the callback
  lock**; safe only because it's called after `StopAsync`, which the contract
  doesn't enforce.

## Overlays

- [ ] ★ **Cache the target-overlay render path.** `Overlay.cs` rebuilds the
  constant target/tolerance curves on every live tick (~30 fps) even though
  they only change when the user edits the target. Cache the built curves and
  invalidate on edit. The clean way in: extract the pure curve math out of
  `Overlay.cs` into a testable class, and introduce an `OverlaySlotState`
  record to replace the triple field-mapping between overlay, slot file and
  UI state.
- [ ] **Slot files re-read per mode switch.** All 12 overlay slot JSON files
  are re-read from disk on every mode switch; cache them and reload only on
  external change.

## Plotting

- [ ] **Live-path allocation churn.** The live spectrum rebuilds fresh series
  objects plus several list copies on every 30 fps tick; reuse series and
  update points in place.
- [ ] **`LogarithmicClipAxis` label trim.** Edge tick labels can be trimmed at
  the plot boundary.
- [ ] **Dead `FourierCSD` enum value.** Unused member of the transform enum;
  remove or implement.

## Custom controls (dark theme)

- [ ] **Per-paint Pen/Brush churn.** Dark controls allocate pens/brushes in
  every `OnPaint`; cache per-color instances.
- [ ] **`DarkComboBox` keyboard/focus gaps.** Swallows Enter (so a dialog's
  AcceptButton never fires from a combo) and doesn't take focus on click.
- [ ] **`DarkNumericUpDown.BeginInit` is a no-op.** Designer property order
  can clamp `Value` against a not-yet-set `Minimum`/`Maximum`; implement real
  init batching.
- [ ] **Tools menu rebuilt on every open.** Rebuilding the drop-down each time
  is wasted work and loses per-item state; build once and update.

## Shell

- [ ] **`UpdatePeakInfo` unsynchronized pair read.** Peak value and peak index
  are read as two separate volatile-ish reads and can disagree; publish them
  as one immutable pair.
- [ ] **Options dialogs leak handlers on handle-less close.** `FormClosed`
  unsubscribe never runs when a dialog is disposed without having shown;
  unsubscribe in `Dispose` as well.
- [ ] **`WireLiveApply` covers only dialog-open controls.** Controls created
  after wiring never get live-apply behavior.
- [ ] **`CloseReason.WindowsShutDown` ignored.** `Form1_FormClosing` cancels
  the close to run async teardown even during OS shutdown; detect shutdown and
  do a fast synchronous flush instead.

## Update path / installer

- [ ] **Prerelease identifiers ignored in version comparison.**
  `1.2.0-rc.1` → `1.2.0-rc.2` does not prompt for an update; compare full
  SemVer including prerelease ids.
- [ ] **Uninstaller leaves settings behind.** Offer (or document) removal of
  the settings/history files on uninstall.
