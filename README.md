<p align="center">
  <img src="assets/images/banner.png" alt="Resonalyze banner">
</p>

<h1 align="center">Resonalyze</h1>

<p align="center">
  <strong>Engineering Acoustic Measurements for Loudspeakers and Rooms</strong>
</p>

<p align="center">
  A Windows desktop analyzer for impulse response, frequency response,
  loopback-referenced timing, live transfer functions, overlays, and practical
  loudspeaker alignment.
</p>

<p align="center">
  <a href="https://github.com/DIMOSUS/Resonalyze/releases/latest"><strong>Download latest release</strong></a>
  ·
  <a href="#quick-start"><strong>Build from source</strong></a>
  ·
  <a href="#measurement-workflow"><strong>Measurement workflow</strong></a>
</p>

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![UI](https://img.shields.io/badge/UI-WinForms-5C2D91)](https://learn.microsoft.com/dotnet/desktop/winforms/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](License.md)
[![Build](https://github.com/DIMOSUS/Resonalyze/actions/workflows/build.yml/badge.svg)](https://github.com/DIMOSUS/Resonalyze/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/DIMOSUS/Resonalyze?display_name=tag)](https://github.com/DIMOSUS/Resonalyze/releases/latest)

**Resonalyze** is an open-source desktop application for measuring and
visualizing the acoustic behavior of audio systems, rooms, loudspeakers,
headphones, microphones, and complete signal paths. It generates test signals,
records the response through a Windows audio device, processes the captured
data, and presents the result as engineering-focused plots.

> Resonalyze is under active development. Treat its results as diagnostic
> measurements, not as certified laboratory data.

## Demo

A one-minute tour of the main features:

<p align="center">
  <img src="assets/images/resonalyze.gif" alt="Resonalyze feature tour">
</p>

## Download

Download the latest ready-to-run build from
[GitHub Releases](https://github.com/DIMOSUS/Resonalyze/releases/latest):

- `Resonalyze-Setup-vX.Y.Z-win-x64.exe` — the recommended installed build
- `Resonalyze-vX.Y.Z-win-x64.zip` — for most Windows computers
- `Resonalyze-vX.Y.Z-win-arm64.zip` — for Windows on ARM

The `.zip` builds are self-contained portable packages and do not require a
separate .NET installation. The installer adds shortcuts, uninstall support,
and automatic in-app updates for the installed x64 build. A SHA-256 checksum
file is provided with every release.

## Highlights

- Exponential sine sweep measurement with impulse-response JSON save/load
- Optional loopback-referenced sweep processing via a transfer function
- Time Alignment with sub-sample delay estimation from the transfer IR
- Live Spectrum with `Transfer Function` and `Input Spectrum` modes
- Frequency response, phase, group delay, waterfall, Burst Decay, and
  autocorrelation
- Minimum-phase / excess-phase decomposition with a millisecond gate, gate
  offset, and a τ (delay) detrend for cross-measurement phase comparison
- Per-curve visibility toggles in every analysis view; curves redraw on the fly
  with no separate draw/clear step
- Harmonic distortion, THD, and THD+N analysis
- Persistent comparison overlays with labels, styling, curve math, targets,
  import/export, and saved per-mode state
- EQ Wizard: design an up-to-32-band parametric EQ against a target, with Auto
  Tune, a live results read-out, cross-tool PEQ import/export, and a printable
  tuning-sheet PDF
- Measurement History with in-memory snapshots, saved-file recall, FR previews,
  per-entry working state (mode, settings, active overlays), and a one-click
  new-session reset
- Windows Wave and ASIO backends with device-aware sample-rate selection and
  backend-specific channel routing
- Compact Mic/Loop input level meter with Peak, RMS, Peak Hold, and stored
  measurement levels
- Docked, non-modal settings panels with live previews and instant graph
  updates
- Auto-update support for installed builds through a signed NetSparkle appcast

## Why Resonalyze?

If you already use tools like REW, OpenSoundMeter, or Smaart, the obvious
question is: why install another analyzer?

Resonalyze is built around a focused engineering workflow:

- **Loopback-referenced timing**
  Measurements can use a recorded loopback channel as the time reference, so
  delay and transfer-function analysis are tied to the actual playback path
  instead of to guesswork.
- **Practical loudspeaker alignment**
  Time Alignment reports first arrival and strongest peak with sub-sample
  interpolation, distance at 20 °C, confidence, signal levels, and a visible
  envelope around the detected arrival.
- **Fast compare-and-adjust work**
  Persistent overlays, calculated overlays, target curves, and on-plot labels
  make it quick to compare measurements, tuning passes, channels, listening
  positions, or before/after changes.
- **Live transfer-function analysis**
  Live Spectrum can use a loopback reference, coherence, overlap, averaging,
  peak hold, and overload detection to show a driven response rather than only
  the raw microphone spectrum.
- **Measurement history as a working shelf**
  Recent captures stay available in memory, saved files are remembered across
  launches, and each entry has a frequency-response preview. Entries also
  remember their full working state — active mode, per-mode settings, and shown
  overlays — so switching between measurements restores the whole context, and a
  one-click reset starts a fresh session from defaults.
- **Developer-friendly, inspectable data**
  IR files, overlays, settings, and history metadata are stored as readable
  JSON where practical, making measurements easy to archive, diff, and debug.

Resonalyze does not try to be every acoustic tool at once. Its sweet spot is
measurement-driven speaker and room work, where timing, repeatability, quick
comparison, and transparent data matter more than a large legacy feature set.

## Gallery

<table>
  <tr>
    <td width="50%">
      <h3>Frequency Response</h3>
      <img src="assets/images/fr.jpg" alt="Frequency response plot">
      <p>One-click loudspeaker response measurement with smoothing,
      calibration, distortion curves, overlays, and target comparison.</p>
    </td>
    <td width="50%">
      <h3>Time Alignment</h3>
      <img src="assets/images/time-alignment.png" alt="Time Alignment measurement">
      <p>Sub-sample delay estimation from a loopback-referenced transfer
      impulse response, with confidence, levels, distance, and envelope view.</p>
    </td>
  </tr>
  <tr>
    <td width="50%">
      <h3>Live Spectrum</h3>
      <img src="assets/images/noise.jpg" alt="Live Spectrum plot">
      <p>Real-time input spectrum or transfer-function analyzer with coherence,
      averaging, overlap, peak hold, and unreliable-band marking.</p>
    </td>
    <td width="50%">
      <h3>Impulse Response</h3>
      <img src="assets/images/impulse.jpg" alt="Impulse response plot">
      <p>Inspect the measured impulse response, save it as readable JSON, load
      it later, and reuse it across analysis modes without re-measuring.</p>
    </td>
  </tr>
  <tr>
    <td width="50%">
      <h3>Group Delay</h3>
      <img src="assets/images/gd.jpg" alt="Group delay plot">
      <p>Analyze timing behavior from the loopback-referenced transfer IR, with a
      millisecond gate, gate offset, and a live impulse-window preview.</p>
    </td>
    <td width="50%">
      <h3>Waterfall and Burst Decay</h3>
      <img src="assets/images/waterfall.jpg" alt="Waterfall plot">
      <p>Visualize frequency decay and stored energy with the Fourier waterfall
      and Burst Decay views.</p>
    </td>
  </tr>
  <tr>
    <td width="50%">
      <h3>EQ Wizard</h3>
      <img src="assets/images/eq_wizard.png" alt="EQ Wizard mode">
      <p>Design a parametric EQ against a target with Auto Tune, per-band curves,
      a live results read-out, cross-tool import/export, and a tuning-sheet PDF.</p>
    </td>
    <td width="50%">
      <h3>Target Overlays</h3>
      <img src="assets/images/target_overlay.jpg" alt="Target overlay settings">
      <p>Compare any source against a parametric target shape with presets, a
      tolerance band, and a deviation / EQ-correction curve.</p>
    </td>
  </tr>
</table>

<details>
<summary><strong>More plots</strong></summary>

### Phase response

![Phase response plot](assets/images/phase.jpg)

### Burst Decay

![Burst Decay plot](assets/images/burst.jpg)

### Overlays

![Calculated overlay settings](assets/images/calc_overlay.jpg)

</details>

## Requirements

To run a release build:

- Windows 10 or later
- Working Windows playback and recording devices
- An optional ASIO driver for low-latency audio interfaces
- A suitable loopback, microphone, or other measurement connection

The self-contained release archives include the required .NET runtime.

To build Resonalyze from source:

- Windows 10 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026 with the **.NET desktop development** workload, or the
  .NET CLI

Use conservative playback levels when connecting physical equipment. Start with
the output turned down and verify the signal path before running a measurement.

## Quick Start

Clone the repository and open:

```text
source/Resonalyze.sln
```

Or build and run it from the command line:

```powershell
dotnet restore source/Resonalyze.sln
dotnet build source/Resonalyze.sln --configuration Release
dotnet run --project source/Resonalyze.csproj
```

Run all application and deterministic DSP tests with:

```powershell
dotnet test source/Resonalyze.sln -c Release
```

The Release executable is produced at:

```text
source/bin/Release/net10.0-windows/Resonalyze.exe
```

Tagged GitHub releases also produce:

- portable self-contained `.zip` packages for `win-x64` and `win-arm64`
- an x64 `Setup.exe` installer with uninstall support
- NetSparkle appcast files that the installed build uses for automatic updates

The `build.yml` workflow runs both the application test project and the DSP
test project on every push to `main` and on every pull request.

## Measurement Workflow

This workflow covers impulse-response (IR) based analysis: a swept-sine
measurement is captured once and then inspected across the frequency-response,
phase, group-delay, impulse, waterfall, and burst-decay views. For continuous,
real-time analysis without capturing an IR, use the additional
[Live Spectrum](#live-spectrum) mode instead.

1. Connect the output of the device under test to the selected input, either
   directly or through a microphone and a suitable interface.
2. Start Resonalyze and open the measurement settings.
3. Select the audio backend, sample rate, devices or backend-specific input and
   loopback channels, sweep duration, playback channel, and analysis
   parameters.
4. Start a recording to generate and capture the exponential sine sweep.
5. Watch the compact input level meter to confirm microphone level, loopback
   presence, and headroom before trusting the measurement.
6. Select the analysis view you need.
7. Adjust smoothing, windows, offsets, and display options as needed. Mode
   settings open in a docked, non-modal panel attached to the plot, so the main
   window stays usable while the settings are visible. Changes apply on the fly
   and update the active graph without closing the panel or resetting the
   current zoom/pan.
8. Capture and compare with [overlays](#plot-overlays): store the current curve
   in an overlay slot, import a reference from text, or combine slots with curve
   math. When tuning home or car systems, add a **target curve** overlay (a
   parametric house/Harman-style target with presets) and switch its deviation
   readout to **EQ correction** to see how much to dial into an equalizer.
9. Use **Save** to keep the captured impulse response for later analysis or
   comparison.
10. Use **History** to review recent measurements, preview their frequency
    response, reload an older snapshot, or save an in-memory capture to disk.

For acoustic measurements, microphone placement and room conditions strongly
affect the result. For electrical loopback measurements, make sure the signal
levels and impedances are safe for both devices.

## Mode Settings

The **Mode Settings...** button opens the settings for the current analysis
mode in a docked panel aligned to the plot area. The panel has no title bar, can
stay open while the main window has focus, and switches automatically to the
matching panel when you change modes.

Settings apply on the fly: changing a value immediately redraws the current
analysis while preserving the visible plot range. This makes it easier to tune
smoothing, FFT windows, Tukey fades, offsets, and display options without losing
the area you were inspecting.

Each curve-based view groups its plotted curves under a **Curves:** heading with
one checkbox per curve — for example Primary / HD2–HD4 / THD+N in Frequency
Response, or measured / minimum / excess in Phase. Toggling a curve redraws
immediately; there is no separate draw or clear step. Numeric and dropdown
settings carry a small **R** button that resets them to the built-in default,
and double-clicking a plot axis restores its default scale.

The Frequency Response, Phase, Group Delay, Waterfall, and Burst settings
include a compact impulse-window preview where applicable. The preview shows the
impulse response used by that mode together with the selected Tukey window. Phase
and Group Delay analyze the loopback transfer IR and are only drawn when the
active record provides one; their preview marks the gate position used for the
analysis.

Live Spectrum has its own docked settings panel. It lets you choose between
`Transfer Function` and `Input Spectrum`, enable or disable calibration, and
select a **Sequence Length** from a power-of-two list. The sequence length is
the FFT block size used by the live analyzer and is preserved between sessions.

## Phase and Group Delay

Phase and group-delay analysis run on the **loopback transfer impulse response**:
both views need the common timing reference it provides, so they are only drawn
when the active record contains a transfer IR. Without one, the plot says that
loopback is required instead of showing a misleading curve.

Both modes share a millisecond-based gate built from a left Tukey fade, a flat
plateau, and a right Tukey fade. A **Gate offset** positions the end of the left
fade inside the fixed analysis frame, and **Fit** snaps it to the transfer-IR
peak. The docked preview draws the impulse response, the gate window, and a
marker at the gate offset. A read-only readout shows the lowest reliable
frequency (≈ 1 / gate length), so it is clear where the gated curve stops being
trustworthy.

The Phase view can show three independently toggled curves:

- **Measured phase** — the raw response, including delay and reflections.
- **Minimum phase** — the part tied to the magnitude (correctable with EQ),
  reconstructed with a real-cepstrum method.
- **Excess phase** — measured minus minimum: the all-pass part (pure delay and
  reflections) that an equalizer cannot fix.

A **τ** (delay) value detrends the linear-phase slope so the excess phase becomes
readable. **Find τ** estimates it either from the dominant arrival (peak) or from
the energy-weighted average group delay (slope). Entering the same τ on two
measurements lines up their phase for a direct comparison — for example, a
midrange and a tweeter on the same axis.

Group Delay reads absolute delay referenced to the start of the transfer IR, so a
peak well into the impulse response reports its true arrival time.

## Audio Backends

Resonalyze can run measurements through the standard Windows Wave backend or
through an ASIO driver.

![Measurement settings](assets/images/measurement-options.png)

The microphone input is the primary measurement channel. The loopback input is
optional for ordinary sweep measurements and required for Time Alignment. When
loopback is enabled for a sweep measurement, Resonalyze records both channels
simultaneously and computes the main impulse response as a transfer function
from the loopback reference to the microphone response. This removes the
playback path from the primary response plots. Harmonic distortion curves still
use the ordinary sweep-deconvolution response, because the harmonic separation
belongs to the sweep analysis itself.

Phase and Group Delay are computed from this loopback-referenced transfer impulse
response and are only available when the active record contains one. The
group-delay reference is the start of the transfer IR, so reported delay is
absolute rather than relative to a response peak.

Without loopback, sweep measurements use the classic single-channel
deconvolution path.

### Wave

Use **Wave** for ordinary Windows playback and recording devices. The
measurement settings dialog lets you choose:

- playback device
- recording device (microphone)
- Wave loopback device (`Same as microphone device` by default, or a separate
  input device)
- sample rate from the values supported by the current configuration
- playback channel
- microphone input channel (`Left` or `Right`)
- optional loopback input channel (`Left` or `Right`)

By default the loopback is captured from a second channel of the microphone
device, so it shares the same clock and stays sample-accurate. If that device
does not expose a stereo input, loopback capture is unavailable and Resonalyze
falls back to ordinary single-channel measurement behavior.

#### Separate Wave loopback device

You can instead capture the loopback from a **different** input device than the
microphone (for example, a second interface or a dedicated loopback dongle).
Pick the device under **Wave loopback device** and choose its loopback channel;
the microphone and loopback channels may then even be the same number because
they come from different devices. Sample-rate options are restricted to rates
supported by the playback device and **both** input devices.

This is a deliberate convenience for users who ask for it, with a real
limitation: the two devices run on **independent hardware clocks**, so the
streams cannot be sample-accurately synchronised. Resonalyze starts both
recorders before playback and aligns them best-effort (at the first recorded
sample, trimmed to a shared length; the live transfer function pairs blocks in
arrival order with bounded drift). Magnitude/frequency-response results remain
usable, but phase, group delay, and time alignment are degraded by the unknown
start offset and clock drift. For sample-accurate loopback timing, keep the
loopback on the microphone device or use ASIO.

### ASIO

Use **ASIO** for audio interfaces that provide a native ASIO driver. The
measurement settings dialog lets you choose:

- ASIO driver
- sample rate from the values supported by the selected driver
- ASIO input channel used for the microphone
- optional ASIO loopback input channel
- ASIO output channel pair used for playback
- playback routing within the selected output pair

ASIO output routing works as follows:

- `Mono` sends the same signal to both channels of the selected output pair
- `Left` sends the signal only to the first channel of the pair
- `Right` sends the signal only to the second channel of the pair
- `Stereo` sends the signal to both channels of the pair

Before applying ASIO settings, Resonalyze checks whether the selected driver
supports the current sample rate. The dialog also shows available driver
diagnostics such as playback latency and, when the driver exposes it, frames
per buffer.

Click **ASIO Control Panel** to open the driver's native control panel. Use it
to configure driver-level settings such as buffer size, clock source, or sample
rate when the driver requires those to be set outside the application.

Click **Test ASIO Inputs** to capture a short diagnostic snapshot of the
available ASIO inputs. This helps verify that the microphone and loopback
channels are truly separate and are not being mono-summed by the driver or the
audio-interface control software.

ASIO support depends on the installed driver. If a driver is already in use by
another application or refuses the selected sample rate, Resonalyze reports the
driver error before starting the measurement.

## Input Level Meter

The right-side control column includes a compact two-channel input meter for
`Mic` and `Loop`. It is designed to stay useful — while routing, checking
loopback, or validating a completed measurement — without opening extra dialogs.

- the bar shows a filtered RMS level
- the bright vertical marker shows Peak Hold
- the text shows `Peak / RMS` in `dBFS`
- after a sweep or time-alignment measurement completes, the meter retains the
  final levels from the last valid capture instead of dropping back to idle

This makes it easy to spot missing loopback, a weak microphone level, overload,
or an unexpectedly hot reference path before you start analyzing the curves.

## Live Spectrum

The **Live Spectrum** mode supports two operating modes:

- **Transfer Function**
  Uses the configured loopback channel as a reference and shows the live
  frequency-domain relationship from loopback to microphone. Because the
  estimate is referenced to loopback rather than to the microphone alone, it
  also helps suppress noise and other input-side content that is not correlated
  with the playback signal.
- **Input Spectrum**
  Shows the classic live spectrum of the selected microphone input, without a
  loopback reference.

In Transfer Function mode, Resonalyze also draws a **coherence** curve (γ²) on a
secondary right-hand axis scaled from 0 to 1. Coherence shows how much of the
measured response is linearly correlated with the loopback reference: values
near 1 mark frequencies where the transfer-function estimate is trustworthy,
while low values flag bands dominated by noise, reflections, or non-linear
behavior.

The live estimate averages in the power domain (and the input spectrum is
power-averaged with a Hann window), which avoids the downward bias that
magnitude averaging introduces on noise-like signals. The level is calibrated
for tones rather than as a power spectral density. On-screen smoothing is
referenced to wall-clock time, so the response stays consistent regardless of
the chosen overlap and sequence length, and the display refreshes at roughly 30
frames per second.

Transfer Function mode requires a configured loopback input in **Record
Settings**. It works with either Wave or ASIO, as long as the microphone and
loopback are routed to separate channels. If loopback is not configured,
Resonalyze blocks Transfer Function start and explains what needs to be fixed.

In practice, Transfer Function mode is more stable than raw input-spectrum
viewing when the room or measurement chain contains unrelated noise. It is not a
magic denoiser, but it is the better choice when you want to focus on the driven
response rather than on whatever the microphone happens to hear.

The Live Spectrum settings panel also exposes **Sequence Length**, the FFT block
size used by the live analyzer. Only power-of-two values are offered, to keep the
live FFT path efficient and predictable.

It also exposes **Overlap** (`Off`, `50%`, or `75%`), which slides the analysis
window by a fraction of its size instead of advancing in non-overlapping blocks.
Both Live Spectrum modes apply a Hann window before the FFT, so overlap reclaims
the samples the window tapers at the block edges. Higher overlap gives faster,
smoother averaging and a more responsive display, at the cost of more FFTs per
second.

**Smoothing** applies fractional-octave smoothing (`Off`, `1/1` … `1/48`) to the
displayed curve, using the same presets as the Frequency Response mode.

**Window** selects the analysis window applied before the FFT: `Hann` (a good
general default), `Flat Top` (maximum amplitude accuracy for tones),
`Blackman-Harris` (strong spectral-leakage suppression), or `Rectangular`
(unwindowed, for special cases).

**Averaging** sets how quickly the trace responds: `Fast`, `Medium`, and `Slow`
select exponential time constants (referenced to wall-clock time, so they are
independent of overlap and sequence length), while `Infinite` integrates a
cumulative average indefinitely. **Reset Average** clears the running average and
peak-hold envelope without restarting the measurement.

**Main curve** (on by default) shows the primary live trace itself; turning it
off leaves only the optional peak-hold and coherence curves.

**Peak Hold** overlays a second curve that retains the maximum level seen on the
trace until it is reset. **Coherence** (on by default) toggles the γ² curve
shown on a secondary 0-to-1 axis in Transfer Function mode.

**Coherence Limit** marks unreliable parts of the transfer-function curve: any
frequency whose coherence falls below the chosen percentage (default `25%`) is
drawn dimmed and dashed, so it is immediately clear which portions of the trace
should not be trusted. Set it to `Off` to draw the whole curve uniformly.

If the CPU cannot keep up with the chosen settings, captured blocks are dropped
rather than allowed to stall the measurement, and a **processing overload**
warning appears at the top of the plot, making the cause of a stuttering display
clear.

Switching to another analysis mode and back restores the last Live Spectrum
curve, its peak-hold envelope, and any active overlays, so a captured trace is
not lost when you step away to inspect a different view. Press **Start** to
resume live capture; starting a new capture replaces the remembered trace
automatically.

## Measurement History

The **History** button opens a docked measurement-history panel with:

- a list of recent measurement snapshots
- a compact frequency-response preview for the selected row
- row tooltips with capture metadata such as time, mode, sample rate, duration,
  channel, peak index, and stored mic/loopback meter levels

History entries come in two kinds:

- `RAM` for in-memory snapshots from the current session
- `FILE` for saved IR files remembered across launches, as long as the files
  still exist on disk

The newest entries appear at the top of the list. Column-header sorting is
intentionally disabled, so the history keeps a stable chronological order and
the row actions always match the visible item.

The currently active loaded snapshot stays highlighted in the list, even when
you click another row only to inspect its preview. This makes it easier to
compare entries without losing track of which measurement is actually driving
the main plots.

Double-click a row to load it into the main workspace. Use:

- **Save** to turn an in-memory snapshot into a regular IR JSON file
- **Delete** to remove an item from history without deleting the underlying file
  from disk
- **New session (reset to defaults)** to start with a clean slate: all per-mode
  settings return to their defaults, and the current measurement and overlays
  are cleared. Audio device and routing settings are kept, and the history list
  and saved files are left intact. The active entry's working state is saved
  first, so nothing is lost.

### Working state remembered per entry

Each history entry remembers the working state it was last used with: the active
mode, every per-mode setting (frequency response, phase, group delay, impulse
response, waterfall, burst decay, live spectrum, time alignment), and which
overlay slots were shown. Switching to another entry and back therefore restores
not just the impulse response but the whole working context.

This state is kept current as you work — it is written back into the active entry
whenever you switch to another entry and when you close the app — so it reflects
what you were actually doing, not just the moment of capture. Audio device and
routing settings are never changed by switching entries. Overlays keep their own
separate on-disk storage; the history records only which slots were active and
reloads their contents from there.

Saving an in-memory snapshot turns that row into a file-backed history entry and
updates the visible name to the chosen file name. Loaded IR files appear in the
same list as fresh captures, so History works as a practical short-term
measurement shelf rather than as a separate file browser.

To keep memory use predictable, Resonalyze retains only a small rolling set of
unsaved in-memory snapshots. Saved file-backed entries are persisted separately.

## Time Alignment

The **Time Alignment** mode analyzes acoustic delay from the currently active
measurement record. It is designed for practical loudspeaker, microphone, and
channel alignment work, where the result has to be more precise than a single
audio sample.

![Time Alignment measurement](assets/images/time-alignment.png)

Time Alignment no longer runs its own separate capture path. Instead, it reads
the active **transfer impulse response** already stored in the current record.
That means it works immediately after:

- a new sweep measurement captured with loopback enabled
- loading an IR JSON file that contains transfer-response data

If the active record does not contain a transfer IR, the mode clearly reports
that the measurement was captured without loopback and does not attempt to
estimate delay from the ordinary sweep-deconvolution response.

The transfer IR itself comes from the same loopback-based sweep measurement
pipeline used elsewhere in the app: Resonalyze plays the exponential sweep,
records the microphone and loopback simultaneously, and computes the microphone
response relative to the loopback reference path. This removes unknown playback
latency from the response used for timing analysis. With ASIO, both recorded
channels also stay locked to the same hardware clock, which gives the most
repeatable result.

The delay estimator uses a deliberately robust chain:

- the active transfer impulse response from the current record
- an optional raised-cosine bandpass window around the frequency range of
  interest
- the analytic-signal envelope of that impulse response
- fractional peak interpolation around the envelope maximum

That last step is the important part: Resonalyze does not stop at the nearest
sample. It refines the peak position between samples, which enables sub-sample
delay estimates such as `87.0 samples` or `1.972 ms` instead of a coarse
integer-sample result. For time alignment, this is a serious practical upgrade:
smaller timing adjustments become visible, repeatable, and easier to trust.

The mode recalculates immediately when you switch into **Time Alignment**, and
also updates live as soon as you change the bandpass settings.

It reports signal quality using the stored meter snapshot from the same
measurement record:

- color-coded `Excellent`, `Good`, `Fair`, or `Poor` confidence based on the
  peak-to-background envelope ratio
- microphone peak and RMS levels in dBFS
- loopback peak and RMS levels in dBFS
- a `CLIP` warning for an overloaded microphone input
- a `FULL SCALE` marker for a digital loopback reference running at 0 dBFS

The compact input level meter remains useful here too: it preserves the final
captured levels from the last valid sweep measurement or loaded file, so the
Time Alignment readout still has the signal context that produced the current
transfer IR.

The measured time, distance, and sample count are clickable. Click one of those
result lines to copy just the numeric value to the clipboard, which is
convenient when pasting delay values into another tool or a spreadsheet.

When the bandpass window is enabled, Resonalyze shows a small frequency-domain
preview of the selected pass band. It also shows the envelope around the
detected peak, making it easy to see whether the reported delay comes from a
clean dominant arrival or from a noisy or ambiguous response.

Time Alignment therefore depends on how the underlying sweep record was
captured. To produce a usable transfer IR, the sweep measurement itself must be
run with a configured loopback input channel. That loopback-enabled sweep can be
captured through:

- **ASIO**, the recommended path for the best timing accuracy
- **Wave**, if the selected recording device exposes a stereo input and one side
  can be dedicated to loopback

Wave loopback is supported for convenience, but ASIO remains the preferred path
for serious timing work because the capture chain is more tightly controlled.

## Installer and Updates

Resonalyze supports two release styles:

- **Portable `.zip` builds**
  Extract and run `Resonalyze.exe` directly. This is convenient for quick
  testing or for keeping multiple versions side by side.
- **Installed `Setup.exe` build**
  Installs to the current user's local Programs folder, creates shortcuts, and
  registers an uninstaller.

When the application detects a newer GitHub release, the version label in the
custom title bar changes to **Update available**. Clicking it opens a focused
update dialog:

- installed builds can either start an **Automatic Update** or open the GitHub
  releases page for a manual download
- portable builds offer a manual download only, because they are not tied to a
  managed install location

Automatic update currently targets the installed x64 build distributed through
`Setup.exe`. Portable `.zip` builds remain fully supported, but they are updated
manually by downloading a newer archive.

## Saving and Loading Impulse Responses

After a sweep measurement completes, click **Save** to store the measured
impulse-response data. Resonalyze proposes a timestamped file name such as:

```text
Resonalyze-IR-2026-06-15_14-30-00.json
```

Files are saved as indented, human-readable JSON. Each file contains:

- format and schema version
- save time in UTC
- sample rate and bit depth
- sweep octave count and duration
- playback channel
- measurement mode (`SweepDeconvolution` or `LoopbackTransfer`)
- sweep-deconvolution impulse-response samples and peak index
- optional loopback transfer-function impulse-response samples and peak index
  when loopback was enabled
- stored microphone and loopback Peak/RMS meter values from the measurement
- embedded preview frequency-response data for the Measurement History panel

Click **Load** to open a previously saved response. Resonalyze validates the
file before using it, rejects files below `44100 Hz`, restores the associated
measurement metadata into the active record, stays in the current analysis view,
and redraws it from the loaded data. Loading an IR does **not** rewrite the
current audio-device configuration in Record Settings. All analyses derived from
an impulse response — frequency response, phase, group delay, waterfall, Burst
Decay, autocorrelation, and loopback-based Time Alignment — can then be generated
without repeating the measurement.

Saving and loading are disabled while a measurement is running. The current file
format identifier is `resonalyze-impulse-response`, version `4`. Files are meant
to stay human-readable, but editing the sample arrays by hand may make a file
invalid or produce misleading analysis results. Files that do not yet contain
the embedded preview-frequency-response section can still be loaded; Resonalyze
rebuilds the preview when needed.

## Plot Overlays

Each supported overlay view provides twelve universal overlay slots. Every slot
can hold one of three kinds, chosen from the numbered capture-button menu (or
from the settings dialog):

- **Captured** — a snapshot of a curve currently on the plot.
- **Operation** — a calculation between two captured slots (`A - B`, `B - A`,
  `A + B`, `(A + B) / 2`, `|A - B|`, or a frequency blend).
- **Target** — a parametric target curve compared against a source.

Overlay slots are stored automatically as human-readable JSON beside the
executable:

```text
overlays/<AnalysisMode>/overlay-01.json
```

The numbered button opens a menu to **Capture curve**, **Import from text**,
**Export to text**, or switch the slot to a **Calculated overlay** or **Target**.
The checkbox shows or hides the slot, the numeric control applies a vertical
offset, and `...` opens the settings dialog. Operation and Target slots
reference only captured slots (or, for a Target, the current measurement), which
keeps the recompute order simple and free of cycles.

Visible overlay names are drawn directly over the plot as a compact legend. Each
entry uses the same color and line style as the curve itself, so exported
screenshots and day-to-day comparisons stay readable even when many curves are
active.

### Target curves

A **Target** overlay compares a source against a parametric target shape and
draws two curves from the one slot: the **target** itself and the **deviation**
(source minus target), plus an optional shaded **tolerance band** (±dB). The
source is either a captured slot or the **current measurement** (the main
Frequency Response curve, or the Live Spectrum main trace — including the running
trace, which the target and deviation follow frame by frame).

The target shape and its tolerance band are parametric over frequency, so they
are drawn whenever the slot is enabled even if no measurement is on the plot yet;
only the deviation curve waits for an incoming source.

The target shape is built from four editable terms: an overall **tilt** around a
1 kHz pivot, a **bass shelf**, a **treble shelf**, and a **presence** bump/dip.
This covers room, car, home-theater, and voicing targets. Presets fill in the
parameters and remain fully editable:

- `Flat`, `Room (gentle)`, `Harman room`, `Warm`
- `Car`, `Car (mild)`, `House / bass boost`
- `X-curve (cinema)`, `Smiley`, `BBC dip`, `Custom`

The deviation curve has selectable modes: **Deviation** (`measurement − target`,
how far the response sits from the target), **EQ correction**
(`target − measurement`, the gain to dial into an equalizer to reach the
target), or **None** to hide it.

The settings dialog shows a live preview of the target shape, and the shared
slot offset moves the target up or down (the deviation follows automatically).
Target overlays are available in Frequency Response and Live Spectrum (both the
running and the paused trace).

![Target overlay settings](assets/images/target_overlay.jpg)

### Import and export

**Import from text** loads a captured overlay from a plain-text file of `X Y`
pairs (for example, `123.4 -5.5`), one per line. Parsing is lenient: values may
be separated by spaces, tabs, commas, or semicolons; extra columns are ignored;
and any line that is not a valid number pair (comments, headers, blanks) is
skipped. **Export to text** writes the slot's current curve in the same format.
For a Target slot, **Export deviation** writes the deviation or EQ-correction
curve, which is handy for transferring corrections into an equalizer or another
tool.

Captured overlay settings include:

![Ordinary overlay](assets/images/regular_overlay.jpg)
- a user-defined name
- line color, thickness, style, and opacity
- optional `1/48`, `1/24`, `1/12`, `1/6`, or `1/3` octave smoothing in
  frequency-based views
- a **Clear** action that removes only that slot in the current analysis mode

Calculated overlay settings additionally include:

![Calculated overlay](assets/images/calc_overlay.jpg)
- two source slots selected from the captured overlay slots
- operations `A - B`, `B - A`, `A + B`, `(A + B) / 2`, and `|A - B|`
- a blend operation with a user-defined crossover frequency and transition width
- optional amplitude-space math for dB-based views, which converts both curves to
  linear amplitude before the operation and back to dB afterward
- independent octave smoothing applied after the selected operation

In **Phase Response**, the difference operations (`A - B`, `B - A`, `|A - B|`) are
phase-aware. Each captured phase curve remembers whether it is an unwrapped
(continuous) or wrapped (-180..180) representation: measured phase follows the
**Unwrap** option in effect when it was captured, while minimum and excess phase are
always continuous. When either operand is wrapped, the difference uses the shortest
angular distance (`atan2(sin Δ, cos Δ)`) so it never jumps by ±360° across the
branch cut. When both are unwrapped it stays a plain subtraction, preserving the
accumulated slope (and therefore the delay) of the two curves. Imported text curves
have no wrap hint and are treated as unwrapped.

Octave smoothing is available only for Frequency Response, Phase Response, Group
Delay, and paused Live Spectrum. Impulse Response and Autocorrelation keep their
original time-domain samples. Overlay JSON always stores the unsmoothed source
points, so changing or disabling smoothing is lossless.

Calculated results use the same axes, units, zoom, and vertical pan as the
ordinary overlays. Operations are applied to the displayed Y values after source
offsets. As a result, addition and averaging on a decibel plot are arithmetic
operations on dB coordinates, not physical summation of acoustic power.

Overlay files are separated by analysis mode and restored automatically when the
application starts or the active view changes. Changes to any source overlay
immediately update the visible calculated overlays.

All overlay slots use a single file format, `resonalyze-overlay`, version `5`,
with a `kind` field selecting captured / operation / target. Older overlay
schema versions are intentionally not loaded.

Overlays are available in the Impulse Response, Frequency Response, Phase
Response, Group Delay, paused Live Spectrum, and Autocorrelation views. A
**Show all** / **Hide all** pair above the overlay panel toggles every active
overlay for the current mode at once, without deleting any saved JSON file.

## EQ Wizard

The **EQ Wizard** (under the **Tools** tab) designs a parametric equalizer — up
to 32 peaking (PK) bands plus a preamp — that moves a measured response toward a
target. It builds directly on Target overlays: you can open it from **Tools > EQ
Wizard** and pick a target, or jump straight in with the **To EQ Wizard** button
in a Target overlay's settings. Because the wizard needs a real curve to tune
against, its target must use a captured source (not the live current measurement).

![EQ Wizard mode](assets/images/eq_wizard.png)

The plot shows, on shared frequency/dB axes:

- **Source** — the captured reference measurement (with optional extra smoothing)
- **Target** — the parametric target shape (always drawn in a fixed blue)
- **Source + EQ** — the source with the current EQ applied
- **EQ** — the filter response itself (all bands, without the preamp) in white
- a shaded **error fill** between Source + EQ and Target, so the remaining
  deviation is visible at a glance

Click a band card (or any of its fields) to overlay that band's individual
contribution as a dashed curve relative to the target; click empty space to clear
it. Each band card carries its **frequency**, **Q**, and **gain**, and the panel
adds a **Target Level** (target offset), a **Gain** (preamp), a **Bands** count,
source **Smoothing** (`1/N` octave), and a **Bypass** toggle that draws the curves
without the EQ. An overlay-settings shortcut reopens the underlying target.

### Auto Tune

**Auto Tune** fits the whole EQ automatically. It works on the error between the
target and the (smoothed) source, sets a preamp for the broadband level, then adds
peaking bands greedily where the residual error is largest — choosing each band's
frequency, gain, and the Q that reduces the error the most. It **chooses the band
count itself**, up to the **Max Filters** limit (4–32). A cumulative-boost cap and
minimum band spacing keep it from stacking many maxed-out bands where the response
simply cannot be corrected (for example a deep-bass roll-off).

A **From / To** frequency window limits where bands are placed; it is drawn on the
plot as a shaded band between dashed guides, and the same window bounds the error
metrics in the results panel.

### Results

Replacing the overlay panel in this mode, a colour-coded **Tuning results** panel
reports the fit quality and the EQ's own extents:

- **RMS error** and **Max error** between Source + EQ and Target, measured inside
  the From / To window
- **Filters used**, the number of active bands
- **Peak boost** and **Peak cut** of the combined EQ
- **Headroom** — the margin to 0 dB (red when the EQ nets a boost that could clip)

### Import, export, and tuning sheet

The wizard imports and exports PEQ profiles in several formats, so tunings move
between tools and DSPs:

- **Import + export:** Equalizer APO, REW filter settings, Generic CSV,
  EasyEffects (JSON), CamillaDSP (YAML)
- **Export only:** miniDSP biquads (RBJ coefficients), GraphicEQ (Wavelet /
  JamesDSP)

Import is deliberately lenient: comments, blank lines, disabled (`OFF`) filters,
non-peaking filter types, and malformed entries are skipped rather than rejected.

**Export as tuning sheet** produces a phone-friendly PDF for reading next to the
car or speaker: the product banner, a title from the file name, the date and fit
range, a small EQ preview graph with the fit window shaded, the tuning statistics,
the preamp, and one large card per filter.

## Calibration

Frequency-response correction is loaded from `calibration.txt` beside
`Resonalyze.exe`. In a source checkout, edit:

```text
source/calibration.txt
```

The project copies this file to the build and publish output automatically. The
calibration data is applied during logarithmic resampling when **Use
Calibration** is enabled in the frequency-response options. Replace the example
data with the correction curve supplied for your microphone or measurement
chain.

## Architecture

```text
Resonalyze/
|-- source/                 WinForms application and measurement orchestration
|   |-- Options/            Measurement and visualization settings
|   |-- Overlays/           Persistent overlay slots and calculated overlays
|   |-- Plotting/           OxyPlot model creation, annotations, and adapters
|   |-- Shell/              Main form, title bar, commands, and docked settings
|   |-- TimeAlignment/      Loopback delay measurement UI and orchestration
|   |-- Tools/              EQ Wizard, PEQ controls, and tuning-sheet PDF export
|   |-- Ui/                 Reusable WinForms controls and dialogs
|   `-- Resonalyze.csproj
|-- dsp/                    Reusable signal-processing library
|   `-- Resonalyze.Dsp.csproj
|-- tests/
|   |-- Resonalyze.App.Tests/  File-format and application tests
|   `-- Resonalyze.Dsp.Tests/  Synthetic DSP tests
|-- .github/workflows/      CI builds and automated tagged releases
|-- global.json             Pinned .NET SDK version
`-- README.md
```

The UI project handles audio-device interaction, the measurement lifecycle, and
plot presentation. `Resonalyze.Dsp` contains reusable DSP operations such as FFT
analysis, windowing, calibration, smoothing, logarithmic resampling, impulse
processing, phase analysis, and group-delay calculation.

## Technology

- [.NET 10](https://dotnet.microsoft.com/)
- [Windows Forms](https://learn.microsoft.com/dotnet/desktop/winforms/)
- [NAudio](https://github.com/naudio/NAudio)
- [NAudio.Asio](https://www.nuget.org/packages/NAudio.Asio)
- [Math.NET Numerics](https://numerics.mathdotnet.com/)
- [OxyPlot](https://oxyplot.github.io/)
- [YamlDotNet](https://github.com/aaubry/YamlDotNet) — CamillaDSP profile import/export
- [PDFsharp / MigraDoc](https://github.com/empira/PDFsharp) — tuning-sheet PDF export

Third-party package licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Contributing

Bug reports, reproducible measurement cases, DSP corrections, and focused pull
requests are welcome. When reporting a measurement issue, include:

- audio interface and driver
- sample rate and bit depth
- measurement mode
- relevant analysis settings
- expected and actual behavior
- a screenshot or exception stack trace

## License

Resonalyze is available under the [MIT License](License.md).
