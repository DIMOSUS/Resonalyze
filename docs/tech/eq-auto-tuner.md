# EQ auto-tuner and EQ Wizard

The EQ Wizard corrects a measured (or imported) response towards a target curve with a bank of
parametric bands plus a preamp. Bands are placed by hand in slot strips or fitted automatically.

Where the code lives:

- `dsp/EqAutoTuner.cs` — the entry point (`EqAutoTuner.Tune`, `EqAutoTuner.Options`, `EqAutoTuneBoosts`): grid,
  preamp and boost mask.
- `dsp/EqBandFitter.cs` — the fit itself: the objective (`EqFitTuning`), insertion, joint refinement, pruning, shelves.
- `dsp/EqBoostabilityMask.cs` — per-frequency decision whether a boost may be placed.
- `dsp/EqualizationCurve.cs`, `dsp/PeqBiquad.cs`, `dsp/BiquadResponse.cs`, `dsp/PeakingBiquad.cs` — the
  band model and its digital (RBJ biquad) response.
- `dsp/PeqQConvention.cs`, `dsp/PeqTextFile.cs`, `dsp/EqProfileFormats.cs`, `dsp/EqualizerApoFormat.cs` —
  Q conventions and profile import/export.
- `source/Tools/Eq/` — the wizard: its session and readers, the panel that binds them, curve sources
  (`EqWizardCurveSource`, `EqWizardSourceResolver`), slot strips and headless auto-tune (`EqAutoTuneHeadless`).
  See [Code map](#code-map).

## Code map

| Area | Code |
| --- | --- |
| The wizard's state: source and calibration, smoothing, target and its level, processor, Auto Tune settings, view toggles, phase gate, Virtual DSP handoff, persisted settings | `EqWizardSession` |
| The bank and its undo history, one step per edit or structural change | `EqWizardBank` |
| Every field's range and decimals; a band as its strip holds it | `EqWizardLimits`, `NumericFieldRange` |
| Calibration options and the choice a source opens with | `EqWizardCalibration` |
| The source curve through its calibration and smoothing; a gated source's preview request | `EqWizardSourceCurve` |
| Target, Source + EQ, statistics, the bank's gain and phase curves, hints, the source's axis | `EqWizardRender` |
| What Auto Tune is given; kept all-pass bands | `EqWizardFit` |
| The phase context a source opens with, a gate edited in the dialog, one render's request | `EqWizardPhase` |
| Gated magnitude and measured phase rendered off the UI thread, keyed by bank | `EqWizardPreviews` |
| The plot model: axes, window marks, curves, deviation shading, the selected band | `EqWizardPlot` |
| Controls, strips, menus, dialogs, the Auto Tune run; binding only | `EqWizardPanel` partials |

The session is UI-free and is what the tests build. The panel writes it from its controls and writes the controls back
from it without raising their handlers; every redraw reads the session through the readers. The session raises
`SettingsChanged` itself, after a change the settings file keeps, and not while settings are being restored, so the
host saves what the session holds. `EqWizardPanelWiringTests` drive a shown panel through its fields.

Every value is held as its field shows it, so the bank that the fit, the plot and an export read is the one on
screen. A value written by code (a restored file, an Auto Tune result, an import, a handoff's seed) lands as
`ThemedNumericUpDownExtensions.ClampValue` puts it in a field: clamped, then rounded to the field's decimals half to
even. A value typed into a field arrives already rounded, half away from zero, by the field itself. Narrowing Max
Boost or Max Cut clamps the bands' gains without rounding, as a pending edit that lands as its own undo step.

## Fit

`EqAutoTuner.Tune` resamples source, target and (optionally) coherence onto a 256-point logarithmic grid over
the window. The error is defined only where both source and target have data; resampling without end clamping
yields NaN outside the measured range and those points are excluded. The preamp absorbs the broadband level
difference (see [Preamp alignment](#preamp-alignment)); `EqBandFitter` fits the shape:

1. **Insert.** Take the grid point with the largest error a band could remove — see
   [The objective](#the-objective) for which errors count in which mode — and seed a bell there: its gain is that
   error (a boost clamped to what Max Gain leaves), its Q the width of the error's lobe between half-height points,
   its frequency free to drift a third of an octave. Refine that band alone. It stays only if it lowers the
   objective by at least `SlotWorth`; otherwise its lobe is blocked for the rest of the pass.
2. **Refine together.** After every kept band, frequency, gain and Q of the whole bank are refined jointly: bounded
   Levenberg–Marquardt on (ln f, gain, ln Q), the Jacobian by forward differences of the digital response.
3. **Prune.** The band that costs least to lose is removed and the rest refitted; the removal stands when the
   objective rises by less than `SlotWorth`. A band whose bare removal already costs six slots is not tried.
4. **Repeat.** Refinement moves bands, so a place refused a band in one pass may earn one after it: the blocks are
   cleared and insertion runs again, up to four passes, while a pass still adds or drops a band. A pruned band leaves
   a free slot and a changed residual, which is as good a reason to look again as an insertion — though no fixture
   found reaches that case: pruning fires in the pass that added the bands, and by the next pass the bank has
   converged (8000 fits over the corpus and over random sources never took the branch, so `AnotherPass` is pinned by
   a unit test rather than by a fit).
5. **Round** to what the strips hold (1 Hz, 0.1 dB, Q 0.1), then enforce the ceilings the objective held softly by
   trimming, in 0.1 dB steps, the boost doing most of any excess — see
   [Ceilings on the finished bank](#ceilings-on-the-finished-bank).

### Ceilings on the finished bank

A ceiling is a promise about the bank, not about the bins it was fitted on. The soft constraints in the objective and
the trimming pass both read the 256-point fitting grid, where a narrow band peaks between samples: at Q 20, the
narrowest a strip allows, a band is about 0.07 octave wide against a grid step of 0.04, so a refill and its cut can
read as summing to zero on the samples and still lift between them (measured: 0.36 dB on a synthetic fixture of
off-bin peaks). The skirts also run through bins the source never covered, which the fit treats as contributing
nothing.

So after rounding, the ceilings are enforced twice: on the fitting grid, where the boost mask lines up exactly, and
again on a grid of 4096 logarithmic points from half the window's low edge (or 10 Hz) to just under Nyquist, dense
enough to give the narrowest allowed band tens of samples. Which points a ceiling answers for differs by what it
promises:

- **A bank that may not lift** (`Off`, `RefillOwnCuts`) promises clip safety, so its sum is held at or below 0 dB
  over the whole dense range, measured bins or not.
- **The stacking limit on boosts** (`Allowed`) is held where the source was measured. Held everywhere instead, a boost
  at the edge of the window was trimmed to nothing for what its skirt does in an unmeasured octave above it, which
  cost 0.10 dB RMS on the corpus for a constraint about frequencies nobody asked the bank to correct.
- **`TotalGainMaxDb`** is a headroom figure, so the bank's peak for it is read over the whole dense range.

The dense pass costs about 2 ms per fit. On the corpus it moves the result only where the promise was being broken:
refilling, RMS 1.381 → 1.386 dB and dug 0.91 → 0.93 dB; with boosts, nothing measurable.

Both ends of a band are bounded: gain by Min/Max Gain with the sign it was seeded with (a boost never turns into a
cut), Q by `[max(QMin, 0.5), QMax]` — a bell wider than Q 0.5 is a tilt, which is a shelf's or the preamp's job.
Candidate responses are evaluated at pre-computed unit-circle points with the arithmetic of
`DigitalEqualizationResponse.MagnitudeDbAt`, so the fit sees what the DSP realises at `SampleRateHz`. A fit takes
under 10 ms on a typical channel and at most about 80 ms on the widest windows measured.

All-pass bands are never fitted: an all-pass is flat, so a magnitude error never asks for one. Callers replace the
whole bank with the result, so hand-dialled bands (shelves included) do not survive a re-fit.

### What it replaced

The fitter before this one set each band's gain to the full error at the worst point and only then chose Q from a
fixed ladder (0.5, 0.7, 1.0, 1.4, 2.0, 2.8, 4.0, 5.6, 8.0, 10.0). At full depth a wider band digs the neighbours of
the local extremum it was aimed at and loses, so almost every band landed on the highest step under Max Q — 5.6
under the default 6, the value users read as a bug — and nothing placed moved again, so the ripple one band's skirt
left became the next band's target. Measured on the fits the wizard's own handoff makes from five Virtual DSP
sessions (moving-mic averages and gated responses, every channel and side, the wizard's defaults), 36 per mode once
the cases the level check would question are left out; the columns are means over the fitting window:

| mode | bands | RMS error | RMS above target | dug below target | bands at Max Q |
| --- | --- | --- | --- | --- | --- |
| Refill cuts | 5.5 → 5.0 | 1.75 → 1.42 dB | 0.07 → 0.07 dB | 2.05 → 0.94 dB | 3.9 → 2.0 |
| Allowed | 11.4 → 6.1 | 0.72 → 0.54 dB | 0.29 → 0.22 dB | 0.83 → 0.66 dB | 7.6 → 3.2 |
| Off | 5.5 → 3.9 | 1.75 → 1.57 dB | 0.07 → 0.14 dB | 2.05 → 1.51 dB | 3.9 → 1.5 |

"Refill cuts" compares against the old fitter's cuts only, which it replaces as the wizard's default. Read back
through the wizard's own render (the bank inside the chain, then the display smoothing), the moving-mic averages keep
the gain — RMS 1.69 → 1.42 dB refilling, 1.04 → 0.95 dB with boosts. A gated source is filtered before it is
windowed (see [Gated sources](#gated-sources)), a gap the fit does not model for old and new alike: there the new
fit's narrower refills show as up to half a decibel more above the target (RMS above 0.05 → 0.12 dB).

### The crossover in the target

A channel handed over from Virtual DSP is measured THROUGH its chain, so its curve already carries the crossover's
skirts. The wizard therefore used to fit inside the passband only: `VirtualDspEqHandoff.PassbandFor` sets From/To to
the corners the channel is cut at — the narrower of the IIR crossover's and a designed FIR's, since a channel running
both stages is filtered by both — and the slopes were left alone as the filter's doing.

`EqTargetCrossover` is the other reading, the one current REW guides tune by: the goal for the channel is the target
curve INSIDE the passband and the crossover's own slope outside it, so the fit can bring the ACOUSTIC roll-off onto
the filter the tune defines — which is what the neighbour's slope has to sum with. `EqWizardSession.CrossoverInTarget`
(the **Crossover in target** box, on by default for a chain handoff) adds `20·log10|crossover|` to the target
everywhere it is sampled, so the plot, the fit, the statistics and the level check all read the same goal.
An imported target that is itself cut with the crossover gets the slope twice; `EqDoubleSkirtCheck` warns in the results
panel when the imported curve falls 15 dB or more from each skirt's −3 dB point to an octave past its −18 dB point, and
never over less than two octaves. The −3 → −18 dB span alone is too narrow for a windowed-sinc FIR skirt (a tenth of an
octave, over which a cut in the file barely moves) and too easily offset for LR12, whose span a bass rise in the file
can eat a third of; past it a skirt keeps falling while a shelf levels off, and no preset shelf spans more than 12 dB.
Measured: the ATF target of #218 cut with LR12 falls 18 dB (low skirt) and 33 (high), with LR24 33 and 42; Car Bass under
an LR12 sub 10.6, under LR24 9.0. `EqDoubleSkirtCheck.Cache` holds the
answer between redraws: a FIR skirt costs a pass over the kernel per frequency.

- **The shape** (`EqTargetSlope`) is a designed FIR crossover's own KERNEL where there is one, and otherwise the
  channel's `VirtualCrossoverChannelSettings.EffectiveCrossover` — the same corners the window comes from, so From/To
  and the shaped target describe one filter. Both travel on the handoff (`EqWizardCurveSource.TargetCrossover` and
  `TargetCrossoverFir`), because the built chain describes neither: a FIR crossover leaves `DspChannelChain.Crossover`
  off and carries the filter as taps. The kernel is read rather than the design's corners because a `WindowedSinc`
  design's slope is its window and length — its `Family` and `SlopeDbPerOctave` are carried but unused, so a brick
  wall would be drawn as the LR24 the corners claim. `FirDesign != null` is what tells a crossover kernel from a
  correction FIR, whose magnitude belongs in the source and not in the goal (folded into the target it would simply
  be cancelled by the fit). A channel may legitimately run both stages, and then the shape is their product, as the
  chain applies them — reading one and dropping the other left the other's skirt unshaped and unprotected. The shape
  is clamped to 0 dB above and has no floor: a floor drew a false shelf, and a window widened by hand past it had the
  fit pin the response onto that shelf. A zero of the response is minus infinity, which the fit skips. A narrow passband's two skirts overlap, so the middle of the band sits a few tenths below 0 — the measured
  curve through the same chain carries that droop too, so target and source still agree there.
- **The window** widens to where each skirt has fallen `SlopeWindowFallDb` (18 dB), bounded by the measured band: far
  enough to score the slope that matters for summation, not so far that the fit chases a filter into the floor. It
  moves only while it still stands where the handoff or the box last put it — a typed edge is the user's and is left
  alone, in either direction. A crossover outside the band that was actually measured (a low-pass at 500 Hz on a
  record that starts at 1 kHz) leaves nothing to widen into, and the clamps would cross: the passband is returned
  instead — clipped to what the record does cover while that still leaves a window — since both callers need one they
  can use: the fields quietly reorder an inverted window, a headless fit hands it to `EqAutoTuner`, which refuses it.
  Where even the passband holds no measured point, the fit itself refuses (`EqWizardFit.NoMeasuredDataRefusal`, and
  `EqAutoTuneHeadless.NoMeasuredDataRefusal` for the import path): the tuner answers an empty window with an empty
  bank, and Auto Tune applies what it returns, so the refusal is what keeps a channel's existing bank from being
  replaced by nothing. The headless refusal is a question the caller asks — one channel outside its measured band
  skips with a line in the import's summary instead of stopping the channels after it — and `Fit` throws on it as the
  backstop for a caller that does not ask.
- **No boost is aimed down the skirts** (`Options.NoBoostBands`, from `NoBoostFallDb` = 6 dB of fall outwards): there
  the target's fall IS the filter, so a boost would fight the crossover, and with boosts Allowed an 18 dB deficit at
  the window edge would otherwise pull bands to Max Gain. What a boost aimed elsewhere spills in through its own skirt
  is bounded by `ForbiddenRegionMaxBoostDb` (0.5 dB), exactly as in a bin the reliability mask closed — refusing every
  last tenth would refuse legitimate boosts beside the corner for their skirts. Cuts stay allowed, which is the
  direction that does the work: a driver whose acoustic slope is shallower than the target gets cut onto it. A window
  that is skirt from edge to edge — a band-pass crossed past itself — is refused throughout rather than left
  unprotected.

The gain is one-sided by nature. Where the measured slope is steeper than the target's — the driver's own roll-off on
top of the filter — a bank that may not lift leaves it alone, and only the statistics notice. Where the driver has
more output than the filter's slope asks for, the fit now brings it down instead of stopping at the corner.

**An acoustic crossover stated on the card.** Where the card states an ACOUSTIC crossover (a junction tune writes the
one it was asked for, or the card editor sets it), the handoff sends that as `TargetCrossover`, per edge
(`VirtualDspEqHandoff.GoalCrossoverFor`: the family and slope asked, at the
electrical corner), so the fit aims at driver and filter together rather than at the filter alone. The filter the
chain runs then travels beside it as `EqWizardCurveSource.ElectricalCrossover` — only where the two differ — and the
wizard draws the target on it too (`EqWizardRender.ElectricalTargetCurve`): dotted, at half the target's opacity,
under the target, and read by nothing else — not the fit, the statistics or the level check. The two skirts side by
side show how much of the slope the driver is expected to supply. The measured verdict:
`docs/tech/crossover-auto-setup.md#measured-on-the-battery`.

### The objective

What the fit minimises is an integral over log frequency (dB²·octave): a per-point loss, soft constraints, and a
price per band. `EqFitTuning` holds the numbers.

- **A deficit the fit fills** (boosts Allowed and the [boost mask](#boost-reliability) trusts the point): the plain
  squared error.
- **Anywhere else** only what the bank itself does is charged: `AboveWeight` (4) × the square of what stands above
  the target, plus the square of what the bank's own cut dug below it — the full depth when the point already sat
  below, the overshoot when it sat above — with `OverCutWeight` (5) more on digging past `OverCutFreeDb` (1 dB).
- **The dug charge falls with depth**: it is scaled by `1 / (1 + (D / 3 dB)²)`, D how far under the target the point
  already was. Digging a point 10 dB under changes nothing anyone sees; digging one at the target shows a dent. A
  cut on a peak beside a crossover slope was otherwise refused because its skirt reached down the slope.
- **Constraints** at 400 per dB²: when refilling, the bank's sum at most 0 dB; with boosts, the boosting bells' sum at
  most Max Gain and at most `ForbiddenRegionMaxBoostDb` in a masked bin.
- **Overlap**: 0.1 × the square of what a boost and a cut take from each other at a point (min of the two). A pair
  cancelling over an octave is two slots spent on their difference; a skirt brushing an opposite band costs little.
  Refilling is overlap by design, so there the weight is 0.01 (see [Boost modes](#boost-modes)).
- **`SlotWorth`** (0.03 dB²·oct) is what a band must remove to earn its slot, at insertion and at pruning alike, and
  what the shelf stage charges per band. A 1 dB bump a sixth of an octave wide is worth about 0.1.

Never charge the depth a point already had: a real response weaves below the target everywhere, and a penalty that
grows with pre-existing depth makes every wide band lose to the narrowest — it once shredded a smooth lobe into a
comb of Q 10 slivers. The old fitter's plain squared error had the same bias in milder form, which is part of why
its bands sat at the top of the ladder.

The weights were read off the 144 fits, varying one at a time:

- `AboveWeight` 1 → 4 moved RMS above target 0.24 → 0.14 dB and dug depth 1.27 → 1.51 dB in cuts only: the objective
  alone, symmetric, left 1–2 dB bumps on peaks next to dips; the old fitter left none at the price of 2 dB gouges.
- `OverCutWeight` barely matters once the dug charge exists (25 → 0 moved above-target RMS 0.31 → 0.24 dB); 5 keeps a
  gouge dearer than a bump.
- `SlotWorth` 0.02 / 0.03 / 0.05 gave 6.6 / 6.1 / 5.4 bands with boosts, RMS 0.51 / 0.54 / 0.58 dB, and 5.2 / 5.0 / 4.8
  refilling; below 0.03 ripple under a decibel starts earning bands.
- `StopResidualDb` 0.5 against 1.0 dB: 5.0 against 4.7 bands refilling, RMS above target 0.07 against 0.10 dB; 6.1
  against 5.4 bands with boosts, RMS 0.54 against 0.61 dB.

### Boost modes

`Options.Boosts` says what the bank may do above 0 dB. The library defaults to `Allowed`; the EQ Wizard to
`RefillOwnCuts`.

- **`Off`**: every band cuts. The preamp aligns to the least-excess point (see [Preamp alignment](#preamp-alignment)).
- **`RefillOwnCuts`**: bands may boost, but only to put back what the cuts dug; the bank's summed response stays at or
  below 0 dB everywhere, so the curve is never lifted and the profile is clip-safe exactly as a cuts-only one. The
  preamp behaves as with `Off`. A broad resonance with steep sides is the case it exists for: a cut wide enough for
  its top digs its sides, a cut narrow enough to spare them combs the top, and a boost on each side of one wide cut
  gives both (on the BMW F30 midbass average, five bands put 80–190 Hz on the target where the old cuts, also five,
  left a 3 dB gouge at 130 Hz). A refill is seeded only where the bank dug past `OverCutFreeDb`: seeded from any dent,
  a 6 dB resonance came back as a −15 dB wide cut between two +6 dB boosts that sculpted its skirts — flat, and absurd
  in the strips; with the floor it is one band. The overlap charge (0.01) keeps boosts from reshaping a cut's own
  centre; ten times that narrowed refills to Max Q and cost depth (dug 0.82 → 0.99 dB when it was tried).
- **`Allowed`**: bands boost and fill deficits the [boost mask](#boost-reliability) trusts. Boosting bells together
  stay under Max Gain, measured on the boosts alone: with the net sum of the bells, a cut made room for boosts stacked
  over it (+10.6 dB at 70 Hz on a 6 dB Max Gain, two bands at one frequency), and a cut in a null's core let a boost
  fill the null's wall — three bands netting +0.36 dB on a synthetic null. The preamp is the median error when
  unpinned.

Rejected, measured:

- REW's rule of no boosts beyond the first and last points where the response reaches the target. It removes
  boosts up a crossover slope, but equally refuses the bass shelf a car target asks for (RMS 0.54 → 1.34 dB with boosts).
- A Q ceiling for boosts alone (3 or 4): with boosts RMS 0.54 → 0.58–0.62 dB; refills need narrow bands between two
  cuts, and dug depth rose 0.82 → 1.01–1.08 dB.

## Preamp alignment

The right broadband level depends on which way bands can move the source:

- **Boosts allowed:** bands correct both ways, so the preamp centres the residual on the MEDIAN error. A null or a
  roll-off is shape the bands answer; a mean carried it into the level and the fit then cut everything by a dB.
- **Off or refilling:** the bank can only pull the source down. A preamp below the largest error would drop a point
  beneath the target where no band can lift it back. So the preamp aligns to the point where the source is LEAST
  above the target (the maximum error), absorbing only the excess every point shares, and stays at the ceiling
  whenever any point is already at or below target. Rounding goes up, so every point stays cuttable.

The ceiling is pre-applied here, not only in the post-band clamp, so bands fit against the same level the curve is
finally realised at. When the bank may not lift, its peak is 0, so the ceiling is 0: such a fit must never lift the
curve whatever the level difference or an unbounded `TotalGainMaxDb`.

### Total gain ceiling

`TotalGainMaxDb` caps preamp + summed band gain at every frequency. A positive preamp stacked under boost bands is a
clipping DSP profile, and handing one out leaves the UI reporting the damage as negative headroom. The cap is applied
to the preamp AFTER bands are placed, so the fitted shape stays and the curve honestly sits below an unreachable
target. It is unbounded by default because, as a pure curve fit, the preamp legitimately carries the level difference
between arbitrarily referenced source and target; a caller producing a clip-safe profile passes 0. The bank's peak is
read with a 10⁻⁶ dB tolerance, since the cap floors to whole dB and rounding noise on a bank that never lifts would
otherwise cost the preamp a decibel. With boosts allowed, prefer pinning the preamp (`PreampMinDb == PreampMaxDb`):
under the post-hoc cap a boosting fit is realised below the level its bands were placed against — the whole curve
drops by the peak boost.

## Boost reliability

With boosts `Allowed`, `EqBoostabilityMask` (options in `Options.BoostMask`) decides per frequency whether a boost
may fill the deficit there (high coherence, not inside a narrow deep null). Elsewhere a deficit is not chased: the
point is charged only for what the bank digs, like every point when boosts are off.

The mask clears where a boost is AIMED. A boost's centre stays inside the trusted run it was seeded in, and a seed
whose boost, even at the narrowest Q, would pour more than `ForbiddenRegionMaxBoostDb` (0.5 dB) into a masked bin is
a null's wall, not a dip: only a tenth of an octave around it is blocked, since further out on the same dip a
narrower boost may fit. A wide correctable dip with a narrow null at its floor thus keeps its reliable shoulders;
only the null is left. The boosting bells' sum in a masked bin stays under the same 0.5 dB, which also stops a wide
boost aimed at a reliable centre from quietly filling the null beside it through its skirt. +infinity restores
unguarded behaviour. Cuts are never checked (a cut cannot fill a null).

A missing coherence curve means every point is treated as reliable (null-detection and the fitting band still gate
boosts); a frequency outside the coherence curve holds the nearest value.

## Shelves

With `Options.AllowShelves` (off by default, so callers get the bells-only curve), the fit may start from one low and
one high shelf.

### Why shelves

The bell is right for a resonance and wrong for a trend. A car target is bass-lifted and tilted down, which leaves
whole octaves at one end of the residual off-target; a stack of bells approximates that badly, spending three or four
slots on a trend and leaving skirts ringing between centres. One shelf replaces them.

### Decision on the finished fit

`ChooseShelves` runs one round per direction. Each round takes every candidate of every direction not yet placed
through a finished fit on scratch state — one insertion pass and the joint refinement, the shelf refined with the bells
— and keeps the candidate whose fit ends cheapest, counting `SlotWorth` per band, provided it beats finishing with no
shelf. The same comparison ranks candidates and refuses all of them, so a shelf that neither shortens the fit nor buys
a slot's worth of error is not placed. The second round runs against the first round's shelf, so a bass and a treble
shelf can describe one tilt together. The chosen shelves then enter the full fit like any band: refined with the
bells, and dropped by pruning if the bells make them redundant.

Candidates are one per octave of the corners a shelf may take in each direction — rounded UP, since a corner drifts
half an octave and seeds further apart than an octave would leave corners in between that no candidate can reach —
each seeded with the mean error over its plateau (only what stands above the target where the mode does not chase a deficit), knee 0.5, and a corner free
to move half an octave while its plateau stays usable. The joint refinement finds the corner a finer ladder would
have enumerated.

Cheaper rules were tried before and all failed the same way: an absolute improvement bar, beating the bell the shelf
displaces, and ranking by the single-band score and finishing only the winner. A shelf acts across octaves and
changes what every later band has to do, which no single-band figure sees; on a tilted response with a loud
resonance the best-reading candidate was a wide shelf shaving the resonance's skirt that finished WORSE than none,
while the winning shelf sat two and a half octaves away.

### Shelf geometry and thresholds

Every number is about "is there really a trend at that end of the range" — a wrong shelf is wrong across octaves, so
none may be decided by a single point.

- **Knee** within 0.3–0.7, in the one decimal the strips keep. Capped at 0.7 by construction, not by the caller's
  QMax: above 1/sqrt(2) an RBJ shelf overshoots its own gain, and overshoot on a CUT is a boost — which a bank that
  may not lift promises never to produce and the mask may have refused. Measured over the whole corner and gain
  ladder at 48 kHz, the most a cutting shelf lifts anywhere: 0.0000 dB at Q 0.70, 0.0002 at 0.71, 0.15 at 0.8, 0.90 at
  1.0, 2.69 at 1.4. QMax bounds how narrow a bell may be and is not applied to knees; QMin is.
- **Quiet-side margin** 2 octaves of fitting range must lie on the shelf's UNAFFECTED side (below a high shelf, above a
  low one). This separates a shelf from a level change: a high shelf an octave off the bottom lifts practically
  everything — a preamp wearing a filter slot.
- **Plateau span** 1 octave must lie on the side it acts on, so there is a plateau to decide about.
- **Plateau start** 0.5 octave out from the corner. There a shelf has reached 57% of its gain at the widest knee and
  78% at the narrowest, so the span beyond is what it decides about, not its transition.
- **Usable plateau** 0.75 of the plateau must carry usable data — measured points for a cut, boost-allowed points for
  a boost — with a floor of two points. At three fifths, the fit answered a mask refusing to lift an incoherent top by
  lifting nearly the whole range through a shelf whose plateau cleared the bar by a single point.
- **Minimum gain** 0.5 dB; below that a shelf is not worth a slot. A boosting shelf is only tried with boosts Allowed.

### Shelves and the boost guards

A BOOSTING shelf is treated differently from a boosting bell. The skirt guard stops a band aimed at a reliable centre
from pouring gain into a null; a shelf has no skirt in that sense — its plateau IS the correction and necessarily
passes over whatever nulls that end holds — so the per-bin guard would refuse every boosting shelf. A shelf is instead
gated on being justified (usable plateau above) and its gain is still bounded by Max Gain.

Both boost ceilings read the boosting BELLS, because both exist to stop bells piling on each other and a shelf is not
a pile. Charging bells for a shelf locks them out of the region it covers: measured, a +6 dB bass shelf left zero
headroom across the bass, every resonance under it was refused, and the finished fit was worse than no shelf.
Consequently a fit with a shelf can boost past Max Gain in total; bounding that is the caller's job via
`TotalGainMaxDb`, and the EQ Wizard reports it as headroom.

## Wizard sources

The wizard owns its source and target and never reaches into the overlay UI or the current measurement.
Importing a curve (overlay slot, history entry, text file) is a snapshot with no link back to where it came from.

### Calibration choice

`EqWizardCalibration.Choose` decides the correction a freshly loaded source starts on:

- a curve that stored its own correction defaults to reproducing it (Own);
- an impulse response restores the user's standing configured preference, regardless of what a previously
  loaded curve forced the effective choice to;
- a curve with no uncalibrated reference cannot be re-calibrated at all (Off);
- a Virtual DSP channel is pinned (`PinnedToSource`) to the correction its panel renders with, and the selector is
  disabled. A PEQ fitted under one calibration and summed under another would break the handoff's identity.
  It is pinned whenever the panel pinned ANY correction, curve or mode: pinning only on a curve dropped the
  spatial average's own correction on every channel whose IR named no calibration file (the choice fell to Off,
  Off resolved the average to Uncalibrated, and the wizard fitted a curve the panel never drew).

The effective choice (`calibrationChoice`) and the persisted IR preference (`preferredIrCalibrationId`) are kept
apart: loading a curve forces Own/Off, which must not overwrite what the user chose for impulse responses. A
configured choice made against an IR (or with nothing loaded) becomes the standing preference; one made against a
curve does not. Only that preference is persisted.

The selector always offers Off and every configured calibration; "own" exists only for a curve that stored its
correction. A correction aggregated from several microphones' files offers only Own and Off: both are exact,
while one microphone's file would apply to positions never read through it and look no different on the plot.

### Imported curve calibration

An imported curve is already a finished response (`ComputeImportedCurve`):

- When its uncalibrated reference was stored, it is re-rendered exactly as its original mode would — same
  resampler — so the mode's smoothing reproduces the on-screen reference; the chosen correction is subtracted
  after smoothing (`ResolveCurveCalibrationCorrection`: none, the one frozen at capture, or a configured profile
  re-frozen on the same output grid).
- Without that reference (a dB SPL capture) the curve's own points are the reference: the correction frozen onto
  them is taken back out, smoothing applies on their own grid, and the chosen correction goes on instead
  (`ResolvePointsCalibrationCorrection`, frozen on the curve's own points — the only frequencies it has).
- A curve that declared neither (text import, legacy slot) has nothing to undo and is drawn as stored.

### Spatial average sources

A spatial average (moving-microphone capture, or a measurement recorded with a microphone array) is one driver's
magnitude over the listening volume rather than at one mic position, which is the shape a tune should be fitted
to. When present it IS the magnitude; the impulse response beside it only feeds the phase view. A capture's "Own"
correction is the capture's own — it was taken through its own file, and reading it through the IR's calibration
would be off by the difference.

`ComputeSpatialAverageCurve` uses the same builder as the Virtual DSP plot, so the tune is fitted to the curve the
user just left:

- on the capture's own grid (its true resolution); the panel samples the same function onto its plot grid;
- the chain is realised at the channel's processor rate, since the bank goes back there;
- the set's offset (resolved over all channels) is applied last as one scalar, so the curve hangs where the plot
  had it and the Target Level travelling with a handoff means the same on both sides;
- the bank under edit is substituted INTO the chain, like the plot substitutes the channel's own PEQ, never added
  to the finished curve. The builder's last step is display smoothing, which does not commute with the bank:
  measured on a real MMM tune (13 bands, psychoacoustic width) the two orders part by up to 3.1 dB, mostly where a
  narrow band sits beside a deep one, because the psychoacoustic mean is a peak-weighted cubic mean.

### Gated sources

A Virtual DSP channel with an impulse response reads through the gate it arrived with (same `DataHelper` call,
template and offset as the DSP panel's magnitude view). An ungated impulse response instead reads through the
steady-state window `FrequencyResponseOptions.SteadyStateWindowSamples` (one definition in ms, long so a bass band's
full depth shows). The gated corrected preview is
filtered THEN windowed — a window does not commute with a filter, and at Virtual DSP gate lengths the difference
reaches several dB in the bass (`EqWizardGatedPreview`). That render is too heavy for a fader frame, so it runs
asynchronously keyed by the bank; the last landed render stays on screen meanwhile. Renders start only once the
panel's handle exists: a handoff installs its source while the wizard is still hidden, and a render landing inside
the window-creation message pump would draw into a half-created control.

Why convolve rather than add the bank's ideal magnitude to the gated curve (what an equalizer normally previews,
and what the wizard still does for its own ungated sources): the two answer different questions once the bank
rings longer than the window. With the Virtual DSP default 6 ms gate, a Q 5 band at 100 Hz reads about 4.4 dB
apart between the two, and a Q 8 band at 60 Hz about 5.4 dB; above roughly 1 kHz they agree to a few hundredths.
So `EqWizardGatedPreview.Render` runs the channel's ORIGINAL measurement (`PreviewImpulseResponse`) through one
`VirtualCrossoverAnalysis.ApplyChain` with the edited bank substituted for the chain's PEQ (`PreviewChain` carries
no PEQ), then gates it. One pass from the original: re-filtering an already processed response would pad and
wrap twice. Both the bare curve (`Bank` null) and the corrected one come from here, so they cannot drift, and
both stop where the measured band stops. The gate anchor is the one resolved at handoff, never re-read per
render, or the curve would slide under its own correction.

The same applies to the fit: when all-pass bands are KEPT through a gated source, `EqWizardFit.FitSource` (and
`EqAutoTuneHeadless.Prepare`) renders the source with them applied, because through a window an all-pass is not
flat — "tune the remaining slots around them" has to mean around what they do. On an ungated curve or a spatial
average an all-pass changes nothing and the source is used as is.

### Re-smoothing imported curves

`EqWizardCurveSource.SupportsSmoothing` is true where an unsmoothed reference exists (an impulse response, a stored
raw spectrum) or where the curve was captured unsmoothed (`CapturedSmoothingCode` 0) and so IS one. Smoothing an
already smoothed curve compounds it, so a curve captured smoothed — or one that never said (text import, legacy
slot) — is left alone.

A no-raw curve must additionally be an RTA (`AnalysisCurveKind.InputSpectrum`). Re-smoothing must REPRODUCE the
analyzer, not look similar, because the result feeds Auto Tune: a mean in the wrong domain or over the wrong window
suppresses narrow peaks differently, and the fit would chase a shape the measurement never had at that width. Only
the RTA's smoothing is a replayable second pass over its stored band levels (`DataHelper.SmoothBandLevels`, which
shares its core with the RTA resampler). A dB SPL sweep smooths linear amplitude inside its Lanczos resampling,
which cannot be replayed from the finished curve; it needs its raw spectrum and is otherwise unsmoothable.

`EqWizardImportedCurve.Render` handles no-raw curves on their OWN frequencies (no resampling, so the range is never
extrapolated past the bands the analyzer resolved), in the order of the primary FR path: remove the captured
correction (these modes apply it additively per frequency, so removal is exact), smooth, apply the chosen
correction. NaN levels stay NaN through every step. A per-point correction whose length does not match the points
is not aligned to them and is ignored; `EqWizardSourceResolver.NormalizePoints` sorts, de-duplicates and drops
non-finite frequencies together with that companion array so the two cannot shift against each other.

`SupportsCalibration` is true for an IR, or for a curve that carries its own correction (raw form, or the per-point
correction of a no-raw additive mode). Without either the correction is fused into the numbers and another would
double it. A Virtual DSP channel gets its pinned correction applied, but its selector stays disabled.

### Spatial average import

`EqWizardSourceResolver.CreateFromSpatialAverage` maps a stored capture onto the imported-curve path. The mapping is
deliberately complete, because everything the wizard may do to an imported curve is decided from these fields:

- the calibration correction travels frozen on the drawn points (additive per frequency, so switching calibration
  is exact);
- the captured smoothing code comes from the recipe (captures are taken unsmoothed), which permits re-smoothing;
- the curve kind is `InputSpectrum`, so re-smoothing is the analyzer's own second pass;
- NaN levels are carried untouched (below a protective high-pass the capture has nothing to say, and the fitter must
  read "do not equalize here", not a level);
- no coherence: one microphone, no reference, so Auto Tune gates boosts on what remains rather than a fabricated one.

`TryCreateFromArray` offers a measurement's own microphone-array average in place of the one-position response. A
point measurement carries dips belonging to its own few centimetres, and an equalizer fitted to those is fitted to
a place nobody's head occupies. The wizard asks rather than substitutes: equalizing the point response with an
average unused in the same file is the mistake the array exists to prevent, but comparing the two is legitimate, and
an average has no impulse response, so the gate preview would disappear.

### Microphone array agreement

For an array source the boost confidence (`Coherence`) is the agreement between positions — for a spatial average a
better witness than γ²: an average is a claim about a listening volume, and where positions disagree wildly it is a
claim about none of them.

`ArraySpreadBoostLimitDb` = 20 dB was read off the owner's seven-position sets, not chosen. After any smoothing the
spread between positions in a car sits at a median of 11–12 dB across the whole band on both a midrange and a
tweeter, p90 near 15 dB. That is the normal state of a listening volume: a gate at 10 or 12 dB would refuse boosts
across half a midrange and nine tenths of a tweeter — a boost ban wearing a threshold's clothes. 20 dB selects the
top few per cent: bands where the average is carried by whichever position happened to be loudest, where filling the
dip the others measured helps one seat centimetre while spending every other seat's headroom. It is applied to the
UNSMOOTHED spread: a single band where positions part by 30 dB is exactly the pathology, and smoothing fills its
neighbours in until nothing on those sets exceeds 20 dB.

`BuildAgreementCurve` is two-valued (1 allow / 0 refuse). A smooth dB-to-confidence map would be invented precision,
since the mask only compares against one floor. A band where fewer than two microphones reported reads 0 whenever
the average itself is a level: there is no second opinion. Returning the spread's NaN would be worse, because the
mask treats non-finite coherence as PERMISSION — the one case the array cannot vouch for a dip would switch the gate
off. Where the average is NaN too, the fit already excludes the band.

### Index-aligned curves

Source, target, Source + EQ and the fit are read against each other BY INDEX: the target is built on the source's
frequencies, the deviation shading pairs each vertex with the one beneath it, the read-out subtracts them and the
tuner takes the error between them. That holds only while they are the same frequencies, and gaps decide it. An
unmeasured bin is masked by frequency alone (`MaskUnmeasuredBands`), so two renders of one measurement mask the
same points — provided a single conversion (`EqWizardSourceCurve.ToPlotPoints`) drops the same points from both. Two
conversions broke it: the bare curve dropped masked bins while the corrected preview kept them, and a channel swept
from 200 Hz read its target 341 grid points too high, so the shading closed in a wedge at 2 kHz instead of
following the result to 20 kHz. Whether to keep gaps belongs to the source (`KeepsGaps`): measured/stored curves
keep NaN gaps, which the fitter reads rather than bridging; a computed FR drops non-finite values as noise.

## Processor rate and Q convention

The fitted biquads are realised at the PROCESSOR's rate (`EqProcessorSampleRate`), which is independent of the
measurement: a 48 kHz sound card can measure a system driven by a 96 kHz processor, and the filters must be the ones
that processor builds (see `PreparedDspResponse`). So the rate selector answers for every source. A Virtual DSP
handoff carries the project's processor profile; the bank returns there, so rate and Q convention are shown but
locked (change them in that project's DSP processor dialog). A non-standard processor rate is added to the list so
the true rate is shown. The user's manual rate and convention are persisted separately and never overwritten by a
handoff's, which would silently retarget later exports at a device the user never chose.

The Q convention (`TargetDspQConvention`) moves the numbers on the tuning sheet ONLY. The fit, the on-screen curve
and profile exports stay in the RBJ convention the library realises, so switching it never changes the tune — only
how it must be typed into the hardware.

## Wizard preamp policy

`EqWizardFit.Options` (mirrored by `EqAutoTuneHeadless.Prepare`) sets the preamp differently per mode:

- **Boosts Off or Refill cuts:** the auto preamp may move within the control's range (±80 dB); it aligns to the
  least-excess point and can only lower the curve, and `TotalGainMaxDb` = 0 keeps the profile clip-free.
- **Boosts Allowed:** the preamp belongs to the user. Auto-centring it would put broadband gain where the Target Level
  datum belongs, and the total-gain ceiling would then "compensate" fitted boosts by dropping the preamp after the
  fit — bands placed against one level, realised far lower, the whole curve falling by the peak boost instead of the
  window rising to target. Pinning `PreampMinDb == PreampMaxDb` to the current value makes every tuner preamp
  decision a no-op: bands carry the full correction within Max Gain, and any positive total gain is the user's
  explicit choice, reported by the headroom read-out.

Other wizard-side rules:

- Max Filters budgets the whole BANK, so kept all-pass bands come off it; a reserve that fills the budget is refused
  up front rather than returning more filters than the limit promises. When a run would replace a bank holding
  all-pass bands, the user is asked whether to keep them (phase work aligned by ear). `WithAllPassBands` puts kept
  bands last; on overflow the FITTED bands give way, since the tuner can regenerate those but not a hand-aligned
  all-pass. Only then is the bank sorted by frequency (`EqWizardFit.Finish`, for the button and the headless fit
  alike): the tuner returns its bands in the order it placed them, most important first, and a tuner reading the
  bank, or a DSP's numbered filters, reads it low to high.
- Band gain is bounded by the Min/Max Gain fields, like the faders. QMin is the strips' own limit
  (`EqWizardLimits.BandQ`); QMax is the user's Max Q (default 6), well below what a hand-typed strip accepts,
  since a fit is free to place filters far sharper than a cabin measurement justifies.
- Shelves are opt-in because they change the SHAPE of the result, and Max Q says nothing about a knee.
- Boosts open on Refill cuts. A settings file from before the choice existed stored only Cuts only: ticked it opens on
  Refill cuts, which keeps the promise the box made (the curve is never lifted), unticked on Allowed. The flag is still
  written, as "not Allowed", so an older build reads the same promise back.
- Before fitting, `EqTargetLevelCheck` takes the median of target minus source over the window. More than 3 dB above
  (broadband boost; unreachable unless boosts are Allowed, the preamp being capped at 0 dB) or 10 dB below (a broadband cut that
  hands level to the amplifier gain and its noise) is a datum set wrong that the fit would follow faithfully, so the
  wizard asks first. The median ignores junction dips and modal nulls, which are shape, not level.
- Every change to a fit input funnels through a redraw that invalidates any in-flight fit; over-invalidation is safe
  (the stale result is dropped and the user re-runs).

## Phase mode

The wizard's phase view (`EqWizardPhase`, `EqWizardPreviews`, `EqWizardPhaseRender`) draws the MEASURED phase of the edited channel
through its chain and bank, against neighbouring drivers frozen at the Virtual DSP handoff. It is the only view where an
all-pass band's work is visible (on magnitude it is flat by construction), and lining a driver up with its neighbour
through the crossover is what such a band is for. It is a mode, not an extra curve: source, target, error fill and
deviation stats are magnitude ideas and leave the plot, though the stats keep being computed.

- **Two windows, one source.** Magnitude keeps the fixed steady-state window that decides tonal balance; phase is read
  through the Virtual DSP gate (where reflections are cut and FDW lives). The Phase gate button opens the same Virtual
  DSP gate dialog, so a window reads the same in both tools, and the plot tracks the dialog live (a gate is placed by
  watching its effect); Cancel returns to the stored gate.
- **Resolved once over the drawn set.** `EqWizardPhaseContext` carries the neighbours, the gate, per-curve placements and
  one shared τ, resolved over exactly the channels drawn. Resolving over hidden channels would let an invisible driver
  move the drawn windows; resolving per channel would flatten each curve onto its own arrival, erasing the offsets a
  crossover region is read for. One τ for the set keeps relative phase honest, so the view can answer "does the junction
  line up". The curves are rendered with that τ as a Manual detrend whatever the stored mode says; the mode rides along
  only so the dialog can show the user's choice. Neighbours are frozen as PROCESSED responses (not drawn curves) so they
  can be re-gated. A raw handoff (its own time base) and imported curves get no context.
- **Windows do not move with the bank.** Re-resolving per keystroke would slide every curve under its own correction. Only
  a gate edit re-resolves, for the whole set, with the panel's arithmetic (`PhaseGatePlacement`, `ResolveCommonDetrendMs`):
  per-curve placement is only allowed while every window opens before its own channel's response, which depends on the
  window lengths just changed, and τ must be taken through the window just resolved (an earlier implementation read the
  neighbours' offsets that were being replaced).
- **Pin vs Auto.** Pinned = one absolute window for every curve; unpinned = each on its driver's arrival. The pin travels
  with the handoff, so an absolute window set by hand does not read as Auto. Auto snaps to the earliest front read from
  the RESPONSES; under a pin the offsets are one absolute time and would make the dialog preview disagree with the plot.
- **Lone IR.** A measurement loaded directly has no neighbours and no chain (the identity stands in); its window and τ
  open on its own front, flattening the propagation delay and leaving the driver's own phase. Overlay slots and text
  curves have no phase at all.
- **Rendering.** Newest-wins off the UI thread (`EqWizardPhaseOrchestrator`), keyed by bank; the neighbours and the bare
  curve ("Without EQ", dashed in the channel's own colour so it does not compete with the bank's white phase) are cached
  per gate. A superseded render must still trigger the redraw that starts the follow-up render, or the view is left with
  no curve and nothing on its way.
- **Drawing.** Phase is wrapped (as in REW and every other phase plot in the app), with NaN breaks at ±180° seams, which
  keeps the axis fixed. The grid is 1500 points so a Q=20 all-pass (most of 360° inside 1/20 octave) still gets several
  points per wrap and seam detection cannot mistake a fast turn for a wrap. Wrap verticals are drawn only for the curve
  under edit: with every curve marking them the HF turns into a picket fence.

## Band shapes

`PeqBandType`: bell (default), low/high shelf, first- and second-order all-pass. All take frequency, Q and gain but read
them differently, so the type travels with the band. `PeqBand.Type` is the last record parameter and defaults to Peaking,
so pre-shelf settings and project files read back as bells. An undefined enum number is turned into a bell where it
enters the app, and `PeqBiquad.Compute` realises any unknown type as a bell (and a degenerate all-pass as a pass-through),
so a hand-edited file never breaks the audio path. Use `IsShelving()`/`IsAllPass()`, never `!= Peaking`, so an
out-of-range value cannot be a bell to the filter and a shelf to the tuning sheet.

- **Shelves** (RBJ): `FrequencyHz` is the MIDDLE of the transition, where the response reaches half the shelf gain in dB;
  Q sets the knee, not a bandwidth. 1/sqrt(2) is the steepest monotonic shelf; above it the shelf overshoots. New shelves
  start at 0.7 (the strips keep one decimal); shelves read from text without a Q parse at 0.707.
- **All-pass:** unity magnitude everywhere. First order: 180° swing, −90° at the corner, no Q (the slot keeps a positive
  sentinel Q because project-file validators require one; the strip greys the field). Second order: 360° swing, −180° at
  the corner; Q sets how abruptly the phase turns and so how much group delay piles up at the corner, which the strip shows
  in place of gain (the hidden gain value is kept so switching back restores the bell). An all-pass is never "transparent"
  at zero gain, and `PeqBand.MagnitudeDbAt` returns unity for it whatever gain a type switch left in the slot.
- New bands from the "+" tile: bell at 1 kHz, narrow (a deliberate correction to drag into place, not a wide bell that
  colours half the spectrum); shelves at the target curve's default corners; all-pass at 2 kHz with a gentle turn (it is
  placed on a crossover). Changing a strip's shape keeps frequency, Q and gain, since a bell and a shelf at the same corner
  are exactly what a tuner compares.

The analog model in `PeqBand.MagnitudeDbAt` is rate-independent and kept for legacy comparisons; fitting, preview and
coefficient output use `DigitalEqualizationResponse`/`BiquadResponse` so they match the RBJ biquads, including bilinear
behaviour near Nyquist. `BiquadResponse.GroupDelaySamples` is closed form, exact where a finite difference of the phase
would wrap a sharp section near Nyquist into a negative delay.

## Q conventions

`PeqQConvention` names what a device's Q number means for a peaking band. All three are the same biquad family with
bandwidth between half-gain points `BW = m · f0 / Q`, differing only in `m`, so conversion (`PeqQConventions.ToConvention`
/ `ToRbj`) is an exact, invertible Q rescale; `PeqQConventionTests` pins it against an independently written Zölzer section
and REW's published bandwidth figures. Names follow REW, the vocabulary a tuner meets elsewhere.

- **RBJ** (`m = 1`, constant Q) — what `PeakingBiquad` realises and the library uses throughout. Equalizer APO,
  CamillaDSP, REW Generic/Extended, Audiotec Fischer (HELIX/MATCH/BRAX), Audison/Hertz, Mosconi, miniDSP, QSC DSP-30,
  StormAudio.
- **Symmetric** / Zölzer–DAFX (`m = sqrt(|gain|)`, proportional Q; boost and cut mirror) — Behringer DCX2496, Rockford
  Fosgate 3Sixty.3, Hypex Input EQ, rePhase, Crown USM810, DSPeaker Anti-Mode Dual Core; confirmed on AMP Panacea (Cirrus
  Logic CS47048C). Nearly RBJ at small gains; at 15 dB the same Q number is over twice as wide.
- **Classic** (`m = sqrt(gain)` with the SIGNED gain) — asymmetric: a boost comes out wider than RBJ, a cut narrower.
  Rare: the JL Audio TwK-88 is REW's one documented processor, and JL's own VXi does not match it, so it is a property of
  a model, not of a maker.

The factor grows with gain (×1.19 at 3 dB, ×2.37 at 15 dB), so a mismatched convention shows up only on deep bands, as an
EQ that measures broader (or narrower under Classic) than designed rather than as an obvious error. Transparent bands,
shelves (Q is a knee with no bandwidth; scaling by gain would print a shelf that overshoots) and all-pass bands (no
half-gain points; scaling by a leftover gain would print a different phase filter) are never rescaled. The convention
affects only the tuning sheet; profile formats are read by RBJ software, and restating Q would corrupt them.

## Equalizer APO text format

`PeqTextFile` reads and writes `Preamp: -6.0 dB` / `Filter N: ON PK Fc 600 Hz Gain 6.0 dB Q 4.0`; the building blocks are
shared with the REW format. Parsing is defensive and never throws: blank lines, comments, OFF filters, unsupported types and
malformed lines are skipped; '.' and ',' decimals are accepted; the band count is capped. `TryParse` reports whether a
Preamp or Filter line was recognised. Band count cannot stand in for that, since a preamp-only file is a valid neutral
profile; treating it as a failure once applied an empty curve over the user's tune.

Type mapping:

- `PK` — bell (Q required).
- `LSC`/`HSC` with a Q — the same centre-frequency, half-gain-at-Fc shelf the library realises; shelves are written this
  way. Plain `LS`/`HS` carry no Q and are read at 0.707.
- `AP` — APO's second-order all-pass: Fc and Q, no gain (Q required, since it is the phase turn).
- `LS 6dB`/`LS 12dB`, `LSC 10.8 dB` (dB per octave) and their high-shelf twins state a CORNER frequency or another slope
  parameterisation; taking their Fc as ours would move the shelf, so they are skipped like unsupported types.
- APO has no first-order all-pass. The capability check drops such bands before export (with a warning); one that slips
  through is skipped with its slot number left as a visible gap. `TypeToken` returns PC-Tool's `AP1` only for the
  Virtual DSP text sheet.

Exports to formats that cannot state shelves, an all-pass order, or a preamp warn first (`EqExportWarnings`, shared with
the Virtual DSP PEQ menu) and are dropped by `EqWizardImportExportCoordinator` either way: the warning is the user's
decision, the drop is the guarantee. A missing preamp slot is the worst case — nothing in the file hints that the whole
curve is that many dB off (louder when a cut is lost), so the user is told which gain to enter on the device. Biquad
coefficients are rate-specific, so each device rate (44.1 kHz car DSPs, 48 kHz miniDSP 2x4, 96 kHz HD-class) is its own
dialog entry.
