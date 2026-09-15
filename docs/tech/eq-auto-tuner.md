# EQ auto-tuner and EQ Wizard

The EQ Wizard corrects a measured (or imported) response towards a target curve with a bank of
parametric bands plus a preamp. Bands are placed by hand in slot strips or fitted automatically.

Where the code lives:

- `dsp/EqAutoTuner.cs` — the fitter (`EqAutoTuner.Tune`, `EqAutoTuner.Options`).
- `dsp/EqBoostabilityMask.cs` — per-frequency decision whether a boost may be placed.
- `dsp/EqualizationCurve.cs`, `dsp/PeqBiquad.cs`, `dsp/BiquadResponse.cs`, `dsp/PeakingBiquad.cs` — the
  band model and its digital (RBJ biquad) response.
- `dsp/PeqQConvention.cs`, `dsp/PeqTextFile.cs`, `dsp/EqProfileFormats.cs`, `dsp/EqualizerApoFormat.cs` —
  Q conventions and profile import/export.
- `source/Tools/Eq/` — the wizard UI (`EqWizardPanel` partials), curve sources (`EqWizardCurveSource`,
  `EqWizardSourceResolver`), phase mode, slot strips and headless auto-tune (`EqAutoTuneHeadless`).

## Greedy fit

`EqAutoTuner.Tune` resamples source, target and (optionally) coherence onto a logarithmic grid
(`GridSize` points, default 256). The error is defined only where both source and target have data;
resampling without end clamping yields NaN outside the measured range and those points are excluded.

The preamp absorbs the broadband level difference (see [Preamp alignment](#preamp-alignment)); bands fit the
shape. The greedy pass (`NextBell`) then repeatedly:

1. picks the grid point with the largest remaining error that is not blocked;
2. refuses boosts the mode or the reliability mask forbids (see [Boost reliability](#boost-reliability));
3. limits a boost to the remaining boost headroom (`BandGainMaxDb` minus the bells' running sum), so a
   roll-off that needs +30 dB gets one capped band rather than a stack;
4. tries every candidate Q (`CandidateQ`: 0.5 ... 10, filtered to the caller's [QMin, QMax]) and keeps the
   one that minimises the residual RMS — narrow peaks get narrow bands, broad trends wide ones;
5. sterilises a footprint around the band.

It stops when the largest remaining error is under `StopResidualDb` (0.5 dB) or the band budget
(`MaxBands`) is spent.

Footprints: `MinBandSpacingOctaves` (0.1 oct) is deliberately small — it only stops the fit re-nibbling the
peak it just corrected, and a cluster of narrow peaks spaced wider than that each still gets its own band
(an older, coarser fixed spacing prevented that). A boost pinned at the headroom limit instead blocks
`SaturatedBlockOctaves` (1 oct), because that whole region genuinely cannot improve further (e.g. a
low-frequency roll-off). If even the narrowest Q would over-fill a masked bin through its skirt, the fit
blocks a small footprint and moves on.

Candidate responses are evaluated at pre-computed unit-circle points, so each candidate costs one biquad
build plus one complex division per point. The arithmetic is identical to
`DigitalEqualizationResponse.MagnitudeDbAt`, so the fit sees exactly what the DSP will realise at
`SampleRateHz`. A degenerate (`IsTransparent`) band contributes nothing and is detected once per candidate.

`ScoreBand` is the single objective for bells and shelves: mean squared residual over valid points plus the
cuts-only over-correction charge. Because shelves go through the same function, a shelf is never accepted
on a softer test than the bell whose slot it takes.

All-pass bands are never fitted: an all-pass is flat, so a magnitude error never asks for one. Callers
replace the whole bank with the result, so hand-dialled bands (shelves included) do not survive a re-fit.

## Preamp alignment

The right broadband level depends on which way bands can move the source:

- **Boosts allowed:** bands correct both ways, so the preamp centres the residual on the MEAN error.
- **Cuts only:** bands can only pull the source down. A preamp below the largest error would drop a point
  beneath the target where no cut can lift it back. So the preamp aligns to the point where the source is
  LEAST above the target (the maximum error), absorbing only the excess every point shares, and stays at the
  ceiling whenever any point is already at or below target. Rounding goes up, so every point stays cuttable.

The ceiling is pre-applied here, not only in the post-band clamp, so bands fit against the same level the
curve is finally realised at. In cuts-only the band peak is 0, so the ceiling is 0: cuts-only must never lift
the curve whatever the level difference or an unbounded `TotalGainMaxDb`.

### Total gain ceiling

`TotalGainMaxDb` caps preamp + summed band gain at every frequency. A positive preamp stacked under boost
bands is a clipping DSP profile, and handing one out leaves the UI reporting the damage as negative
headroom. The cap is applied to the preamp AFTER bands are placed, so the fitted shape stays and the curve
honestly sits below an unreachable target. It is unbounded by default because, as a pure curve fit, the
preamp legitimately carries the level difference between arbitrarily referenced source and target; a
caller producing a clip-safe cuts-only profile passes 0. With boosts allowed, prefer pinning the preamp
(`PreampMinDb == PreampMaxDb`): under the post-hoc cap a boosting fit is realised below the level its bands
were placed against — the whole curve drops by the peak boost.

## Cuts-only over-correction penalty

`CutsOnlyMode` places only cuts. It defaults off in `EqAutoTuner` so the general fitter stays unconstrained,
but the EQ Wizard defaults it on: boosting a reflective cabin's response is where auto EQ does harm (filling
an interference null wastes headroom and a band on a dip that does not survive a small mic move). Cutting
peaks and leaving level on the table is the conservative correction.

In cuts-only, over-cutting pushes a point below the target where no later cut can lift it back. When
choosing a band's Q, each candidate is charged (`CutsOnlyOverCorrectionWeight` = 25) for the over-cut this
band ADDS at a point — the part of its own cut that lands below target — with the first
`CutsOnlyOverCutFreeDb` free. Under-correction (above target) is always fixable and unpenalised.

Charging the band's own contribution, never the depth a point already had, matters: a real response weaves
below the target everywhere, and any penalty scaling with pre-existing depth makes every wide Q lose to the
narrowest one. That shredded a smooth response into a swarm of Q=10 slivers whose notch comb looked worse
than no EQ. With the free zone, a moderately wide skirt grazing a neighbouring dip by up to 1 dB costs
nothing, so residual RMS alone picks the width; only a skirt digging several dB below target (a visible
gouge) is weighted up and loses to a tighter band.

## Boost reliability

When cuts-only is off, `EqBoostabilityMask` (options in `Options.BoostMask`) decides per frequency whether a
boost band may be centred there (high coherence, not inside a narrow deep null). A boost the mask forbids is
not fitted: `BlockForbiddenBoostRun` blocks the contiguous run of boost-wanting, boost-forbidden points
around it, stopping at the first boost-allowed point on each side. A wide correctable dip with a narrow null
at its floor thus keeps its reliable shoulders; only the forbidden core is dropped. It always blocks at
least the centre so the loop progresses.

The mask only clears a band's CENTRE. A wide low-Q boost centred on a reliable point can still pour several
dB into an adjacent forbidden region through its skirt, quietly filling the null the mask protects. So any
boost candidate whose placement would push the cumulative boost at a forbidden bin past
`ForbiddenRegionMaxBoostDb` (0.5 dB) is discarded from the Q search; the fit narrows the band or withholds
it. +infinity restores unguarded behaviour. Cuts are never checked (a cut cannot fill a null).

A missing coherence curve means every point is treated as reliable (null-detection and the fitting band
still gate boosts); a frequency outside the coherence curve holds the nearest value.

## Shelf stage

With `Options.AllowShelves` (off by default, so callers get the bells-only curve), a stage in front of the
greedy pass may place one low and one high shelf.

### Why shelves

The bell is right for a resonance and wrong for a trend. A car target is bass-lifted and tilted down, which
leaves whole octaves at one end of the residual off-target; a stack of bells approximates that badly, spending
three or four slots on a trend and leaving skirts ringing between centres. One shelf replaces them.

### Decision on the finished fit

`PlaceShelves` runs one round per direction. Each round takes EVERY viable candidate of every direction not
yet placed — both shelves, every corner, every knee — through the rest of the fit on scratch state
(`TrialFit`, which runs the greedy bell pass to exhaustion) and applies the one that ends closest to the
target, provided it beats finishing with no shelf. The no-shelf baseline is run once and seeds the
comparison, so the same comparison ranks candidates and refuses all of them when none beats placing nothing.
Running the second round against the first round's residual lets a bass shelf and a treble shelf describe
one tilt together instead of both fitting the same slope. A round in which nothing wins ends the stage (the
next round would search the same residual). The stage never blocks frequencies: bells work on top of shelves.

Three cheaper rules were tried, measured, and rejected:

- an absolute "improves the residual by X" bar, and
- beating the single bell the shelf displaces —

both spent slots on shelves that left the finished fit worse: a shelf acts across octaves and changes what
every later band has to do, which no single-band figure sees.

- Ranking candidates by the single-band score and running only the winner through the finished fit is the
  same mistake one level up. On a tilted response with a loud resonance, the best-reading candidate was a
  wide shelf shaving the resonance's skirt that finished WORSE than none, while the winning shelf sat two and
  a half octaves away and was never asked.

Only the gain at a fixed corner and knee is left to the single-band objective (`ShelfCandidates`): at a fixed
corner and knee gain is a level, and choosing it badly means over-correcting the plateau, which that
objective already charges for. Handing the gain ladder up too would be unaffordable (a tenth of a dB across
the gain range is a couple of hundred candidates per corner, against four knees). Gain is searched in the
0.1 dB the slot strips keep, first in whole dB, then refined within one step of the coarse winner.

### Worth a slot

Beating the bells is not the same as being worth a filter. A shelf that leaves the finished fit SHORTER (fewer
bands, since the pass stops when nothing is worth a filter) has paid for itself. Otherwise the RMS-equivalent
improvement must reach `ShelfSlotWorthDb` = 0.01 dB — two orders of magnitude below anything a tuner would
notice on the plot, and four above the improvements an exhaustive search finds on a response with no trend.
Without it a resonance-only response quietly spends a slot on a half-dB shelf at the bottom of the range for
no visible change.

### Ranking finished fits

`FinalScore` is the mean squared distance from target plus, in cuts-only, the same below-target charge the
band search uses. The charge is on what the BANDS did (`eqSum` = how far this fit pulled the point down; only
the part landing under target counts). Charging the depth itself lets a pre-existing deficit the preamp could
not absorb dominate the number, and the comparison turns on a constant both candidates share. Measured: that
variant rejected at a four-band budget the very shelf it accepted at three, on a response where the shelf was
plainly better.

### Shelf geometry and thresholds

Every number is about "is there really a trend at that end of the range" — a wrong shelf is wrong across
octaves, so none may be decided by a single point.

- **Knees** `ShelfCandidateQ` = {0.3, 0.4, 0.5, 0.7}, in the one decimal the strips keep (a Q the strips
  would round is not the shelf that was scored). Capped at 0.7 by construction, not by the caller's QMax:
  above 1/sqrt(2) an RBJ shelf overshoots its own gain, and overshoot on a CUT is a boost — which cuts-only
  promises never to produce and the mask may have refused. Measured over the whole corner and gain ladder at
  48 kHz, the most a cutting shelf lifts anywhere: 0.0000 dB at Q 0.70, 0.0002 at 0.71, 0.15 at 0.8, 0.90 at
  1.0, 2.69 at 1.4. QMax bounds how narrow a bell may be and is not applied to knees; QMin is (the strips
  accept only that).
- **Quiet-side margin** `ShelfSettledMarginOctaves` = 2 oct of fitting range must lie on the shelf's
  UNAFFECTED side (below a high shelf, above a low one). This separates a shelf from a level change: a high
  shelf an octave off the bottom lifts practically everything — a preamp wearing a filter slot — and the fit
  reached for exactly that when the mask refused the shelf it wanted.
- **Plateau span** `ShelfPlateauSpanOctaves` = 1 oct must lie on the side it acts on, so there is a plateau
  to decide about, not a corner hanging off the range.
- **Plateau start** `ShelfPlateauMarginOctaves` = 0.5 oct out from the corner. There a shelf has reached 57%
  of its gain at the widest knee and 78% at the narrowest, so the span beyond is what it decides about, not
  its transition.
- **Usable plateau** `ShelfPlateauUsableFraction` = 0.75 of the plateau must carry usable data — measured
  points for a cut, boost-allowed points for a boost — with a floor of two points (one point is a bin). At
  three fifths, the fit answered a mask refusing to lift an incoherent top by lifting nearly the whole range
  through a shelf whose plateau cleared the bar by a single point.
- **Corners** `ShelfCorners` tries `ShelfCornersPerOctave` = 3 per octave (a knee is broad; a third octave is
  finer than the choice can be told apart). The ladder is built per direction because the two margins sit on
  opposite sides: a high shelf at 10 kHz is an ordinary car correction, and one symmetric margin wide enough
  to keep a high shelf off the bottom would have excluded it.
- **Minimum gain** `ShelfMinGainDb` = 0.5 dB; below that a shelf is not worth a slot.

### Shelves and the boost guards

A BOOSTING shelf is treated differently from a boosting bell. `ForbiddenRegionMaxBoostDb` stops a band aimed
at a reliable centre from pouring gain into a null through its skirt; a shelf has no skirt in that sense —
its plateau IS the correction and necessarily passes over whatever nulls that end holds — so the per-bin
guard would refuse every boosting shelf. A shelf is instead gated on being justified (usable plateau above)
and its gain is still bounded by `BandGainMaxDb`.

The fitter keeps two running sums: `eqSum` (all bands) and `bellSum` (bells only). Both boost guards — the
headroom cap and the skirt guard — read `bellSum`, because both exist to stop bells piling on each other and
a shelf is not a pile. Charging bells for a shelf locks them out of the region it covers: measured, a +6 dB
bass shelf left zero headroom across the bass, every resonance under it was refused, and the finished fit was
worse than no shelf. The two sums are identical when no shelf is placed, so bells-only fits are unchanged.
Consequently a fit with a shelf can boost past `BandGainMaxDb` in total; bounding that is the caller's job via
`TotalGainMaxDb`, and the EQ Wizard reports it as headroom.

## Wizard sources

The wizard owns its source and target and never reaches into the overlay UI or the current measurement.
Importing a curve (overlay slot, history entry, text file) is a snapshot with no link back to where it came from.

### Calibration choice

`EqWizardPanel.ChooseCalibration` decides the correction a freshly loaded source starts on:

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

The same applies to the fit: when all-pass bands are KEPT through a gated source, `EqWizardPanel.FitSource` (and
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
same points — provided a single conversion (`EqWizardPanel.ToPlotPoints`) drops the same points from both. Two
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

`EqWizardPanel.CreateAutoTuneOptions` (mirrored by `EqAutoTuneHeadless.Prepare`) sets the preamp differently per mode:

- **Cuts only:** the auto preamp may move within the control's range (±80 dB); it aligns to the least-excess point
  and can only lower the curve, and `TotalGainMaxDb` = 0 keeps the profile clip-free.
- **Boosts allowed:** the preamp belongs to the user. Auto-centring it would put broadband gain where the Target Level
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
  all-pass.
- Band gain is bounded by the Min/Max Gain fields, like the faders. QMin is the strips' own limit
  (`PeqSlotControl.MinimumQ`); QMax is the user's Max Q (default 6), well below what a hand-typed strip accepts,
  since a fit is free to place filters far sharper than a cabin measurement justifies.
- Shelves are opt-in because they change the SHAPE of the result, and Max Q says nothing about a knee.
- Before fitting, `EqTargetLevelCheck` takes the median of target minus source over the window. More than 3 dB above
  (broadband boost; unreachable in cuts-only, whose preamp is capped at 0 dB) or 10 dB below (a broadband cut that
  hands level to the amplifier gain and its noise) is a datum set wrong that the fit would follow faithfully, so the
  wizard asks first. The median ignores junction dips and modal nulls, which are shape, not level.
- Every change to a fit input funnels through a redraw that invalidates any in-flight fit; over-invalidation is safe
  (the stale result is dropped and the user re-runs).

## Phase mode

The wizard's phase view (`EqWizardPanel.Phase.cs`, `EqWizardPhaseRender`) draws the MEASURED phase of the edited channel
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
