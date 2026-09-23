# Live Spectrum

The Live Spectrum mode is a real-time analyzer. With a loopback reference it shows a dual-channel
transfer function (with coherence); without one, or when RTA is selected, it shows the
reference-free microphone spectrum (RTA). The MMM mode accumulates an RTA as a moving-microphone
spatial average and saves it as a capture document (see [spatial-average.md](spatial-average.md)).

## Code map

Everything lives in `source/LiveSpectrum/`; the analyzer itself is `Measurements/NoiseMeasurement`.

| Area | Code |
| --- | --- |
| The mode's state: the run, the accumulation it holds, a loaded capture, peak hold, the next run's filter | `LiveSpectrumSession` |
| The analyzer as a plot reads it, once per build | `LiveCaptureSetup` (`NoiseMeasurement.Setup`) |
| Mode in effect, axis, SPL anchor, tilt, smoothing, view-only, peak-hold key | `LiveSpectrumDisplay` |
| Peak-hold envelope | `LivePeakHold` |
| The curves as points, the raw RTA for overlays, the capture document | `LiveSpectrumCurves` |
| The plot's frame and series | `LiveSpectrumPlotFactory` |
| Redraw loop, pooled series, notices; the mode's view | `LiveSpectrumController` |
| Stored capture format | `LiveCaptureDocument`, `LiveCaptureRecipe` |
| Raw RTA samples | `LiveRtaRawCapture` |

The session is UI-free and is what the tests build. It announces `Changed` whenever something the form's live
surfaces read moves: a run starting or ending, a stop, a discard, a loaded capture, the routing, the SPL anchor. The
record button, Save, the calibration read-out and the settings panel's warnings follow that one event, and a device
failure arrives as `Failed`. The controller draws the session into the main plot. It is an `IModeView` beside
`AnalyzerPlot`, so entering the mode draws what the session holds before the plot restores its overlay slots, and it
is the entry point for the gestures that redraw (Record, a display option, a loaded capture). `PlotModelFactory` does
not read the live analyzer.

## Settings panel code map

The settings panel (`Options/LiveSpectrumOpt`) keeps its state in a UI-free `LiveSpectrumSettingsSession`
(`Options/LiveSpectrumSettings/`): each field as its control shows it, the user's own picks that survive what MMM and
periodic pink force on them, and what the analyzer offers now (an SPL calibration, a live curve an uncalibrated dB SPL
would hide, a loopback). Its rules move one field when another does: MMM pins its recipe, periodic pink forces a
rectangular window without overlap, the reference-free modes force the RTA on, and a mode without Silent falls back to
periodic pink. A commit (a pick in the list, a click on a box that takes one) is the user's pick; an arrow key in a
closed list only moves the field. `WriteTo` is what Apply writes: the user's picks wherever a rule forces the field.

| Reader | For |
| --- | --- |
| `LiveSpectrumSettingsChoices` | the lists with their labels (a sequence length with its duration at the rate) and where a stored value lands on them |
| `LiveSpectrumSettingsLook` | what the mode mutes, and the amber of an uncalibrated dB SPL beside a live curve and of Transfer without a loopback |
| `LiveSpectrumSettingsToolTips` | the dB SPL and Transfer tooltips that say why the choice cannot apply |

`LiveSpectrumOpt` writes every edit to the session and presents all of it back (`.Present`). `LiveSpectrumOptBoundaryTests`
keeps statics and nested types off the panel, and `LiveSpectrumOptWiringTests` drives it beside a session the test
changes the same way.

## Analysis modes and signals

- The effective mode is RTA when selected or when there is no loopback; starting is not gated on a
  loopback. In RTA-only views the transfer function and coherence are hidden, the RTA is forced on
  (even if its checkbox is off) and peak hold envelops it.
- Coherence describes the transfer estimate, so it is drawn only with the transfer function. The
  above-threshold transfer segment keeps the primary tag so current-measurement targets use it, not the
  low-coherence segment.
- `LiveSpectrumOptions.NormalizeSignalType`: Silent (ambient RTA, no excitation) is the only mode-exclusive signal. A
  transfer function has nothing to correlate without an excitation, so entering Transfer mode falls back
  to periodic pink. Every real excitation is valid in both modes.
- The live transfer curve and the RTA carry `CurveTag`s so overlays can bind to them by key and capture
  their raw form; the RTA is a curve in its own right (the only trace in RTA views and the source of a
  moving-microphone tune).

## Periodic pink excitation

`NoiseSignal.SynthesizePinkPeriod` builds one FFT-length period whose bin magnitudes are exactly
`1/sqrt(k)` from `PeriodicPinkLowHz` (10 Hz) to `PeriodicPinkHighHz` (28.3 kHz, or Nyquist) and zero
elsewhere; `Dsp.PeriodicNoiseSynthesis` chooses the phases. The period is tiled, so a rectangular frame
of the same length reads every bin leakage-free.

- **Phases.** Plain random phases (the generator before this one) give 13.0 dB of crest factor at 32768
  samples and 13.3 dB at 65536 (48 kHz). The synthesis starts from random phases at a fixed seed, then
  runs 150 passes of clip to a shrinking ceiling, take the phases the clipped period implies, restore the
  exact magnitudes, keeping the lowest-crest period. Measured crest:

  | Length @ rate | 2048 @ 48k | 8192 @ 48k | 32768 @ 48k | 65536 @ 48k | 65536 @ 96k | 65536 @ 192k |
  | --- | --- | --- | --- | --- | --- | --- |
  | Crest factor | 1.98 dB | 2.13 dB | 2.25 dB | 2.32 dB | 3.05 dB | 3.57 dB |

  Magnitudes stay exact to 1e-14 in the synthesis (the float playback buffer then rounds them at ~1e-7),
  so nothing the analyzer reads changes: H1 divides the excitation out, the RTA reads power, and the
  slope-compensation model is the same `1/sqrt(f)`. REW's periodic noise is
  optimised to a crest of 6 dB or less.
- **Not Schroeder's phases.** Their closed form reaches a similar crest in one step (2.4–2.6 dB at
  48 kHz) but the period it produces is a **chirp**: the spectral centroid of successive eighths of the
  period climbed 0.6 → 13.9 kHz. It is audible as a repeating sweep rather than noise, and each
  frequency then sounds at its own instant of the frame, so a moving microphone reads every frequency
  from a different point of its path — the one thing a spatial average must not do. Per octave band, the
  busiest eighth of the chirp's period held 0.71 to 0.97 of that band's energy; the random start holds
  0.14 to 0.25, against 0.125 for an even spread, and that is what
  `Synthesize_SpreadsEveryFrequencyOverThePeriodRatherThanSweepingIt` pins (a centroid alone could be
  held flat by bands that shift in opposite directions). The random start also keeps the centroids flat
  (2.0–3.6 kHz, no trend), reaches a **lower** crest, and its mic peaks measure 1.4–3.7 dB below the
  chirp's on four of the five cabin impulse responses.
- **Level.** The other colours are peak-normalised to 0.5 (−6 dBFS, the sweep's peak); periodic pink to
  `PeriodicPinkPeak` = 0.25 (−12 dBFS). The low crest does not reach the microphone: five cabin impulse
  responses (a tweeter, a midrange and three bass channels) convolved with the period give a 10–13 dB
  crest at the mic, against 9–13 dB for the unoptimised period. So at 0.5 the optimised period's mic
  peaks came within a dB or two of a 10 s sweep's (the unoptimised one sat 12–16 dB under), a microphone
  gain set on the sweeps had no margin left for a walk that passes closer to the driver, and a tweeter
  took roughly the sweep's power continuously. At 0.25 the mic peaks sit 6–9 dB under the sweep's, the
  mic RMS is 5–6 dB above the unoptimised period's, and the electrical RMS is about 6 dB under the sweep's. The
  Signal Generator's periodic pink is the same signal, so its `Level, %` of 50 plays −12 dBFS peak.
- **Low edge.** A `1/sqrt(k)` spectrum has equal power per octave, and a long frame resolves many
  octaves under 20 Hz: below 20 Hz sat 31% of the power at 32768 samples and 36% at 65536 (48 kHz),
  cone excursion and amplifier headroom that no display point reads (the grid starts at 20 Hz). A
  band-limited and a full-band pink rendered through both display paths — band power and per-bin
  Lanczos — at 44.1–192 kHz, 2048, 8192, 32768 and 65536 samples and every smoothing choice differ by
  0.000 dB below 1 kHz with a 10 Hz edge.
- **High edge.** Not 20 kHz: the per-bin path's kernel at the 20 kHz grid point reaches 20 kHz·√2 under
  1/1-octave smoothing, and a 22.4 kHz edge read that point up to 0.97 dB low (0.38 dB at 24 kHz); at
  28.3 kHz no point moves. At 44.1 and 48 kHz the edge is Nyquist; at 96 and 192 kHz it removes 6% and
  13% of the power, all ultrasonic.
- **Cost.** The phase search takes about 0.7 s at 65536 samples and 90 ms at 2048, so periods are cached
  per length and rate for the life of the process. More passes keep paying (300 reach 1.8 dB at 65536)
  but cost the wait at the first run of a length.
- **Clocks.** REW's RTA can monitor whether the input and output clocks match; Resonalyze does not. With
  two clocks the period drifts against the frame and each tone spreads into neighbouring bins. The
  banded display integrates 1/12-octave bands that hold several bins except at the lowest frequencies of
  short frames, so the spread mostly stays inside a band; per-bin views show it first. WASAPI and MME
  expose one interface's input and output as separate devices, so a shared clock cannot be detected
  from the device choice: play and capture through one interface.

## Clipped frames and coherence bias

- **Clipped frames.** `NoiseMeasurement` counts averaged frames in which a microphone sample reached
  `RecordedLevelMetering.FullScaleThreshold`. A clipped frame still enters the average (a walk cannot be
  repeated frame by frame, and dropping frames would reweight the path), so the count is reported
  instead: the MMM read-out appends `N clipped` in amber, and `LiveCaptureRecipe.ClippedFrameCount`
  stores it, null in captures saved before it was counted. The three settling frames are not counted.
  With overlap, one overload can land in two frames.
- **Coherence bias.** γ² averaged over independent frames reads its own weights back on pure noise: the
  floor is Σw², 1/K for K equal frames (0.25 after four). The live snapshot applies
  `SpectrumAnalysis.DebiasCoherence`, `(γ² − floor)/(1 − floor)`, as the sweep path does.
  `CoherenceNoiseFloor` gives the floor. An Infinite average weighs n frames alike: 1/n. An exponential
  average is **not** at its steady state from the start — the accumulator seeds its first frame at weight
  1 and only later ones enter with α — so its floor is
  `q^2(n−1) + α/(2 − α)·(1 − q^2(n−1))` with `q = 1 − α`, reaching the steady-state α/(2 − α) only once
  the seed has decayed. The difference is not a startup detail: at Medium, 2048 samples, 48 kHz and 50%
  overlap (α ≈ 0.021) the seed still holds 12% of the weight after 100 frames, and the floor is 0.025
  against the steady state's 0.011. Reading the steady state too early would leave uncorrelated channels
  looking coherent. Overlapping tapered frames are not fully independent, so with overlap the floor is
  still optimistic and the correction partial.

## Scale and SPL view-only

`LiveSpectrumDisplay.RendersSpl` follows the selection. With dB SPL selected but no matching calibration
(`SplViewOnly`), the SPL axis and SPL overlays show but live curves are suppressed: at raw dBFS on an
absolute axis they would read as absurd sound-pressure levels. A notice explains the missing curve,
added only when a curve really was suppressed. The record button resets the scale to relative before a
run, so this state covers idle redraws of a stale snapshot and a running analyzer losing its
calibration.

MMM never enters view-only: without an anchor it reports a relative scale and keeps drawing band
levels, because a spatial average needs the band-power rendering, not an absolute reference
(`LiveSpectrumDisplay.UsesBandPower`). The options panel colours its SPL choice amber only when
`HasDisplayableCurve` says a curve would actually be hidden.

A loaded capture is always drawn on its own axis: its levels mean what its capture-time anchor made
them mean, whatever the current options say.

## Per-run freezing and calibration

Immediately before an accumulation begins, `LiveSpectrumSession.Start` freezes on it:

- the protective high-pass (`SetCaptureProtectiveHighPass`), so the filter the curve divides out and the
  filter the saved recipe records are the same, and an edit mid-walk cannot re-tilt it;
- the microphone calibration **curve**, not its id (`SetCaptureMicrophoneCalibration`). The bins are
  re-rendered on every redraw and again on Save, so a rig calibration changed between walk and Save
  would otherwise recompute the walk and the file would name a microphone it was never taken through.

`SetProtectiveHighPass` is a separate entry point because a settings edit that leaves the audio session
alone deliberately does not reach `Configure` (reconfiguring restarts a running analyzer), yet the
next run's filter must still update.

`DisplayedCalibrationName` answers for the curve on screen: a loaded capture's own correction (by name,
which need not exist on this machine), else the id frozen on the running or held accumulation, else
null. Showing the rig's selection beside a curve taken through another microphone was the bug this
prevents.

`RefreshCalibration` runs in every app mode because an SPL anchor change invalidates the peak-hold
envelope wherever the analyzer sits; the plot is rebuilt only when Live Spectrum is visible. The capture
itself is never touched: a Silent RTA that loses SPL keeps running on the relative axis.

## Peak hold

- The envelope is held over the **displayed band curve** (frequency, dB), not raw FFT bins. In SPL the
  display sums bin powers per band, so per-bin peaks summed later would add maxima from different frames
  and overstate the band. The grid is stable across ticks and a band level is monotone in its power, so a
  per-index max of displayed dB is the peak of the band level shown.
- Because the envelope stores finished display values, any change to the display transform makes it
  incompatible and it is dropped, not max-ed against new values. `LivePeakHoldKey` captures that
  transform: scale, RTA-only shaping, the **effective** smoothing code (MMM pins smoothing Off, so the
  stored option would call two transforms the same), the SPL offset (only in SPL), and the tilt model
  (null = off is distinct from a flat model).
- The microphone calibration and protective high-pass are not in the key: both are frozen when the run
  begins, right after `LivePeakHold.Suspend`, so they cannot change while an envelope exists. Keying on the
  rig's calibration dropped valid envelopes whenever the next run's microphone was chosen mid-hold.
- `Suspend` briefly pauses tracking so ramp-up frames are not latched.

## Averaging reset and discarding data

- Applying display options restarts an Infinite average — except for a spatial-average capture, where
  the accumulation is the measurement and a checkbox must not throw away minutes of walking. The rule is
  keyed on the analysis mode, not the stored averaging speed (in MMM that is only the remembered RTA
  preference).
- `DiscardCapturedData` runs when an acquisition parameter (mode, signal colour, window, FFT length,
  overlap) changes while stopped: redrawing old data under new parameters would silently re-interpret it
  (slope compensation would re-tilt a pink RTA as if the excitation were white). A loaded capture is
  discarded too. A running analyzer needs no call; its restart begins a fresh accumulation.
- New session discards the same way. The accumulation outlives a stop and a loaded capture is state, so
  forgetting only the held curve let the next visit to the mode read the last session's run, or show its
  capture, again.
- `StopAndHoldAsync` harvests the final accumulation into the held snapshot; `AbortAsync` would stop
  without the last reading. `HasCaptureToSave` excludes a loaded capture, since re-saving would restamp
  it with this session's recipe.

## Loaded captures

A capture shown with `ShowLoadedCapture` is session **state** (`LoadedCapture`), not a one-off draw. Every rebuild (tab
switch, display option, calibration change) goes through `RebuildModel`, which redraws the loaded
capture; painting it once let the surviving accumulation replace it on the next rebuild, which looked
like Load did nothing. Live series and peak hold are cleared so two measurements are not blended. A new
run is what replaces it. The capture progress read-out of a loaded capture reports its own recipe's
frame and clipped-frame counts.

## Redraw loop

- A ~30 fps timer drives redraws with a re-entrancy guard; the measurement runs on background threads, so
  a busy CPU only thins the display rate.
- A redraw clones the accumulators under the data lock and rebuilds the display curve, which is worth
  doing once per analysis **frame**, not every tick. With spatial-average frame lengths (683 ms at
  32768 samples and 48 kHz, no overlap) a frame lands once in about twenty 33 ms ticks; the rest would clone a quarter of a
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
  removes them when their condition ends. The progress annotation's text is rebuilt only when a count
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
  different analyzer sessions cannot be mixed. `CaptureSessionId` is what `JudgeSet` compares for that
  one-session rule.
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
