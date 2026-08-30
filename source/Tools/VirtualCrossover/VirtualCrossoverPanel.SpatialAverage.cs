using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One redraw's hybrid magnitudes: every processed channel's spatially averaged
/// curve with that channel's own DSP chain on top, plus the single offset that
/// puts the whole set on the impulse responses' axis.
/// </summary>
/// <remarks>
/// The channel curves are held WITHOUT the offset. It is one scalar for the set,
/// so adding it at the end keeps the drawing and the summation reading the very
/// same arrays — and the sum shifts by exactly that scalar too, since a common
/// gain factors straight out of a magnitude sum.
/// </remarks>
/// <param name="Channels">
/// What the plot draws: each channel at the display smoothing.
/// </param>
/// <param name="UnsmoothedChannels">
/// What the SUM is built from, and the reason the two exist separately — the same
/// split, for the same reason, as <see cref="GatedMagnitude"/>. The sum substitutes
/// these levels bin by bin into the channels' gated spectra, so it wants them before
/// any display smoothing: smoothing does not commute with the substitution, and a
/// fractional-octave window straddling a steep crossover skirt pulls each channel's
/// level up toward its own passband — exactly at the corners a hybrid gets read at.
/// The finished sum is smoothed once, the order the measured Sum beside it is built
/// in.
/// </param>
/// <param name="ChannelOffsetsDb">
/// Each channel's own offset IN CHANNEL ORDER, null where the two curves never
/// overlap enough to compare. Positional rather than packed: the spread read-out
/// names the channel beside its figure, and a packed list silently shifted those
/// names onto the wrong drivers as soon as one channel had nothing to say.
/// </param>
/// <summary>One channel of the set and how far its capture sits from its response.</summary>
internal readonly record struct SetDatum(
    VirtualCrossoverChannel Channel,
    double? DatumDb);

internal sealed record HybridMagnitudes(
    IReadOnlyList<IReadOnlyList<SignalPoint>> Channels,
    IReadOnlyList<IReadOnlyList<SignalPoint>> UnsmoothedChannels,
    IReadOnlyList<double?> ChannelOffsetsDb,
    double OffsetDb)
{
    /// <summary>
    /// Which channels were drawn from their own point measurement because they had
    /// no spatial average, in channel order.
    /// </summary>
    /// <remarks>
    /// The hybrid exists to keep a point measurement's dips out of an equalizer's
    /// way, so a channel drawn from one is the exception the user has to be told
    /// about. It is legitimate — a subwoofer gains almost nothing from an array,
    /// because below the cabin's first mode a point and an average are the same
    /// measurement — but it must never be silent.
    /// </remarks>
    public IReadOnlyList<bool> PointMeasuredChannels { get; init; } = [];

    /// <summary>How many channels fell back to their point measurement.</summary>
    public int PointMeasuredCount => PointMeasuredChannels.Count(fallback => fallback);

    /// <summary>
    /// How far apart the channels are about where the captures sit relative to the
    /// impulse responses — the largest per-channel offset minus the smallest, in dB.
    /// </summary>
    /// <remarks>
    /// The one number that judges a SET. Every capture in a valid set was taken with
    /// one analyzer recipe at one input gain, so whatever separates the two families
    /// of measurement separates them by the SAME amount in every channel, and the
    /// offsets agree. They stop agreeing when something entered per capture: a
    /// changed input gain, a different frame length or window (the noise-slope
    /// compensation is a curve, not a constant), a mixed scale, a capture from an
    /// unrelated session. The detector does not care which — it says the set does not
    /// hang together, and one offset therefore cannot serve it.
    /// <para>
    /// What it does NOT claim is that the captures and the impulse responses agree.
    /// They are different measurements of different things and their levels may sit
    /// tens of dB apart; only the DISAGREEMENT between channels is evidence.
    /// </para>
    /// </remarks>
    public double SpreadDb
    {
        get
        {
            List<double> known = (SetDatumsDb.Count > 0
                    ? SetDatumsDb.Select(entry => entry.DatumDb)
                    : ChannelOffsetsDb)
                .Where(offset => offset.HasValue)
                .Select(offset => offset!.Value)
                .ToList();
            return known.Count < 2 ? 0.0 : known.Max() - known.Min();
        }
    }

    /// <summary>
    /// Every channel's datum on this side, muted ones included — what
    /// <see cref="OffsetDb"/> is the median of and <see cref="SpreadDb"/> the range
    /// of. Empty for a caller that supplied none, which then falls back to the drawn
    /// channels.
    /// </summary>
    /// <remarks>
    /// The set is the measurements, not the selection of them a user happens to be
    /// listening to. Judging its coherence on the drawn channels alone made the
    /// warning appear and vanish with the mute buttons, and moved every curve on the
    /// plot while it did.
    /// </remarks>
    public IReadOnlyList<SetDatum> SetDatumsDb { get; init; } = [];
}

// Attaching a spatially averaged magnitude to a channel, and deciding whether the
// hybrid view can be shown at all.
//
// The average is an optional REFINEMENT of what the magnitude view draws, never the
// basis of a computation: delays, polarity, junctions and the summation loss keep
// reading the honest impulse responses. What it changes is that the curve stops
// carrying the dips of one microphone position, which are the dips a tune must not
// chase.
public partial class VirtualCrossoverPanel
{
    /// <summary>
    /// Which spatial average this project reads: the stored choice, or — for a
    /// project that has nothing stored and nothing attached yet — a fallback that
    /// <see cref="SettleSpatialAverageMode"/> replaces with a stored choice the
    /// moment the project has anything to guess from.
    /// </summary>
    internal VirtualCrossoverSpatialAverageMode SpatialAverageMode =>
        project.SpatialAverageMode ?? (HasAnyArrayCapture()
            ? VirtualCrossoverSpatialAverageMode.MicArray
            : VirtualCrossoverSpatialAverageMode.MovingMic);

    /// <summary>
    /// Stores the guessed method the first time the project has any spatial average
    /// to guess from. True when it wrote one, so the caller can persist.
    /// </summary>
    /// <remarks>
    /// The guess has to be made ONCE and kept, because it is the answer to "where do
    /// the levels come from" for the whole project — and computing it live made it
    /// change under a project that never chose. A session written before arrays
    /// existed carries attachments and no stored mode; loading a single new
    /// measurement that happens to carry an array flipped the whole project to the
    /// array method, at which point the attachments went unread and every channel
    /// without an array quietly fell back to its point response. The user changed
    /// one channel's source and the source of every channel's levels changed.
    /// <para>
    /// What is stored is whatever the fallback ALREADY says, so a project that opens
    /// today opens the same way tomorrow. Freezing is the whole fix: by the time a
    /// new measurement can arrive the mode is stored, so it cannot move the project
    /// under the user. Preferring one family over the other on top of that was a
    /// second rule doing no extra work, and it changed what a session holding BOTH an
    /// array and attachments displayed — which is a live session of the owner's, and
    /// a surprise is exactly what this is supposed to prevent.
    /// </para>
    /// <para>
    /// A project with nothing to guess from is left unstored, so the first
    /// measurement to arrive still gets to decide — an array arrives WITH the
    /// measurement, and asking a user who just recorded one to find a menu would be
    /// asking twice.
    /// </para>
    /// </remarks>
    private bool SettleSpatialAverageMode()
    {
        if (project.SpatialAverageMode != null)
        {
            return false;
        }

        bool attachments = channels.Any(channel =>
            channel.SideState(false).SpatialAverage != null ||
            channel.SideState(true).SpatialAverage != null);
        if (!attachments && !HasAnyArrayCapture())
        {
            return false;
        }

        project.SpatialAverageMode = SpatialAverageMode;
        return true;
    }

    private bool HasAnyArrayCapture() =>
        channels.Any(channel =>
            channel.SideState(false).ArrayCapture != null ||
            channel.SideState(true).ArrayCapture != null);

    private void SetSpatialAverageMode(VirtualCrossoverSpatialAverageMode mode)
    {
        if (SpatialAverageMode == mode && project.SpatialAverageMode == mode)
        {
            return;
        }

        project.SpatialAverageMode = mode;
        foreach (VirtualCrossoverChannel channel in channels)
        {
            RefreshSpatialAverageStatus(channel);
        }

        RefreshHybridAvailability();
        ScheduleSave();
        OnViewChanged();
    }

    private void ShowSpatialAverageMenu(VirtualCrossoverChannel channel)
    {
        var menu = new ContextMenuStrip();

        // The method is the project's, so it is offered on every channel's button
        // rather than hidden somewhere else: the button is where a user notices the
        // curve is missing, and it is where they will look for why.
        foreach ((VirtualCrossoverSpatialAverageMode mode, string label) in new[]
        {
            (VirtualCrossoverSpatialAverageMode.MicArray, "Use microphone arrays"),
            (VirtualCrossoverSpatialAverageMode.MovingMic, "Use attached MMM captures"),
            (VirtualCrossoverSpatialAverageMode.Off, "No spatial average")
        })
        {
            ToolStripMenuItem item = new(label)
            {
                Checked = SpatialAverageMode == mode,
                CheckOnClick = false
            };
            VirtualCrossoverSpatialAverageMode chosen = mode;
            item.Click += (_, _) => SetSpatialAverageMode(chosen);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem chooseItem = new("Attach capture...");
        chooseItem.Click += (_, _) => ChooseSpatialAverage(channel);
        menu.Items.Add(chooseItem);

        // Offered for a capture the session refers to but could not read, too: that
        // is the state a user most wants to be rid of.
        if (channel.SpatialAverage != null ||
            !string.IsNullOrWhiteSpace(channel.Settings.SpatialAveragePath))
        {
            ToolStripMenuItem detachItem = new("Detach");
            detachItem.Click += (_, _) =>
            {
                channel.SpatialAverage = null;
                channel.Settings.SpatialAveragePath = null;
                channel.Settings.SpatialAverageRelativePath = null;
                OnSpatialAverageChanged(channel);
            };
            menu.Items.Add(detachItem);
        }

        DropDownMenu.ShowAt(this, menu, Cursor.Position);
    }

    private void ChooseSpatialAverage(VirtualCrossoverChannel channel)
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Resonalyze moving-mic capture (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            RestoreDirectory = true,
            Title = $"Attach a spatial average to {channel.Name}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (!LiveCaptureDocument.TryLoad(dialog.FileName, out LiveCaptureDocument document))
            {
                MessageBox.Show(
                    this,
                    "That file is not a Resonalyze capture.",
                    "Attach spatial average",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            channel.SpatialAverage = document;
            channel.Settings.SpatialAveragePath = dialog.FileName;
            // The relative path names the capture the session was IMPORTED with, and
            // this is a different file: left standing it would send the next search
            // after the one just replaced.
            channel.Settings.SpatialAverageRelativePath = null;
            OnSpatialAverageChanged(channel);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "The capture could not be loaded." +
                    Environment.NewLine + Environment.NewLine + exception.Message,
                "Attach spatial average",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void OnSpatialAverageChanged(VirtualCrossoverChannel channel)
    {
        // The first attachment is what a project with no stored method is waiting
        // for; from here the method is the project's own and cannot drift.
        SettleSpatialAverageMode();
        RefreshSpatialAverageStatus(channel);
        RefreshHybridAvailability();
        // The attachment is session state, so it travels with the session — the whole
        // point of storing the reference rather than re-picking it every time.
        ScheduleSave();
        OnViewChanged();
    }

    private void RefreshSpatialAverageStatus(VirtualCrossoverChannel channel)
    {
        if (!channelControls.TryGetValue(channel, out VirtualCrossoverChannelControl? control))
        {
            return;
        }

        VirtualCrossoverSpatialAverageMode mode = SpatialAverageMode;
        LiveCaptureDocument? document =
            channel.SideState(channel.ActiveRight).SpatialAverageFor(mode);
        // A stored path with no document behind it is the missing case: name it from
        // the path, since the file that carried the title is the file that is gone.
        // Only in the moving-microphone method — an array is not attached by path,
        // so there is nothing that could go missing.
        string? path = mode == VirtualCrossoverSpatialAverageMode.MovingMic
            ? channel.Settings.SpatialAveragePath
            : null;
        control.SetSpatialAverage(
            document?.Title
                ?? (string.IsNullOrWhiteSpace(path)
                    ? null
                    : Path.GetFileNameWithoutExtension(path)),
            document?.Recipe.IntegratedSeconds,
            resolved: document != null,
            mode,
            document?.SavedAtUtc);
    }

    /// <summary>
    /// Re-attaches one side's persisted capture: the stored path, then the same file
    /// beside the session it was imported from, then beside the folder the user
    /// pointed at when relinking — the very ladder the measurements climb.
    /// </summary>
    /// <remarks>
    /// A capture that no longer resolves degrades to an unattached side rather than
    /// failing the project load, and the stored path is LEFT standing: it is the only
    /// hint a later relink has, and it is what tells the button to warn instead of
    /// showing a channel that never had an average.
    /// </remarks>
    private void ResolveSpatialAverage(
        VirtualCrossoverChannelSettings settings,
        VirtualCrossoverChannelState state)
    {
        state.SpatialAverage = null;
        if (string.IsNullOrWhiteSpace(settings.SpatialAveragePath))
        {
            return;
        }

        string? path =
            VirtualCrossoverSourceLocator.Locate(
                settings.SpatialAveragePath,
                settings.SpatialAverageRelativePath,
                project.ProjectDirectory)
            ?? VirtualCrossoverSourceLocator.Locate(
                settings.SpatialAveragePath,
                settings.SpatialAverageRelativePath,
                relinkDirectory);
        if (path == null)
        {
            return;
        }

        try
        {
            if (LiveCaptureDocument.TryLoad(path, out LiveCaptureDocument document))
            {
                state.SpatialAverage = document;
                // Pin where it was actually read from, the same rule the source path
                // follows: this project becomes the internal autosave right after the
                // import, and that copy has no session file beside it to search from.
                settings.SpatialAveragePath = path;
            }
        }
        catch (Exception exception)
        {
            // An unreadable or foreign file is an unattached side, not a failed load.
            _ = exception;
        }
    }

    /// <summary>
    /// Whether every channel that plays has a spatial average, and therefore whether
    /// the hybrid view is offered at all.
    /// </summary>
    /// <remarks>
    /// All or nothing, deliberately. A sum mixing spatially averaged channels with
    /// point-measured ones puts two different references on one axis and looks
    /// exactly like a measurement, so a partial set mutes the toggle rather than
    /// drawing something that cannot be read.
    /// </remarks>
    private LiveCaptureSetVerdict JudgeSpatialAverages =>
        JudgeSideSpatialAverages(project.ActiveSideRight);

    /// <summary>
    /// A channel's bypass response read on CANONICAL terms — its own onset, the fixed
    /// steady-state window, no calibration and no display smoothing.
    /// </summary>
    /// <remarks>
    /// Both halves of the datum have to be read the same way or the difference between
    /// them stops being a property of the measurements. Calibration would cancel if it
    /// were on both, but smoothing would not: it is applied to two differently shaped
    /// curves and does not commute with their subtraction. Pinning both here also
    /// keeps the panel's figure identical to the one the threshold was calibrated on.
    /// </remarks>
    private AnalysisCurve BuildCanonicalRawCurve(
        Complex[] impulseResponse,
        int peakIndex,
        int sampleRate,
        MeasuredBand band)
    {
        int anchorIndex = ProcessedChannels.StartAnchorIndex(
            impulseResponse, peakIndex, sampleRate);
        PhaseAnalysisSettings gate = magnitudeGate.Template with
        {
            GateOffsetMs = anchorIndex * 1_000.0 / sampleRate
        };
        return DataHelper.GetGatedPrimarySpectrumPair(
            new ImpulseMeasurementView(impulseResponse, anchorIndex, sampleRate)
            {
                LowestMeasuredFrequencyHz = band.LowEdgeHz,
                HighestMeasuredFrequencyHz = band.HighEdgeHz
            },
            gate,
            calibration: null,
            smoothingInverseOctaves: 0).Unsmoothed;
    }

    /// <summary>
    /// Whether the dashed opposite-side hybrid sum may be drawn: BOTH sides' captures
    /// have to form one set, not each side its own.
    /// </summary>
    /// <remarks>
    /// That curve borrows the active side's offset outright
    /// (<see cref="BuildOppositeHybridSumCurve"/>) — deliberately, since levelling
    /// the sides separately would erase the very L/R difference it exists to show.
    /// Judging the sides independently leaves that borrowing unchecked: two relative
    /// capture runs, one per side, are each internally consistent and say nothing
    /// about how their levels compare, so an input gain that moved between them would
    /// be drawn as an L/R imbalance the car does not have. A recipe that differs
    /// across the sides is the same hole.
    /// </remarks>
    private bool CanDrawOppositeHybridSum(bool oppositeRight)
    {
        if (!TryCollectSideCaptures(project.ActiveSideRight, out var active).Coherent ||
            !TryCollectSideCaptures(oppositeRight, out var opposite).Coherent)
        {
            return false;
        }

        return JudgeSidesShareAnOffset(active, opposite).Coherent;
    }

    /// <summary>
    /// Whether one offset may level both sides' captures — the condition the dashed
    /// opposite sum is drawn under. Static and pure so it can be pinned directly.
    /// </summary>
    internal static LiveCaptureSetVerdict JudgeSidesShareAnOffset(
        IReadOnlyList<LiveCaptureDocument> active,
        IReadOnlyList<LiveCaptureDocument> opposite)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(opposite);
        if (active.Count == 0 || opposite.Count == 0)
        {
            return LiveCaptureSetVerdict.No("One side has no spatial averages.");
        }

        var union = new List<LiveCaptureDocument>(active.Count + opposite.Count);
        union.AddRange(active);
        union.AddRange(opposite);

        // A mono pair contributes the same capture to both lists; a document matches
        // itself, so the duplicate costs nothing.
        LiveCaptureSetVerdict verdict = LiveCaptureDocument.JudgeSet(union);
        return verdict.Coherent
            ? verdict
            : LiveCaptureSetVerdict.No(
                "The two sides' captures are not one set, so the active side's " +
                "offset cannot level them both. " + verdict.Reason);
    }

    /// <summary>
    /// Whether that side's playing channels can produce a hybrid: every one of them
    /// carries a capture, and those captures form one set.
    /// </summary>
    /// <remarks>
    /// Coverage alone is not enough, and the difference matters. Attaching seven
    /// captures taken at three frame lengths and two scales leaves every channel
    /// covered while putting curves compensated by different amounts on one axis,
    /// under one offset that fits none of them. The set-spread warning is a
    /// heuristic backstop reading a median over the working band; the recipe is the
    /// fact, and it is what decides here.
    /// </remarks>
    private LiveCaptureSetVerdict JudgeSideSpatialAverages(bool rightSide)
    {
        LiveCaptureSetVerdict gathered =
            TryCollectSideCaptures(rightSide, out List<LiveCaptureDocument> captures);
        return gathered.Coherent ? LiveCaptureDocument.JudgeSet(captures) : gathered;
    }

    // That side's captures, or why it has none to give.
    private LiveCaptureSetVerdict TryCollectSideCaptures(
        bool rightSide, out List<LiveCaptureDocument> captures)
    {
        // The channels that actually play on that side: an enabled pair with a
        // measurement behind it. A disabled or empty one contributes nothing to the
        // sum and so cannot hold the hybrid view back.
        List<VirtualCrossoverChannelState> playing = channels
            .Where(channel => channel.Pair.Enabled)
            .Select(channel => channel.SideState(rightSide))
            .Where(state => state.TransferImpulseResponse != null)
            .ToList();
        captures = new List<LiveCaptureDocument>(playing.Count);
        if (playing.Count == 0)
        {
            return LiveCaptureSetVerdict.No("No channel on this side plays.");
        }

        foreach (VirtualCrossoverChannelState state in playing)
        {
            if (state.SpatialAverageFor(SpatialAverageMode) is not { } capture)
            {
                // An ARRAY set may have gaps. Both families are levelled by the same
                // loopback the impulse responses are referenced to, so a channel
                // drawn from its own measurement is on the same axis as the rest —
                // the objection that makes this all-or-nothing for a moving
                // microphone (two different references on one axis) does not apply.
                // What remains is a shape difference, and on the band a channel
                // without an array usually covers it is small: below the cabin's
                // first mode a point measurement IS the average.
                if (SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray)
                {
                    continue;
                }

                return LiveCaptureSetVerdict.No(
                    "Needs a spatial average on every channel that plays. " +
                    "Attach one per channel with the MMM button.");
            }

            captures.Add(capture);
        }

        if (captures.Count == 0)
        {
            return LiveCaptureSetVerdict.No(
                SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray
                    ? "No channel on this side was measured with a microphone array."
                    : "No channel on this side has a spatial average.");
        }

        return LiveCaptureSetVerdict.Ok;
    }

    // Whether the set could produce a hybrid at all, as of the last refresh. Cached
    // rather than recomputed per redraw, and it is the same answer the toggle's own
    // look is built from — the toggle stays ENABLED and is muted by hand (see below),
    // so its enabled state cannot stand in for this.
    private bool hybridAvailable;

    // Whether this redraw should draw the hybrid. Both halves matter: the tick
    // survives a view switch, and a ticked toggle with no coverage behind it must not
    // put a half-built hybrid on the plot.
    private bool HybridRequested => checkBoxHybrid.Checked && hybridAvailable;

    private void RefreshHybridAvailability()
    {
        // The tick is INTENT and outlives the coverage: HybridRequested needs both,
        // so a set that is short of a capture draws honest curves whether or not the
        // box is ticked, and clearing the tick would only mean the user has to find
        // it again after re-attaching. Same reason a pinned gate outlives its
        // sources.
        LiveCaptureSetVerdict verdict = JudgeSpatialAverages;
        hybridAvailable = verdict.Coherent;

        // Magnitude-only, muted the way the Sum and Sum loss toggles are (see
        // UpdateViewDependentControls): a spatial average carries no phase, so there
        // is no hybrid phase or impulse view to offer. MUTED and not unticked — a
        // look at another view must not cost the tick. Through the shared helper
        // rather than Enabled, because a CheckBox WinForms disables paints its text
        // in a system grey that reads as near-black on this theme.
        // The Groups view is muted for a third reason: it draws no channel curves
        // at all, only one summed line per zone, and a spatial average is a
        // property of one DRIVER. There is no honest way to hang one on a group's
        // sum, so the toggle says so rather than sitting there doing nothing.
        bool groupSums =
            VirtualCrossoverGroupViews.DrawsGroupSums(SelectedGroupView);
        bool live = hybridAvailable && radioViewMagnitude.Checked && !groupSums;
        UiStyle.SetTextEnabledLook(checkBoxHybrid, live, interactive: true);
        toolTip.SetToolTip(
            checkBoxHybrid,
            !hybridAvailable
                ? verdict.Reason ?? "Needs a spatial average on every channel that " +
                    "plays. Attach one per channel with the MMM button."
                : groupSums && radioViewMagnitude.Checked
                ? "The Groups view draws one summed line per zone rather than the " +
                    "drivers, and a spatial average belongs to a driver — there is " +
                    "nothing to hang one on here. Pick another Show view for the " +
                    "hybrid."
                : !radioViewMagnitude.Checked
                ? "The hybrid is a magnitude view: a spatial average carries no " +
                    "phase, so the phase and impulse views keep reading the impulse " +
                    "responses."
                : "Draw each channel's magnitude from its spatial average with this " +
                    "channel's DSP chain on top, instead of from the impulse response " +
                    "measured at one point. Both sums follow, adding the channels as " +
                    "phasors with the phase the impulse responses measure — the other " +
                    "side needs its own captures too, and its dashed sum is dropped " +
                    "rather than drawn by the other method. Timing, polarity and the " +
                    "sum-loss read-out are " +
                    "unaffected: they keep reading the impulse responses.\r\n\r\n" +
                    "The channel curves are exact — a filter does not depend on " +
                    "microphone position. The Sum is an estimate: it adds the " +
                    "channels as phasors, and the phase holding them together was " +
                    "measured at ONE position, so its peaks and dips can be either " +
                    "stronger or weaker than the volume's average. The gap tends to " +
                    "grow the faster the phase turns across that volume — generally " +
                    "small in the bass, largest at a crossover high up.");
    }

    /// <summary>
    /// This redraw's hybrid magnitudes, or null when the set cannot produce one.
    /// </summary>
    /// <remarks>
    /// Built once per redraw and shared by the drawing and the summation. Every
    /// channel or none: a set where one channel failed to yield a curve would
    /// otherwise sum a spatial average against a point measurement, the mix the whole
    /// feature exists to avoid.
    /// </remarks>
    private HybridMagnitudes? BuildHybridMagnitudes(
        IReadOnlyList<ProcessedChannel> processed,
        IReadOnlyList<AnalysisCurve> references,
        bool rightSide,
        int smoothingCode)
    {
        if (processed.Count == 0 || references.Count < processed.Count)
        {
            return null;
        }

        // The datum is read on the two measurements BEFORE any chain, never on the
        // curves below. Those carry the DSP on both sides and it does NOT cancel: the
        // impulse response is filtered and then gated while the capture is filtered
        // analytically, and a gate does not commute with a filter; and the band the
        // median is taken over is set by the channel's peak, which the crossover
        // moves. Reading it there made the whole hybrid set drift up and down the
        // axis while the user tuned — and that offset travels to the EQ Wizard.
        // Resolved FIRST because a channel falling back to its point measurement has
        // to be lowered by it: the set's curves are held without the offset and it is
        // added on the way to the plot, so a curve already on the impulse responses'
        // axis must arrive pre-subtracted to land back where it started.
        (double?[] offsets, double setOffset, IReadOnlyList<SetDatum> setDatums) =
            ResolveRawHybridOffsetsDb(processed, rightSide);

        var hybrids = new List<IReadOnlyList<SignalPoint>>(processed.Count);
        var unsmoothed = new List<IReadOnlyList<SignalPoint>>(processed.Count);
        var pointMeasured = new bool[processed.Count];
        for (int i = 0; i < processed.Count; i++)
        {
            // Built RAW and smoothed here rather than twice through the chain: the
            // shared builder's own last step is this same smoothing, so smoothing its
            // unsmoothed output reproduces what it would have returned, and the
            // expensive part — the analytic chain over the whole grid — runs once.
            IReadOnlyList<SignalPoint>? raw = BuildHybridChannelCurve(
                processed[i].Channel, rightSide, references[i].Points, smoothingCode: 0);
            if (raw == null)
            {
                if (SpatialAverageMode != VirtualCrossoverSpatialAverageMode.MicArray)
                {
                    return null;
                }

                // This channel has no array. Its own processed magnitude is already
                // on the impulse responses' axis and is a perfectly good curve — it
                // is simply a point measurement, which is what the badge says.
                raw = ShiftedBy(references[i].Points, -setOffset);
                pointMeasured[i] = true;
            }

            unsmoothed.Add(raw);
            hybrids.Add(smoothingCode == 0 || raw.Count < 2
                ? raw
                : DataHelper.SmoothBandLevels(
                    raw,
                    SpectrumSmoothing.SmoothingOctaves(smoothingCode),
                    SpectrumSmoothing.IsPsychoacoustic(smoothingCode)));
        }

        return new HybridMagnitudes(hybrids, unsmoothed, offsets, setOffset)
        {
            PointMeasuredChannels = pointMeasured,
            SetDatumsDb = setDatums
        };
    }

    /// <summary>
    /// One channel's magnitude drawn from its spatial average instead of from the
    /// impulse response measured at one point, on the reference curve's frequency grid
    /// and at the capture's own level. Null when the channel has no average attached.
    /// </summary>
    /// <remarks>
    /// The arithmetic lives in <see cref="SpatialAverageHybrid"/>, shared with the EQ
    /// Wizard so a tune is fitted to the curve this plot drew. What is decided HERE is
    /// the context: which side, which chain, and the panel's own calibration, which the
    /// capture is rebased onto so the hybrid and the measured curves beside it carry
    /// one correction rather than two.
    /// </remarks>
    private IReadOnlyList<SignalPoint>? BuildHybridChannelCurve(
        VirtualCrossoverChannel channel,
        bool rightSide,
        IReadOnlyList<SignalPoint> reference,
        int smoothingCode)
    {
        // Explicitly the side asked for, never the channel's ACTIVE one: the opposite
        // side's sum is built from this too, and reading the shown side's capture there
        // would draw one side's tuning under the other's label.
        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        if (state.SpatialAverageFor(SpatialAverageMode) is not { } document ||
            reference.Count == 0)
        {
            return null;
        }

        return SpatialAverageHybrid.BuildChannelCurve(
            document,
            // The SAME chain the processed response was built through, Bypass included:
            // a bypassed channel contributes its raw measured signal, so putting the
            // chain on its average would make the hybrid the one curve on the plot that
            // ignores the switch.
            channel.Pair.Bypass
                ? DspChannelChain.Identity
                : channel.SideSettings(rightSide).ToChain(),
            // The PROCESSOR's rate: the chain is what the user's DSP will run, and it
            // runs at the device's rate — not at whatever the capture, or the
            // measurement beside it, was taken at. The capture's own rate is already
            // folded into its stored levels.
            channel.ProcessorSampleRateFor(rightSide),
            // This channel's own under "Own (as measured)". For a capture whose
            // positions shared one file the swap is exact and — since that file is
            // normally the one the impulse response beside it was read through — it
            // comes out a no-op; for one whose positions did not, the capture keeps
            // its own corrections and this is ignored, because no single curve could
            // replace a mixture (see SpatialAverageHybrid).
            SpatialAverageCalibrationFor(state),
            reference.Select(point => point.X).ToList(),
            smoothingCode);
    }

    /// <summary>
    /// Each drawn channel's datum, and the SET's, read on the RAW pair: the capture
    /// with no chain against the channel's bypass response. A property of the two
    /// measurements, so nothing the user tunes can move it.
    /// </summary>
    /// <remarks>
    /// The raw impulse-response curve is built here rather than taken from the
    /// redraw, which only has it when the Raw view is switched on. One gated build
    /// per channel while the hybrid is drawn; the alternative — reading the datum off
    /// the processed curves — is what this exists to stop.
    /// <para>
    /// The set's offset is the median over EVERY channel of this side that carries a
    /// capture, muted or not, and that distinction is the whole reason this takes the
    /// channel list rather than only the drawn ones. A mute says which curves to
    /// draw; it does not say which measurements the set is made of. Taking the median
    /// over the drawn ones alone moved every remaining curve each time one was muted
    /// — a quarter of a decibel per channel on the owner's cabins, in the arrays and
    /// the moving-microphone captures alike — so a level read off the plot depended
    /// on which channels happened to be listening.
    /// </para>
    /// </remarks>
    private (double?[] PerChannel, double SetOffsetDb, IReadOnlyList<SetDatum> SetDatums)
        ResolveRawHybridOffsetsDb(
            IReadOnlyList<ProcessedChannel> processed,
            bool rightSide)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.HybridOffsets");
        var datums = new Dictionary<VirtualCrossoverChannel, double?>();
        var setDatums = new List<SetDatum>();
        foreach (VirtualCrossoverChannel channel in AllChannelsWith(processed))
        {
            double? datum = ResolveRawDatumDb(channel, rightSide);
            datums[channel] = datum;
            // A channel with no capture at all is not part of this set and must not
            // appear in what the warning lists; one WITH a capture it cannot compare
            // stays, as a named hole.
            if (channel.SideState(rightSide).SpatialAverageFor(SpatialAverageMode) != null)
            {
                setDatums.Add(new SetDatum(channel, datum));
            }
        }

        var perChannel = new double?[processed.Count];
        for (int i = 0; i < processed.Count; i++)
        {
            perChannel[i] = datums.TryGetValue(processed[i].Channel, out double? datum)
                ? datum
                : null;
        }

        List<double> known = setDatums
            .Where(entry => entry.DatumDb.HasValue)
            .Select(entry => entry.DatumDb!.Value)
            .ToList();
        return (perChannel, known.Count == 0 ? 0.0 : SpatialAverageOffsets.Median(known), setDatums);
    }

    // Every channel that could contribute a datum: the panel's own list, plus any
    // drawn channel it does not hold (a harness builds those directly).
    private IEnumerable<VirtualCrossoverChannel> AllChannelsWith(
        IReadOnlyList<ProcessedChannel> processed)
    {
        var seen = new HashSet<VirtualCrossoverChannel>();
        foreach (VirtualCrossoverChannel channel in channels ?? [])
        {
            if (seen.Add(channel))
            {
                yield return channel;
            }
        }

        foreach (ProcessedChannel item in processed)
        {
            if (seen.Add(item.Channel))
            {
                yield return item.Channel;
            }
        }
    }

    // One channel side's datum, or null when it cannot produce the raw pair. Such a
    // channel contributes nothing rather than falling back to the processed curves,
    // which would put its offset on a different footing from the rest.
    private double? ResolveRawDatumDb(VirtualCrossoverChannel channel, bool rightSide)
    {
        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        if (state.TransferImpulseResponse is not { } ir || state.SampleRate <= 0)
        {
            return null;
        }

        AnalysisCurve rawIr = BuildCanonicalRawCurve(
            ir, state.TransferPeakIndex, state.SampleRate, state.MeasuredBand);
        if (state.SpatialAverageFor(SpatialAverageMode) is not { } document)
        {
            return null;
        }

        IReadOnlyList<SignalPoint>? rawCapture = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            state.SampleRate,
            // Canonical, not what the plot happens to be showing: identity chain, no
            // calibration, no display smoothing. A datum that moved with the
            // smoothing selector would not be a property of the two measurements, and
            // the threshold the spread is judged against is calibrated on these very
            // terms (HybridOffsetDatumMeasurement reads them the same way).
            SpatialAverageCalibration.Off,
            rawIr.Points.Select(point => point.X).ToList(),
            smoothingCode: 0);
        return rawCapture == null
            ? null
            : SpatialAverageOffsets.ChannelDatumDb(rawCapture, rawIr.Points);
    }

    // The set's common offset, applied on the way to the plot.
    private static IReadOnlyList<SignalPoint> ShiftedBy(
        IReadOnlyList<SignalPoint> points,
        double offsetDb)
    {
        if (offsetDb == 0)
        {
            return points;
        }

        var shifted = new List<SignalPoint>(points.Count);
        foreach (SignalPoint point in points)
        {
            shifted.Add(new SignalPoint(point.X, point.Y + offsetDb));
        }

        return shifted;
    }

    /// <summary>
    /// The hybrid channels summed as PHASORS: each channel's gated spectrum rescaled,
    /// bin by bin, to the level its spatial average reports, and the rescaled phasors
    /// added. Null when the set has no usable grid.
    /// </summary>
    /// <remarks>
    /// A spatial average carries no phase, so the phase can only come from the impulse
    /// response — which is why the two measurements travel side by side. Nothing is
    /// borrowed here, which is the point: the previous construction added the
    /// magnitudes and laid the impulse responses' own summation loss on top, and a
    /// loss is a property of the LEVELS it was measured at. At a steep junction on the
    /// owner's car the two families disagreed about the relative levels of two
    /// channels by 23 dB — a gate does not commute with a 48 dB/octave filter, so a
    /// stopband reads far above its analytic slope — and the borrowed loss drew a
    /// 13 dB dip into a sum whose own channels could not have made more than 1.9 dB.
    /// <para>
    /// An ESTIMATE, unlike the per-channel curves, and the distinction is not
    /// pedantry. A channel's own hybrid is exact because a filter does not depend on
    /// position. The phase holding these phasors together does: it was measured at ONE
    /// microphone position, so this draws a point's interference, and its peaks and
    /// dips may come out either stronger OR weaker than the volume's average — nothing
    /// makes one position's relationship the more extreme of the two, and a position
    /// where two channels sit near quadrature carries almost none of a cross-term the
    /// average may hold firmly. What holds is only a tendency: the gap grows the
    /// faster the relative phase turns across the volume, so it is generally small in
    /// the bass and largest at a crossover high up. Nothing downstream treats this as
    /// measured.
    /// </para>
    /// </remarks>
    private static List<SignalPoint>? BuildHybridSumCurve(
        HybridMagnitudes hybrid,
        IReadOnlyList<ProcessedChannel> processed,
        int anchorIndex,
        MagnitudeGateSnapshot snapshot,
        double gateOffsetMs,
        IReadOnlyList<IReadOnlyList<SignalPoint>> channelReferences)
    {
        if (processed.Count == 0 || hybrid.UnsmoothedChannels.Count < processed.Count)
        {
            return null;
        }

        PhaseAnalysisSettings gate = snapshot.Template with { GateOffsetMs = gateOffsetMs };
        var channels =
            new List<(IImpulseMeasurement, IReadOnlyList<SignalPoint>)>(processed.Count);
        for (int c = 0; c < processed.Count; c++)
        {
            // Raw levels in, one smoothing at the end: the same rule the measured Sum
            // beside this one is built under.
            channels.Add((
                new ImpulseMeasurementView(
                    processed[c].ImpulseResponse, anchorIndex, processed[c].SampleRate),
                hybrid.UnsmoothedChannels[c]));
        }

        // Unsmoothed, then masked, THEN smoothed. The order is the contract: a point
        // the mask will break must not have taken part in its neighbours' means on
        // the way, or the hole is filled by the very values that are not allowed to
        // stand. SmoothBandLevels passes a NaN through and excludes it from the
        // neighbours it would otherwise pollute, which is exactly what is wanted.
        List<SignalPoint> sum = DataHelper.GetGatedSubstitutedMagnitudeSum(
            channels, gate, smoothingInverseOctaves: 0);
        if (sum.Count == 0)
        {
            return null;
        }

        List<SignalPoint> masked = MaskMissingContributors(
            sum, hybrid.UnsmoothedChannels, channelReferences, hybrid.OffsetDb);
        int smoothingCode = snapshot.SmoothingInverseOctaves;
        return smoothingCode == 0 || masked.Count < 2
            ? masked
            : DataHelper.SmoothBandLevels(
                masked,
                SpectrumSmoothing.SmoothingOctaves(smoothingCode),
                SpectrumSmoothing.IsPsychoacoustic(smoothingCode));
    }

    /// <summary>
    /// The finished sum with the set's offset on it, broken at the points where a
    /// channel has no capture while its impulse response says it is still playing.
    /// </summary>
    /// <remarks>
    /// Dropping a channel that still contributes would sum one set of sources and
    /// present it as the whole; it is ignorable only below
    /// <see cref="HybridDropoutFloorDb"/> under the loudest channel, where its own
    /// crossover has removed it anyway. Pure and separate so the rule can be pinned
    /// without a panel.
    /// </remarks>
    internal static List<SignalPoint> MaskMissingContributors(
        IReadOnlyList<SignalPoint> sum,
        IReadOnlyList<IReadOnlyList<SignalPoint>> hybridChannels,
        IReadOnlyList<IReadOnlyList<SignalPoint>> channelReferences,
        double offsetDb)
    {
        int count = sum.Count;
        foreach (IReadOnlyList<SignalPoint> channel in hybridChannels)
        {
            count = Math.Min(count, channel.Count);
        }

        foreach (IReadOnlyList<SignalPoint> channel in channelReferences)
        {
            count = Math.Min(count, channel.Count);
        }

        var points = new List<SignalPoint>(Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            // The loudest impulse-response level here, so a missing capture can be
            // judged against what is actually playing rather than against a constant.
            double loudest = double.NegativeInfinity;
            for (int c = 0; c < channelReferences.Count; c++)
            {
                double level = channelReferences[c][i].Y;
                if (double.IsFinite(level))
                {
                    loudest = Math.Max(loudest, level);
                }
            }

            bool missingContributor = false;
            for (int c = 0; c < hybridChannels.Count && c < channelReferences.Count; c++)
            {
                if (double.IsFinite(hybridChannels[c][i].Y))
                {
                    continue;
                }

                double level = channelReferences[c][i].Y;
                if (double.IsFinite(level) && double.IsFinite(loudest) &&
                    level > loudest - HybridDropoutFloorDb)
                {
                    missingContributor = true;
                    break;
                }
            }

            points.Add(new SignalPoint(
                sum[i].X,
                !missingContributor && double.IsFinite(sum[i].Y)
                    ? sum[i].Y + offsetDb
                    : double.NaN));
        }

        return points;
    }

    /// <summary>
    /// How far under the loudest channel an absent capture must sit before the sum
    /// carries on without it, in dB.
    /// </summary>
    /// <remarks>
    /// A capture stops below its channel's protective high-pass, which is usually far
    /// under that channel's own crossover, so in practice this is never reached and
    /// the sum simply continues. When it IS reached the honest answer is a break: the
    /// alternative sums one set of sources and corrects it with a loss measured across
    /// another, which reads as a confident curve rather than as the gap it is.
    /// </remarks>
    private const double HybridDropoutFloorDb = 25;

    /// <returns>
    /// Each channel's own offset, in channel order and skipping the channels with
    /// nothing to compare, together with the set's single figure — the median of
    /// them. Both, because the SPREAD between the per-channel offsets is what judges
    /// the set (see <see cref="HybridMagnitudes.SpreadDb"/>) and taking the median
    /// alone would throw it away. Zero for an empty set: with nothing to align
    /// against, the captures are drawn at their own level rather than pushed
    /// somewhere by an invented figure.
    /// </returns>
    private static (double?[] PerChannel, double SetOffsetDb) ResolveHybridOffsetsDb(
        IReadOnlyList<IReadOnlyList<SignalPoint>> hybrids,
        IReadOnlyList<AnalysisCurve> references)
    {
        var perChannel = new double?[hybrids.Count];
        for (int i = 0; i < hybrids.Count && i < references.Count; i++)
        {
            perChannel[i] = SpatialAverageOffsets.ChannelDatumDb(hybrids[i], references[i].Points);
        }

        List<double> known = perChannel
            .Where(offset => offset.HasValue)
            .Select(offset => offset!.Value)
            .ToList();
        return known.Count == 0 ? (perChannel, 0.0) : (perChannel, SpatialAverageOffsets.Median(known));
    }
}
