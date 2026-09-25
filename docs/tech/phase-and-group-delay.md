# Phase, group delay and spectral analysis in `DataHelper`

`dsp/DataHelper*.cs` (partial static class `DataHelper`) turns an impulse
measurement (`IImpulseMeasurement`) into frequency-domain curves: magnitude,
wrapped/unwrapped phase, minimum and excess phase, group delay, and the helpers
behind them (gating, frequency-dependent windows, smoothing, resampling).
Related files:

- `dsp/DataHelper.Phase.cs` — gated phase and group-delay analysis, the FDW bank,
  phase unwrapping, minimum/excess phase, group-delay smoothing.
- `dsp/DataHelper.Spectrum.cs`, `dsp/DataHelper.cs`, `dsp/DataHelper.Impulse.cs`,
  `dsp/DataHelper.Resampling.cs` — magnitude spectra, impulse helpers, resampling.
- `dsp/MinimumPhase.cs` — minimum phase from a magnitude spectrum.
- `dsp/Windowing.cs` — Tukey and other analysis windows.

## Gate geometry

The gated phase and group-delay FFTs always run at `GatedFftLength` (32768). The
gate (left shoulder + plateau + right shoulder) is specified in time and
zero-padded to that length, so the frequency grid is identical across gates and
measurements.

`ResolveGatePlacement` is the single place a gate becomes samples: the extraction
starts one left shoulder before the gate offset, the plateau begins at the
offset, and the Tukey shape spans the gate's own length. Every caller that judges
a placement goes through it, so it can never judge a different window from the
one that is used. When the gate does not fit the FFT, the trim is shared between
plateau and fade-out (`FrequencyResponseOptions.TrimGateToFft`, also used by the
non-gated spectrum path) instead of emptying the plateau first. A time
correction downstream keeps all readings absolute (referenced to the IR start).

Phase and group delay use the same extraction with `wrap: true`. The dialog
advertises one gate for both, and phase must remain the mathematical integral of
group delay. A gate whose left shoulder starts before the IR (offset shorter than
the left fade) reads the circular tail: a transfer IR is circular by
construction, and its negative-time content lives there. Zero-padding instead
would silently feed phase and group delay two different signals.

For a peak-referenced phase, the extraction starts `offset` samples past the peak
and a reference of 0 makes `BuildMeasuredPhase` compensate exactly that offset.

## Leading-edge loss guard

`GateLeadingEdgeLossDb` reports how much of a response's energy a gate placement
discards ahead of its plateau, relative to the energy it keeps, in dB: −∞ means
nothing was lost, 0 dB means as much was discarded as kept, +∞ means the window
holds none of the channel.

It decides whether a window may be placed per curve. Two curves gated at
different absolute positions stay comparable only while each window opens before
its own channel's response does. A window is not a time shift: re-referencing an
extraction to a common τ rotates the spectrum, but cannot restore a leading edge
the window removed. The figure looks only ahead of the plateau, because cutting
the decay tail is what a gate is for.

- It is measured on the nominal gate, not on the FDW per-band windows. Those are
  shorter by construction and always truncate, so they answer a different
  question; this one asks whether the placement is sane for the channel.
- Energy before the gate opens counts fully; inside the fade-in it is weighted by
  what the fade removes.
- Energy is read with the extraction's own wrapped addressing. A shoulder before
  the record reads the circular tail, where a transfer IR's negative-time content
  lives; treating it as empty would pass a window full of unseen content.
- If the window keeps nothing (it sits entirely before or after the response) the
  result is +∞. Reporting −∞ instead would make a window that misses the channel
  look like the safest placement. The leading-edge sum alone cannot detect this,
  because a window entirely before the response has nothing ahead of its plateau
  either. Silence is the only case with no response to misplace.

## Group-delay identity

Group delay is computed without differentiating phase, as

τ(f) = Re[T·conj(H)] / |H|²

where `H` is the FFT of the windowed impulse and `T` is the FFT of the same
windowed impulse multiplied by time from the extraction start (the
"time-weighted twin", `TimeWeightSpectrum`). `BuildFixedSpectra` builds both for
the Fixed gate.

The analysis cache (`CachedPhaseSpectrum`) keeps the twin null until a group-delay
reader asks for it: phase views need only `H`, and the twin doubles FFT work and
memory per gate. A phase reader takes the spectrum from any entry; a group-delay
reader that finds a phase-only entry rebuilds the pair (the spectrum comes out
bit-identical) and replaces it, so toggling between the views transforms each gate
at most twice over the life of the impulse. Public accessors
(`GetPhaseAnalysisSpectrum`, `GetGroupDelayAnalysisSpectra`) return copies,
because the arrays are shared cache entries.

## FDW bank

Under the frequency-dependent window (FDW) the window applied at a frequency is
the left shoulder plus `cycles / f`, clamped between a 0.8 ms floor and the full
gate (`FdwEffectiveGateSamples`). Fixed mode is the degenerate one-entry bank
whose window is the full gate up to Nyquist.

- The left shoulder is the immutable temporal anchor. Both the `cycles / f`
  duration and the 0.8 ms floor count analysis time *after* the shoulder.
  Otherwise a long configured fade would shorten the post-arrival window (FDW
  more aggressive than advertised) or consume the whole shortest window and zero
  the direct arrival at its endpoint.
- `FdwGateGeometry` resolves the geometry once per (settings, sample rate), so the
  bank that builds spectra and the smoothing floor that reads them cannot disagree
  about window length at any frequency.
- `FdwBankPlan` places three centres per octave from the first FFT bin to Nyquist.
  Neighbouring centres whose windows round to the same length merge into the
  *last* centre that length is valid for, so interpolation starts at that boundary
  and never shortens the low-frequency window early. A shortest-window entry is
  added at Nyquist if the walk stopped short of it. `DescribeFdwBank` exposes the
  merged plan to tests.
- Each window is transformed (twice, with the twin, for group-delay readers),
  re-referenced to one extraction start (`ReReferenceSpectra`) and stitched by
  complex-linear interpolation over log frequency. `H` and `T` use the same
  blend bin for bin, so the stitched pair is exactly the analysis of the impulse
  through the interpolated window and the group-delay identity still holds.
- Re-referencing the twin shifts its time weight first: a sample at buffer index
  `n` of an extraction starting at `s` carries time `(s + n − s_ref) / fs`, so `T`
  gains `((s − s_ref) / fs) · H` before the phase rotation that re-addresses both.
  Today every bank window extracts at one start (the minimum window still exceeds
  the left shoulder), which makes this a no-op in the bank; the general form is
  kept so the identity does not rest on that coincidence, and
  `SumGatedSpectraPairs` depends on it.

### FDW group delay

`GetGroupDelayCurves` under FDW evaluates the same identity on the stitched bank.
The result at a frequency is the energy-weighted arrival time inside the window
applied there: the direct sound's arrival at mid and high frequencies, and the full
gate's reading where the window is clamped to it at low frequencies. It is *not*
the derivative of the FDW phase curve, whose slope also carries the window's own
change with frequency. Only the window fields of `PhaseAnalysisSettings` are read
(gate offset, shoulders, mode, cycles); detrend, unwrap and phase smoothing belong
to the phase display.

## Superposition

Stitching bank spectra by complex-linear interpolation (`InterpolateSpectrum`) is
deliberate. The FFT is linear, so lerping spectra *is* analysing the IR through
the lerped time window `w = (1−t)·w_lower + t·w_upper`. It is also the only blend
that preserves superposition, FDW(ΣIR) = Σ FDW(IR), which the Virtual DSP
channels-vs-Sum phase view relies on.

An earlier log-magnitude / shortest-arc-phase blend was nonlinear: two channels
whose spectra rotate differently between neighbouring windows interpolated to a
phase tens of degrees away from their vector sum, from the order of operations
alone, so the drawn Sum did not have to match the drawn channels. Where the two
windows genuinely disagree in phase the lerp can pass near zero; that is a real
null of the interpolated window and the reliability gate masks it, rather than an
arc gliding over it with an invented magnitude.

`SumGatedSpectra` adds gated spectra whose extractions started at different
positions (the Virtual DSP Auto gate gates each channel on its own arrival): each
is re-referenced to a target extraction start by a pure per-bin phase rotation
(integer shifts keep a real signal's conjugate symmetry) and accumulated, so the
result reads like one spectrum extracted there. `SumGatedSpectraPairs` does the
same for group-delay pairs, shifting each twin's time weight by the offset before
the rotation. Both operators are linear, so the group delay read off the sum is
the group delay of the summed channels. Both work on the fly without cloning
arrays per channel per redraw.

Re-referencing moves only the time origin. It cannot make spectra comparable whose
windows kept different stretches of their signals; that is the caller's job (see
the leading-edge loss guard).

`ResolveCommonPhaseDetrendMilliseconds` accepts only the reference measurement, so
a multi-curve view reuses one Auto τ for every channel and sum; per-channel
auto-flattening becomes a visible policy error instead of an accidental loop.

## Phase unwrapping and reliability

`BuildMeasuredPhase` returns phase in radians for bins 1..n/2−1, referenced to an
absolute sample. The reference is a common origin: setting it equal across two
captures preserves their relative phase, setting it to a measurement's own arrival
flattens that curve.

### Reliability gates

A bin carries no trustworthy phase when it is more than 30 dB below the local
magnitude envelope (`UnwrapMagnitudeGateDb`, envelope smoothed over one octave),
more than 60 dB below the global maximum (`UnwrapAbsoluteFloorDb`), or, when
coherence is available, has γ² below 0.5 (`UnwrapCoherenceFloor`).

- The gate reads a local, octave-smoothed envelope rather than the global maximum:
  one tall resonance (a subwoofer's cabin peak) must not disqualify a quieter but
  repeatable band tens of dB below it.
- The global backstop still rejects true silence; inside a wide dead band the local
  envelope *is* the noise floor and would otherwise pass itself.
- γ² = 0.5 is where less than half the measured energy is coherent with the
  reference and branch choices become unsafe.
- Coherence is linearly interpolated from a uniform 0..Nyquist array
  (`fftLength/2 + 1`, as produced by `TransferFunction.ComputeAveragedRelativeIr`),
  so its grid need not match the phase FFT. Degenerate input counts as trusted,
  like missing coherence coverage in the plots.

Wrapped output blanks unreliable bins (NaN): a wrapped display has nothing to
bridge with, and the phase of a null, a stop-band or an incoherent band is ±180°
noise that reads as signal.

### Anchored unwrap

Each bin takes the 2π branch closest to the phase predicted from the last reliable
anchor plus a running slope estimate (dφ/df, blended with factor 0.25 so one
jittery bin cannot steer the following branch choices). Unreliable bins get a
branch too but never become anchors, so the unwrap bridges them instead of
accumulating their noise into the tail. With every bin reliable and zero slope this
reduces to the classic nearest-to-previous choice. Before the first reliable bin
the output stays wrapped, so a garbage bin near the bottom of the band cannot
offset the first unwrapped branch by 2π.

A bridge that exceeds *both* limits — 64 bins (`UnwrapMaxBridgeBins`) and 1/3
octave (`UnwrapMaxBridgeOctaves`) — is conceded: its guessed points are blanked
and a fresh wrapped segment starts at the next reliable bin, claiming no branch
relation across the gap. Inside a long gap the turn count is lost (an all-pass
section or a crossover transition can add whole turns no slope extrapolation sees).
Both limits are required: a gap narrow in hertz carries few delay turns
(turns = τ·Δf) and bridges safely however many octaves it spans near DC, and a gap
narrow in octaves is a local feature the running slope handles. A gap still open at
Nyquist is judged the same way.

## Minimum and excess phase

`GetMinimumPhase` derives phase from the windowed magnitude through the Bode
relation (`MinimumPhase.FromMagnitude`); it contains no delay or reflection
component and is unaffected by the τ detrend. `GetExcessPhase` is measured
(always unwrapped, so the difference is continuous) minus minimum phase: the
all-pass part — delay plus reflections — that a minimum-phase equaliser cannot
correct. The τ detrend rides on the measured part, so the excess inherits it.

`EstimatePhaseDetrend` returns two τ estimates in milliseconds that flatten the
excess phase through the displayed window: an energy-weighted slope and the
dominant-arrival peak. They are absolute (from IR sample 0), so one value can be
entered on a second measurement to compare relative phase.

For group delay, the minimum-phase counterpart is built the same way as the
measured curve (`ComputeMinimumPhaseGroupDelayNumerator`): reconstruct
|H|·e^{jφ_min}, return to the time domain and time-weight it. The reconstruction
preserves |H| exactly, so the measured |H|² is the shared denominator and one
validity gate covers all curves, which makes excess = measured − minimum bin-exact.
`h_min` is real by construction (log|H| even, φ_min odd), so the finite FFT's
imaginary residue is dropped. The minimum-phase group delay carries no bulk delay,
so the excess reads as the frequency-dependent arrival time of the all-pass part.
Under FDW the magnitude is the windowed one (the direct sound's at mid and high
frequencies), so "minimum" there is what that magnitude dictates, not the
steady-state response's.

### Minimum-phase reconstruction

A measured transfer function factors into a minimum-phase part, uniquely determined
by the magnitude through the Hilbert (Bode) relation and therefore correctable by a
minimum-phase equaliser, and an excess (all-pass) part from delay, reflections and
other non-minimum-phase behaviour. An all-pass *filter* is itself non-minimum-phase,
which is why the PEQ bank carries one: it moves phase where bells and shelves cannot,
though it still cannot undo a reflection.

`MinimumPhase` uses the standard real-cepstrum construction (as MATLAB `rceps`): take
log|H|, inverse-transform to the real cepstrum, fold the anti-causal half onto the
causal half (DC and, for even N, the Nyquist bin keep weight 1; positive quefrencies
double; negative ones are zeroed), transform forward; the imaginary part is the
minimum phase. All methods are pure and do not mutate their inputs.

Before the logarithm, magnitudes are clamped to `DefaultMagnitudeFloor` = 1e-8
(−160 dB) *relative to the spectrum's own peak*; NaN, infinite and negative bins clamp
to the floor instead of poisoning the cepstrum. The floor must be relative because
minimum phase is invariant to overall gain (a constant in log|H| only moves the zeroth
cepstral coefficient). An absolute floor made a quiet measurement's phase depend on
its level, and that is a real capture case: the H1 transfer scale follows the
microphone and loopback input levels.

## Analysis windows

`Windowing` builds asymmetric Tukey windows for gating and symmetric analysis windows
for spectral FFTs. `SharedAnalysisWindow` keeps a one-entry cache because a live
session rebuilds the same (type, length) window thousands of times (per analysis frame
plus per UI snapshot), each costing `length` trig calls and an allocation next to the
audio pipeline. The cached array is shared and read-only; the public
`CreateAnalysisWindow` returns a copy.

- `EquivalentNoiseBandwidthBins`: ENBW = N·Σw² / (Σw)², the factor by which a windowed
  periodogram over-states broadband noise power relative to tone calibration.
  Rectangular 1.0, Hann ≈ 1.5, Blackman-Harris ≈ 2.0, flat-top ≈ 3.77.
- `MainLobeWidthBins`: full main-lobe width (twice the first-null distance). A band at
  least this wide holds a coherent tone's whole lobe, so summed power / ENBW recovers
  the tone amplitude. Rectangular 2, Hann 4, Blackman-Harris 8, flat-top 10 (SRS
  five-term flat-top, chosen for tone amplitude accuracy).

## Group-delay smoothing

Numerator Re[T·conj(H)] and denominator |H|² are smoothed separately and then
divided. The result is energy-weighted: near-null bins, where the per-bin ratio
legitimately spikes to tens of milliseconds, enter with weight |H|² ≈ 0, so the
curve follows the delay of the dominant energy instead of the singularity.

- A minimal 1/48-octave smoothing (`GroupDelayStabilizationOctaves`) applies even
  with display smoothing off: wide enough to bridge single-bin interference nulls,
  narrow enough to leave the visible curve unchanged elsewhere.
- The smoothing half-width never falls below half the window's spectral resolution
  (`GroupDelayResolutionHalfWidthFactor` = 0.5 of fs / window length, i.e. 1/T).
  Features narrower than that cannot be resolved by the gate, and it is exactly the
  scale of the interference nulls of the longest in-gate reflection. Under Fixed the
  floor is one scalar; under FDW it follows the shrinking window (at 8 cycles without
  clamping it is f/16, about a twelfth of an octave either side, so display
  smoothing finer than that changes nothing above the transition frequency).
- The validity gate compares energy against a local octave-smoothed envelope, like
  the unwrap gate, with the same −60 dB global backstop. Energies are |H|², so the
  dB thresholds divide by 10. Bins with no coherent energy in the smoothing window
  (outside the sweep band, silence, deep local notches) and bins outside the
  measured band are blanked.
- The extraction start is added back, so group delay is absolute from the IR start.

### Anchored Hann smoothing

`SmoothBinsHann` is a Hann-weighted fractional-octave moving average over the linear
FFT bin grid (bin 0 excluded). The kernel is strictly non-negative, unlike the
Lanczos kernel used for display smoothing: the group-delay division needs positive
smoothed energy, and a signed kernel could cancel it near sharp transitions and
reintroduce the spikes being removed. A variant takes a frequency-dependent floor
(`minHalfWidthHzAt`) for FDW; a floor that varies smoothly keeps the result smooth,
which the anchored walk relies on.

Evaluating the average at every bin is quadratic (the kernel widens with frequency):
the naive form ran about 1.1e8 iterations, each with a cosine, over a 32k FFT —
half a second per curve on the UI thread. Instead:

- The average is evaluated exactly on log-spaced anchors, 16 per kernel width
  (`SmoothingAnchorsPerKernel`), never less than one bin apart. At the low end this
  degenerates to exact per-bin evaluation, where the kernel is narrowest anyway.
- Between anchors a chord is accepted only if it matches the exact value at the
  span midpoint within 0.5 % relative (`SmoothingRelativeTolerance`, about
  0.04 dB — far below the 30 dB gate margins); otherwise the span is split, down to
  2 bins (`SmoothingMinimumSpan`).
- The tolerance is floored at 1e-6 of the array peak (`SmoothingScaleFloor`): below
  it every caller has already dropped the bin (−60 dB on amplitude for unwrap, on
  energy for group delay), so precision there buys nothing.

The seeded grid alone is not enough: smoothed does not mean linear. Across a sweep's
band edge or a steep stop-band the average falls exponentially and a chord reads
high — 10 dB high at the band edge, worst where the value is small. The midpoint
probe turns the error bound into an assertion at almost no cost on smooth
stretches. `SmoothBinsHann` is internal so tests can hold it against an exact
reference.

Phase-domain display smoothing (`SmoothPhaseCurve`) decodes the stored smoothing
code through `SpectrumSmoothing`; the psychoacoustic magnitude mode falls back to
its base width, because cubic magnitude averaging is meaningless for a signed phase
trace. Wrapped phase is smoothed as unit phasors (cosine and sine separately, then
`atan2`): averaged as numbers, +170° and −170° meet at 0°, and every wrap grew a
false ramp across the kernel's width. Unwrapped and excess phase are continuous and
are averaged directly.

## Magnitude spectra

`DataHelper.Spectrum.cs` builds the primary magnitude curve: window the impulse,
zero-pad to an oversampled power-of-two length (`GetOversampledLength`; the finer
linear grid feeds the logarithmic resample at low frequencies and improves the
cepstral minimum-phase reconstruction), resample logarithmically, apply
calibration and smoothing. `GetOversampledPrimarySpectrum` exposes the linear
spectrum before the resample so overlays can reproduce any smoothing width exactly.
Harmonic and THD curves do not come from here but from `EssDistortion`, which
normalises every order against the same linear packet.

### Magnitude window anchor

The magnitude window opens at the response's estimated *start*
(`MagnitudeAnchorIndex`), not its peak. A driver's group delay puts the peak
milliseconds behind the front (an archived woofer measurement peaks 5.4 ms after
its onset), so a window whose fade-in ends at the peak discards the direct arrival;
with a left fade of a couple of milliseconds whole octave bands misread by 10+ dB.
The estimate is memoised per IR array, and the peak remains the fallback when the
estimator rejects the record.

A start less than one left fade into the record puts the window's opening before
sample 0. The transfer IR is circular, so the fixed window reads that pre-roll from
the record's end, as the FDW, phase and group-delay gates do; read as zeros, a
1.25 ms loopback-referenced arrival came out 0.3-0.4 dB high below 60 Hz against
the same IR further in, and the FDW curve left the fixed one below its transition.
Only the pre-roll wraps (`wrapPreRoll`): past the record's end stays zero, so a window
longer than an imported record does not read the direct sound twice. The Waterfall does
not wrap at all: its later slices would read the direct sound again.

A composite record (a sum of arrivals) must pass `anchorIndex` = the earliest of its
parts' own starts. On the mixed record the start estimator reads the front of the
dominant band, which a later, louder arrival can own. The Virtual DSP tool anchors
its shared window the same way (`ProcessedChannels.SharedStartAnchorIndex`).

### FDW magnitude

`GetFdwPrimarySpectrum` is a REW-style frequency-dependent window built on the same
bank as the FDW phase analysis, so both views read one analysis and share the
per-impulse cache. The fixed window's fade-in ends at the response start, so the
gate offset is the start time and the configured window is the outer gate FDW never
exceeds: below the transition frequency (where `MagnitudeFdwCycles` periods outgrow
the window) the curve equals the fixed window's; above it the effective window
shrinks as cycles/frequency. `MagnitudeWindowMode` defaults to Fixed, because the
steady-state curve is the canonical magnitude reading (and what in-car SPL targets
are stated against); FDW is the opt-in quasi-anechoic view. Phase defaults the other
way, since an ungated in-car phase trace is unreadable.

`GetGatedPrimarySpectrum` computes magnitude through exactly the phase gate
construction (`PhaseAnalysisSettings`), for callers whose magnitude must read the
window their phase view shows (Virtual DSP).

### Steady-state magnitude window

`FrequencyResponseOptions.SteadyState*` is one steady-state window for every
magnitude curve Virtual DSP and the EQ Wizard draw. It is long enough to show what
the ear hears — tonal balance with the cabin, and an EQ band's full depth at high Q
in the bass (a Q 10 bell at 60 Hz rings for about 100 ms; a short gate reads a
fraction of its gain). It is deliberately not the user's gate: that gate exists to
time junctions and shapes the phase and impulse views, where cutting before the
first reflection is the point.

It is specified in milliseconds so analysed time does not shrink with sample rate,
but it is clamped to `GatedFftLength` samples, so the effective length is
min(682 ms, 32768 samples): 682 ms up to 48 kHz, 341 ms at 96 kHz, 171 ms at
192 kHz (still about 6 Hz resolution).

`FrequencyResponseOptions.TrimGateToFft` is the one place a too-long Tukey geometry
is fitted, shared by the gated carve (`ResolveGatePlacement`) and the plain
oversampled window (`SteadyStateWindowSamples`). The shortfall is shared in
proportion between plateau and fade-out. Trimming only the plateau — what a naive
clamp does — removed the plateau entirely at 192 kHz: the window faded in and
immediately out, weighting the same measurement very differently than at 48 kHz.
The left fade keeps its absolute length (about 2 ms, to avoid a hard edge on the
arrival, which does not scale with the tail). Phase gates are far shorter than the
FFT and pass through untouched.

### Measured-band mask

`Masked` breaks a finished curve where the response carries no measurement, and it
runs *after* smoothing. Masking the oversampled spectrum instead lets the smoothing
window straddle the boundary both ways: on a 1 kHz / 48 dB-per-octave corner,
29 bands below the limit survived on borrowed passband energy while 9 bands above
it were lost to NaN. On the output grid the break lands exactly where the filter put
it. Display and unsmoothed curves are both masked, so a channel that measured
nothing contributes NaN to the summation loss, which skips it.

### Measured sums

`GetGatedMeasuredMagnitudeSumPair` gates each channel, clears its own unmeasured
bins, then adds phasors. Summing the IRs and gating once is arithmetically identical
(one shared window makes the transform linear) but not honest: a channel the sweep
never excited below its corner has an exactly zero spectrum there, and the window
smears its in-band energy across the gap. On two brick-walled bands an octave apart
the total read 1.4 dB above the only measuring channel at 900 Hz and 2.5 dB at
990 Hz — a summation gain the loudspeakers never produced, right at the crossover,
and invisible in the per-channel curves because each is broken there. Where no
channel measured, the total is zero and the caller breaks those frequencies.

Calibration is per channel because the sum is over pressures: Σ HᵢCᵢ, not C·ΣHᵢ.
With one microphone (the normal case) the correction is applied once by the
resample, as for the channel curves, so the summation loss cancels it exactly. With
different microphones it has to go inside the sum; a single correction cannot undo
two microphones, and leaving it out drew a raw total beside corrected channels, the
difference reading as a false summation loss.

The display/unsmoothed pair comes from one gated FFT because the FFT, not the
resample, is the cost, and the summation loss must divide unsmoothed curves
(`VirtualCrossoverAnalysis.SumLossCurve`).

### Substituted magnitude sum

`GetGatedSubstitutedMagnitudeSum` serves the Virtual DSP hybrid view: levels come
from spatial averages (no phase), phase from the impulse responses. Each channel's
gated complex spectrum is rescaled bin by bin to the supplied level (unit phasor
times amplitude) and the phasors are added. The shortcut — add supplied magnitudes
and lay the IRs' own summation loss on top — is wrong wherever the two families
disagree about relative channel levels, since the loss belongs to the levels it was
measured at. On a real car at a 1.6 kHz junction the disagreement reached 23 dB (a
gate does not commute with a steep filter, so a stop-band reads far above its
analytic slope), and the borrowed loss drew a 13 dB dip into a sum whose channels
could not produce more than 1.9 dB. All channels must share the caller's window (the
sum of gated spectra equals the gated sum only then). A NaN level contributes
nothing; whether that is a hole or silence is the caller's decision. Levels are
interpolated on log frequency and never bridge holes; interpolation snaps at the
ends because NaN × 0 is NaN.

### Ungated band levels

`GetUngatedBandLevels` reads an IR the way a steady-state capture of the same source
reads: the whole record, no window, band mean of power on the shared
spatial-average grid (both choices explained at
`SpatialAverage.FromTransferMagnitude`).

- Ungated, because the comparison kernel and a steady-state capture both carry the
  whole decay; a window leaves out the cabin's decay and a difference would read the
  missing energy as disagreement.
- Band mean of power, not the interpolating resampler: an ungated response carries
  every mode at full bin resolution, so sampling a few bins per grid point reports
  whichever modal notch it landed in. With one 5 ms reflection the two estimators
  differ by 11 dB at 500 Hz, which a difference against a capture would spend as
  correction.

The level is relative; smoothing and then calibration belong to the caller, as for a
capture.

## Logarithmic resampling and smoothing

`DataHelper.Resampling.cs` maps linear FFT bins onto the logarithmic display grid.

`LogarithmicResample` interpolates amplitude through a Lanczos kernel, avoiding the
aliasing and jagged traces of nearest-bin lookup. Lanczos weights are signed, so the
sum degenerates when the kernel falls outside the input grid (resampling to 20 kHz
from a spectrum that ends below it); the nearest input sample is held instead of
pinning the point to the −160 dB floor.

The psychoacoustic mode uses a Gaussian cubic mean whose FWHM follows the
frequency-dependent octave width: it gives audible peaks more weight without a hard
lower envelope that would kink smooth valleys. Its kernel keeps the ordinary
resampler's minimum half-width of two FFT bins, measured on whichever side needs
the larger octave radius (equal Hz distances are asymmetric on a log axis). The
lower side is bounded by the input grid (the first sample sits one bin above DC).
Without that bound the lower octave distance diverges as the centre approaches two
bins from DC, the Gaussian covers the whole spectrum, and the cubic mean draws the
loud midrange as a spike: a 192 kHz measurement with a 2048-sample window spiked
+33 dB at 47 Hz.

### RTA power bands

`LogarithmicPowerBandResample` produces band levels for an RTA shown in absolute
units. Unlike `LogarithmicResample`, which averages amplitude (right for relative or
transfer traces), it integrates *power* over each band, so a broadband level does
not move with FFT length: summed bin power in a fixed band is invariant to N.

- Power is divided by the window's equivalent noise bandwidth (ENBW), so a noise
  band reads its true power rather than the coherent-gain (tone) over-estimate, while
  a bin-centred full-scale tone under a rectangular window still reads its
  calibrated level.
- Each bin owns [(k−½)·Δf, (k+½)·Δf] and contributes only the fraction overlapping
  the band, so the level is continuous as bins cross band edges.
- The integration band is a fixed 1/12-octave reference resolution, widened to the
  window's main lobe (`mainLobeBins·Fs/N`, not the narrower ENBW) or the grid cell
  where those are coarser. It is *not* the display smoothing: band power grows with
  bandwidth, and tying it to smoothing raised a quiet spectrum by many dB at high
  frequencies.
- N-invariance holds only where the 1/12-octave band is wider than the main lobe.
  Below that crossover (low frequency, a long window such as Flat Top, or a short
  FFT) the band is floored to the main lobe, which shrinks with N, so broadband level
  drops about 3 dB per doubling of N. That is the resolution limit of any single-FFT
  RTA and the cost of the floor; without it a coherent tone there would read low.
- The grid is bounded so every band, both its octave width and resolution width, lies
  wholly inside [first bin low edge, last bin high edge]. A band spilling past Nyquist
  or below the first bin would integrate half-empty and show a false roll-off on a
  flat input; nothing above the last bin is synthesised.
- Display smoothing runs afterwards as a level-preserving moving mean of linear band
  powers over the requested fractional octave. A mean leaves flat or sloped levels
  unchanged, and averaging power rather than dB keeps a silent neighbour at zero
  power instead of −160 dB, so it cannot swamp a nearby tone.

The result is relative dB (10·log10 band power); the caller adds microphone
calibration and the SPL offset.

`SmoothBandLevels` replays that second pass over a stored dB SPL RTA curve (an overlay
slot, and through it the EQ Wizard), which has no raw spectrum to return to. It shares
`SmoothBandPowersToAmplitudes` with the live resampler — same window, same
psychoacoustic cubic mean — so "1/6 octave" means the same in both places. Non-finite
levels mark unmeasured bands: they pass through and are excluded from neighbours'
means (prefix sums carry a count), so gaps neither spread nor fill. Widths below one
grid step leave levels untouched, as the resampler does.

### Ratio smoothing

`SmoothRatioLevels` smooths a ratio curve already on the display grid (a per-point dB
difference such as the Virtual DSP summation loss) with an arithmetic mean of
decibels. A ratio is not a level: the magnitude paths average power and weight peaks
with a cubic mean, which on a ratio biases toward whichever side of the window is
nearer 0 dB. The smoothing must also run on the finished ratio, not on the two
operands before division (see `VirtualCrossoverAnalysis.SumLossCurve`). The
psychoacoustic mode keeps its frequency-dependent width and Gaussian kernel (tapered
to zero at three sigma), dropping only the magnitude weighting. Non-finite points are
skipped, as in `SmoothBandLevels`.

## Impulse view traces

`DataHelper.Impulse.cs` (`GetImpulseCurves`) builds the impulse, envelope and step
traces over the whole record on its own timeline, so zooming to the tail or to a
sample is a gesture, not a settings change. All framing is view-only
(`ImpulseRenderFrame`): the record is never rewritten, because Time Alignment, the
Virtual DSP gate pin and saved offsets all refer to its absolute timeline.

- Every curve on a plot is normalised against one reference peak; normalising each
  to itself would make two records 4 dB apart read identical. A Compare set passes
  the main set's peak.
- The SNR figure is read against the *envelope* peak, not the sample peak (the
  analytic magnitude rides above the samples), because Time Alignment grades the
  record against its envelope peak and the two figures must match. It exists only
  when the envelope is drawn, since the envelope costs a transform.
- A band filter (`BandFilterOctaves`, zero-phase mask with a fade skirt of half the
  pass width, as the Time Alignment probe uses) replaces the source signal, so peak,
  reference and SNR describe the band. A band is realisable only if its whole
  octave-symmetric passband fits under Nyquist (one octave at 16 kHz needs 22.6 kHz);
  the fade skirt may clip.
- The band mask and the Hilbert transform treat the buffer as circular, which is
  correct because the record is one period of the deconvolution. Padding the record
  was measured on archived cabins and was worse: the abrupt record end against the
  padding is an edge the transform spreads back inside, lifting the numerically
  silent region from about 1e-13 to 2e-9 and inflating the noise-floor estimate.
  Unpadded, the envelope tracks |x| by a constant ratio throughout.
- Envelope smoothing is a centred moving average (prefix sums, so wide windows cost
  the same): a trailing average would slide every reflection later by half its
  window and falsify arrivals.
- The step response is always emitted normalised for its own axis (divided by the
  reference impulse peak, or by its own peak when `NormalizeStepToImpulsePeak` is off). Sharing the
  impulse's units fails on real records: DC or low-frequency content integrates into
  a step many times the impulse peak (a synthetic cabin IR reached 1000 %), flattening
  the impulse; and dB cannot hold a signed quantity crossing zero.
- Autocorrelation uses Wiener-Khinchin (zero-pad the mean-removed signal to twice its
  length so lags cannot wrap), O(n log n). Sub-sample values come from a 4-tap Lanczos
  read of the correlation, which equals interpolating the signal first because
  correlation is linear in the shifted signal.
