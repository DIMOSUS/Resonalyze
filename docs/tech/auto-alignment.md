# Auto alignment (Virtual DSP "Auto delay")

Auto delay proposes an absolute delay and a polarity for every channel of a multi-way system so that
the drivers' direct fronts line up at the microphone and each crossover junction sums as designed.
It is the hottest and most defended part of the Virtual DSP: almost every constant below exists
because a specific field measurement went wrong without it.

## Code map

| Area | Where |
| --- | --- |
| Engine, all passes and thresholds | `dsp/AutoAlignmentEngine.cs` (`AutoAlignmentEngine`) |
| Tie-breaks between candidates | `dsp/AlignmentSelection.cs` (`AlignmentSelection`) |
| Low-crossover polarity witness | `dsp/LowJunctionPolarity.cs` (`LowJunctionPolarity`) |
| Direct-sound check on the final lobe | `dsp/DirectLobeWitness.cs` (`DirectLobeWitness`) |
| Branch a junction's two sides disagree on | `dsp/StereoJunctionBranch.cs` (`StereoJunctionBranch`) |
| Band-limited arrival detector, energy onset, manual-mode honesty probe | `dsp/TimeAlignmentAnalysis.cs` (`TimeAlignmentAnalysis`) |
| Loss surfaces, candidates, direct-sound cuts, coherence ladder | `VirtualCrossoverAnalysis` (`FindAlignmentCandidates`, `BuildAlignmentBins`, `CutDirectSound`, `CutDirectSoundPair`, `SumLossEvaluator`, `ArrivalCoherencePoint`) |
| Excess-delay readout (not used by the engine) | `dsp/ExcessDelay.cs` |

Inputs are `AlignmentSnapshot`s (one processed impulse response per channel, its `ValidRange`, the
chain-free `BypassedImpulseResponse` and the `ProcessingChain`), `AlignmentJunction`s (adjacent pair,
crossover frequency, pair band about an octave to each side) and an `AlignmentReprocessor` delegate
that re-renders channels under trial overrides. The reprocessor runs off the UI thread and must not
touch shared mutable state. All searches assume one sample rate (`RequireOneSampleRate`): a
neighbour's IR is read at the searched channel's rate, so mixed rates would silently misscale.
`IAlignmentChannel.ProcessorSampleRate` is used only for chain math (`ApplyChain`, the crossover
filter responses of the polarity rule, a FIR kernel's delay); everything measured lives at `SampleRate`.

## Pipeline

**Mono run (`Compute`).**

1. **Stage 1, arrival timeline (`BuildArrivalTimeline`).** A band-limited arrival per junction side,
   graded by the honesty probes, refined by a whitened-correlation (GCC-PHAT) seed where that seed is
   trustworthy. The result is a coarse relative delay per channel.
2. **Stage 2, fine alignment (`AlignChannelAtJunction`).** The top channel of the chain is the
   fixed reference and the walk descends from it (see [Walk order](#walk-order)); each channel is
   searched against its already-settled upper neighbour only, inside their pair band. Each window
   is therefore sized by its own junction: a mid channel's low-junction window is not squeezed to
   the period of its high junction. An error at a low junction moves the whole group below it
   together, which a per-channel search against all fixed channels could not do.
3. **Polarity presentation (`NormalizePolarityPresentation`).** A global flip changes no relation.
   When a proposal inverts more channels than it keeps, the field is flipped, so an inverted
   sub/stack relation reads as one inverted sub rather than three inverted stack channels. Exactly
   half is a tie, broken so that the bottom channel reads normal: a tuner leaves the sub alone and
   flips the stack. A stereo run counts driver positions, not channels — the sides always share a
   driver's polarity, so the reference side stands for both — or a mono sub plus two inverted pairs
   would read as 3 of 7 and keep the sub inverted.

**Stereo run (`ComputeStereo`).** See [Stereo cascade](#stereo-cascade).

Negative delays are never clamped mid-run. A channel that would need one is served by a uniform
shift of every other channel in scope (`ShiftAllExcept`). Uniformity is what preserves the
alignment, so in stereo the scope spans both sides. A channel pinned at the ceiling would silently
break relations and the scene. Only `NormalizeAndVerifyFeasibility` at the end rebases the minimum
to zero (lifting a negative minimum is as legal as trimming a positive one) and refuses the whole
run if the span exceeds the device ceiling.

The ceiling is `DefaultMaxDelayMs` = 50 ms when the device's own `DspProcessorPreset.MaxDelayMs` is
unknown. It is tighter than the 100 ms manual UI range because car processors cap per-channel delay
in the tens of milliseconds (about 17 m of path). Real cabin spans are well under 10 ms, so this is
a transferability gate, not an operating region.

## Walk order

`Compute` anchors the walk on the top channel of the chain (`byBand[^1]`) and descends junction by
junction; there is no upward leg. The anchor used to be the latest-arriving channel, chosen so that
every delay came out non-negative by construction, and the walk went outward from it in both
directions. On a typical car (sub last, tweeter first) that made the sub the anchor and the tweeter
the last link of a chain of three inherited junction errors, while a stereo run then bridged the
sides on that same tweeter, transporting the chain's error to the far side. The weighting is the
owner's: the top junction wants 0.01 ms, the sub junction cannot hear 1 ms, and the far side is
bridged on the top, so the top is where the walk must start.

Non-negativity is served elsewhere and always was: a lower channel that arrives later than the
settled stack above it needs a negative delay, which `ShiftAllExcept` turns into a uniform shift of
everything settled so far, and `NormalizeAndVerifyFeasibility` rebases the union at the end. In a
two-channel proposal that means a late woofer reads as a delayed tweeter, and the inversion of a
pair is attributed to the searched (lower) member; both are relations, not per-channel facts.

Measured on the archive the day it changed (stereo battery, 29 sides): the top junction's metric is
unchanged on 21 sides, average −0.004 dB; the dip moved −0.32 dB, all of it on two v6 and two v4
rows. The gain is structural, not metric: on v6 session 11 the tweeter went from a Low-confidence
last link to the reference, and the unresolved junction is reported where it is, at B/C.

## Arrival detector

`TimeAlignmentAnalysis.Analyze` bandpasses the IR, takes its Hilbert envelope and walks it for the
first credible arrival.

- **Search depth.** `DefaultFirstPeakThresholdBelowMaxDb` is 25 dB. A soft direct rise under a
  strong in-room modal build-up is a real front, and reading the mode instead would time the
  channel whole periods late. The engine derives `SeedVetoMinProminenceDb` from this depth, so the
  two must move together.
- **SNR vs prominence.** `SignalToNoiseDecibels` is the strongest envelope peak against the RMS of
  the quietest quarter of the record (reflections and modal decay do not count as noise). It grades
  the measurement. `FirstArrivalProminenceDecibels` (≤ 0 dB) is the pick relative to the strongest
  peak. A low value means a broad leading edge, which is normal for band-limited LF drivers.
  Folding the two into one "quality" figure would misread great woofer measurements as fair.
- **Sub-sample refinement.** GCC-PHAT on the transfer IR sharpens the envelope anchor independently
  of the driver's magnitude shape. The search radius is `PhatSearchRadiusSeconds` (~0.1 ms): wide
  enough to absorb envelope bias, narrow enough not to slide onto a reflection. The 32-sample cap
  is only a backstop; a cap of 8 gave ±0.04 ms at 192 kHz. Below `PhatTrustCoefficient` (0.2) the
  envelope parabola sets the sample. `FirstArrivalRefinedByPhat = false` together with a sub-gate
  confidence is the honest "coarse" signal.
- **Invalid results.** A band with no energy has a flat-zero envelope where every sample passes the
  thresholds, and the walk would fabricate a confident delay. `IsValid = false` and zeros are
  returned instead. The engine refuses a run whose junction band is near noise and names the
  channel (dead driver, wrong source, mis-set crossover). Uncertain timing of a strong signal is
  rescuable; absence of signal is not.
- **Strongest-later-peak flag.** A strongest peak clearly later than the first arrival is a
  reflection or mode (the classic narrowband-subwoofer trap). It must sit more than
  `SignalEnvelope.ArrivalPacketMilliseconds` later *and* be separated by a real valley
  (`SeparateArrivalValleyDb` = 6 dB). A band-limited LF direct sound keeps rising for milliseconds,
  and within the ~1/bandwidth envelope blur two humps are one packet's interference structure. The
  exception is a valley deep enough (`SignalEnvelope.ArrivalPacketResolvedValleyDb`) to prove the
  events resolved, since destructive interference nulls faster than 1/BW. Distances are measured in
  the search window's frame: a window re-anchored on a far peak (chain latency) can straddle the
  circular buffer's seam.

`ExcessDelay` reuses the same first-arrival detector for its Peak estimator, so a louder reflection
does not capture the τ reference. Its Slope estimator is the energy centroid (equal to the mean
group delay), which is the τ to subtract when detrending excess phase.

### Run memo

An Auto delay run, a junction tune and the crossover wizard's ranking read the same response in the
same band over and over: every bins build re-reads its gate anchor. In a stereo 4-way Auto delay
the detector was 88% of the run, 110 of its 367 reads repeated one array, band and range, and the
band-pass kernel's envelope was rebuilt 351 times for 16 windows; 83% of a junction tune's reads
were repeats. `AlignmentRunMemo` keeps both for one run: `Compute`, `ComputeStereo`, the tuner's
entry points and `Propose`/`ProposeRanked` open it (an inner opening does nothing), and parallel work
sees it. An arrival read keys on the response array itself, because a run renders each response once
and never writes it after; outside a run nothing is kept. Results are identical.

## Energy onset

`TimeAlignmentAnalysisResult.EnergyOnsetDelayMilliseconds` is a second estimator. It is the point
where the running energy of the envelope, summed from the start of the search window, reaches
`EnergyOnsetFraction` of the energy up to `EnergyOnsetTailSeconds` past the strongest peak. The
crossing is interpolated sub-sample.

**Why it exists.** In the band-limited low end the first peak is a coin. The direct front (rise
time ~1/bandwidth, 7 ms in 65-200 Hz) and an arrival a few milliseconds behind it merge into one
climb, and whether the front's hump becomes a local maximum is decided by a fraction of a dB. On the
field midbass pair the left envelope dipped 0.5 dB after its hump and read 14.4 ms. The right never
dipped and read 21.3 ms: a 7 ms L/R split no cabin produces. Running energy is monotone, so the
shoulder counts either way: the same pair read 2.2 ms apart (1.1 ms on the raw responses, the hand
tune). The idea matches the Hinkley criterion of acoustic-emission onset picking and the energy-ratio
detectors that beat maximum-based methods in the room-impulse-response onset comparison of Defrance,
Daudet and Polack (JASA 2008).

**Fraction = 10 %.** On the midbass pair the L/R onset split is flat from 2 % to 15 % (0.75-1.11 ms
raw, 2.2-2.4 ms processed). It then slides into the envelope's second hump (0.46 ms at 20 %, sign
flip at 30 %). The tail bound makes the total independent of reverberant length: 99.5 % of the
energy sits within 80 ms of the front, and the onset moved at most 0.15 ms between a whole-record
total and a 50 ms one (0.4 ms on the 33-130 Hz sub band).

**Gate = 30 dB under the peak** (`EnergyOnsetGateDb`). The gate is fixed to the signal, never to the
noise. A noise-relative gate would move the front as SNR falls, and at the 12 dB admission floor it
would sit on the peak itself. The gate removes noise far below the front. That matters because the
window ahead of the peak is ~80 ms long: at 20 dB SNR the noise ahead of a 7 ms packet holds about
a fifth of the total, so the tenth would be reached in the noise. The consumer guards the SNR
([Energy onset links](#energy-onset-links)).

## Bandpass padding

Spectral filtering is circular, so how a record is padded decides whether its tail wraps onto its
head.

- **Complete records** (`TimeAlignmentAnalysisOptions.WrapPeakPositions`) are circular by
  construction (deconvolved), so unpadded transforms are exact and peaks may wrap. Zero padding such
  a record manufactures a seam transient that reads as a front. On a field sub transfer IR with a
  DC shelf, the padded envelope dipped 9 dB at the record start and "rose" back, and was accepted as
  a front at 0.2 ms on a record whose driver played at 13 ms. On this path a single forward
  transform serves the mask, the envelope and the correlation. That cut five full-length transforms
  and took a 1 MB transfer-IR read from 741 ms to 477 ms. The correlation can share the transform
  only when the length is a power of two; otherwise it pads to a different bin grid and takes the
  padded route.
- **Cuts** (the default: a channel's valid range) are filtered zero-padded and trimmed back
  (`FilterCut`). This is not a rounding artifact: a lone impulse 64 samples from the end of a
  32768-sample buffer puts 93 % of its own peak into the first 40 ms (`BandpassWrapTests`). The
  guard is sized by the kernel first and then rounded to a power of two. Rounding alone leaves an
  already-power-of-two length, which is what `ChainValidRange` returns for whole-sample delays, with
  no guard at all. Power-of-two lengths also matter for speed: MathNet takes 20 ms at 262144 samples
  and falls back to Bluestein at 317 ms for 262145. The mask, the envelope and the correlation each
  run at their own length, with trims in between.
- **Guard size** (`BandpassGuardSamples`). The kernel's reach is counted in periods of the frequency
  its lower fade starts at (the pass edge when there is no fade, since a brick wall rings longer).
  Measured decay to −120 dB takes 13.2 periods at 20-110 Hz, 14.9 at 27.5-110, 17.2 at 110-290 and
  17.9 at 3.5-20 kHz, hence `BandpassGuardCycles` = 20. The cap is in seconds: a fixed sample count
  was fine at 48 kHz but gave only 9.4 of the ~15 cycles needed at 384 kHz. Two seconds is twenty
  cycles at 10 Hz, the lowest fade start a band-limited read can produce.
- **Envelope.** The Hilbert transform is circular too, so a cut's envelope is padded in
  `EnvelopeOfCrop`, not in `SignalEnvelope.Envelope`, whose contract (a bin-centred cosine yields a
  flat envelope) padding would break. Half a buffer is ample because the analytic kernel 1/(πn)
  decays to −80 dB within a few thousand samples.
- `BuildKernelEnvelope` gives the zero-phase mask's own ringing envelope by |offset|. The detector
  uses it to tell the window's pre-ring from a genuine earlier arrival.

## Arrival certificate

`ArrivalCertificate` is the one classification shared by the cross-side links, the donor
certificates, the stereo bridge and the manual Time Alignment mode
(`TimeAlignmentAnalysis.ProbeArrivalHonesty`). A full-band read is compared with the same record's
upper-half read:

- **Latched.** The full band times a feature far *later* than its own upper half (a modal latch),
  so the read is garbage.
- **Unverified.** No certificate either way. Either a read is unmeasurable or low-SNR, or the probe
  itself timed a far later feature (weak in-band HF blinds it). The read stays usable but earns no
  tight scene lock.
- **Verified.** The two agree within the dispersion one wavefront can show.

## Arrival honesty probe

**Stage-1 conviction.** A steep low-pass can concentrate a pair band's energy below the corner,
where the direct front hides under a modal build-up deeper than the search depth. On a field midbass
under LP 180 Hz/36, the 90-360 Hz read was 21.96 ms against a 10.99 ms front in its upper half. A
convicted latch does two things. First, the pair re-anchors on the half-band reads (only when both
certificates are not Unverified). Second, the seed reach veto is lifted, because measuring the PHAT
extremum against a convicted arrival would enforce the very cycle skip the veto prevents. The
half-band anchor is mushier (an octave of HF mush can drag a woofer 6 ms off), so it only recentres
the correlation window and the fallback diff; a trustworthy PHAT extremum still wins the seed.

**Conviction without a comparable replacement changes nothing.** If the other side's probe read
different physics, the corrupted diff keeps centring the window and the reach veto stays armed.

**One read.** The whole honesty pass — prediction grading, dead-zone arbitration, the upper-half probe
and both re-anchors — is `ReadJunctionArrivals`, which stage 1 walks. The Virtual DSP correlation view
drew it as its arrival marker for two releases and dropped it: the read is ten band-limited arrival
analyses and six chain renders, most of the view's build, and the marker told the owner nothing
(docs/tech/virtual-dsp-panel.md#junction-views).
Lifting it would trust an extremum measured around the convicted anchor. The prominence exception
must not undo this either.

**Allowance** (`ArrivalProbeToleranceMs`, per channel because each side's chain smears differently)
has two terms:

1. **Generic dispersion.** Half a period at the probe's lower edge, never under 1 ms.
2. **Predicted skew.** The difference of two `PredictedFrontArrivalMs` readings (full band vs
   probe). A steep low-pass puts the full band's energy below its corner, where the chain runs
   slower. A field midbass under LP 200 Hz/36 read 13.04 ms in 100-400 Hz against 10.16 ms in
   200-400 Hz, a 2.88 ms skew that a flat 2.5 ms allowance would convict. The predicted skew also
   credits the driver's own band dependence (a woofer reads later at 100-200 Hz than at 200-400).
   An averaged chain group delay alone over-credits HP/PEQ-fed channels by more than 1 ms.

The credit must be earned carefully:

- **Both predictions must verify.** Grading only one lets the other be arbitrarily wrong: a
  220 Hz all-pass source verified in 40-160 Hz still earned the whole clamped credit against an
  honest 0.04 ms skew.
- **Inconsistent or Latched sides earn nothing.** The fallback must be independent of the estimator
  it fell back from.
- **Clamped to [0, generic allowance].** Uncapped, the same all-pass source earned 7.79 ms and
  swallowed a genuine late mode. The predictions read a bypassed response that may itself carry the
  mode, and no conviction factor protects this credit.
- **Graded with the override delay taken out.** The prediction excludes bulk delay, so a read taken
  through a settled channel's delay (the bridge's reference top, a cross-side link's settled side
  and its donors) is shifted back before grading. Graded as read, a 20 ms delay turns every such
  prediction Latched and silently drops the credit, and a chain-explained skew then refuses the run.

**Link reads** (`LinkProbeToleranceMs`). An energy-onset read cannot be graded against the
predictor, which speaks in peaks, so it keeps the generic allowance only.

## Predicted front arrival

`PredictedFrontArrivalMs` estimates where the processed arrival should land if it timed the direct
front: the arrival read off the chain-free bypassed response, plus the shift the channel's own chain
applies to a reference impulse in the same band.

- **It sees through modal latches.** A steep crossover concentrates a junction band's energy in the
  room's modal region, so the processed envelope can front on a mode, while the full-range driver
  still shows the front.
- **The bypassed response is not gated.** At a low junction one period is 6-12 ms, and any gate
  short enough to exclude a mode 10 ms out also truncates the front. That biased the archived
  midbass read by 2.5 ms, enough to hand the junction to the flip impostor.
- **The chain term is measured, not derived** (`ChainArrivalShiftMs`). A reference impulse goes
  through the real `ApplyChain` and is read by the real detector. Bulk delay and polarity are
  excluded because they are neutral in the override-free reprocess. An analytic band-averaged group
  delay misses by 3.8 ms on a steep 80 Hz HP and 2.3 ms on a 330 Hz all-pass, and that error is not
  common-mode across a junction. The impulse is as long as the measured content, not the padded
  bypassed array, which is already twice the crop. Both reads go through `ApplyChain` with their
  `ValidSampleRange`, so the detector crops them alike.
- **It is an estimate, never a conviction on its own.** The detector is nonlinear, and a shift
  measured on an impulse does not transfer exactly to a shaped front. Across a source matrix the
  error stays inside a quarter of the conviction threshold for realistic roll-offs. It reaches 1.2
  allowances where the source has strong in-band structure (a steep LP barely radiating there, an
  all-pass twisting phase).

**Grading** (`GradeAgainstPrediction`, `PredictionState`):

- **Allowance** (`PredictedArrivalAllowanceMs`). Half a period at the band's geometric centre,
  floored at `PredictedArrivalAccuracyMs` (2.5 ms). Honest reads land well inside it; field latches
  run 6.7 ms and up.
- **Latched.** Later than the prediction by more than `PredictedArrivalConvictionFactor` (2.0)
  allowances. A driver worked below its own passband (a midbass in a 40-160 Hz band) makes its HP
  cost several ms more than on an impulse. That error is systematic and one-signed, so a marginal
  exceedance is not evidence. Field false convictions fell at 1.01-1.17 allowances and true latches
  at 2.49-3.89.
- **Inconsistent.** Earlier than the prediction beyond the allowance (a truncated front, a
  mis-captured bypassed response), or in the 1-2 allowance dead zone. Neither convicted nor
  certified, and the pair is withdrawn from the predictor.
- **Two-sided on purpose.** An early read is not a latch, but treating it as a confirmation would
  hand a tightened reach to an unverified anchor.

**Both sides must be gradeable.** The timeline stores a difference, and residuals cancel only
between two predictions or two measurements, never one of each. Mixing them injected 2.45 ms of
base error at the v4 cabin's 160 Hz corner and held the mid on the flip impostor. So on conviction
both sides move to their predictions, and a pair with one ungradeable side falls through to the
upper-half probe for both sides.

**The disagreement figure is not an error bound.** Measurement and prediction share a detector, a
room and a bypassed response, so a common bias leaves it at zero. It may only add restrictions.
Where a side was replaced, its residual is unknowable, and the stand-in is twice the per-side
allowance (two sides each within A differ by up to 2A).

## Linear-phase FIR

A symmetric FIR kernel (`LinearPhaseKernelOf`) is exactly a delay of half its length times a
zero-phase filter, and a zero-phase filter rings before its peak as long as after it. The detector
takes its noise floor from the quietest quarter and its arrival from the envelope rise, so a long
pre-ring at a low corner puts both at the mercy of where content sits in the record. A unit impulse
through a 4095-tap linear-phase LR24 at 80 Hz (latency 42.65 ms) read 27.1, 35.1, 38.3, 42.6 or
55.6 ms depending only on impulse position; through 1023 taps it read 9.6-23.6 ms against 10.65.
Predicted fronts and timeline anchors inherited that scatter, and seed windows at 40-150 Hz
junctions landed 9-46 ms off.

Such a stage is therefore known, not read. `ReadProcessedArrival` reads the measurement through the
chain without the kernel (built from the bypassed response's measured content, cut at the end only
so the time origin is kept) and adds `LinearPhaseDelaySamples` at the processor's rate. A zero-phase
filter has zero group delay, so nothing honest is lost. Asymmetric (minimum-phase) kernels are read
normally.

The arrival is not the window. The score's junction windows and the direct-sound cuts open at a
front, and a linear-phase branch's front sits on its peak with the pre-ring ahead of it; cutting
that pre-ring broke the complementary sum and moved the score optimum 11 ms on a real
FIR session. Those windows open earlier by the chain's `ValidSampleRange.LeadSamples` (see
virtual-dsp-analysis.md#window-anchors).

## Latch arbitration

Conviction needs two allowances, but at a bass junction the allowance is about half the crossover
period, which puts conviction at a full period: exactly where a modal latch lands. The upper-half
probe cannot examine a steeply low-passed reference with nothing above its corner. The archived
Passat v2 sub read 26.8 ms against a predicted 13.6 ms (1.7 allowances), and its latch anchored the
junction a period late.

In that dead zone (later by 1-2 allowances, both predictions available, the other side not refusing
the grade), the pair's whitened correlation is the second witness. It is taken at its strongest
lobe within half a period of the prediction-implied lag and within half a period of the
measured-implied lag. If the prediction family wins with |r| ≥ `LatchArbitrationMinR` (0.6) and a
real margin, the pair moves to the predictions (Passat v2: r 0.91 vs 0.81). The floor and the
advantage mirror the direct-coherence calibration. Otherwise the zone withdraws the pair from the
predictor. A conviction by arbitration must not be logged as if the predictor convicted alone.

## Lobe-boundary conviction

When the pair anchor disagrees with its own predicted fronts by more than half the measured lobe
spacing, the anchor cannot say which lobe the junction sits on, so it must not be what resolves the
lobe. A 55 Hz sub junction read 8.7 ms past its predicted front: 0.96 of an allowance, under every
conviction bar, because the allowance *is* half a period at fc and cannot be finer than the lobe
spacing. An older rule only vetoed the extremum and then seeded from the same anchor. It parked the
stack half a period late while an r 0.99 extremum stood refused. Now the anchor is convicted: both
sides move to their predictions, the correlation is recentred once (iterating has no fixed point),
and the extremum is judged on its own quality.

This applies only when all of the following hold:

- The lobe geometry was measured. No separated opposite-sign structure (or an edge-pinned one)
  means no spacing, and the reach rule already refuses there.
- The anchor is still the raw read, not one replaced upstream.
- The disagreement exceeds the predictor's accuracy floor.

## Seed selection

The coarse offset is refined with the dominant GCC-PHAT extremum of either sign. Only its position
is used; polarity and the final lobe stay with the loss search. At mid/high junctions this lands the
stage-2 window on the correct lobe directly. A trough seed matters as much as a peak: at the
inverted cabin sub/woofer junction (whitened trough r −0.97), the arrival fallback plus a
period-wide window left the true lobe and a non-inverted rival a third of a period away within
fractions of a dB, a coin flip worth 3.5-5 ms of sub misalignment.

The timeline stores arrivals as (upper − lower); the extremum is the delay to add to the upper
channel, i.e. that quantity negated.

Distrust rules are applied in order, because each corrupts the next one's inputs:

1. **Edge-pinned.** A lobe cut by the window edge has artifact position and magnitude.
2. **Weak.** |r| below `PhatSeedMinCoefficient`; see [Seed trust gates](#seed-trust-gates). There
   is no peak-vs-trough dominance gate.
3. **Near-tie with the same-sign rival one period over.** Peak-vs-trough confidence cannot see
   it, and the lobe (a whole-period cycle skip) would fall to whichever reflection ran hotter.
4. **Farther from the arrival than the reach.** See [Seed reach veto](#seed-reach-veto).

When the seed is untrusted and the envelope arrival is used instead, the junction is recorded as
untrusted. The key is the junction, not the channel, because the uncertainty belongs to the
relation. Its fine window then reaches toward a half period, whichever side the walk arrives from,
without leaking onto the channel's other junction. For trusted seeds the measured distance to the
opposite-polarity partner is recorded instead (see [Fine-search window](#fine-search-window)).

## Seed trust gates

- **`SeedCorrelationWindowPeriods` = 1.25.** The stage-1 correlation window must hold both polarity
  partners as whole lobes, and at a low junction the arrival it centres on can be half a period
  off, so it needs a full period each side. A window edge that cuts the rival lobe understates it,
  and a gate would pass on a truncated number. The gates that read it are the
  edge-pinned and same-sign rival gates (peak-vs-trough is log-only): a period plus half a lobe
  each side of the centre holds the rival one period over whole, and a seed half a period off
  still has its inner rival whole. Mid/high
  junctions stay on the fixed ±3 ms floor.
- **`PhatSeedMinCoefficient` = 0.15.** The minimum |r| of the dominant extremum. It is deliberately
  low: even a modest genuine extremum beats the envelope, and a slightly-off seed is recovered by
  the onset lock, the loss search or promotion.
- **`PhatSeedMinRivalDominance` = 0.05.** The margin over the same-polarity rival one period over,
  the cycle skip the fine window cannot undo. The peak-vs-trough margin does **not** gate. On a
  perfect synthetic junction (two filters off one impulse, no room, no noise) the peak/trough
  margin is 0.167 at a two-octave band and 0.100 / 0.049 / 0.012 at 1.5 / 1.0 / 0.5 octaves, at
  every fc, family and slope. It measures band width, not credibility. The old 0.1 gate demanded
  60 % of what a flawless junction produces and refused 34 of 40 archived junctions, including
  every one whose extremum the hand tune landed on. The half-period ambiguity it flags is spanned by
  the fine window and settled by polarity, so the figure is now log-only. The rival margin is 0.534
  on the perfect junction; field true rivals sit 0.09-0.53 apart, and junctions failing 0.05 have no
  separated structure at all.

## Direct-sound seed

At and above `DirectSeedMinCrossoverHz` (1 kHz) a second witness runs the same whitened correlation
on the two channels' direct-sound cuts (`CutDirectSoundPair`: 1-2 periods behind each front, so the
drivers and not the room).

**Why.** At mid/tweeter junctions most of the full record is the cabin. Across the archived cabins
the full-record extremum sat 3.4-4.7 periods from the hand tune in half of the 1.3-2.9 kHz junction
cells, while passing every trust gate. The cuts read r 0.58-0.96 with zero catastrophic misses on
the same cells, reproduced two owner tunes to 6 µs, and held their position to ~10 µs across record
rates. Below ~1 kHz a cut does not isolate a wavefront, and the sub/bass junctions belong to the
envelope and the modal-latch machinery.

**Gates.**

- No edge pin.
- `DirectSeedMinCoefficient` = 0.5, far above the full-record floor, because honest cuts correlate
  strongly (field minimum 0.58); a middling r means the cut caught reflections.
- A same-sign rival margin, because a periodic front (an echo one period out) ties its own lobes.
  Honest margins run 0.10-0.47.
- A position within the arrival's reach.

A silenced witness leaves the full-record path exactly as before.

**Adjudication.**

- **The two extrema disagree by more than half a period.** The seed goes to the position with
  higher joint support (the smaller of the two surfaces' |r| within a quarter period), by at least
  `DirectSeedJointTieMarginR` (0.05); otherwise the full-record extremum stands. A phantom lobe is
  strong on the surface that made it and near zero on the other. The four contested cells split
  0.24/0.02, 0.32/0.02, 0.28/0.20 and 0.13/0.02 toward the tuned lobe. The one cell where the full
  extremum was right read 0.51/0.35 the other way. Near-ties (0.03, 0.05) sat within one lobe pair,
  where either window covers the truth.
- **A direct seed exists.** The full-record extremum's reach tightens to
  `DirectSeedTrustReachPeriods` (1.5 periods); the fixed 3 ms floor is 4-5 periods up there and
  vetoes nothing. Honest full-record extrema sit within 1.15 periods of the arrival; phantoms from
  1.66 out (to 3.9).
- **The full-record extremum failed its gates.** The direct front seeds. The envelope fallback used
  to sit 0.6-1.2 periods off the hand tunes in most such cells.

## Seed reach veto

`SeedReachMs` is half a period (the next same-polarity lobe is a full period out), floored at the
fixed ±3 ms span. An extremum at or beyond it is refused; at the boundary itself the two lobes are
equidistant, so it is refused too.

The veto does not apply to a pair re-anchored by a conviction. Otherwise an extremum past the reach
is admitted only when the pair's chain skew explains the offset (see [Chain skew](#chain-skew)), or
when the anchor is disqualified (a chain skew as large as the reach, or a deep pick) and the
extremum can stand on its own (`ExtremumMayStandOnItsOwn`). A conviction without a replacement
keeps the veto.

**Deep picks** (`SeedVetoMinProminenceDb` = half the 25 dB search depth). A pick in the upper half
of the depth is a shoulder on the band's energy; one in the lower half is a separate feature. The
whitened correlation is driven by where the band's energy is. A field 50-110 Hz sub had its front
at 8.7 ms, its body at 23.2 ms, and an arrival 23.5 dB down sitting 14.5 ms ahead of the energy.
Comparing that against a neighbour whose arrival is its band peak measures two different questions,
and the veto refused an r 0.95 extremum that the direct cut confirmed at r 0.93.

**A deep pick alone does not withdraw the veto.** The reach is the seed's last distance constraint;
every other gate is a quality test that cannot tell lobes apart, and stage 2 reaches only the
half-period partner, never the full-period lobe. `MayWithdrawSeedReachVeto` therefore requires
every clause (the direct cut is evaluated lazily, only after the cheap clauses pass):

- The anchor is still the raw measured reads.
- The pick is in the lower half of the depth.
- The extremum clears `DirectSeedMinCoefficient`, not merely `PhatSeedMinCoefficient`.
- At or above `DirectSeedMinCrossoverHz`, the direct cut lands on the same lobe within
  `DirectCorroborationPeriods` (0.25) in the same polarity. Half a period is the flip partner and a
  full period the cycle skip. On the field cabin the three deep-pick junctions agreed to 0.00-0.02
  periods, and every honest-anchor junction disagreed by 0.54-3.88.
- Below that frequency there is no witness, and the rule says so rather than faking one. The cut's
  window is sized in periods (half a period of fade, two of plateau), so at 110 Hz it runs 27 ms
  with the build-up inside it: 96 % of its energy lay more than a period behind the front, and
  shortening the plateau to 1.5 / 1.0 / 0.75 / 0.5 periods walked its extremum to +4.95 / +4.16 /
  −0.24 / −0.38 ms. A sub-band consistency test fails too: the lower half of a pair band is the
  lower channel alone and the upper half the upper channel alone, reading +6.37 and +4.61 against
  the whole band's +1.32. There the extremum's own strength is the footing. The intended target at
  a low junction is the dominant full-record extremum, where hand tunes land (field: r 0.950 with
  the same-sign rival at 0.418).

**Retired clamp.** Prediction-gradeable pairs once had their reach clamped to half the measured
peak-trough spacing (a quarter period). The certificate resolves only max(2.5 ms, half a period),
so it could not certify that reach. At the v5 cabin's 1500 Hz junction a ±2.5 ms "verified" read
enforced a 0.167 ms reach and refused an r 0.587 extremum that the panel measured 0.13 dB better on
average and 2.7 dB shallower in the dip. A gate must not be tightened by evidence fifteen times
coarser than the distance it decides.

## Chain skew

The anchor is a difference of two band-envelope arrivals, and each side's chain drags its envelope
without moving the wavefront the tune aligns. On a field cabin a sub low-pass alone shifted its
band arrival by 12.8 ms while its woofer partner moved 6.1 ms. `PairChainArrivalSkewMs` measures the
non-common part (synthetic impulses through each chain, no room), signed like the anchor
(lowerArrival − upperArrival). It is null if either side is unmeasurable, because the partner's
shift is unknown there, not zero.

- **Subtracted, never added to the reach** (`ChainSkewExplainsSeedOffset`). The half-period reach
  is the no-cycle-skip guarantee. A symmetric reach-plus-skew allowance was rejected: at an 80 Hz
  junction the field's 6.7 ms skew would have stretched it past the full-period lobe. Where the pair
  band sits inside both passbands, the skew is microseconds.
- **The gate reads the raw offset; the correction only opens admittance doors.** Gating everything
  by offset+skew looks symmetric but was rejected. The corrected anchor is a bounded estimate: the
  skew is the chains' full envelope shift, while the honest correction is its group-minus-phase
  part. On the archived v2 woofer/mid junction the hand-tuned lobe sat 1.9 periods from the
  corrected anchor while agreeing with the raw one, and gating by the corrected offset re-vetoed a
  tune the panel measured 0.5 dB better. Raw gating is also what every archived cabin was calibrated
  against.
- **A skew as large as the reach disqualifies the anchor** (`ChainSkewDisqualifiesTheAnchor`),
  exactly like a deep pick, with the same stand-down policy. On a field woofer/mid junction a 3.9 ms
  skew against a 3.0 ms reach admitted an extremum a period from the raw anchor, on the lobe the
  panel measured 0.5 dB better.

## Settled polarity seed

`SettledJunctionPolarity` is the polarity a walk has already fixed before its search. Walks that
search polarity ask the crossover (`CrossoverSettlesJunctionPolarity`). The stereo far-side descent
asks the reference side instead (`InheritedJunctionPolarity`), because polarity belongs to the
driver and inherited polarity outranks the crossover rule. Stage 1 must use the same authority, or
it centres the coarse window on a family stage 2 then forbids. `BuildArrivalTimeline` takes that
authority with no default, so forgetting it is a compile error. Inheritance returns null unless both
channels resolve to a settled counterpart (a mono channel is its own; a twinned one is named by the
pair link).

`SeedFamilyFollowsTheSettledPolarity`: a whitened surface's peak and trough are one comb under two
polarity assumptions, half a period apart, and their |r| difference measures band width. Seeding
from the forbidden family centres the window half a period off, leaving the loss search two aliases
it cannot separate (a field mid/tweeter pair: 0.20 dB apart, direct wavefronts 0.84 vs 0.57). The
seed then goes to the direct cut, not to the record's best extremum in the permitted family. The
record gives no ranking there: its extrema may sit lobes apart (hence
`CorrelationAlignmentResult.PositiveOppositeNeighbor`), and the adjacent lobe is chosen by distance
alone (0.106 vs 0.110 ms, a 4 µs tie deciding a cycle).

The direct seed must agree with the settled answer; that is the whole guard. A matched split states
how the filters relate, not how the drivers are wired, and a backwards driver reads |r| near 1.0 in
the forbidden family. Stage 2 accepts that trade for the polarity it applies; the seed must not,
because a half-period position error is inherited downstream.

Field case (reference car, 4300 Hz LR36 mid/tweeter): the direct cut read +0.642 ms (r 0.83) in the
inverted family the crossover sums in, the same lobe as the whitened trough within 2 µs. The full
record's dominant extremum was the +0.746 ms peak (r 0.42, in phase); the seed followed the peak and
the search settled a period late. The criterion is the wavefront read (0.84 vs 0.57). The summation
metric happened to agree by 0.24 dB, but at this class of junction it routinely cannot separate a
lobe from its half-period twin, so it must not be quoted as the reason.

## Expected polarity

`FilterPolarityPreferenceDb` is how much better the junction's own two filters sum at the corner
with one channel inverted. It is well posed only for a **matched split**: the low-pass and high-pass
share family, slope and corner (within `MatchedSplitToleranceHz` = 0.5 Hz; corners are typed, not
measured). The summing polarity is then a designed property of the order: LR12/LR36 and BW12/BW36
sum inverted, LR24/LR48 and BW24/BW48 in phase, and BW18 is indifferent at 90°.

Anything else returns NaN. Split corners or slopes overlap across a region, and their best relative
delay is not zero, so summing them as they stand answers the wrong question. The v2 cabin's
1500/1900 Hz BW36 split reads "inverted, +1 dB" that way, while the real junction (each polarity at
its own optimum delay) prefers in phase by 3.3 dB at the owner's setting. The LR24-against-LR48
splits in the 3RC and Passat cabins behave the same. `ExpectedInversionMarginDb` (1 dB) only keeps
filters that "say nothing" in phase instead of flipping on rounding noise.

In stage 2 (`AlignChannelAtJunction`) a matched split forces the polarity; both answers are forced,
since LR24 sums in phase as decisively as LR36 nulls. On the v6 cabin with an LR36 mid/tweeter split,
letting the summation decide gave the two sides opposite answers on 0.2-0.5 dB margins: the in-phase
option buys the null back by sliding a quarter period. A caller polarity (stereo inheritance)
outranks the rule. The rule also applies only:

- **Without a wide seed.** There the window spans several lobes and the recovery machinery compares
  both polarities. On the v4 cabin's wide-seeded 180 Hz BW36 junction, forcing the correct flip
  moved the channel a period off.
- **Outside a joint two-neighbour search.**
- **At or above `DirectSeedMinCrossoverHz`.** Lower down, modes shape the band, and the archived
  matched BW36 splits at 70 and 180 Hz answer polarity by moving up to a period rather than
  flipping.

## Fine alignment at a junction

`AlignChannelAtJunction` is shared by the mono walk and the stereo descent. The window has three
authorities, strongest first: a scene lock (the pin is the window), the onset lock, and otherwise
the coarse base(s) ± the period-scaled range.

- **Clean reprocess.** The searched channel is dropped from the overrides, so its IR is undelayed
  and the chosen delay is absolute. An earlier uniform shift applied to a not-yet-searched channel
  cannot leak in. The fine, wide and retry searches and the onset lock all read this one state.
- **Traveling windows.** Every loss measurement windows each response at its own band-limited front
  (`BuildAlignmentBins`), detected once per bins build, so every candidate is scored through the
  same windows. Valid ranges travel along, so a delay's silent prefix never reads as SNR. A shared
  anchor could not work: mid-cascade a settled neighbour can sit further away than a band-sized
  window spans.
- **Level match.** The lobe choice must not depend on playback gains. The match saturates at its
  cap, after which the user is told to level the gains.
- **Secondary neighbour.** When a second settled neighbour exists (the shared mono sub below a
  descent channel), both junctions are optimized together: both neighbours are fixed, the band
  spans both, and the anchor sits between the two bases. Otherwise the channel buys a perfect upper
  junction while parking a period off the sub.
- **External prior.** A cross-side Δ-consistent delay replaces the base as the gentle tie-break
  when supplied.
- **Edge retry.** A result pinned to the window edge is retried once with range
  min(1.8 × half period, 3 ms) and a relaxed prior, since the base is suspect. The retry goes
  through the same selection rules, because the raw best could be a flip + half-period impostor.
  The arrival-anchored pick is captured before the retry, so a widened retry cannot stack with the
  promotion reach.
- **Wide-seed lobe gate.** Under a wide seed, picks beyond the trusted reach must beat the best
  near candidate on the prior-free score by the promotion margin (`GateWideSeedLobe`).
- **No evidence in either window.** The run is refused. Fabricating a candidate would apply a delay
  built on an invalid arrival, and skipping the channel is no better: earlier shifts may have
  written its override, and later walks would align against an unaligned neighbour.
- **Polarity purity.** Purity is judged relative to the settled neighbour. An absolute-flag
  preference would rescue a mixed pair at the cost of a quarter period, sliding a tweeter off its
  inverted twin's onset line.
- **Prior-free score.** `AcousticScore` is the loss plus the dip-excess term without the prior.
  `ScoreDb`'s prior scales with the window (sigma = window / 4), so only the prior-free figure
  compares across windows.

**Decision report** (`BuildDecision`).

- The confidence is the prior-free margin over the best rival (another lobe ≥ a quarter period away,
  or the opposite polarity), pooled over the uncapped fine, wide and retry optimum sets. The
  selection lists are truncated (six candidates, 1.5 dB gap), so a margin over them could read
  "unrivalled" only because the rival was cut.
- `DecisionMediumMarginDb` = 0.4 dB: comb noise between real lobes is a few tenths.
- A lock is a constraint, not an acoustic vote: it reports `Locked` without confidence. A wide seed
  or an edge retry tempers a free search.
- Overlap is the weaker per-neighbour fraction, each in its own junction band. Below
  `MinTrustedOverlapFraction` (12 %; healthy junctions share ~19-25 % on the v3 cabin) the
  confidence caps at Low and every decision, even a locked one, is annotated.
- A policy override (for example sub precedence) is reported so that a negative margin does not
  read as an algorithm error.
- The detail text is culture-invariant.
- Post-passes amend decisions so the report describes final delays.

## Fine-search window

- **Span.** Half the crossover period, clamped to [`MinFineAlignmentRangeMs` 0.5,
  `MaxFineAlignmentRangeMs` 2.5]. The coarse error grows with the period, but arrival estimates
  carry an error floor (filter group-delay asymmetry, driver rise time) that does not shrink, so at
  a high split half a period would miss. Extra lobes are handled by the candidate list, the prior
  and `AlignmentSelection`. Wide enough to absorb coarse error, narrow enough not to span two
  same-polarity lobes of one base.
- **Untrusted (wide) seed.** At a low junction a whitened correlation with few in-band periods can
  seed half a period off, onto a flip + half-period impostor whose true partner is beyond the fixed
  cap. The reach grows to `LowJunctionReachFraction` (0.97) of a half period: it reaches the flip
  partner without spanning the full-period lobe.
- **Trusted seed.** It fixes where the adjacent lobe pair sits, not which lobe is right, so the
  window must contain the polarity partner. The reach is the measured partner distance ×
  `SeedPartnerReachFactor` (1.2), so the partner's optimum is interior rather than an edge pin,
  capped at `SeedPartnerMaxReachPeriods` (0.75 periods). Real extrema are not where a monochromatic
  comb says (3.18 ms vs a nominal 3.33 at the v5 cabin's 150 Hz junction). Without this, the fixed
  2.5 ms cap excluded the partner at 10 of 13 archived junctions under 400 Hz (7.57 ms out at 60 Hz,
  3.18 at 150). A hundredth of PHAT coefficient would have decided polarity, and reaching the partner
  through the wide sweep costs the 1.6 dB promotion margin a near-tie cannot pay.
- **Diagnostic sweep.** `DiagnosticFineRangeMs` (3 ms) is always logged, as `[diag]`, including the
  searched window. At low junctions it grows to `DiagnosticFineReachHalfPeriods` (1.25) half periods
  so it reaches past the flip partner.

## Candidate selection

`AlignmentSelection.Select` works on candidates ordered best first:

1. **Delay tie-break.** Within `DefaultDelayTieMarginDb` (0.1 dB) the candidate closest to the
   arrival wins, regardless of polarity; fractions of a dB never choose a lobe. On an 80 Hz
   sub/woofer junction (arrival latched 10 ms early), a non-inverted lobe 3.5 ms from the prior at
   −2.57 dB beat the true inverted lobe 0.54 ms away at −2.61 dB. That 0.04 dB cost 3 ms of bass
   attack.
2. **Relative non-inverted preference.** An inverted winner must beat the best relatively
   non-inverted candidate by `DefaultInvertPreferenceMarginDb` (0.5 dB). Reflections hand
   flip + half-period impostors a few tenths (+0.32 dB at a mid/tweeter junction), while a genuinely
   flipped driver wins by the full prior penalty, several dB. The rescue may sit at most
   `DefaultInvertPreferenceReachMs` (0.75 ms) farther from the arrival: on an 80 Hz sub/midbass
   junction the inverted winner sat 0.79 ms from the arrival, and a 0.03 dB margin handed the result
   to a lobe 4.98 ms out, putting the sub 5 ms behind. The reach is absolute because transient smear
   is absolute. A wide seed dilutes the prior, which is why the reach fences the margin.
   `DeclinedInvertRescue` logs a rescue the reach blocked.
3. **Re-break** near-ties of a rescue within its polarity, measured from the rescue's score (the best of
   its polarity within reach). A pick the first step made is already the arrival-closest within the
   margin of the best and stands: re-broken from its own score, the margin grew to twice its width, and
   candidates at 0.00, −0.08 and −0.17 dB returned the one 0.17 dB down.

Polarity is relative to the settled neighbour. Where the filters expect inversion
(`expectedRelativeInversion`) the preference is withdrawn, not reversed. Reversing it defended the
opposite lobe blindly and measured 0.4 dB worse on a matched 180 Hz BW36 junction.

`GateWideSeedLobe`: an untrusted seed's window spans foreign comb lobes that, under a trusted seed,
only promotion could reach. Inside one window only the prior and the tie-break defend the arrival,
and fractions of a dB overrun both. On an 80 Hz sub/midbass junction a lobe 4.4 ms off beat the
arrival-adjacent candidate by 0.13 dB and started the midbass 4 ms early. So a far pick must beat the
best near candidate on the prior-free score by the promotion margin.

## Onset lock

At a high junction the summation surface is a comb of near-equal minima. The band-limited anchor
marks the first peak of an octave-band envelope, and the two drivers occupy opposite halves of that
band, so their peaks lag their fronts by different rise times. That is a systematic ~0.3-0.4 ms bias
(0.45-0.8 periods at 1.5-2.3 kHz) that parks the anchor between lobes. The broadband threshold
onset (`EstimateBroadbandOnset`) marks the front itself, the feature a human validates on the IR
plot. Where the front is sharp, the window is the onset anchor ± reach; the edge retry and promotion
stay shut, and the sum only polishes and chooses polarity. The wide sweep still logs what the lock
excluded.

- **`OnsetLockMinCrossoverHz` = 700 Hz.** At 1.5-2.3 kHz the 10-vs-50 % onset spread is ~0.3
  period. At 220 Hz it is milliseconds (thresholds land on modal build-up), and at 80 Hz there is no
  front at all.
- **`OnsetLockReachPeriods` = 0.75.** Admits the onset error (~0.3 period), the per-driver
  group-delay split and the half-period flip partner, and excludes the full-period lobe.
- **`OnsetLockMaxSpreadPeriods` = 0.5.** The onset *difference* is read at 10/25/50 % thresholds
  (per-channel spreads partly cancel) and must agree within this. Smeared or reflection-led fronts
  (off-axis drivers, modal bass) make the lock stand down.
- **`OnsetLockMinimumSnrDb` = 20 dB.** Random crossings in noise can look stable across thresholds.
  A pure-noise Hilbert envelope peaks ~13-14 dB over its quiet quarter (the Rayleigh peak factor at
  this crop), while loopback measurements run 40 dB and more.
- **Only where the anchor is the arrival envelope.** A trusted whitened extremum is the better
  witness, because a threshold onset differences rise times. At the v5 cabin's 1500 Hz junction the
  mid's front rose over 0.274 ms and the tweeter's over 0.037, so the onset difference moved
  0.237 ms (0.4 period) across thresholds and landed on the weakest lobe.
- **Not for joint two-neighbour searches**, and a scene lock outranks it.
- **`OnsetLockState`** keeps the lock's cap for the stereo co-move, which must keep each locked
  junction's front gap within it (gap_after = gap ± (delta − neighbourDelta)).

## Wide-window promotion

At junctions the onset lock does not govern (below its frequency, or with a smeared front), the fine
window is still centred on a coarse arrival that may be a lobe off. The wide-window optimum is
promoted only when it is clearly better:

- **`WideWindowPromotionMarginDb` = 1.6 dB** on the prior-free score. Comb noise between real lobes
  runs up to ~1.4 dB (a false hop offered 1.40), while a real envelope error shows as ~2 dB across
  the basin (a genuine recovery offered 1.91). A distance-scaled ramp cannot separate those two
  points at any slope; a flat threshold can. Declined promotions with more than 0.2 dB of gain are
  logged.
- **`PromotionReachPeriods` = 2.5** from the pre-retry arrival pick. Recovering an arrival a lobe or
  two off at a degenerate junction (a spectral gap between corners turns the correlation into
  near-equal lobes) needs ~2 periods. Beyond that the sum is a comb of near-equal minima: an
  uncapped window let an alias 3.9 periods out win on 0.25 dB. The cap also bounds the far-alias
  inflation from the wide window's weaker prior.
- **Lobe snap** (`SelectPromotionLobe`). The deepest-summing lobe tripped the gate, but it is not
  necessarily the right cycle. At a 1500 Hz split two adjacent same-polarity lobes both cleared, and
  the 0.14 dB deeper one was a full period past. The pick is the arrival-nearest same-polarity lobe
  that independently clears the gate; the gate winner always qualifies.
- An onset-locked junction never promotes.

## Sub precedence

At a junction with the shared mono sub, a near-tie between the lobe that leaves the sub trailing the
stack and the one that leaves it leading is not acoustically resolvable, but it is perceptually
one-sided. The first wavefront binds the bass to the localizable midbass transient (precedence), so
a slightly leading sub reads as "bass up front" and a trailing one as sluggish and detached.

`PreferSubLeading` replaces a pick that leaves the sub trailing the envelope anchor by more than
`SubPrecedenceSlackMs` (0.5 ms) with the anchor-nearest candidate that does not, within
`SubPrecedenceMarginDb` (1 dB).
The margin sits above the near-tie scale and just under the ~1.4 dB comb-noise ceiling within which
a mode can flatter either side. It was calibrated on the v3 cabin, where the leading lobe scored
0.66-0.73 dB under the trailing pick yet localized the bass to the front stage. The pool spans the
fine and wide sets on the prior-free score, because the prior is what keeps parking the result on
the trailing lobe. The lead is bounded to one period past the anchor: a sub leading by whole periods
is detached the other way. The rule does not apply under a scene or onset lock.

## Direct-coherence witness

Where thin overlap ties a lobe with its polarity partner within hundredths of a dB (split corners
like 1500/1700 Hz at 48 dB/oct leave half an octave), the whitened correlation of the two channels'
direct sound (`CutDirectSound`) still separates them. r at the lobe measures how well the direct
wavefronts' phase slope matches across the whole overlap; the flip partner is phase-equivalent only
at the corner. Above the bass, fusion and localization follow the direct wavefront, and on the
reference car the hand tune sits on the lobe this figure prefers (r 0.89 vs 0.81) while the score
reads a 0.04 dB tie.

The witness only arbitrates ties within `DirectCoherenceTieMarginDb`; a larger score preference
stands, because the score reads the whole windowed sum the cabin produces. It stands down:

- below `DirectCoherenceMinCrossoverHz` (120 Hz), where two periods of cut span the room's build-up
  and archived sub junctions read high r at delays whole periods away;
- under a lock, a forced polarity or a joint search;
- where coherence is weak or the advantage is inside its noise.

Field calibration over six cabins: genuine mid/tweeter discriminations read |r| 0.74-0.96 with
advantages from 0.08; the 0.6 / 0.05 thresholds sit under those. A coherence-free junction (Passat
C/D, r 0.07) cannot vote.

## Coherence ladder veto

Where the direct correlation's advantage is itself slim (below `LadderVetoMaxAdvantage` 0.10), the
arrival-coherence ladder votes, and a decisive vote for the standing lobe vetoes the swap. The two
witnesses read different things. The correlation is one whitened comb over the band: it carries
polarity, but neighbouring lobes differ little. The ladder cuts sub-band probes at their own scales:
it is polarity-blind (see `VirtualCrossoverAnalysis.ArrivalCoherencePoint`) but counts how many bands
want the upper channel where a candidate puts it. A slim advantage plus a clear band disagreement is
the one combination where the comb is deciding on noise.

- **Frequency.** Only above `LadderVetoMinCrossoverHz`, where the ladder's windows are short enough
  that a band optimum is a wavefront, not a mode.
- **Votes.** Only coherent bands vote, and the vote needs `LadderVetoMinBandMargin` bands. The
  archive's high junctions split 4 to 1 where the veto belongs (v3 L: swap asked on a 0.07 advantage
  with a 0.05 dB score gap) and 3 to 2 where it does not (v6 L, decisive at 0.11 and never gated).
- **Placement.** The ladder reads the pair at its applied alignment (lag axis a few periods around
  zero), so the pair is first placed at the standing candidate with whole-sample `PlacePairAt`. A
  negative candidate slides the neighbour later instead, since a front cannot be slid earlier. A
  wrong sign would offset every reported lag by the candidate delay.
- **Reporting.** A veto is reported as a decision.

## Direct lobe check

The tie arbitration above only weighs the flip partner, and only inside `DirectCoherenceTieMarginDb`.
It cannot see the other failure: a search whose **window never contained the answer**. The coarse base
comes from the arrival timeline, so one overruled arrival displaces it, and the fine window — half a
crossover period — then holds a set of candidates none of which is right. No arbitration among them
can repair that, because the right lobe is not among them.

`DirectLobeWitness` therefore asks the same whitened direct-sound correlation about the **final** pick,
over a full period either way, and reports two different things:

- **A better lobe among the candidates.** `Overturns` needs the rival to reach `MinimumR` (0.6, the tie
  arbitration's own floor) and to beat the standing pick by `LobeAdvantage` (0.25). That is five times
  the tie arbitration's advantage: one comb separates neighbouring lobes poorly (see
  `#coherence-ladder-veto`), so only a gulf may move a pick the summation already made.
- **A lobe no candidate occupies.** `NamesAnUnreachedLobe` needs only `ProbeAdvantage` (0.10) — a probe
  proposes nothing by itself, so it may sit at the arbitration's resolution. One extra search runs
  centred on that lag (`SearchJunction`'s `centerOverrideMs`, which also carries the prior, since the
  arrival anchor is what sent the search to the wrong place). Its result is adopted only where the
  **summation also prefers it** by more than `DecisionMediumMarginDb` — comb noise. Not the
  score-only `WideWindowPromotionMarginDb`: that bar is for a move the score carries alone, and here
  two independent witnesses agree. Where the summation does not agree, the junction is reported
  unsettled with the lag the wavefronts wanted.

The v6 cabin's 200 Hz junction is the case it was built for. `B`'s arrival in 100-400 Hz read 19.6 ms,
the modal-latch conviction re-anchored it to 10.3 ms, and `C`'s window came out 1.3-6.3 ms while the
hand tune sits at 10.1 ms — 3.7 ms outside it. The correlation reads r 0.97 at 9.98 ms against 0.47 at
the search's own pick. The check first moves the pick one lobe among the candidates (r 0.47 -> 0.85),
then probes 7.5-12.5 ms, finds 10.05 ms 0.93 dB better, and lands 0.07 ms from the hand tune.

Field effect (16 archived sessions, 44 junctions): two v6 sessions change, both improve — the 200 Hz
junction by 1.18 and 1.00 dB average and by 4.05 and 2.11 dB of dip — and nothing else moves by more
than 0.005 dB. Over the whole battery the proposal goes from 0.021 dB WORSE than the saved hand tunes
to 0.037 dB better (21 junctions better / 13 worse, against 18 / 16), and dip from -0.080 to +0.108.

It stands down under a lock, a forced polarity or a joint search, and below
`DirectCoherenceMinCrossoverHz`, where `#low-junction-polarity` takes over.

## Low-junction polarity

Below `DirectCoherenceMinCrossoverHz` the direct-coherence witness stands down, and nothing else
separates a lobe from its half-period-plus-inversion twin: the two are phase-equivalent at the
corner, and away from it one driver dominates, so the summed magnitude barely moves. Measured over
the archive (30 sessions, 9 cabins, 190 junctions), **32 of 62 low junctions carry an
opposite-polarity lobe within 0.5 dB** of the winner, some within 0.05 dB — the margin
`AlignmentSelection.DefaultInvertPreferenceMarginDb` is asked to defend.

`LowJunctionPolarity` reads the other evidence the channels carry: the neighbour's tallest crest
fixes a sign, and the variable channel's tallest crest of each sign says how far that channel would
have to move to meet it in phase and inverted. The meeting nearer the fronts' anchor names the polarity:
nearer the delay the arrivals predict, not nearer the channel's undelayed position, which handed the
vote to whichever crest met closer to 0 ms once a channel had to move several milliseconds (identical
80 Hz LR24 channels 8 ms apart read inverted). The archive figures below were taken before that change
and want re-taking. The neighbour
is read as rendered, its own settled inversion applied, so the meeting names the variable channel's
absolute polarity; the relation to the neighbour that the filters and the tie-breaks speak in is that
XOR the neighbour's flag, taken once. This is crest
matching, which `#predicted-front-arrival` rejects for **timing** — a filtered channel's crest trails
its front by the crossover's group delay — and the rejection stands: the vote picks a branch, never a
delay. Two channels either side of a low crossover carry similar group delay, so the branch survives
what the delay does not.

- **Frequency.** Only below `DirectCoherenceMinCrossoverHz`, so exactly where the direct-sound
  correlation stands down and never alongside it. Above the bass the crests stop tracking the fronts
  anyway: over the archive the vote matches the matched-split filters on 33 of 38 junctions under
  150 Hz and on 8 of 36 between 150 Hz and 1 kHz.
- **Broadband, deliberately.** The crests are read off the processed responses as they are, not
  band-limited to the junction. The objection is fair — a midbass under an 80 Hz split carries its
  passband up to 300 Hz and its tallest crest need not belong to the corner — but the archive
  answers it: on the 30 distinct low matched junctions the broadband read matches the filters on 23,
  the read band-limited to the junction's overlap band on 21; they disagree on 6, and the broadband
  one is right on 4 of those (both Passat sides, the v2 right side, one negative-control session).
  Below the corner a filtered channel is one click of its passband, and its crest is that click.
- **Authority, per candidate.** Only candidates of the voted branch that the prior-free score ties
  with the pick (within `TieMarginDb`) reach the tie-breaks: those read the prior-laden score and
  would otherwise hand the vote to a lobe the acoustics never tied.
- **Decisiveness.** `IsDecisive` requires the losing sign's crest to sit at least a quarter period
  farther than the winner's. A dispersive channel carries both signs at nearly the same distance
  (`FirCrossoverAlignmentTests`' tilted driver: 0.02 ms apart at a 120 Hz split), and then the nearer
  one is noise — a magnitude tilt would otherwise move the verdict.
- **Authority.** The vote only breaks ties, within `TieMarginDb` on the prior-free score, and only
  among lobes inside one and a half half-periods of the standing pick, so it cannot walk a period.
  It stands down under a lock, a forced polarity or a joint search.
- **Cross-check.** The matched split does not decide down here (see `#expected-polarity`) — it never
  flips anything — but it does withhold the crests' authority: where it is well posed and says the
  opposite (5 of the 38 archived low matched junctions), the crests stand down, the summation's pick
  stands, and the junction is reported unsettled with its confidence dropped to Low. Without the
  stand-down the Passat cabin's 65 Hz junction moved off the branch its filters and its score agreed
  on, on crests 6.3 ms apart.

Judged against those filters, the crests and the summation score are equally accurate over the
archive's low matched junctions — 33 of 38 each — but they are not wrong in the same places: they
agree on 29, are both wrong on 1, and disagree on 8, which hold 8 of the 9 junctions where either is
wrong. That is why the vote is a tie-break and a report rather than an authority: a disagreement is a
coin flip, and saying so beats presenting one as a reading.

Field effect (16 archived sessions, 44 junctions, judged by the panel's summation loss): one session
changes. The v4 cabin's sub junction leaves the in-phase branch its score preferred for the inverted
one the crests and its saved tune agree on, its B/C dip improves 0.46 dB, and its total average
improves 0.02 dB. Everywhere else the gate is inert or only reports.

Re-measured with the walk anchored on the top channel and every post-descent pass on the physical
sum (10 stereo sessions, 58 junctions; 16 mono runs, 88): switching the vote off changes one session
again, now v3, whose sub would drop to the half-period-plus-inversion twin at −3.78 ms inv instead
of 1.56 ms in phase; its two sub junctions read 0.02-0.04 dB worse on the average and 0.03-0.13 on
the dip, the battery +0.113 → +0.111 (dip +0.023 → +0.018). Four junctions carry the unsettled
report. Removing the vote was on the simplification list and is declined on that measurement: a
sub inverted against its woofer on a tie is the outcome the owner asked the engine to avoid.

## Stereo cascade

`ComputeStereo` aligns two sides that never meet at a crossover:

1. **Left side** exactly like `Compute`. Mono channels (typically the shared sub) are part of that
   walk and final afterwards.
2. **Bridge.** The right top channel is fitted to the settled left top by band-limited envelope
   arrivals in the top band, honouring `SceneOffsetMs`. It is not a cross-correlation: same-band L/R
   drivers sit at different spots with different room paths, their HF correlation is lobe-ambiguous
   noise (r ~0.3, dominance ~0.01 on real cars), and the envelope arrival is what the image follows
   up there. A positive offset makes the plan's Right (far) side lead, pulling the image toward the
   dash centre; right-hand-drive callers pass the plan mirrored.
3. **Right descent** junction by junction from the bridged top (the same walk as stage 2), skipping
   mono channels whose right junction is only measured (`MeasureFixedJunction` logs the price of
   sharing one sub). Settled polarity is inherited, not read from the crossover.
4. **Post-descent passes**, then a rebase of the union to zero.

Every uniform shift spans both sides, or the bridge's offset would silently break.

**The battery can judge this cascade, and until recently it could not.**
`SessionBatteryHarness` mirrored the panel's single-side Auto delay, so the bridge, the cross-side
targets, the scene lock, the mono co-move, the far-side polish and the polarity symmetry had no
archive coverage at all — the numbers in this file that predate that ran through `Compute`, one side
per session. Setting `RESONALYZE_SESSION_BATTERY_STEREO` runs `ComputeStereo` instead and judges both
sides, through the panel's own plan builders (`CollectStereoSides`, `PickStereoBridge`,
`StereoBridgeBand`, `ComputeStereoAlignment`) so the battery cannot drift from what the app does.
A session with no front-chain pair resolved on both sides says so and falls back to one side.

**Bridge gates.** The bridge is the single link between the sides, so its arrivals are gated, not
trusted:

- A silent band or an SNR below `MinimumArrivalSnrDb` (12 dB; clean records run 40-70) refuses the
  run.
- Each side must pass the honesty certificate, because SNR proves a strong signal, not that both
  sides timed the same event: a strong early reflection passes SNR and would skew the whole right
  side. A Latched verdict refuses; an unmeasurable upper half lets the bridge proceed with
  confidence capped at Low.
- Bridge confidence is the weaker side's SNR (Low within ~6 dB of the floor, High from
  `BridgeHighSnrDb` 30 dB).
- A right top that needs to be advanced makes everything settled so far be delayed by the deficit.

**Polarity** belongs to the driver. The right top inherits the left top's sign before the descent,
and each right channel inherits its left twin's, so asymmetric per-driver inversion is structurally
impossible. There is no sum-loss polarity guess for the tops: two separated tops comb-filter, and
the guess would invert an identical off-axis tweeter alone. `EnforcePolaritySymmetry` restates the
invariant in one testable place. Reverse-wired drivers are a manual flip.

## Stereo branch check

The reference side settles its junctions alone, and the far side then inherits that polarity driver by
driver. So a junction the reference side could barely tell apart — the archive's v6 200 Hz split reads
r 0.75 against 0.81 for its flip partner, 0.04 dB on the panel metric — commits the far side too, and
the far side is the one that pays: at that junction it lands anti-correlated (r -0.97) and 0.45 dB down.
The information that would settle the branch lives on the side that is never asked.

`RebalanceJunctionBranches` asks it, after both descents. For each junction that has a twin on the far
side it probes ONE move: the whole stack ABOVE the junction, on both sides, shifted half a period and
flipped. That operation changes the junction and nothing else — every junction above it moves rigidly,
which is why a lone pair co-move cannot express it (the tweeter has to follow the midrange).

- **Finding the candidate** is an analytic scan (`StereoJunctionBranch.Read`, `SumLossEvaluator`
  rotations either way around the half period). The feasible set is searched first: the delta that
  serves the far side most is not always one the reference side can live with. The refinement step
  is 0.1 ms or an eighth of the half period, whichever is smaller: where the eighth is the step the
  flip partner itself is probed, and under the cap the grid is far finer than the lobe. A flat
  0.1 ms probed 0.15 and 0.25 ms at a 2500 Hz junction and never 0.20, and a single point at 5 kHz.
  The optimum is then re-read at the better of the two DSP ticks around it (`Quantize`, the scan's
  own preference: reference-feasible first, then the far gain) before anything else reads it, so the
  re-render judges, the log names and the alignment carries a delay the processor can play.
- **Adopting it is decided on a RE-RENDER.** The scan rotates inside a window anchored to the current
  fronts, and half a period is where that approximation is weakest, so the candidate is applied to a
  trial alignment, `reprocess`ed, and measured. Worth the reprocess: on the v6 junction the scan and
  the re-render disagreed by 2.8 dB on one half-band.
- **The far side must gain** more than `FarGainDb` (0.30) and **the reference side must not pay**
  more than `ReferenceLossDb` (0.10). This pass is for branches the reference could not tell apart,
  not for trading one side against the other — `RebalancePairsKeepingScene` states the same rule for
  its own polish ("a two-side mean buys the far junction with the near one").
- **No half-band may lose more than the far side gains** — both halves of both junctions. Scale-free
  on purpose, so there is no threshold to overfit: the far gain is the whole justification for
  disturbing a settled junction, and damage beyond it is not paid for.
- **The field stays realizable.** The move is optional, so one whose rebased span would pass the
  device ceiling is declined rather than left for the final feasibility check to refuse the whole run.
- **Both reads are the physical sum**, as in every post-descent pass (see [One sum, one
  veto](#one-sum-one-veto)). The level-match artefact was found here: on the v6 200 Hz split the
  level-matched 200-400 Hz half read the owner's tune at a −8.9 dB dip against −2.1 in the physical
  sum, and vetoed a 0.3 dB difference as 3.2 dB.
- **The far gain is the allowance, not a flat margin.** The mono hop's 0.1 dB margin was tried here
  on the archive: it refused the v6 200 Hz branch the owner tuned, whose right lobe costs the left
  200-400 Hz half 0.26 dB for 0.53 on the far side (v6-11 right B/C went −0.33 → −0.61 dB), and the
  v4 750 Hz branch with it. A true lobe does not hold every half on this junction; the far gain
  does the judging.

The trial and the adopted move go through the same `ApplyBranchMove`: a delay without the flip is the
worst of both branches, which `RebalanceJunctionBranches_AdoptedMove_DelaysAndFlipsTheStackAbove`
pins with a reference junction tied between its lobes and a far tweeter wired inverted on the alias.

Field effect (10 stereo sessions, 20 sides, 58 junctions, walk anchored on the top channel): five
moves are adopted. The v6 200 Hz split goes +2.58 ms flipped and lands on the owner's lobe (left
within 0.13 ms of the hand tune); v2 takes two, v4 one at its 750 Hz split (a wash by the metric,
better dip on both sides), and the synthetic array one at its mono sub junction (a wash). The battery
moves from +0.091 to +0.120 dB on the junction average (31 better / 19 worse, from 24 / 23) and from
−0.092 to −0.050 on the dip. The totals' dip reads worse (−0.585 to −0.803), all of it a −17 to −25 dB
notch on the v6 right mid/tweeter split that no setting removes and where 0.01 ms swings 3 dB.

One delta serves both sides, so a junction the owner tuned asymmetrically (v6: 8.27 ms on the left,
7.63 on the right) leaves a residual on the far side for `PolishFarSideJunctions` to take, within its
period-scaled reach.

That reporting is the pass's other half. A junction whose two sides want different branches is named in
the log whenever the far side would gain more than `NoteworthyFarGainDb` (0.15), even where the move is
refused, because the disagreement is a tuning fact the tuner cannot otherwise see.

## Cross-side target

`CrossSideTargetMs` is the delay that lands a right channel's arrival exactly the scene offset ahead
of its settled left twin. It serves as the right search's prior (a gentle, polarity-blind pull that
breaks lobe near-ties) and as the scene-lock pin.

- **Coarse** targets pin only the lobe.
- **TightLock** comes only from corroborated donor geometry.
- **Null** means no trusted target, and the search keeps its own-side anchor.

**Per-band read.** Both sides are read in one band with the upper-half probe. A full-band read far
behind its own upper half is a latch (an under-seat midbass: 21.2 ms in 80-200 Hz against 13.9 ms
one band up); one far ahead means the upper half timed a later feature. The narrow upper half only
votes and is never a substitute (an octave of mush can drag a woofer 6 ms). A probe that cannot
measure leaves the read Unverified, usable as a lobe pin only. One instrument (peak or energy onset)
is chosen for the whole link, so a split never subtracts a peak from an onset.

**Consistency ladder.**

1. The pair's shared band.
2. On a latch, the channel's junction band, where the direct rise hidden under a mode is usually
   plain an octave up. An unmeasurable link band does not ladder: the link was inadmissible, not
   mis-read.
3. [Donor geometry](#donor-geometry).

If every rung is poisoned, the prior is withdrawn.

**The junction band also witnesses the link certificate.** The link band's upper half can sit under
the same mode, so full and probe agree on the mode and Verified is issued for a latched read. A
midbass link read 22.2 ms in 80-200 Hz, its 126-200 Hz half saw the same hump, and the resulting
−8.4 ms split was scene-locked onto the right midbass. The witness only proves a mode below its own
probe half, which may lie below the link band, so it convicts the link read only when both timed the
same feature (agreement within the wavefront tolerance). A link read that timed an earlier,
different feature stands.

## Energy onset links

Below `EnergyOnsetBandCenterHz` (300 Hz band centre) a cross-side link reads the energy onset
instead of the first peak ([Energy onset](#energy-onset) has the 14.4 / 21.3 ms midbass case, which
the upper-half probe certified and the scene lock enforced).

- **Why the band centre decides.** The coin is a matter of rise time, so the centre decides, not the
  low edge: a mid pair's 200-1610 Hz link band has a 0.7 ms rise and a sharp front. In wide bands the
  first peak is better; mid and tweeter L/R peak splits hold ±0.06 ms where the onset wanders ±0.3 ms
  with early reflections.
- **One decision per link** (`LinkReadsEnergyOnset`), from both full-band reads, applied to both
  sides and their probes.
- **Links only, never the junction timeline.** A link compares two sides of one driver pair through
  near-identical chains, so the onset's bias cancels. The Virtual DSP L / R / Δ read-out uses the same
  rules and detector on its own window, and agrees to a thousandth of a millisecond on clean
  records. The timeline's predictor transfers a chain shift measured on an impulse, and an energy
  onset is a distribution statistic that no such shift transfers: read that way, the predictor
  missed a plain BW36 70-200 Hz band-pass by 2.4 ms and convicted a plain BW48 80 Hz low-pass as an
  18 ms latch.

**`EnergyOnsetMinimumSnrDb` = 30 dB on both sides**, else both sides fall back to peaks. The 30 dB
figure was measured; a Rayleigh-tail estimate had first suggested 40. The fallback is not free,
because on the midbass pair the peaks are the coin. With white noise added to the right record, its
65-200 Hz onset read 16.602 ms at 63 dB, 16.600 at 38, 16.494 at 32.6 and 16.581 at 27.8, never
0.11 ms off. A 40 dB rule sent the link to peaks from 38 dB down, and the 4.5 ms lobe error returned
(5.78 ms split against the tuned 1.35). A chain-shaped front with a 1.4× arrival 7 ms behind drifts
0.05 ms at 36.6 dB and 0.03 ms at 30.4, where its first peak has already broken. A bare band-limited
delta reaches the tenth in the noise at 20 dB. Field records read 45-88 dB.

`ClassifyLinkArrival`: an onset read whose upper-half probe is under 30 dB is Unverified rather than
judged. A steep low-pass or weak upper half can leave the probe far noisier than its full band (in
the 12-30 dB range), exactly where an onset drifts into the noise.

## Donor geometry

When no band reads both direct rises (a latch on at least one side), energy peaks do not substitute:
the two sides can latch onto different modes and fabricate a path (23.5 vs 17.9 ms, a 5.6 ms split
that dragged the right midbass past the scene onto a junction notch). The cabin's L/R geometry is,
however, often measurable on other linked pairs.

- **Donors** are pairs where both sides positively read a clean direct arrival: full band and upper
  half valid, SNR-qualified and Verified. Absence of a proven latch is not proof.
- **Resolution** (`CrossSideLockTier`). Two or more splits mutually within
  `CrossSideDonorAgreementMs` (0.6 ms) are the cabin's L/R offset (v3: mids +1.37 ms, tweeters
  +1.41) and earn a Tight (quarter-period) lock. A lone donor carries its own filter/driver asymmetry
  and earns a Loose (half-period) lock. None, or equally large disagreeing clusters, earn no pin, and
  the free own-side search stands, since a fabricated geometry hard-locked is exactly the
  confidently-wrong pin to avoid.
- **Clustering.** A cluster is the largest window with max − min ≤ tolerance (0.45 / 1.00 / 1.55
  is not a cluster even though all sit within 0.6 of the middle), found by a two-pointer sweep over
  sorted splits. The window is contiguous, so [min, max] names its members exactly; a
  distance-to-median test could catch an outlier. The resolver is pure and unit-tested.

## Scene lock

Right channels whose pair band reaches the localization region are pinned to a non-Coarse
cross-side target within `SceneLockToleranceMs` (0.05 ms): the image outranks the junction handover.
A Coarse target pins only the lobe, as for low pairs below.

- **Localization band.** The target is measured only in the sub-band above
  `SceneLockLocalizationLowHz` (300 Hz), because low soft envelopes carry no localization. At least
  a third of an octave above that edge is required (80-310 Hz is not lockable).
- **Low pairs** are pinned only to the lobe (half the tightest junction period). Their L/R split is
  still physical (path difference), and a comb whose lobes differ by a dB must not choose it:
  unchecked, it put one under-seat midbass at 0 ms and the other at 10.85 ms.
- **TightLock** targets get ±T/4 instead of ±T/2. Where the pair's own direct arrivals were
  unmeasurable, modes shape the junction sum too (its optimum sat 0.6 ms past every
  geometry-consistent point), so multi-donor geometry gets the larger say. A lone donor keeps ±T/2.
- **No reliable target.** The free joint-junction search remains.

## Post-descent passes

### One sum, one veto

Every post-descent pass scores the same thing through the same helpers (`PenalizedLoss`,
`JunctionSum`, `HalfBandCells`, `HalfBandRefusal`):

- **The physical sum**, dip-penalized, of each junction the move touches: the sum the panel's
  junction metric reads. The co-moves read a level-matched sum until this was unified. A level
  match in a half-band where the lower member is on its slope invents cancellations that never
  play (the v6 200 Hz case under the branch check), and a half-band veto over such a read refuses
  honest moves. On the archive the unification alone moved the stereo battery from +0.122 to
  +0.113 dB on the junction average and from +0.082 to +0.023 on the dip (mono: +0.087 → +0.083
  and +0.070 → +0.058): v2's left stack, 5.8 ms off the saved tune either way, settled on another
  lobe, and the v3 right mono co-move stopped at +0.77 ms instead of +1.13. The v6-11 yardstick
  did not move.
- **The half-band veto.** No observable half of a scored junction may lose more than the move is
  allowed. The allowance is the move's own gain for a trim (pair co-move, far-side polish) and for
  the stereo branch (what the far side gains, both sides re-rendered). A mono hop alone must hold
  every half within `MonoHopHalfBandMarginDb` (0.1 dB), the one flat margin left: at a sub junction
  an impostor lobe flattered by a mode wins the full band and loses the clean half (the synthetic
  `ComoveMonoChannels_SubBandInconsistentHop_IsVetoed`, the v3 80 Hz case), and "no cell may lose
  more than the hop gains" adopted that impostor. The same flat margin on the branch refused the
  owner's v6 tune, so neither rule serves both.
- **Scan versus re-render.** The co-moves rotate e^{−jωΔ} inside windows fixed at the pass start
  and adopt on the scan; the branch re-renders its candidate. Re-rendering every co-move pick was
  measured on the archive: the scan and the render agreed within 0.01 dB on every adopted move and
  no stereo row changed, so the extra reprocess per pair and per mono channel was not kept. The
  branch keeps its re-render, where a whole stack moves half a period and flips.

**Pair co-move** (`RebalancePairsKeepingScene`). Both sides of a linked pair move by one delta,
which leaves the L−R timing (the scene) untouched. This is the only lever that recovers the junction
quality the scene mandate cost without touching the image.

- **Order and speed.** Top pair first. The scan is analytic: one reprocess, evaluators built once,
  and each probed delta is an e^{−jωΔ} rotation.
- **Scoring.** Only the reference side's adjacent junctions score. A two-side mean buys the far
  junction with the near one, and the reference side's drivers are closest to the listener. Both
  sides' junctions bound the delta. Every evaluator window is held fixed across probes (a shifting
  window would be the size of the change) and rebuilt from the current render. Scores carry the
  dip-excess penalty, since a plain mean buys a hundredth of a dB with a deep notch. The sum is the
  physical one, and no half of a scored junction may lose more than the co-move gains (see [One
  sum, one veto](#one-sum-one-veto)); on the archive that veto only ever refused moves already
  under the gain threshold.
- **Bounds.** The range is ±`PairComoveSearchRangeMs` (1.2 ms), intersected, for every adjacent
  junction, with half that junction's period around its neighbour's already-applied co-move delta. A flat window around
  zero let two adjacent pairs drift a full period apart: a 0.1-0.2 dB gain walked a tweeter pair a
  lobe off its mid at a 2.3 kHz junction, with the sum back in phase so no loss was seen. Keeping
  the pair where it is stays legal. Onset-locked junctions keep their front gap within the lock cap.
  Bounds are fixed before the search so the delta applies verbatim to both sides. They are relative:
  they close only where the whole field would leave the DSP range, so plans differing by a global
  offset co-move identically.
- **Mono neighbours.** Pairs bordering the mono channel are not co-moved, because the mono is timed
  by the left pass alone (the sub/left-woofer relation must match a left-only run).

**Mono co-move** (`ComoveMonoChannels`). The mono channel's lobe was chosen by the left junction
alone. Moving or flipping one mono channel cannot change any pair's L−R timing, so it is swept across
±`MonoComoveSearchHalfPeriods` (1.0) of its tightest junction's half period in both polarities, for
the best mean dip-penalized loss over its left and right junctions: the compromise a user would dial
by hand. In the field the sub/midbass junctions were near-tied on the left while the right clearly
preferred the flip partner a third of a period away.

- **Evidence first.** Every junction must hold delay evidence on its own. The descent searches a
  combined band that can hide an evidence-less sub junction behind a healthy upper one. Otherwise the
  pass abstains.
- **One render.** Rotation evaluators per junction and per half-band; windows travel with their
  channels, so a probe cannot slide the mono into a fixed window's fade (the older per-probe
  re-rendering used ~40 reprocesses and still did).
- **Hop margin.** A hop (a flip, or a move beyond the in-lobe polish reach) must beat the best
  in-lobe polish by `MonoComoveLobeHopMarginDb` (0.1 dB). A false hop at a sub junction costs up to
  half a period (~6 ms at 80 Hz). Near-tied co-moves measure 0.01-0.02 dB, and genuine two-sided
  recoveries 0.20 dB (v3 BW36, matching the hand-tuned compromise) and 1.36 dB (v2). In-lobe moves
  use 0.05 dB.
- **Sub-band veto** (`MonoHopHalfBandMarginDb` 0.1 dB, the one flat margin among the passes'
  vetoes, see [One sum, one veto](#one-sum-one-veto)). A full-band mean cannot tell a
  genuine recovery from a comb impostor flattered by a mode. The check narrowband ranging disciplines
  converge on (sub-band GCC, multi-scale FWI, GPS widelane ambiguity resolution) is consistency
  across sub-bands: the true alignment holds in both halves, and an impostor wins one and loses the
  other. On the v3 80 Hz sub junctions a false full-period hop gained 1.43 dB full-band while losing
  the clean 40-80 Hz half by 0.29 dB; the genuine recovery's worst half deficit was 0.02 dB.
  - The check runs per (junction, half-band) cell, so a deficit cannot hide behind a surplus.
  - Only observable cells vote; halves where one channel is a filter tail are refused.
  - The reference is the better of in-lobe polish and the incumbent. On a mode-dominated junction
    the polish itself can slide onto a modal trade (an 80 Hz reproduction polished to −2.45 ms,
    where the clean lower half reads −6.6 dB).
- **Relative move.** Results below zero rebase the rest of the field.

**Far-side polish** (`PolishFarSideJunctions`). Each far channel may leave its scene position to
recover its own far-side junctions, by an eighth of the period of its highest junction
(`FarSidePolishReachPeriods`); the bridge never moves.

- **Reach.** The leash tightens up the chain, which is the owner's weighting: 45° at the channel's
  highest junction cannot hop a lobe (a lobe is half a period wide), the midrange keeps its
  0.04-0.08 ms at a 1.5-3 kHz split, and a midbass under a 200 Hz split gets 0.6 ms, where the image
  does not localize. It used to be a flat 0.03 ms sized for the top junction, which left the v6 200 Hz
  split 0.6 ms short on the far side: the owner tuned that junction 8.27 ms on the left and 7.63 on
  the right, and the stereo branch move is one delta for both sides. The reach is a total budget from
  the scene position: each channel's spent trim is carried across the rounds below. Spent per round,
  the second round walked the Passat's mid 0.13 ms at a 0.125 ms leash and its 1 kHz split read
  0.14 dB worse (0.49 on the dip).
- **The bridge has no reach.** It IS the scene: the far top stands at the user's delta to its twin, and
  the chain below it is what gets polished.
- **No half-band may lose more than the trim gains**, per (junction, half-band) cell, the rule the
  stereo branch check and the mono co-move already apply. A period-long leash can sell a junction's
  upper half for its lower one: on the Passat's right 250 Hz split a −0.50 ms trim read +0.38 dB over
  the midbass's two junctions while 250-500 Hz went from −0.78 to −1.96 dB, and the panel — which
  windows the upper half tighter, as the ear does — read the junction 0.7 dB worse. The reads are the
  physical sum, as in every pass.
- **Field effect** of the period-scaled reach with that veto (10 stereo sessions, 58 junctions):
  junction average +0.120 → +0.124 dB, dip −0.050 → −0.005; totals +0.075 → +0.079, dip −0.803 →
  −0.763. Without the veto the same reach lost 0.69 dB on the Passat junction above and 0.21 on the v3
  650 Hz split. On the v6 200 Hz split the far midbass closes +0.20 ms of its 0.6 ms residual: the
  next 0.28 would cost the sub junction's 70-140 Hz half 0.27 dB for a 0.16 dB gain.
- **Gain threshold.** Below the co-move's 0.05 dB, because such a trim only buys fractions of a dB:
  on the v6 cabin the honest gains ran 0.01-0.03 dB, and even 0.02 dB refused them all.
- **Order.** Band order from the bridge down. Mono channels never move here; they follow in the mono
  co-move this pass alternates with (below).
- **Grid.** Absolute ticks of the DSP's 0.01 ms grid, since gains between its points are unrealizable: the
  exact move to a tick is scored and that tick is written. The channel may stand off the grid when the
  pass starts (the descent rebases the field by unrounded amounts), and a move rounded to the grid
  scored one delay and wrote another, or read a tick 0.004 ms away as the incumbent.
- **Feasibility.** The pass never makes delays negative, never shifts the field uniformly, and never
  widens the span past the DSP range, checked against both ends of the rest of the field. Being the
  last pass, it would otherwise turn a valid proposal into a refusal for a hundredth of a dB.

**Polish and mono co-move alternate** (`PolishMonoRounds`, at most 3; the archive converges in two).
The polish moves the far side under the mono channels' right junctions, so the mono co-move runs
again after it; a sub that followed may in turn release a trim the polish had refused for its sake,
so the polish runs again. The rounds go on only while both keep moving: a polish that kept leaves
the mono channels where their last pass put them, a system without a mono channel polishes once, and
a run still moving at the cap says so in the log. A mono trim that follows the polish amends the
channel's decision and keeps its confidence; only a hop re-decides it. On the v6 cabin the far
midbass first takes +0.20 ms of its 0.6 ms residual (the next +0.28 would cost the sub junction's
70-140 Hz half 0.27 dB), the sub then follows by +0.34 ms, and in the second round the midbass
takes the remaining +0.24: its 200 Hz split goes −0.31 → −0.22 dB (dip −2.09 → −1.55), the sub
junctions' dips improve 0.10-0.15, and the right 200 Hz split lands 0.20 ms from the owner's hand
tune instead of 0.44. Ten stereo sessions go +0.113 → +0.118 dB on the junction average and
+0.023 → +0.049 on the dip, the 16 mono runs +0.082 → +0.084 and +0.055 → +0.063, nothing worse.
Polishing before the only mono pass instead cost three v6 mono runs their lobe choice (v6-8's right
sub junction −0.05 dB and −0.21 on the dip): the first pass is where the mono's right junction
votes on the lobe, and the polish must not see a sub still parked by the left side alone.
