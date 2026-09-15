# Typical cabin transfer function

`dsp/CabinTransferFunction.cs` provides a **typical** cabin gain curve per body style (`CabinBodyStyle`), used to
subtract the envelope of a car's bass rise in audition.

## What it models

Below a corner the cabin behaves like a sealed box around the listener and gain climbs at a constant dB/octave.
Published figures reach +17 dB at 20 Hz for an averaged sedan, +23.5 dB for a hatchback and +27 dB for a compact
sedan with the enclosure coupled straight into the cabin. It is deliberately **not** a target curve: targets sit
far lower because the subwoofer's own roll-off eats most of the rise.

Only the idealized envelope is modelled. Modal peaks and nulls stay in the audition on purpose, so subtracting the
envelope leaves this car's deviation audible.

## Parametric shape

Most presets are two parameters: corner and slope (`FromBodyStyle`).

- The corner is the knee **centre**, near the pressure-zone onset c/2L for the cabin's longest dimension (lower for
  long SUV cabins). A soft knee leaves a couple of dB at the corner and a fraction of a dB an octave above.
- The slope tracks how directly the source couples into how sealed a volume: 12 dB/oct in theory for a tight cabin,
  nearer 9 measured on leaky trunk-isolated sedans.
- The knee is a softplus in octaves, written overflow-safe (exp only sees non-positive arguments).
  `KneeSharpness` = 3 reproduces the published averaged-sedan table (17/8/3/1.3 dB at 20/40/63/80 Hz for a 70 Hz
  corner) within about 0.5 dB.

## Tabulated presets

A trunk coupled through an opening (e.g. `BmwF30SkiHatch`) does not fit the parametric shape: the trunk-plus-hatch
path acts as an acoustic low-pass and the curve breaks over a steep cliff near the corner. Such presets are
piecewise-linear in log frequency over measured anchors (`Tabulated`): 0 above the last point, the first anchor's
value held below the first. The F30 table was read off an owner measurement (driver's seat vs nearfield at the sub,
normalized to the 80–150 Hz shelf): about 11 dB/oct below 50 Hz, then about 30 dB/oct where the hatch coupling gives
out.

## Infrasonic limits

- `MaximumSubtractionDb` = 40 dB caps the rise. Below the audible band the slope would run to tens of dB by DC — an
  unmeasured notch whose only audible effect is deep, long infrasonic ringing in the correction FIR. 40 dB sits above
  every preset's deepest anchor (F30: +34 dB at 20 Hz), so only the extrapolated tail is clipped.
- `MinimumFrequencyHz` = 1 Hz: the FIR design samples the 0 Hz bin, where a log-frequency slope is undefined.
