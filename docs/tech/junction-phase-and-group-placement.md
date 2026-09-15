# Junction phase, phase gate and group placement

This document covers the application side of alignment in the Virtual DSP (Virtual Crossover) tool and the
Time Alignment panel:

- the **junction phase read-out** (`dsp/JunctionPhaseAlignment.cs`, `JunctionPhaseAlignment`,
  `JunctionPhaseResult`) that scores how two adjacent processed channels sum in phase at their crossover;
- **group placement** (`source/Tools/VirtualCrossover/VirtualCrossoverGroupPlacement.cs`,
  `VirtualCrossoverGroupPlacement`) that times a rear fill or a centre against the already settled front stage;
- **phase gate placement** (`PhaseGatePlacement`, `JunctionPhaseSpectra`, `GatedPhaseCurves`) shared by the
  Virtual DSP phase view and the EQ Wizard;
- **zones, group views and alignment stages** (`VirtualCrossoverZone`, `VirtualCrossoverGroupView`,
  `VirtualCrossoverAlignmentStage`) and the Auto delay dialog/report (`VirtualCrossoverAutoDelayDialog`,
  `VirtualCrossoverAutoDelayReport`, `AlignmentReprocessor`);
- the **Time Alignment panel** (`source/TimeAlignment/TimeAlignmentPanelController.cs`, `AnalysisReadSchedule`).

The alignment engine itself (seed selection, chain walk, arrival reading) is described in `auto-alignment.md`.

## Junction phase read-out

`JunctionPhaseAlignment` is pure math over spectra: no UI and no coupling to the engine. It was
field-validated against a manually tuned cabin: the sweep optimum reproduced the tuned subwoofer delay within
0.3 ms and resolved the L/R compromise the tuner had split by hand, while envelope arrivals in the same band
disagreed in sign and direct-sound phase carried a systematic multi-millisecond room shift.

### Figures

- **Current score** `Σw·cos(Δφ)/Σw`, weights `w = |H_lower|·|H_upper|`: 1 = in phase across the band,
  −1 = out of phase. It is a phase-alignment score, not the magnitude-squared coherence γ² (0..1) the
  measurement pipeline reports.
- **φ at the crossover** (`PhaseAtCrossoverDeg`): lower minus upper, wrapped to ±180°, taken as the weighted
  circular mean over ±1/6 octave around fc (`PhaseWindowOctaves`). It is deliberately local and not the
  straight-line fit's intercept: the intercept extrapolates through interference notches and spectral gaps, and
  on a real mid/tweeter junction it read +158° where the handover stood near −15°. Widths from 1/12 to
  1/3 octave agreed within a few degrees on that junction. When a gap leaves the window empty, φ falls back to
  the intercept and the consistency is reported as 0.
- **Phase consistency** R (mean resultant length, 0..1): how much the window's bins agree on one phase.
  Below `MinimumPhaseConsistency` (0.5) φ is mush (a notch sits at the handover) and no layer may show it as a
  number.
- **Best extra delay / best invert**: the extra delay on the LOWER channel that maximises the band score, relative to
  the current settings (a negative value advances the lower channel: a positive delay on the upper one when the
  lower is at 0), and whether flipping the lower channel wins.
- **Opposite polarity score**: the best score the other polarity reaches, shown in the tooltip so the user sees
  how close the alternative sits.
- **Rival lobe** and **lobe margin**: the best same-polarity local optimum at least 0.4 period
  (`RivalMinimumSeparationPeriods`) from the global one; closer bumps are texture of the same lobe. A small
  margin means the band is too narrow to discriminate whole-period hops. The margin stays a same-polarity
  question, because the polarity ambiguity is reported separately and would otherwise flag every low-frequency
  junction, where flip-plus-half-period always ties.
- **Fit delay / fit rms**: slope and residual of a weighted straight-line fit of the unwrapped cross-phase.
  The fit is lobe-blind, so it disagrees with the sweep optimum when phase offset and slope pull apart; the rms
  says how straight the cross-phase is (modal regions bend it). A degenerate determinant means the bins
  collapsed onto one frequency, and a flat fit through their weighted mean is used.

### Polarity decision

Inverting the lower channel negates every cross-phase, so the inverted-polarity score at any delay is exactly
the negative of the normal sweep: one sweep gives both, and the best inverted alignment is its deepest trough.
A genuine broadband inversion makes the trough reach about +1 while no single delay on the kept polarity can,
so the two separate. φ ≈ ±180° alone is never used to recommend a flip: at fc an inverted channel and a
half-period delay are identical.

A flip is recommended only when it beats the kept polarity by `PolarityFlipAdvantage` (0.05); within that
margin the display marks polarity as ambiguous and keeps the current one. A flip is disruptive and easily
wrong: on a real 80 Hz sub junction the two best scores came within about 0.001. A genuine inversion clears
0.05 easily.

### Sweep

The sweep spans ±1.25 crossover periods (enough to include the ±1-period rival lobes) at 128 steps per period.
The discrete optimum is refined parabolically over three samples and then re-evaluated exactly, so the reported
score is real rather than interpolated. A delay `dt` on the lower channel rotates its phase by `−2πf·dt`.

Bins whose weight product falls more than 30 dB (`WeightGateDb`) under the band maximum are excluded (one side
filtered out or in a null); this is the figure validated in the field probe. Fewer than 8 gated bins
(`MinimumFitBins`) returns null rather than a fabricated read-out.

A crossover at or above the bilinear transform's realizable limit (`BilinearTransform.NyquistFraction` of the
sample rate) is silently clamped to a different frequency by the filter, so the spectra roll off elsewhere
than `crossoverHz` says; the sweep would use the wrong period. The read-out is suppressed there.

### Analysis windows

The Virtual DSP read-out calls `AnalyzeWindowedSpectra`: the panel's phase gate (offset and Tukey durations) at
an 8-cycle frequency-dependent window, which in the view's default mode is the same window its phase curves are
drawn through, so a handover the numbers call inverted is visible on the plot. Both spectra must share one FFT
length and one absolute time origin; per-channel window placement has to be re-referenced to a common origin
first (`DataHelper.SumGatedSpectra`), otherwise the placement difference reads as delay.

Until 2026-09-01 the read-out used a 0.68 s steady-state window, on the grounds that direct-sound phase
disagreed by several milliseconds at subwoofer junctions. Re-measured over the archived cabins (20 junctions in
8 cars) that ground is gone: below 500 Hz the two windows agree to a median 5° and 0.046 ms. The old
disagreement came from the phase gate anchoring on the IR peak, which for a steeply low-passed driver sits tens
of milliseconds after its own front; anchoring on the arrival removed it. The gain is at the top: above 1 kHz
the steady state's ceiling (best score any delay reaches) is a median 0.690, so a correctly tuned tweeter
junction could not score above about 0.7 — the residue is the room decorrelating the paths. Through the gated
window the same ceiling is 0.928. Below 500 Hz the ceiling is unchanged (0.896 vs 0.908).

The steady-state path (`BuildAnalysisSpectrum`, `AnalyzeSpectra`) stays as the reference baseline for window
comparisons and synthetic tests. Its window is sized in time (0.68 s, `AnalysisDurationSeconds`), so the
horizon and the recommended fix do not change with sample rate: the FFT is the next power of two above the
span and the rest is zero padding, not extra signal (otherwise the window would jump up to 1.5× across a
power-of-two boundary, e.g. 0.68 s at 48 kHz but 1.02 s at 32 kHz for the same 32768-point FFT). A cap bounds
FFT cost at exotic rates (768 kHz would ask for a 1M-point transform per channel) by trimming the span there.
An IR cut mid-decay gets a 46 ms half-Hann tail fade, far inside the window and about 60 dB under the direct
sound, so the cut does not splash broadband ripple.

### Thresholds

`MinimumAlignableScore` (0.5) gates the recommendation (extra delay and polarity mark), separately from the φ
gate: a junction can hold one clean phase at fc and still be incoherent across its band. Calibrated on the
archived cabins by sweeping the threshold and asking what suppressing each junction's fix would have cost:
from 0.35 to 0.65 exactly one junction is suppressed — a 2 kHz handover whose drivers do not correlate in their
direct sound (whitened r = 0.07), whose recommended −0.37 ms worsened the panel's summation loss by 1.15 dB and
its dip to −10.9 dB. At 0.70 the threshold starts catching junctions whose fix changed nothing, by 0.80 one whose
fix helped. 0.5 sits mid-plateau and means the band is still 60° out of phase on average at the optimum.

## Group placement

`VirtualCrossoverGroupPlacement` places a whole group (a rear fill, a centre) against a reference that is already
settled. It is deliberately not a junction search: a rear fill shares no crossover with the front stage and a
centre shares none with anything, so there is no handover to optimise, only one number per group. Computing it
from a fixed reference also makes the staging one-way: a placement cannot move what it was computed from.

`Place` works in two steps, the same shape the engine uses at a junction: a coarse band-limited arrival
difference says roughly where the answer is, then the phase-whitened correlation is searched in a short window
centred there. A rear fill can sit 10–20 ms from the front stage; a correlation searched around zero would find
a lobe, and one searched across the whole range would have many lobes to choose from. The correlation's
convention is the delay to add to the second response, so the window is centred on the negative of the
arrival lateness. `Place` returns null when either response has no reliable arrival (validity and SNR) in the
shared band.

`GroupPlacement` carries the delay to ADD to the group (negative when the group is already later; group
normalization turns that into delaying everything else), polarity (a negative whitened-correlation extremum is
a measurement of relative polarity), |r|, and `EdgePinned`. Below `MinimumTrustedCoefficient` (0.25) a placement
is reported but not trusted: groups play the same band from different places so correlation is never as clean as
at a crossover, but this low means no common feature exists.

### Edge-pinned placements

When the extremum sits on the refinement window's boundary, the correlation never found an interior one. The
placement is then the coarse arrival itself and polarity is "not measured" (reported as not inverted): a polarity
read off a clamped window is a fact about the clamp. Reporting the boundary lag instead would put the answer up to
a whole window from the arrival it claims to stand on (2 ms at a subwoofer band) and could flip a channel. The
coefficient is still reported because callers rank on it. This is a weaker reading than an interior extremum and
the dialog says so. With the band-sized window it is common; with the old flat 2 ms it was rare.

### Refinement window

`RefineRangeMs` is a quarter period at the band's geometric centre (`250 / sqrt(low·high)` ms), capped at
`MaximumRefineRangeMs` (2 ms). Correlation extrema alternate every half period, so a quarter period each way
contains only the extremum the arrival anchor sits in.

It used to be a flat 2 ms, "narrow enough not to walk into the neighbouring lobe" — true for a subwoofer and
false above ~125 Hz. On the reference car a centre is placed over 400 Hz–20 kHz, where 2 ms holds eleven
periods; the four candidates inside it stood at |r| 0.20–0.29 (noise) over 2.4 ms of delay, so the winning lobe
was a coin toss and the centre landed 0.5 ms off the midpoint its own arrivals agreed on. The centre's witness
settled the width: refined over a quarter period the two side readings differ by 0.36 ms against a 0.20 ms
scene offset and corroborate; over a half period they differ by 0.67 ms and do not.

Below ~125 Hz the cap governs: there the binding constraint is the arrival estimate's own error, not lobe
spacing, and nothing is gained by a window wider than that error.

### Reference choice

`ChooseReference` picks ONE settled channel, and the band, a group is timed against: the candidate whose overlap
with the group covers most of the voice band 1–4 kHz (`VoiceBandLowHz`/`VoiceBandHighHz`); if none reaches it, the
one whose overlap sits lowest. Ties (including all-zero ties) break toward the lower overlap: its period is
longer, so the envelope arrival's roughly fixed millisecond error is a smaller fraction of a period. The band must
also pass the arrival analysis's own admission rule (`VirtualCrossoverAnalysis.MinimumArrivalBandRatio`), stated
once in `IsWideEnough`, so a chosen band is never one `Place` rejects. Null makes the caller fall back to the
whole stage summed.

Why not the front stage summed: a band-limited arrival is the arrival of whatever plays earliest in the band, so a
centre read against the front sum is timed against the tweeter (which it does not overlap) instead of the
midrange (which it does); on the reference car those arrive 0.2 ms apart. The whitened correlation across five
octaves, most of which the centre does not play, then answers at |r| ≈ 0.27. One driver in the genuinely shared
band is also what a tuner does by hand: mute the rest of the front, match the centre to what is left.

Why the voice band and not "widest overlap": a centre exists for the voice, and 1–4 kHz (upper formants,
consonants) is where the ear is most sensitive and a misplaced centre is heard first. Widest overlap picks the
wrong driver on an ordinary front: a centre high-passed at 300 Hz beside a 60–500 / 500–3000 / 3000–20000 Hz
3-way overlaps the tweeter by 2.74 octaves and the midrange by 2.58, handing the centre to a driver that starts
at 3 kHz. Both figures are part fiction anyway — a channel with only a high-pass is booked to 20 kHz. The band is
narrower than the voice's full range on purpose: below ~1 kHz cabin modes and boundary reflections dominate the
early field, so timing there says more about the room than about the two sources.

### Centre references and witness

A centre is read against each side (`ChooseCentreReferences`, producing a `CentreReferencePlan` or a refusal
sentence in `CentreReferenceChoice`) and placed at the `Midpoint` of the two readings: it plays a signal derived
from L and R and sits between them.

The two readings are each other's witness: they should differ by the scene offset (the figure the stereo run
applies and the metric panel verifies). A larger disagreement means one reading landed on the wrong lobe.
`CentreCorroboration` keeps four tests apart — polarity agreement, offset agreement, coefficient, edge pinning —
and `Describe` lists every failing one, because the note has to send the tuner to the right problem. Polarity is
applied only when both sides agree; a centre inverted against one side and normal against the other is an
unsettled measurement, not a wiring fault. An edge-pinned reading is still used (it is the arrival) but is not a
corroborated phase measurement.

The witness only works if the two readings measure the same thing from two sides:

- **Peers**: the preferred plan is one block's left and right instance, over ONE band for both readings (a
  midpoint between two bands is not a midpoint). A near-side midrange witnessed by a far-side tweeter is two
  different measurements averaged; a mono block picked by both sides is one measurement counted twice and would
  satisfy the witness vacuously at a difference of zero.
- **Own content fallback** (`OwnContent`): otherwise each side's references are summed from what that side plays
  in the band and the other does NOT. A mono front block is in both sides' lists; summing sides whole would put
  it in both references and reintroduce the vacuous witness. An empty own-content list is a refusal, not a signal
  to widen: whatever plays there belongs to both sides, and the "midpoint" would be one reading reported twice.
  The centre keeps its delay. Any overlap counts as "plays in"; whether the content is timeable is left to
  `Place`'s arrival gate on the actual signal, so a silent side yields a refused reference and a weak one a low
  correlation.
- **Band from coverage** (`Coverage`, `WidestShared`): the band is derived from the references as they are after
  removing shared content, as merged intervals rather than lowest-to-highest spans. A side left with a 60–200 Hz
  midbass and a 4–20 kHz tweeter would otherwise still "share" 20 Hz–20 kHz with the other side, and a band in
  the hole measures filter leakage against real content, which correlates about as well as anything that quiet
  and can carry a confident midpoint. Adjacent intervals merge: a crossover is two bands meeting at a corner.
  Only members that play in the chosen band are kept in the plan, so the trace does not name drivers that did not
  contribute.

Refusal is a sentence because the causes need different actions: sides sharing everything is an installation
fact; sides whose own content lies in different spectrum regions is a crossover fact. Only the chooser owns both
numbers.

## Phase gate placement

`PhaseGatePlacement` decides where each channel's phase window sits and which common τ the phase is detrended
by. The Virtual DSP panel resolves it per redraw from its project and gate dialog; the EQ Wizard resolves it from
what its handoff froze (it has no live channels, hence `PlacementChannel` rather than `ProcessedChannel`). Both
must get the same numbers from the same channels or a tune made in one view would not hold in the other, so the
arithmetic lives here. Every method takes the set it resolves over, and that set matters: resolving over hidden
curves would let a channel nobody can see move the windows of those on screen.

- **Auto anchor** (`EarliestStartMs`): the earliest band-limited first-arrival front across the set, memoized per
  IR in `TransferIrStartCache`; robust to head garbage that poisons a bare peak read. Each channel's front is read
  within its own valid range, so a chain delay's silent prefix cannot certify a front.
- **Shared window** (`ResolveSharedOffsetMs`): a pinned offset as-is, or for Auto the earliest front, so the gate
  follows source and delay changes until the user pins it.
- **Per-curve windows** (`ResolvePerCurveOffsets`): a pinned gate is one absolute window for all curves. Auto puts
  each channel's window on its own arrival, which lets FDW's short high-frequency windows sit on that channel's
  first cycles instead of whichever channel arrived first — the point of reading phase through FDW. Per-curve
  placement is only comparable while every window opens before its channel's response, so each is tested by
  `AllowsPerCurveGate` and the whole set drops to the shared window if any fails; mixing placements is worse than
  either.
- **Common detrend** (`ResolveCommonDetrendMs`): one τ for the whole set, so relative phase — what a crossover
  region is read for — survives. Per-channel τ would flatten each curve onto its own arrival. Auto estimates τ once
  from the anchor channel (earliest processed front, the one the shared window opens on) through the gate
  template, then applies it to every driver and the sum.

### Leading-edge loss

`MaxLeadingEdgeLossDb` (−20 dB) caps `DataHelper.GateLeadingEdgeLossDb`: above it a window cuts into its
channel's leading edge and the curve describes what came after the channel instead. It gates per-curve windows
and the panel's gate-placement warning. On the v5 field session (four processed channels, gate 5/50/20 ms)
windows on each channel's arrival start read −28.4 to −72.2 dB, while the arrival-peak placement that drew a
summing pair as antiphase read −3.5 to −10.8 dB (discarding a fifth to nearly half of a steeply low-passed
channel's energy); −20 dB sits in that 17.6 dB gap. The Passat session marks the other end: a 15.06 ms gate
inherited from another car, against processed arrivals at 4.10–5.97 ms, read +1.0 to +15.2 dB (discarding up to
thirty times what it keeps), while the same channels gated on their own arrivals read −42.9 to −55.0 dB.

The ceiling alone is not enough: a gate can be too short to contain a channel's leading edge wherever it is
placed. The project default (0.5/4/1.5 ms) cannot hold one period of a 55 Hz subwoofer, and on the field session
it read −19.4 dB at both the channel's own arrival and the shared one. Refusing there buys nothing and costs the
per-curve placement that keeps late channels inside FDW's short windows, so `AllowsPerCurveGate` also accepts a
per-curve loss no worse than the shared window's. The peak placements the guard exists for are 25.7–61.4 dB
worse than shared, so they fail both tests with room to spare.

### Junction read-out spectra

`JunctionPhaseSpectra.Build` produces the spectra the junction read-out uses: the user's gate offset (or the
arrival it follows unpinned) and Tukey durations, but always a frequency-dependent window of `FdwCycles` = 8,
re-referenced to one absolute origin. Spectra use peak index 0 (gate offsets are absolute times from record start,
the same origin `GatedPhaseCurves` uses), and each is rotated to the record origin through a one-spectrum
`DataHelper.SumGatedSpectra`, because per-curve placement is a different time reference per channel and the
cross-phase would carry the placement difference as delay. No detrend is applied: a shared τ cancels from the
cross-phase and a per-channel one would be the answer itself. The `sampleRate` argument only decides sample
rounding of the placement; each spectrum is transformed at its own channel's rate.

The cycle count ignores the dialog's 4/6/8 selector. That selector shapes a curve for the eye; for a number, on
the archived cabins 4 cycles moved φ by a median 36° against the steady-state reference and flipped the
recommended polarity on 5 of 20 junctions, while 8 cycles moved it 5° and flipped 4 (all of them the
delay-versus-flip tie the block already marks). 8 is also the longest option, closest to the sustained sum the
loss column measures. For the same reason FIXED window mode is not honoured by the numbers: the curves keep the
fixed duration, but a fixed window's ceiling above 1 kHz is 0.693 against 0.928 gated, so a fixed-mode project
could never score a correctly tuned tweeter junction above ~0.7. In the default FDW mode curves and numbers share
one window.

Placement is resolved over the summing channels handed in. In a grouped view that is not quite the drawn set (a
centre is drawn but sums with nothing), so when such a spectator fails the leading-edge guard the drawn curves
fall back to a shared window while these spectra keep per-curve placements; a channel in none of the reported
junctions has no claim on their windows.

`GatedPhaseCurves` draws a wrapped phase curve with a NaN break at each ±180° wrap plus a thinner dashed vertical
at the wrap (at the geometric mean of the two bins, the visual midpoint on a log axis): at full stroke a wrap reads
as a phase transition, and not drawn at all the curve seems to jump for no reason.

## Zones and alignment stages

`VirtualCrossoverZone` (Front, Sub, Rear, Center) says what part of the system a block is, which crossover corners
cannot. Front, rear and centre drivers routinely play the same band from different places: a rear pair high-passed
at 290 Hz overlaps the front midrange and tweeter completely without a junction with either, and ordering by band
centre invents a handover no filter creates. The zone is independent of the `Mono` routing flag: a sub pair can be
stereo, a two-way centre is two mono blocks, one install can carry two mono subs in different bands. Only Center
implies mono (enforced by the panel). The zone selector lists Front, Rear, Center, Sub (`VirtualCrossoverZones.All`); the tuning sheet
follows DSP entry order instead, Sub first (`VirtualCrossoverSheetGroups.SectionOrder`).

Pre-v9 projects had no zone, and "mono" then meant "shared subwoofer". `GuessForLegacyPair` maps a stereo pair to
Front (a rear pair must be re-pointed by hand), a high-pass mono block to Center (no sub plays up the spectrum), and
other mono blocks to Sub. A wrong guess costs one combo-box change; all settings load unchanged.

`VirtualCrossoverAlignmentStage` orders Auto delay: FrontChain, then Rear, then Center. The engine walks one chain
along the spectrum, which is right for a crossover chain and wrong for a car (on the reference installation it
produced a front midrange "handing over" to a rear fill at its low-pass corner). Subwoofers belong to the front
chain: that is where their junctions are (on the reference car the two subs, below 50 Hz and 50–110 Hz, cross each
other and the lower crosses the midbass) and how they are tuned by hand; Sub is a display and tuning-sheet zone.
Only the front chain searches junctions. The rear pair is settled between itself and then placed as a group; the
centre is placed between the two front sides (see Group placement). Placements are computed against a settled
front, so later stages cannot pull an earlier one out of tune except by sliding it as a rigid body.
`NeedsStaging` is false when every block is in the front chain; such projects take the single-stage path, which is
the old code, so the session battery is unchanged by construction.

### Auto delay run and report

`AlignmentReprocessor` crops every channel's measured IR to one shared direct-sound window (a common offset keeps
inter-channel timing) and reprocesses through the engine's delay/polarity overrides, caching per channel by
cropped source, rate and chain value, since only one or two channels move per junction step. Inputs, including
the processing rate and base chain, are captured before the run because the user can edit the panel while the
engine's background probes read them. The crop is sized by time: after the 1/8 pre-peak reserve it must hold the
longest band-sized alignment window (`VirtualCrossoverAnalysis.MaximumAlignmentGateMs`) plus the channels'
arrival spread (fleet worst ~46 ms, 175 ms reserved): 8/7 · 525 = 600 ms. At 48/96 kHz the base 65,536 samples
already exceeds that and is kept exactly so archived results do not move; higher rates double it (192 kHz →
131,072, 384 kHz → 262,144, where the fixed length left 149 ms, less than one sub-band window).

`AutoDelayRunRequest.RearFillOffsetMs` is how far behind the front the rear fill should arrive. Zero sums the two
coherently, which a second row of listeners wants; `VirtualCrossoverAutoDelayDialog.DefaultRearFillOffsetMs`
(15 ms) is the middle of the 10–20 ms precedence range where the ear stops localizing the rear speakers and hears
them as room, keeping the image on the dash. The report prints the offset whenever there is a rear, zero included,
since a saved proposal otherwise cannot say whether the rear was co-arrived or held back.

The dialog only computes a proposal; nothing is written until Apply, and any input change invalidates the proposal
so Apply always writes what the report shows. It cannot close mid-run because the runner reads live channel
configuration that only modality keeps stable. `VirtualCrossoverAutoDelayReport` is UI-free text shaping: invariant
culture (a shareable diagnostic), "value (kept)" instead of "a -> a" so real changes stand out, a value counts as
changed only when its printed number differs (summary list and arrows always agree), and the sum-loss improvement is
computed from the rounded printed figures. The headline figure is the predicted average summation loss per side:
how far the coherent sum falls short of the phase-blind magnitude sum over the crossover window.

## Group views

`VirtualCrossoverGroupViews` decides what the plot, the sum, the loss curve and the metric read-out describe for a
`VirtualCrossoverGroupView`. Drawn together, a car with front 3-way, rear pair and centre is seven traces over
290 Hz upward whose sum describes no seat; each view is a subset that is one coherent question (Front+Sub,
Rear+Sub, Front+Center, Groups compared, Everything).

- A **centre never enters a sum**. It plays content synthesised from L and R, so how much of the music reaches it
  is a property of the programme, not the tune; adding its path to the front's would assert a signal split no
  measurement can know. It is drawn to be compared and judged by arrival and level.
- **Summation loss** is reported only for a view on one crossover chain (`LossChainZone`). Loss measures
  cancellation at a crossover, where the delay that removes the dip is the tune. Front against rear play the same
  band from opposite ends of the cabin with no filter between them, so their sum combs however well each is tuned;
  quoting it would report damage nothing can repair. Cross-group views report arrival and level differences against
  the front instead (`ComparedAgainstFront`). Front+Center reports the front chain's loss but without the subs,
  because that view does not draw them.
- **Groups compared** draws one summed line per group and no per-driver curves: it is for setting the rear offset
  and level against the front.

## Time Alignment panel

`TimeAlignmentPanelController` reads the delay between a Main and an optional Compare record in a band (Full band
bypass, Auto dominant band, or manual).

- **Background reads.** One read of a megabyte transfer IR takes a few hundred milliseconds, so reads run off the
  UI thread. `AnalysisReadSchedule` makes the newest request authoritative: a request freezes the records and every
  setting, equal requests share one answer (the panel refreshes from more paths than there are changes), a
  superseded read is never drawn, and a running read cannot be cancelled, so the desired one starts when it lands.
  Per-record derivations (real projection of the transfer IR, crosstalk verdict and cleaned copy) are cached so band
  edits do not repeat them.
- **Hygiene order.** Crosstalk detection runs on the raw record; banded modes analyze the cleaned record — the
  engine's order. A broadband click lands inside the analysis band and the upper-half probe alike, so a raw read
  could "verify" an arrival that times the click. The bypass mode keeps the raw record and flags it. The v3 field
  failure: an electrical copy of the playback at a fixed early sample of every record, which the full-band first
  arrival timed instead of the sound.
- **Shared band.** With both records present the Auto band is the overlap of their dominant bands: two drivers
  are only comparable where both play, and an overlap is symmetric. Taking Main's band alone made the delta depend
  on which record was Main (a field mid pair: 32.7–7671 Hz one way, 75.5–4695 Hz the other, 0.3 ms of delta).
  Too little overlap (sub vs tweeter) keeps Main's band and the label stops saying "shared"; a Compare detection
  failure falls back the same way while Main's failure aborts the read.
- **Recommended row.** The table marks the first arrival as the alignment figure unless it is disqualified
  (`IsArrivalRecommendable`: modal latch, SNR below `AutoAlignmentEngine.MinimumArrivalSnrDb`, or a full-band read
  over detected crosstalk). Never the strongest peak (a later stronger peak is a mode or reflection). Never the
  energy onset either, although the engine's cross-side links read it below 300 Hz: an onset's distance from the
  front depends on the response's tail and chain (on the field midbass pair the same two fronts read 2.2 ms apart
  through their chains and 1.1 ms raw). Between two sides of one driver pair that bias cancels, which is the only
  case the engine uses; this panel cannot know what two records are, so the onset row is shown but not recommended.
- **Modal latch.** With a bandpass active, the full-band first arrival is re-checked against the band's upper
  half; a full-band read far later than its upper half times a room mode's build-up. The upper-half figure is
  diagnostic only (in the engine's field case an upper-half read walked a woofer 6 ms off).
- **Quality figures.** SNR (strongest envelope peak vs the rest) grades the recording; first-arrival prominence
  grades how sharp the pick is. They are kept apart because a woofer's broad leading edge gives low prominence on an
  excellent recording. Below the engine's SNR floor (independent noise records read ~8 dB) figures are shown as
  not-evidence and no confidence percentage is printed.
- **Envelope plot.** Both curves are normalized to Main's strongest peak. Normalizing each by its own first-arrival
  level made the axis mean different things per curve: picks 6 dB and 25 dB under their peaks put equal levels
  19 dB apart on screen. Each curve's floor rides 80 dB under its own maximum so a genuinely quieter Compare record
  is drawn whole, and decimation pools min/max per bucket so narrow reflection peaks are not skipped.
- **Delay table.** Each column holds the widest cell with a Compare delta (16 characters, e.g. "163.000 (+2.604)") plus
  one space, so a full row is 66 characters, what the status box shows at the table font without wrapping. The recommended
  marker goes at the end of the row, because a glyph of uncertain width ahead of the cells shifts the columns.
- **Imported recordings** have no absolute time (nothing ties the recorder start to playback), so the panel refuses
  them rather than show meaningless delays.
