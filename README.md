<p align="center">
  <img src="assets/images/logo.png" alt="Resonalyze logo" width="256">
</p>

# Resonalyze

### Acoustic Measurement & Analysis

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![UI](https://img.shields.io/badge/UI-WinForms-5C2D91)](https://learn.microsoft.com/dotnet/desktop/winforms/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](License.md)
[![Build](https://github.com/DIMOSUS/Resonalyze/actions/workflows/build.yml/badge.svg)](https://github.com/DIMOSUS/Resonalyze/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/DIMOSUS/Resonalyze?display_name=tag)](https://github.com/DIMOSUS/Resonalyze/releases/latest)

**Resonalyze** is an open-source desktop application for measuring and
visualizing the acoustic behavior of audio systems, rooms, loudspeakers,
headphones, microphones, and complete signal paths.

It generates test signals, records the response through a Windows audio
device, processes the captured data, and presents the result as
engineering-focused plots.

> Resonalyze is under active development. Treat its results as diagnostic
> measurements, not as certified laboratory data.

## Download

Download the latest ready-to-run build from
[GitHub Releases](https://github.com/DIMOSUS/Resonalyze/releases/latest):

- `Resonalyze-vX.Y.Z-win-x64.zip` for most Windows computers
- `Resonalyze-vX.Y.Z-win-arm64.zip` for Windows on ARM

The release archives are self-contained and do not require a separate .NET
installation. SHA-256 checksum files are provided with every release.

## Highlights

- Exponential sine sweep measurement
- Impulse response with JSON save/load
- Windows Wave and ASIO audio backends
- Playback/recording device selection with ASIO channel routing
- ASIO loopback Time Alignment with sub-sample delay estimation
- Frequency response
- Harmonic distortion and THD+N
- Phase response
- Group delay
- Fourier waterfall
- Burst Decay
- Live Spectrum using a continuous noise measurement
- Autocorrelation
- Microphone calibration correction
- Persistent, styled plot overlays with curve arithmetic
- Configurable FFT windows, smoothing, offsets, sweep timing, and playback
  channel

## Gallery

<table>
  <tr>
    <td align="center"><strong>Frequency response</strong></td>
    <td align="center"><strong>Impulse response</strong></td>
  </tr>
  <tr>
    <td><img src="assets/images/fr.jpg" alt="Frequency response plot"></td>
    <td><img src="assets/images/impulse.jpg" alt="Impulse response plot"></td>
  </tr>
  <tr>
    <td align="center"><strong>Waterfall</strong></td>
    <td align="center"><strong>Burst Decay</strong></td>
  </tr>
  <tr>
    <td><img src="assets/images/waterfall.jpg" alt="Waterfall plot"></td>
    <td><img src="assets/images/burst.jpg" alt="Burst Decay plot"></td>
  </tr>
</table>

<details>
<summary><strong>More plots</strong></summary>

### Phase response

![Phase response plot](assets/images/phase.jpg)

### Group delay

![Group delay plot](assets/images/gd.jpg)

### Live Spectrum

![Live Spectrum plot](assets/images/noise.jpg)

### Time Alignment

![Time Alignment measurement](assets/images/time-alignment.png)

</details>

## Requirements

To run a release build:

- Windows 10 or later
- Working Windows playback and recording devices
- Optional ASIO driver for low-latency audio interfaces
- A suitable loopback, microphone, or other measurement connection

The self-contained release archives include the required .NET runtime.

To build Resonalyze from source:

- Windows 10 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026 with the **.NET desktop development** workload, or the
  .NET CLI

Use conservative playback levels when connecting physical equipment. Begin
with the output level turned down and verify the signal path before starting a
measurement.

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

The Release executable is generated at:

```text
source/bin/Release/net10.0-windows/Resonalyze.exe
```

## Measurement Workflow

1. Connect the output of the device under test to the selected input, directly
   or through a microphone and appropriate interface.
2. Start Resonalyze and open the measurement settings.
3. Select the audio backend, devices or ASIO channels, sweep duration,
   playback channel, and analysis parameters.
4. Start a recording to generate and capture the exponential sine sweep.
5. Select the required analysis view.
6. Adjust smoothing, windows, offsets, and display options as needed.
7. Use **Save** to preserve the captured impulse response for later analysis
   or comparison.

For acoustic measurements, microphone placement and room conditions strongly
affect the result. For electrical loopback measurements, make sure signal
levels and impedances are safe for both devices.

## Audio Backends

Resonalyze can run measurements through the standard Windows Wave backend or
through an ASIO driver.

### Wave

Use **Wave** for ordinary Windows playback and recording devices. The
measurement settings dialog lets you choose the playback device, recording
device, and playback channel.

### ASIO

Use **ASIO** for audio interfaces that provide a native ASIO driver. The
measurement settings dialog lets you choose:

- ASIO driver
- ASIO input channel used for recording
- ASIO output channel pair used for playback
- Playback routing inside the selected output pair

ASIO output routing works as follows:

- `Mono` sends the same signal to both channels of the selected output pair
- `Left` sends the signal only to the first channel of the pair
- `Right` sends the signal only to the second channel of the pair
- `Stereo` sends the signal to both channels of the pair

Before applying ASIO settings, Resonalyze checks whether the selected driver
supports the current sample rate. The dialog also shows available driver
diagnostics such as playback latency and, when exposed by the driver, frames
per buffer.

Click **ASIO Control Panel** to open the driver's native control panel. Use it
to configure driver-level settings such as buffer size, clock source, or sample
rate when the driver requires those settings outside the application.

ASIO support depends on the installed driver. If a driver is already in use by
another application or refuses the selected sample rate, Resonalyze reports the
driver error before starting the measurement.

## Time Alignment

The **Time Alignment** mode measures acoustic delay against an ASIO loopback
reference. It is designed for practical loudspeaker, microphone, and channel
alignment work where the result has to be more precise than a single audio
sample.

![Time Alignment measurement](assets/images/time-alignment.png)

Resonalyze plays the same mono exponential sweep used by the ordinary impulse
response measurement, records the microphone channel and the ASIO loopback
channel at the same time, and computes the microphone response relative to the
loopback reference path. This removes the unknown playback latency from the
measurement and keeps both recorded channels locked to the same hardware clock.

The delay estimator uses a deliberately robust chain:

- frequency-domain relative transfer function between loopback and microphone
- optional raised-cosine bandpass window around the frequency range of interest
- analytic-signal envelope of the relative impulse response
- fractional peak interpolation around the envelope maximum

That last step is the important bit: Resonalyze does not stop at the nearest
sample. It refines the peak position between samples, which enables
sub-sample delay estimates such as `87.0 samples` or `1.972 ms` instead of a
coarse integer-sample result. For time alignment, that is a serious practical
upgrade: smaller timing adjustments become visible, repeatable, and easier to
trust.

The mode also reports measurement quality:

- Color-coded `Excellent`, `Good`, `Fair`, or `Poor` confidence based on
  peak-to-background envelope ratio
- microphone peak and RMS levels in dBFS
- loopback peak and RMS levels in dBFS
- `CLIP` warning for overloaded microphone input
- `FULL SCALE` marker for a digital loopback reference running at 0 dBFS

The measured time, distance, and sample count are clickable. Click one of
those result lines to copy just the numeric value to the clipboard, which is
convenient when pasting delay values into another tool or a spreadsheet.

When the bandpass window is enabled, Resonalyze shows a small frequency-domain
preview of the selected pass band. After the measurement completes, it also
shows the envelope around the detected peak, making it easy to see whether the
reported delay comes from a clean dominant peak or from a noisy/ambiguous
response.

Time Alignment requires an ASIO driver with loopback input channels. Standard
Windows Wave devices cannot provide a reliable hardware reference input for
this workflow.

## Saving and Loading Impulse Responses

After a sweep measurement completes, click **Save** to store the current
impulse response. Resonalyze proposes a timestamped file name such as:

```text
Resonalyze-IR-2026-06-15_14-30-00.json
```

Files are saved as indented, human-readable JSON. Each file contains:

- Format and schema version
- Save time in UTC
- Sample rate and bit depth
- Sweep octave count and duration
- Playback channel
- Impulse-response peak index
- Real and, when present, imaginary sample values

Click **Load** to open a previously saved response. Resonalyze validates the
file before using it, restores the associated measurement metadata, and opens
the **Impulse Response** view. All analyses derived from an impulse response,
including frequency response, phase, group delay, waterfall, Burst Decay, and
autocorrelation, can then be generated without repeating the measurement.

Saving and loading are disabled while a measurement is running. The current
file format identifier is `resonalyze-impulse-response`, version `1`. Files are
intended to remain readable by people, but editing sample arrays manually may
make a file invalid or produce misleading analysis results.

## Plot Overlays

Each supported overlay view provides ten ordinary overlay slots and two
calculated overlay slots. Click a numbered button in slots 1-10 to capture one
of the curves currently shown on the plot. Overlay slots are stored
automatically as human-readable JSON beside the executable:

```text
overlays/<AnalysisMode>/overlay-01.json
```

For slots 1-10, the checkbox shows or hides the saved curve, the numeric
control applies a vertical offset, and `...` opens the settings dialog. A slot
without a saved file remains disabled.

Slots 11 and 12 are reserved for calculations between any two ordinary
overlays from slots 1-10. They do not have capture or clear buttons. Instead,
the row displays the currently selected operation, such as `A-B`, `A+B`,
`AVG`, or `|A-B|`. Use `...` to select source overlays A and B, choose the
operation, and configure the result appearance.

Ordinary overlay settings include:

![Ordinary overlay](assets/images/regular_overlay.jpg)
- A user-defined name
- Line color, thickness, style, and opacity
- Optional `1/48`, `1/24`, `1/12`, `1/6`, or `1/3` octave smoothing in
  frequency-based views
- A **Clear** action that removes only that slot in the current analysis mode

Calculated overlay settings additionally include:

![Calculated overlay](assets/images/calc_overlay.jpg)
- Two source slots selected from overlays 1-10
- Operations `A - B`, `B - A`, `A + B`, `(A + B) / 2`, and `|A - B|`
- Blend operation with a user-defined crossover frequency and transition width
- Optional amplitude-space math for dB-based views, which converts both
  curves to linear amplitude before the operation and back to dB afterward
- Independent octave smoothing applied after the selected operation

Octave smoothing is available only for Frequency Response, Phase Response,
Group Delay, and paused Live Spectrum. Impulse Response and Autocorrelation
keep their original time-domain samples. Overlay JSON always stores the
unsmoothed source points, so changing or disabling smoothing is lossless.

Calculated results use the same axes, units, zoom, and vertical pan as the
ordinary overlays. Operations are applied to displayed Y values after source
offsets. Consequently, addition and averaging on a decibel plot are
arithmetic operations on dB coordinates, not physical summation of acoustic
power.

Overlay files are separated by analysis mode and restored automatically when
the application starts or the active view changes. Changes to any source
overlay immediately update visible calculated overlays.

Ordinary overlay files use format `resonalyze-overlay`, version `4`.
Calculated overlay files use format `resonalyze-overlay-operation`, version
`4`. Older overlay schema versions are intentionally not loaded.

Overlays are available in the Impulse Response, Frequency Response, Phase
Response, Group Delay, paused Live Spectrum, and Autocorrelation views. The
Clear button removes all plotted curves and hides every active overlay without
deleting its saved JSON file. When Live Spectrum is running, Clear pauses it
before clearing the plot.

## Calibration

Frequency-response correction is loaded from `calibration.txt` beside
`Resonalyze.exe`. In a source checkout, edit:

```text
source/calibration.txt
```

The project copies this file to build and publish output automatically. The
calibration data is applied during logarithmic resampling when **Use
Calibration** is enabled in the frequency-response options. Replace the
example data with the correction curve supplied for your microphone or
measurement chain.

## Architecture

```text
Resonalyze/
|-- source/                 WinForms application and measurement orchestration
|   |-- Options/            Measurement and visualization settings
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

The UI project handles audio-device interaction, measurement lifecycle, and
plot presentation. `Resonalyze.Dsp` contains reusable DSP operations such as
FFT analysis, windowing, calibration, smoothing, logarithmic resampling,
impulse processing, phase analysis, and group-delay calculation.

## Technology

- [.NET 10](https://dotnet.microsoft.com/)
- [Windows Forms](https://learn.microsoft.com/dotnet/desktop/winforms/)
- [NAudio](https://github.com/naudio/NAudio)
- [NAudio.Asio](https://www.nuget.org/packages/NAudio.Asio)
- [Math.NET Numerics](https://numerics.mathdotnet.com/)
- [OxyPlot](https://oxyplot.github.io/)

## Roadmap

- Complete measurement-session save/load
- Plot and raw-data export
- Improved calibration workflow
- Versioned Windows installer

## Contributing

Bug reports, reproducible measurement cases, DSP corrections, and focused pull
requests are welcome. When reporting a measurement issue, include:

- Audio interface and driver
- Sample rate and bit depth
- Measurement mode
- Relevant analysis settings
- Expected and actual behavior
- Screenshot or exception stack trace

## License

Resonalyze is available under the [MIT License](License.md).
