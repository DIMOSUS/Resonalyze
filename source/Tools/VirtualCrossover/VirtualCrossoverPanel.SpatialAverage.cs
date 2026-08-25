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
/// split, for the same reason, as <see cref="GatedMagnitude"/>. Smoothing does not
/// commute with an amplitude sum: a fractional-octave window straddling a steep
/// crossover skirt pulls each channel's level up toward its own passband, so a sum
/// of smoothed channels rides above the smoothed sum exactly at the corners, which
/// is where a hybrid gets read. Sum the raw curves, add the raw loss, smooth the
/// finished curve once — the order the measured Sum beside it is built in.
/// </param>
/// <param name="ChannelOffsetsDb">
/// Each channel's own offset IN CHANNEL ORDER, null where the two curves never
/// overlap enough to compare. Positional rather than packed: the spread read-out
/// names the channel beside its figure, and a packed list silently shifted those
/// names onto the wrong drivers as soon as one channel had nothing to say.
/// </param>
internal sealed record HybridMagnitudes(
    IReadOnlyList<IReadOnlyList<SignalPoint>> Channels,
    IReadOnlyList<IReadOnlyList<SignalPoint>> UnsmoothedChannels,
    IReadOnlyList<double?> ChannelOffsetsDb,
    double OffsetDb)
{
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
            List<double> known = ChannelOffsetsDb
                .Where(offset => offset.HasValue)
                .Select(offset => offset!.Value)
                .ToList();
            return known.Count < 2 ? 0.0 : known.Max() - known.Min();
        }
    }
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
    private void ShowSpatialAverageMenu(VirtualCrossoverChannel channel)
    {
        var menu = new ContextMenuStrip();

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

        menu.Show(Cursor.Position);
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

        LiveCaptureDocument? document = channel.SpatialAverage;
        // A stored path with no document behind it is the missing case: name it from
        // the path, since the file that carried the title is the file that is gone.
        string? path = channel.Settings.SpatialAveragePath;
        control.SetSpatialAverage(
            document?.Title
                ?? (string.IsNullOrWhiteSpace(path)
                    ? null
                    : Path.GetFileNameWithoutExtension(path)),
            document?.Recipe.IntegratedSeconds,
            resolved: document != null);
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
            if (state.SpatialAverage == null)
            {
                return LiveCaptureSetVerdict.No(
                    "Needs a spatial average on every channel that plays. Attach " +
                    "one per channel with the MMM button.");
            }

            captures.Add(state.SpatialAverage);
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
        bool live = hybridAvailable && radioViewMagnitude.Checked;
        UiStyle.SetTextEnabledLook(checkBoxHybrid, live, interactive: true);
        toolTip.SetToolTip(
            checkBoxHybrid,
            !hybridAvailable
                ? verdict.Reason ?? "Needs a spatial average on every channel that " +
                    "plays. Attach one per channel with the MMM button."
                : !radioViewMagnitude.Checked
                ? "The hybrid is a magnitude view: a spatial average carries no " +
                    "phase, so the phase and impulse views keep reading the impulse " +
                    "responses."
                : "Draw each channel's magnitude from its spatial average with this " +
                    "channel's DSP chain on top, instead of from the impulse response " +
                    "measured at one point. Both sums follow, carrying the summation " +
                    "loss the impulse responses measure — the other side needs its " +
                    "own captures too, and its dashed sum is dropped rather than " +
                    "drawn by the other method. Timing, polarity and that loss are " +
                    "unaffected: they keep reading the impulse responses.\r\n\r\n" +
                    "The channel curves are exact — a filter does not depend on " +
                    "microphone position. The Sum is an estimate: the interference " +
                    "between channels does depend on position, and its loss is read " +
                    "at one, so its peaks and dips can be either stronger or weaker " +
                    "than the volume's average. The gap tends to grow the faster the " +
                    "phase turns across that volume — generally small in the bass, " +
                    "largest at a crossover high up.");
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

        var hybrids = new List<IReadOnlyList<SignalPoint>>(processed.Count);
        var unsmoothed = new List<IReadOnlyList<SignalPoint>>(processed.Count);
        for (int i = 0; i < processed.Count; i++)
        {
            // Built RAW and smoothed here rather than twice through the chain: the
            // shared builder's own last step is this same smoothing, so smoothing its
            // unsmoothed output reproduces what it would have returned, and the
            // expensive part — the analytic chain over the whole grid — runs once.
            if (BuildHybridChannelCurve(
                processed[i].Channel, rightSide, references[i].Points, smoothingCode: 0)
                is not { } raw)
            {
                return null;
            }

            unsmoothed.Add(raw);
            hybrids.Add(smoothingCode == 0 || raw.Count < 2
                ? raw
                : DataHelper.SmoothBandLevels(
                    raw,
                    SpectrumSmoothing.SmoothingOctaves(smoothingCode),
                    SpectrumSmoothing.IsPsychoacoustic(smoothingCode)));
        }

        // The datum is read on the two measurements BEFORE any chain, never on the
        // curves above. Those carry the DSP on both sides and it does NOT cancel: the
        // impulse response is filtered and then gated while the capture is filtered
        // analytically, and a gate does not commute with a filter; and the band the
        // median is taken over is set by the channel's peak, which the crossover
        // moves. Reading it there made the whole hybrid set drift up and down the
        // axis while the user tuned — and that offset travels to the EQ Wizard.
        (double?[] offsets, double setOffset) =
            ResolveRawHybridOffsetsDb(processed, rightSide);
        return new HybridMagnitudes(hybrids, unsmoothed, offsets, setOffset);
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
        if (state.SpatialAverage is not { } document || reference.Count == 0)
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
            // The CHANNEL's rate: the chain is what the user's DSP will run, and it
            // will run at the project's rate, not at whatever the capture was taken
            // at. The capture's own rate is already folded into its stored levels.
            state.SampleRate,
            Calibration,
            reference.Select(point => point.X).ToList(),
            smoothingCode);
    }

    /// <summary>
    /// Each channel's datum and the set's, read on the RAW pair: the capture with no
    /// chain against the channel's bypass response. A property of the two
    /// measurements, so nothing the user tunes can move it.
    /// </summary>
    /// <remarks>
    /// The raw impulse-response curve is built here rather than taken from the
    /// redraw, which only has it when the Raw view is switched on. One gated build
    /// per channel while the hybrid is drawn; the alternative — reading the datum off
    /// the processed curves — is what this exists to stop.
    /// </remarks>
    private (double?[] PerChannel, double SetOffsetDb) ResolveRawHybridOffsetsDb(
        IReadOnlyList<ProcessedChannel> processed,
        bool rightSide)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.HybridOffsets");
        var captures = new List<IReadOnlyList<SignalPoint>>(processed.Count);
        var references = new List<AnalysisCurve>(processed.Count);
        for (int i = 0; i < processed.Count; i++)
        {
            VirtualCrossoverChannelState state =
                processed[i].Channel.SideState(rightSide);
            AnalysisCurve? rawIr = state.TransferImpulseResponse is { } ir &&
                state.SampleRate > 0
                    ? BuildRawMagnitudeCurve(ir, state.TransferPeakIndex, state.SampleRate)
                    : null;
            IReadOnlyList<SignalPoint>? rawCapture =
                rawIr != null && state.SpatialAverage is { } document
                    ? SpatialAverageHybrid.BuildChannelCurve(
                        document,
                        DspChannelChain.Identity,
                        state.SampleRate,
                        Calibration,
                        rawIr.Points.Select(point => point.X).ToList(),
                        magnitudeGate.SmoothingInverseOctaves)
                    : null;

            // A channel that cannot produce the raw pair contributes no datum. Its
            // hole stays in place; it never falls back to the processed curves, which
            // would put one channel's offset on a different footing from the rest.
            captures.Add(rawCapture ?? []);
            references.Add(rawIr ?? new AnalysisCurve("raw", []));
        }

        return ResolveHybridOffsetsDb(captures, references);
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
    /// The hybrid channels summed the way the impulse responses sum: their magnitudes
    /// added as amplitudes, then the summation loss the impulse responses measure
    /// laid on top. Null when the set has no usable grid.
    /// </summary>
    /// <remarks>
    /// A spatial average carries no phase, so the channels cannot be summed as
    /// vectors — the arithmetic sum alone would draw a system that cancels nowhere,
    /// which is exactly the picture a tune must not be built on. The loss curve
    /// supplies what the average threw away: it is the signed dB gap between the
    /// complex sum and the phase-blind one, measured on the honest impulse responses,
    /// and it carries over because a per-channel filter changes both halves of that
    /// ratio by the same factor.
    /// <para>
    /// An ESTIMATE, unlike the per-channel curves, and the distinction is not
    /// pedantry. A channel's own hybrid is exact because a filter does not depend on
    /// position. The cross-term between two channels does: ⟨|H₁+H₂|²⟩ needs
    /// ⟨H₁H₂*⟩ over the volume, and a loss curve read at one microphone position
    /// carries that product at ONE position. So this draws a point's interference,
    /// and its peaks and dips may come out either stronger OR weaker than the
    /// volume's average — nothing makes one position's cross-term the more extreme
    /// of the two, and a position where the two channels sit near quadrature carries
    /// almost none of it while the average may be firmly constructive. What holds is
    /// only a tendency: the gap grows the faster the relative phase turns across the
    /// volume, so it is generally small in the bass and largest at a crossover high
    /// up. Nothing downstream treats this as measured.
    /// </para>
    /// <para>
    /// Where the loss curve breaks — its level gate finds every channel filtered far
    /// under the local level, so the gap it would report is two noise floors doing
    /// arithmetic — the hybrid sum breaks with it rather than falling back to a
    /// lossless sum, which would draw its most confident fiction exactly where the
    /// measurement is weakest. A channel whose own curve is NaN at a point (below its
    /// protective high-pass, or past the end of its capture's grid) drops out of that
    /// point's sum only while it is INAUDIBLE there — see
    /// <see cref="HybridDropoutFloorDb"/>. Dropping a channel that still contributes
    /// would leave the numerator summing one set of sources while the loss correcting
    /// it was measured across another.
    /// </para>
    /// <para>
    /// Operands unsmoothed, result smoothed once — the rule
    /// <see cref="VirtualCrossoverAnalysis.SumLossCurve"/> is built on, and it applies
    /// here for the same reason. A fractional-octave window straddling a crossover
    /// skirt lifts each channel toward its own passband, so summing smoothed channels
    /// draws a sum that rides above the truth at every corner.
    /// </para>
    /// </remarks>
    private static List<SignalPoint>? BuildHybridSumCurve(
        HybridMagnitudes hybrid,
        IReadOnlyList<SignalPoint> reference,
        IReadOnlyList<IReadOnlyList<SignalPoint>> channelReferences,
        IReadOnlyList<SignalPoint> unsmoothedLoss,
        int smoothingCode)
    {
        int count = Math.Min(reference.Count, unsmoothedLoss.Count);
        foreach (IReadOnlyList<SignalPoint> channel in hybrid.UnsmoothedChannels)
        {
            count = Math.Min(count, channel.Count);
        }

        foreach (IReadOnlyList<SignalPoint> channel in channelReferences)
        {
            count = Math.Min(count, channel.Count);
        }

        if (count <= 0)
        {
            return null;
        }

        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            // The loudest impulse-response level here, so a missing channel can be
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

            double amplitude = 0;
            bool missingContributor = false;
            for (int c = 0; c < hybrid.UnsmoothedChannels.Count; c++)
            {
                double level = hybrid.UnsmoothedChannels[c][i].Y;
                if (double.IsFinite(level))
                {
                    amplitude += DataHelper.DecibelsToAmplitude(level);
                    continue;
                }

                // No capture here. Ignorable only while the impulse response says this
                // channel is not part of the sum at this frequency either.
                double reference_c = c < channelReferences.Count
                    ? channelReferences[c][i].Y
                    : double.NaN;
                if (double.IsFinite(reference_c) && double.IsFinite(loudest) &&
                    reference_c > loudest - HybridDropoutFloorDb)
                {
                    missingContributor = true;
                    break;
                }
            }

            double lossDb = unsmoothedLoss[i].Y;
            points.Add(new SignalPoint(
                reference[i].X,
                !missingContributor && amplitude > 0 && double.IsFinite(lossDb)
                    ? DataHelper.AmplitudeToDecibels(amplitude) + hybrid.OffsetDb + lossDb
                    : double.NaN));
        }

        return smoothingCode == 0 || points.Count < 2
            ? points
            : DataHelper.SmoothBandLevels(
                points,
                SpectrumSmoothing.SmoothingOctaves(smoothingCode),
                SpectrumSmoothing.IsPsychoacoustic(smoothingCode));
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

    /// <summary>
    /// How far below its own peak a channel is still read when its offset is taken.
    /// </summary>
    /// <remarks>
    /// Wide enough to hold a driver's whole working band with its crossover skirts,
    /// narrow enough to stay out of the stopband — where the impulse response shows
    /// what the room and the noise floor left of a filtered driver while the hybrid
    /// shows the filter's own analytic slope, and the two part by tens of dB.
    /// </remarks>
    private const double HybridOffsetBandDb = 20;

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
            perChannel[i] = ResolveChannelOffsetDb(hybrids[i], references[i].Points);
        }

        List<double> known = perChannel
            .Where(offset => offset.HasValue)
            .Select(offset => offset!.Value)
            .ToList();
        return known.Count == 0 ? (perChannel, 0.0) : (perChannel, Median(known));
    }

    /// <summary>
    /// The middle of a set of levels — the mean of the two central values when there
    /// is an even number of them, not the upper one.
    /// </summary>
    /// <remarks>
    /// Taking the upper central value moves the whole hybrid set by half the gap
    /// between the two middle channels, which on a four-way is not a rounding
    /// difference. The list is sorted in place.
    /// </remarks>
    private static double Median(List<double> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : 0.5 * (values[middle - 1] + values[middle]);
    }

    // One channel's median difference inside its own working band. Null when the two
    // curves never overlap there — nothing to align against.
    private static double? ResolveChannelOffsetDb(
        IReadOnlyList<SignalPoint> hybrid,
        IReadOnlyList<SignalPoint> reference)
    {
        int count = Math.Min(hybrid.Count, reference.Count);
        // The peak is taken over the points where BOTH curves exist, or the band it
        // sets could sit where the hybrid has nothing to say.
        double peak = double.NegativeInfinity;
        for (int k = 0; k < count; k++)
        {
            if (double.IsFinite(reference[k].Y) && double.IsFinite(hybrid[k].Y))
            {
                peak = Math.Max(peak, reference[k].Y);
            }
        }

        if (double.IsNegativeInfinity(peak))
        {
            return null;
        }

        double floor = peak - HybridOffsetBandDb;
        var differences = new List<double>();
        for (int k = 0; k < count; k++)
        {
            double difference = reference[k].Y - hybrid[k].Y;
            if (double.IsFinite(difference) && reference[k].Y >= floor)
            {
                differences.Add(difference);
            }
        }

        return differences.Count == 0 ? null : Median(differences);
    }
}
