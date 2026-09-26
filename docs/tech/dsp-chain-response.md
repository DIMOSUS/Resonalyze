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

`FirFilter.RecordBins` computes the FIR response at every record bin on the processor's circle:

- **Fast path:** when `length / rateRatio` is a whole number M no shorter than the kernel (equal rates, 48 kHz
  record through a 96 kHz processor, or the reverse), the M-point DFT of the zero-padded kernel lands exactly on the record's bins.
- **Otherwise** (44.1 vs 48 kHz) the chirp-z transform (`FirFilter.ChirpSpectrum`) reads the kernel at each bin
  in three FFTs, instead of taps × bins multiplies (seventeen billion for the longest kernel and render).
- **Cache:** bins depend only on kernel, record length and rate pair, so every knob turn reuses them. They are
  kept on the kernel itself, a few (length, rate) entries since one kernel can sit on channels with different
  record lengths, each computed once by its first reader: parallel channels wait for that one computation, and a
  read of another entry does not wait at all.

## FIR on a plotted grid

The chain plot, the hybrid view, the level read-outs and the EQ Wizard's corrected curve and target read a chain
at a list of frequencies, redrawn on every knob turn. The FIR stage is a Horner sum over every tap at each point:
22 ms for a 16,383-tap kernel on the chain plot's 512 points, 447 ms for a 131,071-tap one on a 2,000-point
hybrid grid, per channel and frame. Only the gain, delay and biquads change with a knob, so
`FirFilter.Responses` and `FirFilter.GroupDelaysSamples` keep the kernel's values per (rate, grid) on the kernel
itself, and `PreparedDspResponse.Responses`/`GroupDelaysMs` multiply them into the per-point IIR read.

- **Identical by construction:** a cached value is the same `Response(frequency, rate)` call a point read makes,
  and grids match bit for bit, so a cached curve equals the point-by-point one to the last bit
  (`FirGridResponseTests`).
- **Bounded:** sixteen grids per kernel, least recently used first (one kernel can feed the chain plot, both
  sides' hybrid grids, the level bands and the EQ Wizard's target at once); a new kernel (a load, a FIR
  Constructor edit) starts empty and the old one's values go with it.
- **Computed once, never waited on by another grid:** as with the bins, only the lookup is locked. A second reader
  of a grid waits for its one computation; the chain plot on the UI thread never waits for a hybrid build's grid.
