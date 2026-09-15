# Audiotec-Fischer phase control

`dsp/PhaseRotationControl.cs` models the channel PHASE control of Audiotec-Fischer processors (HELIX / MATCH /
BRAX). `PhaseRotationSpec` holds one setting; `PhaseRotationControl.Realize` turns it into an `AllPassSpec`.

## What the control is

The user chooses an **angle**, not a corner. The device places a single second-order all-pass with Q = 1 so that
its phase at the channel's crossover frequency equals the dialled angle. Angle and reference frequency must
travel together: the same 90° is a different filter on a channel crossed elsewhere. This is what separates it
from a PEQ band and from `PeqBandType.AllPassSecondOrder`.

`PhaseRotationSpec.ReferenceIsLowPass` records which corner the reference is (the low-pass on a subwoofer
channel, the high-pass otherwise). A search moving one corner must know whether it moves the reference, and a
chain carries only the corners its kind engages.

## Measured law

Measured on a DSP ULTRA S over sixty-odd electrical sweeps (issue #88): nine ratio curves fit one RBJ all-pass
at 0.11–0.17° rms with Q = 1.0000 and magnitude flat to 0.02 dB. Fitted corners match "phase = setting at the
crossover" to 0.2 % (7961 Hz measured vs 7977 solved for 90° at a 5 kHz reference; 4999 vs 5000 for 180°; 3109
vs 3107 for 270°). Q = 1.0000 held on six independent curves across the range (45°, 270°, 354.375° and two
steps of a 500 Hz block). The manufacturer's knowledge base states the rule, the grid and the range.

Two measured facts are not in the documentation:

- **Reference is the crossover as configured, not as active.** Bypass and slope = OFF keep the phase reference,
  so reading the live crossover gives the wrong corner on every channel whose filter is off.
- **The corner is capped** (`MaximumCornerFraction`). At a high crossover the smallest steps collapse onto one
  filter and deliver an angle not even on the control's grid. `DeliveredDegrees` exposes this to the user.

Everything is solved at the processor's rate: 90° at a 5 kHz reference is a 7977 Hz corner at 96 kHz and
7674 Hz at 48 kHz.

## Grid and range

`StepDegrees` = 360/64 = 5.625°. Confirmed where nothing is capped: one step read −5.54° at a 500 Hz reference,
two steps −11.29°. But that bench block was a **subwoofer** channel; the mid/high block only captured 180°, which
fits both a 5.625° and an 11.25° grid. Older tool generations reportedly step mid/high by 11.25°. The grid is
therefore measured for subwoofers and assumed for others; a coarser device or channel type would need the grid
to become a per-device property. `MaximumDegrees` = 354.375° (360° equals 0°). The DSP accepts any in-range angle
so hand-edited projects still open; the editors snap via `SnapToGrid`.

## Corner ceiling

Measured only at 96 kHz: three recoveries at two reference frequencies put the ceiling at 18007–18011 Hz, which is
both 3/16 of the rate and an absolute 18 kHz within the spread. The library takes the rate-relative reading,
since the corner is placed in the digital domain and one coefficient generator serving both device generations
would naturally clamp there. On 96 kHz units the two readings coincide; the choice matters only on 48 kHz models
at high crossovers. If a 48 kHz unit is measured, `MaximumCornerFraction` is the line to correct. Whether the
ceiling is deliberate or a firmware defect is unknown; it is modelled because the hardware does it.

## Solver

`RotationAt` returns the lag at the reference in (0, 360), decreasing monotonically with the corner. The arctangent
folds the section's full 360° turn into (−180, 180], so it is unfolded or the solver would see a jump. `SolveCornerHz`
bisects on log corner (the curve is smooth and monotone, checked against the closed form at 2000 points); a hundred
halvings resolve well inside the bench's 0.2 %. The lower bracket is reference/64, where nearly the whole turn has
happened. If even the maximum corner turns further than requested, the setting is capped at that corner.
