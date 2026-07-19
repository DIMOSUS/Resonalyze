# HANDOFF — remaining TODO work, split by assistant

Working notes for continuing the `TODO.md` register. Blocks 1–7 (full review,
correctness fixes, performance, dedup, Form1 decomposition, weighted phase
unwrap) are merged into `main`. `TODO.md` is the source of truth for what is
left; this file says **who** should take **what** and **how** to work.

## Environment crib (read first)

- `global.json` pins an SDK the container may not have — if `dotnet` fails on
  the pin, run it with a working directory **outside the repo** (absolute
  paths to the solution) instead of editing `global.json`.
- Build everything with `-p:EnableWindowsTargeting=true` on Linux:
  `dotnet build source/Resonalyze.sln -c Release -p:EnableWindowsTargeting=true`.
- `tests/Resonalyze.Dsp.Tests` runs locally (deterministic and synthetic).
  `tests/Resonalyze.App.Tests` compiles
  locally but **runs only on the Windows CI** — push a PR to execute it.
- Workflow used so far: verify every TODO claim against the code before
  fixing → fix → build + dsp tests → update `TODO.md` honestly (record
  deliberate non-fixes and residuals) → commit → PR → squash-merge on green
  CI. Deviations from current behavior ("drift alignments") are called out
  explicitly in the PR body.

## Take now (mechanical, locally verifiable) — ordered easiest first

1. **Deconvolution flatness test** — pure dsp test pinning the `2/N` scaling
   and envelope compensation (`SyntheticMeasurement`-style; in-band ripple
   was verified at < 0.01 dB during review).
2. **DSP duplication**: coherence formula lives twice
   (`SpectrumAnalysis.ComputeCoherence` vs inline in
   `TransferFunction.ComputeAveragedRelativeIr`); `DspChannelChain.Response`
   re-implements `PreparedDspResponse.Create`;
   `SweepAnalysis.DeconvolveWithInverseFilter` has three overloads with
   manual float→double conversion.
3. **Magic numbers in harmonic analysis** (`DataHelper`) — name/document
   `h+0.03`/`h−0.5`, THD window `5.5…1.5`, silent smoothing doubling.
4. **`OverlaySlotState` record** — replace the triple field-mapping between
   overlay, slot file, and UI state.
5. **`CalibrationFile` → "parser accepts text"** — align with the
   `IEqProfileFormat` pattern; keeps `LoadError` semantics.
6. **Options-panel small items**: `SetOptions` reads bits from the
   measurement instead of the control; `TukeyWindowControlHelper` shadow-value
   restore (mirror the loopback-channel fix); `LiveSpectrumOpt` three
   identical floor-index loops → one helper; persist the preferred loopback
   offset separately from the effective one.
7. **Split `DataHelper` (~1200 lines)** into Spectrum/Phase/Impulse/Resampling
   modules — *move-only*, zero behavior change; the dsp tests must pass
   untouched.
8. **`FrequencyResponseOptions` grab-bag** — move the UI visibility flags
   (`ShowHd2`, `ShowGroupDelay`, …) out of the dsp layer; many call sites,
   all compiler-checked.
9. **UI polish batch**: per-paint Pen/Brush caching in the dark controls;
   build the Tools menu once; `ColorPickerDialog` rainbow bitmap cache;
   split `VirtualCrossoverPanel` (~2.6k lines) into partials; `DelayTableText`
   value model; surface the silent project-file `.backup` rename;
   `WireLiveApply` for late-created controls.

## Leave for the next Fable session

- **`EqAutoTuner` polish pass** — joint gain/Q optimization, a final
  coordinate-descent pass (borrow from `CrossoverAutoSetup`), preamp rounding
  *after* the fit. Success is measured in residual dB against the current
  tuner, not in green builds.
- **`CrossoverAutoSetup.Optimizer.Score()` caching** — cache invalidation
  inside an optimizer; a slightly stale score silently degrades convergence.
  (Acceptable for Opus only with a strict "cached == uncached, bit-identical
  on a fixed scenario" test.)
- **WASAPI migration** of the Wave backend + the final recorder merge that
  depends on it (also needs hardware).
- A review pass over the merged mechanical blocks.

## Needs Windows / hardware (any assistant, deferred)

ASIO channel-window conversion and averaged-session verification; Time
Alignment analysis caching (live staleness check); crossover-wizard DPI and
`ChromeTitleBar` DPI; `LogarithmicClipAxis` label trim; PDF base64 images
(pixel-identical check); `LevelsAvailable` stall check; top-edge resize grip;
uninstaller settings cleanup; a visual pass on the new phase unwrap with a
noisy averaged measurement (the tail should no longer jump by 360° past deep
nulls).
