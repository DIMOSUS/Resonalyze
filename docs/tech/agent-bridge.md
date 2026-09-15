# Agent Bridge (app side)

The Agent Bridge lets a user hand a Virtual DSP tune to an external chat assistant and bring its
answer back. **Copy for AI** puts a package (inline rules plus one compact JSON document) on the
clipboard; **Import AI proposal** reads the assistant's reply from the clipboard, reviews it and
applies what the user ticks. What the assistant is told, and the normative wire format, live in
[`docs/agent/AGENT_GUIDE.md`](../agent/AGENT_GUIDE.md) and
[`docs/agent/PROTOCOL.md`](../agent/PROTOCOL.md). This document covers how the application builds,
validates and executes that exchange.

Where the code lives:

- `source/Tools/VirtualCrossover/VirtualCrossoverPanel.AgentBridge.cs` - the panel side: gathering
  package inputs, the session snapshot and fingerprint, the import flow, headless engine runs,
  probes and undo.
- `source/Integration/AgentBridge/` - UI-free pieces: `AgentProtocol` (words and limits),
  `AgentPackageBuilder` / `AgentPackageModels` / `AgentPackageInputs` / `AgentCurveSampling`
  (package), `AgentProposalParser` / `AgentProposal` (reply), `AgentProposalValidator` /
  `AgentOperations` (review), `AgentProposalApplier` (commit and undo), `AgentProbeBuilder` /
  `AgentSeriesProbe` (probes), `AgentDiagnosticBuilder` (named diagnostics), `AgentPeqHeadroom`,
  `AgentSessionFingerprint`, `AgentSessionSnapshot`, `AgentClipboard`.
- `AgentProposalDialog` (review table) and `AgentProgressDialog` (step window) in
  `source/Tools/VirtualCrossover/`.

## Package gathering

`CaptureAgentPackageInputsAsync` reads everything a package is built from, and it runs the same
computations the screen runs: the coordinator's processed responses for both sides (its cache makes
the shown side free), the metric block's curves and read-outs per side, the lower plot's junction
views per adjacent pair, and the stereo and group deltas. The panel does no formatting; the builder
(`AgentPackageBuilder`) reads no control, project or coordinator - `AgentPackageInputs` is its whole
input. That split keeps the builder testable on synthetic curves and keeps a package number equal to
the on-screen number.

Details that matter:

- Junction rows come from the channels that **sum**, exactly as `UpdateMetric` does. A drawn but
  unsummed centre channel (high-passed with no upper corner, so its band centre falls between the
  midrange and tweeter) would otherwise invent junctions and lose the real one
  (`VirtualCrossoverMetricsTests.BuildEntries_ReadsJunctionsOffTheSummingSet`).
- The junction phase block reads through the phase gate over the summing channels with this side's
  pin; the direct-sound (FDW-8) loss is always carried beside the full loss, read off the same
  spectra, whatever the Sum loss selector shows. The two families are labelled apart and never
  compared.
- The opposite side is windowed through its own gate placement, never the active side's pin - the
  same rule as the on-screen opposite sum.
- Channel settings are cloned (`AgentOperations.CloneEditable`) because the builder runs off the UI
  thread after gathering returns and the live objects may be edited meanwhile.
- A junction view that fails to build is reported missing instead of failing the package.
- The gather is bracketed by the session fingerprint (`GatherAgentPackageAsync`): the coordinator
  revision does not cover the target level, the Hybrid tick or a gate pin, so the fingerprint is
  taken before and after and the result kept only when both agree. The caller retries once.
- **Copy for AI** writes the clipboard once, after the build succeeds, so a failure never leaves a
  partial or older package there.

## Package smoothing

Every package uses one smoothing independent of the display: Sum loss and curves compared across
sessions and users must not move with a combo box, and a dip at 1/48 octave is a different reading
from one at 1/6. The panel's psychoacoustic setting (1/6 octave at its narrowest) is what the manual
reads a tune at, so the package uses it. The panel's own `metrics` delegates smooth at the display
width (and `BuildCurves`' smoothing argument reaches only the loss curve), so the package builds its
own metric block whose delegates window through the package gate.

Hybrid (spatial-average) curves and their sum are the exception. The manual reads them with
smoothing **off**: the average has already removed position-dependent wiggles, and a
fractional-octave window straddling a crossover skirt pulls the level toward the passband exactly
where slopes are judged. Off cannot travel on a 12-points-per-octave grid, so they go at the grid's
width, 1/12 octave (`AgentHybridSmoothingInverseOctaves`). Point-measurement fallbacks entering the
hybrid sum are built at the hybrid width too, so nothing is smoothed twice at two widths.

The package states the spatial-average status as one word (`none`, `capturedNotShown`, `partial`,
`active`), counted over the channels the current view shows. "Drawn" is read off hybrid curves
actually present, not off attachments: a capture the mode does not read, or a tick the view cannot
honour, leaves the point measurement in charge, and that is what the assistant must tell the user.
Both `HybridTicked` and `HybridDrawn` travel because "ticked but not drawn" needs a different fix.
Hybrid curves are carried on the impulse responses' level axis (shifted by the set's datum, as
drawn), so every column of a channel compares with every other.

## Package size

Numbers tokenize at four to five tokens each, so an 80 KB package (`AgentProtocol.TargetPackageBytes`)
is already tens of thousands of tokens. The builder is pure (same inputs, id and clock produce the
same bytes) and trims deterministically:

1. **Thinning first.** `AgentSampling.Ladder` starts at the nominal densities and steps thinner. The
   junction grid and lag series thin first (they are read for shape around one corner), then the
   broadband grid. The last step still resolves a third of an octave and a dozen lags - below that a
   curve stops being a curve.
2. **Omission second.** Only if the thinnest package is still over target are whole optional series
   dropped, in the fixed `OmissionOrder`, and named in `omitted`. The direct-loss curve column goes
   first: its figures (`sumLossDirect`, `totalSumLossDirect`) stay, the full-loss column already gives
   the shape, and on the reference car that column alone pushed a package over target and cost it the
   junction sweep, which is worth far more.
3. Past the ceiling, with every optional series gone, nothing is copied.

Every figure (sum loss, dips, phase read-outs, target datum) is computed on full-resolution curves
before sampling, so thinning changes what rows show, never what numbers say. `SumVsTargetDb` is a
median over the nominal grid - a junction dip or the sub's roll-off is interference or a band edge,
not a level. Sampling uses fixed log grids (`AgentCurveSampling`) independent of plot width or zoom;
holes in a measured band are reported as nulls, never bridged. A column a channel has nothing for is
left out rather than filled with nulls.

## FIR in the package

`AgentPackageFir` describes a loaded kernel by file name (the path is the user's business), tap
count and **peak position** at the processor's rate. The peak position is not a group delay: for a
conventional linear-phase kernel it roughly equals the bulk delay the kernel adds, which is already
inside every curve; for a minimum-phase kernel it says nothing about delay. The protocol says so, so
the assistant never "compensates" a delay the kernel does not have. The design (`Crossover`) is
present only for FIR Constructor kernels and, like the kernel, is read-only.

## Session fingerprint

`ComputeAgentFingerprint` writes a manifest of lines and `AgentSessionFingerprint.Compute` hashes it
(16 hex digits of SHA-256, order-sensitive because block order is part of what channel ids mean). The
manifest covers everything a package vouches for that an expected current value does not already
guard:

- block order and letters, each side's measurement **by content** (a file re-measured and saved over
  its own name keeps reference, length and rate), peak, band, coherence, calibration curve by points,
  capture sessions, gain, delay, polarity, chains, FIR kernel by content and its design;
- project figures the diagnostics were computed under (phase window, calibration by id and points,
  target level and shape, AI notes);
- the shown side and view, because engines read them (Auto crossover proposes from the shown side,
  a single-sided Auto delay aligns it, Auto-tune's default source follows the view).

It excludes zoom and the smoothing selector, which change what is shown rather than what was measured.

The fingerprint is taken at Copy and again at every review, so no code path that changes the session
(picking a source, attaching a capture, moving a gate, loading a project, undoing an import) has to
remember to forget the package. The same session loaded again hashes the same, so its package stays
valid. Array digests are cached per array instance in a `ConditionalWeakTable`; this is sound because
impulse responses, coherence and imported target curves are replaced, never edited in place.
Calibration point lists are hashed on every call. The last package id and fingerprint are not
persisted: a reopened session cannot vouch for what an earlier one copied.

## Reply parsing

`AgentProposalParser` treats the clipboard as untrusted:

- The reply must contain exactly one JSON object whose `kind` names the proposal. Two are refused,
  not guessed between - guessing is how the wrong tune gets applied. Chats paste the object in a
  Markdown fence with arbitrary text around it, so the object identifies itself; the older
  BEGIN/END marker envelope is still read because assistants copy what worked, but chats that put the
  markers outside the copyable block are why the kind became the identifier.
- A brace scanner that understands JSON strings finds candidates. Each unclosed brace walks to the
  end of the text, which is quadratic on a minified paste, so the scan has a budget
  (`8 * length + 1 MiB`); past it the reply holds whatever was found.
- Deserialization uses its own strict options, not the session loader's (which tolerate hand edits):
  no comments, trailing commas, named float literals, unknown properties or case games. `required`
  only checks presence, so JSON nulls are caught by `NotNull`, including nested elements like
  `"variants": [null]` that would otherwise surface as an uncaught `NullReferenceException`.
  `extensions` is the one ignored open door for future additive fields.
- Each operation is mapped on its own, so one bad object becomes one rejected review row.
- `summary` and `reason` are wanted, not required: refusing over missing prose costs a chat round
  trip. Blank equals missing, and the review says the prose is absent. Only blanks within length
  limits collapse, so an over-limit string is still refused.
- Enum names must match the published names exactly (`TryParseName`), because `Enum.TryParse` would
  also accept other casing and numeric strings.
- `MaxJsonDepth` is 12: a probe variant carries a PEQ bank two levels deeper than a settings
  operation's (root, operations, operation, variants, variant, changes, change, peq, bands, band).

## Review rules

`AgentProposalValidator.Review` decides admissibility only - a valid proposal can still be a worse
tune, so nothing is ever called "verified". Every trial edit goes to a copy of the channel settings
and is judged by the same `VirtualCrossoverChannelSettings.Validate` the session loader runs, and
`AgentOperations` is the single path that touches settings for both review (copy) and commit (live),
so what was reviewed is what is applied.

Value rules:

- Gain and delay are held to the channel block's dialable range, narrower than the file format's
  (±60 dB, 1000 ms), because the block would clamp a wider value on first touch and silently move
  the tune. Auto delay inputs and the target level are held to their dialog fields for the same
  reason. `AgentProposalValidatorTests` pins these constants to the controls.
- Expected current values are compared **exactly**: the package prints round-trip values, so a
  tolerance would admit a reply reasoned about a different value. A PEQ bank is expected by
  `AgentPeqHash` (12 hex digits of SHA-256 over bands in order, then preamp), so any edit or reorder
  changes it.
- Headroom is judged on the net response (`AgentPeqHeadroom`), never band signs: a boost inside a
  wider cut or under a negative preamp asks for nothing, while a net rise above unity clips a
  full-scale signal. It is a warning; below 0.05 dB a rise is bilinear warping and rounding. The
  peak search covers 20 Hz to min(20 kHz, Nyquist) on a log grid plus every band centre (Q is
  unbounded), refining each local maximum by golden-section search in log frequency.
- A crossover over a side already cut by a FIR crossover, or a junction tune writing IIR edges over
  one, is allowed (two crossovers are a legitimate chain) but warned, so the red FIR indicator is
  explained.
- Engine inputs a reply omits are not judged: they are the panel's own defaults.

Set rules, applied after per-row checks:

- Two applicable edits of the same parameter on one channel are both refused; neither wins.
- Each engine runs once per import; the first request is kept. Probes are exempt (a second probe is
  another question) and bounded by budgets instead.
- The target level is one project datum: only the first stated level stands.
- The Auto crossover wizard rewrites every junction and runs first, so a junction tune beside it is
  refused.
- A hand-written value an engine would overwrite is refused in favour of the engine (which computes
  the number). Only an engine this build can run overwrites anything.
- Final-state notes (`FinalStateNotes`) run last over applicable rows, so they never describe a state
  produced by a refused row. The stale mark comes after every note.

### Junction zone Q

`JunctionQLimit = 2`: within an octave of one of the channel's active corners (the span the panel's
junction band covers) a narrower bell turns the channel's phase by tens of degrees where the pair's
sum is built, and a dip that close to a crossover is more often pair interference than the driver.
Only bells are checked - shelves are wide by nature and an all-pass at a junction is intentional.
FIR crossover corners count where the IIR crossover is off. The check runs on the channel's final
state, since a bell can land in a zone because another row moved the crossover.

### Stale session

`StaleSessionReason` explains why the session cannot vouch for the reply's package: the reply names
none while asking for an engine; no package was copied since the session opened (or it came from
elsewhere); it is a different package than the last copied; or the fingerprint changed since that
copy. A reply of settings rows alone that names no package is taken at its word, since each row
carries its expected value. When stale:

- settings rows stay, judged on their expected values, but are offered **unticked** and marked - the
  expected value can still match after the measurement it was reasoned from was replaced;
- engine requests are refused, because an engine reads the session as it is now, which the assistant
  has not seen;
- probes stay: they write nothing and read the current session, which is exactly what a stale reader
  needs.

## Probes

A `probe` operation reads without changing anything. Probes run **before** any write of the import,
because they answer a question about the tune as it stands. All probes of one import go into one
clipboard document (the user pastes once); a probe that cannot be computed reports in its own entry
instead of taking the others or the import down.

- Readings come off snapshots (responses, chains, gate), so the computation runs off the UI thread and
  the progress window takes nothing away from the panel.
- The fingerprint is compared at every reading's boundary, not once around the batch: a tune changed
  and changed back would pass a first-to-last check while a reading in between saw the other state.
  A change is declared (`SessionChangedWhileReading`), not refused - nothing was written, and each
  reading is still true of what it read.
- A junction probe reads the side its junction id names. Variant changes go onto copies of the two
  channels' settings, validated through the same path as settings operations (a variant that reads
  well is meant to become a proposal word for word). Copies keep the phase rotation and the FIR
  kernel, design and run rate, or the junction would be judged without filters the tune runs.
- The baseline entry is identified by position (first), never by its label - the reply's own variant
  labels may say "current" too.
- Each entry names the other junctions its own changes reach (`NeighbourJunctionIds`); a list pooled
  over the probe would send the assistant to a junction the winning variant never touched. Only
  junctions `ResolveJunction` accepts are named.
- The `series` probe (`AgentSeriesProbe`) re-gathers the package's own rows at the reply's density
  with no size target, built by the package's own methods, so columns, ids and rounding match.
- A probe document over `MaxProbeDocumentBytes` (1 MiB) is not copied: unthinned is not unbounded on
  an untrusted reply's say-so.

### Probe budgets

- `MaxProbeVariantsPerImport = 24`, counted per **import** (a per-probe cap is dodged by sending two
  probes). The budget is the user's patience, not the machine's: readings run while they wait with
  nothing to cancel, and the answer is text they paste. On a reference session a variant costs about
  65 ms and 0.8 KB, so the budget is a second or two and a package-sized text. A reply that wants more
  searched should ask for the junction tune, which searches a window properly.
- A variant may change two channels, because a junction has two.
- `MaxSeriesProbesPerImport = 1`. The variant budget cannot see series probes, and the once-per-import
  engine rule exempts probes, so without this a reply could re-gather the whole package at full
  density once per operation slot. One series probe already covers everything it names.

## Import flow

`ImportAiProposal`: clipboard, strict parse, review against the live session, the review dialog,
then under `AgentProgressDialog`:

1. Warn if the ticked subset leaves a final state the review never showed (unticking a row can take
   a compensating change with it). A warning, not a refusal.
2. Run probes (read-only, before anything is written).
3. `CommitAgentImportAsync` re-judges the ticked rows against the session **as it is now**
   (`AgentProposalApplier.Prepare`). This is not ceremony: rows were prepared before probes ran,
   probes take seconds, and the panel stays editable. Ticked is the review's default, not a gate
   (stale rows are offered unticked for deliberate opt-in), so the check is whether the fingerprint
   moved after the dialog showed it, not the fresh verdict's tick flag. The probes stand; they only
   read.
4. Undo is armed **before** the first write: an engine can throw after the settings rows landed, and
   an import the user cannot undo is the worst outcome. The previous import's undo returns only if
   nothing moved.
5. Settings rows are written as one set (`AgentProposalApplier.Apply`); if a write throws, what was
   written is restored. The side lock then `Remember`s the rows as written (the dialog showed exactly
   those sides) - before engines run, because a junction tune saves inside them and would otherwise
   read the rows as a hand edit.
6. Engine requests run in the fixed order below, then one summary. Entries already in the summary did
   happen, so an error never claims "not imported" over them.

Undo (`AgentImportUndo`) captures every channel's chain, not only named ones (engines write channels
no row names, and the crossover wizard can reorder blocks), the block order (restored by identity),
the stereo scene, level tilt and rear-fill offset that Auto delay commits, and the target level. It
also records the project generation: after a session load the entries would restore into settings
objects nobody displays. `CopyEditable` restores phase rotation and FIR even though no operation
writes them, since undo must not leave a later edit behind. After undo the side lock remembers the
restored state as it stands; reading it as a difference could carry a side where it never was
(L=A, R=B; import wrote L=B; undo restores L=A and a difference would carry A onto R).

## Engine order

`RunAgentEngineRequests` runs engines in a fixed order regardless of reply order: spatial average
first (it decides which curves the rest read), Auto crossover, junction tune (after the wizard,
before Auto delay, which realigns whatever the crossover became), Auto delay, then Auto-tune last,
fitting the bank to everything the others left. Each keeps its own confirmation; cancelling one skips
only it. Engines are addressed through the verdict's settings object, not the channel id, because the
crossover wizard earlier in the same import may have re-lettered the blocks.

Headless engine runs:

- **Spatial average** sets mode and Hybrid tick together (either alone leaves the point measurement in
  charge), with project events suppressed so the import saves and redraws once.
- **Auto delay** (`RunAgentAutoDelayAsync`) runs the button's checks headless (a refusal becomes a
  summary phrase), the dialog's compute delegate and its Apply commit, including report, log and
  outcome metric. Inputs a request omits come from `AgentAutoDelayDefaults`: layout-neutral
  magnitudes (the layout toggle owns signs) and gain balance unticked (the project stores the tilt,
  not the opt-in). The panel is disabled during compute, because the dialog's modality is what kept
  the chain still. The summary shows only the report's head, since a message box does not scroll.
- **Junction tune** (`RunAgentTuneJunctionAsync`) reads the two blocks off live channels, passes every
  side the pair is measured on with its raw responses and current chains, and writes the one
  crossover the tuner settles on to both sides of both blocks, as the wizard writes. One slope for
  both edges unless freed (the free search costs slopes squared per corner). The panel is not disabled:
  disable/enable repaints every plot twice, which with spatial averages costs seconds more than the
  tune. Instead the fingerprint is taken around the compute and a moved fingerprint drops the result.
  The side lock remembers the result as it stands; as a difference, a hidden side already holding the
  new edge would look untouched and receive the shown side's whole crossover. Readings are reported on
  the package's octave-each-side junction band so they compare with what the assistant read.
- **Auto-tune** (`RunAgentAutoTuneAsync`) builds the PEQ menu's handoff for the channel (only the shown
  side: gate pin, render anchor and hybrid datum are the shown side's), fits with `EqAutoTuneHeadless`
  (pinned against the wizard's render) and lands the way the wizard's Return lands, guards included,
  so a channel that moved during the fit is refused. The wizard's target-level question becomes a
  refusal. All fits of one import use one target level decided up front (`ImportTargetLevelDb`): the
  first stated level, else the project's; read per operation, a row stating none would fit at the old
  datum and the next row would move it. The level travels in the request token and reaches the panel
  only when the fit lands, so a skipped run leaves nothing behind.

## Excess group delay diagnostic

`BuildExcessGroupDelayCurve` (panel) and `AgentDiagnosticBuilder.BuildExcessGroupDelay` produce each
measured channel's excess group delay: the group delay minus its minimum-phase part (what the
magnitude dictates, and what a minimum-phase PEQ straightens along with it). What remains is arrivals
and reflections - the question a junction that will not sum asks - plus, at a band edge, possibly the
gate's own truncation of a steep filter.

- Read through the project's phase gate **and** window mode with cycles (what the group-delay view
  draws), placed at the channel's own arrival (the handoff's rule for a measurement read without the
  chain). Under FDW both parts are read against the windowed magnitude, so reflections the window
  drops leave the excess too. The window is stamped on the document (`AgentDiagnosticWindow`) so a
  document read alone still says Fixed vs FDW and the gate length.
- Smoothing is the group-delay view's default (1/12 octave), not psychoacoustic: group delay is a
  phase slope, and a psychoacoustic width is a hearing model for levels, not time.
- The computation is pure (gated FFTs, minimum-phase reconstruction, difference), so it runs off the
  UI thread on a snapshot; on a large installation it takes seconds.
- It is a separate text beside the package (which already fills a chat), named after the last copied
  package only while the session still matches its fingerprint, with the same grids, rounding and
  holes-as-null rule so the two line up by channel id. The probe asking for the same reading shares
  `ExcessGroupDelaySeries`.

## Protocol constants

`AgentProtocol` holds the fixed words and limits. The markers are the protocol version: a breaking
schema change gets new markers so an old reply is simply not found rather than half-understood;
additive changes keep them. Limits are generous for anything an assistant has a reason to send and
tight enough that a runaway reply cannot take the importer down. `Operations` lists what this build
executes; the parser and validator understand every operation the protocol names, so a reply written
for a later build is reviewed and refused with a plain reason instead of being mangled. `GuideUrl`
points to raw Markdown so a fetching assistant needs no scraping; `InlineRules` repeats the guide's
opening for chats that cannot fetch it; `GuideVersion` tells an assistant reading a newer guide which
methodology the package expected.

`AgentClipboard` is the one transport, swappable for tests; each call is retried a few times because
a remote desktop or clipboard manager may briefly hold the clipboard, and failures are reported,
never thrown. `AgentProgressDialog` is informational, not modal - engines that need a still tune
disable the panel themselves, and the crossover wizard opens its own window a modal box would fight
for the foreground - and has no Cancel, because no step can stop without leaving a half-written tune.
