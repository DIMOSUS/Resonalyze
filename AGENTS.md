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

Test data: `assets/test_data` is a git submodule ([Resonalyze-test-data](https://github.com/DIMOSUS/Resonalyze-test-data), ~230 MB of real transfer IRs). Some dsp tests read it; run `git submodule update --init assets/test_data` once per clone, or those tests fail with a message pointing here.

## Architecture

Three projects, with deliberate boundaries (`Dsp` ⟂ `Audio`; the app depends on both):

- **`dsp/Resonalyze.Dsp`** — pure, UI-free signal-processing library. Depends only on MathNet.Numerics and YamlDotNet. Contains FFT/spectrum analysis, windowing, transfer functions, minimum phase, excess delay, time-alignment analysis, biquad/crossover filters, the EQ auto-tuner, and PEQ profile import/export formats (Equalizer APO, REW, MiniDSP, CamillaDSP, EasyEffects, generic CSV — all implementing `IEqProfileFormat`). The app hands measurement data to this layer through `IImpulseMeasurement` (impulse response + peak index + sample rate).
- **`audio/Resonalyze.Audio`** — owns all audio drivers/devices (WASAPI Shared/Exclusive, ASIO, MME), format negotiation, capture/playback lifecycle, PCM decoding, diagnostics and warm-up. NAudio is confined here (declared `PrivateAssets="compile"` so `using NAudio` does not compile in the app). Low-level device types are `internal`; the measurement layer talks only to the neutral abstraction: `IAudioSessionFactory` + `IAudioDuplexSession`/`IAudioStreamingSession`/`IAudioPlaybackSession` and backend-neutral DTOs (`AudioSessionRequest`, `AudioPlaybackSignal`, `AudioCaptureResult`, `AudioSessionDiagnostics`, `AudioEndpointDescriptor`, `AudioFormat`, `PlaybackChannel`). Backends are chosen by the persisted `AudioBackend` enum inside `AudioBackendRegistry` (the only backend dispatch — no `switch (AudioBackend)` in `source/`).
- **`source/Resonalyze`** — the WinForms app: composition root, measurement lifecycle, and plotting.

Inside `source/`, the flow is: signal generation (`Measurements/` — `ExponentialSineSweep`, `NoiseSignal` produce float data only) → an audio session opened via `IAudioSessionFactory` (composition root in `Shell/Form1` builds `AudioBackendRegistry.CreateDefault()` and injects the factory into `ExpSweepMeasurement`, `NoiseMeasurement`, the signal generator and warm-up) → analysis via the Dsp library → plot presentation (`Plotting/` — `PlotModelFactory` builds OxyPlot models, `OxyPlotAdapter` hosts them). Microphone and loopback are always channels of ONE input device, so timing stays sample-synchronous.

Key structural points:

- **`Shell/Form1` is the hub**, split into partial classes by concern (`Form1.Measurement.cs`, `Form1.Plotting.cs`, `Form1.History.cs`, `Form1.Compare.cs`, etc.). The `Mode` enum in `Form1.cs` defines all analysis modes (frequency/phase/group delay/waterfall/burst decay/live spectrum/time alignment/EQ wizard/signal generator/virtual crossover); `ModeSwitching/ModeController` orchestrates tab switches.
- **`Options/`** holds one settings panel per mode (`FROptions`, `IROpt`, `GDOpt`, ...), docked into the shell via `Shell/DockedModeSettingsHost`.
- **`Tools/`** contains the larger feature panels: EQ Wizard, Signal Generator, Virtual DSP (`VirtualCrossoverPanel` + project file persistence), and PDF tuning-sheet export (PDFsharp/MigraDoc). `EqWizardPanel` is self-contained: it loads its own impulse response, computes its source FR with the selected microphone calibration, uses a mode-local target curve and persists through `MeasurementSettingsFile.EqWizard`; do not couple it to overlays or the current measurement.
- **`Overlays/`** manages persistent overlay slots and calculated (math) overlays; **`History/`** persists measurement snapshots with per-entry working state.
- Update checking uses NetSparkle + `Settings/GitHubReleaseChecker`.

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

Tests use xUnit. DSP tests are deterministic and synthetic: `tests/Resonalyze.Dsp.Tests/SyntheticMeasurement.cs` implements `IImpulseMeasurement` so analysis code is exercised against generated impulses/filters/delays rather than recordings. The exception is `RealMeasurementArrivalTests`, which pins field regressions against the real measurements in the `assets/test_data` submodule. App tests focus on file formats and non-UI logic (overlay files, impulse-response files, plot model construction, PDF sheets) plus the measurement layer against a fake `IAudioSessionFactory` (`tests/Resonalyze.App.Tests/Fakes/`) — sweep/averaging/retry/cancellation/device-failure/live paths with no NAudio or hardware. `tests/Resonalyze.Audio.Tests/` exercises the audio internals directly (via `InternalsVisibleTo`): PCM decoding, accumulation, session reuse, WASAPI configuration. Hardware smoke tests are marked `[Trait("Category","Hardware")]` and excluded with `--filter "Category!=Hardware"`.

## Code Style

Enforced by `.editorconfig`; notable deviations from common C# defaults:

- Private fields are `camelCase` with **no underscore prefix** (and no `this.` qualification except in constructor assignment).
- `var` only when the type is apparent; explicit types otherwise, including built-ins.
- CRLF line endings, 4-space indent, Allman braces, braces always.
- New non-UI code uses file-scoped namespaces (see `Program.cs`, `ModeController.cs`).
- Keep static WinForms controls in `.Designer.cs`. For genuinely dynamic controls, use a designer-defined `TableLayoutPanel` or `FlowLayoutPanel`; avoid absolute 96-DPI coordinates because controls created after `InitializeComponent` miss designer autoscaling.

## User data paths

Implicit user data (settings, history, overlays, Virtual DSP state and crash
logs) is rooted by `ApplicationDataPaths`. Installed mode uses
`%LocalAppData%\Resonalyze`; a `portable.flag` file beside the executable opts
into portable storage beside the app. Do not introduce new direct
`AppContext.BaseDirectory` persistence paths.
