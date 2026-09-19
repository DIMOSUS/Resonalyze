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

- **`Shell/Form1` is the composition root and binding**, split into partial classes by concern (`Form1.Measurement.cs`, `Form1.Plotting.cs`, `Form1.History.cs`, `Form1.Compare.cs`, etc.). The `Mode` enum (`ModeSwitching/Mode.cs`) names the analysis modes and tools; `ModeSwitching/ModeCatalog` is the plain table of what each tab shows, and `ModeSwitching/ModeController` switches tabs one at a time against an `IModeView`: the view leaves, running work stops, the view enters, the window lays out, the view draws.
- **The analyzer's open measurement is an `AnalyzerDocument`.** Every input (a run, a file, history, REW, a recorded sweep) builds one immutable `MeasurementResult` and installs it through the document; every mode, Save, Send to REW and Time Alignment read the document. `ExpSweepMeasurement` holds only the next run's configuration. A new input brings a result builder beside its parser, never engine state. The views follow the document on their own: `Plotting/AnalyzerPlot` (the main plot, its overlays, zoom memory and peak read-out), Time Alignment and the settings panels that preview it subscribe to it, so an input installs and never tells a view to redraw. Every mode's view options live in one `Plotting/AnalyzerViewSettings`, which the settings file and each history entry keep a copy of. See `docs/tech/sweep-measurement.md#the-open-measurement`.
- **Live Spectrum's state is a `LiveSpectrumSession`**: the run, the accumulation it holds after a stop or the capture loaded in its place, the peak-hold envelope and the next run's frozen inputs. `LiveSpectrumDisplay` turns the options and one read of the analyzer (`LiveCaptureSetup`) into what a build draws, and `LiveSpectrumCurves` draws it as points, the capture document and the overlays' raw RTA included. `LiveSpectrumController` is the plot binding and a mode view beside `AnalyzerPlot`; the form's record button, Save, calibration read-out and settings panel follow the session's `Changed`. `PlotModelFactory` does not read the live analyzer. See `docs/tech/live-spectrum.md#code-map`.
- **`Options/`** holds one settings panel per mode (`FROptions`, `IROpt`, `GDOpt`, ...), docked into the shell via `Shell/DockedModeSettingsHost`.
- **`Tools/`** contains the larger feature panels: EQ Wizard, Signal Generator, Virtual DSP (`VirtualCrossoverPanel` + project file persistence), and PDF tuning-sheet export (PDFsharp/MigraDoc). `EqWizardPanel` is self-contained: it owns its source, uses a mode-local target curve and persists through `MeasurementSettingsFile.EqWizard`. It picks that source itself — an impulse response (file or history), a captured overlay slot, or a text curve — through `EqWizardSourceResolver`, which reads overlay slot FILES and history results as one-time imports. Keep it that way: the panel must not reach into the live `OverlayCollection` or the current measurement, and an imported curve is a snapshot with no link back to what it came from.
- **Virtual DSP keeps its tune in a UI-free `VirtualCrossoverSession`.** Whatever reads the tune (curves, warnings, Auto delay, the crossover wizard's inputs, the audition, the Agent Bridge) is a class that takes the session; `VirtualCrossoverPanel` is its only writer and holds binding code only, in partials named for what they bind. A new Virtual DSP feature brings its own session-reading class and, if it has controls, a partial, never logic in the panel; a test of a rule builds a session, not a panel. `VirtualCrossoverPanelBoundaryTests` keeps non-private statics out of the panel. See `docs/tech/virtual-dsp-panel.md#code-map`.
- **`Plotting/` owns the plot INTERACTION as well as the models.** `PlotInteraction.Enable` installs `PlotGestureController` on every `PlotView` in the app, and that controller is the single place the mouse/keyboard map lives — it is shaped after REW's graph panel (wheel zooms both axes, Shift/Ctrl or the pointer over an axis restricts it, the end of an axis moves one limit, middle-drag is a variable zoom, double click opens `Ui/Dialogs/GraphLimitsDialog`), and the `REFERENCE.md` table under "Graph Zoom and Limits" is the user-facing copy of it. Keep new gestures there rather than on individual views. `PlotViewportMemory` carries a plot's zoom across the constant model rebuilds — the main plot keyed per `Mode`, the two Time Alignment previews one memory each — by asking the axes which ranges a user forced on them (`PlotAxisViewport.CaptureOverrides` resets an axis, reads what the model computes on its own, and puts the override back). Do NOT replace that with a baseline captured when a model is shown: overlays join a plot afterwards — a mode switch restores its slots after `AnalyzerPlot` has drawn, Show All later still — and on an auto-scaled axis they widen the range through the data, which a baseline comparison reads as a zoom. An axis the user moved is restored; an untouched one is left to the new model, so `RaiseDecibelViewCeiling` and the group-delay auto-fit still work. A setting that changes what an axis MEANS calls `Forget`.
- **`Overlays/`** manages persistent overlay slots and calculated (math) overlays; **`History/`** keeps measurement results with per-entry working state.
- Update checking uses NetSparkle + `Settings/GitHubReleaseChecker`.

### Where logic lives

A panel that is the only holder of its tool's state collects every computation on that state. The Virtual DSP panel
reached 11,091 lines over five files that way, each piece readable and the whole not; taking it apart needed a UI-free
session and about twenty classes that read it (#203). These rules keep the other parts from repeating it.

- **State gets a UI-free owner before features are written against it.** Once a second feature reads a tool's data,
  the data moves into a plain type (`VirtualCrossoverSession`, `AnalyzerDocument`, `LiveSpectrumSession`); the UI writes it and presents what
  readers return.
- **A feature is a type that takes the model, plus a partial if it has controls.** Computations, verdicts, report text
  and write-back rules take the model and return values; the UI reads controls into the model, calls the feature and
  shows the result. Logic never calls back into the UI.
- **One owner per fact.** A value kept in two places and synchronised by hand drifts the first time someone writes one
  of them. Derive the copy from the owner (a Virtual DSP block reads its shown side from the session through a
  provider) or delete it.
- **Read the UI once per operation.** Async work captures the control values it needs into an immutable record before
  its first await (`VirtualCrossoverViewState`); workers read snapshots, never controls or a mutable model.
- **Extract before the second copy.** When a second path needs a computation (one side and stereo, the screen and an
  export), move it out first. Virtual DSP's group placement had two copies that had already drifted apart, and the AI
  package rebuilt the screen's frame on its own.
- **The tests are the early warning.** A test that reaches a UI class through reflection into private members or
  `GetUninitializedObject`, or an `internal static` put on a UI class so a test can call it, is testing logic that
  lives in the wrong place: move the logic and test its type. Where a boundary matters, a test pins it
  (`VirtualCrossoverPanelBoundaryTests`).
- **Partials hold binding code.** Splitting a UI class by concern, as `Form1` is, helps once the logic has left; a
  partial full of computation is the same monolith in more files.
- **Size is a signal.** A UI file past about 1,500 lines, or one that grows with every feature, has its rules extracted
  before the next feature rather than after. The boundary is a type in the same assembly; a new project is neither
  needed nor wanted.

**Refactoring a part that has grown.** Keep behaviour identical and name each deliberate difference in the PR. Before
moving code, build a characterization harness outside the repo that drives the real UI on real sessions and dumps
everything the part produces (curves, read-outs, reports, exported documents); build it against `main` and against the
branch and require a byte-identical diff. A harness that builds panels runs portable (see User data paths). Keep a
synthetic version in the repo that drives the live UI through its controls (`VirtualCrossoverPanelWiringTests`), and
prove it catches wiring mistakes by putting some in on purpose. Work in stages, a commit each: the state owner, the
features one by one, then the partial split as a pure move checked line by line.

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

The images live in `assets/images/` (README and reference) and
`assets/images/manual/` (the manual). Most of them are re-taken by
`tools/Resonalyze.Screenshots`, which drives the real shell:

```powershell
dotnet run --project tools/Resonalyze.Screenshots -- --list
dotnet run --project tools/Resonalyze.Screenshots -- fr gd
```

It needs a local `screenshots.json` pointing at measurements of your own (see that
project's README), so a change that alters a panel a figure shows should say so in
the PR description — the owner re-runs it. The tool is in the solution deliberately:
it drives panels through internals, so a renamed control breaks the BUILD rather than
producing a screenshot of the wrong thing months later. When it does break, fix it in
the same commit as the rename.

A handful of figures cannot be produced offline and stay committed artifacts: the
Live Spectrum RTA shot (it needs a live signal), the sum-loss before/after crop, and
the manual figures that came from the forum article. The tool's README lists them.

### Technical documentation

`docs/tech/<feature>.md` holds the maintainer-facing design of the complex features:
how an algorithm works, why it is built that way, what was measured, which
alternatives were rejected and the thresholds' provenance. One file per feature,
organised by mechanism rather than by source file. This is where reasoning that
would otherwise become a long code comment goes; the code keeps a one-line pointer
(`// See docs/tech/auto-alignment.md#seed-selection.`). A change to such a mechanism
updates its section in the same commit. `TechDocPointerTests` fails on any
`docs/tech/<file>.md#anchor` pointer whose heading no longer exists, so a renamed
heading takes its pointers along. `docs/specs/` holds feature specifications written
before implementation; `docs/agent/` is what the external AI assistant reads.

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
- **Colour comes from the palette, by role.** `UiThemePalette` holds every colour the app paints, named for the
  job it does (`ControlSurface`, `TextSecondary`, `CurveExcessPhase`), and answers it in BOTH themes — the
  properties are `required`, so a new role cannot be added to one theme and forgotten in the other. Call sites,
  including `.Designer.cs` files, read `UiPalette.<Role>`; plot code converts with `.ToOxy()`. Nothing anywhere
  else builds a colour out of numbers or framework names, and `UiPaletteCoverageTests` fails the build if it
  does (its allow list names the few exceptions and why: the user's own curve colours, PDF sheets on paper,
  OxyPlot's defaults compared against). The theme is chosen once in `Program.Main` before the first control is
  built, because a designer reads the palette inside `InitializeComponent`; changing it asks for a restart.
  Plot chrome has one home, `PlotModelStyle` — `ApplyChrome`/`CreatePreviewModel` and `AddAxis`/`StyleAxis`;
  do not set an axis colour by hand.
- Buttons, checkboxes and radio buttons are `ReleaseClickButton` / `ReleaseClickCheckBox` / `ReleaseClickRadioButton`, never the WinForms types they derive from — in the designer as well as in code. WinForms puts a `WindowFromPoint` ownership check in its mouse-release click path — one of several conditions, alongside cancelled validation and `ButtonBase`'s own press/capture state — so any window overlapping that one pixel (a tooltip, above all — measured: 20 of 20 clicks lost) takes the click silently while the control still paints its press. `ReleaseClick*` works around that hit-test failure and only it, and does not decide anything itself: when the release point belongs to another window, the release is handed to the framework at another free point on the same control, so the framework still answers whether a click is due and every other condition it withholds one under keeps applying. A release outside the control, or a control covered edge to edge, is left exactly as it arrived. A menu is then opened through `DropDownMenu`, which posts the show clear of the mouse message and guards it against the focus change opening it causes.
- Comments stay under roughly 10% of code lines; every line of them is read, and paid for, by each agent that opens the file. See **Comments** below.
- Every WinForms container scales with `AutoScaleMode.Dpi` and `AutoScaleDimensions = (96, 96)` — do not go back to `AutoScaleMode.Font`. Font autoscaling is anisotropic: at 120 DPI it widens boxes by the average character width (7→8, ×1.14) while the glyphs themselves grow ×1.25, so labels, radios and buttons were clipped across the app at 125%. DPI scaling uses one uniform `DeviceDpi / 96` on both axes, which is the ratio the text grows by. Designer slack therefore scales with the text: leave a few pixels beside a label rather than sizing a box to its exact 96-DPI extent.

### Comments

A comment says what the code cannot: a non-obvious reason, an invariant, a unit, a
trap a future edit would fall into. Everything else is noise that costs every
reader tokens and goes stale silently.

- Do not describe what the next lines obviously do, and do not restate a name:
  `/// <summary>Gets the sample rate.</summary>` on `GetSampleRate` is deleted, not written.
- No `<param>`/`<returns>` on every parameter. Add one only when it carries a unit,
  range or contract the name does not (`delayMs` needs none; "seconds, relative to the
  capture start, may be negative" earns one).
- Keep a comment to 1–3 lines. Longer design reasoning — what was measured, rejected
  alternatives, where a threshold came from — goes to `docs/tech/<feature>.md` (see
  Technical documentation), with a one-line pointer in code.
- No history in comments: no "used to", "previously", PR or review references, owner
  requests. Git and the PR description keep that.
- Tests: the test name and asserts are the explanation. At most a short line for a
  scenario that is not evident, such as which field defect a synthetic fixture reproduces.
- Not deleted as noise: the `Reserve API` remark described above.

When you change code, fix or delete the comments it made false; a stale comment is
worse than none.

## User data paths

Implicit user data (settings, history, overlays, Virtual DSP state and crash
logs) is rooted by `ApplicationDataPaths`. Installed mode uses
`%LocalAppData%\Resonalyze`; a `portable.flag` file beside the executable opts
into portable storage beside the app. Do not introduce new direct
`AppContext.BaseDirectory` persistence paths. The App test project writes
`portable.flag` into its own output, so the test host is portable: a panel a test
builds autosaves beside the test assembly, never over the developer's session
(`ApplicationDataPathsTests.TheTestHost_KeepsItsDataBesideItself` fails without it).
