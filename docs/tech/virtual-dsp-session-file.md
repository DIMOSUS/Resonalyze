# Virtual DSP session file

The Virtual DSP tool persists its whole state — channel pairs, their DSP chains, the processor profile,
calibration, target and view flags — as a JSON project (`format: resonalyze-virtual-crossover`). The same
format serves two writers: the internal autosave in the application data folder (debounced, rewritten on
every knob turn) and the user's exported sessions, which travel between machines together with their
measurements.

Code lives in `source/Tools/VirtualCrossover/`:

- `VirtualCrossoverProjectFile.cs` — the project, `VirtualCrossoverChannelPairSettings`,
  `VirtualCrossoverChannelSettings`, `VirtualCrossoverTargetSettings`, `VirtualCrossoverCalibrationSettings`,
  `VirtualCrossoverPhaseGateSettings`; load, save, migration and validation.
- `VirtualCrossoverSourceLocator.cs`, `VirtualCrossoverSource.cs`, `VirtualCrossoverSourceRules.cs` — resolving
  and admitting measurements.
- `VirtualCrossoverCalibrationSelection.cs`, `SessionCalibrationFiles.cs` — calibration carried by sessions.
- `FirKernelWire.cs`, `FirFilterFiles.cs` — FIR kernel storage and import/export.
- `VirtualCrossoverChannel.cs`, `VirtualCrossoverChannelState.cs` — runtime model of a block and its sides.
- `VirtualCrossoverLimits.cs`, `VirtualCrossoverChannelEdit.cs` — the channel fields' ranges, and a block edit
  written back one field at a time.
- `VirtualCrossoverSideLock.cs` — the L/R Lock.
- `VirtualDspEqHandoff.cs` — PEQ handoff to and from the EQ Wizard.

## Schema versions and migrations

`VirtualCrossoverProjectFile.CurrentVersion` is 11. An incompatible change bumps it and adds a step to
`Migrate`, which runs before `Validate`. Files from a newer version (a downgraded app) are never migrated:
validation rejects them, `LoadOrDefault` moves the file to `.backup` and starts fresh, `LoadFrom` throws.

Legacy payload properties (`Channels`, `LegacyEnabled`, `LegacyAllPassType`, `LegacyPhaseGateOffsetMs`,
`CalibrationMode`, …) exist only so old files deserialize; the scalar ones are nullable to tell "absent" from a
value, and only `Migrate` reads them.

- **v1 → v2**: single-sided channels become the LEFT side of a pair (historical measurements were the user's
  only side); the right side starts empty.
- **v2 → v3**: v2 had only a fixed gate and a numeric common detrend, so `PhaseWindowMode` becomes `Fixed`.
- **v4 → v5**: the gate *placement* became per side. Both sides inherit the old shared offset/detrend (null
  "follow earliest arrival" migrates to the same meaning), so a migrated project draws exactly as before.
  Window lengths kept their v4 names and deserialize straight into the project.
- **v5 → v6**: the three fixed calibration modes become an id into the calibration list; 90° maps to the
  entry the settings migration creates from the old second slot.
- **v6 → v7**: Mute, Bypass and the two curve toggles move from each side onto the pair (they describe the
  block; per side, switching sides silently changed what the plot drew). The pair inherits from sides that
  actually carry a measurement, so a project that could not disagree opens unchanged. Where two loaded sides
  disagree, the louder answer wins — muted, bypassed and "curve shown" survive — because a mute lost in a
  migration is the one outcome the tuner cannot see coming.
- **v7 → v8**: the per-side all-pass stage becomes a band of the PEQ bank, where hardware EQ tables hold it
  (AP1/AP2). The band realizes the same biquad bit for bit (pinned by `AllPassBandTests`). The legacy type is
  stored as a string, not the enum, because an unknown enum name would throw during deserialization, before
  `Migrate` could tolerate it; an unreadable type or bad numbers degrade to "no all-pass". A side whose bank
  already held the full 32 bands loses its last gain-bearing band rather than the all-pass: that band is a
  magnitude correction Auto Tune can propose again, while the all-pass sits on a junction aligned by ear. The count
  (`migratedFullBanks`) is reported once via `MigrationNoticeText`.
- **v8 → v9**: every block gets a `Zone` (front/rear/centre/sub), guessed from the mono flag and filter.
  Nothing else changes, so a wrong guess costs one combo box; guessing beats leaving everything Front, where a
  shared subwoofer would be wrong for every install.
- **v9 → v10**: the channel phase control (`PhaseRotationDegrees`, `DspProcessorPhaseControl`). Purely
  additive, but the version is bumped because the reverse is harmful: an older build would draw a rotated tune
  without the filter; refusing the file is how it says so.
- **v10 → v11**: the FIR stage, bumped for the same reason.
- **Always**: the stereo scene's wire sign and layout flag are re-aligned (see [Stereo scene](#stereo-scene)).

`MigrationNoticeText` lists what a load had to drop — bands given up to a migrated all-pass, and phase rotations or FIR kernels
cleared because the named processor has no such control (reachable only by a hand-edited file, but a silently
dropped filter is exactly what the notice exists to prevent).

### Additive fields without a version bump

Many fields are optional and additive: absent in older files, they default to the old behaviour, and an older
build ignores them. Patterns used repeatedly:

- **Flag beside a legacy enum**: `DspPlotMode` never stores `Correlation`/`Coherence`; those are
  `DspPlotCorrelationView`/`DspPlotCoherenceView` flags (correlation wins if both are set), so an older build
  opens on magnitude instead of failing on an unknown enum string. Write through `SetDspPlotMode`.
  `PsychoacousticSmoothing` likewise sits beside the plain `SmoothingInverseOctaves` width.
- **View flags with a fallback**: `ShowImpulseView` wins over `ShowPhaseView`; `ShowGroupDelayView` wins over
  phase and loses to impulse, and the panel writes `ShowPhaseView` beside it; `ShowStepView` wins over all and
  writes `ShowImpulseView` beside it — each older build opens on the nearest view it has.
- **Per-view Sum visibility**: `ShowSumCurve` is the magnitude answer (the only one old builds know).
  `ShowSumCurveOnPhase` inherits it when absent; `ShowSumCurveGroupDelay` inherits the phase answer;
  `ShowSumCurveStep` inherits the magnitude answer (the impulse view draws no Sum).
- **Null resolves at read time**: `SpatialAverageMode`, `DspProcessorPhaseControl`, `DspProcessorFirFilters`
  are null until the user chooses; the choice is then stored and stops being guessed. A project whose
  measurements carry arrays reads them without the user finding a menu. `SpatialAverageMode` is also stored
  as soon as the panel has a capture to guess from, so a later measurement cannot flip it.
- **Empty stored as absent**: `AiNotes` (installation notes sent with every Copy for AI package) so a session
  that never had notes serializes byte for byte as before.
- **Sum loss window**: `LossWindow` is null in files before the selector. `SumLossWindowMode` then answers
  `Full` when the old `ShowLossCurve` flag is on and `Direct` otherwise. The asymmetry is deliberate: the
  flag's default was off, so "false" cannot be told from untouched and gets the new default; "true" could only
  be set by hand, and it always meant the steady-state curve, so switching it to the direct read would change
  the meaning of a number the user chose to watch. `ShowLossCurve` is still written so flag-only builds agree.
- `GroupView` defaults to `FrontAndSub`, which draws exactly what pre-group files always drew.
- `ShowHybridCurves` is stored with the captures it needs. The tick is intent and survives a load even when a
  playing channel no longer has an average; the hybrid is then simply not drawn, so the session still opens honest.
- `acousticLowPass` / `acousticHighPass` (per side, absent unless stated) are the acoustic crossover goals: a family
  and a slope, the corner always the electrical one. A goal stays with its edge while the kind does not run that
  edge, as the edge's own corner and slope do, and is read — by Auto Tune's target, as stated on the card — only
  while it runs (`RunsLowPass` / `RunsHighPass`).
- `JunctionTune` is the Tune junction dialog's last question (`VirtualCrossoverJunctionTuneSettings`): absent until
  the dialog has been opened, so older sessions round-trip untouched. It is a convenience, not part of the tune, so
  it is the one block that never refuses a file: `Sanitize` drops a family, goal or corner window it cannot use
  instead of throwing — a window outside the corner boxes' own 10 Hz–24 kHz included, since no decimal field holds
  every finite double. Corner windows are keyed by the junction label the dialog lists (`B-C`), since a window
  belongs to one junction and everything else in the block is one choice.

## Source paths

A channel side stores `SourceFilePath` (absolute), `SourceRelativePath`, and optionally `HistoryEntryId`. On
load a source re-resolves by history entry first, then by path, then via `VirtualCrossoverSourceLocator`.

Absolute paths are written on the machine that measured, so they are the first thing a session loses when it
travels. Because measurements normally travel *with* the session, the folder the session was opened from
(`ProjectDirectory`, null for the autosave) is the one honest hint. `VirtualCrossoverSourceLocator.Locate`
tries, in order:

1. The stored path, if it still exists.
2. The path relative to the exporting session's folder (`Relativize`). This reproduces the original layout
   exactly, including sibling folders (`..\v4\mid.json`), which no search under the session folder can reach.
3. The stored path's tails, longest first (`v5\left\woofer.json`, `left\woofer.json`, `woofer.json`), at most
   `MaximumTailDepth` = 6 components long (the file name plus five folders). Longest first because the number of agreeing components is the only
   evidence a tail match has: a tree holding both `left\woofer.json` and a different `woofer.json` would
   otherwise swap two measurements of the same rate silently. This is also the only route for sessions
   exported without relative paths. Collection stops at the drive/UNC root.

No step enumerates a directory — only paths the stored ones name are probed, so a same-named file in an
unrelated folder is never picked up. A rooted "relative" part is refused, so a hand-edited session cannot gain
a second absolute reference. `Relativize` returns null across volumes and for paths that are not fully
qualified.

**Relative paths are a property of the write, not of the project.** `SaveTo` (export) writes paths relative to
its own folder; the autosave and the reset backup write none, since no measurement sits in the application
data folder and a leftover value would be a confident wrong answer if that file were carried elsewhere.
`WriteWithExportRelativePaths` swaps the values in around serialization and restores them afterwards: the
in-memory values belong to the session the project was imported from and are still needed — for instance by
the relink prompt, behind whose modal dialog the debounced autosave can run. Spatial-average captures
(`SpatialAveragePath`/`SpatialAverageRelativePath`) follow the same discipline.

Spatial averages are referenced, not embedded: a capture is about 900 kB per channel, a session carries up to
sixteen sides, and the autosave rewrites on every knob turn.

`VirtualCrossoverSourceRules` admits a source only with a loopback transfer IR and at the project's single
sample rate. `ResolvedVirtualDspSource.FromResult` also refuses measurements without absolute time
(`TimingReferences.HasAbsoluteTime`): summing drivers sums their arrivals, and an imported arrival is set by
when the recorder started, a non-causal loopback by something other than the tract.

## Calibration

The project stores the microphone calibration as the **curve itself** (`Calibration`: name, file name,
ascending `[Hz, dB]` points) plus `CalibrationId`, the entry of *this* machine's calibration list it maps to. A
calibration describes the microphone the measurements were taken with, so it travels with them; a few dozen
points cost nothing next to the impulse responses.

A project nothing was saved to (`VirtualCrossoverProjectFile.CreateNew`: no file, or an unusable one) starts on
Own. It is a factory rather than a property default because a stored `null` is a deliberate Off, and a schema-5
file without the field must reach its legacy-mode migration unchanged. Reset binds `ForReset`, which keeps the
selection and its curve and defaults everything else.

The id is only a hint. Slot-style ids such as `90deg` are minted on every machine that migrated a legacy 90°
slot, so two machines' ids agreeing says nothing about their files. `VirtualCrossoverCalibrationSelection.Resolve`
therefore decides by curve content (`CalibrationFile.SameCurve`):

- **Own (as measured)** (`OwnId`) is a rule, not a curve: each measurement is read through the calibration its
  own file recorded (a microphone array corrects each capsule by its own file; channels may have been measured
  with different microphones). It persists as the id alone — storing one measurement's curve would read every
  channel through it elsewhere. It is listed first after Off because it needs no configuring.
- The named entry is kept when it still holds the same curve.
- **Autosave** (`imported` false): the entry follows its file even if the curve changed (the user edited it);
  a missing file stays selected and marked, and `Persist` keeps the stored curve so the record of what the
  session was tuned with survives an unplugged drive.
- **Imported**: any configured entry with the same curve is selected under its local id. Otherwise the curve
  is offered as the session's own item (`SessionId`, never written — its persisted form is the curve without
  an id), with notice `CarriedBySession`; `SessionCalibrationFiles` names the file and entry if the user keeps it.
  An autosave whose curve names no configured entry lands on the same item, without the notice.
- An id with no curve (pre-curve sessions): a generated id cannot be minted twice, so it proves the same
  machine; a slot id matches by name only (`MatchedBySlotName`). If the entry is absent the previous selection
  is kept (`KeptPrevious`) rather than replaced with nothing.

Validation requires at least two distinct frequencies: duplicates merge into one knot, and a one-knot curve
would load as available, apply nothing, and fail validation on the next save.

## EQ target

`VirtualCrossoverTargetSettings` is a flat by-value copy of the target shape and styling, including an
imported house curve (`ImportedName`, `ImportedCurve`), not a preset reference or a file path. Presets are a
starting point whose numbers can change between versions, while a session must open aiming at exactly the
curve it was tuned against. Loading applies it as the app's target (the EQ Wizard owns that definition); a file
without a target keeps the current one and writes it on the next save.

A bad field does not fail the load: `ToCurve` normalizes it (the file allows named floating-point literals, and
the target also fills the settings dialog), and the importer drops a curve too damaged to be a shape, leaving
the parametric terms in charge. Normalization happens only on the way out of the file, since the app itself
produces sound values.

## FIR kernel storage

A side's FIR kernel (schema v11) is stored **in** the session, like PEQ bands: a kernel is part of the tune and
the tune must travel whole with nothing to relink. Files are only import (`FirFilterFiles.Load`) and export.

- **Wire form** (`FirKernelWire`): `{ "sampleRateHz": 48000, "taps": "<base64>" }`, taps as little-endian
  float64. Unlike impulse responses (float32), a kernel is a designed set of numbers; a rePhase text kernel
  carries sixteen digits an export must give back unchanged. At the tap ceiling that is 1.4 MB per side; the
  kernels car processors run are a few hundred kB. `FirFilter` itself is not serializable (span-typed taps).
- **Autosave cost**: unlike spatial averages, the kernel is in the file by decision. The wire form is built
  once per immutable kernel instance (`ConditionalWeakTable`) and reused for every save.
- `sampleRateHz` is the rate the source file declared, kept only for the block's warning. The taps are
  convolved at the processor's rate regardless, so a kernel designed at another rate is a different filter
  there; export writes the processor's rate for the same reason.
- A malformed block throws `JsonException`, failing the load like any other bad field instead of opening with
  some other filter. An impossible declared rate is damage, not "none".
- **Import**: WAV (first channel) via `AudioFileCodec`, or text (`.fir`/`.txt`) via `FirFilterTextFile`. WAVs
  longer than 10 s are refused by the decoder's duration guard (the tap ceiling at the lowest rate is under
  3 s), so program material picked by mistake is not loaded into memory first.
- **Export**: 32-bit float WAV (integer PCM would clip taps past ±1 into another filter) or text with a header
  the import reads back, doubles printed with `"R"`. A designed kernel adds its design description as a
  comment line — its only record once the file has left the session.
- `FirDesign` (a `FirCrossoverDesign` from the FIR Constructor) is kept beside the taps so the kernel reopens
  as the crossover it is. Validation refuses a design without its kernel or with a different tap count (the
  file was written by something else and would name a crossover it does not run). Slopes are checked against
  the constructor's list, which runs steeper than hardware (the IIR check would refuse anything past
  48 dB/oct), and family/slope only where the design method reads them.
- `ClearUnavailableFirFilters` detaches kernels when the named processor has no FIR stage (the invariant: a
  kernel in the file means a device that can convolve it), on load and whenever the processor changes.

## Effective crossover

`VirtualCrossoverChannelSettings.EffectiveCrossover` is what everything asking "where is this channel cut" reads
— the junction list, the Auto Tune window (`VirtualDspEqHandoff.PassbandFor`), the phase control reference: the
IIR crossover when on, otherwise the FIR crossover's corners. It is never used to build the chain: `ToChain`
builds the IIR stage from `CrossoverKind` alone and the kernel is already the FIR stage, so nothing is filtered
twice.

A channel may legitimately run both stages, and then the effective crossover speaks for the IIR alone. Where the
question is how wide the channel plays rather than which spec to read, `PassbandFor` narrows the band with
`FirDesignCrossover` as well — the designed FIR's own corners, scaled the same way — so the Auto Tune window of a
FIR high-pass beside an IIR low-pass starts at the FIR's corner instead of at 20 Hz, which would send the fit down
the kernel's whole stopband. The junction list and the phase control still read the IIR alone there; that is Auto
crossover's reading of a pair, not this file's.

A FIR crossover's corners are where the kernel *cuts*, not where it was designed: taps designed at one rate and
run at another scale every frequency by the ratio, so a 48 kHz design on a 96 kHz processor cuts an octave
higher until rebuilt (the block's FIR button is red meanwhile). Corners are scaled by `FirRunSampleRateHz`
(stamped by the panel, not stored) over the design rate; before a stamp they are the design's own. Only the corner frequencies of the returned spec are
meaningful; when neither crossover exists the kind is Off and the edges are returned only to avoid nulls.

Both IIR edges are validated even when the kind ignores them, because the UI shows them greyed out and they must
round-trip; Chebyshev ripple is validated only for that family (outside (0, max] its pole math is NaN).

## Channel field ranges

A side's numbers are held to the ranges of the block fields that edit them. `VirtualCrossoverLimits` is their one
owner: the fields take their bounds and decimals from it, `Validate` refuses a file outside it, and the AI review
holds a reply to it.

| Field | Range | Field step |
| --- | --- | --- |
| Gain | −60 to +20 dB | 0.1 dB |
| Delay | 0 to 100 ms | 0.01 ms |
| Crossover corner, both edges | 10 Hz to 24 kHz | 1 Hz |
| Chebyshev ripple | above 0, up to 3 dB (the field: 0.1 to 3.0) | 0.1 dB |
| Phase rotation | 0 to 354.375° | 0.001° (edits snap to the device's 5.625°) |

**The range is the field's.** A block shows a value outside its field clamped while the chain runs the stored one,
so the display would lie. Against the file format's former ±60 dB and 1000 ms only the upper gain and delay bounds
moved, and nothing but a hand edit ever went past the fields' +20 dB and 100 ms: the fields and the AI review stop
there, Auto delay stays under the processor's ceiling (50 ms unless the catalog says otherwise), and the crossover
wizard and gain balance only cut. So the loader refuses such a value as it refuses any other impossible one.

**The decimals are not.** A file may hold a value finer than its field shows — 83.7 Hz, 1.234 ms — from a hand edit,
or a corner or ripple from an AI import reviewed before replies were held to their steps. Refusing it would set aside
autosaves that loaded before; rounding it on load would change the tune on open. It is kept, the field shows it
rounded (`NumericFieldRange.Clamp`), and it changes only when that field is edited. The Chebyshev ripple's field
starts at its first step, 0.1 dB, so a finer positive ripple is the same case and the file keeps the buildable range.

**An edit writes its own field.** The block names the field that changed (`VirtualCrossoverChannelField`) with what
it shows (`VirtualCrossoverChannelShown`), and `VirtualCrossoverChannelEdit` writes that one field. Writing the whole
block back rewrote every rounded value on the first unrelated edit, and the [side Lock](#side-lock), reading moves by
difference, then saw the rounded crossover as a move and carried it over a deliberately different hidden side.
Family and slope are written together, since the slope list follows the family; when an edge turns Chebyshev, a
stored ripple it cannot build (another family's ripple is not checked) is replaced by the shown one. Zone and Mono,
which their fields show exactly, are stored on every edit: a Centre zone ticks Mono while a load's events are
silenced, and a hand-edited stereo Centre is made mono by its next edit.

**The AI review holds a reply to range and step** for gain, delay, corners and Chebyshev ripple, so a value a reply
moves is one the field shows unchanged. A corner, or a Chebyshev edge's ripple, that a reply restates exactly as
stored passes, so echoing a finer stored value is not refused; a ripple stored under another family counts as moved
once the edge turns Chebyshev. See [agent-bridge.md](agent-bridge.md#review-rules).

## Phase control

`PhaseRotationDegrees` (0 = unused) is the processor's channel phase control. Its reference frequency is not
stored: `PhaseReferenceHz` takes the low-pass corner on a subwoofer zone and the high-pass corner elsewhere,
from the edge **as configured**, whatever `CrossoverKind` engages. This is measured behaviour: on the bench a
bypassed low-pass and one set to slope OFF both went on supplying the reference. `ToChain(zone)` requires the
zone so a subwoofer call site cannot silently get the other rule, and `PhaseRotationSpec` records which corner
the angle follows because the junction search moves one corner at a time. Validation checks only the range,
not the hardware's 5.625° grid (editors snap).

`DspProcessorPhaseControl` is not a view switch: saying the device has no such control means rotations are not
part of the tune, and `ClearUnavailablePhaseRotations` zeroes them where the user sees it happen (the dialog
reports the count), rather than leaving an invisible all-pass in every curve and a "Phase 90°" line on the
tuning sheet.

## DSP processor

`DspProcessorModelId` names a catalog entry; an absent or unknown id opens as Custom with the stored numbers.
`DspProcessorSampleRateHz` is the only rate simulated biquads are designed at, independent of the measurement
rate (a 48 kHz sound card can carry a 96 kHz processor). Null means "follow the measurements"
(`DspProcessorRateFollowsMeasurements`), which is deliberately distinct from a stated rate that equals the
measurement rate today: the stated one keeps its number when measurements are replaced. `SetDspProcessor`
stores that intent. A named model answers from the catalog, so a corrected preset corrects every project.
`DspProcessorQConvention` only changes how Q is printed on tuning sheets; every simulated band is an RBJ biquad.

`VirtualCrossoverChannel.ProcessorSampleRateProvider` reads the project's rate on demand rather than copying it
into channels (a copy goes stale); unset, it falls back to the measurement rate.

## Stereo scene

`StereoSceneOffsetMs` is written as a magnitude with the steering layout in its **sign** (negative = right-hand
drive) — the exact pre-flag format — so a build from before `StereoRightHandDrive` reads an RHD session correctly
and even resaves it without flipping it to LHD (the unknown flag would not survive such a resave; the sign
does). `StereoRightHandDrive` is kept explicitly so a zero offset still remembers the layout: false = LHD (left is
the timing reference, right leads by the offset), true = RHD (mirrored).

A zero RHD offset still needs a negative sign, but IEEE −0.0 neither compares below zero nor survives a decimal
round-trip. It is written as −`RhdZeroOffsetMarkerMs` (the constant is +0.001 ms; `SetStereoScene` applies the sign): a tenth of the UI's 0.01 ms grid and a
twentieth of a sample at 48 kHz, inaudible to old builds and read back as zero by
`StereoSceneOffsetMagnitudeMs`. The UI cannot produce a genuine 0.001 ms offset, so the marker is unambiguous.
All in-app writes go through `SetStereoScene`. `Migrate` re-aligns files carrying only one of the two: the sign is
the wider channel, so it wins over a missing flag; a set flag over a positive offset keeps RHD.

`MaximumSceneOffsetMs` = 5: beyond a couple of milliseconds an inter-side lead is an echo, not an image shift.

`StereoLevelDifferenceDb` is stored as LEFT minus RIGHT (default −1 dB: left 1 dB below right, the same image
direction as the scene offset traded as level). The UI edits a non-negative near-side cut; the stored sign
follows the layout (LHD negative, RHD positive) so older builds read it unchanged. `RearFillOffsetMs` is part of
the tune rather than a dialog default, since cars tuned for front seats and for the second row differ.

## Phase gate

The gate's **placement** (`VirtualCrossoverPhaseGateSettings`: `OffsetMs`, `DetrendMs`) is per side because the
left and right drivers sit at different distances, so their arrivals and the reflections the gate cuts do not
land together; one shared gate meant fitting it on one side threw the other's traces off. Null offset follows the
side's earliest estimated IR start until pinned; null detrend follows the earliest processed arrival.

Everything that decides **how** phase is read stays project-wide — window lengths (they set frequency
resolution), window mode, detrend mode, FDW cycles — because two sides read through different windows are not
comparable, and comparing them is what the view is for.

Defaults: left 1 ms + plateau 30 ms + right 10 ms = 41 ms, deliberately longer than Phase Response mode's
junction-length gate. By the 1/T criterion a 6 ms gate holds one period only down to ≈170 Hz, short exactly
where sub-to-midbass junctions live; 41 ms reaches ≈24 Hz (nominal reach, not a promise about the phase). FDW is
on by default so the long window's reflection tail does not reach mid and high junctions, where channels are
timed on the direct arrival; 8 cycles is the gentlest of the three counts, keeping the most late detail — the
suppression is there to make junctions readable, not to reduce every channel to its first cycle.

A window with no length at all (all three at 0 ms, which the Gate dialog's fields allow) reads nothing, so
`SetPhaseGateLengths` and `Validate` put the defaults back instead of refusing: a refusal made every autosave
throw, and a file holding such a gate would have been set aside as unusable at the next start.

## Autosave, reset backup and load fallback

- `LoadOrDefault` loads the autosave and falls back to a fresh default when the file is missing, unreadable or
  from an unknown version — tool state is a convenience and must never block startup. An existing unusable file
  is first renamed to `.backup` (`BackupNoticePath` tells the user), because the next scheduled save would
  overwrite it and a downgrade or bug must not cost the tuning session. The move is best effort (the file may be
  locked).
- `LoadFrom` (import) throws on a broken or incompatible file: an explicit import deserves an explicit error.
- Nothing is saved before the first show. Until `OnPanelShown` starts the stored load the project is the
  constructor's placeholder, so `ScheduleSave` drops edits to it: a panel built and disposed unseen (a test, a
  harness, the host pushing its target at startup) must not write that placeholder over the stored session.
  Nothing real edits earlier: a dropped session file shows the tool before importing, and Load lives on the panel.
- `SaveResetBackup` writes the project **in memory** aside before Reset replaces it. Copying the autosave file
  was wrong: the file lags the screen by the debounce (and by everything on a never-written session), and a
  missing file was reported as "nothing to lose" while a whole tune stood in memory. Write failures are
  reported, not swallowed as the autosave must. One copy is kept and overwritten — "undo that reset" is always
  the last reset, and dated files would be a second archive. It has its own `.json` name (what Load session
  offers), not `.backup`, so it can never clobber the only copy of an unreadable project.

## Side lock

`VirtualCrossoverSideLock` implements the **Lock** beside the Virtual DSP side radios: while on, a crossover,
polarity or FIR-crossover edit on the shown side is written onto the hidden side of the same pair, so a
symmetric tune stays symmetric without an L→R copy after every corner move. The lock is on by default and not
persisted.

**By difference, not by hooking editors.** Every change funnels through the panel's autosave; the lock keeps a
snapshot of both sides of every pair from the previous `Follow` and copies units that differ on the shown side.
Snapshots are keyed by pair object (reference identity), so a loaded session's new pairs start fresh; the
physical sides are snapshotted, since through mono routing the right snapshot would silently copy the left. A
mono pair has nothing to mirror but its physical right side is still remembered for a later Mono-off.

**Runs that write both sides call `Remember`** before the save, and nothing of them is carried. This is a rule,
not a heuristic, because a difference cannot tell such a run from a hand edit: auto delay may flip the shown
side's polarity and keep the hidden one; a junction tune or the crossover wizard writes one edge onto both sides,
and a hidden side that already held it looks untouched while the shown side's whole crossover would be carried
over the hidden side's other edge; an AI import names its sides; an undo read as a difference could carry a
restored value onto a side the import never touched. A guard that carried only when the hidden side had not
moved in the same step was tried and failed on the "already held it" case. `Remember` is also called after a
rebind (loaded session, reset), otherwise the first edit would meet an unknown pair and never be mirrored.

**Engaging copies nothing**: sides that differ keep differing until that unit is next touched, so the user can
see both before deciding which to type over.

**Three units.** The crossover (kind and both edges) moves as one, or an edit meant to equalize would leave the
other corner or kind unequal. Polarity moves alone, so a corner move does not flip the other side. The FIR stage
(kernel, source name, design) is one unit for the crossover's reason; the kernel compares by instance (immutable
and shared, as L→R shares it), so an import, a Clear or a constructor return each read as a move. Gain, delay,
phase angle and PEQ are not locked: each aligns a driver against its own side's level and geometry.

**FIR carries only crossovers** (`HasFirCrossover`). An imported kernel may be a room or driver correction, which
the two sides of a car do not share, so (`CarriesFir`):

| Shown side move | Carried? |
| --- | --- |
| anything → designed | yes, unless the hidden side holds an imported kernel |
| designed → cleared | yes, onto a hidden side holding a crossover |
| imported → cleared | no — the removed kernel was a correction |
| anything → imported | no |

## EQ Wizard handoff

`VirtualDspEqHandoff.Build` sends one channel side to the EQ Wizard; `TryApplyReturn` lands the edited PEQ bank
back. The rules are UI-free so tests can hold them.

**What travels.** With `withChain` the curve is the side's measurement through its chain *without* PEQ (gain,
delay, polarity, crossover stay; the whole bank including all-pass bands goes over as the thing edited), windowed
by the gate the processed view uses. Delay and polarity are magnitude-transparent but keep the response in the
processed view's time, where the pinned offset and render anchor point. A raw handoff is the measurement under
the same gate anchored on its own start (the panel's Raw curve; a processed-view pin would clip it into the left
fade). The chain is realized at the processor's rate while the record stays at the measurement's, so the profile
travels too. The corrected preview reruns `ApplyChain` on the original measurement with the edited bank, instead
of adding the bank's ideal magnitude, which would part from the panel by several dB wherever a filter rings
longer than the window. `TargetLevelDb` is valid verbatim (same dB frame); its range is the panel's, since an
out-of-range level would come back silently clamped. The measured band travels so the wizard does not draw where
the sweep never excited.

- Neighbour phase context goes only with a chain handoff: a raw curve has no crossover or delay while neighbours
  have theirs (an LR corner alone turns 360° through the overlap), so lining them up describes no real system.
- With the hybrid view, the spatial average replaces the magnitude being fitted while the impulse response still
  serves phase — the average is where tonal balance is honest, the IR is where timing is.
- For an array, positions' agreement replaces IR coherence as the boost-gating witness. A channel without an
  average in an averaged set keeps its IR coherence, and `EqBoostabilityMask` still refuses boosts where
  magnitude climbs 6 dB on both sides within a quarter octave (a single position's interference null). What is
  lost is the array's witness for broad disagreement: on two real seven-position sets a single position departs
  from the seven-position average by at most 4.9 dB at 1/6 octave (midrange 400–700 Hz; tweeter 3.4 dB), under
  the null detector's 6 dB. It is bounded and named on the plot and in the note; a third gate was judged not
  worth its complexity.
- A bypassed block still builds the chain (the PEQ belongs to it) and the receipt says so.

**Return guards** (`VirtualDspEqReturnToken`). A return is refused — no write, wizard stays open — when it would
land somewhere else or against a curve the wizard no longer reflects. The line runs through the magnitude the bank
was fitted to or the level it was fitted against; only a polarity flip changes neither.

- `ProjectGeneration`, checked first: binding a project reuses channel objects when the count matches, so after
  an import the same object holds another session.
- Channel membership (removed channel), and `Mono`: mono routes both sides to the left set, so a changed pair
  would deliver to the other settings. A mono token addresses LEFT outright; the side state is read physically.
  The address uses `SideFor(token.RightSide)`, not the active side, since the user may flip L/R while editing.
- `SourceRevision`: a new measurement picked for that side while the session survives a tab trip.
- `Calibration`, compared by curve content: the wizard disables its own selector during a handoff, and the panel's
  selector, one tab switch away, would otherwise walk around that lock.
- `ProcessorSampleRateHz`: a bank fitted for 96 kHz run at 48 kHz is a different filter (up to 4 dB apart in the
  top octave). Only the rate is guarded; the Q convention changes printed numbers only.
- `SpatialAverage` by reference identity (a re-attached file is a different capture even with identical bytes) and
  `SpatialAverageCalibration`: Off/Own/Specific turn one capture into three magnitudes, and switching Own to the
  file the IR names, or Own to Off for an array without its own, leaves the calibration guard satisfied. Checked
  only where a capture exists.
- `GateTemplate`, reduced to what the magnitude reads (it forces Fixed and ignores FDW cycles, detrend and unwrap,
  which belong to the phase and impulse views), and `PinnedGateOffsetMs` for chain handoffs only, against the pin
  of the side the curve was gated through (`GateRightSide`), not the side shown when the bank comes back. Where an
  *auto-placed* window ended up is deliberately not guarded: it follows the earliest arrival across all channels,
  and measured, a 50 ms move changes the reading by 0.000 dB at 48 kHz and 0.078 dB at 192 kHz (the window opens
  ahead of the response and runs far past it) — two orders below anything else refused, while the guard would fire
  on a co-channel mute.
- `TargetLevelDb`: the wizard may move the level and it travels back; if the panel's level also changed, the two
  conflict and the return is refused.
- `PreviewChain`, compared whole with polarity normalized away (records give value equality; PEQ is null on both).
  Measured through the real filter-then-window path across UI ranges, the worst shape shift from one edit:
  polarity exactly 0 at every rate; delay 0.000 dB at 48 kHz but 1.70 dB at 192 kHz, where the rate clamps the
  window to 171 ms; an all-pass (when it was a chain stage) 0.27 dB at 48 kHz and 4.77 dB at 192 kHz (40 Hz,
  Q 20 — 318 ms of group delay against that window). Gain leaves the shape alone but the bank's preamp was fitted
  to an absolute target level. A raw handoff's chain is the identity and is not compared.
- `Peq`, the bank the session started from: the chain comparison excludes the PEQ, so a Load or Clear in the
  panel would otherwise be a lost update. All-pass edits are caught here, since all-passes live in the bank.

**An unedited bank goes back as it came.** The wizard holds every band to its strips' limits and steps (whole Hz,
0.1 Q and dB, Max Cut and Max Boost), and Load PEQ, the session file and AI replies all take banks past them. A
bank returned without an edit is therefore the channel's own, not its held copy: Return otherwise turned an
imported −20 dB notch into −15 dB. A returned bank equal to the channel's writes nothing and keeps the name the
bank was loaded under.
