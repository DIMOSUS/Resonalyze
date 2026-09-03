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
| `guideVersion` | The version of [AGENT_GUIDE.md](AGENT_GUIDE.md) this build was written against (its first line). The guide at the URL may be newer; read it, and know which methodology the package's author expected. |
| `packageId` | A new GUID per copy. Echo it in the reply: it is how the importer knows which session the reply describes, and an engine request without it is refused. A reply naming a package the session cannot vouch for — none copied, another one, or this one after the session changed — is shown with a warning; its settings rows are judged on their expected current values but offered unticked, its engine requests refused (§2.2). |
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
                 "replacePeqBank", "probe", "useSpatialAverage", "runAutoCrossover",
                 "tuneJunction", "runAutoDelay", "autoTunePeq"],
  "probes": ["junction", "junctionDelay", "excessGroupDelay"],
  "probeVariantsPerImport": 24, "probeChanges": 2 }
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
`activeSide`, `smoothingInverseOctaves` and `psychoacousticSmoothing` (the
package's OWN smoothing, not the display's: every magnitude curve, sum and Sum
loss in a package is computed at psychoacoustic smoothing — 1/3 octave below
100 Hz narrowing to 1/6 above 1 kHz — whatever the panel's combo box shows, so
two packages compare and a dip's depth means the same thing in each; the
screen's read-outs at another smoothing may differ — the hybrid curves and
sums are the one exception, at `spatialAverage.smoothingInverseOctaves`, below),
`spatialAverage` (below), `phaseWindowMode`, `fdwCycles`, `phaseDetrendMode`, `gateShapeMs`
(`left`, `plateau`, `right`), `gateLeft` / `gateRight` (`offsetMs`, `detrendMs`
where pinned), `calibration` (the microphone calibration's name, no path),
`stereoSceneOffsetMs`, `rightHandDrive`, `stereoLevelDifferenceDb`,
`rearFillOffsetMs`.

`spatialAverage` says where the tune stands with spatial averages, counted over
the channels that have a measurement:

```json
"spatialAverage": { "mode": "MovingMic", "hybridTicked": true, "hybridDrawn": false,
                    "smoothingInverseOctaves": 12, "status": "capturedNotShown",
                    "channelsShown": 7, "channelsWithCapture": 5, "channelsDrawn": 0 }
```

- `mode` is the family the project reads (`MovingMic`, `MicArray`, `Off`; absent
  when the project has never chosen). `hybridTicked` is the Hybrid box;
  `hybridDrawn` whether the hybrid curves are actually on the plot — that also
  needs every playing channel to carry a capture of the selected family and a
  view that shows them (not a group comparison).
- `smoothingInverseOctaves` is the width the hybrid curves and sums are read at
  — `hybridPreDspDb`, `hybridProcessedDb`, the sides' hybrid sum and
  `hybridSumVsTargetDb`: 1/12 octave, the grid's own width, not the package's
  psychoacoustic one. The manual reads a spatial average with the smoothing
  off — the average has already removed the position-dependent wiggles
  smoothing exists to hide, and a fractional-octave window straddling a
  crossover's skirt pulls the level toward the passband right where the
  acoustic slopes are judged — and off cannot travel on a 12-point-per-octave
  grid; one grid step is the nearest. A hybrid column and the measured column
  beside it are therefore not at one width: compare the two by shape, not by
  the depth of a narrow feature.
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
  channel has a capture, at the hybrid's own smoothing, §1.4), `coherence` (γ², when the source carried it). The two
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

Every block is the panel's own read-out, unchanged. `id` is what a
`tuneJunction` request names (§2.2).

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

### 1.10 Diagnostics on request

The package is already the size a chat takes, so a reading most tunes never
need is not in it. The assistant asks for one by its menu name, the user copies
it (**AI assistant… → Copy diagnostics for AI → …**) and pastes it into the
same chat as a second text:

```text
RESONALYZE_AGENT_DIAGNOSTIC_V1
…
BEGIN_RESONALYZE_AGENT_DIAGNOSTIC_JSON
{ "kind": "resonalyze.agent-diagnostic", "protocolVersion": 1, "guideVersion": "1.5",
  "diagnostic": "excessGroupDelay", "packageId": "…", "createdAtUtc": "…",
  "conventions": { … },
  "channels": [ { "id": "B:left", "series": { "columns": ["frequencyHz","excessGdMs"], "rows": [ … ] } }, … ] }
END_RESONALYZE_AGENT_DIAGNOSTIC_JSON
```

`packageId` names the package the diagnostic was copied beside (absent when
none was copied since the session opened, or when the session has changed
since that copy — the same check the reply's review makes, §2.2 — so an id
that is present vouches that the curves belong beside it); the channel ids and the
broadband grid are the package's, so the two lay side by side. A row is left
out where the reading could not be made.

| `diagnostic` | What the series holds |
| --- | --- |
| `excessGroupDelay` | Each measured channel's excess group delay in ms: its group delay less the minimum-phase part the magnitude dictates (which a minimum-phase PEQ straightens along with the magnitude), read off the raw impulse response through the project's phase gate at the channel's own arrival, at the group-delay view's own default smoothing, 1/12 octave, whatever the display shows — a group delay is a phase slope, and the package's psychoacoustic width is a hearing model for levels, not for time. What remains is arrivals and reflections — the part of a junction's phase mismatch that no PEQ can touch. The chain does not enter it: the curve is the same with any PEQ bank in place or none. |

## 2. The reply

The assistant may write anything before and after. Resonalyze reads the one
JSON object in the reply whose `kind` is `resonalyze.agent-proposal` — the
object identifies itself, and there must be **exactly one** such object:

```json
{ "kind": "resonalyze.agent-proposal", "protocolVersion": 1, … }
```

A Markdown fence around the JSON is expected and tolerated; braces inside
strings do not confuse the reader. The earlier envelope — the object between a
`BEGIN_RESONALYZE_AGENT_PROPOSAL_V1` and an `END_RESONALYZE_AGENT_PROPOSAL_V1`
line — is still read when a reply carries it, but it is no longer asked for:
a chat that sets such markers outside the block it offers to copy leaves the
user pasting a block the importer could not find. Everything else is strict: no
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
| `packageId` | for engine requests | Echo of the package's id. Required when `operations[]` holds an engine request — one without it is refused; a reply of settings rows alone may leave it out, each row carrying its own expected current value. A package the session cannot vouch for (§2.2) is shown as a warning, refuses the engine requests and offers the settings rows unticked. |
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

The operations below carry no value of their own. Four ask for one of the
panel's engines to be run with the inputs stated; `useSpatialAverage` puts the
panel into the state the others should be read in; and `probe` asks a question
and changes nothing at all. They exist because the engines compute what no
reading of a curve can, because a user told to "run Auto delay with a 0.25 ms
scene offset" otherwise has to find the dialog and retype the numbers, and
because "what would this do" deserves an answer that costs the user no undo.

They carry no `expectedCurrent`: what the engine writes is what the run decides.
The review is the gate. `runAutoDelay` then runs **without its dialog** — the
same checks the button makes (two measured channels, no bypassed participant,
the gate in place, a crossover somewhere; a failed check skips the operation
with the reason in the summary), the same search, the same commit — and its
report, the text the dialog would have shown, comes back in the import's
summary and the alignment log. `autoTunePeq` runs without the EQ Wizard too:
it fits the curve the wizard would have opened on for that channel — the
spatial average while the hybrid view draws it, the point measurement
otherwise, or whichever `source` names — with the EQ Wizard's Auto Tune
settings as they stand (Max Filters, Gain min/max, Max Q, Cuts only, Shelves —
the same bank the wizard's button would fit for the same project) for what the
reply leaves out, and the channel's passband as the window; all-pass bands
in the bank are kept and the fit tunes around them; a `targetLevelDb` moves the
project's target level, as the wizard's Return does — it is one datum for the
whole project, so every request in a reply that states one must state the
same value (the first stated level stands, the others are refused naming it),
and a run that skips itself leaves the level untouched. A channel on the side not
on screen is refused at review (the handoff is the shown side's: its gate pin,
its anchor, its hybrid datum — switch the L/R selector, copy a new package and
ask again). The run skips itself, with the reason, when `spatialAverage` is
asked for and the hybrid view is not drawing it, or when the target level sits
3 dB or more above the curve (10 dB or more below), which is the question the
wizard would have asked. `runAutoCrossover` opens the wizard: its rows
are where the driver types are confirmed, and cancelling it skips that
operation while the rest of the import carries on. `tuneJunction` runs
without a dialog, on one junction, and is described after the table.

`runAutoDelay`, `runAutoCrossover`, `tuneJunction` and `useSpatialAverage`
take **no** `channelId`: the first three address the whole project or a
junction of it, and a `channelId` on one is refused as a property the protocol
does not name. Every other input is optional, and one that is left out means
the value the panel would open with — so a reply that wants only the scene
offset changed states only that.

| `op` | Fields | Checked against |
| --- | --- | --- |
| `runAutoDelay` | `sceneOffsetMs?`, `rightHandDrive?`, `adjustGains?`, `nearSideCutDb?`, `rearFillOffsetMs?` | the dialog's own fields: scene offset 0–5 ms in steps of 0.01, near-side cut 0–6 dB in steps of 0.1, rear fill offset 0–30 ms in steps of 0.1. `nearSideCutDb` is a magnitude: the LHD/RHD toggle owns the sign. Stereo or single-sided is the panel's decision, as it is for the button |
| `probe` | `probe`, `junctionId?`, `variants?` | `probe` is one of `limits.probes`; `junctionId` a `junctions[].id` of this package, required by every probe but `excessGroupDelay`, and resolved as `tuneJunction` resolves it. For `junction`: at least one variant and, across every probe of the reply together, at most `limits.probeVariantsPerImport`; each variant 1…`limits.probeChanges` changes, each change naming one of the junction's OWN two channels once and stating at least one of `gainDb`, `delayMs`, `invertPolarity`, `crossover`, `peq` — every stated value held to the limit the settings operation that writes it is held to |
| `runAutoCrossover` | none | the wizard has no inputs: the families, the corner window and the chain order are chosen in its own dialog |
| `tuneJunction` | `junctionId`, `minHz?`, `maxHz?`, `families?`, `slopes?`, `independentSlopes?` | `junctionId` is a `junctions[].id` of this package (`left:C-D`): both blocks on that side with a measurement, in the sum, not bypassed, in one group, neighbours along the spectrum that hand over to each other; each stated edge within 20 Hz–20 kHz and below the processor's Nyquist, the window as the run will use it — a stated edge with half an octave from the current corner for the one left out — ordered; `families` names from `limits.slopes` (left out: the families the two facing edges use today); `slopes` offered by one of those families (left out: every slope from 12 dB/oct up); `independentSlopes` left out is `false` — one slope for both edges |
| `autoTunePeq` | `channelId`, `targetLevelDb?`, `minHz?`, `maxHz?`, `allowShelves?`, `cutsOnly?`, `source?` | the channel exists, has a measurement and is on the side on screen; target level −120…60 dB in whole dB, the same in every request that states one; each stated edge within the wizard's From/To fields (20 Hz–20 kHz) and below the processor's Nyquist, and the window as the run will use it — a stated edge with the channel's passband edge for the one left out — ordered; `source` is `point` or `spatialAverage`, and `spatialAverage` needs the channel to carry one |
| `useSpatialAverage` | `mode`, `hybrid` | `mode` is `MovingMic` or `MicArray` and at least one channel carries that family (`channels[].source.spatialAverageCaptures`); `hybrid` must be `true`; a mode already in force with Hybrid already ticked is refused as "no change" |

```json
{ "operations": [
  { "id": "op-6", "op": "useSpatialAverage", "mode": "MovingMic", "hybrid": true,
    "reason": "Every channel carries an MMM capture and the tune is still read at one point." },
  { "id": "op-7", "op": "runAutoDelay", "sceneOffsetMs": 0.25, "adjustGains": true,
    "reason": "Two polarity flips changed the arrivals the current delays were set for." }
] }
```

### `probe` — reading without changing

`probe` is the one operation that writes NOTHING. It asks what the tune WOULD
measure under settings the reply names, Resonalyze computes it on copies, puts
the answer on the clipboard as a `resonalyze.agent-probe` text (§2.4) and asks
the user to paste it into the same chat. There is nothing to undo, nothing to
be careful about, and no reason to ask the user to apply something and put it
back afterwards.

```json
{ "operations": [
  { "id": "op-1", "op": "probe", "probe": "junction", "junctionId": "left:C-D",
    "variants": [
      { "label": "no bank on C",
        "changes": [ { "channelId": "C:left", "peq": { "preampDb": 0, "bands": [] } } ] },
      { "label": "BW48 at 2.4 k",
        "changes": [
          { "channelId": "C:left", "crossover": { "kind": "BandPass",
              "highPass": { "family": "LinkwitzRiley", "frequencyHz": 350, "slopeDbPerOctave": 36 },
              "lowPass":  { "family": "Butterworth", "frequencyHz": 2400, "slopeDbPerOctave": 48 } } },
          { "channelId": "D:left", "crossover": { "kind": "HighPass",
              "highPass": { "family": "Butterworth", "frequencyHz": 2400, "slopeDbPerOctave": 48 } } } ] }
    ],
    "reason": "Read both before proposing either." },
  { "id": "op-2", "op": "probe", "probe": "junctionDelay", "junctionId": "left:C-D",
    "reason": "What a delay search would find here, without moving anything." }
] }
```

A `junction` probe's variant is a set of changes stated exactly as the settings
operations state them — the same five parameters, the same shapes, the same
limits — so a variant that reads well converts to a proposal word for word. A
change states only what it moves; everything it leaves out the channel keeps.
An empty `peq` bank (no bands, no preamp) is the bank CLEARED, which is how a
reply asks the diagnostic pass's question without the user applying and undoing
anything. The junction as it stands is always read beside the variants as the
`current` entry, so a probe answers "what have I got, and what would I get".

`junctionDelay` answers what an alignment search would find at the junction as
it stands — the delay and polarity it would pick for the upper channel, and the
rival lobes it weighed — without writing any of it. `excessGroupDelay` is the
diagnostic of the same name, asked for in the reply rather than found in a menu;
it names no junction.

A probe reads the side its `junctionId` names, since a variant's changes are
that side's channels'. A crossover is one filter for both sides, so a reply
weighing one asks for both junctions (`left:C-D` and `right:C-D`); the
once-per-import rule counts a probe per reading AND junction, so those are two
rows, not a repeat.

Probes run BEFORE anything else in the import — a probe answers a question about
the tune as it stands, and reading it after the import's own rows had landed
would answer a different one — and every probe of one import goes into ONE
document, so the clipboard holds one text and the user pastes once. A probe is
refused only where its question cannot be answered (a junction the package could
not have printed, a variant outside the settings limits, a reading this build
does not compute); it survives a package the session can no longer vouch for,
where every engine request is refused, because what it reads is the session as
it is now — the document says whether that session still matches the package.

`tuneJunction` is the crossover engine for a tuned system, where the wizard is
not: it searches ONE junction's two facing edges — the lower block's low-pass
and the upper block's high-pass; corner on the wizard's lattice, family,
slopes — and scores every candidate on the pair's coherent sum **at the
current delays and polarity**, through the whole current chains (PEQ
included), on every side the pair is measured on: the summation loss, its
dip, and the ripple of the sum, read on one band shared by every candidate
(an octave outside the corner window and the current corner, so the car's
own ripple is the same term for all of them) and again on the candidate's own
band, an octave each side of its corner — the band the panel's Sum loss row
and the package read a junction on. No slope is preferred. Gains, delays,
polarity, PEQ and every other junction stay exactly as they are; one
crossover is written to both sides of both blocks, as the wizard writes one.
The current crossover keeps its place unless a candidate beats it by 0.5 dB
on the shared-band score and reads no worse on its own band, and the summary
says either way: the two edges before and after, per side the own-band loss,
dip and ripple before and after, and what the best delay of the upper block
would leave — the reading that says whether `runAutoDelay` should follow. One
import may carry both (they run in that order), though the guide asks for the
realignment in the reply AFTER the tune has been read, since the second cannot
be judged while the first is unread.

An import runs what it was given in one fixed order, whatever order the reply
listed: the probes first (they read the tune as it stands), then the five
settings operations, then `useSpatialAverage` (it decides which curves the rest
are read on), then `runAutoCrossover`, then `tuneJunction`, then `runAutoDelay`,
then `autoTunePeq`. One summary at the end
says what was applied and what was skipped, and *Undo AI import* puts back
everything the whole sequence moved.

An engine request is refused when the reply names no package, or names one the
session cannot vouch for: no package copied since the session opened, another
package than the last one copied, or that package copied from a session that
has since changed (a measurement, capture or calibration replaced, a block
added, removed or reordered, a chain, gate or datum moved, the side on screen
or the view switched, an import undone). Resonalyze fingerprints the session
at every copy and compares at every review. An engine reads the session as it
is *now*, which the assistant has not seen. The settings rows stay — each is
still judged on its expected current value — but are offered **unticked** and
marked, since a current value can match after the measurement the row was
reasoned from has been replaced; the user ticks what still applies. Ask for a
new package. A reply of settings rows alone that names no package is taken at
its word.

An engine and a hand-written value the engine would write over cannot both be
meant, so the hand-written row is **rejected**, naming the engine that would have
erased it. The engine keeps its row: it is the one that computes the number.

| Requesting | Rejects |
| --- | --- |
| `runAutoDelay` | `setDelayMs` and `setPolarity` on any channel; `setGainDb` too when the run balances gains |
| `runAutoCrossover` | `setCrossover` and `setGainDb` on any channel (the wizard writes a cut-only gain with every corner), and every `tuneJunction` (the wizard rewrites every junction of the chain) |
| `tuneJunction` | `setCrossover` on either of its two blocks, either side |
| `probe` | nothing — it writes nothing, and nothing writes over it |
| `autoTunePeq` | `replacePeqBank` on the same channel |

An engine this build cannot run erases nothing, so its request is refused and the
hand-written rows are left to do the work instead. Request each engine once: a
repeat is refused, naming the first. `autoTunePeq` counts per channel — one fit
per channel is the point of it — so a second request for the SAME channel is the
one that is refused; `tuneJunction` counts per junction the same way, and
`probe` per reading and junction.

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
4. On *Apply selected*, first compares the current session fingerprint with the
   one the review showed; a difference stops the whole import with a request to
   review again. If it is unchanged, the ticked rows are judged once more
   against the live settings and written as one set. A stale-package warning
   already shown in the review does not block a settings row the user deliberately
   ticked; a row that is no longer applicable does. A failure writes nothing.
5. Runs the ticked engine requests, in the fixed order above — the spatial
   average straight onto the panel, Auto crossover through its wizard, the
   junction tune, Auto delay and Auto-tune without their dialogs — and
   ends with one summary of what was applied and what was skipped. Undo is armed
   before the first of these writes, so an import that stops part-way is still
   undone in one step.
6. *Undo AI import* puts back everything the import could have moved: every
   channel's chain, the spatial average mode and the Hybrid tick, and the block
   order the crossover wizard may have changed. The fingerprint check reads
   the undone session as what it is: a package copied *before* the import
   describes it again, and a reply answering that package is taken in full; a
   package copied *after* the import — the guide's diagnostic pass — no longer
   does, and a reply answering it has its engine requests refused until a new
   package is copied.

### 2.4 The probe result

What a probe answers comes back the way a diagnostic does — a text of its own,
copied to the clipboard, pasted into the same chat:

```text
RESONALYZE_AGENT_PROBE_V1
…
BEGIN_RESONALYZE_AGENT_PROBE_JSON
{ "kind": "resonalyze.agent-probe", "protocolVersion": 1, "guideVersion": "1.5",
  "packageId": "…", "sessionMatchesPackage": true, "createdAtUtc": "…",
  "conventions": { … },
  "probes": [
    { "id": "op-1", "probe": "junction", "junctionId": "left:C-D",
      "lower": "C:left", "upper": "D:left", "sharedBandHz": [875, 4800],
      "entries": [
        { "label": "current", "current": true,
          "lowPass": { "family": "Butterworth", "frequencyHz": 2000, "slopeDbPerOctave": 48, "rippleDb": 1 },
          "highPass": { … }, "bandHz": [1000, 4000],
          "sides": [ { "side": "left", "sumLossDb": -0.4, "dipDb": -1.2, "rippleDb": 3.1,
                       "shared": { "sumLossDb": -0.3, "dipDb": -1.2, "rippleDb": 3.4 },
                       "afterBestDelay": { "extraDelayMs": 0.08, "invertUpper": false,
                                           "sumLossDb": -0.1, "dipDb": -0.4 },
                       "phase": { "phaseAtCrossoverDeg": -12.4, "consistency": 0.82,
                                  "currentScore": 0.79, "bestScore": 0.88,
                                  "bestExtraDelayMs": 0.08, "bestInvert": false, "fitRmsDeg": 14 } } ] },
        { "label": "no bank on C", … } ] },
    { "id": "op-2", "probe": "junctionDelay", "junctionId": "left:C-D",
      "sides": [ { "side": "left", "bandHz": [1000, 4000], "searchHalfWindowMs": 2,
                   "candidates": [ { "extraDelayMs": 0.08, "invertUpper": false, "scoreDb": -0.2,
                                     "sumLossDb": -0.1, "dipDb": -0.4, "chosen": true }, … ] } ] },
    { "id": "op-3", "probe": "excessGroupDelay",
      "channels": [ { "id": "B:left", "series": { "columns": ["frequencyHz","excessGdMs"], "rows": [ … ] } } ] }
  ] }
END_RESONALYZE_AGENT_PROBE_JSON
```

`conventions` spells out what each figure is; three of them decide how the
document is read:

- an entry's `sides[]` are read on the entry's OWN junction band (`bandHz`, an
  octave each side of where its two edges hand over — their geometric middle
  when they differ), which is what the panel and the package show for a
  junction; `shared` is the same three figures on the one band every entry of
  the probe was read on (`sharedBandHz`, drawn from every edge of every entry).
  Entries whose corners differ are comparable only on `shared` — and where a
  variant holds its two edges more than two octaves apart, which is a hole
  rather than a handover, `shared` is also the only band that contains them
  both.
- `afterBestDelay` is what the junction would measure once the alignment had
  been re-run for THAT entry. The delays in the tune were set for the tune as it
  stands, so it is the fair comparison between entries; a reply that wants that
  delay applied asks for `runAutoDelay`.
- `phase` is the pair's cross-phase over the same window as the sums. Compare it
  BETWEEN the entries of one probe; the package's `junctions[].phase` is read
  through the panel's own gate and is not the same number.

`packageId` names the package the reading belongs beside, and
`sessionMatchesPackage` says whether the session still is the one that package
described. `sessionChangedWhileReading` appears, true, when the tune moved across any
reading's boundary — the readings then do not all describe one state, and a
fresh probe settles it. Its absence means every reading here was taken off the
same session (an edit made and undone entirely inside one reading changes
nothing it holds, and is not flagged). An entry or a whole probe that could not be read carries
`unavailable` with the reason, and the others still stand.

## 3. Privacy

The package never contains file paths, folder names, the Windows user name,
history ids or raw impulse responses. A channel's `displayName` is the name the
panel shows (a file name without its folder). `notes` is whatever the user
typed. Resonalyze makes no network request in any part of this workflow.
