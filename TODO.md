# TODO — tech debt from the full code review

Items found during the two review passes (PR #1, PR #2) that were deliberately
reported instead of fixed. Grouped by area, highest-value items marked ★.

## DSP library (`dsp/`)

- [ ] **`CalibrationFile` swallows a missing/unreadable file.** `HasData` makes
  it detectable, but nothing surfaces an error to the user — measurements just
  run uncalibrated. Add a `TryLoad`-style factory that reports the failure.
  Related: this is the only dsp class doing filesystem I/O; aligning it with
  the "parser accepts text" pattern of the EQ formats would simplify both
  error handling and tests.
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
- [ ] **Calculated A−B clamps instead of gapping.** Amplitude-space
  subtraction clamps negative results to −160 dB; a NaN gap in the curve would
  be honest instead of drawing a floor.
- [ ] **Corrupt slot files fail silently.** A corrupt slot JSON presents as an
  empty slot and can be silently overwritten; surface a warning and keep the
  damaged file (e.g. rename to `.corrupt`).

## Plotting

- [ ] **Live-path allocation churn.** The live spectrum rebuilds fresh series
  objects plus several list copies on every 30 fps tick; reuse series and
  update points in place.
- [ ] **`fftLength` off by 2.** Reconstructing the FFT length from the
  coherence array length is off by 2, giving a systematic (tiny) frequency
  skew in the live transfer function; pass the real FFT length through instead
  of deriving it.
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
