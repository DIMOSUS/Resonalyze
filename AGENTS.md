# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Project Overview

Resonalyze is a Windows desktop application (WinForms, .NET 10) for acoustic measurements: impulse/frequency response, loopback-referenced time alignment, live transfer functions, EQ design, and virtual DSP crossover simulation. The SDK version is pinned in `global.json`.

## Commands

```powershell
dotnet restore source/Resonalyze.sln
dotnet build source/Resonalyze.sln --configuration Release
dotnet run --project source/Resonalyze.csproj

# All tests
dotnet test source/Resonalyze.sln -c Release

# One test project
dotnet test tests/Resonalyze.Dsp.Tests/Resonalyze.Dsp.Tests.csproj

# One test class or method
dotnet test tests/Resonalyze.Dsp.Tests/Resonalyze.Dsp.Tests.csproj --filter "FullyQualifiedName~TransferFunctionTests"

# Performance profiling build (defines TRACY_ENABLE, references Tracy-CSharp)
dotnet run --project source/Resonalyze.csproj -c Tracy
```

Platform constraint: `source/` (the app), `audio/Resonalyze.Audio`, `tests/Resonalyze.Audio.Tests/` and `tests/Resonalyze.App.Tests/` target `net10.0-windows` and only build/run on Windows (WASAPI/ASIO/MME are Windows-only). `dsp/` and `tests/Resonalyze.Dsp.Tests/` target plain `net10.0` and are cross-platform — on a Linux environment, only the DSP library and its tests can be built and run.

## Architecture

Three projects, with deliberate boundaries (`Dsp` ⟂ `Audio`; the app depends on both):

- **`dsp/Resonalyze.Dsp`** — pure, UI-free signal-processing library. Depends only on MathNet.Numerics and YamlDotNet. Contains FFT/spectrum analysis, windowing, transfer functions, minimum phase, excess delay, time-alignment analysis, biquad/crossover filters, the EQ auto-tuner, and PEQ profile import/export formats (Equalizer APO, REW, MiniDSP, CamillaDSP, EasyEffects, generic CSV — all implementing `IEqProfileFormat`). Every `PeqBand.Q` in the library is RBJ-cookbook Q, which is what `PeakingBiquad` realizes and what the fitting, previews and profile formats all assume; `PeqQConventions` restates a band for a device that reads Q as Symmetric (Zölzer/DAFX) or Classic, and is applied only where numbers leave for such a device (the tuning sheets), never to the internal representation. The conventions are one filter family differing by a Q scale of `10^(±gain/40)`, so conversion is exact — `PeqQConventionTests` pins it against an independently written Zölzer section and against REW's published half-gain bandwidths. The app hands measurement data to this layer through `IImpulseMeasurement` (impulse response + peak index + sample rate).

  **Two sample rates, deliberately apart.** A record has the rate it was MEASURED at; a chain is realized at the rate the target PROCESSOR runs (`DspProcessorProfile`, one line per device in `DspProcessorCatalog`, carried by a Virtual DSP project and by a PEQ handoff). `PreparedDspResponse.Create` takes the processor's rate — the bilinear transform warps every corner by the rate it was designed at, so building a chain at the measurement's rate simulates filters no device produces — while `ApplyToSpectrum` and `VirtualCrossoverAnalysis.ApplyChain` take the record's rate for the bin grid. Passing one rate for both is only correct when they genuinely agree: a 48 kHz sound card measuring a 96 kHz processor is a supported, ordinary case. Anything that reads measured CONTENT (windows, gates, arrivals, metrics) belongs to the measurement's rate; anything that BUILDS a filter or states numbers for the device (previews, exports, the auto-tuner, the crossover optimizer) belongs to the processor's.
- **`audio/Resonalyze.Audio`** — owns all audio drivers/devices (WASAPI Shared/Exclusive, ASIO, MME), format negotiation, capture/playback lifecycle, PCM decoding, diagnostics and warm-up. NAudio is confined here (declared `PrivateAssets="compile"` so `using NAudio` does not compile in the app). Low-level device types are `internal`; the measurement layer talks only to the neutral abstraction: `IAudioSessionFactory` + `IAudioDuplexSession`/`IAudioStreamingSession`/`IAudioPlaybackSession` and backend-neutral DTOs (`AudioSessionRequest`, `AudioPlaybackSignal`, `AudioCaptureResult`, `AudioSessionDiagnostics`, `AudioEndpointDescriptor`, `AudioFormat`, `PlaybackChannel`). Backends are chosen by the persisted `AudioBackend` enum inside `AudioBackendRegistry` (the only backend dispatch — no `switch (AudioBackend)` in `source/`).
- **`source/Resonalyze`** — the WinForms app: composition root, measurement lifecycle, and plotting.

Inside `source/`, the flow is: signal generation (`Measurements/` — `ExponentialSineSweep`, `NoiseSignal` produce float data only) → an audio session opened via `IAudioSessionFactory` (composition root in `Shell/Form1` builds `AudioBackendRegistry.CreateDefault()` and injects the factory into `ExpSweepMeasurement`, `NoiseMeasurement`, the signal generator and warm-up) → analysis via the Dsp library → plot presentation (`Plotting/` — `PlotModelFactory` builds OxyPlot models, `OxyPlotAdapter` hosts them). Microphone and loopback are always channels of ONE input device, so timing stays sample-synchronous.

Key structural points:

- **`Shell/Form1` is the hub**, split into partial classes by concern (`Form1.Measurement.cs`, `Form1.Plotting.cs`, `Form1.History.cs`, `Form1.Compare.cs`, etc.). The `Mode` enum in `Form1.cs` defines all analysis modes (frequency/phase/group delay/waterfall/burst decay/live spectrum/time alignment/EQ wizard/signal generator/virtual crossover); `ModeSwitching/ModeController` orchestrates tab switches.
- **`Options/`** holds one settings panel per mode (`FROptions`, `IROpt`, `GDOpt`, ...), docked into the shell via `Shell/DockedModeSettingsHost`.
- **`Tools/`** contains the larger feature panels: EQ Wizard, Signal Generator, Virtual DSP (`VirtualCrossoverPanel` + project file persistence), and PDF tuning-sheet export (PDFsharp/MigraDoc). `EqWizardPanel` is self-contained: it owns its source, uses a mode-local target curve and persists through `MeasurementSettingsFile.EqWizard`. It picks that source itself — an impulse response (file or history), a captured overlay slot, or a text curve — through `EqWizardSourceResolver`, which reads overlay slot FILES and history snapshots as one-time imports. Keep it that way: the panel must not reach into the live `OverlayCollection` or the current measurement, and an imported curve is a snapshot with no link back to what it came from.
- **`Plotting/` owns the plot INTERACTION as well as the models.** `PlotInteraction.Enable` installs `PlotGestureController` on every `PlotView` in the app, and that controller is the single place the mouse/keyboard map lives — it is shaped after REW's graph panel (wheel zooms both axes, Shift/Ctrl or the pointer over an axis restricts it, the end of an axis moves one limit, middle-drag is a variable zoom, double click opens `Ui/Dialogs/GraphLimitsDialog`), and the `REFERENCE.md` table under "Graph Zoom and Limits" is the user-facing copy of it. Keep new gestures there rather than on individual views. `PlotViewportMemory` carries a plot's zoom across the constant model rebuilds — the main plot keyed per `Mode`, the two Time Alignment previews one memory each — by asking the axes which ranges a user forced on them (`PlotAxisViewport.CaptureOverrides` resets an axis, reads what the model computes on its own, and puts the override back). Do NOT replace that with a baseline captured when a model is shown: overlays join a plot afterwards — a mode switch restores its slots after `ModeController` has drawn, Show All later still — and on an auto-scaled axis they widen the range through the data, which a baseline comparison reads as a zoom. An axis the user moved is restored; an untouched one is left to the new model, so `RaiseDecibelViewCeiling` and the group-delay auto-fit still work. A setting that changes what an axis MEANS calls `Forget`.
- **`Overlays/`** manages persistent overlay slots and calculated (math) overlays; **`History/`** persists measurement snapshots with per-entry working state.
- Update checking uses NetSparkle + `Settings/GitHubReleaseChecker`.

### Accessibility is not an external contract

`Resonalyze.Dsp` and `Resonalyze.Audio` are separate assemblies for the sake of
the dependency boundaries above, **not** because they are distributed. Nothing
packs them: `release.yml` only runs `dotnet publish source/Resonalyze.csproj`,
and the DLLs ship inside the app's installer. There is no supported external
API and no downstream consumer to deprecate for.

So `public` on a member of those assemblies means "reachable from the app or its
tests", not "part of a contract". An unreferenced member is dead code and is
deleted outright — no `[Obsolete]` cycle, because there is nobody to deprecate
for. Judge deadness across the whole solution (the tests count as a consumer),
and prefer `internal` for anything a new member does not need to expose beyond
its own assembly; both projects grant `InternalsVisibleTo` to their test
project, so `internal` costs no coverage.

**The exception is a deliberate reserve.** `Resonalyze.Dsp` and
`Resonalyze.Audio` are libraries in shape even if not in distribution, and a
small, self-contained primitive may be worth keeping for work that is coming
(`MinimumPhase.FromSpectrum`, `HarmonicWindowDefinition.NominalLength`,
`AudioBackendDescriptor.Supports`, `AsioDeviceCatalog.IsLoopbackChannel`). Such
a member is kept on two conditions, both required:

- its doc comment carries the line
  `/// <remarks>Reserve API: no caller in the solution today (see AGENTS.md).</remarks>`,
  so a dead-code sweep can tell "kept on purpose" from "nobody noticed"; and
- it has tests. A reserve member whose only consumer is the compiler drifts
  silently — the tests are what keep it honest, and they are why it no longer
  reads as unreferenced.

A member that earns neither is still deleted. This exception does not extend to
`source/`: the app is not a library, and an unused control factory or dialog
helper there is just dead weight (the removed `UiStyle.Create*` helpers also
hard-coded absolute 96-DPI coordinates, which the WinForms note below forbids).

Two caveats when sweeping for dead code: `override` members and interface
implementations are called by the framework, not by name (`WaterfallSeries.
GetNearestPoint` is OxyPlot's tracker, `WindowsAudioEndpointService.OnDevice*`
is `IMMNotificationClient`), and a static class holding only extension methods
is never referenced by its own name.

### Numeric precision (float vs double)

Raw and real-time audio samples stay `float`: capture/playback buffers, the
`float[]` channels of `AudioCaptureResult`, recorded microphone/loopback data,
and generated playback signals (sweep/noise). Doubling those buffers adds no
information after the ADC and only costs memory traffic and GC/cache pressure.

Everything past the analysis boundary is `double`/`Complex`: FFT/IFFT and
spectra, H1/H2 transfer functions, coherence and accumulated power/cross
spectra, phase/unwrap/group delay, fractional delay, biquad coefficients and
responses, crossover/EQ optimizers, correlation/GCC-PHAT, channel summation,
window coefficients, frequency/time axes, and every accumulator (RMS, energy,
average, sum of squares). The reason is intermediate cancellation — dividing
tiny spectral values, subtracting near-equal phases, accumulating millions of
terms, sub-sample delay — where `float` error shows long before a single sample
overflows its range.

Convert exactly once, while filling the first analysis buffer — write
`float`-sourced samples straight into the `Complex[]`/`double[]` FFT input in the
same loop (see `SpectrumAnalysis.ComputePowerSpectrum` and
`SweepAnalysis.DeconvolveWithInverseFilter`). Do not materialize an intermediate
`float[] → double[] → Complex[]` copy. Keep public DSP APIs typed to their
natural source (`float` when the input is captured audio) rather than forcing
callers to pre-convert to `double[]`.

## Testing Conventions

Tests use xUnit. DSP tests are deterministic and synthetic: `tests/Resonalyze.Dsp.Tests/SyntheticMeasurement.cs` implements `IImpulseMeasurement` so analysis code is exercised against generated impulses/filters/delays rather than recordings. App tests focus on file formats and non-UI logic (overlay files, impulse-response files, plot model construction, PDF sheets) plus the measurement layer against a fake `IAudioSessionFactory` (`tests/Resonalyze.App.Tests/Fakes/`) — sweep/averaging/retry/cancellation/device-failure/live paths with no NAudio or hardware. `tests/Resonalyze.Audio.Tests/` exercises the audio internals directly (via `InternalsVisibleTo`): PCM decoding, accumulation, session reuse, WASAPI configuration. Hardware smoke tests are marked `[Trait("Category","Hardware")]` and excluded with `--filter "Category!=Hardware"`, which every CI step now passes. They also carry `[HardwareFact]`/`[HardwareTheory]` (`tests/HardwareFact.cs`, linked into both suites), which skips them with a reason when the endpoint environment variables are unset. Both layers matter: the filter keeps them off CI, and the attribute keeps a local unfiltered run from reporting them as passed — they used to open with an early `return`, which xUnit records as a pass, so nine tests reported green having executed no assert.

The build treats warnings as errors (`Directory.Build.props`), excluding only the NuGet audit warnings `NU1901`–`NU1904`, which can appear against an unchanged dependency when an advisory is published. There are no suppressions anywhere in the tree — no `#pragma warning disable`, no `NoWarn` — and that is meant to stay true.

## Documentation

The user-facing documentation is three files, and a change lands in whichever one
matches the question it answers:

- **`REFERENCE.md`** — every mode, panel, setting and graph gesture, plus the
  reasoning behind the ones whose behaviour is not obvious (why a window is
  anchored where it is, why a read-out refuses rather than guesses, what a number
  was measured against). Nearly every user-visible change belongs here.
- **`MANUAL.md`** — the car-tuning workflow in order, from measuring drivers to
  verifying the tune in the car. A change belongs here only when it changes what
  the tuner should DO, or in what order.
- **`README.md`** — the introduction: what the program is, what it needs, and how
  to take a first measurement. Only a change to that story belongs here.

Together they are the product's only user-facing manual — there is no separate
help, no wiki, no release notes describing behaviour. A feature that is in none
of them does not exist for anyone who did not write it.

So a change that alters what the user sees or does is **not finished until the
documentation says so**, in the same commit. That covers a new control or dialog,
a new setting or default, a refusal or warning the user can hit, a renamed or
removed control, and any change to what a curve, axis or read-out means. Purely
internal work (a refactor, a test, an optimization that no dial exposes) needs
nothing.

Two failure modes, both seen in this repository:

- **A shipped feature nobody documented.** The protective high-pass compensation
  landed with a Record Settings block, persistence and a settings migration, and
  went five commits without a line of README.
- **Prose that quietly became false.** A retired curve was still listed in the
  Virtual DSP section, and the Live Spectrum scale paragraph described a control
  that had been replaced. Adding a paragraph is not enough: **grep all three files
  for the terms your change makes obsolete** and fix what you find, because a
  stale sentence is worse than a missing one — a reader has no way to tell it is
  wrong.

When a change alters a panel shown in one of their screenshots, say so in the PR
description so the owner can re-capture. The images live in `assets/images/`
(README and reference) and `assets/images/manual/` (the manual, whose figures are
taken from one real tuning session), and none of them are generated by the build.

## Pull requests

Unless the owner asks for something else, a finished pull request is merged
**squashed, with its branch deleted**. The repository's squash default builds
the body from the branch's commit messages (`squash_merge_commit_message` is
`COMMIT_MESSAGES`), so hand the description over explicitly or `main` gets a
list of commit subjects where the PR's own text belongs:

```powershell
gh pr merge <n> --squash --delete-branch `
  --subject "<the PR title> (#<n>)" --body-file <the description>
```

`main` therefore carries one commit per PR, holding that description as its
message — so write the description as the commit message the repository is
going to keep, and correct it there if the branch outgrew it.

## Code Style

Enforced by `.editorconfig`; notable deviations from common C# defaults:

- Private fields are `camelCase` with **no underscore prefix** (and no `this.` qualification except in constructor assignment).
- `var` only when the type is apparent; explicit types otherwise, including built-ins.
- CRLF line endings, 4-space indent, Allman braces, braces always.
- New non-UI code uses file-scoped namespaces (see `Program.cs`, `ModeController.cs`).
- Keep static WinForms controls in `.Designer.cs`. For genuinely dynamic controls, use a designer-defined `TableLayoutPanel` or `FlowLayoutPanel`; avoid absolute 96-DPI coordinates because controls created after `InitializeComponent` miss designer autoscaling.
- Every WinForms container scales with `AutoScaleMode.Dpi` and `AutoScaleDimensions = (96, 96)` — do not go back to `AutoScaleMode.Font`. Font autoscaling is anisotropic: at 120 DPI it widens boxes by the average character width (7→8, ×1.14) while the glyphs themselves grow ×1.25, so labels, radios and buttons were clipped across the app at 125%. DPI scaling uses one uniform `DeviceDpi / 96` on both axes, which is the ratio the text grows by. Designer slack therefore scales with the text: leave a few pixels beside a label rather than sizing a box to its exact 96-DPI extent.

## User data paths

Implicit user data (settings, history, overlays, Virtual DSP state and crash
logs) is rooted by `ApplicationDataPaths`. Installed mode uses
`%LocalAppData%\Resonalyze`; a `portable.flag` file beside the executable opts
into portable storage beside the app. Do not introduce new direct
`AppContext.BaseDirectory` persistence paths.
