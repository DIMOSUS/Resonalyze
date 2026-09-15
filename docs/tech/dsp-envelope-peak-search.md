# Envelope first-arrival search

`dsp/SignalEnvelope.cs` computes Hilbert (analytic-signal) envelopes and finds the first arrival in an
impulse-response envelope (`FindPeak`, returning `PeakSearchResult`). Consumers include the Time Alignment
and Auto-delay paths.

## Envelope

`AnalyticMagnitude` keeps DC and Nyquist, doubles positive frequencies, drops negative ones and transforms
back. `EnvelopeFromSpectrum` exists for callers that already hold the forward spectrum at the target
length (a band-limited read that also wants the whitened correlation and the band mask's ringing from the
same spectrum), saving an inverse and a forward transform of the whole record.

## Search anchor on chain latency

A chain with real processing latency longer than the search window (a field chain ran about 163 ms) parks
the whole IR beyond a start-anchored window, whose "strongest peak" is then leading residue — a confident
zero. `FindSearchAnchorRotation` then re-anchors on the envelope's global maximum and searches a rotated
view of the circular buffer; indices are mapped back.

- The peak is placed at the window's far usable index, not its centre, so nearly all of the window is
  pre-history and a direct arrival up to a full window ahead of a stronger room mode remains findable. The
  first-arrival walk only looks before the strongest peak, and post-peak data for the mirror checks stays
  reachable through the rotated view.
- It fires only when nothing in the start-anchored window sits both within the first-arrival search depth
  of the global peak **and** above the noise gate. Depth alone fails on near-noise records, where window
  noise bumps can sit within the depth of a weak global peak. Records whose front is in reach keep the
  start-anchored geometry bit for bit.
- `PeakSearchResult.SearchRotation` is non-zero after a re-anchor; distances between returned indices must
  be measured as `(index - SearchRotation) mod length`, or a pair straddling the seam reads a buffer length
  apart.

## Noise floor

`EstimateEnvelopeNoiseRms` is the RMS of the quietest 25 % of envelope samples (`NoiseFloorQuantile`). An
acoustic IR's remainder is reflections, modal decay and ringing, not noise; the earlier mean over
everything-but-the-peak read reverberation as noise, misgraded clean reverberant records, and inflated the
first-arrival threshold until weak direct sounds were cut. The quantile reads the true floor as long as
decay and arrivals occupy under three quarters of the record. It is order-invariant, so one estimate serves
the anchor decision and the threshold before and after rotation.

Two corrections apply only to the **reported** SNR, not to the threshold (where under-estimating noise is
the safe direction):

- **Rayleigh bias.** For Gaussian noise the envelope is Rayleigh-distributed; the lowest-quartile RMS is
  about 0.370 of the full envelope RMS (`RayleighLowestQuartileRmsRatio`), which would flatter SNR by about
  8.6 dB. The reported figure divides it back out.
- **Deconvolution floor.** A transfer IR from a long FFT carries a contiguous tail of numerical silence
  100+ dB below the peak (on a clean cabin sweep up to a third of the record near −140 dB). The quartile
  would land entirely in it (an envelope showing ~65 dB SNR read 123). The reported noise is measured only
  over samples within `DeconvolutionFloorDropDb` = 100 dB of the peak, the same intent as the Auto-delay
  `ValidSampleRange` crop (where this bound is a no-op). `FindPeak` gates on max(noise, −25 dB below peak),
  so the tail never affects the arrival.

## Pre-ringing sidelobes

The analysis chain is zero-phase (bandpass window, the discrete Hilbert transform's 1/t skirt), so every
arrival drags a symmetric train of pre-ringing lobes; the stronger ones cleared the threshold and read as
arrivals milliseconds early — more of them the cleaner the measurement. `EliminatePreRingingSidelobes` tests
candidates against the known kernel:

- **Level ceiling.** An arrival of height H can produce at offset d a lobe no higher than H times the kernel
  envelope at d (`KernelRingLevel`, from `AnalysisKernelEnvelope`; without a kernel, the Hilbert skirt's
  2/(πn), the delta worst case). Above that ceiling with a 6 dB superposition margin
  (`SidelobeLevelMarginRatio` = 2) the candidate cannot be pre-ring and is genuine.
- **Mirror.** At or below the ceiling, the candidate is a sidelobe if an equal lobe exists at the mirrored
  position after the peak; decay and reflections only add late energy, so the mirror cannot hide a lobe. The
  mirror is the max over a small neighbourhood (the integer peak index is up to half a sample off), clamped
  so it never touches the peak's own lobe.
- Candidates are walked latest to earliest; every accepted (and every dwarfed) candidate becomes a sidelobe
  reference, so a weak first arrival's own pre-ring cannot pose as an even earlier arrival. Beyond the offset
  where even the strongest peak cannot ring above threshold, no comparison is needed.

Level-and-mirror together keep genuine early arrivals in reverberant rooms while rejecting the kernel's
ring exactly.

## Front tests

The earliest surviving candidate must also pass two rise tests (the strongest peak passes both trivially,
so the walk never comes back empty):

- **Rises out of its approach** (`RisesOutOfItsApproach`): the candidate must stand `FrontApproachRiseRatio`
  = 1.41 (3 dB) above the quietest sample in its approach span. A zero-phase kernel spreads the whole later
  record backwards and forms a flat shelf ahead of the first real arrival (field subwoofer at 32.5–130 Hz:
  −20 dB against a per-peak ring ceiling of −24.7 dB), whose micro-ripples are a few hundredths of a dB
  proud. No single-peak ceiling prices that shelf, since it is the sum of every later skirt; the rise test
  reads it directly. The approach span (`ApproachSpanSamples`) is the kernel core out to where its envelope
  has decayed by the window level, so it scales with the band's rise time; never shorter than one packet.
- **Rises within its packet** (`RisesWithinItsPacket`): the candidate must reach `ArrivalPacketRiseRatio` =
  25 % (−12 dB) of its packet's peak. Leading edges carry interference structure; a comb null just before the
  front leaves a bump above threshold, and taking it reports ripple-dependent time: identical drivers in
  opposite doors read one at the packet peak and one 20 dB down its foot (field pair: 0.31 ms of a 1.45 ms
  split; tweeter pair: 0.125 ms). On that cabin foot ripples sat 19–21 dB under their packet and genuine fronts
  within 7 dB. 25 % matches the broadband onset level in `VirtualCrossoverAnalysis.EstimateBroadbandOnset`.
  - The packet spans `ArrivalPacketMilliseconds` = 1 ms forward, the same separate-arrival rule as
    `TimeAlignmentAnalysis`, so the test stays local: a soft direct arrival buried under a room mode
    milliseconds later is nobody's foot ripple.
  - The packet ends early at a null of `ArrivalPacketResolvedValleyDb` = 20 dB (the same resolved-valley depth
    as `TimeAlignmentAnalysis`; interference nulls faster than an envelope rises), so a reflection rising
    after the null cannot dwarf a direct sound. Real foot ripples dip at most 14.5 dB before their peak.
