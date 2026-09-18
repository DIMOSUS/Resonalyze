# Virtual DSP: chain analysis, summation and metrics

The Virtual DSP tool simulates every channel of an installation playing through its DSP
settings and reads what the microphone would capture. All transfer impulse responses share
the loopback time reference (sample 0), so a sample-wise sum of the processed responses is
exactly the captured sum — relative delay, polarity and phase included.

Where the code lives:

- `dsp/VirtualCrossoverAnalysis.cs` — `VirtualCrossoverAnalysis`: chain application, direct-sound
  gates, alignment searches, band-limited correlation, arrival-coherence ladder, sum-loss
  arithmetic, band levels, polarity. UI-free.
- `source/Tools/VirtualCrossover/VirtualCrossoverProcessingCoordinator.cs` — revisions, the
  processed-response cache, background scheduling, the source snapshot and its render crop.
- `source/Tools/VirtualCrossover/ProcessedChannel.cs` — `ProcessedChannel` snapshots and
  `ProcessedChannels` (band order, junctions, window anchors).
- `source/Tools/VirtualCrossover/VirtualCrossoverMetrics.cs` — builds curves, sum-loss entries,
  junction phase entries, stereo and group Δ read-outs and the opposite side's sum.
- `source/Tools/VirtualCrossover/VirtualCrossoverMetric.cs` — pure formatting of those read-outs.
- `source/Tools/VirtualCrossover/SumLossWindow.cs` — the Full / Direct / Off selector.
- `source/Tools/VirtualCrossover/VirtualCrossoverAcousticPlot.cs`,
  `VirtualCrossoverDspChainPlot.cs` — OxyPlot presenters for the main plot and the lower plot
  (chain response, junction correlation, junction coherence).

## Chain application and the valid sample range

`ApplyChain` multiplies the IR spectrum by the chain response bin by bin (conjugate-mirrored,
so a real input stays real). The record's sample rate and the processor's rate may differ (a
48 kHz measurement can carry a 96 kHz chain); only the processor rate shapes the filters.

Filter tail padding. Zeros are appended before the FFT so the delay shift and filter ringing
stay linear instead of wrapping. The floor is 8192 samples (~170 ms at 48 kHz); the actual pad
follows the chain's slowest pole, because a low high-Q PEQ rings far longer (a 20 Hz / Q 10
boost decays only ~9 dB over the floor, and the rest would wrap into the early IR). A cap
bounds FFT growth for pathological settings.

`ValidSampleRange` records where MEASURED content sits in the processed record. The chain
delay manufactures silence before it and the power-of-two FFT length manufactures a tail after
it (exact zeros for a scale-only chain, the kernel's sub-noise decay otherwise). Envelope noise
floors are quantile-based and manufactured silence collapses them: a pure-noise 65k record
zero-padded to 131k grades ~60 dB SNR instead of ~6, and a 25 ms delay prefix alone lifts a
short noise record past the 20 dB onset-lock floor. That would wave noise-only fronts through
every SNR gate (onset lock, stereo bridge, cross-side ladder), so envelope analyses read inside
the range while reporting positions in full-record coordinates.

`ChainValidRange` recomputes the range arithmetically so a caller holding a cached processed
array does not have to re-run the chain:

- The delay is signed. A positive delay shifts content right (silent prefix); a negative one
  shifts it left, so content ends earlier and the vacated tail is manufactured silence.
- A FIR stage shifts content by its leading exact zeros and extends it by N − 1 kernel samples —
  what the convolution writes after the input's end is filter output, i.e. content. The kernel
  is stated at the processor rate, so both figures are scaled by recordRate / processorRate
  (the tail end is rounded up).
- `LeadSamples`: the kernel's span from its first non-zero tap to its largest (≈ 0 for a
  minimum-phase kernel, half the length for a linear-phase one), same scaling. A length, not a
  position: crops and slides keep it (`with`, never a fresh range). Front-anchored windows open
  this much earlier (see Window anchors).
- The measurement's own quiet regions (leading silence, an anechoic tail inside the input) stay
  in the range: they are recorded silence, not padding.
- A delay that pushes the whole input out of the record yields an unknown range.

Fallback heuristic (`AnalysisWindow` / `AnalysisLength`) for callers without the metadata. It
can only trim the tail. A trailing run below `SyntheticTailFloorRatio` (−140 dB: far below any
real noise floor — loopback captures grade −115 dB at best — and far above numerical residue)
is treated as padding only if the content region before it is dense
(`PaddedContentMinimumDensity` = 50 % above-floor samples). A measured record carries noise in
every sample; a synthetic or anechoic record is mostly digital silence and is analysed whole.

## Render crop

`VirtualCrossoverSourceSnapshot` keeps only the first `RenderCropLength` = 65 536 samples. A
sweep record is 524 288 samples (~12 s) whose last three quarters sit at the noise floor
(−67 dBFS against the peak) while the arrival lands in the first thousand. Being exactly 2^19,
any tail padding tips the FFT to 2^20 — 82 ms and 16 MB per side versus 10 ms and 2 MB for the
head. Nothing downstream reads past it: the magnitude window is clamped to 32 768 samples and
the phase gate is a few hundred samples. Verified on three cabin measurements: magnitude and
phase identical to 0.00000 dB / 0.00000°. A record whose arrival is so late that 32 768
post-peak samples would not fit keeps its full length.

The crop truncates from sample 0 rather than centring on the arrival: every channel keeps its
peak index and relative timing, and the user's absolute gate offset still points where it was
set. The Auto delay search, which does need a tighter crop, shares one offset across channels
(`CropSharedDirectSoundWindow`, offset just before the earliest peak). Running the alignment
search, gated loss and band arrival on that crop gave bit-identical Auto delay cascades and
junction losses on real measurements.

## Processing coordinator

- Cancellation driven by revisions (every delay edit) is silent: `ProcessChannel` and auxiliary
  operations return null instead of throwing, because exceptions crossing the TPL boundary stop
  a Just-My-Code debugger on every edit. `Parallel.For` uses `ParallelLoopState.Stop`.
  A caller's own (external) token still raises, so it is re-checked after the work — the
  delegate's null cannot tell the two cancellations apart. Throwing delegates are still
  tolerated as a backstop. A render is published only when every slot is filled.
- Tracy zones sit on worker threads because a zone must begin and end on one thread.
- `DspChannelChainCacheKey` exists because `EqualizationCurve` has reference equality. Every
  chain stage must be listed by hand; a forgotten stage compiles and silently serves stale
  renders forever. The processor rate is part of the key (same chain at another rate is another
  filter). FIR kernels compare by reference: the same loaded instance is the same filter.
- Snapshotting detaches only the mutable PEQ and copies the rest with `with`. Member-by-member
  copying once silently dropped the all-pass stage from the processed path.
- The response's sample rate (the measurement's record rate) is captured in `ProcessedChannel` /
  `VirtualCrossoverProcessedChannel`.
  A session import rebinds channels on the UI thread while renders are in flight; reading the
  rate back off the live channel read zero and crashed with `ArgumentOutOfRangeException`.
  `MeasuredBand` and the microphone calibration are snapshotted per side for the same reason:
  a list can carry the opposite side's responses.

## Window anchors

A peak is the wrong anchor for a window. A crossover's group delay puts a filtered channel's
peak milliseconds behind its front: on the archived Passat right side a 250–2000 Hz midrange
peaks 7.11 ms in (its 250 Hz high-pass) while its 1–4 kHz energy is there at 4.28 ms, and the
subwoofer peaks 5.55 ms behind its 33–130 Hz arrival. A peak-anchored window opens after the
front and its fade-in attenuates one member's rise more than the other's.

- `ProcessedChannels.StartAnchorIndex` — a channel's estimated response start (memoized by
  `TransferIrStartCache`), falling back to the peak. `SharedStartAnchorIndex` takes the earliest
  across a set, each read within its valid range. The magnitude window, the phase view's Auto
  placement and the junction gate all use the start rule; they differ only in span.
- `VirtualCrossoverAnalysis.FindGateAnchor` — band-limited first arrival in the junction band.
  Guards: a band without measurable arrival (invalid or below
  `AutoAlignmentEngine.MinimumArrivalSnrDb`) falls back to the peak, and the result is clamped
  to never be later than the peak (a latched read on a room mode is a late one), so it can only
  move a window earlier than the old peak anchor. The front is floored to a sample: half a
  sample early costs nothing (covered by the fade), half late puts the plateau inside the front.
  The result then moves earlier by the range's `LeadSamples` (not before the range start), and
  the windows opened there lengthen by the same amount so their reach past the front is
  unchanged: the junction alignment gate and the direct-sound cuts (plateau 2T + lead).

  Why: a linear-phase FIR's envelope rises symmetrically into its peak, so its band-limited
  front lands on the peak (v6 FIR session, 4095 taps at 96 kHz: A's front equal to its peak,
  B's 0.55 ms ahead) while ~21 ms of pre-ring precedes it. A window opened one fade (13.5 ms)
  before that cut the pre-ring of each branch — the half that makes a complementary pair sum —
  and the junction score optimum moved 11 ms (A 2 / B 0 read −0.17 dB where Full and Direct read
  −0.97 / −0.99; the true B 9 ms read −1.40 against −0.05 / −0.04). "PHAT direct" put its
  extremum 9.4 ms off the same way. With the lead the score equals that of one shared window
  opened before both pre-rings, and IIR chains (lead 0) read bit-for-bit as before. The Direct
  and Full loss windows were never affected: FDW-8 and the steady-state window reach the
  pre-ring on their own.

## Magnitude curves and the shared window

`VirtualCrossoverMetrics.BuildCurves` gates every channel and the sum with one anchor (the
earliest start; a gate offset pinned in the dialog replaces it and is shared just the same).
Per-channel anchors capture different room content, the drawn Sum stops being
the vector sum of the drawn curves and the loss can exceed its 0 dB ceiling. The summed
envelope peak can sit between arrivals or vanish under cancellation, so it is not used. The
magnitude always reads the fixed gate; FDW would need per-channel windows, so FDW shapes only
the phase view and the direct loss.

The Sum curve is built by the panel from the channels (each contributing only where it
measured, each through its own microphone calibration, added as phasors) rather than as the
gated total of the summed IR: one shared window makes the transform linear, so that total would
carry every channel's window leakage into ranges that channel never measured. The metrics-only
fallback sums in the time domain and takes no calibration; the panel does not use it.

Microphone calibration is a per-measurement property under "Own (as measured)", so it is
passed in as a function of the channel.

A metric needs two summing channels, but per-channel magnitudes are drawn for one channel too
(withholding them broke the hybrid view and the EQ handoff when all but one channel were
muted). A grouped view can draw a centre beside the front without summing it; the loss divides
by the incoherent sum of exactly the summed channels.

## Measured bands and junctions

- `UnionOfMeasuredBands` is the hull of the channels' bands (a sum plays wherever any channel
  does). It cannot express a hole between disjoint sweeps (woofer to 500 Hz, tweeter from
  1 kHz), so sum curves are masked per frequency by `MeasuredBySomeChannel`: in the hole the
  summed IR is zero from every contributor and the curve would be the analysis window alone.
  Both halves of `GatedMagnitude` are masked, since the unsmoothed half is the loss operand.
- `GetAdjacentPairs`: band-order adjacency is necessary but not sufficient. A subwoofer stopping
  at 110 Hz beside a rear fill starting at 290 Hz hands nothing across. A pair is a junction
  only when both channels play inside the octave-each-way window the junction is measured over;
  this reuses the measurement window instead of inventing a gap tolerance.
- `IsContinuousChain`: the reference car's Rear + Sub view has two subwoofers that genuinely
  cross (below 50 Hz into 50–110 Hz) and a rear fill from 290 Hz. Per-junction figures are real;
  a total over the set is not, so no total is reported unless the set is one chain.
- `JunctionsInView` / `JunctionChains`: band order over a whole installation is not a chain.
  Rear fills and centres are high-passed with no upper corner, so their band centre lands
  between midrange and tweeter and invents junctions while hiding the real mid/tweeter one.
  Chains are Front+Sub, Rear+Sub and Center (the centre sums with nothing); subwoofers belong
  to both stages. Views spanning several groups list no junctions.
- `PhaseNeighbourhood` takes chains from the channel's own zone, not the group selector; a
  neighbour is included only when drawn.

## Sum loss curve and level gate

`SumLossCurve` is the single definition read by the drawn "Sum loss" curve,
`AverageSumLossDb` and `MinimumSumLossDb`: 20·log10(|ΣH| / Σ|H|) per point, ≤ 0 dB.

Operands must be unsmoothed; display smoothing is applied to the finished ratio. Smoothing the
channels first does not commute with the division: a fractional-octave window on a steep skirt
lifts that channel toward its passband (several dB on 36 dB/oct, more under the psychoacoustic
peak-weighted mean) while the flat sum barely moves, drawing a dip at every corner — a 70 Hz
junction losing a true 0.2 dB read 1.6 dB that way. Hence `GatedMagnitude` carries both widths.

A channel whose curve is NaN at a point (a protective high-pass took the signal past recovery)
is skipped rather than poisoning the sum.

Level gate: points whose combined magnitude is more than `SumLossLevelGateDb` (25 dB) below the
loudest combined level within ±`SumLossLevelGateReferenceOctaves` (1 octave) become NaN. There
the "loss" is the phase arithmetic of two noise floors, producing deep fake dips outside any
driver's band. A local reference keeps a tilted in-room response (loud bass, quiet treble)
measured across its range. `LocalMagnitudePeaks` computes it in O(n) with a monotonic deque.

`MinimumSumLossDb` complements the average: a sharp audible notch barely moves the average.

`PredictedAverageSumLossDb` quotes before/after figures for Auto delay proposals without UI
state: 1/24-octave grid, each point a power mean over ±1/6 octave so a null reads as the audible
notch rather than a −∞ bin, with optional per-channel gain previews.

Read-outs: per-junction first (so an improvement at one crossover is not averaged away), each
junction reading the full sum inside its pair band. Two decimals: genuine lobe alternatives
differ by hundredths (the v5 cabin's 150 Hz junction separates its candidates by 0.08 dB).

## Sum loss window: Full vs Direct

`SumLossWindow` selects what the loss is measured through; the two families of numbers are not
comparable and the column header says which one is shown.

- **Full** — the steady-state magnitude window (~680 ms, clamped to 32 768 samples at high
  rates) at one shared anchor. What the ear hears in the cabin; the Auto delay battery and the
  tuning sheet use it. **Off** hides the curve; the column keeps the Full read.
- **Direct** (default) — `BuildDirectLossCurve`: each channel through the junction phase block's
  8-cycle frequency-dependent window at its own front (`JunctionPhaseSpectra.Build`), spectra
  rotated into one absolute frame and added. Superposition holds, so the loss stays ≤ 0 dB.
  Same arithmetic as `BuildCurves` otherwise. Channels at different rates yield no curve.

On a seven-position grid in the reference car the direct window cut the seat-to-seat
group-delay scatter from 2.3 ms to 0.4–0.8 ms, but its loss figures scatter more between seats
and run deeper: early reflections within 1–3 ms of the direct sound sit inside any window that
still resolves 1/6 octave, and the late tail the full window keeps fills the notch. The AI
package carries both reads.

## Alignment search objective

`FindBestAlignment` / `FindAlignmentCandidates` choose delay and polarity of a variable channel
against fixed channels by minimising the log-frequency-weighted average summation loss — the
metric the tool reports. A raw cross-correlation weights bins by energy product, which steep
crossovers concentrate at the corner, so the true peak and the (flip + half-period) impostor
differ by a few percent and room reflections promote the wrong one; the dB average punishes the
impostor's off-corner cancellations. The invert flag is relative to the IR passed in.

`FindBestDelayMs` (no polarity freedom) keeps a correlation objective: Re Σ conj(F)·V·e^(−jωτ)
over the window bins, an exact fractional-delay cross-correlation. Without polarity freedom the
half-period impostor cannot win.

Search mechanics (`SearchBestDelay`, `SearchAlignmentCandidatesByLoss`):

- Coarse grid (step ≤ min(0.02 ms, a quarter of 250/fmax)) then two refinement passes around
  each local optimum; the step stays well below the shortest period so refinement cannot jump
  lobes. The coarse grid is evaluated transposed (bins outer, delays inner) with an incremental
  phasor instead of `Complex.Exp` per (bin, delay).
- Each polarity seeds and refines its own optima. On a max-of-both envelope one polarity edging
  the other across a basin would hide the loser, leaving `AlignmentSelection`'s normal-polarity
  preference nothing to prefer. A forced polarity (inherited from the stereo counterpart) seeds
  only that polarity. Refinement is clamped to the window so the score belongs to an in-window
  delay.
- `MinBinAmplitudeRatio` floors each bin's loss at −60 dB so one perfectly cancelled bin cannot
  dominate the unsmoothed average.
- Arrival prior: a quadratic penalty of `PriorPenaltyDbAtSigma` = 0.25 dB at one sigma around an
  arrival-based, polarity-blind estimate. Gentle because genuine candidates differ by tenths of
  a dB; it only breaks near-ties and deters far lobes.
- `DipExcessPenaltyWeight` = 0.5: penalty = weight × (DipDb − LossDb). The average cannot tell a
  smooth −0.7 dB loss from −0.7 dB hiding a −5 dB notch; penalising the excess over the average
  (not the dip) avoids punishing a uniformly lossy candidate twice. Same weight as the dip
  penalty in `CrossoverAutoSetup`. Folded in after refinement, since the dip varies slowly within
  a lobe, but before ranking so a notched optimum cannot pass as the equal of a smooth one.
- Candidates within `CandidateGapDb` = 1.5 dB of the winner are returned, capped at
  `MaxAlignmentCandidates` (room for three lobes even when each polarity contributes one).
  The uncapped, prior-free-enriched list is also returned, because rival margins computed over
  the capped list could read "unrivaled" only because the rival was cut.
- `DetailedLoss` dip = minimum of a 1/6-octave moving average, so a single-bin modal notch
  cannot pose as the junction dip while a real cancellation trough reads at full depth.

## Delay evidence gate

`HoldsDelayEvidence`: a delay is observable only where both sides genuinely radiate over a
usable width. With one side silent or buried under its partner the loss |F+V|/(|F|+|V|) is flat
at 0 dB for every delay, and the search would manufacture a confident candidate at the prior.
Evidence bins need:

- the weaker side within `OverlapReliabilityGateDb` (30 dB) of the stronger side in that bin — a
  per-bin balance, because a global reference lets a subwoofer's cabin-gain peak veto a healthy
  hand-over 30 dB below it;
- the bin within `EvidenceNoiseFloorGateDb` (60 dB, the loss floor's scale) of the in-band peak;
- a run of at least `MinEvidenceBins` = 3 adjacent bins spanning `MinEvidenceOctaves` = 1/6
  octave. Healthy junctions measure 0.4+ octaves of overlap; a lone bin or single shared tone
  (delay ambiguous modulo its period) must not pass. At a low junction one coarse FFT cell can
  span a sixth of an octave, hence the bin count. Bins are stride-decimated and zero-dropped, so
  adjacency is the smallest FFT-index step present (`AlignmentBin.FftBin`); gaps are not
  credited.

The gate judges raw magnitudes (`RawVariableMagnitude`), before the search-side level match:
after it, a −60 dB filter tail amplified by the capped match lands exactly on the reliability
gate and votes as evidence. It grades spectral structure, not noise (two comparable noise floors
pass); the engine's arrival-SNR refusal owns that upstream. `MeasureSumLoss` applies the same
gate when a caller compares alignments (`requireDelayEvidence`).

## Search-side level match

`BuildAlignmentBins` scores the junction as if both sides were gain-matched in the band. True
timing does not depend on level, but the loss surface's power to resolve it does: the anti-phase
null only reaches full depth at equal levels, and a 10 dB imbalance flattens it until near-tie
gates flip on noise (field case: a sub at −10 dB re the midbass picked a lobe a full period late
that 0 dB resolved cleanly). One scalar per channel set preserves spectral shapes; reported
losses elsewhere keep the real gains. The correction is capped at `LevelMatchCapDb` = 30 dB
(matching `OverlapReliabilityGateDb`): deeper than that is roll-off tail or residue. Past the cap
`MeasureInBandImbalanceDb` lets the Auto delay log tell the user to level gains first.

## Junction direct-sound gate

The alignment spectra are taken through a cosine-faded Tukey window per response. Reading the
full IR folds hundreds of ms of reverberation into every bin — comb structure the alignment
cannot change — so the search would optimise reflections while the panel shows the gated sound.

Anchor history and rules:

- Anchoring a pair on its earlier peak ranked alignments wrongly (field cabin, LP/HP 55 Hz
  36 dB/oct: tuned alignment read −0.44 dB and one 2 ms later was preferred; a window at the
  whole system's earliest peak read it at −0.02 dB, agreeing with the panel and with whitened
  correlation, r 0.99), because the fade-in cut into the low channel's rise.
- Reaching outside the pair for an earlier peak fixed that by accident but made the window move
  whenever an unrelated channel was muted or re-timed (Passat right side: muting mid and tweeter
  moved the 65 Hz junction window 8.96 ms and its optimum 14 ms). The junction's own members set
  the anchor, and it is a front.
- Default (`gateAnchorSample` null, what every engine site passes): each response windowed at
  its own band-limited front (`FindGateAnchor`, reading each response's valid range — a delay
  prefix inflates arrival SNR and can certify the wrong event), cuts restored to absolute time
  by the linear phase of their window start. The window belongs to the channel and travels with
  it; mid-cascade a settled neighbour can sit tens of ms behind a not-yet-delayed channel, which
  a shared window only survived by being 85 ms long. A non-null anchor forces one shared window
  (no restoration needed) for displays and placement comparisons.
- Earliest content must land on the plateau, not in the fade-in (unequal attenuation biases the
  loss); a front closer to the start than a full fade shrinks the fade.

Remaining gap and the declined fix. On the 55 Hz reproduction in `JunctionCorrelationCurveTests`
all placements pick the same lobe (−3.5 ms, inverted) but read different flatness: 0.00 dB
through a window at the drivers' shared source, −0.21 dB through the pair's filtered fronts,
−0.23 dB through their peaks. The crossover's rise starts before any detectable filtered front
(that 36 dB/oct low-pass reads its front 12.8 ms after the driver's, the woofer partner 8.6 ms).
Anchoring on the chain-free response (`PredictedFrontArrivalMs`) closes the gap exactly and was
measured and declined — do not re-propose it on the reasoning above. On the session battery
(`tests/Resonalyze.App.Tests`) it moves only the lowest junction of a cabin, and there away from
the owner's tuning on the reference car: v5 sub/bass −0.20 dB against the saved tuning and
v5_exp −0.13, versus +0.03 and +0.10 for the filtered front; proposed delay 0.74 ms off the
manual one versus 0.11 ms. Only v3 prefers it (+0.38 dB), landing 1.56 ms from that session's
tuning. A flatness trusted to a tenth of a dB needs the chain-free front; the search must not
follow it.

## Junction window length and FFT sampling

Sized in time, not samples: a fixed 4096 samples is ~85 ms at 48 kHz but 21 ms at 192 kHz, and
the delay estimate drifts below ~40 ms. The fade is 1/16 of the window
(`AlignmentGateFadeFraction`), reproducing the 4096/256 reference at 48 kHz exactly.

`AlignmentGateBandLowEdgeCycles` = 1/(2^(1/12) − 2^(−1/12)) ≈ 8.66 periods of the band's low
edge. The dip is a 1/6-octave moving average, ≈ 0.1155·f wide at f; a window of 1/(0.1155·f) is
the shortest whose ~1/T kernel fits inside that average at the binding low edge, so the dip reads
the spectrum rather than the window kernel. That is 262 ms for a 33–130 Hz sub band (the old
85 ms was too short) and 11.5 ms for 750–3000 Hz, where 85 ms was mostly cabin reflections.
Bounds: `MinimumAlignmentGateMs` = 4 ms (8.66 periods of 20 kHz is under 0.5 ms, too short for a
real driver's direct sound) and a public ceiling for bands reaching toward 20 Hz; the Auto delay
crop (`AlignmentReprocessor` in the app) budgets for that ceiling at any rate.

`MeasureBandLevelDb` keeps the fixed reference-length gate: a level is an energy average
indifferent to kernel width, and a channel band's low edge runs to 20 Hz where the size rule
would pay its ceiling for nothing.

`AlignmentFftInterpolationFactor` = 4: the span is zero-padded to 4× the gate, rounded to a
power of two. This buys sampling, not resolution. Padded only to the gate, at 96 kHz an 8192-
sample gate puts bins 11.7 Hz apart and a 33–130 Hz band holds nine of them, while the dip width
at 65 Hz is 7.7 Hz — the dip was the worst single bin and notches between bins were missed. 4×
gives 34 bins (a 750–3000 Hz junction already had ~190, which is why only bass showed it). On the
archived cabins this moved LF verdicts more than any anchor rule: v3's 80 Hz junction optimum
went from 9.6 ms away from the whitened-correlation extremum to 0.1 ms, v5_exp's 65 Hz from
12.6 ms to 0.3 ms. Sampling alone did not fix the LF dip (1/6 octave at 65 Hz is narrower than an
85 ms window resolves); that took the band-sized window above. Window length and FFT length stay
separate so the physical window cannot jump across a power-of-two boundary (the same split as
`JunctionPhaseAlignment`). Bins are decimated to at most 4096 for speed.

## Junction loss sweep and SumLossEvaluator

`JunctionLossSweep` draws the prior-free loss surface versus an extra delay on the variable
channel. Each channel is windowed once (by default at its own front, exactly as the search) and
every probe rotates the variable cut by e^(−jωΔ) — the same bins and rotation
`SumLossEvaluator` reads, so with the search's settings (null anchor, level match on) the drawn
surface is the searched one. The anchor must pass through untouched: deriving a shared pair
anchor here once redirected the default onto a different window than the search's, exactly where
it matters (short HF windows over separated arrivals).

Rotation replaced re-gating each probe through a stationary window. A stationary window holds a
moved channel only for the few ms between its front and the window opening; a panel sweep spans
±1.5 crossover periods (±27 ms at 55 Hz), and past that the moved channel slid into the fade, the
sum degenerated toward one channel and both polarities converged to a fake near-0 dB plateau (on
the archived cabins in-band level skew grew from 3 to 19 dB across the sweep while the evidence
gate saw nothing). With rotation the window travels with the channel and level balance is
delay-independent. An older argument for one shared window (per-channel fronts under the then
fixed 85 ms gate read −4.27 dB versus −2.35 dB at 55 Hz) was retired by band-sized windows.

`JunctionLossSweepBothPolarities` builds the bins once (inversion is only a sign). Probes run in
parallel over immutable bins.

`SumLossEvaluator` serves searches that slide one channel against fixed neighbours without
re-running DSP chains; `Evaluate(0)` reproduces `MeasureSumLoss`.

`MeasureJunctionSpectrum` adds the ripple of the summed magnitude (log-weighted dB RMS about its
mean): the loss says how much the pair cancels, the ripple whether the rest is flat — two drivers
wide open at the corner sum into a 6 dB hump and lose nothing.

## Effective overlap

`EffectiveOverlapOctaves` integrates O(f) = 2·min(|F|,|V|)/(|F|+|V|) over log frequency using the
alignment gate and spectra, dropping bins whose weaker channel is more than
`OverlapReliabilityGateDb` below the in-band combined peak (roll-off tail, noise, residue — equal
levels there would still read O = 1). It measures level balance, not coherence/SNR: two channels
together in the noise floor still read as overlap. A confidence read-out, not a search weight
(the search scores amplitude-normalised loss weighted only by 1/f).

## Direct-sound cuts

`CutDirectSound` zeroes everything outside [front − T/2, front + 2T + T/2] (T = one crossover
period) with half-period raised-cosine fades, front from `FindGateAnchor` (a FIR lead moves the
front earlier and lengthens the plateau by the same amount). Two periods reads the
drivers: on the archived mid/tweeter junctions the whitened correlation peaks at the drivers'
timing one period behind the front (r ≈ 0.85 on the reference car); from two-and-some periods
the cabin's early reflections take the extremum over and carry it whole periods away (−2.5 ms,
polarity alternating between lobes). The correlation view's "PHAT direct" curve and the engine's
direct-coherence witness read the same cuts. `DirectSoundWindowBounds` is the one span definition.

`CutDirectSoundPair` / `TrimmedDirectSoundPair` trim both cuts to the union of their spans with
one shared offset, so every lag is unchanged while FFTs shrink to the window. Buffers must reach
the searched lag range: the correlation transform is circular and lag R is wrap-free only if the
padded length covers content + R (NextPowerOfTwo(2·length) does for length ≥ R); a high-band
window of a few dozen samples read over ±6 ms otherwise aliases its own lobes. Windows fully
outside the records give zero buffers, read downstream as nothing measurable.

## Band-limited arrival and broadband onset

`AnalyzeBandLimitedArrival` runs the Time Alignment detector on a band-passed copy.
`MinimumArrivalBandRatio` = one third of an octave: narrower bands leave too few periods, so they
are refused rather than silently widened (callers admitting shared bands test the same figure).
Search depth is the analyser's full 25 dB: gentle spectral fades keep bandpass ringing tame and
kernel-envelope sidelobe rejection separates ringing from real arrivals, while a shallower depth
misses a soft direct rise under a strong modal build-up (an under-seat midbass in its 80–200 Hz
band) and latches milliseconds late. Arrival positions are full-record; envelope indices are
window-local.

`EstimateBroadbandOnset` returns Hilbert-envelope crossings at 10 / 25 / 50 % of the first
credible arrival's peak, walking backward down that peak's rising front. The arrival comes from
the same first-arrival search (25 dB depth, noise gate, Hilbert pre-ringing rejection): thresholds
against the crop's global maximum would let a stronger late reflection usurp all three crossings
with a deceptively tight spread. Differences from `FindBandLimitedArrivalMs`: two drivers at a
crossover occupy opposite halves of the shared band, so envelope-peak times lag their fronts by
different rise times and the arrival difference carries a ~1/bandwidth bias — ~0.3–0.4 ms
(0.45–0.8 periods) on real mid/tweeter junctions. The threshold onset marks the front itself.
At low frequencies the front smears and crossings wander with the threshold. Callers comparing
channels must gate on the spread of the difference across thresholds and on `SnrDb` — a
noise-only record still produces stable-looking crossings.

## Band-limited correlation

`FindBandLimitedCorrelationDelay` / `BandLimitedCorrelationCurve` compute a normalised
cross-correlation band-limited around a junction without an explicit FIR: filtering both IRs by
the same band-pass and correlating equals inverse-transforming the cross-spectrum weighted by the
band's squared magnitude — no taps, latency or side lobes, one FFT pair. `BandWeight` is a raised
cosine over log frequency (no brickwall ringing). Padding to len1+len2 avoids wrap on read lags.
The lag window is centred on the arrival-based estimate, since low junctions differ by several
ms.

GCC-PHAT mode whitens each bin to unit magnitude so the peak depends only on phase difference.
Normaliser: raw mode sqrt(Ea·Eb)/N (Parseval sums carry N); PHAT mode (Σ W²)/N, counting only
contributing bins so a perfect alignment reaches 1.

Extrema (`FindCorrelationExtremum`): sub-sample refinement with windowed-sinc interpolation (a
3-point parabola mislocates a sinc-shaped peak). `CorrelationEdgeGuardSamples` = 2: a truncated
lobe's argmax lands on the boundary or one sample inside; such an extremum is flagged
`EdgePinned`, keeps its integer lag, and neither its position nor magnitude should be trusted.

Rivals and neighbours in `CorrelationAlignmentResult`:

- `PositiveRival` / `NegativeRival` (`FindSameSignRival`): strongest same-sign extremum outside
  the main lobe's contiguous region — the neighbour a period away that peak-vs-trough confidence
  cannot see. A window-boundary lag qualifies when an opposite-sign gap separates it (truncated
  value as a bound); lags still connected to the main lobe never do.
- `PositiveOppositeNeighbor` / `NegativeOppositeNeighbor` (`FindNearestOppositeExtremum`): the
  nearest opposite-polarity lobe, an adjacency fact bounding a cycle-skip. It takes the extremum
  of the first contiguous opposite-sign region past the zero crossing, not the first local
  extremum: shoulders, partial-overlap ripple or a reflection bump would give a spacing far
  shorter than the lobe and refuse a good seed.

## Arrival-coherence ladder

`ArrivalCoherenceLadder` slides a probe across the pair band every
`ArrivalCoherenceStepOctaves` = 1/6 octave, each `ArrivalCoherenceBandOctaves` = 2/3 octave wide
(narrower cannot localise the envelope peak — the packet widens as 1/bandwidth; wider stops being
a reading at a frequency). Fronts are found once per channel in the pair band; each probe
re-cuts with a window spanning two periods of the probe frequency (a fixed cut would hold one
period at the low edge and a dozen at the top). The cuts are trimmed to their span union with one
shared offset (full-length correlation padded FFTs a hundredfold — seconds per junction at
96 kHz). Per probe, band-limited GCC-PHAT is computed over twice the displayed lag range and the
analytic envelope maximum gives `LagMs`, `PeakR` and `CurrentR` (envelope at lag 0). Envelope
readings are clamped to 1 (PHAT normalisation can overshoot by a few percent). Channels enter
processed, so lag 0 is the applied alignment and lags correct the upper channel.

Level gate `ArrivalCoherenceLevelGateDb` = 25 dB (same as `SumLossLevelGateDb`): at the band
edges one driver is deep behind its slope and PHAT, ignoring magnitude, read confident optima off
filtered remnants the sum cannot hear.

No polarity, at any frequency. A band of B octaves around f is f·(2^(B/2) − 2^(−B/2)) wide, its
packet ~1/that, lobe spacing 1/(2f); the ratio 2/(2^(B/2) − 2^(−B/2)) is independent of f and is
4.3 at 2/3 octave, so neighbouring lobes sit inside the packet plateau. Measured envelope drop
between a band's optimum and its neighbours was 0–6 % on every junction; a sub/woofer band once
announced an inversion its neighbours contradicted. The correlation view reads polarity over the
pair's whole band (two octaves there, ratio 1.3) where lobes separate.

The ladder is chiefly a diagnostic. On low junctions band windows are long enough for cabin modes
to rule (the archived v3 mid junction draws its optimum on the mode), so its lag is not a move
recommendation; the coherence plot subtitles junctions below 120 Hz accordingly. The engine takes
no Δt from it; `CountLadderAgreement` counts coherent bands (PeakR ≥ `minPeakR`) within a quarter
period of a candidate — a count, not a mean miss, since one outlier band drags a mean — above a
kilohertz, where the direct correlation separates lobes only by a hair.

## Band level

`MeasureBandLevelDb`: fixed Tukey gate at the response's own peak, 1/f-weighted mean of bin
powers in the band, converted to dB once. Averaging energy tracks loudness and ignores the narrow
interference nulls a cabin riddles the response with (a deep null drags a dB average by most of
its depth but removes almost no energy). The absolute reference is arbitrary, so it is for
differences over the same band, e.g. L−R level (ILD) beside the arrival Δ (ITD).

## Polarity estimate

`EstimatePolarity` reads the sign of the first lobe reaching a quarter of the absolute peak. The
global extremum misreads band-limited drivers that ring up to a larger opposite later lobe. A
quarter balances two failures: a wide-band driver's leading lobe can be well under half the peak,
while anti-aliasing pre-ringing (arbitrary signs) stays around a tenth.

## Step response

`StepResponse` always integrates from the record start, whatever stretch is requested: the step
is a property of the response, and the Virtual DSP step view's window follows the phase gate — a
sum starting at the window would change shape with a gate that is never applied.

## Stereo delta read-out

`ComputeStereoDeltasAsync` reads each pair's band-limited arrival on both fully processed sides
in the shared band; Δ = left − right, positive when the right side leads (the Auto delay scene
offset convention, so after a stereo run every row reads the offset). Level Δ is left − right
(positive: left louder). Mono channels report one arrival in their own band.

- Instrument: follows the engine's cross-side link rule. A pair centred below
  `AutoAlignmentEngine.EnergyOnsetBandCenterHz` (300 Hz) with both sides above
  `EnergyOnsetMinimumSnrDb` (30 dB) is timed by energy onsets (a tenth of band energy), others
  and mono channels by first envelope peaks. On a slow LF envelope the first peak is a coin: on
  one midbass pair the row read 5.6 ms (right peak 4.7 ms into the modal build-up, its upper half
  on the same mode so the latch probe passed it) where onsets read 1.0 ms. The instrument is
  decided once per pair from both full-band reads and applied to both sides and probes, so a Δ
  never subtracts a peak from an onset. `EnergyOnsetWithheld` flags a qualifying band that fell
  back to peaks for SNR.
- Modal latch (`IsModalLatched`): the full-band read must agree with the upper-half probe (from
  the band's geometric mean up) within half a period at the probe's low edge, never under 1 ms.
  A full-band read far behind its probe latched onto the modal build-up; the row renders "~".
  A probe too noisy for an onset abstains. The probe only votes; its number never substitutes.
  Weak full reads get no probe; probes drop their envelope before caching.
- Hybrid level: with the hybrid view on, the level Δ comes from spatial averages through the
  chains and outranks the point measurement and its reliability gate; timing stays on the IRs
  (an average has no phase). Mixed lists mark point-measured rows "(point mic)".
- The channel list passed in must be the full project list: a block's position is its slot in
  the coordinator cache, and a filtered list renumbers slots and evicts responses every frame.
  Narrow with `includePair`.

## Group delta read-out

In views spanning several listening groups a summation loss is meaningless (a rear fill and a
front stage comb whatever the tune), so each group is compared against the front stage in the
band they share: delay (positive = later, which a rear fill wants for precedence and a centre
does not) and level (negative = quieter). The group band is the union of members' crossover bands
excluding subwoofers (which belong to whichever stage is shown). Computed from already processed
responses on the coordinator's auxiliary path; the front's arrival and level are read once per
band. Groups without enough overlap are reported as unmeasurable rows rather than dropped.

The last result is cached in `groupDeltas`: without it every view switch reran the arrival FFTs
(hundreds of ms per toggle on a full installation) while views without group Δ switched instantly.
The key compares processed responses by reference — the coordinator returns the same array while
nothing changed and a new one otherwise, so reference equality is the exact question — plus band,
rate and the hybrid level (a hybrid toggle must not be served a point-measured set). Superseded
frames get nothing and cache nothing. The cost is keeping one generation of responses alive.

## Junction phase read-out formatting

`VirtualCrossoverMetric.FormatPhaseCompact` shows φ at the crossover, the fix (extra delay on the
lower channel) and the current phase score.

- `SignificantFixDegrees` = 10°: a fix under 10° of phase at fc renders "·". Ten degrees costs
  0.03 dB (20·log10(cos(φ/2)) for equal contributions), below audibility and repeatability; the
  threshold is phase so it scales per junction (0.05 ms is nothing at 65 Hz, most of a period at
  4 kHz). Fixes below 0.005 ms take a third decimal so they keep their sign.
- `AmbiguousLobeMargin` = 0.10: below it "!" warns of a possible whole-period hop (field probe
  ~0.19 on a healthy octave-wide junction, ~0.04 on a narrowed one). The margin itself is a
  tooltip figure: a difference of scores has no scale a reader can judge.
- Polarity slot: "i" recommends flipping, "~" flags a near-tie (inversion and half-period delay
  sum alike, common at subwoofer junctions), blank means clearly right.
- Junctions below `JunctionPhaseAlignment.MinimumAlignableScore` dash the fix and blank the
  polarity and "!" slots: the best delay is the least bad of bad alignments. φ is dashed below `MinimumPhaseConsistency`.
- The score column (−1..+1) moves while dragging a delay and answers "is it getting better",
  which the fix alone cannot.
- Signed values use three format sections: since .NET Core 3.0's signed-zero change a negative
  value rounding to zero renders "-+0.00" through the two-section form.

## Plots

- Acoustic plot: the sum loss is a dB gap (≤ 0), drawn on its own right-hand amber axis — on the
  shared dB axis it sat a full scale below the curves. Loss axis: 6 dB steps, top just above 0 dB,
  nominal depth extended in whole steps to the deepest loss down to a floor. Axis and time-axis
  ranges are re-armed only when the nominal range / gate window actually moves, so zoom survives
  the constant redraws triggered by chain edits. The loss axis is added after the value axis
  because `PlotAxisZoom.FindZoomableAxis` takes the first zoomable vertical axis. Phase is locked
  to ±180° (nothing beyond; locking also removes it from the limits dialog). Impulse traces are
  normalised to their own envelope peak, step traces to the largest among them.
- DSP-chain plot: drawn without the bulk delay (it would wrap phase into a sawtooth and swamp
  filter group delay) at the processor rate. The correlation view shows four curves: PHAT
  (whitened full-record comb, the honest read at bass junctions), PHAT direct (driver
  wavefronts, the polarity witness) and the dip-penalised loss score for both polarities. The
  raw amplitude-weighted correlation was removed: it hands the lag to whatever the cabin plays
  loudest. Analytic-envelope guides mark each comb's packet centre. Coherence axes are keyed on
  pair, lag limit bucketed to 0.5 ms and the band (the frequency axis is fitted from it; editing a
  crossover keeps the title and often the lag bucket). The ±T/2 corridor marks where an optimum
  belongs to the next lobe.
