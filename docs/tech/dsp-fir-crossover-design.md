# FIR crossover design

`dsp/FirCrossoverDesign.cs` holds the parameters of a linear-phase crossover kernel (`FirCrossoverDesign`)
and designs it (`Build`). The FIR Constructor edits this record, and a session stores it beside the kernel so
the kernel can be reopened as a crossover rather than a list of taps.

## Kernel shape

- Every kernel is **symmetric with an odd tap count**. Symmetry makes it linear-phase (a pure delay of
  `LatencySamples = (N−1)/2` times a real zero-phase response). Odd length is required: an even symmetric
  kernel has a forced zero at Nyquist (no high-pass possible) and a delay half a sample off the grid.
- The taps are the filter only at `SampleRateHz`. A processor at another rate convolves the same numbers as a
  different filter, so a session compares rates and flags the kernel for rebuild.
- `MaximumTapCount` = 16383 (2^14 − 1, the largest odd count a 16k-tap stage holds). The limit is about
  latency (half the length: 171 ms at 48 kHz) and binds the constructor only; imports use
  `FirFilter.MaximumTaps`.

## Methods

- `FirCrossoverMethod.IirMagnitude`: the magnitude of an IIR slope (family, corner, steepness) with the phase
  discarded. A Linkwitz-Riley pair sums flat in magnitude like its IIR original, without the phase turn.
- `FirCrossoverMethod.WindowedSinc`: an ideal brick wall truncated by the window; window and length alone set
  the slope. It has no target shape (`HasTargetMagnitude` is false).

Families: Chebyshev is excluded (its ripple is an extra parameter, and a rippled passband defeats the point of a
linear-phase design). Slopes go steeper than hardware (`CrossoverFilter.SupportedSlopes`) because the kernel
needs only the magnitude: Linkwitz-Riley in 12 dB steps and Butterworth in 6 dB steps up to 96 dB/oct. Bessel
keeps the hardware list since its prototype table ends at 48 dB/oct.

## Complementary sum

The window multiplies an ideal zero-phase response and every window is exactly 1 at the centre. A low-pass and
high-pass with the same corner, length and window therefore sum to a pure delay whenever their ideal responses
add to a unit impulse: always for the windowed sinc (high-pass = impulse − low-pass), and for Linkwitz-Riley
under `IirMagnitude` (magnitudes add to one). Butterworth pairs add in power and sum with a bump. For the same
reason `Build` does no gain renormalization — scaling would break the sum.

`WindowValue` stretches each window over c + 1 rather than c so the ends stop just short of zero instead of
wasting the outermost taps.

## Building the ideal response

- Windowed sinc: analytic, `2·fc/fs · sinc(2·fc·n/fs)` per corner; high-pass alone is a unit impulse minus the
  low-pass.
- IIR magnitude: the magnitude is sampled on `MagnitudeGridLength` points (a power of two, at least 16 kernel
  lengths and at least 2^18) and inverse-transformed (Matlab scaling divides by the grid length, so the result
  is the impulse whose DFT is the magnitude). A steep low corner rings long, and whatever runs past half the
  grid folds back into the kernel; at 2^18 the fold starts 1.36 s out at 96 kHz, past the ring of a 48 dB/oct
  corner at 20 Hz.

`EdgeMagnitude` evaluates Butterworth and Linkwitz-Riley in closed form. The crossover biquads are the bilinear
transform of the analog prototype prewarped at the corner, so `|B|² = 1/(1 + r^(2n))` with
`r = tan(πf/fs)/tan(πfc/fs)`, and LR of order 2n is that Butterworth squared, `|LR| = 1/(1 + r^(2n))`. This
allows orders no section list carries. The high-pass uses the inverted ratio rather than `r^(2n)/(1 + r^(2n))`,
which becomes ∞/∞ once a 96 dB/oct ratio overflows. Bessel has no closed form and is read off its sections,
built once per design rather than per bin (half the grid, at least 131 073 bins).

`WorstDeviationDb` compares a kernel with the target only where the target is above a floor (−30 dB default):
below it the target heads to −∞ and any finite kernel misses by an unbounded, meaningless amount.
