# ESS harmonic analysis

`dsp/EssHarmonicAnalysis.cs` separates a deconvolved exponential-sine-sweep (ESS) impulse response into
its linear packet (order 1) and harmonic distortion packets (orders 2..`MaxHarmonic`). It is pure DSP:
calibration, smoothing, display and loopback-transfer division belong to the consumers
(`EssDistortion`, the distortion panel). Sweep geometry lives in `EssSweepMetadata`; every harmonic
position is derived from it, never from constants scattered elsewhere.

## Packet geometry

- The time advance of harmonic *n* for a logarithmic sweep is `Δt = L · ln(n) / ln(f2/f1)`
  (`HarmonicTimeOffsetSeconds`). It depends only on the sweep, never on level.
- `HarmonicOffsetSamples` accepts fractional orders, so packet centres and boundaries come from one formula:
  `BuildWindow` places edges at the geometric means `√(n·(n±1))`, the energy probe's flanks at `n ± 0.5`.
- `BuildWindow` gives each order its own window, from the boundary toward order+1 (earlier) to the
  boundary toward order−1 (later). Order 1 has no lower neighbour, so its later edge mirrors the earlier
  one, centring the linear window on the peak.
- Per-order windows, not one shared HD2..HD5 window: a shared window forces THD to sum packets
  complex-wise, which makes it depend on their relative phase.
- The app's sweep always ends at Nyquist and spans `octaves` downward, so `f1 = Nyquist / 2^octaves`
  (`EssSweepMetadata.FromExponentialSweep`). Harmonic *n* is observable only up to
  `min(sweep end, Nyquist / n)` (`MaxExcitationHz`).

## Spectrum normalization

A packet is a contained impulse response isolated by a unity-plateau window. Over the plateau the window
is 1, so the windowed FFT equals the packet's DFT and the raw magnitude *is* the transfer magnitude
(`WindowedSpectrum.AmplitudeAt`). No coherent-gain division is applied: that is the normalization for
*tones* and would make |Hn|/|H1| depend on the two windows' lengths. With raw plateau magnitudes the
ratio is independent of window length, shape and zero-pad factor. `WindowCoherentGain` is kept for
diagnostics only. A window longer than the common FFT is truncated around the peak (does not occur
for the app's sweeps).

## Overlap classification

`EvaluatePacketOverlap` compares residual energy in each edge region (15 % of the window at each side,
`EdgeRegionFraction`) with the packet peak. The leading edge faces the higher harmonic (earlier), the
trailing edge the packet's own decay (later). Slowly decaying packets (bass, short sweeps, car cabins)
still carry energy at the edge and leak into their neighbour.

- Edge below `ReliableEdgeDb` (−40 dB): well isolated.
- Between the two margins: drawn with a marginal-isolation warning. This is the common case on real
  captures, where a practical sweep does not fully isolate the high harmonics.
- Above the invalid margin: a neighbour swamps the packet; the order is dropped and excluded from THD.
  The drop margin is deliberately lenient so real HD3/HD4 curves survive, caveated, instead of vanishing.

The linear packet is not checked: its later edge is room decay, not a harmonic neighbour.

## Below-noise classification

The edge test is relative to the packet's own peak, so on its own it cannot tell a leaking packet from
no packet. A harmonic below the noise floor leaves a window of flat noise whose maximum
(σ·√(2 ln M) over M samples, about 3–4 σ for the lengths in play) reads 10–12 dB above the edge RMS —
inside the "overlaps its neighbour" verdict.

An order is therefore classified *below noise* (`HarmonicPacketValidity.IsBelowNoiseFloor`) when both hold:

1. The maximum over the **whole** window (not only the plateau, so a packet hiding in a shoulder counts)
   stays within `BelowNoiseWindowPeakDb` = 16 dB of the tail-noise RMS. 16 dB covers the noise crest
   factor up to M ≈ 10^7 samples; a genuine leak with a visible curve sits far above the floor.
2. Both edge RMS values stay within `BelowNoiseEdgeMarginDb` = 6 dB of the tail noise. An edge RMS is
   averaged over hundreds of samples and sits tight on the true noise RMS, so a modest margin suffices,
   and it catches low-crest coherent contamination (a neighbour's tail, the linear skirt) whose maximum
   stays under the 16 dB ceiling.

If either fails the order keeps the warned overlap verdict rather than being blessed as clean. A
below-noise order is still not drawable (its curve would be the noise floor) but carries no warning:
an unresolvably small harmonic is the mark of a clean capture.

The tail noise (`EstimateTailNoiseAmplitude`) is read from the quiet region after the linear packet and
its reverb guard, the same region `EssNoise` uses. It is split into `TailNoiseChunkCount` = 8 chunks whose
RMS values are combined by median, so one stray thump cannot inflate it. A tail whose chunks would be
shorter than `MinTailNoiseChunkLength` = 128 samples returns 0 and the below-noise test is skipped.

## Harmonic energy probe

`MeasureHarmonicEnergy` returns one channel's harmonic content relative to the linear packet
(`EssHarmonicEnergy`). Typical readings: a wired electrical path far below −40 dB; a loudspeaker through
air tens of dB below linear; an input stage driven past its limit within about 15 dB.

**Detection.** Equal-width probes sit at each packet centre and at the half-order boundaries on both
sides, where no harmonic can live. One radius serves every probe (the ratio only means something over
equal widths): `PacketProbeSeconds` = 1 ms, capped at a third of the tightest spacing, which is between the
highest order and its upper boundary. A packet counts only if it stands `PacketAboveFloorDb` = 6 dB above
the louder of its two flanks. The floor is local per order because the between-packet residue is not
stationary; one quiet stretch must not license every other order. The threshold serves detection only.

**Ceiling.** `CeilingDb` sums, with nothing subtracted, the energy of each judged order's whole isolation
window (the same window `AnalyzeEssHarmonics` uses). A probe-sized ceiling certified clean a −20 dB
harmonic sitting one sample past the probe while deep inside its window; harmonic IRs have time extent.
The flanks bound the background beside a packet, never inside, so subtracting them would be wrong.
A perfectly quiet, fully covered record reads −∞.

**Coverage.** The deconvolution is a linear, not circular, convolution: nothing exists before index 0, so
`RangeEnergy` refuses out-of-range reads. Orders are read upward; the upper boundary is the farthest probe
from the peak, so once it runs off the record front no higher order fits. `CompleteCoverage` is false
when that happened, and the ceiling speaks only for the orders read — an unread order may even have its
packet inside the record.

**Certifying clean** requires a below-threshold ceiling *and* complete coverage; "nothing detected" alone
never means clean. A null result means no verdict at all (for example second-order probes do not fit, the
probe radius rounds to zero, or the linear packet is empty or non-finite).
