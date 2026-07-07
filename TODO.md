# TODO — tech debt from the full code review

Items found during the two review passes (PR #1, PR #2) that were deliberately
reported instead of fixed. Grouped by area, highest-value items marked ★.

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
