# Live Spectrum

The Live Spectrum mode is a real-time analyzer. With a loopback reference it shows a dual-channel
transfer function (with coherence); without one, or when RTA is selected, it shows the
reference-free microphone spectrum (RTA). The MMM mode accumulates an RTA as a moving-microphone
spatial average and saves it as a capture document (see [spatial-average.md](spatial-average.md)).

Where the code lives (`source/LiveSpectrum/`):

| Area | Code |
| --- | --- |
| UI controller, redraw loop, peak hold, capture state | `LiveSpectrumController` |
| Stored capture format | `LiveCaptureDocument`, `LiveCaptureRecipe` |
| Raw RTA for overlays | `LiveRtaRawCapture` |
| Plot models, scale and smoothing rules | `PlotModelFactory` (`EffectiveLiveSpectrumScale`, `EffectiveLiveSmoothingCode`, `LiveUsesBandPower`) |

## Analysis modes and signals

- The effective mode is RTA when selected or when there is no loopback; starting is not gated on a
  loopback. In RTA-only views the transfer function and coherence are hidden, the RTA is forced on
  (even if its checkbox is off) and peak hold envelops it.
- Coherence describes the transfer estimate, so it is drawn only with the transfer function. The
  above-threshold transfer segment keeps the primary tag so current-measurement targets use it, not the
  low-coherence segment.
- `NormalizeSignalType`: Silent (ambient RTA, no excitation) is the only mode-exclusive signal. A
  transfer function has nothing to correlate without an excitation, so entering Transfer mode falls back
  to periodic pink. Every real excitation is valid in both modes.
- The live transfer curve and the RTA carry `CurveTag`s so overlays can bind to them by key and capture
  their raw form; the RTA is a curve in its own right (the only trace in RTA views and the source of a
  moving-microphone tune).

## Scale and SPL view-only

`RenderingSpl` follows the selection. With dB SPL selected but no matching calibration
(`SplViewOnly`), the SPL axis and SPL overlays show but live curves are suppressed: at raw dBFS on an
absolute axis they would read as absurd sound-pressure levels. A notice explains the missing curve,
added only when a curve really was suppressed. The record button resets the scale to relative before a
run, so this state covers idle redraws of a stale snapshot and a running analyzer losing its
calibration.

MMM never enters view-only: without an anchor it reports a relative scale and keeps drawing band
levels, because a spatial average needs the band-power rendering, not an absolute reference
(`PlotModelFactory.LiveUsesBandPower`). The options panel colours its SPL choice amber only when
`HasDisplayableCurve` says a curve would actually be hidden.

A loaded capture is always drawn on its own axis: its levels mean what its capture-time anchor made
them mean, whatever the current options say.

## Per-run freezing and calibration

Immediately before an accumulation begins, the controller freezes on it:

- the protective high-pass (`SetCaptureProtectiveHighPass`), so the filter the curve divides out and the
  filter the saved recipe records are the same, and an edit mid-walk cannot re-tilt it;
- the microphone calibration **curve**, not its id (`SetCaptureMicrophoneCalibration`). The bins are
  re-rendered on every redraw and again on Save, so a rig calibration changed between walk and Save
  would otherwise recompute the walk and the file would name a microphone it was never taken through.

`ApplyProtectiveHighPass` is a separate entry point because a settings edit that leaves the audio session
alone deliberately does not reach `ConfigureFrom` (reconfiguring restarts a running analyzer), yet the
next run's filter must still update.

`DisplayedCalibrationName` answers for the curve on screen: a loaded capture's own correction (by name,
which need not exist on this machine), else the id frozen on the running or held accumulation, else
null. Showing the rig's selection beside a curve taken through another microphone was the bug this
prevents.

`RefreshCalibration` runs in every app mode because a calibration change invalidates the peak-hold
envelope wherever the analyzer sits; the plot is rebuilt only when Live Spectrum is visible. The capture
itself is never touched: a Silent RTA that loses SPL keeps running on the relative axis.

## Peak hold

- The envelope is held over the **displayed band curve** (frequency, dB), not raw FFT bins. In SPL the
  display sums bin powers per band, so per-bin peaks summed later would add maxima from different frames
  and overstate the band. The grid is stable across ticks and a band level is monotone in its power, so a
  per-index max of displayed dB is the peak of the band level shown.
- Because the envelope stores finished display values, any change to the display transform makes it
  incompatible and it is dropped, not max-ed against new values. `PeakHoldDisplayKey` captures that
  transform: scale, RTA-only shaping, the **effective** smoothing code (MMM pins smoothing Off, so the
  stored option would call two transforms the same), the SPL offset (only in SPL), and the tilt model
  (null = off is distinct from a flat model).
- The microphone calibration and protective high-pass are not in the key: both are frozen when the run
  begins, right after `SuspendPeakHold`, so they cannot change while an envelope exists. Keying on the
  rig's calibration dropped valid envelopes whenever the next run's microphone was chosen mid-hold.
- `SuspendPeakHold` briefly pauses tracking so ramp-up frames are not latched.

## Averaging reset and discarding data

- Applying display options restarts an Infinite average — except for a spatial-average capture, where
  the accumulation is the measurement and a checkbox must not throw away minutes of walking. The rule is
  keyed on the analysis mode, not the stored averaging speed (in MMM that is only the remembered RTA
  preference).
- `DiscardCapturedData` runs when an acquisition parameter (mode, signal colour, window, FFT length,
  overlap) changes while stopped: redrawing old data under new parameters would silently re-interpret it
  (slope compensation would re-tilt a pink RTA as if the excitation were white). A loaded capture is
  discarded too. A running analyzer needs no call; its restart begins a fresh accumulation.
- `StopAndHoldAsync` harvests the final accumulation into the held snapshot; `AbortAsync` would stop
  without the last reading. `HasCaptureToSave` excludes a loaded capture, since re-saving would restamp
  it with this session's recipe.

## Loaded captures

A capture shown with `ShowLoadedCapture` is controller **state**, not a one-off draw. Every rebuild (tab
switch, display option, calibration change) goes through `RebuildModel`, which redraws the loaded
capture; painting it once let the surviving accumulation replace it on the next rebuild, which looked
like Load did nothing. Live series and peak hold are cleared so two measurements are not blended. A new
run is what replaces it. The capture progress read-out of a loaded capture reports its own recipe's
frame count.

## Redraw loop

- A ~30 fps timer drives redraws with a re-entrancy guard; the measurement runs on background threads, so
  a busy CPU only thins the display rate.
- A redraw clones the accumulators under the data lock and rebuilds the display curve, which is worth
  doing once per analysis **frame**, not every tick. With spatial-average frame lengths (683 ms at
  32768 samples and 48 kHz) a frame lands once in about forty ticks; the rest would clone a quarter of a
  megabyte to an identical curve while contending with the audio thread. `lastDrawnFrameCount` lets such
  ticks skip. Notices still update every tick: an overload is a shortage of frames.
- `RebuildModel` prefers a freshly computed snapshot (accumulators survive a stop) so a scale switch
  picks up curves the stored snapshot lacks, falling back to the last drawn one. It rebuilds even while
  running because display options such as coherence add or remove an axis.
- A padded loopback puts a live transfer above 0 dB, so the default view ceiling is raised expand-only
  from the live series' maximum (`LiveDisplayMaxDb`); overlays in the same model must not steer it.
- **OxyPlot ownership:** an element belongs to one `PlotModel` at a time. Line series are pooled and
  refilled in place (recreating them each tick was allocation churn) and detached from the previous model
  after a rebuild; removing and re-adding them each tick keeps z-order against overlays. Annotations
  (SPL view-only notice, capture progress) are created per model instead: carrying one instance across
  models threw on add, left the plot empty and surfaced later as "element already belongs to a
  PlotModel". Notices are kept in sync by remove-then-add on each tick, which prevents duplicates and
  removes them when their condition ends. The progress annotation's text is rebuilt only when the count
  changes.
- MMM shows an integration-progress read-out, because the curve stops visibly moving long before the
  average settles. While running it shows the live count; once held, the snapshot count Save will store.

## Capture document

`LiveCaptureDocument` stores one reference-free capture whole: accumulated FFT bins, the recipe that
turns them back into the curve, the corrections already applied and the drawn curve. One payload serves
a standalone file, an overlay slot and a Virtual DSP attachment, so there is one parser, validator and
version story.

- **Bins, not the drawn curve.** A dB SPL trace is a band-power integral over a fixed 1/12-octave band,
  clamped to where a whole band fits inside the resolved spectrum. Re-gridding the drawn curve would hold
  its lowest band down to 20 Hz and invent a bass tail. `SpectrumDb` is dB per bin from bin 0 (DC) —
  never trimmed at the low end, because the band integrator addresses bins by index — up to
  `StoredSpectrumCeilingHz` = 24 kHz (higher bins are unread, most of the array at 96 kHz and above, and
  the integrator's upper clamp still lands above 20 kHz). `StoreSpectrumBins` and `ToAmplitudeSpectrum`
  are inverses and define the storage together.
- **Recipe** (`LiveCaptureRecipe`). Slope compensation is a curve, not a slope, and depends on frame
  length, window and rate together: the same "compensation off" is worth −13.3 dB at 20 Hz on a
  16384-frame Hann capture at 96 kHz and −7.1 dB on a 32768-frame rectangular one. Without the recipe a
  stored curve cannot be corrected, only guessed at — which is how a plausible bass tilt gets equalized
  out of a system that never had it. Window ENBW and main-lobe width are stored even though derivable,
  because the integrator divides by the first and widens its band to the second, and a reader must use
  the capture's numbers, not today's derivation. `FrameMilliseconds` is informational (a rectangular
  window resolves 2/T Hz whatever the rate). `IntegratedSeconds` is the honest measure of a walk (ten
  seconds and ninety seconds along one path are different measurements). `SplAnchorOffsetDb` may be null:
  a set is levelled against the impulse responses by one offset anyway; null only means captures from
  different analyzer sessions cannot be mixed. `CaptureSessionId` is persisted for future set checks.
- **Corrections stored both ways** — as recipe fields and as applied per-point arrays — so a reader that
  cannot reproduce the pipeline can still undo exactly what was applied:
  - `TiltCompensationDb`;
  - `CalibrationCorrectionDb`, in the sign convention of `CalibrationFile.GetDecibelCorrection` (the
    pipeline subtracts it; undo by adding). The calibration is also stored as a curve, not an id, which
    means nothing on another machine. `CalibrationIsAggregate` marks an array whose positions were
    corrected by different files: undoing stays exact (the correction is measured as the difference
    between corrected and raw averages), but replacing it with one curve is wrong by the spread of the
    files. Older files omit it and read as false, which is what they were.
  - `ProtectiveHighPassCorrectionDb`: the hardware protective high-pass is divided out of a sweep's
    transfer response but carried by a reference-free capture. Uncompensated, a tweeter capture reads low
    by the whole filter slope (24 dB an octave below a 2 kHz, 24 dB/oct corner). NaN where the filter took
    the signal below what can be recovered.
- **Grid.** The drawn curve has `CurvePointCount` = 1024 points on a log grid between `GridStartHz` and
  `GridStopHz`, stored because the integrator's clamp moves the start with frame and window. Consumers
  use `ToCurvePoints`, `FrequencyAt` and `IndexOf` rather than reconstructing it. NaN levels are kept: no
  data below a high-pass is different from a level of zero.
- **Validation.** Bins must exist and be finite (they are the measurement); each correction array is
  empty or exactly `CurvePointCount` long; the curve may hold NaN.
- **Format marker and loading.** `Format` defaults to empty so a JSON without a `format` property fails
  the capture gate (a default of `CurrentFormat` let foreign files through to confusing recipe errors);
  `Save` stamps it. `TryLoad` reads the marker from the file head first (`JsonFormatMarker.Read`) and
  declines foreign files without deserializing — the shared Load button asks every file, and an impulse
  response is tens of megabytes. A file that declares the capture format and then fails to parse or
  validate throws, because returning false would misroute a damaged capture to the impulse-response
  loader. Saves are atomic (`AtomicFile.Write`).

## Raw RTA capture

Overlays store a raw (unsmoothed) curve and re-apply their own smoothing. `LiveRtaRawCapture` provides
that for the RTA, with the same contract as the swept primary curve:

- The **relative** RTA is FFT bins converted to dB (`DataHelper.MagnitudeBinsToDecibels`) and smoothed
  by `DataHelper.LogarithmicResample` onto the 20 Hz–20 kHz grid, so the bins are the reference and
  re-smoothing reproduces the trace. An active noise-tilt compensation is baked in per bin, as the display
  applies it, so the overlay records the curve the user saw.
- The **dB SPL** RTA has no raw form. Its level is a band-power integral
  (`DataHelper.LogarithmicPowerBandResample`) on a grid clamped to where a whole band fits (at a
  2048-point FFT and 48 kHz it starts near 59 Hz). Re-gridding onto 20 Hz would invent a flat bass tail,
  so an SPL capture stores the drawn curve, as the swept frequency response already does.
