# Prepared DSP chain response

`dsp/PreparedDspResponse.cs` builds a `DspChannelChain` (biquads, optional FIR stage, gain, polarity,
delay) once and reuses it for plot curves and for filtering FFT spectra of measured records.

## Processor rate vs record rate

Coefficients are designed at the **processor's** rate, the rate the simulated hardware runs its biquads at,
never at the measurement rate. The bilinear transform warps corners by the design rate, so a chain built at
the measurement rate is a different filter: an LR4 low-pass at 8 kHz designed at 48 kHz sits 1.5 dB below the
96 kHz design at 10 kHz and 4.1 dB at 12 kHz.

`ApplyToSpectrum` takes the record's rate separately. Bin *i* at `i·fs_record/N` Hz is
`ω = 2π·i·fs_record/(N·fs_processor)` on the processor's unit circle, so a 48 kHz record reads a 96 kHz chain
across the lower half of the circle — exactly the band the record carries. This is exact: an LTI chain
invents no frequencies. Delay is a time and is expressed in record samples for the phase ramp.

Bins above the processor's Nyquist (record rate > processor rate) are zeroed
(`SilenceAboveProcessorNyquist`), not filtered by the periodic continuation of H, which no device produces.
This also makes the same setup measured at 96 and 192 kHz simulate alike. For the same reason
`CanScaleInTimeDomain` refuses the scalar shortcut when the record rate exceeds the processor rate: a bypassed
channel would otherwise keep ultrasonics its filtered neighbours lose, and the channels would sum and be timed
against different bandwidths.

The record's Nyquist bin has no conjugate partner and is scaled by the real part of the response (dropping a
fraction of the top bin).

## Tail padding

`RequiredTailSamples` sizes zero-padding so ringing decays by the target before a circular FFT wraps it into
the head. It follows the slowest biquad pole: a 20 Hz, Q 10 peaking filter rings for hundreds of
milliseconds, far past a pad sized for crossovers. Unstable sections (pole radius ≥ 1) get the maximum.

- `BiquadCoefficients` uses the **additive** feedback convention, `y[n] = … + A1·y[n−1] + A2·y[n−2]`
  (denominator `1 − A1·z⁻¹ − A2·z⁻²`), so poles are roots of `z² − A1·z − A2`. Textbook
  `1 + a1·z⁻¹ + a2·z⁻²` formulas read ordinary stable sections as unstable and pinned padding at the max.
  For complex conjugate poles `|p|² = −A2`.
- The pole radius is a per-sample decay at the processor rate; the count is converted to record samples
  before clamping, since ringing lasts a fixed time.
- A FIR stage adds N − 1 samples at the processor rate, converted to record samples as
  `ceil((N − 1) · recordRate / processorRate)` — about (N − 1)/2 for a 48 kHz record through a 96 kHz
  processor, about 2·(N − 1) the other way. The tail sits **outside** the clamp: there is no decay to wait for and a
  cap would wrap the kernel tail into the head. Kernel length is bounded at load (`FirFilter.MaximumTaps`).
  No safety sample is added: the caller rounds to a power of two, and one sample over doubles the render.

## Group delay

`GroupDelayMs` sums the closed-form biquad group delay (`BiquadResponse.GroupDelaySamples`) plus bulk delay;
gain and polarity add nothing. A secant of `-Im(H'/H)` never wraps but approximates H rather than φ and
flattens sharp peaks (a Q-20 all-pass near Nyquist read 1.4 ms against a true 127 ms), and would disagree with
readouts sharing the helper. The FIR stage adds `FirFilter.GroupDelaySamples`, NaN at a true kernel null,
drawn as a gap.

## FIR bins

`FirSpectrumBins` computes the FIR response at every record bin on the processor's circle:

- **Fast path:** when `length / rateRatio` is a whole number M (equal rates, 48 kHz record through a 96 kHz
  processor, or the reverse), the M-point DFT of the zero-padded kernel lands exactly on the record's bins.
- **Otherwise** (44.1 vs 48 kHz) the chirp-z transform (`FirFilter.ChirpSpectrum`) reads the kernel at each bin
  in three FFTs, instead of taps × bins multiplies (seventeen billion for the longest kernel and render).
- **Cache:** bins depend only on kernel, record length and rate pair, so every knob turn reuses them. The table
  is weakly keyed on the kernel, locked per kernel (parallel channels wait for one computation), and holds a
  few (length, rate) entries since one kernel can sit on channels with different record lengths.
