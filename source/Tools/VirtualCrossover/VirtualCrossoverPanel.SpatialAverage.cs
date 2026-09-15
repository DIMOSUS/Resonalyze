using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

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
    /// <summary>Channels drawn from their point measurement for lack of a spatial average; must always be surfaced to the user.</summary>
    public IReadOnlyList<bool> PointMeasuredChannels { get; init; } = [];

    public int PointMeasuredCount => PointMeasuredChannels.Count(fallback => fallback);

    /// <summary>Largest minus smallest per-channel offset, dB: judges set coherence, not capture/IR agreement. See docs/tech/spatial-average.md#set-offset-and-spread.</summary>
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

    /// <summary>Every channel's datum on this side, muted ones included, so mutes cannot move the offset or the warning.</summary>
    public IReadOnlyList<SetDatum> SetDatumsDb { get; init; } = [];
}

// Hybrid magnitude view: spatial averages refine the drawn magnitude only; timing, polarity and loss still read the IRs.
// See docs/tech/spatial-average.md.
public partial class VirtualCrossoverPanel
{
    internal VirtualCrossoverSpatialAverageMode SpatialAverageMode =>
        project.SpatialAverageMode ?? (HasAnyArrayCapture()
            ? VirtualCrossoverSpatialAverageMode.MicArray
            : VirtualCrossoverSpatialAverageMode.MovingMic);

    /// <summary>Stores the guessed mode once, the first time there is anything to guess from; true when it wrote one.</summary>
    /// <remarks>Freezing keeps a later measurement from flipping the project's level source. See docs/tech/spatial-average.md#mode-selection.</remarks>
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
            // The relative path names the previously imported capture; left standing it would steer the next search to it.
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
        SettleSpatialAverageMode();
        RefreshSpatialAverageStatus(channel);
        RefreshHybridAvailability();
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
        // Missing capture: name it from the stored path. Arrays are not attached by path, so only MovingMic can go missing.
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

    /// <summary>Re-attaches a persisted capture via the same path ladder as measurements.</summary>
    /// <remarks>An unresolved capture leaves the stored path standing: it drives relink and the button's warning.</remarks>
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
                // Pin the actual read location: the autosave copy has no session file beside it to search from.
                settings.SpatialAveragePath = path;
            }
        }
        catch (Exception exception)
        {
            _ = exception;
        }
    }

    /// <summary>Whether every playing channel has a spatial average forming one set (the hybrid toggle's gate).</summary>
    private LiveCaptureSetVerdict JudgeSpatialAverages =>
        JudgeSideSpatialAverages(project.ActiveSideRight);

    /// <summary>Bypass response on canonical terms (own onset, fixed window, no calibration, no smoothing), matching how the spread threshold was calibrated.</summary>
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

    /// <summary>Both sides' captures must form one set: the opposite sum borrows the active side's offset. See docs/tech/spatial-average.md#coverage-and-set-verdict.</summary>
    private bool CanDrawOppositeHybridSum(bool oppositeRight)
    {
        if (!TryCollectSideCaptures(project.ActiveSideRight, out var active).Coherent ||
            !TryCollectSideCaptures(oppositeRight, out var opposite).Coherent)
        {
            return false;
        }

        return JudgeSidesShareAnOffset(active, opposite).Coherent;
    }

    /// <summary>Δ L−R level source in hybrid mode (spatial averages through chains), or null for point levels.</summary>
    /// <remarks>Follows hybrid intent, not the current view, so the basis does not flip on a view glance. See docs/tech/spatial-average.md#level-read-outs.</remarks>
    private Func<VirtualCrossoverChannel, double, double, double?>?
        HybridStereoLevelReader() =>
        checkBoxHybrid.Checked && hybridAvailable &&
        CanDrawOppositeHybridSum(!project.ActiveSideRight)
            ? HybridStereoLevelDeltaDb
            : null;

    // Null when a side has no capture or the captures do not overlap the band; the read-out then falls back to point levels.
    private double? HybridStereoLevelDeltaDb(
        VirtualCrossoverChannel channel, double lowHz, double highHz)
    {
        List<double> grid = HybridLevelGrid(lowHz, highHz);
        IReadOnlyList<SignalPoint>? left =
            BuildHybridSideLevelCurve(channel, rightSide: false, grid);
        IReadOnlyList<SignalPoint>? right =
            BuildHybridSideLevelCurve(channel, rightSide: true, grid);
        return left == null || right == null
            ? null
            : SpatialAverageHybrid.BandLevelDeltaDb(left, right);
    }

    private IReadOnlyList<SignalPoint>? BuildHybridSideLevelCurve(
        VirtualCrossoverChannel channel, bool rightSide, List<double> grid)
    {
        VirtualCrossoverChannelState state = channel.PhysicalSideState(rightSide);
        if (state.SpatialAverageFor(SpatialAverageMode) is not { } document)
        {
            return null;
        }

        return SpatialAverageHybrid.BuildChannelCurve(
            document,
            channel.Pair.Bypass
                ? DspChannelChain.Identity
                : channel.Pair.ToChain(rightSide),
            channel.ProcessorSampleRateFor(rightSide),
            SpatialAverageCalibrationFor(state),
            grid,
            smoothingCode: 0);
    }

    /// <summary>"vs Front" level source in hybrid mode, or null for point levels. Active side only, so the set offset cancels.</summary>
    private Func<IReadOnlyList<ProcessedChannel>, IReadOnlyList<ProcessedChannel>,
        double, double, double?>? HybridGroupLevelReader() =>
        checkBoxHybrid.Checked && hybridAvailable
            ? HybridGroupLevelDeltaDb
            : null;

    // Null when any member lacks a capture (power sum would understate the group) or the groups share no in-band point.
    private double? HybridGroupLevelDeltaDb(
        IReadOnlyList<ProcessedChannel> members,
        IReadOnlyList<ProcessedChannel> front,
        double lowHz,
        double highHz)
    {
        List<double> grid = HybridLevelGrid(lowHz, highHz);
        List<SignalPoint>? zoneCurve = BuildHybridGroupPowerCurve(members, grid);
        List<SignalPoint>? frontCurve = BuildHybridGroupPowerCurve(front, grid);
        return zoneCurve == null || frontCurve == null
            ? null
            : SpatialAverageHybrid.BandLevelDeltaDb(zoneCurve, frontCurve);
    }

    private List<SignalPoint>? BuildHybridGroupPowerCurve(
        IReadOnlyList<ProcessedChannel> members, List<double> grid)
    {
        bool rightSide = project.ActiveSideRight;
        var curves = new List<IReadOnlyList<SignalPoint>>(members.Count);
        var bands = new List<(double LowHz, double HighHz)>(members.Count);
        foreach (ProcessedChannel member in members)
        {
            VirtualCrossoverChannelState state =
                member.Channel.SideState(rightSide);
            if (state.SpatialAverageFor(SpatialAverageMode) is not { } document)
            {
                return null;
            }

            IReadOnlyList<SignalPoint>? curve = SpatialAverageHybrid.BuildChannelCurve(
                document,
                // Bypassed members do reach grouped read-outs, contributing their raw signal as on the plot.
                member.Channel.Pair.Bypass
                    ? DspChannelChain.Identity
                    : member.Channel.Pair.ToChain(rightSide),
                member.Channel.ProcessorSampleRateFor(rightSide),
                SpatialAverageCalibrationFor(state),
                grid,
                smoothingCode: 0);
            if (curve == null)
            {
                return null;
            }

            curves.Add(curve);
            bands.Add(HybridGroupMemberBand(member.Channel, rightSide));
        }

        return SpatialAverageHybrid.PowerSum(curves, bands);
    }

    /// <summary>Band where a group member is expected to play: inside it a silent capture breaks the group point, outside the member is absent.</summary>
    /// <remarks>Bypassed members use their full measured range: their idle crossover corners say nothing about presence.</remarks>
    internal static (double LowHz, double HighHz) HybridGroupMemberBand(
        VirtualCrossoverChannel channel, bool rightSide) =>
        channel.Pair.Bypass
            ? (20.0, 20_000.0)
            : VirtualCrossoverJunctions.GetChannelBand(
                channel.SideSettings(rightSide));

    // Log grid finer than the captures (~1/48 oct); uniform weights on it reproduce the IR band level's 1/f weighting.
    private static List<double> HybridLevelGrid(double lowHz, double highHz)
    {
        const double PointsPerOctave = 48.0;
        int steps = Math.Max(
            1, (int)Math.Ceiling(Math.Log2(highHz / lowHz) * PointsPerOctave));
        var grid = new List<double>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            grid.Add(lowHz * Math.Pow(highHz / lowHz, (double)i / steps));
        }

        return grid;
    }

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

        LiveCaptureSetVerdict verdict = LiveCaptureDocument.JudgeSet(union);
        return verdict.Coherent
            ? verdict
            : LiveCaptureSetVerdict.No(
                "The two sides' captures are not one set, so the active side's " +
                "offset cannot level them both. " + verdict.Reason);
    }

    /// <summary>That side's playing channels all carry captures and those form one set (recipe decides, not coverage).</summary>
    private LiveCaptureSetVerdict JudgeSideSpatialAverages(bool rightSide)
    {
        LiveCaptureSetVerdict gathered =
            TryCollectSideCaptures(rightSide, out List<LiveCaptureDocument> captures);
        return gathered.Coherent ? LiveCaptureDocument.JudgeSet(captures) : gathered;
    }

    private LiveCaptureSetVerdict TryCollectSideCaptures(
        bool rightSide, out List<LiveCaptureDocument> captures)
    {
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
                // Array sets may have gaps: both families share the loopback reference, and below the first cabin mode a point equals the average.
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

    // Cached set verdict; the toggle is muted by hand, so its Enabled state cannot stand in for this.
    private bool hybridAvailable;

    // Groups view included: a group line is the same hybrid sum construction over its members. See docs/tech/spatial-average.md#hybrid-toggle.
    private bool HybridRequested =>
        checkBoxHybrid.Checked &&
        hybridAvailable;

    private void RefreshHybridAvailability()
    {
        // The tick is intent and outlives coverage (like a pinned gate outlives its sources).
        LiveCaptureSetVerdict verdict = JudgeSpatialAverages;
        hybridAvailable = verdict.Coherent;

        // Muted, not unticked or disabled: UiStyle.SetTextEnabledLook would memorize the reminder colour, and WinForms' disabled grey is unreadable here.
        bool live = hybridAvailable && radioViewMagnitude.Checked;
        checkBoxHybrid.ForeColor = !live
            ? UiPalette.TextDisabled
            // Available but unticked: the plot ignores attached captures, so warn in the error colour.
            : checkBoxHybrid.Checked ? hybridToggleColor : UiPalette.ErrorSoft;
        checkBoxHybrid.AutoCheck = live;
        checkBoxHybrid.TabStop = live;
        toolTip.SetToolTip(
            checkBoxHybrid,
            !hybridAvailable
                ? verdict.Reason ?? "Needs a spatial average on every channel that " +
                    "plays. Attach one per channel with the MMM button."
                : live && !checkBoxHybrid.Checked
                ? "Every channel that plays has a spatial average attached and the " +
                    "plot is not using one: these curves are the response at a " +
                    "single microphone position, dips and all. Tick this to draw " +
                    "them from the averages instead." +
                    Environment.NewLine + Environment.NewLine +
                    "What that changes is below."
                : !radioViewMagnitude.Checked
                ? "The hybrid is a magnitude view: a spatial average carries no " +
                    "phase, so the phase and impulse views keep reading the impulse " +
                    "responses."
                : "Draw each channel's magnitude from its spatial average with this " +
                    "channel's DSP chain on top, instead of from the impulse response " +
                    "measured at one point. Both sums follow, adding the channels as " +
                    "phasors with the phase the impulse responses measure — the other " +
                    "side needs its own captures too, and its dashed sum is dropped " +
                    "rather than drawn by the other method. The read-out's level " +
                    "rows follow too: Level Δ L−R compares the sides' captures " +
                    "under the same condition, and the vs Front ΔdB compares the " +
                    "groups'. Timing, polarity and the " +
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

    /// <summary>This redraw's hybrid magnitudes, shared by drawing and summation; null unless every channel yields a curve.</summary>
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

        // Datum is read on the raw pair, never the processed curves (gate does not commute with the chain; tuning would drift the offset).
        // Resolved first so point-measured fallbacks can be pre-subtracted. See docs/tech/spatial-average.md#set-offset-and-spread.
        (double?[] offsets, double setOffset, IReadOnlyList<SetDatum> setDatums) =
            ResolveRawHybridOffsetsDb(processed, rightSide);

        var hybrids = new List<IReadOnlyList<SignalPoint>>(processed.Count);
        var unsmoothed = new List<IReadOnlyList<SignalPoint>>(processed.Count);
        var pointMeasured = new bool[processed.Count];
        for (int i = 0; i < processed.Count; i++)
        {
            // Built raw and smoothed here: the shared builder's last step is this smoothing, so the expensive chain runs once.
            IReadOnlyList<SignalPoint>? raw = BuildHybridChannelCurve(
                processed[i].Channel, rightSide, references[i].Points, smoothingCode: 0);
            if (raw == null)
            {
                if (SpatialAverageMode != VirtualCrossoverSpatialAverageMode.MicArray)
                {
                    return null;
                }

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

    /// <summary>One channel's magnitude from its spatial average through its chain, on the reference grid; null without an average.</summary>
    /// <remarks>Arithmetic lives in <see cref="SpatialAverageHybrid"/>, shared with the EQ Wizard.</remarks>
    private IReadOnlyList<SignalPoint>? BuildHybridChannelCurve(
        VirtualCrossoverChannel channel,
        bool rightSide,
        IReadOnlyList<SignalPoint> reference,
        int smoothingCode)
    {
        // The requested side, never the active one: the opposite-side sum is built from this too.
        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        if (state.SpatialAverageFor(SpatialAverageMode) is not { } document ||
            reference.Count == 0)
        {
            return null;
        }

        return SpatialAverageHybrid.BuildChannelCurve(
            document,
            // Same chain as the processed response, Bypass included.
            channel.Pair.Bypass
                ? DspChannelChain.Identity
                : channel.Pair.ToChain(rightSide),
            // Processor rate: the chain is what the device runs; the capture's rate is already folded into its levels.
            channel.ProcessorSampleRateFor(rightSide),
            // Swap to the panel calibration is exact for single-file captures; mixed-calibration captures keep their own (see SpatialAverageHybrid).
            SpatialAverageCalibrationFor(state),
            reference.Select(point => point.X).ToList(),
            smoothingCode);
    }

    /// <summary>Per-channel and set datums on the raw pair (capture without chain vs bypass response), so tuning cannot move them.</summary>
    /// <remarks>Median over every channel with a capture, muted included. See docs/tech/spatial-average.md#set-offset-and-spread.</remarks>
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
            // No capture: not part of the set. A capture that cannot compare stays as a named hole.
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

    // The panel's list plus drawn channels it does not hold (harness-built).
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

    // Null when the raw pair is unavailable; never falls back to processed curves (different footing).
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
            // Canonical terms, matching HybridOffsetDatumMeasurement and the spread threshold's calibration.
            SpatialAverageCalibration.Off,
            rawIr.Points.Select(point => point.X).ToList(),
            smoothingCode: 0);
        return rawCapture == null
            ? null
            : SpatialAverageOffsets.ChannelDatumDb(rawCapture, rawIr.Points);
    }

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

    /// <summary>Hybrid channels summed as phasors: each gated spectrum rescaled per bin to its spatial-average level.</summary>
    /// <remarks>An estimate: the phase is from one mic position. See docs/tech/spatial-average.md#hybrid-sum.</remarks>
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
            channels.Add((
                new ImpulseMeasurementView(
                    processed[c].ImpulseResponse, anchorIndex, processed[c].SampleRate),
                hybrid.UnsmoothedChannels[c]));
        }

        // Unsmoothed, masked, then smoothed: masked points must not feed neighbours' means (SmoothBandLevels skips NaN).
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

    /// <summary>Adds the set offset and breaks the sum where a still-playing channel has no capture.</summary>
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

    /// <summary>dB under the loudest channel below which a missing capture is ignored; above it the sum breaks.</summary>
    private const double HybridDropoutFloorDb = 25;

    /// <returns>Per-channel offsets (positional; spread judges the set) and their median; zero for an empty set.</returns>
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
