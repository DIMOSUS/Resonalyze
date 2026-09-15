# Spatial average (MMM / microphone array) and audition

A spatial average is a magnitude curve averaged over the listening volume, taken either with one
microphone walked through it (moving-microphone measurement, MMM) or with several microphones
recorded at once (a microphone array). Resonalyze uses it in two places:

- the **hybrid magnitude view** of Virtual DSP, which draws each channel's spatial average with that
  channel's DSP chain on top instead of the impulse response measured at one point, and
- the **audition render**, which can correct a headphone render so its tonal balance follows the
  averages instead of one microphone position.

The average is a refinement of what is drawn, never the basis of a computation: delays, polarity,
junctions and the summation loss keep reading the impulse responses. What it removes is one
position's interference dips, which are the dips a tune must not chase.

Where the code lives:

| Area | Code |
| --- | --- |
| Stored capture, recipe, set rules | `LiveCaptureDocument`, `LiveCaptureRecipe`, `LiveCaptureSetVerdict` (`source/LiveSpectrum/LiveCaptureDocument.cs`) |
| Hybrid arithmetic (shared with the EQ Wizard) | `SpatialAverageHybrid` (`source/LiveSpectrum/SpatialAverageHybrid.cs`) |
| Calibration modes | `SpatialAverageCalibration`, `SpatialAverageCalibrationMode` |
| Datum and median rule | `SpatialAverageOffsets` (`source/Tools/VirtualCrossover/`) |
| Panel integration | `VirtualCrossoverPanel.SpatialAverage.cs` (`HybridMagnitudes`, `SetDatum`) |
| Audition correction | `SpatialAverageAudition` |
| Audition flow and dialog | `VirtualCrossoverPanel.Audition.cs`, `VirtualCrossoverAuditionDialog` |

The stored capture format itself is described in [live-spectrum.md](live-spectrum.md#capture-document).

## Methods: moving microphone vs array

`SpatialAverageMethod` records how the average was obtained. The arithmetic that puts a chain on top
is identical for both, and no consumer branches on it. The method is recorded because the two are
**tethered differently**, so the checks a set must pass differ:

- A moving-microphone pass is reference-free. Its level is held by nothing but one analyzer session
  at one input gain, and its slope compensation is a curve that depends on frame length, window and
  rate.
- An array is a swept transfer function riding the measurement's own loopback, so its level is
  fixed by the same reference the impulse responses use. Channels measured minutes or days apart are
  comparable by construction.

A set may not mix the two methods (`LiveCaptureDocument.JudgeSet`). Mixing would need a per-channel
offset, and the per-channel offsets are exactly the spread detector: drawing with them would give a
set that can never disagree with itself and a diagnostic that is identically zero.

## Mode selection

The project's method (`VirtualCrossoverPanel.SpatialAverageMode`) is the stored choice, or a fallback
(array if any channel carries an array capture, else moving mic). `SettleSpatialAverageMode` stores
the fallback **once**, the first time there is anything to guess from, and from then on the mode is
the project's own.

Computing the fallback live was a real defect: a session written before arrays existed carries
attachments and no stored mode; loading one new measurement that happened to carry an array flipped
the project to the array method, the attachments went unread and every channel without an array
silently fell back to its point response. Freezing is the whole fix. What is stored is what the
fallback already says, so a project opens the same way tomorrow; an extra preference of one family
over the other was rejected because it changed what an existing session holding both displayed.
A project with nothing to guess from stays unstored, so the first measurement (an array arrives with
it) decides without asking the user to find a menu.

The method is offered on every channel's spatial-average button, because that is where a user notices
a missing curve.

## Attaching and resolving captures

`ResolveSpatialAverage` re-attaches a persisted moving-microphone capture by the same ladder the
measurements use: stored path, then the same file beside the session it was imported from, then
beside the folder the user pointed at when relinking. A capture that does not resolve degrades to an
unattached side rather than failing the project load, and the stored path is left standing: it is
the only hint a later relink has, and it tells the button to warn instead of showing a channel that
never had an average. Once read, the actual path is pinned, because the project becomes the internal
autosave right after import and that copy has no session file beside it to search from. Replacing a
capture clears the relative path, which named the previously imported file. Arrays are not attached
by path, so only moving-microphone captures can go missing.

## Coverage and set verdict

The hybrid toggle is offered only when the playing channels (enabled, with a measurement) form a
valid set (`JudgeSideSpatialAverages`). Coverage alone is not enough: seven captures taken at three
frame lengths and two scales cover every channel while putting curves compensated by different
amounts under one offset that fits none of them. The spread warning is a heuristic backstop reading a
median over the working band; the **recipe** is the fact and decides.

**Moving-microphone sets** (`LiveCaptureDocument.JudgeSet`):

- *All or nothing.* A sum mixing averaged and point-measured channels puts two references on one axis
  and looks exactly like a measurement.
- *Recipe must match* (`LiveCaptureRecipe.MatchesSetOf`): the corrections are curves whose shape
  depends on frame length, window and rate together. Two recipes can differ mostly in the bass and
  still land on a similar median, so the spread warning may not notice. `DescribeSetMismatch` names
  the field (for example "32768 vs 65536") rather than just saying the captures disagree.
- *Levels must be comparable:* one analyzer session (one input gain), or every capture carrying an
  absolute SPL anchor.
- *Not compared:* the protective high-pass (it describes the channel's hardware path, a tweeter has
  one and a subwoofer does not, and each capture divides its own out; comparing it rejected a valid
  seven-channel set) and the microphone calibration (each consumer undoes a capture's own correction
  before applying its own).

**Array sets** (`JudgeArraySet`) have far less to agree on: no analyzer recipe and no session, since
the loopback holds the level. They may have gaps — a channel without an array is drawn from its own
point measurement, which is on the same axis (same loopback), and below the cabin's first mode a
point measurement *is* the average (a subwoofer gains almost nothing from an array). Such channels are
always reported (`HybridMagnitudes.PointMeasuredChannels`), never silent. The protective high-pass is
not compared, for the same reason as above (it refused an ordinary four-way set). Array composition
(seven positions vs five) is not judged by the verdict either: it does not stop one offset from
levelling the set, and refusing would hide the view that shows the difference. Virtual DSP warns
about composition separately, across every array in the project, which also catches the cross-side
case.

**Across sides.** The dashed opposite-side hybrid sum borrows the active side's offset on purpose —
levelling sides separately would erase the L/R difference it exists to show. Judging each side on its
own leaves that borrowing unchecked (two relative capture runs are each self-consistent but say
nothing about each other; a gain change between them would draw as an L/R imbalance), so
`CanDrawOppositeHybridSum` / `JudgeSidesShareAnOffset` judge the union of both sides as one set. The
Δ L−R read-out and the audition render use the same check.

## Set offset and spread

Captures and impulse responses are different measurements and their levels may sit tens of dB apart.
One scalar, the **set offset**, puts the whole set on the impulse responses' axis. Because every
capture in a valid set shares recipe and gain, whatever separates the two families separates them by
the same amount in every channel.

- **Datum per channel** (`SpatialAverageOffsets.ChannelDatumDb`): the median of reference minus
  average inside the channel's working band, `WorkingBandDb` = 20 dB below the channel's peak (taken
  over points where both curves exist). Twenty dB holds the working band and crossover skirts while
  staying out of the stopband, where the impulse response shows room and noise while the capture shows
  the filter's analytic slope and the two part by tens of dB.
- **Set offset** = `SpatialAverageOffsets.Median` of the datums — a true median (mean of the central
  pair). Taking the upper central value shifted a four-way set by half the gap between its middle
  channels.
- **Spread** (`HybridMagnitudes.SpreadDb`) = max − min datum. It judges the set's coherence only: the
  offsets stop agreeing when something entered per capture (input gain, frame length or window, mixed
  scale, a capture from another session). It does not claim the captures agree with the impulse
  responses. Per-channel offsets are kept positional (null for "cannot compare") so the warning names
  the right driver; a packed list shifted names onto the wrong drivers when one channel had nothing to
  say.

**Read on the raw pair.** `ResolveRawHybridOffsetsDb` reads each datum on the capture with no chain
against the channel's *bypass* response, built on canonical terms (`BuildCanonicalRawCurve`: own
onset, fixed steady-state window, no calibration, no display smoothing). Reading it on the processed
curves did not cancel the DSP: the impulse response is filtered then gated while the capture is
filtered analytically (a gate does not commute with a filter), and the median band is set by the
channel's peak, which the crossover moves. The whole hybrid set drifted up and down while the user
tuned, and that offset travels to the EQ Wizard. Calibration would cancel, but smoothing does not
commute with subtraction, and the spread threshold was calibrated on these canonical terms
(`HybridOffsetDatumMeasurement` reads them the same way). A channel that cannot produce the raw pair
contributes nothing rather than falling back to processed curves.

**Muted channels count.** The median is over every channel of the side that carries a capture,
muted or not (`HybridMagnitudes.SetDatumsDb`). A mute says what to draw, not what the set is made of;
a median over drawn channels only moved every remaining curve about a quarter of a dB per mute on
real cabins (arrays and MMM alike), and made the spread warning appear and vanish with mute buttons.

**Held without the offset.** Channel curves in `HybridMagnitudes` are stored without the offset,
which is added on the way to the plot (`ShiftedBy`). The drawing and summation read the same arrays,
and a common gain factors straight out of a magnitude sum. A point-measured fallback channel, already
on the impulse responses' axis, is therefore pre-subtracted by the offset. The offset is zero for an
empty set, so captures are drawn at their own level rather than pushed by an invented figure.

## Hybrid channel curve

`SpatialAverageHybrid.BuildChannelCurve` is shared by the Virtual DSP plot and the EQ Wizard, so a
tune is fitted to the curve the panel drew. It is exact, not a convenience: a spatial average is
√⟨|H(f, r)|²⟩ over the volume and a filter D(f) does not depend on position, so
⟨|D·H|²⟩ = |D|²·⟨|H|²⟩. Delay and polarity are pure phase and are absent; this is tonal balance only.

- **Chain added analytically**, not as the difference of two gated spectra: a spatial average is a
  steady-state curve with no window, and gated readings of a filter part by several dB wherever the
  bank rings longer than the window.
- **Rate:** the chain is realized at the channel's *processor* rate (`ProcessorSampleRateFor`), the
  rate the user's DSP runs. The capture's own rate is already folded into its stored levels.
- **Side:** the requested side, never the active one, because the opposite-side sum is built from the
  same function.
- **Bypass:** a bypassed channel uses `DspChannelChain.Identity`, matching the processed response.
- **Gaps:** where the capture has nothing (below a protective high-pass, past its grid) the result is
  NaN. A break says "do not equalize here"; interpolation never bridges or spreads a gap.
- **Order:** smoothing on the finished curve after the chain (smoothing the capture then adding an
  unsmoothed filter left crossover corners razor sharp beside rounded measured curves), then
  calibration last — the app's pipeline order, and the order the EQ Wizard uses when it opens a
  capture directly. Correcting before smoothing smoothed the correction too, so one capture read
  differently depending on the route.
- **Grid snap:** the display grid and the capture's grid are the same log grid built two ways and
  differ in the last ULPs (20.000000000000004 vs 20). Exact bounds dropped the lowest band (20 Hz on a
  subwoofer), and exact index tests let NaN·0 spread a gap one point backwards; a 1e-9 index tolerance
  fixes both (an index step is about 1/100 octave).

**Calibration modes** (`SpatialAverageCalibrationMode`): a nullable curve could not carry the intent,
because null meant both "no correction" and "no single curve to name" (which is what a multi-capsule
capture always looks like), so "as measured" collapsed into "uncalibrated".

- *Off* — the capture's own correction is undone on its own grid before interpolation (corrections
  are additive per frequency and subtracted by the pipeline, so the undo is exact).
- *Own* — the capture as stored.
- *Specific* — a named curve replaces the capture's own, only when the capture declares one
  correction. When `LiveCaptureDocument.CalibrationIsAggregate` is set (array positions corrected by
  different files) no curve can replace the mixture and the request reads as Own.
- `SpatialAverageCalibration.Matches` compares mode first and the curve only when applied; record
  equality compares the curve by reference, and a calibration re-read from its file would spuriously
  refuse a returning tune.

In the panel, `SpatialAverageCalibrationFor` supplies the channel's "Own (as measured)" curve; for a
single-file capture the swap is exact and usually a no-op.

## Hybrid sum

`BuildHybridSumCurve` sums the channels as **phasors**: each channel's gated spectrum is rescaled bin by
bin to its spatial-average level (`DataHelper.GetGatedSubstitutedMagnitudeSum`) and the phasors added.
A spatial average carries no phase, so the phase must come from the impulse response.

The earlier construction added magnitudes and laid the impulse responses' own summation loss on top.
A loss is a property of the levels it was measured at: at a steep junction on a real car the two
families disagreed about two channels' relative levels by 23 dB (a gate does not commute with a
48 dB/oct filter, so a stopband reads far above its analytic slope), and the borrowed loss drew a
13 dB dip into a sum whose channels could not make more than 1.9 dB.

The sum is an **estimate**, unlike the per-channel curves. The phase was measured at one position, so
it draws that point's interference, which may be stronger or weaker than the volume average. The gap
generally grows with how fast relative phase turns across the volume: small in the bass, largest at a
high crossover. Nothing downstream treats the sum as measured.

**Smoothing order.** `HybridMagnitudes` holds both display-smoothed `Channels` (drawn) and
`UnsmoothedChannels` (summed). Smoothing does not commute with the per-bin substitution, and a
fractional-octave window straddling a steep skirt pulls a channel's level toward its passband exactly
at the corners a hybrid is read at. The sum is built unsmoothed, masked, then smoothed once — the
order the measured Sum is built in. Masking before smoothing matters: masked points must not feed their
neighbours' means (`SmoothBandLevels` passes NaN through and excludes it).

**Dropout mask.** `MaskMissingContributors` breaks the sum where a channel has no capture while its
impulse response says it is still playing. A missing capture is ignored only more than
`HybridDropoutFloorDb` = 25 dB below the loudest channel at that frequency, where its own crossover
has removed it. Captures normally stop below the protective high-pass, far under the crossover, so the
floor is rarely reached; when it is, a break is honest, while continuing would sum one set of sources
and present it as the whole.

`BuildHybridMagnitudes` is all-or-nothing per redraw: a channel failing to yield a curve would sum a
spatial average against a point measurement. For efficiency each channel is built unsmoothed once and
smoothed locally, since the shared builder's last step is that same smoothing.

## Level read-outs

In hybrid mode the Δ L−R read-out (`HybridStereoLevelReader`) and the "vs Front" rows
(`HybridGroupLevelReader`) read levels from the spatial averages through their chains. Both follow
the hybrid **intent plus coverage**, not the current Show view (`HybridRequested`): a level that
changed basis when the user glanced at the phase view would read as two imbalances in one tune.

- **Δ L−R** compares across sides, so it requires both sides to form one set (the same check as the
  opposite-side sum). The set offset is common and cancels, so none is applied. A pair with no capture
  on a side, or no overlap in the band, keeps its point-measured level and says so.
- **vs Front** compares groups on the active side only, so the offset cancels and the cross-side check
  would refuse comparisons it has no stake in. Groups are combined by `SpatialAverageHybrid.PowerSum`.
  A group with any member lacking a capture falls back whole (power-summing the rest would understate
  it).
- **Band level** (`SpatialAverageHybrid.BandLevelDeltaDb`) is the mean of point powers over indices
  finite on both curves, converted to dB once — the energy-mean rule of
  `VirtualCrossoverAnalysis.MeasureBandLevelDb`, which tracks loudness and shrugs off narrow dips.
  Both curves are built on one log grid (`HybridLevelGrid`, finer than the captures' ~1/48 octave);
  uniform weights on a log grid reproduce the impulse-response band level's 1/f weighting. Pairing
  points means a gap on either side removes that frequency from both.
- **Power sum.** Each member's curve carries its expected band (`HybridGroupMemberBand`). Outside it a
  NaN is absence (the crossover removed the driver); inside it the capture has nothing to say about a
  playing driver and the group point becomes a gap, since summing the rest would quote part of the
  group as all of it. A finite value counts wherever it sits (a skirt is a real contribution). A
  bypassed member's band is its full measured range, because its idle crossover corners say nothing
  about where it plays; reading them turned "the capture does not know" below an idle high-pass back
  into "the driver is absent". Bypassed members reach grouped read-outs with their raw signal (unlike
  the stereo block, which skips bypassed pairs).
- The power sum is incoherent by necessity: captures carry no phase. In a junction overlap two
  coherent in-phase contributions sum up to 3 dB above their powers; much of that cancels in a
  front-vs-rear difference of the same architecture, little against a single-driver centre. Resolving
  it would borrow one position's phase — what the hybrid mode distrusts — at the cost of full-length
  gated FFTs per group per frame.

## Hybrid toggle

- The tick is **intent** and outlives coverage (like a pinned gate outlives its sources): a set short of
  a capture draws honest curves whether or not the box is ticked, and clearing the tick would make the
  user find it again after re-attaching.
- The hybrid is magnitude-only, so the toggle is **muted, not unticked**, in other views. It is coloured
  by hand rather than through `UiStyle.SetTextEnabledLook`, which memorizes the colour it mutes (this
  toggle wears a reminder colour when live and unticked); AutoCheck and TabStop carry the disabling
  because WinForms' disabled paint is near-black on the dark theme. `hybridAvailable` caches the verdict
  because the Enabled state cannot stand in for it.
- **Live, available and unticked** is shown in the error colour: captures are attached and the plot is
  drawing one position's dips — a tune about to be fitted to the wrong curve.
- The **Groups view is included.** A group line is the same hybrid sum construction over that group's
  members; excluding it made the "vs Front" rows compare groups on the captures while the plot drew them
  on another basis.

## Audition correction

`SpatialAverageAudition` makes a render follow the spatial averages. The correction is per channel and
magnitude-only: `point − (average + setOffset)` read on the **bypass** pair. The chain divides out
exactly (render is raw·D, target is average·D), so the correction survives tuning.

- **Raw pair, again.** Between a processed response and an analytic chain the filter does not cancel;
  at a 1.6 kHz junction on a real car the two readings parted by 23 dB in the stopband, and a
  correction built there would amplify that error into the render.
- **Ungated** on both sides, because the kernel being corrected carries the whole decay and so does the
  average. The processed response's *cropped* source is used (as the EQ Wizard handoff does): the full
  record runs seconds past the arrival at the noise floor and would lift quiet bands.
- **Same estimator.** `PointCurve` reads the bypass response as band power means over bins, the way a
  capture is built, then smoothing, then calibration. Reading it with an interpolating resampler put the
  curves 11 dB apart at 500 Hz on a response with one 5 ms reflection — an artefact the correction would
  have spent most of its range on.
- **Fixed width** `SmoothingOctaves` = 1/6 octave (not the display selector, which is not a property
  of the measurements). Fine enough for tonal balance, coarse enough that a point response's narrow
  interference nulls never enter the difference; inverting a null would demand twenty-odd dB of boost
  and the render would ring.
- **Each measurement through its own calibration.** With one microphone the corrections cancel; with an
  array whose positions carry individual calibrations, reading both raw left the gap between the
  aggregate and the measurement mic's file in the correction, tilting the whole render.
- **Limit** `LimitDb` = ±12 dB, a bound on damage: inside a working band the curves are a few dB apart,
  so it is reached only where the difference is no longer evidence (noise-floor stopbands, or a capture
  the offset does not fit).
- **One set offset** (median of datums, as the plot uses) keeps corrections near zero and within the
  limit; being common it only changes loudness, which is normalized anyway. A channel without a datum
  is left uncorrected rather than corrected by the offset alone.
- **Gaps** are bridged linearly between covered bands and held flat past the ends (`Bridge`,
  `SampleDb`): the FIR is designed from DC to Nyquist and any step in its magnitude rings. Bridging costs
  nothing where the response was never measured (it is zero there).
- **Linear-phase FIR** (`Apply`, via `CalibrationFirFilter.Design`, which realizes the inverse of the
  curve it is given): a minimum-phase design would add channel-specific group delay to the alignment
  being judged. Every channel, including uncorrected ones (flat design = pure delay), gets the same
  length, so the half-kernel delay is common and arrivals stay where the tune put them.
- Each side is rebuilt from corrected channels (`CorrectedSum`) rather than correcting the sum: one
  filter over overlapping channels could only compromise between their averages.

Phase stays the point measurement's, so junction interference in the render is still one position's —
the same honest limit as the hybrid sum.

## Audition render

The "Audition track" command (`VirtualCrossoverPanel.Audition.cs`) produces a headphone-only stereo
auralization of the measured left and right paths at the microphone position (drivers, cabin and
capsule included; not a binaural simulation, and played through the car it would convolve the car
twice).

**Preparation.**

- Both sides are summed from one `processingCoordinator` revision, and staleness is checked first (a
  mid-flight change nulls the second sum, which must not read as a missing side) and again after the
  spatial-average preparation, because the panel stays live behind the wait cursor.
- Half a tune renders both ears from the side that exists; the report warns. `MeasuredSides` returns
  the side flags actually rendered (`[false]` for left-only). Getting the flag backwards is silent in
  the half case: every lookup would read the empty side and report no averages. The list is taken
  before borrowing so a borrowed ear does not enter the spatial-average set twice.
- The corrected pair is built eagerly (UI-thread snapshot, worker compute) so the dialog can say what
  it would do before the user renders. Both ears are judged as one set
  (`JudgeAuditionSpatialAverages`), because per-side levelling would put an L/R imbalance into the
  track. A coherent set that still yields nothing is reported as a measurement mismatch, not as a
  missing file.

**"Own (as measured)" calibration** (`ResolveOwnCalibration`). The panel corrects each channel
separately; a render bakes one filter into sides that are already sums. When every channel was read
through one curve (the ordinary case, including arrays, whose impulse response has one microphone
behind it) the render carries that curve. Otherwise it refuses and lets the user name one, rather than
labelling a render with corrections it does not carry. The refusal is wider than strictly needed (each
ear has its own kernel, so per-side curves would be possible), but the render applies one calibration
filter to both kernels today. A mono pair is counted once. Resolving Own through the app's calibration
list previously returned nothing, rendering uncalibrated while blaming an unreadable file.

**Dialog** (`VirtualCrossoverAuditionDialog`).

- The calibration and the optional cabin subtraction (`CabinTransferFunction`, default sedan) become
  one combined linear-phase FIR in both side kernels: their dB corrections add, costing one convolution
  and one truncation window and half the constant delay. Identical on both sides, so the inter-side
  timing being auditioned shifts by the same constant. The raw render carries the full in-car bass rise
  (+15…+27 dB at 20 Hz), which headphones reproduce as boom the in-car listener never perceives;
  subtracting the typical rise leaves this car's deviation from it audible.
- With cabin subtraction, a reference kernel pair with calibration only is built first and the render is
  level-matched against it, so an A/B between cabin choices differs in tone, not loudness.
- The spatial-average toggle defaults to ticked (a render keeping point dips is the exception), is muted
  by hand rather than disabled, and is remembered only when the choice was real.
- The calibration choice is the panel's, every opening; track, target and cabin are remembered per
  process (not persisted — a stale path from last week is noise), and the source is re-probed on restore.
- **Memory bounds:** a 10-minute duration cap plus a projected-bytes cap, because memory scales with rate
  (ten minutes at 192 kHz is six times ten minutes at 32 kHz). The peak working set is the decoded
  stereo source, its resampled copy and two rendered sides, all float32; only two channels are decoded.
  The budget is checked at pick time on the claimed duration, enforced during decode by a byte cap, and
  re-checked on the actual frame count.
- **Output:** WAV only (a lossy codec would add artefacts to what is auditioned, and Windows does not
  guarantee an MP3 encoder). Written through a temporary file so a cancel never leaves a truncated WAV.
  The target may not be the source. Overwrite consent is tracked per dialog: a restored or newly created
  path is the previous render, and replacing it silently would collapse an A/B pair into just B.
- **Progress:** the one `Progress<T>` is created on the UI thread so reports post in order; every lower
  layer relays synchronously (`SynchronousProgress`).
- The report (`ComposeReport`) puts the result first once there is one: appended last, it scrolled
  below the box on tunes with many channels and a finished render looked unchanged.

## Averaging core

`dsp/SpatialAverage.cs` turns several microphones' curves of one driver into a spatial average and a
per-band spread (`SpatialAverageResult`). All work happens on levels already integrated onto one shared
logarithmic grid (`BuildGrid`), never on FFT bins, because microphones may be read at different
resolutions.

### Shared grid

`BuildGrid` is 1024 log-spaced bands from 20 Hz to 20 kHz — deliberately the grid the rest of the app
already draws on (`ResampleGatedMagnitude`, moving-microphone averages). A private grid would need
resampling at every boundary (plot, overlays, Virtual DSP hybrid, EQ wizard). The step (about 1/103
octave) is finer than anything here is measured at.

### Reading bins onto the grid

`FromTransferMagnitude` computes the band mean of **power**. Neither existing resampler fits:

- `LogarithmicResample` interpolates amplitude across a few bins around each grid point — right for a
  gated, smooth curve, wrong for an ungated response carrying every mode at full resolution, where
  sampling five of sixty bins reports whichever modal notch the point landed in.
- `LogarithmicPowerBandResample` integrates power, right for noise spectra (power grows with bandwidth)
  but wrong for a transfer function, whose level must not depend on band width.

Bands meet at geometric midpoints between grid points. Bins closed by the excitation gate contribute
nothing; a band with no measured bin is NaN (e.g. below the sweep start). A band narrower than the bin
spacing reads the bin it sits in; DC is skipped.

### Levelling to the anchor

`Average` shifts every microphone onto the **anchor** (the measurement microphone, which produced the IR
and carries the SPL calibration), not onto the set mean: a mean would drift with array composition.

`ResolveTrimDb` uses the median of the per-band difference, over bands within `DefaultTrimBandDb` = 20 dB
of the peak where both curves exist. Why:

- A whole-grid trim is measured mostly over noise: a tweeter array holds the driver over two of ten
  octaves; the rest differs by self-noise and preamp gain. 20 dB matches the hybrid's channel offsets.
- Median, not mean: the same driver heard from different places differs by tens of dB at interference
  notches; the median asks where the curves agree.
- Null means no common working band (unplugged, muted, wrong input). Callers must drop that microphone;
  averaging it in would add a dead channel's noise floor. It appears as a null curve, distinct from a
  NaN (silent-here) band.

Trims are kept in the result as a diagnostic: tenths of a dB = matched pair, ~8 dB = sensitivity
difference the calibration files missed, ~40 dB = wrong channel.

#### Cost of trimming before the mean

Trimming first means a power average of levelled positions, not of the field as it stands: positions at
70 and 76 dB average to 74 dB as pressure but 70 dB here. A moving microphone (one capsule, one gain) has
no such need; an array does, since a sensitivity difference is not sound — at the cost of removing genuine
level differences, which one scalar per microphone cannot separate. Measured on two real seven-position
sets: trims −1.4 to +1.6 dB; versus a pure power average the result differs by 0.2–0.4 dB in level
(re-anchored downstream by the raw-IR offset) and 0.14–0.32 dB rms in shape (0.36–1.01 dB at the worst
band). Positions around one head differ far less in broadband level than the arithmetic allows.

### Average and spread

`RmsAverageDb` averages power and returns a level (RMS pressure). Averaging dB would be a geometric mean:
a position in a 25 dB notch pulls as hard as one 25 dB hot. The power mean is also the one a linear filter
factors out of: `⟨|D·H|²⟩ = |D|²·⟨|H|²⟩` for position-independent D, so a predicted DSP chain can be applied
on top of an averaged curve. Counts are per band; a band nobody measured stays NaN.

`SpreadDb` is loudest minus quietest per band — the confidence of the average: near 0 the positions agree;
20 dB means a dip belongs to one seat and is nothing an EQ should fill. It is NaN for fewer than two
microphones: a lone microphone has no spread, and 0 would falsely read as perfect agreement.
