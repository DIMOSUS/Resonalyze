# HANDOFF — how to work this repo, and what suits whom

`TODO.md` is the register of what is left. This file is not a second one: it
holds the environment facts a fresh session needs, the workflow that produced
the merged work, and a note on which of the remaining items suit which kind of
session.

Last reconciled with `TODO.md` and the code on 2026-07-22 (tip `255de70`).

## Environment crib (read first)

- `global.json` pins SDK 10.0.301 (`rollForward: latestPatch`) — a container may
  not have it. If `dotnet` fails on the pin, run it with a working directory
  **outside the repo** (absolute paths to the solution) instead of editing
  `global.json`.
- Build everything with `-p:EnableWindowsTargeting=true` on Linux:
  `dotnet build source/Resonalyze.sln -c Release -p:EnableWindowsTargeting=true`.
- `tests/Resonalyze.Dsp.Tests` targets `net10.0` and runs locally — it is
  deterministic and synthetic, and it is where a behavior claim gets pinned.
  `tests/Resonalyze.App.Tests` and `tests/Resonalyze.Audio.Tests` target
  `net10.0-windows`: they compile locally but **run only on the Windows CI**, so
  push a PR to execute them. The audio suite's hardware cases are excluded there
  by `--filter "Category!=Hardware"` — nothing in CI touches a real device.
- Performance work starts with a profile, not with a reading of the code: build
  `-c Tracy` and profile the actual scenario before optimizing. A scratchpad
  harness must `ProjectReference` the dsp project, never a `HintPath` to a built
  DLL.

## Workflow

Verify every `TODO.md` claim against the code before fixing — the 2026-07-22
audit found four items already closed and five described wrongly after just four
days and 23 commits of drift → fix → build + dsp tests
→ update `TODO.md` honestly, recording deliberate non-fixes and residuals →
commit → PR → squash-merge on green CI. Deviations from current behavior are
called out explicitly in the PR body.

Two register conventions worth keeping: an item that describes work already done
keeps only its residual, and a settled decision stays as `[✗]` so the same idea
is not re-proposed later.

## What suits which session

The mechanical block this file used to enumerate is essentially finished: the
deconvolution flatness test, the coherence dedup, the harmonic-analysis
constants, the `CalibrationFile` text parser, the `DataHelper` split, the
`FrequencyResponseOptions` carve-out and most of the UI polish batch are in
`main`. Two deliberate non-fixes from that batch, recorded here so they are not
rediscovered as oversights: the dark controls still allocate `Pen`/`Brush` per
paint (a micro-optimization nobody has profiled — Tracy first, and only if a
repaint actually shows up), and `DelayTableText` still parses its own rendered
columns (dropped for low payoff). What is left in `TODO.md` falls into three
kinds.

- **Judgment-heavy, measured in dB rather than green builds.** The EQ Wizard
  redesigns (shelf filters ★, the greedy-fit rework, spatial averaging) and the
  Auto delay modelling items (scene-lock tolerance, polarity margin, promotion
  thresholds from comb statistics). These need a real measurement to argue
  against, and the field data lives outside the repo — the synthetic suite pins
  regressions, it does not decide these.
- **Mechanical and locally verifiable.** `OverlaySlotState`, overlay-curve
  normalization, the surviving DSP chain-assembly duplication
  (`DspChannelChain.Response` vs `PreparedDspResponse.Create`), the
  `VirtualCrossoverPanel` residual boundaries (it has grown back to ~3900 lines
  since the last split), the waterfall <8-slice guard and the wavelet
  time-support validity.
- **Gated on Windows or hardware.** ASIO channel-window conversion and the
  averaged-session run on a real (ideally slow) driver; Time Alignment analysis
  caching, which needs a live staleness check; `ChromeTitleBar` DPI across
  mixed-DPI monitors; `LogarithmicClipAxis` label trim; PDF base64 images with a
  pixel-identical render check; `TukeyWindowControlHelper` clamp reversibility;
  `WireLiveApply` for late-created controls; uninstaller settings cleanup.

Two long-standing entries are gone rather than deferred: the WASAPI migration
shipped (shared + exclusive backends alongside the legacy MME one), and the
recorder merge that was waiting on it is dropped — the migration landed without
ever needing the common device abstraction that was its justification.
