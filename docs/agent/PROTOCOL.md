# Resonalyze Agent Bridge — protocol v1

The Agent Bridge is how Resonalyze's Virtual DSP talks to a chat assistant
without a network: **Copy for AI** puts a text *package* on the clipboard, the
user pastes it into any assistant, and **Import AI proposal** reads the
assistant's reply back off the clipboard. This document is the normative
description of both texts. The tuning method the assistant should follow is in
[AGENT_GUIDE.md](AGENT_GUIDE.md); the user-facing description is in
[REFERENCE.md](../../REFERENCE.md#ai-assistant-bridge).

Version rules: a breaking change to either text bumps the version and the
markers (`…_V2`), so an old reply is simply not found rather than misread.
Adding a field to the package, or an optional field to the reply, does not.
Everything below is version **1**.

## 1. The package

```text
RESONALYZE_AGENT_PACKAGE_V1

<inline rules — the opening of AGENT_GUIDE.md, word for word>

BEGIN_RESONALYZE_AGENT_PACKAGE_JSON
{ … one compact JSON object … }
END_RESONALYZE_AGENT_PACKAGE_JSON
```

Strict JSON, camelCase, invariant numbers, finite numbers only, no comments.
Anything unavailable is **absent** — a property is not written when it has no
value — and where the absence needs a reason, a sibling `unavailableReason`
says it. Nothing is ever a zero standing in for "unknown".

Size: Resonalyze aims under 80 KB of JSON and never exceeds 100 KB — a seven-channel, six-junction car fits the target whole; a larger installation is trimmed. Over the
target it drops optional series in this fixed order, listing what it dropped in
`omitted`; once every optional series is gone, the mandatory payload may grow up
to the 100 KB ceiling, and beyond that nothing is copied:

1. `junctions[].sweep`
2. `junctions[].coherenceLadder`
3. `channels[].curves.broadband.coherence`
4. `junctions[].correlation.curve`
5. `junctions[].curves`

### 1.1 Top level

| Field | Meaning |
| --- | --- |
| `kind` | `"resonalyze.agent-package"` |
| `protocolVersion` | `1` |
| `packageId` | A new GUID per copy. Echo it in the reply; a mismatch is a warning, not a refusal. |
| `createdAtUtc` | ISO 8601, UTC. |
| `application` | `{ name, version }` |
| `conventions` | Units and sign conventions, as text, for readers without the guide. |
| `notes` | The user's *Notes for AI*: car, seat, drivers, amplifiers, processor, goals. Absent when empty. |
| `processor` | See 1.2. |
| `limits` | What the importer will hold a reply to. See 1.3. |
| `analysis` | The settings that decide what the diagnostics mean. See 1.4. |
| `target` | The EQ target shape, level and curve. See 1.5. |
| `channels[]` | Every physical channel. See 1.6. |
| `sides[]` | Each side of the car in the current view. See 1.7. |
| `junctions[]` | Each adjacent pair along the spectrum, per side. See 1.8. |
| `stereo[]` | Left-vs-right arrival and level per block. See 1.9. |
| `groups[]` | Each zone against the front stage. See 1.9. |
| `omitted[]` | Optional series left out to fit the size limit. |

### 1.2 `processor`

```json
{ "modelId": "helix-dsp-ultra-s", "displayName": "HELIX DSP ULTRA S", "custom": false,
  "sampleRateHz": 96000, "followsMeasurements": false, "qConvention": "Rbj",
  "maxDelayMs": 10, "maxDelaySource": "catalog", "peqBandsPerChannel": null }
```

`sampleRateHz` is the rate the device builds its filters at — every corner and
band frequency in a reply must sit below half of it. `qConvention` is how the
device *reads* Q on its own screen (`Rbj`, `Symmetric`, `Classic`); it does not
change any number in the package or the reply, which are always RBJ Q.
`maxDelaySource` is `"catalog"` when the ceiling came from the device's manual
and `"default (device not looked up)"` otherwise. `peqBandsPerChannel` is absent:
the catalog does not know it.

### 1.3 `limits`

```json
{ "gainDb": [-60, 20], "gainStepDb": 0.1, "delayMs": [0, 100], "delayStepMs": 0.01,
  "peqBands": 32, "peqPreampDb": 60, "crossoverHz": [10, 24000],
  "slopes": { "Butterworth": [6,12,18,24,30,36,42,48], "LinkwitzRiley": [12,24,36,48],
              "Bessel": [6,12,18,24,36,48], "Chebyshev": [6,12,18,24,30,36,42,48] },
  "chebyshevRippleDb": [0, 3],
  "operations": ["setGainDb", "setDelayMs", "setPolarity", "setCrossover",
                 "replacePeqBank", "useSpatialAverage", "runAutoCrossover"] }
```

These are Virtual DSP's own limits, not the device's. A reply outside them is
rejected; a reply inside them may still exceed what the device can dial.

`operations` is what THIS build can execute. The protocol below describes more
than any one build runs: an operation missing from the list is still read and
reviewed — so a reply written for a newer build is understood rather than
mangled — and then refused with "not available in this version of Resonalyze".
Use only the operations the package names; everything else goes in `advice`.

### 1.4 `analysis`

`groupView` (which part of the installation the diagnostics were computed for:
`FrontAndSub`, `RearAndSub`, `FrontAndCenter`, `GroupsCompared`, `Everything`),
`activeSide`, `smoothingInverseOctaves`, `psychoacousticSmoothing`,
`spatialAverage` (below), `phaseWindowMode`, `fdwCycles`, `phaseDetrendMode`, `gateShapeMs`
(`left`, `plateau`, `right`), `gateLeft` / `gateRight` (`offsetMs`, `detrendMs`
where pinned), `calibration` (the microphone calibration's name, no path),
`stereoSceneOffsetMs`, `rightHandDrive`, `stereoLevelDifferenceDb`,
`rearFillOffsetMs`.

`spatialAverage` says where the tune stands with spatial averages, counted over
the channels that have a measurement:

```json
"spatialAverage": { "mode": "MovingMic", "hybridTicked": true, "hybridDrawn": false,
                    "status": "capturedNotShown",
                    "channelsShown": 7, "channelsWithCapture": 5, "channelsDrawn": 0 }
```

- `mode` is the family the project reads (`MovingMic`, `MicArray`, `Off`; absent
  when the project has never chosen). `hybridTicked` is the Hybrid box;
  `hybridDrawn` whether the hybrid curves are actually on the plot — that also
  needs every playing channel to carry a capture of the selected family and a
  view that shows them (not a group comparison).
- The counts run over the channels the current view **shows** — the union of
  `sides[].channels`, the channels the diagnostics are built from. A channel the
  view leaves out has its own curves but never hybrid ones, whatever it holds;
  a muted channel has no curves at all; neither is counted. `channelsDrawn` is
  read off the hybrid curves actually present in the package, not off what is
  attached.
- `status`: `none` — no shown channel carries a capture of any family, the tune
  rests on single-point measurements; `capturedNotShown` — captures exist, but
  no hybrid curve is in the package (the box is off, the mode does not read
  them, the view cannot show them, or a playing channel lacks one); `partial` —
  hybrid curves for some shown channels; `active` — every shown channel is
  judged on its average.
- The status is about the channel curves. The `stereo[]` and `groups[]` level
  rows choose their basis on their own — they read the averages whenever the
  box is ticked and the captures cover both sides, whatever the view — and say
  so per row in `levelFromSpatialAverage`.

### 1.5 `target`

The parametric terms (`levelDb`, `preset`, `tiltDbPerOctave`, `bassShelf`,
`trebleShelf`, `presence` as `{ gainDb, frequencyHz, widthOctaves }`,
`toleranceDb`, `importedName`) and the resulting curve as a series
`{ columns: ["frequencyHz", "targetDb"], rows }` at 12 points per octave from
20 Hz to 20 kHz. Read-only: a reply cannot change the target.

### 1.6 `channels[]`

```json
{ "id": "B:left", "block": "B", "side": "left", "mono": false, "zone": "Front",
  "displayName": "left mid.json", "enabled": true, "bypass": false,
  "source": { "available": true, "sampleRateHz": 48000, "measuredBandHz": [40, 20000],
              "spatialAverage": "MovingMic" },
  "dsp": { "gainDb": -2, "delayMs": 1.42, "invertPolarity": false,
           "crossover": { "kind": "BandPass",
                          "highPass": { "family": "LinkwitzRiley", "frequencyHz": 250, "slopeDbPerOctave": 24, "rippleDb": 1 },
                          "lowPass":  { "family": "LinkwitzRiley", "frequencyHz": 2800, "slopeDbPerOctave": 24, "rippleDb": 1 } },
           "peq": { "preampDb": -1, "hash": "3f9a1c0b7e2d",
                    "bands": [ { "type": "Peaking", "frequencyHz": 820, "q": 2.1, "gainDb": -2.4 } ] } },
  "curves": { "broadband": { "columns": ["frequencyHz","preDspDb","processedDb","chainDb","peqDb","coherence"],
                             "rows": [ [20, null, null, -3.1, -1, null], … ] } } }
```

- **`id`** is the block letter as the panel prints it, a colon, and the side:
  `A:left`, `A:right`, `C:mono`. A mono block has one channel, `side: "mono"`.
  Ids are stable for as long as the blocks keep their order — which is exactly as
  long as the expected current values in a reply keep matching.
- `source.available` is false when no measurement is loaded; `unavailableReason`
  says why a loaded channel has no curves (`channel muted`, `not processed`).
  `source.spatialAverage` names the capture family the hybrid curves are built
  from — the selected mode (`MovingMic` or `MicArray`) when the channel holds a
  capture of it; absent otherwise, whatever other captures the channel has.
  `source.spatialAverageCaptures` lists every family the channel holds, read or
  not; absent when it holds none. The two differ exactly when the user has an
  average they are not using — see `analysis.spatialAverage`.
- Both crossover edges are always stored; `kind` says which act (`LowPass` uses
  `lowPass`, `HighPass` uses `highPass`, `BandPass` both, `Off` none). `rippleDb`
  matters only for `Chebyshev`.
- `peq.hash` is twelve hex digits of SHA-256 over the bands in order (type,
  frequency, Q, gain in round-trip form) and the preamp. A `replacePeqBank`
  reply echoes it instead of the whole current bank.
- `peq.peakDb` / `peq.peakHz` is the highest point of the bank's **net**
  response — preamp and every band together, built at the processor's rate —
  over the band the tune is judged in, 20 Hz to 20 kHz (or the processor's
  Nyquist where lower), with every band's centre sampled and each maximum
  refined, so a narrow bell is not stepped over. A band outside that range is
  legal; what it does there is not a tuning question.
  Above 0 dB the device is asked for more than unity there and a full-scale
  signal clips. A boost inside a wider cut, or under a negative preamp, is not
  a headroom problem; the sign of one band says nothing.
- `curves.broadband` runs 20 Hz to the lower of 20 kHz and half of either sample
  rate, at **12 points per octave**, endpoints included. Columns present only
  when the channel has them: `preDspDb` (the measured response before the chain —
  the panel's Raw curve), `processedDb` (through the chain, sharing the side's
  window), `chainDb` (the chain alone, built at the processor's rate), `peqDb`
  (the PEQ alone), `hybridPreDspDb` and `hybridProcessedDb` (the spatial average
  before and through the chain, present when the hybrid view is on and the
  channel has a capture), `coherence` (γ², when the source carried it). The two
  hybrid columns are placed on the **same level axis** as the impulse-response
  columns — the datum the panel draws them with is applied — so every column of
  a channel compares directly with every other. A `null` cell is a frequency the
  channel did not measure — never a zero.

### 1.7 `sides[]`

`side`, `channels` (the ids that played on this side in the current view),
`sumDb` (the coherent sum on the broadband grid), `totalSumLoss`
(`averageDb`, `dipDb` — only where the chain is continuous and the view is one
listening group), `sumVsTargetDb` (the median of `sumDb` minus `target.curve`
over the grid: positive = the side plays above the target level; where the
target datum sits, unmoved by a junction dip or a band edge),
`hybridSumVsTargetDb` (the same reading off the sum the hybrid view draws —
the spatial averages' levels held together by the point measurement's phase;
present only while the hybrid curves are drawn, and an estimate of one
position's interference rather than a measurement, so it carries the datum but
not a junction's depth), `unavailableReason`. `sumDb` itself is always the
measured, single-position sum.

### 1.8 `junctions[]`

```json
{ "id": "left:B-C", "side": "left", "lower": "B:left", "upper": "C:left",
  "crossoverHz": 350, "bandHz": [175, 700],
  "sumLoss": { "averageDb": -0.2, "dipDb": -0.7 },
  "phase": { "phaseAtCrossoverDeg": 6.9, "consistency": 0.84, "currentScore": 0.84,
             "bestExtraDelayMs": 0.1, "bestInvert": false, "bestScore": 0.86,
             "oppositePolarityScore": 0.31, "rivalExtraDelayMs": 2.1, "rivalScore": 0.8,
             "lobeMargin": 0.33, "fitDelayMs": 0.1, "fitRmsDeg": 12 },
  "lobes": [ { "extraDelayMs": -0.07, "invert": false, "scoreDb": -0.2 }, … ],
  "sweep": { "columns": ["extraDelayMs","scoreNormalDb","scoreInvertedDb"], "rows": [ … ] },
  "correlation": { "searchRangeMs": 4.28,
                   "fullRecordPeak": { "lagMs": -0.04, "r": 0.94 }, "fullRecordTrough": { "lagMs": -1.35, "r": -0.86 },
                   "directPeak": { "lagMs": 0.15, "r": 0.97 }, "directTrough": { "lagMs": 1.45, "r": -0.9 },
                   "arrivalLagMs": 1.84,
                   "curve": { "columns": ["lagMs","fullRecordR","directR"], "rows": [ … ] } },
  "coherenceLadder": { "columns": ["frequencyHz","lagMs","peakR","currentR","halfPeriodMs"], "rows": [ … ] },
  "curves": { "columns": ["frequencyHz","lowerDb","upperDb","sumDb","lossDb"], "rows": [ … ] } }
```

Every block is the panel's own read-out, unchanged:

- `sumLoss`: the **Sum loss** row for this junction — dB ≤ 0, how far the
  coherent sum falls short of the magnitude sum over the band; `averageDb` over
  the band, `dipDb` at its worst point.
- `phase`: the **Junction phase** row. `bestExtraDelayMs` and `bestInvert` are
  applied to the **lower** channel; scores run −1…1, higher is better;
  `lobeMargin` below about 0.05 means a whole-period hop cannot be ruled out.
  Absent, with `unavailableReason`, where the pair's phase is not consistent
  enough across the band.
- `sweep`: the summation score against an extra delay applied to the **upper**
  channel, both polarities, at most 48 rows; `scoreDb` ≤ 0, 0 is perfect.
  `lobes` are its local maxima, best first, at most five — the candidates an
  Auto delay run would weigh.
- `correlation`: GCC-PHAT between the pair. `lagMs` is the delay that, added to
  the **upper** channel, aligns it with the lower; a positive peak is a
  normal-polarity alignment, a negative trough the same with the upper channel
  inverted. `fullRecord*` reads the whole capture, `direct*` the direct sound
  alone. `arrivalLagMs` = lower arrival − upper arrival.
- `coherenceLadder`: per band, `lagMs` is the upper channel's arrival relative to
  the lower at that frequency, `peakR` the best coherence found, `currentR` the
  coherence at the current alignment.
- `curves`: the two channels, the side's sum and the loss on a dense grid an
  octave to each side of the crossover at **24 points per octave**, the crossover
  frequency itself always a point.

### 1.9 `stereo[]` and `groups[]`

`stereo[]`, per block: `leftMs`, `rightMs`, `deltaMs` (left − right; positive
means the right side leads), `bandHz`, `levelDeltaDb` (left − right),
`leftLatched` / `rightLatched` (the arrival timed the room's modal build-up
rather than the direct rise — the number is real but overstates the skew),
`levelFromSpatialAverage`.

`groups[]`, per zone compared against the front stage: `delayMs` (the zone's
arrival minus the front's), `levelDb` (the zone's level minus the front's),
`bandHz`, `levelFromSpatialAverage`. Empty in views that compare no groups.

## 2. The reply

The assistant may write anything before and after. Resonalyze reads only the
JSON between the markers, and there must be **exactly one** such block:

```text
BEGIN_RESONALYZE_AGENT_PROPOSAL_V1
{ … }
END_RESONALYZE_AGENT_PROPOSAL_V1
```

A Markdown fence around the JSON is tolerated. Everything else is strict: no
comments, no trailing commas, no `NaN`/`Infinity`, property names exactly as
written here (case matters), no properties the protocol does not name (one
open door: an `extensions` object, whose content is ignored).

```json
{
  "kind": "resonalyze.agent-proposal",
  "protocolVersion": 1,
  "packageId": "b6bd73c2-997b-4fe0-814a-d123cc403b8a",
  "summary": "The left mid/tweeter junction cancels near 3.1 kHz; the mid's polarity is the first thing to test.",
  "advice": [
    "After applying, run Auto delay with scene offset 0.25 ms (LHD) and gains enabled.",
    "Re-measure the right tweeter: coherence above 8 kHz is below 0.5."
  ],
  "sources": [
    { "url": "https://example.com/gb60.pdf", "title": "GB60 datasheet", "factsUsed": ["Fs 65 Hz", "Xmax 6 mm"] }
  ],
  "operations": [
    { "id": "op-1", "op": "setPolarity", "channelId": "B:left", "expectedCurrent": false, "proposed": true,
      "reason": "Phase opposition at the B/C junction: score 0.3 now, 0.68 inverted." },
    { "id": "op-2", "op": "setGainDb", "channelId": "A:right", "expectedCurrent": -2, "proposed": -2.6,
      "reason": "Level Δ L−R of +0.6 dB in the mid band." },
    { "id": "op-3", "op": "setDelayMs", "channelId": "A:right", "expectedCurrent": 1.42, "proposed": 1.37,
      "reason": "Δ L−R of −0.36 ms in the shared band." },
    { "id": "op-4", "op": "setCrossover", "channelId": "B:left",
      "expectedCurrent": { "kind": "BandPass",
                           "highPass": { "family": "LinkwitzRiley", "frequencyHz": 250, "slopeDbPerOctave": 24 },
                           "lowPass":  { "family": "LinkwitzRiley", "frequencyHz": 2800, "slopeDbPerOctave": 24 } },
      "proposed":        { "kind": "BandPass",
                           "highPass": { "family": "LinkwitzRiley", "frequencyHz": 250, "slopeDbPerOctave": 24 },
                           "lowPass":  { "family": "LinkwitzRiley", "frequencyHz": 2600, "slopeDbPerOctave": 24 } },
      "reason": "Keeps the mid's beaming out of the overlap; datasheet recommends up to 3 kHz." },
    { "id": "op-5", "op": "replacePeqBank", "channelId": "B:left", "expectedCurrentHash": "3f9a1c0b7e2d",
      "proposed": { "preampDb": -1, "bands": [ { "type": "Peaking", "frequencyHz": 820, "q": 2.1, "gainDb": -2.4 } ] },
      "reason": "Door resonance at 820 Hz in both the point measurement and the spatial average." }
  ]
}
```

### 2.1 Fields

| Field | Required | Rule |
| --- | --- | --- |
| `kind` | yes | `"resonalyze.agent-proposal"` |
| `protocolVersion` | yes | `1` |
| `packageId` | no | Echo of the package's id. A different id is shown as a warning. |
| `summary` | yes | One paragraph, shown at the top of the review. |
| `advice[]` | no | Changes that are not operations — engines to run, things to re-measure. Shown, never applied. |
| `sources[]` | no | `{ url, title?, factsUsed[] }`; `url` must be `http(s)`. Shown as text, never opened. |
| `operations[]` | yes | The typed changes, see 2.2. May be empty when the reply is advice only. |
| `extensions` | no | Ignored. |

Limits: 1 MiB of clipboard text, 64 operations, 32 advice lines / sources /
facts per source, 2000 characters per string, JSON depth 8.

### 2.2 Operations

Every operation has `id` (unique in the reply), `op` and `reason` (shown in the
review). There are two families. The five below WRITE one channel's settings:
they add `channelId` (an id from the package) and what they believe the
**current** value is, copied from the package. A current value that no longer
matches means the tune moved on since the package was copied, and the operation
is refused rather than applied to a state it was not reasoned about. Two
operations on one channel's same parameter refuse each other. An operation that
changes nothing is refused as "no change". The four under *Engine requests*
below ask for one of the panel's own engines instead.

| `op` | Fields | Checked against |
| --- | --- | --- |
| `setGainDb` | `expectedCurrent`, `proposed` (dB) | `limits.gainDb`, `limits.gainStepDb` |
| `setDelayMs` | `expectedCurrent`, `proposed` (ms) | `limits.delayMs`, `limits.delayStepMs`; above `processor.maxDelayMs` is a warning |
| `setPolarity` | `expectedCurrent`, `proposed` (booleans, true = inverted) | — |
| `setCrossover` | `expectedCurrent`, `proposed` (a crossover object) | kind and family names exactly as in the package; slopes per family; corner in `limits.crossoverHz` and below the processor's Nyquist; ripple in `limits.chebyshevRippleDb` for Chebyshev |
| `replacePeqBank` | `expectedCurrentHash`, `proposed` `{ preampDb, bands[] }` | at most `limits.peqBands` bands; every band `frequencyHz > 0` and below the processor's Nyquist, `q > 0`, finite `gainDb`, `type` one of `Peaking`, `LowShelf`, `HighShelf`, `AllPassFirstOrder`, `AllPassSecondOrder`; preamp within ±`limits.peqPreampDb`; a net response rising above 0 dB is a warning naming the peak and the preamp that would absorb it; a bell with Q > 2 within an octave of one of the channel's own active crossover corners is a warning naming the corner — judged on the channel as it would end up after every applicable row on it, so a crossover row that moves a corner onto an existing bell warns too |

A crossover object is `{ kind, highPass?, lowPass? }` with each edge
`{ family, frequencyHz, slopeDbPerOctave, rippleDb? }`. The edges the kind uses
must be stated; an edge it does not use may be omitted and keeps its stored
value. In `expectedCurrent`, only the edges the kind uses are compared, and
`rippleDb` only when stated.

All `q` values are RBJ cookbook Q. Resonalyze never converts them on the way in;
the processor's own convention is applied only where numbers leave for the
device (the tuning sheets).

#### Engine requests

The four operations below carry no value of their own. Three ask for one of the
panel's engines to be opened with the inputs stated; the fourth,
`useSpatialAverage`, puts the panel into the state the others should be read in.
They exist because the engines compute what no reading of a curve can, and
because a user who has been told to "run Auto delay with a 0.25 ms scene offset"
otherwise has to find the dialog and retype the numbers.

They carry no `expectedCurrent`: what the engine writes is what the run decides,
and the engine's own dialog — Auto delay's report, the crossover wizard's rows,
the EQ Wizard's graph — stays the gate it goes through. Cancelling one skips
that operation; the rest of the import carries on.

`runAutoDelay`, `runAutoCrossover` and `useSpatialAverage` take **no**
`channelId`: they address the whole project, and a `channelId` on one is refused
as a property the protocol does not name. Every other input is optional, and one
that is left out means the value the panel would open with — so a reply that
wants only the scene offset changed states only that.

| `op` | Fields | Checked against |
| --- | --- | --- |
| `runAutoDelay` | `sceneOffsetMs?`, `rightHandDrive?`, `adjustGains?`, `nearSideCutDb?`, `rearFillOffsetMs?` | the dialog's own fields: scene offset 0–5 ms in steps of 0.01, near-side cut 0–6 dB in steps of 0.1, rear fill offset 0–30 ms in steps of 0.1. `nearSideCutDb` is a magnitude: the LHD/RHD toggle owns the sign. Stereo or single-sided is the panel's decision, as it is for the button |
| `runAutoCrossover` | none | the wizard has no inputs: the families, the corner window and the chain order are chosen in its own dialog |
| `autoTunePeq` | `channelId`, `targetLevelDb?`, `minHz?`, `maxHz?`, `allowShelves?`, `cutsOnly?`, `source?` | the channel exists and has a measurement; target level −120…60 dB in whole dB; each stated edge above 0 Hz and below the processor's Nyquist, the lower below the upper; `source` is `point` or `spatialAverage`, and `spatialAverage` needs the channel to carry one |
| `useSpatialAverage` | `mode`, `hybrid` | `mode` is `MovingMic` or `MicArray` and at least one channel carries that family (`channels[].source.spatialAverageCaptures`); `hybrid` must be `true`; a mode already in force with Hybrid already ticked is refused as "no change" |

```json
{ "operations": [
  { "id": "op-6", "op": "useSpatialAverage", "mode": "MovingMic", "hybrid": true,
    "reason": "Every channel carries an MMM capture and the tune is still read at one point." },
  { "id": "op-7", "op": "runAutoDelay", "sceneOffsetMs": 0.25, "adjustGains": true,
    "reason": "Two polarity flips changed the arrivals the current delays were set for." }
] }
```

An import runs what it was given in one fixed order, whatever order the reply
listed: the five settings operations first, then `useSpatialAverage` (it decides
which curves the rest are read on), then `runAutoCrossover`, then `runAutoDelay`,
then `autoTunePeq`. One summary at the end says what was applied and what was
skipped, and *Undo AI import* puts back everything the whole sequence moved.

An engine and a hand-written value the engine would write over cannot both be
meant, so the hand-written row is **rejected**, naming the engine that would have
erased it. The engine keeps its row: it is the one that computes the number.

| Requesting | Rejects |
| --- | --- |
| `runAutoDelay` | `setDelayMs` and `setPolarity` on any channel; `setGainDb` too when the run balances gains |
| `runAutoCrossover` | `setCrossover` and `setGainDb` on any channel (the wizard writes a cut-only gain with every corner) |
| `autoTunePeq` | `replacePeqBank` on the same channel |

An engine this build cannot run erases nothing, so its request is refused and the
hand-written rows are left to do the work instead. Request each engine once: a
repeat is refused, naming the first. `autoTunePeq` counts per channel — one fit
per channel is the point of it — so a second request for the SAME channel is the
one that is refused.

Beyond the operations of this section, nothing in a session can be addressed:
not the target, the processor, the gates, the sources, the calibration, the
channel list, mute or bypass. Such changes belong in `advice`, as text. An
unknown `op` is listed in the review as rejected and never applied.

The review judges the junction-zone rule on each channel as it would end up
after **every** applicable row; the commit judges it again on the rows the user
actually ticked — on the channels whose ticked rows touch the crossover or the
bank — and asks before applying a subset that leaves a state the review never
showed (a crossover moved without the bank that went with it).

### 2.3 What the importer does

1. Reads the clipboard, finds the one block, parses it strictly.
2. Judges every operation against the live settings: expected value, limits,
   conflicts, no-ops — each on a copy, validated by the same rules a saved
   session must pass.
3. Shows the review: every row with its current and proposed value; admissible
   rows ticked, rejected rows greyed with their reason, warnings in words.
4. On *Apply selected*, judges the ticked rows once more against the settings as
   they are at that moment, then writes the settings rows as one set. A failure
   there writes nothing.
5. Runs the ticked engine requests, in the fixed order above — each engine
   through its own dialog, the spatial average straight onto the panel — and
   ends with one summary of what was applied and what was skipped. Undo is armed
   before the first of these writes, so an import that stops part-way is still
   undone in one step.
6. *Undo AI import* puts back everything the import could have moved: every
   channel's chain, the spatial average mode and the Hybrid tick, and the block
   order the crossover wizard may have changed.

## 3. Privacy

The package never contains file paths, folder names, the Windows user name,
history ids or raw impulse responses. A channel's `displayName` is the name the
panel shows (a file name without its folder). `notes` is whatever the user
typed. Resonalyze makes no network request in any part of this workflow.
