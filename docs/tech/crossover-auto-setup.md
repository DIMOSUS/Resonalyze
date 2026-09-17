# Crossover auto setup

The crossover wizard proposes a DSP starting point per channel: crossover frequency, filter family,
slope and polarity per junction, plus a cut-only gain per channel. It sums the channels as an ideal
complex sum: measured magnitude carrying its own minimum phase, times the crossover's real phase,
times polarity, with the drivers taken as perfectly time-aligned. Delay is still a separate step done
later, and Auto delay may flip a polarity the wizard set.

Code:

- `dsp/CrossoverAutoSetup.cs` — `CrossoverAutoSetup` (band estimation, classification, the optimizer
  `Propose` and the ranked search `ProposeRanked`), records `DriverBandEstimate`, `AutoSetupSource`,
  `CrossoverProposal`, `RankedCrossoverProposal`, `CrossoverAutoSetupOptions`.
- `dsp/CrossoverJunctionTuner.cs` — the per-junction tuner that reuses the wizard's lattice and
  post-check window.
- `source/Tools/VirtualCrossover/VirtualCrossoverAutoSetupDialog.cs`, `VirtualCrossoverAutoSetupOrder.cs`
  — the UI.

## Ideal complex sum

Channels were combined as a plain amplitude sum until the 2026-09 rethink. An amplitude sum cannot
null and cannot bump, so it was blind to everything a crossover actually decides: it scored a
Butterworth pair the same whichever way round it was wired, and it could not see a split corner at
all, because what a split corner changes is phase.

The sum is now complex, and every term of it is something we know rather than something we measure
through the car:

- **The driver** contributes its measured magnitude with the minimum phase of that magnitude
  (`MinimumPhaseOnGrid` over `MinimumPhase.FromMagnitude`). Not a flat-phase driver: junctions sit
  near a driver's own roll-off — the tweeter Fs region this file already guards is exactly that — and
  on a roll-off the driver has real phase that takes part in the summation. The cepstrum runs on a
  fixed 48 kHz circle of 65536 bins rather than the measurement rate, so the driver phase is the same
  whether the car was measured at 44.1 or 192 kHz, and 0.73 Hz lands under the 20 Hz end of the grid.
  Unmeasured skirts extend flat (`InterpolateDb` clamps its ends): a NaN mask or a collapse to silence
  would put a step into the log magnitude and blow the cepstrum up.
- **The crossover** contributes the phase it really has. `CrossoverFilter.Response` always returned a
  `Complex`; the old `EdgeMagnitude` threw the phase away with `.Magnitude`.
- **Polarity** is a sign, chosen per junction. See [Polarity](#polarity).

What the model does NOT contain is the acoustic path difference between drivers, or the room. That is
deliberate: those are what Auto delay and the achievability post-check are for, and a blank-slate
proposal cannot know them. It also means the wizard's predicted sum is an idealised number, not what
the panel's Sum loss will read — the dialog says so, and the measured answer comes from the junction
tuner and the post-check.

How realistic the ideal is for a candidate is judged separately by `ProposeRanked`, which re-ranks the
top candidates by the loss achievable after the best per-junction delay, measured on impulse responses
with the production alignment search.

## Curve source

The panel builds each driver curve with psychoacoustic smoothing (1/3 octave below 100 Hz easing to
1/6 above 1 kHz) on an 8-cycle FDW, so the wizard reads what the ear resolves and most of the room is
gone before the band read ever happens. Both the band estimate and the score run on that curve.

The FDW's outer gate is stated in **milliseconds**, not in the default 4096 samples. A window fixed in
samples is a different window at every rate: 4096 samples is 85 ms at 48 kHz but 21 ms at 192 kHz, and
an FDW clamped by its outer gate stops being an FDW — at 192 kHz it would collapse to a short fixed
gate from about 380 Hz up, which is most of the band the wizard cares about.

This is scoped to the per-channel curves. The coherent readings — the achievability post-check and the
junction tuner — keep their own gate, because FDW cannot hold a summed impulse response whose arrivals
have not been aligned yet (the window at high frequency is shorter than the spread), which is why
Virtual DSP magnitude is Fixed everywhere else.

The optimizer evaluates the exact digital biquad cascades the DSP runs, at the processor's sample rate
(`CrossoverAutoSetupOptions.ProcessorSampleRateHz`), which need not equal the measurement rate
(`SampleRateHz`, which only bounds the analysis grid and crossover window). The bilinear transform
warps a corner by the rate it was designed at, so scoring at the wrong rate scores filters the device
will not produce (see `PreparedDspResponse`).

With `IndependentSlopes` off, each driver's two shoulders (high-pass and low-pass) share one slope, so
no channel ends up 12 dB/oct on one side and 18 on the other; different drivers remain free to differ.

## Band estimation

`EstimateBand` reads a driver's usable band from its smoothed magnitude curve.

- **Reference level**: the 85th percentile of the whole curve — robust against narrow room dips and
  single peaks. It is read over all points, not just coherent ones: for a narrow-band driver such as a
  sub the passband is a small slice of the log grid, and a percentile over that slice tracks the peak
  and shrinks the band, over-constraining the crossover. The 85th percentile is already robust to the
  out-of-band floor.
- **Edge threshold** (`BandEdgeDropDb` = 8 dB below reference): between the -6 dB textbook edge and
  the -10 dB the residual ripple of a 1/3-octave-smoothed in-room curve asks for.
- **Coherence** (optional γ², aligned 1:1 with magnitude points; a mismatched length is ignored rather
  than trusted). A point with γ² below `CoherenceFloor` = 0.5 cannot anchor an edge even if its level
  clears the threshold. 0.5 matches the phase-unwrap coherence floor: below it the transfer estimate is
  dominated by noise or non-linearity (a breakup resonance, or out-of-band SNR).
- **Segments**: trusted above-threshold points are grouped into contiguous segments, bridging a
  below-threshold gap only up to `MaxBandGapOctaves` = 0.5 octave. A driver's own passband can have a
  narrow interference or room null that dips past the threshold (a 1/3-octave-smoothed null lands
  around this width); anything wider is a real dead zone that separates the band from an isolated
  resonance and must not be bridged, or a lone peak would stretch the band and mislabel the driver.
- The usable band is the most **prominent** segment: largest γ²-weighted area above threshold,
  integrated over log frequency. An isolated resonance past a dead gap, or an incoherent noisy region,
  therefore cannot extend the band or skew the crossover bounds.

## Distortion-clean band

When a THD curve (dB relative to the fundamental, from sweep deconvolution) is supplied,
`DistortionCleanBand` bounds the crossover: a tweeter's low handover follows its measured distortion
knee instead of a fixed floor, and no driver is crossed up into its breakup.

A driver is clean where THD stays below `DistortionCeilingDb` = -30 dB (3 %, the usual
audibility-adjacent line for loudspeaker THD). The result carries a `DistortionBandStatus` so "no data"
and "dirty everywhere" are treated differently:

- `Unavailable` — no curve supplied. Edges NaN; the class-based sensible range stands.
- `Unreliable` — curve supplied but every in-band point is masked (NaN, the |H1| denominator
  collapsed). No information; same as Unavailable.
- `CleanBandFound` — the widest (in octaves) contiguous run below the ceiling (bridging a narrow spike the
  way the magnitude band bridges a null). `DistortionLowHz` is the knee (lowest frequency a high-pass
  may cross at), `DistortionHighHz` the breakup onset (highest a low-pass may cross at).
- `NoCleanBand` — reliable points exist but none clear the ceiling: the driver audibly distorts across
  everything it was measured playing. This is the most dangerous case and must not silently relax to
  the softer class heuristic. The edges become the dirty span's edges, swapped: `DistortionLowHz` is
  the top of the dirt (keep a tweeter above all of it) and `DistortionHighHz` the bottom of the dirt
  (keep a lower driver below all of it), so the bound tightens.

The span of reliable (finite) in-band points, regardless of the ceiling, is what separates Unreliable
from NoCleanBand and supplies the protective edges.

## Classification and sensible ranges

`Classify` picks the driver class by the band's log-centre, with thresholds 63 / 141 / 450 / 2500 Hz at
the geometric midpoints between fixed class centres: 40 (subwoofer), 100 (woofer), 200 (midbass),
1000 (midrange) and ~6300 Hz (tweeter). These centres are not the centres of `SensibleRange` below:
the ranges bound where a class may hand over, not what a driver is. Deriving the thresholds from
the ranges (~423 and ~2283 Hz for the upper two) would class a wide-band midrange reaching 20 kHz
as a tweeter. The class only seeds the suggestion; the user confirms it.

`SensibleRange` caps each class to musically sane handovers: a woofer measured in-room still shows
output near 850 Hz, but nobody crosses a woofer there. Notable floors:

- **Midrange 200–4000 Hz**. The 200 Hz floor lets a woofer/midbass hand over
  before its cone-breakup region when the midrange measures headroom down there; a wide overlap higher
  up interferes badly, and a midrange crossed low with a steep filter cleans the handover. The measured
  midrange band still gates it (a midrange rolled off by 300 Hz crosses no lower).
- **Tweeter 1.7–20 kHz**. A quality tweeter crossed low with a steep filter covers more of the
  critical midrange for a better soundstage; a tweeter rolled off by 2.5 kHz still crosses no lower.
  The 1.7 kHz floor bounds the seed; in the search window the resonance floor below replaces it.

`CrossoverMarginOctaves` = 1 octave keeps the seed crossover (and `ProposeSingle`'s corner) above the
upper driver's low edge (excursion protection) and below the lower driver's high edge. The search
window itself reaches the measured band edges.

## Tweeter resonance floor

A dome's excursion for a given SPL rises 12 dB/oct as frequency falls and peaks at Fs, so crossing at
or below Fs overexcurts it at volume. The high-pass must deliver `TweeterFsAttenuationTargetDb` = 22 dB
at Fs; for a slope S dB/oct that means fc >= Fs·2^(22/S). A steeper filter may cross closer to Fs; a
shallow one is held well above it (`TweeterMinCrossoverHz`).

The 22 dB target is anchored on the Focal TNF datasheet: recommended minimum 3.2 kHz at 18 dB/oct with
Fs ~1370 Hz gives ~22 dB at Fs. Rearranged per slope, `SlopeFloor` holds a tweeter at
slope >= 22 / log2(fc / Fs); at or below Fs no real filter qualifies, which pushes the search off that
frequency. `JunctionSearchBounds` opens the tweeter window down only to where the steepest available
slope still protects Fs, so the floor never strands the seed above every option.

Because frequency and slope are searched decoupled, the descent can end with the tweeter high-pass
below its floor for the slope it settled on (one lattice step in matched-slope mode, or a whole floor
when a low max-crossover limit boxes the junction in and the deviation penalty favoured a gentler
slope). `EnforceTweeterResonanceFloor` runs afterwards as a backstop: raise the crossover to the lowest
protecting lattice point, and if the max-crossover limit blocks that, steepen instead.

Fs is estimated from the tweeter's own measured low roll-off and
floored at `TweeterFsFloorHz` = 1200 Hz so a spuriously low or already-filtered edge cannot license a
dangerous crossover.

## Group delay budget

`MaxCrossoverGroupDelaySeconds` = 10 ms. A slope is excluded when its peak group delay exceeds the
budget: a steep low-frequency crossover smears the arrival by many periods, more than the protection
it buys. The bound is on delay, not frequency, so the same slope is allowed higher up (48 dB/oct is
fine at a 250 Hz woofer/mid handover, ~5 ms, but not at a 75 Hz sub/woofer handover, ~17 ms).

- Group delay is identical for low-pass and high-pass, so both shoulders are bounded the same way.
  With matched slopes a channel is held to the gentler of its two junctions, so a steep woofer
  low-pass with a gentle high-pass needs independent slopes.
- The bound only caps how much steeper than the practical floor (the family's gentlest slope,
  12 dB/oct) the search may go. The floor is always admitted: at a junction so low that even it exceeds
  the budget, a gentler crossover would break the overlap policy, so that delay is inherent to crossing
  that low. A 24 dB/oct slope over the budget is excluded like any other.

## Polarity

A crossover of a given family and order puts a fixed phase relationship across its junction, so the
polarity that makes the two sides sum is the crossover's to state. The wizard now states it
(`CrossoverProposal.InvertPolarity`), and the panel writes it to both sides of the pair with the
frequencies, families, slopes and gain — a crossover is one electrical filter.

It is **derived by evaluation, not tabulated**. The textbook rule — invert when the order N satisfies
N ≡ 2 (mod 4), which is 12 and 36 dB/oct — describes a matched-corner Linkwitz-Riley and nothing else:

- odd Butterworth orders (6/18/30/42) put the two sides in quadrature, where |A + B| = |A − B| and
  polarity cannot change the summed magnitude at all; a table would have to invent an answer;
- Bessel is defined for flat group delay and its low-pass/high-pass pair is not phase-complementary in
  the Butterworth/Linkwitz-Riley sense, so the parity rule does not hold for it;
- independent slopes have no single order, so the rule has no entry;
- a split corner leaves it with nothing to say at all;
- and the filters are realized through the bilinear transform at the processor rate, which walks away
  from the analog prototype as the corner climbs.

`BestPolarity` scores each candidate both ways round and keeps the better, which costs one extra
evaluation per candidate. Where the table is valid the computation reproduces it, and
`AutoAlignmentEngineTests.CrossoverSettlesJunctionPolarity_ReadsTheSplitAboveTheFence` already pins
that for Linkwitz-Riley 24 (upright) and 36 (inverted).

The junction decides a RELATIVE polarity; the absolute one is a global flip, which is acoustically
free. `NormalizePolarity` settles it once at the end: take the side with fewer inverted channels, and
on a tie leave the lowest driver upright.

Auto delay runs after the wizard (the documented order is Auto crossover, junction tune, Auto delay)
and composes its own flip over this one with an XOR, reading the already-inverted response. Nothing
forces the wizard's answer on it.

## Per-junction windows

`CrossoverAutoSetupOptions.MinCrossoverHz` / `MaxCrossoverHz` are the SYSTEM band limit — they
band-limit the outermost channels — and are no longer the search window. A junction is narrowed
through `JunctionSearchWindow`, one entry per junction. Before the split, typing 200 Hz as a lower
limit because a midrange/tweeter junction should not go below it also hung a 200 Hz high-pass on the
subwoofer.

`ResolveJunctionWindow` is the one function that resolves a window, used by both the search and the
dialog, so the row a user reads is the window the search runs on. It returns the effective bounds plus
a **note for every bound that moved** — the tweeter's Fs floor, the distortion knee, a class bound,
the measured band, the system limit. A window that collapses to one frequency says so too. The old
code clamped silently, which is most of why the wizard read as wilful.

A `JunctionWindowNote` carries two strings, and the split is the point. `Summary` is the FACT, short
enough to sit beside the row — `Estimated tweeter Fs 1.2 kHz`, `1500 → 2100 Hz`, `18 → 24 dB/oct`.
`Detail` is the reasoning, and it belongs in a tooltip: a row that explains a design rule where a
number should be is unreadable, which is what `712–736 → 1649–4663 Hz: the tweeter's Fs floor sits
above the lower driver's breakup onset` proved in the field.

The user may narrow, never widen; where safety disagrees, safety wins and prints why. Neighbour
separation is NOT part of the window: it moves as the descent moves the junctions either side, so
`JunctionSearchBounds` applies it on top.

### Three strengths

The bounds that shape a window are not equal, and treating them as one number is what produced two field
reports at once. Weakest first:

1. **Class bounds** say which class SHOULD own a region. The drivers have already said what they CAN do, so a
   class bound that empties the window is dropped rather than obeyed, silently: a preference losing to a
   measurement is not news. A midbass capped at 500 Hz under a driver that only starts at 702 Hz keeps the
   measured 702-741 Hz overlap.
2. **Measured bands** are what the drivers actually produce, and they bound the window unless safety disagrees.
3. **Safety** — the tweeter Fs floor and the distortion knee — always applies, including where the class bounds
   had to be dropped. It used to be bundled into the same variable as the class bound and went out with it: a
   midbass measuring to 736 Hz under a tweeter measuring from 712 Hz produced a 712-736 Hz window, inside the
   dome's own resonance.

Where safety and the drivers disagree, the floor is the one bound that protects hardware rather than quality —
a tweeter crossed under its resonance overexcurts, while a lower driver asked to reach past its breakup merely
sounds worse — so the floor stands and the overlap gives way. The window then opens UPWARD from the floor by
`SafetyOverrideSpanOctaves` = 1.5 rather than collapsing onto it.

Both halves of that matter. Collapsing to a single frequency left the descent nothing to search and handed the
corner to `EnforceTweeterResonanceFloor` afterwards, which puts it at the lowest merely SAFE frequency that
nothing has optimized. And the span is bounded because the alternative — opening to the system limit — offers a
mid-to-tweeter search everything up to 20 kHz, which is not a handover anybody would dial. 1.5 octaves is not a
round number: protecting Fs needs fc >= Fs·2^(22/slope), so covering every admissible slope from 48 down to 12
is 22/12 − 22/48 = 1.375 octaves.

## Slope window

A junction's slope window is clamped so that it always contains `MandatorySlopeDbPerOctave` = 24, and
the clamp is reported like any other. This is not a taste call: 24 dB/oct is the anchor of the
slope-deviation penalty, the seed slope, and the dedicated conventional run that wins ties. A window
excluding it would leave all three pulling at a slope the search cannot take, and the conventional
candidate would not exist for the ranked search to prefer.

The window bounds what the search may CHOOSE; the group-delay budget and the tweeter Fs floor still
filter on top, and can exclude a slope the window allows. Where that leaves nothing,
`AllowedSlopes(junction, …)` falls back to the unrestricted set rather than stranding the junction.

## Split corners

A junction may hold its two corners apart — the lower channel's low-pass below the upper channel's
high-pass — or overlap them the other way round, when that sums flatter. It is **off by default and
enabled per junction**: a split corner is a deliberate choice, not something to discover in a tuning
sheet.

It is searched as a SIGNED offset from the junction corner (`SplitOffsetOctaves` = 0 and ±1/12, ±1/6,
±1/4 octave), not as two free frequencies. A free pair squares the lattice, and the coordinate descent
and the candidate pool are both built around one frequency per junction. The corner stays the
geometric middle of the two edges, which is also how `CrossoverJunctionTuner.Probe` already described a
split variant, so every placement prior, separation rule and penalty keeps working on the same number
as before.

The offset is a coordinate of the descent in its own right (`OptimizeJunctionSplit`, run after
`OptimizeJunction` has settled frequency, family and slope), NOT a factor inside the junction sweep.
Crossed with the sweep it multiplied every frequency, family and slope combination by the length of the
offset list, and the dialog's preview for a four-way with all three junctions split went from 0.3 s to
10 s — CI caught it as a 30 s timeout in `ASplitVerdict_FitsInsideTheWindow`. As its own coordinate the
same fit is 1 s, because the offset is tried on one corner rather than on every candidate corner.
Offset 0 is on the list, so a junction that gains nothing keeps the matched corner the sweep gave it.

Both signs are searched because the junction defect has two signs. Holding the corners apart takes
level out of the overlap, which is what a bump needs; overlapping them puts level back in, which is
what a suckout needs. Searching only the first would have left half of "no bumps and no dips"
unreachable.

An offset is charged for itself at `SplitPenaltyDbPerOctave`, which is `OverlapPenaltyDbPerOctave`.
Parting the corners shrinks the overlap integral whatever it does to the response, so the overlap term
hands a split a reward it has not earned: uncharged, the widest offset on the list won at every
junction that was not pinned, and the option was not a search but a constant +1/4 octave. Charged back
at the same rate the overlap term pays out, only a real flatness gain survives — on the synthetic
fixtures, a wide-overlap two-way still parts by 1/4 octave and takes 0.04 dB off the junction, while a
Bessel pair over the same drivers stays matched.

Where the tweeter Fs floor and a split disagree, `EnforceTweeterResonanceFloor` drops the split: Fs is
safety and the split is a preference.

## Optimizer

Coordinate descent over junction frequency, family, slope, split offset, polarity and channel gain,
scoring flatness of the summed magnitude on a 24-points-per-octave log grid.

- Frequencies are searched directly on the lattice of `RoundToLattice` (5 Hz steps below 100 Hz,
  10 Hz below 1 kHz, 50 Hz above), so the scored frequency is the proposed frequency. The junction
  tuner shares `LatticePoints`, so any corner it proposes is one the wizard could have produced.
- Adjacent junctions stay at least `MinJunctionSeparationOctaves` = 0.5 octave apart so a three-way
  search cannot collapse two junctions onto one frequency.
- Gain is refined ±8 dB in 0.25 dB steps; cut-only is enforced afterwards by referencing the loudest
  channel.
- Up to 6 passes, stopping when a pass improves less than 0.01 dB; it converges well within that on
  two- and three-way systems.
- Flatness is judged over the interior passband only: half an octave is trimmed inside the outermost
  drivers' skirts (or the band-limit edges). Those skirts are identical for every candidate, and that
  constant floor would otherwise swamp the crossover ripple the optimizer can change.
- The score is invariant to a global level shift, so only relative gains are searched; gains are then
  referenced to the loudest channel (`NormalizeGainsCutOnly`).
- With independent slopes off, the slope belongs to the channel: `EnumerateJunctionOptions` varies only
  frequency and family (skipping a family that cannot supply the held slopes), and
  `OptimizeChannelSlope` tunes both shoulders together, starting from an allowed slope because the
  current one may have fallen under the tweeter floor after a frequency move.
- When the user narrows the window inside a driver that still plays there, the outer channels get a
  band-limit high-pass / low-pass at the window edge, on the gentlest admissible slope (24 dB/oct in the conventional run) (e.g. a 75 Hz
  limit on a woofer reaching lower). It only counts when at least a semitone inside the driver edge, so float noise does not
  sprout filters, and it is not part of the search.
- `JunctionSearchBounds`: where both drivers produce output, inside the class-compatible band when the
  classes overlap (a woofer must not cross up in its roll-off skirt), and separated from neighbours.
  Crossed bounds collapse to one pinned frequency. The seed (`ProposeCrossoverFrequency`) is the
  intersection of the level-aligned curves inside the overlap, an octave from both band edges,
  falling back to the geometric mean.
- The seed slope is the admissible slope closest to 24 dB/oct, so descent only leaves the standard when
  the score rewards it.
- **Dip penalty**: a narrow suckout is far more audible than the same energy spread as ripple, so the
  deepest dip below the mean is added to the RMS flatness score (weight 0.5).
- **Psychoacoustic smoothing** is applied to the summed level before mean, RMS and dip are read. A
  coherent sum can put a single-bin notch anywhere and the ear does not hear one; the kernel is the
  same 1/3-octave-to-1/6-octave width the curves themselves carry.

### Junction flatness

Flatness across the whole system band is not the same question as flatness AT a junction, which is
what a crossover decides and the only thing the user asked the wizard for. `JunctionPenalty` scores
each junction separately over an octave either side of its corner, weighted `JunctionFlatnessWeight` =
0.5.

Two choices in it are worth stating, because the obvious versions of both are wrong:

- It scores FLATNESS, not the tuner's summation loss |A + B| / (|A| + |B|). The loss asks whether the
  two sides add coherently, and under ideal alignment they nearly always do once polarity is right — a
  Butterworth pair in phase scores a perfect zero loss while putting a 3 dB bump on the response.
  Bumps count as well as dips (`BumpPenaltyWeight` = 0.5); the system-wide term never looked for a
  bump because an amplitude sum could not make one.
- It is referenced to the band's own straight trend in log frequency, not to its mean. A mean
  reference charges the junction for the drivers' tilt through the band, which is largest exactly
  where a handover is most needed — the term would quietly become a placement force and fight the
  class priors. A tilt is free; a bump or a suckout is not.

The weight is a judgement call, measured on synthetic fixtures and not in a car: at 1.0 the junction
term outweighed the overlap penalty and took a 12 dB/oct woofer low-pass where the engineering answer
is 24; at 0.5 the engineering penalties hold and the term still rejects the Butterworth bump. The
discriminator for "is this term doing it?" is to set the weight to zero and re-run — that is how the
placement complaint above was traced to the sum itself rather than to this term.

## Engineering penalties

Pure flatness is blind to band overlap: shallow filters let adjacent drivers overlap widely, which
averages out each other's ripple and reads flat, but wide overlap means lobing, intermodulation and
out-of-band excursion. The score therefore adds penalties that encode engineering practice.

- **Overlap** (`OverlapPenaltyDbPerOctave` = 0.6): octaves of meaningful overlap between adjacent
  drivers; steeper filters narrow it.
- **Minimum slope** (`MinPracticalSlopeDbPerOctave` = 12): 6 dB/oct protects nothing and is excluded;
  12 dB/oct is a valid second-order choice but discouraged by the slope-deviation penalty.
- **Slope deviation** (anchor `PreferredSlopeDbPerOctave` = 24, the car-audio standard): each
  crossover shoulder pays `SlopeDeviationPenaltyDb` (0.7 dB) per unit of |log2(slope/24)| — 18/36 cost ~0.42/0.59 units,
  12/48 cost 1.0. A deviation is taken only when it earns more than it costs. This stops the search
  dragging a tweeter maximally low on 48 dB/oct when a standard 24 dB/oct handover a little higher
  (even in the ear's sensitive band) is the cleaner choice.
- **Non-adjacent overlap** (weight 4 per band of distance): a driver whose roll-off reaches past its
  neighbour into a non-adjacent driver's band (a 12 dB/oct woofer bleeding up to the tweeter) is
  pushed to a steeper slope.
- **Ear sensitivity** (2–4 kHz, sigma 0.5 octave, weight 0.5 dB): a handover there puts phase wobble,
  lobing and any residual dip where they are most audible. A Gaussian bump in log frequency centred on
  ~2.83 kHz (0.61 of full at 2 and 4 kHz, ~0.14 an octave from the centre). A tie-breaker, not an override of a genuinely
  flatter split.
- **Sub handover up-bias** (0.6 dB): a sub should hand over where it stops being localizable
  (~80 Hz), not as low as flatness drags it; a sub crossed at 45 Hz leaves the woofer carrying real
  bass. The handover is nudged toward the top of the sub's sensible range.
- **Wide-overlap low bias** (0.4 dB): when adjacent drivers share a wide band an engineer crosses low,
  letting the smaller upper driver take over as early as it cleanly can (better dispersion, less
  excursion and breakup demand on the lower driver). The pull scales with the shared band's width.
- **Midrange localization** (threshold 250 Hz, weight 2 dB): a bass/midbass handing up to the
  midrange should cross below where the ear begins to localize by content (~300 Hz); above that a
  low-mounted midbass smears the stage. The threshold sits a margin under 300 Hz so the midbass
  passband, not just its -3 dB point, stays safe. The pull is log-proportional to how far above the
  threshold the junction sits, and firm because flatness otherwise drags the junction to the top of
  the shared band (a broad midbass reads flatter carrying more low-mids). It self-limits: where the
  midrange cannot fill down, the flatness cost holds the junction higher. It is deliberately scoped to
  the midrange handover; strengthening the generic wide-overlap bias instead would wrongly drag the
  tweeter junction down too, whose low placement is governed by the Fs floor.

- **Overlap measure**: each driver is normalized to its own passband peak (so gains drop out) and the
  overlap is the log-frequency integral of two normalized responses' product — near an octave for a
  clean LR24 handover, several octaves for shallow filters.
- **Same-class junctions** (two subs, two midbasses) get none of the class-placement priors: those
  answer which class should own a shared region, which has no meaning here; flatness and the
  post-check decide.

## Target-curve gains

The optimizer level-matches drivers to flatten the sum, which is right for choosing crossovers but not
for the gains a user wants. `ApplyTargetCurveGains` replaces the gains with a car target-curve fit,
leaving crossovers untouched. Every level is measured over the channel's assigned passband.

- **Reference (flat top)**: the quietest driver that is not a subwoofer; the whole system is cut to it.
  All midranges and tweeters are levelled to it. Every sub is excluded, not just the bass anchor: a
  sub is a separately amped, lifted way, and a quiet one (or a second sub splitting the bottom) must
  not set the level the rest of the system is cut to. A woofer/midbass anchor stays a member, so when
  it measures quieter than the mid/tweeter the system is still cut to it (elevation then zero).
- **Bass anchor**: the subwoofer when present, else the lowest woofer/midbass (the first of its class
  in chain order). A sub-less bass driver carries the cabin's low-end elevation just the same.
  Anchoring only a subwoofer once left such systems with a flat target at the mid/tweeter reference,
  cutting a woofer with real cabin gain down to tweeter level (field case: -24 dB) while the elevation
  control sat dead at zero.
- The anchor sits at reference + elevation (`SubElevationDb`; null = measured elevation, i.e. raw
  level). The target descends in log frequency from the anchor to the start of the flat top (the
  lowest reference member). Intermediate drivers are fit onto that line. All gains are cut-only, so no
  measured dip is filled and the result is headroom-safe.
- `MeasuredSubElevationDb` is both the default and the upper limit of the elevation control: the user
  may only trim the bottom down. The dialog recomputes that ceiling on every fit, because reordering
  or retyping can change which driver anchors the bass; a stale ceiling would either be silently
  clamped by the DSP while the preview printed the old value, or cap the field at a value a later
  order makes wrong. The field's value is pre-filled only on the first fit.
- **All-sub chain** (two subs splitting the bottom, rest of the car in other groups): no flat top, so
  the subs level to each other, cut-only, and the elevation is zero. Reading it off the bass level
  instead would give +8 dB or 0 dB for the same pair depending on which end of the chain was loud.

## Groups outside the chain

Only a group is a crossover chain. A rear fill or a centre plays the same band as the front stage from
another place with no filter handing anything between them; a chain drawn through all of them would
invent junctions. The dialog therefore fits each group separately, the primary group (front chain,
else the first staged group) first, since others are levelled onto it.

- `ProposeSingle` handles a driver that crosses with nobody (rear fill, centre, lone sub). `Propose`
  needs two channels, but such a driver still needs the protection a chain member gets from the
  driver under it: a high-pass. The corner starts an octave above the measured low edge (the same
  margin the junction seed keeps) and is raised by the tweeter Fs floor and by the distortion knee. The
  octave margin is a preference that a driver narrower than two octaves cannot have; Fs and the
  distortion knee are safety, so where the window cannot hold them the method refuses rather than
  return a corner under the floor it computed. The corner is snapped up to the lattice (a step down
  would be under the floor) and capped an octave under the driver's top. A low-pass is added only when
  the window cuts into the driver and still leaves a band. The slope is re-chosen at the final corner:
  floors only raise the corner and a higher corner only admits more slopes, so it cannot become
  invalid.
- `OffsetToReferenceLevel` slides a group's gains rigidly onto the primary group's reference level
  (`ReferenceLevelDb`), preserving the group's internal balance, cut-only. A rear fill's right level is
  a mix decision made by ear (usually well under the front); matching the front is the measured
  starting point for that decision, not the answer.

## Chain order

`Propose` walks channels in the order given: channel i hands over to i+1 only. Driver type cannot
provide that order once two channels share a class (a pair of subs dividing the bottom). The dialog
orders each group by `VirtualCrossoverAutoSetupOrder.CenterHz`, the log-centre of the effective band:
the measured band narrowed by corners already set on the channel. Setting those corners is how the
user resolves what the raw measurement cannot — two full-range subs look alike, one crossed at 50 Hz
and one under it do not. A corner pair that leaves nothing falls back to the measured band.

`Judge` rates each adjacent pair: `AsMeasured` (later clearly higher), `Unclear` (centres within
`AmbiguousSeparationOctaves` = 0.5, the same separation the optimizer demands between junctions, so
there is no room for a handover anyway), `Reversed` (later clearly lower — usually a row moved the
wrong way or a wrong confirmed type). The dialog colours Unclear amber and Reversed red, and asks for
confirmation before applying such an order rather than refusing, since the user may know which sub is
which.

## Ranked search

`ProposeRanked` builds a candidate pool from the best `PoolOptionsPerJunction` = 4
(frequency, family, slope) options per junction, scores their cross combinations (at most 512), and
sends the top of the pool to the achievability post-check. A dedicated conventional run forces every
slope to 24 dB/oct (Linkwitz-Riley when allowed); it is the engineering baseline that wins ties
(`IsConventional24`). A pool candidate that merely uses 24 dB/oct slopes is not conventional.

Final score = magnitude score + `AchievabilityWeight` (0.5) × achievability penalty. A challenger must
beat the conventional candidate by more than `Conventional24PreferenceDb` = 0.25 dB.

## Achievability post-check

The achievability penalty is the summed dip-penalized junction loss remaining after the best
per-junction delay.

- It works on gated direct sound, so the chains run on a shared crop of the measured IRs
  (32768 samples, 8192 before the peak) instead of the full capture. Verified against full-length IRs
  on real measurements: the 4096-sample evaluation gate sits at the shared peak anchor, so the crop
  does not change the losses.
- Per junction the alignment search runs in a window around the raw channels' band-limited arrival
  difference, computed once because it is candidate-independent. The half-window absorbs the group
  delay any candidate can add, which scales as 1/fc (an LR24 rings ~10 ms at 40 Hz, ~0.1 ms at 4 kHz):
  `PostCheckHalfWindowMs` = clamp(1200 / fc, 2, 12) ms. A wide window at a high junction would cost
  thousands of probe deltas across short periods for nothing.
- A junction where the search finds no candidate is scored with a flat 6 dB penalty instead of
  silently winning by absence.
- Both sides' raw arrivals are read in the same shared junction band (cached per channel and band).
  Arrivals from different measuring bands are not comparable: each band carries its own driver group
  delay and envelope rise. Unreadable arrivals fall back to an unanchored search over the widest window.
- `AchievabilityPenaltyDb` runs the production alignment search per junction: arrival-anchored prior and
  `AlignmentSelection` tie-breaks, so an inverted half-period impostor cannot fake an achievability the
  real Auto delay would refuse. A pick within 10 % of the window edge triggers one retry at double
  width, re-selected through the same rules (taking the retry's raw best would hand the wider window
  to exactly that impostor).
- Deliberate simplifications versus the full engine, acceptable for ranking: no PHAT-seeded timeline and
  no cascade reprocessing of settled neighbours (junction deltas of a mono N-way compose independently).
- Candidates are ranked with the optimizer's level-matched gains (right for comparing crossovers); the
  emitted proposals carry target-curve gains. The conventional candidate is identified by signature
  and forced into the post-check, replacing the worst pool entry if truncation dropped it.
- The pool (`SolvePool`) bounds each junction's options against the descent optimum's neighbours, so
  two junctions moved toward each other (a peaked middle driver pulls both inward) can jointly break
  the minimum separation or swap order; such combinations are rejected. Enumeration is mixed-radix,
  covering the best-ranked choices first when the product exceeds the cap.

In the dialog the ranked search runs off the UI thread (seconds on a 4-way); the preview shows the
fast proposal until the ranking lands, and the inputs are frozen meanwhile so the applied result
matches the visible settings.

### Decimated ranking

Every candidate the junction tuner ranks costs two gated FFTs per side, and their length is the
measurement rate times a gate fixed in TIME (about nine periods of the ranking band's low edge). For a
sub junction at 96 kHz that is a 262144-point transform to read a band that stops at 260 Hz: almost
all of that rate is bandwidth the read throws away.

`BuildRankingWork` therefore decimates the crops to just above the ranking band (`Decimate`, a
double-precision windowed-sinc by a whole factor — the app's rational-ratio converter is a float
playback path) and ranks there. Measured on the archived cabins, a whole chain at 96 kHz with
2-octave windows went from 27.3 s to 3.8 s with matched slopes and from 122 s to 7.1 s with free
slopes, and the marginal cost of one candidate fell from 4-38 ms to 0.07-0.9 ms. The picks were
unchanged on six junctions across two cabins, with scores within 0.03 dB; one tie moved by a single
lattice step.

The own-band reads and the after-delay search stay at the MEASURED rate. They are a handful of calls,
they are the numbers the user reads, and the alignment search resolves delay against the sample grid.
The headroom (four times the band top) keeps every junction band well inside the decimated Nyquist, so
the bands the reads use are the ones an undecimated run would have used. At a tweeter junction the
band already fills the rate and there is nothing to throw away, so the factor falls below two and the
decimation is skipped; the remaining lever there is caching the gated spectrum per chain.

## Junction tuner

`CrossoverJunctionTuner.Tune` retunes one junction of a finished tune: the lower channel's low-pass and
the upper channel's high-pass (corner, family, slopes), keeping everything else — other junctions,
gains, delays, polarity, PEQ, chain order. One result serves both sides of a stereo pair, because a
crossover is one electrical filter; current edges are read from the first side.

It deliberately does not use the wizard's objective. The wizard reads magnitudes under ideal
alignment, anchors on 24 dB/oct and levels channels — right for a blank tune, wrong for a finished one,
where the phase the two drivers put into the junction at their current delays is the whole question.
A steeper slope that narrows the band where a ragged excess phase can interfere is a legitimate answer
the magnitude cannot see. So each candidate is scored on the coherent sum of the measured responses
through their full chains: loss, plus the dip's excess over the loss at half weight, plus ripple (room
ripple included — what varies between candidates is the crossover's doing). There is no slope
preference, and the current crossover is kept unless a challenger beats it by `KeepMarginDb` per side.

- **Two bands**. Candidates are ranked on one shared band: an octave outside the whole corner window
  and the current corner, inside the audio band and measured range, so every overlap region is inside
  it and the car's own ripple is the same term for all. Readings on two different bands are not
  comparable — the car's ripple differs between bands by more than a corner decides. Each reported
  candidate is also read on its own junction band (an octave each side of its corner, what the
  panel's Sum loss row and the AI package show). A challenger wins only if it beats the margin on the
  shared band and reads no worse on its own band; otherwise the win would not show in the user's
  read-outs.
- **Cost control**. Readings run on a direct-sound crop sized to the ranking band's gate (about nine
  periods of its low edge, plus room for chain delay and fades), capped at the wizard's post-check
  length. Corner probes are the wizard lattice thinned to 1/24 octave (the 50 Hz lattice above 1 kHz
  is finer than any crossover decision), always keeping the window top. Processed responses are cached
  by chain record: a lower channel's response depends only on its low-pass, so slope combinations of
  one corner share it. Own-band reads are taken only for reported candidates.
- **Phase rotation reference**. A channel phase control states its angle at one of the channel's
  crossover corners (`PhaseRotationSpec.ReferenceIsLowPass` says which, rather than inferring it from
  frequency — a sub with no low-pass dialled in yet can still carry a low-pass reference). A search
  that moves that corner must move the all-pass too, or it would score a response the device would not
  produce.
- **After-delay reading** (`JunctionTuneAlignment`): what the junction would measure after the delay
  production alignment would pick for the upper channel, with the same search and tie-breaks as the
  wizard post-check. It shows how much of what remains is timing's to fix. Polarity is reported as
  the upper channel's resulting polarity: the search reads the already-inverted response, so its flip
  is relative, and reporting it raw would tell a reply to invert a channel that should stop being
  inverted.

`Probe` is the read-only counterpart for an assistant: it reads arbitrary variants (a crossover, a PEQ
bank, the baseline as it stands) on each variant's own band, on one band shared by all variants, and
after re-running alignment for that variant (the tune's delays were set for the current chains, so
judging a candidate on them is unfair). The shared band spans both edges of every variant, and a
variant's corner is the geometric middle between its two edges, since a variant may hold them apart on
purpose (a mid low-passed at 3.6 kHz under a tweeter high-passed at 4.4 kHz). Its cross-phase reading
uses the phase analysis's own window and is comparable only between entries of one probe, not with the
panel's gated junction phase. `ProbeAlignment` reports what Auto delay would pick per junction plus the
rival optima it weighed, always including the pick.
