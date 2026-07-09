# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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

Platform constraint: `source/` (the app) and `tests/Resonalyze.App.Tests/` target `net10.0-windows` and only build/run on Windows. `dsp/` and `tests/Resonalyze.Dsp.Tests/` target plain `net10.0` and are cross-platform — on a Linux environment, only the DSP library and its tests can be built and run.

Test data: `assets/test_data` is a git submodule ([Resonalyze-test-data](https://github.com/DIMOSUS/Resonalyze-test-data), ~230 MB of real transfer IRs). Some dsp tests read it; run `git submodule update --init assets/test_data` once per clone, or those tests fail with a message pointing here.

## Architecture

Two projects, with a deliberate boundary:

- **`dsp/Resonalyze.Dsp`** — pure, UI-free signal-processing library. Depends only on MathNet.Numerics and YamlDotNet. Contains FFT/spectrum analysis, windowing, transfer functions, minimum phase, excess delay, time-alignment analysis, biquad/crossover filters, the EQ auto-tuner, and PEQ profile import/export formats (Equalizer APO, REW, MiniDSP, CamillaDSP, EasyEffects, generic CSV — all implementing `IEqProfileFormat`). The app hands measurement data to this layer through `IImpulseMeasurement` (impulse response + peak index + sample rate).
- **`source/Resonalyze`** — the WinForms app: audio device I/O, measurement lifecycle, and plotting.

Inside `source/`, the flow is: signal generation and capture (`Measurements/` — `ExponentialSineSweep`, `NoiseSignal`, `SoundRecorder`; `Audio/` — NAudio Wave and ASIO backends, `DualDeviceCapture`, `LoopbackSequencePairer` for loopback-referenced timing) → analysis via the Dsp library → plot presentation (`Plotting/` — `PlotModelFactory` builds OxyPlot models, `OxyPlotAdapter` hosts them).

Key structural points:

- **`Shell/Form1` is the hub**, split into partial classes by concern (`Form1.Measurement.cs`, `Form1.Plotting.cs`, `Form1.History.cs`, `Form1.Compare.cs`, etc.). The `Mode` enum in `Form1.cs` defines all analysis modes (frequency/phase/group delay/waterfall/burst decay/live spectrum/time alignment/EQ wizard/signal generator/virtual crossover); `ModeSwitching/ModeController` orchestrates tab switches.
- **`Options/`** holds one settings panel per mode (`FROptions`, `IROpt`, `GDOpt`, ...), docked into the shell via `Shell/DockedModeSettingsHost`.
- **`Tools/`** contains the larger feature panels: EQ Wizard, Signal Generator, Virtual DSP (`VirtualCrossoverPanel` + project file persistence), and PDF tuning-sheet export (PDFsharp/MigraDoc).
- **`Overlays/`** manages persistent overlay slots and calculated (math) overlays; **`History/`** persists measurement snapshots with per-entry working state.
- Update checking uses NetSparkle + `Settings/GitHubReleaseChecker`.

## Testing Conventions

Tests use xUnit. DSP tests are deterministic and synthetic: `tests/Resonalyze.Dsp.Tests/SyntheticMeasurement.cs` implements `IImpulseMeasurement` so analysis code is exercised against generated impulses/filters/delays rather than recordings. The exception is `RealMeasurementArrivalTests`, which pins field regressions against the real measurements in the `assets/test_data` submodule. App tests focus on file formats and non-UI logic (overlay files, impulse-response files, plot model construction, PDF sheets).

## Code Style

Enforced by `.editorconfig`; notable deviations from common C# defaults:

- Private fields are `camelCase` with **no underscore prefix** (and no `this.` qualification except in constructor assignment).
- `var` only when the type is apparent; explicit types otherwise, including built-ins.
- CRLF line endings, 4-space indent, Allman braces, braces always.
- New non-UI code uses file-scoped namespaces (see `Program.cs`, `ModeController.cs`).
