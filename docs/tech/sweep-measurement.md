# Sweep and noise measurement

This document covers how Resonalyze captures a measurement and turns it into the
impulse response, transfer function and diagnostics the rest of the application reads:
the exponential sine sweep and its deconvolution, the loopback-referenced H1 estimate,
the checks that decide whether a run or a whole measurement is credible, imports of
sweeps recorded elsewhere, microphone arrays, SPL calibration, the live noise analyzer
and the impulse-response file format.

Where the code lives:

- `source/Measurements/ExpSweepMeasurement.cs` — the sweep measurement policy: run
  acceptance, averaging, deconvolution, transfer function, imports. It holds the next
  run's configuration only; the device lifecycle is behind `IAudioSessionFactory`.
- `source/Measurements/MeasurementResult.cs`, `AnalyzerDocument.cs` — one result, and the
  analyzer's open one (see [The open measurement](#the-open-measurement)).
- `source/Measurements/ExponentialSineSweep.cs` — sweep geometry (`ExpSweepSpec`),
  sample synthesis and the inverse filter.
- `dsp/TransferFunction.cs` — H1 estimate, excitation gate, coherence, GCC-PHAT.
- `dsp/TransferIrDiagnostics.cs` — compactness, arrival sharpness, pre-arrival,
  dominant band, IR start, crosstalk head gate.
- `source/Measurements/SweepRunQuality.cs` — per-run checks and the result caution.
- `source/Measurements/RecordedSweep*.cs`, `RewImportTiming.cs` — external recordings.
- `source/Measurements/ArrayMicrophoneAnalysis.cs`, `ArrayPlacement.cs`,
  `ArrayCaptureDocument.cs` — microphone arrays.
- `source/Measurements/SplCalibration*.cs` — SPL anchor.
- `source/Measurements/NoiseMeasurement.cs`, `NoiseSignal.cs`, `LiveSpectrumOptions.cs` —
  live analyzer.
- `source/Measurements/ImpulseResponseFile.cs`, `Float32SampleArrayJsonConverter.cs` — file format.
- `source/Measurements/MeasuredBand.cs` — which frequencies a response really measured.
- `source/Options/RecordSettings/` — the Record Settings dialog's state and rules (see
  [Record Settings code map](#record-settings-code-map)).

## The open measurement

A sweep result arrives five ways: a run, a recorded-sweep import, an impulse-response file,
a history entry and REW (a text export or the API). Each builds the same immutable
`MeasurementResult`: the IRs, the band asked for, swept and measured at full amplitude, the
sweep length, the timing reference, levels, and what was frozen with it (SPL anchor,
microphone curve, protective high-pass, array, audio-session diagnostics).
`ImpulseResponseFile.From` and `ToResult` convert it to and from the file, and
`MeasurementResult.Validated` is the one check a restored result passes.

`AnalyzerDocument` owns the open result for the whole analyzer: every mode, Save, Send to
REW, Time Alignment and the level meter read it. Plot builds read it off the UI thread, so a
result is swapped whole.

Every input replaces it the same way: it takes a request when it starts and installs through
that request when it finishes, and the install lands only if no request has been taken since.
Whichever input started last wins, however long each one takes.

- `TryBegin` is the request of a file load, a history entry, Virtual DSP's Open in analyzers
  and a REW read.
- `TryAcquire` is the request of a run or an import (recorded sweep, REW text export), and
  also holds the document until it installs or is released: the record button, history,
  drops and other inputs refuse meanwhile. A run clears the open result when it starts.
- `Clear` (New session) supersedes every request taken before it, a run's or an import's
  included. New session aborts a run, and a run or an import that finishes anyway lands
  nothing and enters no history.
- `ExpSweepMeasurement` keeps only the configuration of the next run, so a loaded file never
  changes what the next sweep, the signal generator or Record Settings see.
- History entries hold a `MeasurementResult` beside their preview and session; Compare, Time
  Alignment's compare record, Virtual DSP sources and the EQ Wizard take one too.

**The views follow the document.** An input installs its result and stops there. The main plot
(`AnalyzerPlot`), Time Alignment and the settings panels that preview it subscribe to the document's
`Changed`, and the plot and Time Alignment to the compare selection too. `Changed` announces
everything a view reads: the result, its name, and whether a producer holds the document. A
producer that lands is announced once; one that ends without a result (aborted, failed,
cancelled, superseded) is announced when it lets go.

- The plot and Time Alignment refresh once after the input's work (`DeferredRefresh`), so an
  input that installs and then changes the view (a history entry applies its session, a run
  selects its own calibration) draws once, with the final state. A draw the view makes on its
  own, such as a mode switch, cancels the queued one.
- While a run or an import holds the document the plot and Time Alignment keep what they show;
  the end of the hold redraws them, whether a result landed or not.
- Time Alignment reads only while it is shown; showing it reads.
- The Frequency Response panel recolours its SPL choice from the measurement it shows.

What each tab shows is the plain table `ModeCatalog`, and every mode's view options are one
`AnalyzerViewSettings`: the settings file keeps one copy and each history entry keeps the copy it
was left with (`CaptureSession`, `ApplySession`).

## Sweep generation

`ExponentialSineSweep` is pure signal generation: it exposes float samples and the audio
layer builds the playback stream. A sweep is requested by a low/high frequency and a
duration (not an octave count), so it can end below Nyquist.

**Phase alignment.** The Farina phase is `phi(i) = 2*pi*p * exp(i/N * ln(q/p))` with
integer `p` and `q` (`StartCycles`, `EndCycles`), so both ends sit on a whole cycle. The
requested band is rounded outward to those endpoints and widened by a guard band of
`DesiredGuardOctaves` (half an octave) on each side, where the fades live; the envelope is
therefore flat across the requested band. The bottom guard is often larger (few whole
cycles exist at low frequency) and the top is capped at `Nyquist * (1 - 1e-4)` so outward
rounding never aliases. `p` and `q` are computed directly rather than by iterative
widening: they share the `ln(q/p)` factor, so nudging them independently makes the band
run away. Afterwards only `q` is adjusted (cover the requested top, stay below Nyquist),
which preserves the bottom guard. Everything is `double`, so `sampleRate * q` cannot
overflow on long or high-rate sweeps.

**When the band cannot be covered.** A short sweep fails at both edges: its low edge
needs a whole cycle to exist (20 Hz is unreachable in under 50 ms), and its fades, padded
to `MinFadeSamples` (128) so the excitation never starts on a hard edge, eat into the
band. `ExpSweepSpec.Covers` reports this so panels can warn; it allows one sample of slack
because fade lengths are rounded to whole samples. `FullAmplitudeLowFrequencyHz` /
`FullAmplitudeHighFrequencyHz` are where the envelope really opens and closes.

**Duration.** `MaxDurationSeconds` (100 s) is applied in `ComputeSpec`, the single place
that resolves a request, so a preview never promises a length the run then shortens.
`TotalDurationForOctavePace` converges in a couple of iterations because the achieved
octave span depends on band and rate and only marginally on duration. `ComputeSpec` is
pure and deterministic (previews, restores), and `FillData` synthesizes samples lazily —
restoring a measurement needs only the geometry.

**Playback amplitude.** `PlaybackAmplitude` is 0.5 (-6.02 dBFS), for two field reasons:
it matches the Signal Generator default and the live noise, so an output level set with
those tools survives the switch to a sweep (a full-scale sweep was 6 dB hotter and clipped
an interface); and a full-scale sine sweep clips on reconstruction through inter-sample
overs. The headroom costs SNR, not reported level.

**Inverse filter.** Time reversal plus an exponential envelope compensating the sweep's
shrinking time per hertz at high frequency, expressed through the (possibly fractional)
achieved octave span. `PlaybackAmplitude` is divided out twice: once for the headroom
baked into the reversed sweep, once for the attenuated excitation the microphone hears.
Dropping either lowers every result by 6 dB.

**Stretched sweeps.** `FillStretched` lays the same whole-cycle trajectory over
proportionally more or fewer samples, which is exactly what an independent clock does to
a played sweep. Because the trajectory is analytic the stretched reference is generated,
not resampled. The band moves with the stretch.

## Excitation band gate

`ExcitationBandGate` (in `TransferFunction.cs`) expresses spectral validity as Nyquist
fractions: zero outside the **achieved** sweep edges (nothing was excited), unity inside
the **requested** band (full amplitude), and a raised cosine across the fade guard bands.
The ramps must sit inside the excited guard bands: a ramp below the achieved edge
half-passes bins the sweep never reached, where Gxy/Gxx is microphone noise over the
reference's leakage skirt, which shows as large spikes just outside the sweep band. The
legacy scalar-edge overload ramps between the edge and half of it, i.e. in unexcited
territory; callers that know both bands use the gate overload.

`ExpSweepMeasurement.BuildExcitationGate` reads the edges from `ExpSweepSpec` (where the
envelope actually opens, which is above the requested edge when a fade was padded), clamps
them into order for short sweeps, and takes the rate from the sweep because an import
builds its gate before its configuration is applied.

## H1 transfer estimate

`TransferFunction.ComputeAveragedRelativeIr` sums cross- and auto-spectra over run frames
and returns `cross / (auto + lambda)`, shaped by validity weights and inverse-transformed.

- **Regularization** (`RelativeRegularization`, -100 dB of the strongest excitation bin).
  An absolute epsilon changed meaning with record level and with the number of averages
  (the spectra are unnormalized running sums), so its strength drifted between
  measurements. It does no gating work: any bin the gates pass already has at least -74 dB
  re max in the denominator, so lambda biases it by under 0.25 %; it only keeps the
  division Tikhonov-safe if the gate constants ever loosen.
- **Power gate** (`ExcitationGatePowerRatio`): bins more than 60 dB under the strongest
  excitation bin fade to zero over another 14 dB. It is only a safety net at the true
  noise floor (below -90 dB even for 16-bit loopbacks). It cannot find the sweep start: at
  measurement FFT lengths the sweep's leakage skirts keep reference power at -40..-20 dB
  re max down to DC (verified on reconstructed field captures), while genuinely excited
  bins reach -45 dB (about 36 dB of 1/f tilt over 12 octaves plus the first-octave
  fade-in). Cutting below the sweep start is the explicit edge's job — the sweep
  parameters are known, the spectrum cannot reveal them.
- The weights are real and Hermitian-symmetric (folded through `min(bin, N - bin)`), so
  applying them is zero-phase: nothing moves in time.
- The peak scan that anchors the thresholds and regularization only reads bins at **full**
  edge weight. Otherwise mains hum below or inside the ramp of a narrow sweep's start, or
  DC converter-offset splatter (which rivals sweep bins at measurement FFT lengths), would
  scale the gate and fade genuinely excited bins.
- **Coherence** is debiased by the average count before use: at 2-4 averages the raw MSC
  of pure noise reads 1/K (0.5 at K=2), straddling the thresholds the phase unwrap and
  PHAT weighting trust. It is also multiplied by the gate: below the sweep start the
  deterministic leakage repeats across runs, so raw gamma^2 reads about 1 exactly where H1
  is zeroed, and consumers would present those bins as reliable.

`ComputeAveragedMagnitude` stops before the inverse transform. A spatial average needs the
steady-state magnitude (whole decay, no gate), and deriving it from an IR would be an
inverse transform immediately followed by a forward one returning the same array. Closed
bins come back as exactly zero so "the sweep never went here" is a gap rather than a
-200 dB point. `ComputeAveragedMagnitudeAndIr` returns both from one accumulation, since
the forward transforms are the cost.

`MeasureSingleFrameCompactness` judges several targets against one reference transformed
once — the shape of an array run, where one loopback sits beside every microphone. Per
target it costs 2n+1 transforms instead of 3n; on eight channels of a 96 kHz / 20 s take,
3993 ms became 2093 ms, the rest of the saving being reused 64 MB scratch buffers. Results
are identical bin for bin, because gate and regularization depend on the reference only.
It returns compactness figures, not responses: one response at that length is 64 MiB and
eight would peak near a gigabyte, so each is measured and dropped.

## GCC-PHAT

`ComputePhaseTransformFromResponse` whitens a loopback-referenced transfer IR's spectrum
(which already carries the microphone/loopback cross-phase) to unit magnitude over the
band with energy. The correlation collapses to a sharp, low-side-lobe peak at the true
broadband delay, independent of the driver's magnitude and polarity (an inverted channel
flips the peak). It is index-aligned with the IR so envelope-peak lags refine directly;
`PhaseTransformCorrelation` refines any number of coarse lags without recomputing.

- **Coherence weighting.** Optional half-spectrum gamma^2 from the same transfer FFT
  scales each in-band bin, so bins whose phase does not repeat across averages (noise,
  drifting distortion, non-averaging reflections) have less say; stationary harmonic
  distortion reads coherent and is kept. It is applied only on an exact length match — a
  different grid would misattribute SNR, and a no-op is safer. `CoherenceWeightFloor`
  (0.25) makes it floored-linear rather than a bin selector: sub-sample precision follows
  the Cramer-Rao bound (~1/(SNR * B_rms^2)) and comes from broadband phase agreement, so
  every in-band bin keeps at least a quarter of its weight, preserving occupied bandwidth
  and avoiding spectral holes that ring back as side lobes, while still demoting
  untrustworthy bins 4:1. The complement form `1 - (1 - floor)(1 - g2)` is exactly 1.0 at
  unit coherence for any floor; NaN maps to the floor.
- **Soft band mask.** Bins fade in over a raised cosine between two fractions of the
  reference peak; a brick wall would ring into side lobes that bias refinement.
- **FFT length.** Padded to a power of two to keep MathNet on the radix-2 path (other
  lengths silently use the much slower Bluestein algorithm); zero-padding does not move the
  peak.
- **Refinement** (`RefinePeakLag`): Lanczos windowed-sinc upsampling around the integer
  extremum, then one parabolic step with the same interpolator. The band-limited peak is
  sinc-shaped, so sinc interpolation is unbiased where a raw 3-point parabola is not. The
  extremum is taken by magnitude and its sign kept, so an inverted arrival refines to its
  trough rather than a neighbouring positive side lobe. A peak pinned to the search window
  edge is reported as not refined.

## Run acceptance

A measurement averages several runs. Each run is checked **before** it enters the average
so a bad capture cannot contaminate it irreversibly.

**Per-run checks** (`SweepRunQualityCheck`) are limited to unambiguous failures: clipping,
a dead signal (`SilentPeakThreshold`, about -80 dBFS: unplugged cable, wrong channel, dead
device) and an undersized capture. Statistical outlier checks (peak delay vs median, IR
correlation against a reference run) would need thresholds calibrated on real multi-run
captures and are not implemented. A quiet-but-present loopback is not a failure: transfer
estimation is scale-invariant, and the readme itself tells users to turn playback down.
The whole capture is judged, including the pre-playback roll, because that is what is
analysed. A full-scale loopback is normal (it is the reference).

**A bad run stops the measurement.** There used to be one automatic retry; in the field it
never recovered anything, because what the checks catch — wrong gain, wrong socket, a
missing channel — is configuration, which the next sweep reproduces exactly. Silently
dropping the run instead would leave an average built on fewer runs than requested.

**Array microphones are judged exactly like the measurement microphone**, and any
compromised channel fails the run. The rejected alternative (drop that microphone from
that run) produced measurements that looked complete and were not: the array keeps only
each position's curve, so a position that lost its runs silently vanishes, and six
positions where seven were set up is a different measurement under the same name. A sweep
is cheap. For the same reason `NormalizeArrayChannels` refuses array channels colliding
with the measurement inputs rather than dropping them.

Level faults are checked first for every channel (a clipped channel has already failed),
then `AddIncredibleResponses` asks whether each channel divides into a response at all.
This matters for the measurement microphone too: a run that recorded noise instead of the
sweep passes every level check, hides in the H1 average (the good runs still put an
arrival in it) and scales the whole result by the fraction of good runs — -2.50 dB for one
bad run in four — on the channel every other is compared against. The loopback is
transformed once for all channels (see `MeasureSingleFrameCompactness` above). Refusals
name the **configured** input: capture indices are relative to the first opened channel,
so an ASIO rig on inputs 6, 8 and 9 arrives as 1, 3 and 4.

**Whole-measurement failure.** If the run that stopped the measurement was rejected on shape
(a "credible response" issue), that is the bad-loopback case, and `DiagnoseTotalFailure`
analyses that one rejected capture for the richer diagnosis
(quiet loopback means bleed instead of the wire; which channel distorts). It throws that
diagnosis; the caller's generic refusal remains the fallback for captures that will not
analyse or plain level faults. It builds no array curves, since those would refuse on the
very fault being explained.

## Shape gate

`RequireCredibleTransferIr` refuses to publish an averaged transfer IR that does not look
like an impulse response. A genuine measurement is a localized event; a capture whose
reference was unusable (field case: a "loopback" that was playback bleed instead of the
wire) divides into stationary noise across the buffer. The gate is scale-invariant, so it
cannot punish a legitimately quiet capture. It is fail-closed: a shape that cannot be
measured (degenerate or non-finite content) is a refusal, since a NaN would otherwise pass
every comparison. A named distortion culprit replaces the generic wiring advice; the two
never both apply.

`DescribeResultCaution` (the pre-arrival notice) runs after the refusal, never inside it,
because the total-failure diagnosis also calls the refusal check.

For imports, the compactness floor applies together with `MinimumArrivalSharpnessDb`, and
`RefuseImportedRecording` gives none of the wiring diagnosis: a shapeless IR there means the
file is not a recording of this sweep. The
message depends on which gate failed — quoting a compactness figure for a sharpness
failure would show a number inside the passing range.

When the protective high-pass is divided out, its group delay goes too, so a live
measurement's corrected arrival may move; an import is rotated back to its 10 ms origin.

## Distortion diagnosis

H1 divides the microphone by the reference and believes whatever the reference says was
played. A reference driven past its input stage produces a garbage transfer function while
every level check passes — analog distortion does not need full scale.

- `DistortingChannelDb` (-26 dB, 5 %): harmonic packets 2..5 relative to the linear one.
  A wired loopback reads far below -40 dB and a loudspeaker through the air tens of dB
  down, so this accuses nothing healthy. The field case: an overdriven loopback input
  carried 25 % second harmonic (-12 dB) while the microphone read 0.24 % (-52 dB), and the
  loopback peaked at -14.6 dBFS, so every level check and the meter saw a normal reference.
- `SuspiciouslyQuietLoopbackDbFs` (-30): a wired reference is metered near full scale; the
  field garbage set peaked at -33..-49 dBFS. This flags a likely culprit in the message
  only — a cleanly attenuated wire measures fine and must not be refused on level.
- Harmonics are read per run while that run's deconvolution is in hand, so the refusal
  path never allocates FFT-sized buffers while the averaged IRs and retained frames are
  alive. `MeasureDistortion` never throws: the loopback deconvolution exists only for the
  hint, so its failure costs the hint, not the measurement.
- **Verdicts** (`ClassifyDistortionReading`): *Distorting* stands on any coverage, since
  unread orders could only add to it. *JudgedClean* needs every requested order readable
  and the total packet energy below the threshold. Anything else is *Unjudged*: the noise
  floors bound the background beside the packets, not inside them, so up to
  `floor * 10^0.6` can hide in an undetected packet, and an unread order can hide anything.
- **Wording.** The tally keeps the worst figure, how many runs crossed the threshold and
  how many could be judged — one bad run of eight, and "the other seven read clean" versus
  "could not be read", are different stories. Companion facts (the microphone reading,
  loopback level) are taken from the run that produced the worst figure
  (`WorstLoopbackRun`), because aggregate levels are maxima over runs and would pair facts
  no single capture showed.

## Diagnosis size bounds

`MaxLoopbackDiagnosisFftLength` is 2^22: two 67 MB spectra plus a 34 MB result, about
170 MB of transient scratch per run, reached around a 21 s sweep at 96 kHz (10 s at
192 kHz, 43 s at 48 kHz) — several times any field sweep so far (3.2 s). That cost is
accepted below the bound because the microphone's own deconvolution is the same size and
always ran per run. Above it the loopback reading is skipped and a refusal falls back to
the level heuristic and the microphone reading.

`RunCredibilityDiagnosisFits` applies the same ceiling to the per-run credibility check,
whose H1 pads to twice the capture. At 2^22 bins its scratch is already hundreds of MB,
and accepted configurations reach 2^24 (384 kHz, 20 s) and 2^27 (384 kHz, 100 s). Above
the bound only the level checks judge a run; the averaged verdict still runs because its
transform was needed anyway. Lengths are computed in `long` so an absurd configuration
reads "does not fit" instead of throwing.

## Transfer IR diagnostics

`TransferIrDiagnostics` holds record-hygiene measures shared by the manual Time Alignment
mode, the auto-delay launcher and the measurement's own gates: whether a transfer IR is a
credible response, where the driver's energy lives in frequency, where its acoustic
content starts, and whether its head carries a playback-crosstalk click (field evidence: a
broadband spike at one fixed sample in every record of a session, an electrical copy of the
playback that lands before any possible acoustic arrival).

## Compactness

`MeasureCompactness` returns how far (dB) the per-sample energy of a short circular window
around the strongest peak sits above the per-sample energy outside it. A genuine IR is the
direct sound plus at most a few hundred ms of decay; an IR from an unusable reference is
division noise across the capture. The verdict rests on energy geometry only, with no
peak-position rule, so an electrical chain measurement peaking at 0 ms passes.
`PeakDelayMs` is signed circularly (past the midpoint reads as acausal) and is a
diagnostic, not a verdict.

`MinimumCompactnessDb` = 22 dB. Calibrated with the 100 ms / 500 ms window on 14 field
records from two cabins plus synthetic transfers through the production excitation gate:
genuine measurements read 28.8-48.6 dB, ideal band-limited transfers (even a 20-50 Hz band
sweep) 42.9 dB and up, a session whose loopback was playback bleed 11.2-18.7 dB, gated
uncorrelated noise about 0 dB. 22 dB sits 3.3 dB above the worst garbage and 6.8 dB below
the weakest genuine record — deliberately closer to the garbage side so a noisy but real
capture is not refused.

The window is circular because a zero-phase excitation gate turns even H(f)=1 into a
symmetric band-limited kernel whose pre-ringing lives at the buffer's far end. The 100 ms
pre side is sized for that ringing at the lowest supported band edges (10 ms cut a
20-50 Hz ideal kernel to 19.9 dB, a false rejection); the post side covers cabin decay and
processing latency. Both shrink on short records so the outside stays the majority. NaN or
infinity yields null, which callers treat as a refusal. The `Complex` overload reads
through a real-part view (a copy would be 34 MB at 96 kHz / 20 s).

## Arrival sharpness

`MeasureArrivalSharpnessDb` measures how far the strongest sample stands above the RMS of
its circular +-2 ms neighbourhood (`ArrivalWindowSeconds`). It answers what compactness
cannot: whether the IR was deconvolved against the sweep that was actually played. A sweep
deconvolved against a different sweep still looks localized in the compactness window (a
100 ms smear fits comfortably), so a mismatched band or per-octave pace passes, and on
field takes sometimes scored higher than the correct one. A peak stands above its
immediate surroundings; a smear does not, and room decay barely enters 2 ms.

`MinimumArrivalSharpnessDb` = 8 dB. Two field takes of one exported sweep (car cabin and
room), each analysed with correct and six neighbouring wrong parameters: correct read
15.5 dB (cabin) and 10.7 dB (room), every mismatch 3.1-6.3 dB. 8 dB sits 1.7 dB above the
worst mismatch, on the garbage side of the gap.

Rejected alternative: the share of energy near the arrival. A synthetic record with a
quarter-second decay held only 12 % of its energy in the arrival window, indistinguishable
from a smear, so that measure would refuse reverberant rooms.

## Pre-arrival

`MeasurePreArrivalDb` compares the per-sample energy of the stretch 100-600 ms before the
peak (`PreArrivalStartSeconds`, `PreArrivalEndSeconds`) with the arrival's neighbourhood,
circularly. It asks whether the ring is causal: nothing arrives before the direct sound.
The field pair it was written for read -14.8 and -14.1 dB where clean records from the same
rig read -39.0 and -44.3 dB — and both had cleared the compactness floor at 26.0 and
24.1 dB. The session behind it spent an evening on eleven takes whose reference ran through
an interface's direct mixer: levels normal, coherence 0.9995, all runs accepted, and the
two worst records carried a 34 Hz resonance of Q 38-54 that rang for seconds with half
its energy before the arrival.

**Why a separate measure.** A resonance imposed in magnitude only shows up here; a
minimum-phase one of the same depth does not, because it rings forward. Compactness reads
the two identically (24.68 vs 24.65 dB). A cabin's own resonances are minimum-phase, so
this measure can carry a threshold a reverberant cabin cannot trip.

**Against the arrival, not the record.** An electrical measurement peaks at zero (so
"before" is the buffer's far end) and an acoustic one tens of ms in. A share-of-total
measure moves by tens of dB with that delay (an ideal kernel at zero delay puts half its
energy in wrapped negative time) and cannot hold a threshold. The far edge shrinks on
short records so the two windows cannot meet around the circle; the reading floors at
-120 dB.

**One mechanism.** An interface monitor path with the direct level pulled down and a
delayed return left up: once the delayed component exceeds the direct one the reference
stops being minimum-phase and its inverse is anticausal. The flip is sharp — with one
20 ms copy, the inverse holds 0 % of its energy in negative time when the direct leads by
1.6 dB and 100 % when it trails by 0.9 dB. The thresholds are calibrated on field records,
not on that model: a dense reverb mixed hot enough to reach the field reading also drives
compactness to 12-15 dB, where the shape gate already refuses.

**Threshold.** `SuspectPreArrivalDb` = -22 dB, scaled by imposing a magnitude-only
resonance at 34.5 Hz, Q 40, of growing depth on a clean field record (a controllable
acausal ring, not a claim about the field fault's origin): 6 dB depth reads -28.8 dB,
9 dB -24.5, 12 dB -21.0, 16 dB -17.1. -22 dB starts reporting around 11 dB and leaves
4.8 dB to the worst legitimate shape, an ideal half-octave-guarded kernel over a 20-25 Hz
band sweep at -26.8 dB. Genuine field records read -39.0 to -47.3 dB.

**A report, never a refusal.** A refusal was built on this measure and withdrawn. Telling
a fault from a record whose strongest sample is simply not its arrival needs the
pre-arrival window's crest, and that separation narrows with bandwidth — to 2.5 dB at the
effective width of one field fault. Too thin to destroy a measurement over on two records
from one rig. `SweepResultCaution` therefore publishes the measurement with a notice that
names no cause: both shapes are given, the reference first because everything is divided
by it. Naming a cause sends a tuner to check wiring that was correct all along.

**When it can judge.** `CanJudgePreArrival` withholds the verdict when the low guard band
is under `MinimumJudgeableGuardOctaves` (0.30). The gate's own kernel rings for a time set
by its narrowest transition — the low guard, narrowest in Hz. The generator aims for half
an octave and only falls short when the duration cannot open one. Ideal gated kernels:
with a half-octave guard the worst supported band (20-25 Hz) reads -26.8 dB, still at or
under -22.7 dB down to a quarter-octave guard, but -14.9 dB at 0.05 octaves — where the
broken field records read. Full-band excitation has no low-edge kernel and is always
judgeable.

## Dominant band

`DetectDominantBand` finds the contiguous region of the 1/6-octave-smoothed magnitude
spectrum within `DominantBandThresholdDb` of its peak, on a 1/24-octave log grid, using at
most `MaxAnalysisSamples` (65536, enough for 1/6 octave at 20 Hz). A smoothing window
needs at least half its bins coherent, so a lone bin cannot represent it and the edge does
not jitter.

`DominantBandThresholdDb` = 15 dB, calibrated on 7 v3 cabin records at 10/12/15/20 dB:
15 dB gives driver-shaped bands (sub 20-110 Hz, midbass 78-320, mid 110-2560, tweeter
1108-20k) with arrivals intact. 20 dB let a mid record swallow 20 Hz-14 kHz (cabin gain and
door leakage keep its shelves within 20 dB); 12 dB squeezed the midbass to 82-155 Hz and
its band-limited arrival modal-latched (7.4 -> 15.3 ms).

Expansion from the peak bridges dips narrower than `MaxBridgedGapOctaves` (0.5): cabin
cancellation notches are deep but narrow and must not cut the working band, while a wide
silence is a real edge. A bridge must land on at least `MinSolidLandingOctaves` (1/6) of
contiguous above-floor band; ripple hovering around the threshold offers only isolated
spikes, and chaining bridges through them tripled the midbass band on field records.

## IR start estimate

`EstimateIrStart` returns `IrStartEstimate`: `StartMs` is the 25 % rising-front crossing
of the first credible arrival's Hilbert envelope, read inside the record's dominant band;
`EarlyMs` (10 %) and `LateMs` (50 %) bound it. Their spread is the front's sharpness — a
fraction of a ms for direct sound, milliseconds for a modal low-frequency build-up — and
is the honesty figure to show next to a single number.

**Why band-limited.** A crosstalk click or other broadband head garbage carries almost no
energy inside a band-limited driver's band, so no head-cleaning pass is needed. v3 field
data: a click at about 0.5 ms poisoned every full-band read of the midbass and subwoofer
records (0.46-3.3 ms against fronts at 4.5-9 ms), while the in-band read landed on the
front on all seven. The arrival uses the shared first-arrival physics (25 dB depth, noise
gate, pre-ringing rejection), so a stronger modal peak later cannot usurp the front. When
the dominant-band read is invalid the estimate falls back to the full-band envelope, which
a head artifact can poison; `DominantBandLimited` says which answered.

**Band floor.** The read uses `IrStartBandThresholdDb` = 25 dB, wider than the dominant
band's 15 dB. The 15 dB floor answers "where does energy live" tightly (the crosstalk
complement needs that), but a time resolves only to about 1/bandwidth. Around a cabin mode
a 15 dB band can collapse to one octave — field case, a door woofer at the listening
position reading 75-155 Hz, with notches on both sides just too wide to bridge — and then
nothing shorter than about 12 ms resolves: the envelope's only maximum is the mode's
build-up 8.4 ms after the arrival, and the 25 % crossing lands at 3.00 ms, 2.5 ms before
the record leaves its noise floor. Calibrated across v2-v5 (23 records) at 15/20/25/30 dB:
25 and 30 agree on the collapsed record (5.64 ms, against a 6.42 ms peak and a front near
5.6 ms), 20 is transitional (4.46 ms), so 25 sits at the near edge of the plateau. Records
whose 15 dB band already spanned four octaves move at most 0.5 ms; the rest (subwoofers
included) move 0.3-1.7 ms later, towards their arrival and still before their peak. None
falls back onto the head burst: every v3/v4 read stays in the 5.2-7.5 ms front range.

**Refusals.** Null when no credible arrival exists: empty, shorter than 256 samples,
silent, or noise-only. The envelope peak-to-noise grade must clear
`IrStartMinimumSnrDb` = 20 dB (the same floor as `AutoAlignmentEngine.OnsetLockMinimumSnrDb`;
clean cabin records grade 50+ dB, pure noise 10-13 dB), because a noise envelope still has
a strongest peak. The caller keeps its previous figure.

**Valid range.** With a known valid range the analysis is restricted to measured content
but times are reported in full-record coordinates: a chain delay's silent prefix sinks the
noise-floor estimate (25 ms of zeros ahead of a short noise record lifted its grade from
about 6 to 26 dB, past the floor). The head cap counts from the range start so late
content is not truncated before its front.

**Crossing.** Walked backward down the arrival peak's own rising front and interpolated to
sub-sample; an earlier disjoint event past a dip never captures it. A front running off
the head of the record is floored at 0 — unbounded extrapolation read -1.5 ms on a field
subwoofer.

## Crosstalk head gate

`DetectCrosstalkHead` is a field-calibrated detector for **band-limited** records; a
full-range record offers no complement band and always returns null, even with a click
(measured inert on v3: engine proposals move at most 0.01 ms). It also returns null
whenever any guard fails. Analysis is capped to `MaxAnalysisSamples` so the panel, the
engine and the probes reach identical verdicts.

1. **Candidate.** The first arrival of the complement band, starting `ComplementGapOctaves`
   (0.5) above the dominant band's top and spanning at least an octave, where the driver
   has nothing to say. Sidelobe rejection keeps window pre-ring from masquerading as it.
2. **Island.** The candidate must end — the complement envelope falls `IslandEndBelowPeakDb`
   (12 dB) below its peak and stays there — within `IslandCapSeconds` (10 ms); a click is
   millisecond-scale, anything longer is sound. A genuine early front's complement energy
   rises into the room decay instead, so no end is found. The island is judged on its own
   envelope, never against later complement content: a click hotter than the driver's
   out-of-band tail, or the only complement event, is the more dangerous artifact and must
   not detect worse than a faint one.
3. **Proportionality guard.** The candidate must precede the in-band front by
   `PreFrontGuardSeconds` (2 ms), and the gate may reach at most half-way to the front. A
   genuine early arrival carries its out-of-band content with the in-band front (one
   wavefront), so only a non-acoustic event can be complement-early. This is deliberately
   not an onset-threshold walk: the click's in-band shadow and the window pre-ring ramp
   poisoned that in two field-tested attempts.
4. **In-band quiet.** The gate claims everything before it is pre-sound, so the in-band
   envelope must stay `InBandQuietBeforeGateDb` (15 dB) below its first-arrival peak over
   the whole gated stretch. That refuses a co-onset genuine burst whose in-band envelope is
   already rising where the island ends (about -10 dB on field records), while a click's
   in-band shadow sits near -25 dB.
5. **Experiment.** The island is trial-removed and the full-band first arrival re-read. Only
   a jump later than `FirstArrivalJumpMs` (2 ms, above the 1-1.5 ms band-to-band dispersion
   of one wavefront in a cabin) convicts: the record's first arrival was the head burst. If
   the read barely moves, the burst was driving nothing and the record is left alone.

`CleanCrosstalkHead` zeros `[0, GateEndSample)` and raised-cosine-fades the following
`FadeSeconds`, before any linear processing, so every downstream band-limited read is clean.

## Measured band

`MeasuredBand` says which frequencies a loopback transfer response actually measured. Two
things narrow it, for the same reason: a protective high-pass took the signal past what the
compensation can invert (those bins are zeroed), and a band sweep never excited part of the
range (the gate zeroes those). The response is exactly zero there, and a windowed spectrum
refills it with the analysis window's own leakage — smooth, plausible and unmeasured. On
the owner's tweeter a sweep from 565 Hz drew 495 of 1024 points as a roll-off from -60 to
-96 dB where nothing had played.

- Only a loopback transfer has those zeros. A sweep deconvolution is normalized by the
  digital excitation and still carries the high-pass, so its edges are real output, and
  blanking them would delete a measurement.
- The low edge uses the band excited at **full** amplitude (`MeasuredLowFrequencyHz`), not
  the achieved band. Inside the fade guard bands H1 is still unbiased (the taper cancels in
  Gxy/Gxx) but its SNR falls and the validity weight attenuates it: on a 500-5000 Hz sweep
  the weight reads -13.0 dB at 400 Hz, -2.7 at 450 and -10.3 at 6300. Calling that
  measured would draw the estimator's roll-off as the driver's response.
- A null filter means "not recorded", not "off": nothing is masked, rather than breaking an
  old curve at the corner of a filter it may never have passed. An absent sweep band is read
  the same way. The two limits combine by taking the tighter one. A default-constructed
  zero high edge means "not narrowed".
- `MaskUnmeasured` also breaks a summed curve in holes between contributors: two
  non-overlapping sweeps leave a range where every contributor is zero at once.
- Files written before the full-amplitude edges were recorded fall back to the achieved band.

## Importing a recorded sweep

`ExpSweepMeasurement.ImportRecordedSweep` publishes a sweep recorded outside Resonalyze.
The sweep the configuration describes — the same signal the options panel exports as WAV
(`SweepWavExport`) — stands in for the loopback: the excitation is known exactly. The
analysis is then the live one: deconvolution plus the gated H1 estimate.

- The import touches no engine state: its configuration describes the sweep in the file,
  not the next run, so it generates its own sweep and hands back a `RecordedSweepImport` (the
  result, the channel measured, any time-scale correction). A rejected import throws before
  anything is installed, so the open measurement stays.
- The file import holds the document (`AnalyzerDocument.Acquire`) from before the decode:
  the analysis runs off the UI thread, and the record button is a click away.
- The result is stamped with the import time: the recording carries no time of its own.
- Only the excitation and its decay are analysed (`RecordedSweepWindow`). People start the
  recorder, walk to the seat, play the sweep and walk back, leaving tens of seconds of
  silence; every FFT is sized by its input, so a five-minute file holding a two-second
  sweep would cost gigabytes of spectra and a transfer IR of the same length. The window
  keeps 0.5 s of lead-in (room for the gate's left shoulder) and a 2 s tail (0.5 s covers a
  cabin, 2 s a live room). Candidate spans come best match first and are analysed in turn
  until one yields a credible IR, because a match is evidence, not proof; the first
  credible take wins. Each span covers one attempt: other takes are cut away, since a second
  excitation reads as an enormous reflection that either fails the shape gate or wins the
  arrival. Only takes that finish before or start after this one are cut on. There is
  always at least one span.
- Span length is measured from the excitation start, not the span start, or a take with
  pre-roll that stops mid-sweep would look long enough.
- The same capture-failure checks as a live run apply; a clipped sweep still deconvolves
  compactly (full of harmonics), so the shape gate cannot catch it.
- When the excitation is found too near the end, the message names both readings: a
  normalized match separates a clean take (1.0) from noise (0.02), but a noisy real take
  reads 0.06 and a recording of a different sweep 0.21, so no threshold picks between them.
- An import carries no absolute time (see `TimingReference.RecordedSweep`), no SPL anchor
  (`Init` clears it; the recording chain gain is unknown) and no array.

`RecordedSweepFile` bounds the decode (ten minutes, with a byte cap that holds during
decoding, so a file lying about its duration stops at the budget).

## Locating a recorded sweep

`RecordedSweepDetector` finds the excitation by matching the sweep against the recording,
not by level. A level detector is a proxy content defeats from both sides: a system that
barely reproduces one end of the band reads as a sweep starting a second late, and a voice
or door louder than a quiet measurement hides it. Matched filtering answers with the start
sample, and from far down: its gain is the time-bandwidth product, about 46 dB for 2 s
over 20 Hz-20 kHz. `SweepMatch.Quality` is the normalized correlation; 1 means nothing but
the sweep, and a genuine acoustic take reads a few tenths.

- **Coarse search.** Both signals are averaged and thinned identically (the average is a
  crude anti-alias filter, but identical on both sides is all a matched filter needs), so
  the peak keeps its place and only broadens. Decimation aims at `SearchSampleCeiling`
  (2^20 samples) but stops while the sweep is still a chirp; a 53 ms sweep (5 ms per
  octave) stops it at 2. So the search is also chunked (`MinimumSearchChunk` 2^18) and each
  chunk is decimated straight from the recording, keeping memory at a few MB regardless of
  file length. The answer is refined at full rate within two decimated samples.
- **Normalization** uses cumulative energy per chunk, so shape rather than level decides and
  a quiet channel holding the sweep outranks a loud one full of hum.
- Placements running off the end are included down to half overlap: a take stopped
  mid-sweep has its true start only there, and excluding them yields the best wrong position,
  which then reads as a complete take. The last chunk owns all remaining placements;
  otherwise the loop re-transforms the tail one sample at a time.
- **Separation.** Matches closer than half a sweep (`SeparationShare`) are one arrival
  reported twice (a strong reflection rides beside the direct sound). The candidate pool
  keeps one entry per neighbourhood and only compares with its most recent entry, because
  placements arrive in order; scanning the whole pool was quadratic on short sweeps (a 36 ms
  sweep in a ten-minute file took 143 s). A final pass settles replacements and runs before
  refinement, while pool and separation are still in decimated units.

## Multi-channel recordings

`RecordedSweepChannels` ranks channels by how well they match the sweep, not by level: a
recorder often delivers one live microphone beside a dead input that is rarely silent (hum,
preamp hiss, a cable picking up the car). The rank cannot finish the job: a DAW export
often carries the played sweep on one track beside the microphone, and that track matches
perfectly and measures as a flat, credible, meaningless response. No number separates the
reference track from the take, so when more than one channel plausibly holds the sweep
(`IsAmbiguous`) the UI asks the user; the "best match" overload is only for callers with no
one to ask. The chosen channel is reported as `ImportedChannelIndex`.

`AmbiguousShare` = 0.25 (runner-up within 12 dB of the best). A microphone beside a played
reference reads 0.77 and 0.85 of it on two synthetics; a channel with no take reads 0.031
and 0.032 (dead channels of two field files) and 0.004 for hum. In absolute terms real
takes score 1.00, 0.93 and 0.42; hum 0.05, hiss 0.01, a recording of a different sweep
0.10. The rule sits between two clusters a factor of ten apart, which makes a threshold
defensible here where one on absolute quality is not.

## Import time scale

A recording can be slightly out of scale with the sweep it is analysed against, for two
indistinguishable reasons: the playback and recording clocks are separate crystals (tens of
ppm apart), and the per-octave field is whole milliseconds, so the exact duration that
produced a file often cannot be typed back in. On a field pair, the sweep the panel could
express ran 489 ppm longer than the one the exported file proves was played, while the
clocks agreed within 25 ppm.

`EstimateTimeScalePpm` finds the reference stretch (built with `FillStretched`) that makes
the deconvolved arrival sharpest, and reports it as `ImportedTimeScalePpm`:

- The objective is arrival **sharpness**, not height, so a wandering recording level cannot
  tilt it, and it runs on the sweep deconvolution alone — half the cost of a full analysis,
  and the part a scale mismatch smears.
- `MaximumTimeScalePpm` (800) covers both causes and stays far from a neighbouring sweep
  rate, which is percent away, so the search cannot walk into one.
- A coarse scan (`CoarseScaleStepPpm`, 100) precedes refinement. Hill-climbing from zero
  fails: the objective dips on the way to its peak — for a recording 500 ppm out, the
  200 ppm probe reads worse than no correction. The objective rises over about 200 ppm each
  side of the truth, so a 100 ppm step cannot skip the peak. The scan and refinement cost
  about two dozen deconvolutions, a second or two once per import.
- Refinement stops at `FinestScaleStepPpm` (12.5 ppm, 50 µs over a 4 s sweep), where the
  objective says more about the room than the scale.
- A gain under `MeaningfulScaleGainDb` (0.5 dB) reports zero: on a field take sharpness
  wandered by a few tenths of a dB across +-50 ppm.
- The reference is the sweep at the start of a view as long as the analysed window: the
  estimator truncates both signals to the shorter one, so the bare sweep would discard the
  room decay. Both sides are views (`PaddedExcitationView`, `RecordedSamplesView`) because
  the estimator fills its own FFT buffers; materialized copies would add full-length signals
  beside spectra that already dominate memory (157 MB at the longest supported sweep).

## Imported arrival placement

An imported IR's raw arrival position is the recorder's start offset — 730 ms on one field
take, 1220 ms on another — and absolute read-outs such as group delay (referenced to the
IR start) would inherit it. Since that origin means nothing, the whole IR is rotated to
place the arrival at `ArrivalPlacement.PlacedArrivalSeconds` (10 ms). The rotation is circular
because the transfer IR's acausal pre-ringing lives at the buffer's far end. Delays within the
measurement are untouched. Not zero: an honest arrival start sits a little before its peak
(a low-frequency front can build for milliseconds), and at zero it would wrap to the end of
the buffer, where the automatic gate would read a delay of nearly the whole record.

## Arrival ahead of the loopback

Nothing reaches a microphone before the signal that drives it, so on one audio device a loopback
transfer IR is causal: the arrival sits a few milliseconds into the circular buffer. When the
microphone heard the sweep first, the loopback does not time the measurement. The stamp records that
fact, not a cause. The usual cause is a driver that joins two interfaces (ASIO4ALL, FlexASIO, an
aggregate device) with the microphone on one and the loopback on the other; each starts its stream
with its own buffering, so the offset is set anew at every start. A loopback path that adds latency
the loudspeaker's does not reads the same. The 12-microphone set of #214 (UMIK-X for the microphones, an RME for
the loopback, clocks locked over optical) put the arrival 39 to 52 ms ahead, a spread of 13 ms
between measurements where the cabin explains 2 or 3. Within one measurement the offset held: the
tweeters' coherence over five runs read 0.995 at 8 to 16 kHz. So the shape is real and the
position is not, which is what `TimingReference.RecordedSweep` already means for imports.

`ArrivalPlacement.Judge` files such a result as `TimingReference.NonCausalLoopback` and places
its arrival like an import's. It reads the transfer peak, and the split is exact, not a threshold: H1
pads both records of length L to at least 2L, so a delay (a lag from 0 to L−1) lands in the near half
and an arrival ahead of the loopback (−L+1 to −1) wraps into the far half; a delay longer than the
record cannot be measured at all. A REW import is the exception, since its buffer is REW's: there a
delay past half of it would read as ahead. It runs where a run is built (`ExpSweepMeasurement.BuildResult`, which then publishes
the notice through `SweepResultCaution`) and where a file is read (`ImpulseResponseFile.ToResult`,
which the EQ Wizard's file source reads too), so files saved before the check are re-filed on load
and saved back re-filed; a history row redraws its preview from the re-filed IR. Without it the
arrival stayed at the far end: the IR start estimate reads only the head of the record and found
a start in the wrapped tail, every magnitude window opened 40 to 50 ms after the direct sound, and
the curves sat 13 to 43 dB low and matched none of the set's twelve microphones.

What follows from the stamp is `TimingReferences.HasAbsoluteTime`: Virtual DSP and Time Alignment
refuse the result, and a compare view shares no time base with it, as for an import. A positive
offset of the same origin cannot be told from a long path from one file; only the microphone that
times the measurement and the loopback on one device make the timing real.

## REW import timing

`RewImportTiming` decides what a REW text export may claim about time. The earlier design
tried to prove from the file that REW's timing offset was zero, which the file cannot say:
the header of a measurement taken with an offset is identical to one without, and the
offset is folded into the start time. Its only visible symptom, an arrival preceding the
reference, appears only when the offset exceeds the arrival, so quiet cases slip through.

The offset is therefore asked for. A stated value (zero included) is a user assertion: the
import is compensated and stamped `SynchronizedLoopback`. "Unknown" is a different
measurement — shape real, position not — and is stamped `RecordedSweep`, with nothing to
check. If a stated offset puts the arrival before the loopback, which is physically
impossible, the refusal reports the offset that would make it physical. The logic lives
beside the enum, like `SampleRateOptions.Resolve`, so it is testable without a window.

The API route (`RewMeasurementImport`) feeds the same decision, and `PrepareAsync` runs every
check before the picker closes, so a refusal leaves the list open. What REW 5.40 Beta 134 /
API 0.9.7 serves, measured against a loopback sweep of the same microphone position taken
by both programs:

- **The offset is recorded** for a sweep REW measured: the summary and the IR object carry
  `timingReference: "Loopback"`, `timingOffset` (seconds) and `delay`. The picker fills the
  offset from `timingOffset` and still lets it be changed; a measurement imported into REW
  has `timingReference: "No timing reference"` and no offset, so it is asked for. The sign is the
  text route's: a sweep taken with a 4 ms offset reads its peak at -3.6153 ms with `timingOffset`
  +0.004 and `cumulativeIRShiftSeconds` 0, and imports at 0.3847 ms, 0.003 samples from the same
  position taken without an offset (r = 0.9994).
- **Time base.** With offset 0 the imported arrival sat 0.004 samples from Resonalyze's own
  measurement (xcorr, r = 0.996 over 85 ms), same polarity.
- **Scale.** `unit=percent` is REW's default and is requested explicitly: a sample sent as
  0.8 reads back as 80, so samples are divided by 100. REW then scales to digital full scale,
  not to the loopback: the REW measurement at −12 dBFS came out 12.0 dB below Resonalyze's
  (±0.1 dB from 125 Hz to 16 kHz). The picker's sweep level takes that back out
  (`PrepareAsync` multiplies by `10^(-level/20)`), after which the same pair agrees within
  0.11 dB. No measurement field states the level, so the picker starts at REW's current
  setting (`GET /measure/level`, used only when its unit is dBFS). The relation holds for a
  digital loopback; an analog one adds its converters' gain.
- **Text route.** A text export holds fractions of full scale: an unnormalised export equals
  the API's percent divided by 100 to -144..-151 dB of the peak on four REW 5.40 b134
  measurements. A normalised export is multiplied back by its `Peak value before
  normalisation` and then matches the unnormalised export of the same measurement to
  -140..-150 dB, so it is accepted; one that states no positive peak is refused. Its excitation line states the level
  (`at -12.0 dBFS`), which is taken out without asking. The header's band and sweep length do
  not give REW's rate — 0.4 Hz to 20,000.2 Hz over 256k puts H2 at -174.9 ms, measured at
  -138.6 ms — so the rate is read from the harmonics on both routes.
- **Address.** REW listens on 127.0.0.1 only, and `localhost` resolves to ::1 first; the
  refused IPv6 connect took 2037 ms, past the 2 s probe, so a running REW looked absent.
  `RewApiClient` connects to 127.0.0.1 whenever the address says `localhost`.
- `GET /measurements/selected-uuid` is a JSON string.
- **Band.** The summary's `startFreq`/`endFreq` are REW's response range: 0.366 Hz (one bin) to
  20,000.244 Hz for a 0–20 kHz sweep at 96 kHz, 2.197 Hz to 24,027 Hz on a 48 kHz measurement in
  REW's own example. They become the measured band with the top clamped to Nyquist, falling back
  to 20 Hz–Nyquist when absent. The fallback let REW's content above its sweep through as measured:
  at 31.5 kHz it sat 70 dB above Resonalyze's gated response, 43 dB above its own noise, so it is
  correlated leakage, not noise.
- **Sweep length.** REW states none, so the harmonic geometry comes from the packets themselves
  (see docs/tech/dsp-ess-harmonics.md#sweep-rate-from-harmonic-positions); without a packet the IR
  length stands in, as for a header without one. The sweep count is not stated and one is assumed.
- **IR shift.** REW's *Offset t=0* moves the axis like a timing offset: on a copy of a measured
  sweep, +2 ms read `cumulativeIRShiftSeconds` +0.002 and moved the peak time by exactly -2.000 ms,
  so the shift is added to the stated offset before `RewImportTiming` (an unknown offset stays
  unknown). The copy itself had lost its loopback reference, so the shifted original was not imported.
- **No impulse response.** A magnitude-only measurement answers the IR route with 400 and
  `... does not have an impulse response`; REW's message is shown rather than a generic failure.
- **Offset per row.** The picker keeps what the user set per measurement UUID; a row takes that,
  else REW's `timingOffset`, else 0, so no row inherits another's offset.
- **Level source.** `/measure/level` may answer in dBu, dBV or V, or not at all; the field is then
  left as it was and marked amber rather than presented as REW's.

## Array microphones

A measurement can record further microphones on the same device for a spatial average.

**True transfer functions.** Each array microphone has the loopback beside it sample for
sample, so it is read as a transfer function, not a bare deconvolution. A deconvolution is
normalized by the digital excitation: raising the playback level between two channels'
measurements would lift one channel's array curve while its loopback-normalized IR stayed
put, and the array would disagree with the IRs about relative levels — fatal for a hybrid
view. `ArrayMicrophoneAnalysis` uses the same H1 estimator, gate and regularization as the
measurement microphone, so curves differ only by position. No loopback means no array; that
is refused for the whole measurement up front.

**The measurement microphone is in the array**, as a position like the others and the only
one tied to the SPL calibration. It is marked because it is the anchor the others are
levelled onto. Its array curve gets no credibility verdict: `RequireCredibleTransferIr`
judges those frames with a better diagnosis, and refusing here would blame an array
microphone for the reference's fault.

**Credibility of array channels.** Level checks catch silent, clipped or short channels, not
one that is live and wrong — an unused preamp hissing at -40 dBFS, a wrong socket, a failed
capsule. Such a channel divides into an H1 with no arrival, and the spatial average would
trim its median onto the anchor and give it a full share of weight: a plausible curve, and
a tune fitted to nothing. It is refused on `MinimumCompactnessDb`, fail-closed, naming the
input (otherwise the user unplugs seven microphones in turn). `DescribeIncredibleShape` is
the single verdict shared by the per-run check (which measures shapes through
`MeasureSingleFrameCompactness`) and the averaged backstop (via `DescribeIncredibleResponse`).

**Per-run floor.** `RunFloorDb` lowers the floor for one run of an N-run average by
`10*log10(N)`. Averaging leaves the coherent arrival alone and divides the uncorrelated rest
by N, so runs each reading R average to somewhere in `[R, R + 10*log10(N)]` (R when the
surroundings are room decay, the upper end when they are noise). On archived cabins genuine
records read 27.7 dB and up against 22 — under 6 dB of margin, which four runs of averaging
can account for alone. Gated uncorrelated noise still reads about 0 dB.

**Raw storage.** Array levels are stored with the protective high-pass divided out and
without microphone calibration, so the calibration can be changed later and the view's
calibration switch still means something. The high-pass must be removed because it sits in
the user's DSP after the loopback tap: every microphone hears it while the transfer IR
already had it removed, and a tweeter's array curve would otherwise sit a filter slope under
its IR (24 dB/octave below a 2 kHz corner). The same model, cap and fade as the IR path are
used; bins where the filter cannot be inverted become NaN (nothing to recover, nothing an
equalizer should fill). Each microphone carries its own calibration curve, because an array
need not be one capsule model; notes and calibrations are paired with measured curves by
channel, not list index, because a microphone that failed every run is absent.

## Array placement

`ArrayPlacement` levels an array onto the anchor once and exposes both readings: each
position through its own calibration, and the same positions with those corrections
removed. A trim answers "how much more sensitive is this capsule", measured as the median
difference from the anchor over the driver's working band, and it is answered on the
calibrated curves, where a level difference is a level difference. Turning a view's
calibration off asks to see different curves, not to re-place the microphones. When the
frequency-response view and Virtual DSP each derived their own placement, a mixed array
parted by 3 dB with both looking reasonable, and `CalibrationCorrectionDb` (add it back to
get the measured level) could not hold.

`CorrectionDb` is measured as the difference between the two averages, which is exact for
matched and mixed arrays alike because both stand on one placement. Where nothing was
measured it is zero, not NaN, so the gap does not spread into whatever undoes it. The anchor
is the measurement microphone; a set without one levels onto its first. A curve not on this
build's grid cannot be placed.

## Array capture document

`ArrayCaptureDocument` hands an array to the Virtual DSP hybrid and the EQ wizard as a
`LiveCaptureDocument`, the shape they already consume for moving-microphone captures —
a spatial average is a spatial average.

- Calibration is applied per microphone through `ArrayPlacement`: a consumer wants the
  driver's response, not the microphones' colouring. The document names a calibration only
  when every **placed** microphone shares one; a mixed array still averages correctly but has
  no single curve to undo (`CalibrationIsAggregate`). Letting non-placed positions vote made
  a set of six sharing one file declare itself aggregate because of a non-contributing
  seventh, and Virtual DSP then refused a swappable correction.
- At least two placed positions are required. One is a point measurement wearing a title
  that claims a listening volume, with NaN spread; consumers fall back to the IR instead.
- The title counts placed positions ("7 microphones"): the count is read by the button, the
  composition warning and the set comparison.
- `measuredAtUtc` is null only for a measurement taken now; a loaded one keeps its own date,
  because the composition warning offers the date as evidence two channels came from one
  sitting. (`MeasurementResult.MeasuredAtUtc` is likewise stamped per run and survives
  re-saves; a save stamp would make Monday's and Friday's measurements both read Saturday
  after being re-saved.)
- Each array measurement gets its own capture session id: what a session id guards (levels
  held together by one analyzer run) is guaranteed by the loopback. Analyzer fields in the
  recipe stay default — an array has no analyzer, the set rules compare only the protective
  high-pass, and invented values would make it look like a capture it is not. The curve is
  relative (not SPL) and unsmoothed, so consumers can re-smooth. The per-position spread is
  returned beside the document, since a moving microphone has none.

## SPL calibration

`SplCalibration` anchors digital level to absolute SPL using an acoustic calibrator (a
94/104/114 dB tone at 1 kHz, IEC 60942). It stores the raw ingredients, not a pre-baked dB
shift: the displayed response is a loopback-referenced transfer (microphone / loopback), so
placing it on an SPL axis needs this microphone-side anchor and each measurement's own
loopback level, combined where the curve is drawn. `OffsetDb` is derived
(`SPL = dBFS + OffsetDb`).

The anchor is valid only while the gain chain is unchanged. Its digital half (backend,
format, channel routing) is recorded so a mismatched input is detected; the analog preamp
gain is invisible to software, so the standing rule is to leave it alone between
calibration and measurement.

Each result carries a frozen copy of the SPL calibration, protective high-pass and microphone
calibration taken at run start. The plot and the saved file read those, so recalibrating or
changing settings later never rewrites what an existing IR means. A run keeps the anchor
only when it was captured on the input the run measured (`MeasurementInputIdentity`); a
calibration left over from a different device would otherwise be trusted on reload. A loaded
file keeps its own anchor, which passed that check when the file was first saved.

`SplCalibrationListener` measures the tone on the microphone alone through a silent
streaming session, accumulating a flat-top power spectrum whose peak gives the level within
a fraction of a dB wherever the tone falls between bins. Samples at 0.999 (about -0.009 dBFS)
count as clipped. The first two frames are scanned for clipping but kept out of the level
and stability estimate (the device may still be priming). A seated calibrator holds
hundredths of a dB, so a level standard deviation over 1.5 dB means a drifting coupler. Any
dropped frame or backend packet discontinuity invalidates the capture, since the missing
frames could be the failing ones.

## Record Settings code map

| Concern | Where |
| --- | --- |
| Every field as it shows (route, sweep, protective high-pass, calibrations, arrays), the rules that move one field when another does, load and live write | `RecordSettingsSession` (+ `.Devices`), fields `RecordChoice` / `RecordNumber` |
| The machine: devices, endpoints and their changes, rates, ASIO drivers, format checks | `IRecordDevices` → `SystemRecordDevices` |
| Device captions, which controls apply, the loopback line, the ASIO rate status | `RecordDeviceStatus` |
| Apply: the route checked against the hardware now, the engine configuration, what the settings keep | `RecordSettingsApply` |
| 0° file, further calibrations, the microphone's pick, the SPL anchor and whether it is stale, the SPL capture request | `RecordCalibrations` |
| The inputs an array can use and which of them a run records | `RecordArrayInputs` |
| The achieved-band line, its shortfall warning, the sweep file | `SweepBandPreview` |
| The protective high-pass as configured | `RecordHighPass` |
| Binding: controls, dialogs, message boxes | `MeasurementOptions` (+ `.Devices`, `.Calibration`, `.Sweep`) |

- **Fields behave as their controls.** A `RecordChoice` raises `Changed` only when its selection moves; clearing
  it is silent, as `ThemedComboBox.Items.Clear` is. A `RecordNumber` rounds and clamps as its field does
  (`NumericFieldRange`). The cascades between fields (a backend repopulates the devices, a device re-probes the
  rates, a mono MME device forces the loopback to None and a stereo one restores the remembered channel) are the
  session's handlers, fired when the controls' used to be, so the host sees the same live-apply events.
- **Presenting.** The form writes one field into the session per edit and then presents the whole session,
  ignoring its controls' events meanwhile. Everything shown is derived on each present, so the SPL anchor's stale
  warning follows every change of the input. The ASIO rate status describes the last driver probe and is written
  only while ASIO is selected; hidden, it is greyed.
- **Two ways out.** Everything but the audio-backend group applies as edited: `SweepSettingsChanged`, then the host
  calls `ApplySweepSettings`. The backend group waits for Apply (`RecordSettingsApply`), which throws the refusal
  the host shows. A calibration change raises `CalibrationChanged` and the host persists it at once.
- **Tests.** Rules are tested on a session over `FakeRecordDevices`; `MeasurementOptionsBoundaryTests` keeps
  statics and nested types off the form, and `MeasurementOptionsWiringTests` drives its controls.

## Live analysis modes

`LiveSpectrumOptions` lives beside `NoiseMeasurement` because the analyzer reads it by
reference on the analysis path.

- **Transfer function**: dual-channel H1 of microphone over loopback, with coherence; falls
  back to RTA without a loopback.
- **RTA**: reference-free microphone spectrum, the only mode where the Silent (ambient)
  signal makes sense (enforced at settings load and by
  `LiveSpectrumOptions.NormalizeSignalType`); dB SPL applies only to reference-free modes,
  since a transfer function under noise has no scalar SPL.
- **MMM**: the same reference-free spectrum under the one recipe a moving-microphone
  measurement is valid under. It is a mode, not an RTA preset, because its settings are not
  preferences: read through the wrong window, averaging or slope compensation, a spatial
  average is wrong by a smooth, plausible trend nothing downstream detects (a missing slope
  compensation reads 13 dB hot at 20 Hz). It pins periodic pink excitation (exactly
  `1/sqrt(f)`, whereas the Kellett bank's poles sit in normalized frequency and move with
  the rate), Infinite averaging (an exponential window would weight the end of the
  microphone path over its beginning), slope compensation on and smoothing off. The colour
  and averaging pins live on the options object (`EffectiveNoiseColor`,
  `EffectiveAveragingSpeed`) because both the analyzer and the plot factory read them and
  do not always hold the same instance; slope compensation and smoothing are pinned in
  `PlotModelFactory`.
- Code asks the traits `IsReferenceFree` and `IsSpatialAverageCapture`, never `== Rta` or
  `== Mmm`, so a future array mode joins in one place instead of a dozen call sites where a
  miss produces a smooth wrong curve.
- `LiveSequenceLengths` is one list shared by the options panel and settings schema; separate
  copies let a length be offered and then silently floored on the next save. 32768 and 65536
  exist for MMM: band-power resolution is set by frame duration (2/T Hz rectangular, 4/T
  Hann), so the same resolution costs twice the samples at twice the rate.

**Noise colours and tilt compensation.** `NoiseSignal` uses a fixed seed. Periodic pink is
one FFT-block period with exact `1/sqrt(f)` magnitude from 10 Hz to 28.3 kHz and phases chosen
for a low crest factor, tiled a whole number of times (a partial period would jump phase at
the loop seam); being period-synchronous with the FFT it converges without spectral variance
(see [live-spectrum.md](live-spectrum.md#periodic-pink-excitation)). Random pink uses Paul Kellett's filter bank
(`Dsp.KellettPinkFilter`), brown a leaky integrator whose leak is derived from a fixed
`BrownCornerHz` (76 Hz) — a fixed 0.99 coefficient put the corner at 76 Hz at 48 kHz but
305 Hz at 192 kHz. `NoiseColorTilt` models the shapes actually synthesised: both random
colours flatten below their filter corners, and compensating a nominal slope there would
print an artificial bass roll-off onto a correct measurement. Silent has no model.

## Live noise analyzer

`NoiseMeasurement` streams capture blocks to a background FFT accumulator.

- **Periodic pink** is exactly one FFT period and both playback paths loop seamlessly, so
  only one period is generated; materializing 60 s allocated about 35 MB of LOH arrays next
  to the first audio callbacks. Aperiodic colours still need the full length. Periodic pink
  forces a rectangular window (a whole period per block is leakage-free; ENBW 1 bin, main
  lobe 2 bins) and disables overlap (overlapping frames of a deterministic period add no
  averaging). Recipes record the effective window, not the stored option.
- **RTA opens the microphone alone** even with a loopback configured: WASAPI refuses an
  endpoint with fewer channels than the mic+loopback routing, and ASIO would widen the
  captured window. The configured offsets are kept for the Transfer mode.
- **Dropped blocks** bump a generation counter that resets the reframer, because a frame
  built across the gap reads the step as a broadband burst and poisons H1, coherence and the
  EMA for seconds.
- **Coherence** is hidden (null) until four frames have accumulated: single-frame gamma^2 is
  1 in every energized bin.
- **Accumulators are seeded** with the first frame: H1 and coherence divide the scale out,
  but the RTA reads `sqrt(target power)` directly and would start `sqrt(alpha)` low.
- **Changing averaging speed** restarts the statistics, like Reset.
- **Warm-up**: one synthetic frame runs through the full analysis path before the driver
  starts, so MathNet initialization, JIT and large-array commits do not land on the first
  callbacks as dropouts.
- **Snapshots** clone the accumulators under the lock and do the math outside it, so a slow
  UI cannot stall accumulation. `LiveSpectrumSnapshot.FrameCount` is read under the same
  lock, so a stored capture never claims more integration than its bins hold.
- `CaptureSessionId` is regenerated on every `Init`. Spatially averaged captures are levelled
  against the IRs by one common offset, valid only within one analyzer configuration (an SPL
  anchor re-establishes the absolute reference per session). A hardware gain change is
  invisible to the id; the per-channel offset-spread check catches it.

## Live capture snapshots

A reference-free capture carries the user's protective high-pass (there is no loopback to
divide it out), so its curve and saved recipe must compensate for the filter that was in
force during the walk. `NoiseMeasurement.CaptureProtectiveHighPass` is frozen at run start.
Read live, a setting change could re-tilt half a pass; the first version read the sweep
measurement's copy, which was the filter of the last configured sweep and was not refreshed
by an options edit, so enabling the filter and capturing immediately left a tweeter low by
the whole slope.

The microphone calibration is frozen the same way (`CapturedMicrophoneCalibration`, curve,
name and id resolved together): bins are rendered again when drawn and when saved, so a live
read would let a change between the walk and Save recompute the walk and stamp the file with
the wrong microphone. The curve is held rather than the id so editing the file behind the id
cannot reach a finished capture. The name is what people read; ids (e.g.
`cal-8db411cb...` for a user-named "90° capsule 2") are only for matching.

## Impulse response file format

`ImpulseResponseFile` is versioned JSON.

- **Version 8** stores the five bulk sample arrays as base64 of little-endian float32
  (`Float32SampleArrayJsonConverter`): about 5.3 bytes per sample instead of ~28 as indented
  numbers. Float32 keeps about 7 significant digits (~-140 dB relative), far below any noise
  floor, and only in the file — properties stay `double[]`. Byte order is fixed by contract,
  not host. Pre-v8 number arrays are still read at full double precision. A value beyond
  `float.MaxValue` is refused at save, since it would round to infinity and only fail on the
  next load.
- That was a representational change, hence a version bump. Builds up to v7 validate the
  version after deserializing and fail on v8 with a parse error; from v8 on, `LoadAsync`
  reads the version first (`JsonFormatMarker.ReadWithVersion`) and refuses a later version
  up front, so future format changes read as "unsupported version".
- Additive optional fields (protective high-pass, microphone calibration, array microphones,
  measured band, measured-at time, timing reference) are deliberately not bumps: a bump would
  make every new file unreadable to older builds over metadata they would ignore.
- **Protective high-pass**: null means "not recorded" and differs from Off. "No filter" can be
  checked against a reference-free capture that carries one; "not recorded" cannot, and
  conflating them would pass a tweeter whose two measurements sit a filter slope apart. Off is
  written explicitly; a re-saved old file keeps null.
- **Microphone calibration** is stored as a curve: IRs are raw, so a recipient without the
  author's calibration file would otherwise see a different curve with nothing saying so. As
  in Virtual DSP sessions, the curve is the truth and the name a hint (ids differ per machine).
- **SPL calibration** is the result's anchor, kept at run start only when it matched the run's input.
- **Timing reference** defaults to `SynchronizedLoopback`, right for every file written before
  imports existed.
- **Sweep band**: `Octaves` is legacy (sweep from Nyquist / 2^octaves to Nyquist) and kept only
  so pre-band files deserialize. The achieved band is stored because the harmonic geometry is
  keyed to it; band-generator files written before it was stored re-derive it via
  `ExponentialSineSweep.ComputeSpec` (deterministic). `SweepDurationSeconds` is the achieved
  duration, which can exceed the generation cap a rebuilt sweep would apply.
- **Array microphones** allow NaN (`AllowNamedFloatingPointLiterals`): NaN marks bands the sweep
  never reached and must not become a low level an equalizer would fill; only infinity is
  refused. The frequency grid endpoints are stored even though the grid is constant today,
  because a stored curve outlives the code and a silently changed grid would shift every level
  in frequency. On an endpoint mismatch (compared with a 1e-6 relative tolerance, since two
  constructions of one log grid differ in the last ULPs) the curves are dropped, not the file:
  the IR is still readable and the tools say when they fall back to it.
- **Coherence** must hold exactly N/2 + 1 bins, because the FFT length is reconstructed from it.
- **Saving** writes a sibling temp file and moves it into place atomically; creating the target
  directly truncates it first, so a crash or full disk would destroy the previous save.
