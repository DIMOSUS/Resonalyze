using Resonalyze.Dsp;

namespace Resonalyze;

/// <param name="ArrayStandOff">True: the worst array's distance from its IR; false: the moving-mic set's offset.</param>
internal sealed record HybridReadOut(double Db, bool ArrayStandOff);

/// <summary>One channel of the set and how far its capture sits from its response.</summary>
internal readonly record struct SetDatum(
    VirtualCrossoverChannel Channel,
    double? DatumDb,
    bool RightSide = false);

internal sealed record HybridMagnitudes(
    IReadOnlyList<IReadOnlyList<SignalPoint>> Channels,
    IReadOnlyList<IReadOnlyList<SignalPoint>> UnsmoothedChannels,
    IReadOnlyList<double?> ChannelOffsetsDb,
    double OffsetDb)
{
    /// <summary>Whose the positional lists are, in order: the shown group, which may hold fewer channels than the render.</summary>
    public IReadOnlyList<VirtualCrossoverChannel> DrawnChannels { get; init; } = [];

    /// <summary>Channels drawn from their point measurement for lack of a spatial average; must always be surfaced to the user.</summary>
    public IReadOnlyList<bool> PointMeasuredChannels { get; init; } = [];

    public int PointMeasuredCount => PointMeasuredChannels.Count(fallback => fallback);

    /// <summary>Largest minus smallest per-channel offset, dB: judges set coherence, not capture/IR agreement. See docs/tech/spatial-average.md#set-offset-and-spread.</summary>
    public double SpreadDb
    {
        get
        {
            List<double> known = KnownDatumsDb();
            return known.Count < 2 ? 0.0 : known.Max() - known.Min();
        }
    }

    /// <summary>The datum furthest from zero, signed: how far the worst array stands off its IR.</summary>
    public double? WorstDatumDb
    {
        get
        {
            List<double> known = KnownDatumsDb();
            return known.Count == 0 ? null : known.MaxBy(Math.Abs);
        }
    }

    private List<double> KnownDatumsDb() =>
        (SetDatumsDb.Count > 0
            ? SetDatumsDb.Select(entry => entry.DatumDb)
            : ChannelOffsetsDb)
        .Where(offset => offset.HasValue)
        .Select(offset => offset!.Value)
        .ToList();

    /// <summary>The set's datums (this side, and the other when both are one set), muted ones included, so mutes cannot move the offset or the warning.</summary>
    public IReadOnlyList<SetDatum> SetDatumsDb { get; init; } = [];

    /// <summary>One group's slice of the set's hybrid.</summary>
    /// <remarks>Per-channel lists are narrowed positionally; the set offset and datums are not, keeping all lines on one axis.</remarks>
    public HybridMagnitudes Subset(IReadOnlyList<int> positions) =>
        this with
        {
            Channels = [.. positions.Select(index => Channels[index])],
            UnsmoothedChannels = [.. positions.Select(index => UnsmoothedChannels[index])],
            ChannelOffsetsDb = [.. positions.Select(index => ChannelOffsetsDb[index])],
            DrawnChannels = DrawnChannels.Count == 0
                ? []
                : [.. positions.Select(index => DrawnChannels[index])],
            PointMeasuredChannels = PointMeasuredChannels.Count == 0
                ? []
                : [.. positions.Select(index => PointMeasuredChannels[index])]
        };
}

/// <summary>The hybrid magnitude view read off a session: spatial averages refine the drawn magnitude only, while timing,
/// polarity and loss still read the impulse responses. UI-free; the arithmetic it shares with the EQ Wizard lives in
/// <see cref="SpatialAverageHybrid"/>. See docs/tech/spatial-average.md.</summary>
internal sealed class VirtualCrossoverHybrid(VirtualCrossoverSession session)
{
    /// <summary>dB under the loudest channel below which a missing capture is ignored; above it the sum breaks.</summary>
    private const double DropoutFloorDb = 25;

    /// <summary>That side's playing channels carry captures (all for MMM; arrays may have gaps) and those form one set (recipe decides, not coverage).</summary>
    public LiveCaptureSetVerdict JudgeSide(bool rightSide)
    {
        LiveCaptureSetVerdict gathered =
            CollectSideCaptures(rightSide, out List<LiveCaptureDocument> captures);
        return gathered.Coherent ? LiveCaptureDocument.JudgeSet(captures) : gathered;
    }

    public LiveCaptureSetVerdict CollectSideCaptures(
        bool rightSide, out List<LiveCaptureDocument> captures)
    {
        VirtualCrossoverSpatialAverageMode mode = session.SpatialAverageMode;
        List<VirtualCrossoverChannelState> playing = session.Channels
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
            if (state.SpatialAverageFor(mode) is not { } capture)
            {
                // Array sets may have gaps: both families share the loopback reference, and below the first cabin mode a point equals the average.
                if (mode == VirtualCrossoverSpatialAverageMode.MicArray)
                {
                    continue;
                }

                return LiveCaptureSetVerdict.No(
                    "Needs a spatial average on every channel that plays. " +
                    "Attach one per channel with the MMM button: a moving-microphone " +
                    "capture or a response file.");
            }

            captures.Add(capture);
        }

        if (captures.Count == 0)
        {
            return LiveCaptureSetVerdict.No(
                mode == VirtualCrossoverSpatialAverageMode.MicArray
                    ? "No channel on this side was measured with a microphone array."
                    : "No channel on this side has a spatial average.");
        }

        return LiveCaptureSetVerdict.Ok;
    }

    /// <summary>Both sides' captures must form one set: the opposite sum borrows the active side's offset. See docs/tech/spatial-average.md#coverage-and-set-verdict.</summary>
    public bool CanDrawOppositeSum(bool oppositeRight)
    {
        if (!CollectSideCaptures(session.ActiveSideRight, out var active).Coherent ||
            !CollectSideCaptures(oppositeRight, out var opposite).Coherent)
        {
            return false;
        }

        return JudgeSidesShareAnOffset(active, opposite).Coherent;
    }

    private bool SidesFormOneSet() => CanDrawOppositeSum(!session.ActiveSideRight);

    /// <summary>The read-out's health figure; null while no hybrid is drawn.</summary>
    public HybridReadOut? ReadOut(HybridMagnitudes? hybrid) =>
        hybrid == null
            ? null
            : session.SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray
                ? hybrid.WorstDatumDb is { } worst ? new HybridReadOut(worst, ArrayStandOff: true) : null
                : new HybridReadOut(hybrid.OffsetDb, ArrayStandOff: false);

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

    /// <summary>The Δ L−R level source while the hybrid is requested and both sides' captures form one set, else null for
    /// point levels. Follows hybrid intent, not the current view, so the basis does not flip on a view glance.
    /// See docs/tech/spatial-average.md#level-read-outs.</summary>
    public Func<VirtualCrossoverChannel, double, double, double?>? StereoLevelReader(bool hybridRequested) =>
        hybridRequested && CanDrawOppositeSum(!session.ActiveSideRight)
            ? StereoLevelDeltaDb
            : null;

    /// <summary>The "vs Front" level source while the hybrid is requested, else null for point levels. Active side only, so
    /// the set offset cancels.</summary>
    public Func<IReadOnlyList<ProcessedChannel>, IReadOnlyList<ProcessedChannel>, double, double, double?>?
        GroupLevelReader(bool hybridRequested) =>
        hybridRequested ? GroupLevelDeltaDb : null;

    /// <summary>Δ L−R level of one block off the spatial averages through the chains; null when a side has no capture or the
    /// captures do not overlap the band, and the read-out then falls back to point levels.</summary>
    public double? StereoLevelDeltaDb(
        VirtualCrossoverChannel channel, double lowHz, double highHz)
    {
        List<double> grid = LevelGrid(lowHz, highHz);
        IReadOnlyList<SignalPoint>? left = SideLevelCurve(channel, rightSide: false, grid);
        IReadOnlyList<SignalPoint>? right = SideLevelCurve(channel, rightSide: true, grid);
        return left == null || right == null
            ? null
            : SpatialAverageHybrid.BandLevelDeltaDb(left, right);
    }

    private IReadOnlyList<SignalPoint>? SideLevelCurve(
        VirtualCrossoverChannel channel, bool rightSide, List<double> grid)
    {
        VirtualCrossoverChannelState state = channel.PhysicalSideState(rightSide);
        if (state.SpatialAverageFor(session.SpatialAverageMode) is not { } document)
        {
            return null;
        }

        return SpatialAverageHybrid.BuildChannelCurve(
            document,
            channel.Pair.Bypass
                ? DspChannelChain.Identity
                : channel.Pair.ToChain(rightSide),
            channel.ProcessorSampleRateFor(rightSide),
            session.Calibration.SpatialAverageFor(),
            grid,
            smoothingCode: 0);
    }

    /// <summary>"vs Front" level of a zone off the shown side's captures, so the set offset cancels; null when any member lacks
    /// a capture (a power sum would understate the group) or the groups share no in-band point.</summary>
    public double? GroupLevelDeltaDb(
        IReadOnlyList<ProcessedChannel> members,
        IReadOnlyList<ProcessedChannel> front,
        double lowHz,
        double highHz)
    {
        List<double> grid = LevelGrid(lowHz, highHz);
        List<SignalPoint>? zoneCurve = GroupPowerCurve(members, grid);
        List<SignalPoint>? frontCurve = GroupPowerCurve(front, grid);
        return zoneCurve == null || frontCurve == null
            ? null
            : SpatialAverageHybrid.BandLevelDeltaDb(zoneCurve, frontCurve);
    }

    private List<SignalPoint>? GroupPowerCurve(
        IReadOnlyList<ProcessedChannel> members, List<double> grid)
    {
        bool rightSide = session.ActiveSideRight;
        var curves = new List<IReadOnlyList<SignalPoint>>(members.Count);
        var bands = new List<(double LowHz, double HighHz)>(members.Count);
        foreach (ProcessedChannel member in members)
        {
            VirtualCrossoverChannelState state = member.Channel.SideState(rightSide);
            if (state.SpatialAverageFor(session.SpatialAverageMode) is not { } document)
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
                session.Calibration.SpatialAverageFor(),
                grid,
                smoothingCode: 0);
            if (curve == null)
            {
                return null;
            }

            curves.Add(curve);
            bands.Add(GroupMemberBand(member.Channel, rightSide));
        }

        return SpatialAverageHybrid.PowerSum(curves, bands);
    }

    /// <summary>Band where a group member is expected to play: inside it a silent capture breaks the group point, outside the member is absent.</summary>
    /// <remarks>Bypassed members use the full 20 Hz–20 kHz range: their idle crossover corners say nothing about presence.</remarks>
    internal static (double LowHz, double HighHz) GroupMemberBand(
        VirtualCrossoverChannel channel, bool rightSide) =>
        channel.Pair.Bypass
            ? (20.0, 20_000.0)
            : VirtualCrossoverJunctions.GetChannelBand(channel.SideSettings(rightSide));

    // Log grid finer than the captures (~1/48 oct); uniform weights on it reproduce the IR band level's 1/f weighting.
    private static List<double> LevelGrid(double lowHz, double highHz)
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

    /// <summary>This redraw's hybrid magnitudes, shared by drawing and summation; null when a moving-mic channel yields no
    /// curve (array channels fall back to their point response).</summary>
    public HybridMagnitudes? Build(
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
            ResolveRawOffsetsDb(processed, rightSide);

        var hybrids = new List<IReadOnlyList<SignalPoint>>(processed.Count);
        var unsmoothed = new List<IReadOnlyList<SignalPoint>>(processed.Count);
        var pointMeasured = new bool[processed.Count];
        for (int i = 0; i < processed.Count; i++)
        {
            // Built raw and smoothed here: the shared builder's last step is this smoothing, so the expensive chain runs once.
            IReadOnlyList<SignalPoint>? raw = ChannelCurve(
                processed[i].Channel, rightSide, references[i].Points, smoothingCode: 0);
            if (raw == null)
            {
                if (session.SpatialAverageMode != VirtualCrossoverSpatialAverageMode.MicArray)
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
            DrawnChannels = [.. processed.Select(item => item.Channel)],
            PointMeasuredChannels = pointMeasured,
            SetDatumsDb = setDatums
        };
    }

    /// <summary>One channel's magnitude from its spatial average through its chain, on the reference grid; null without an average.</summary>
    public IReadOnlyList<SignalPoint>? ChannelCurve(
        VirtualCrossoverChannel channel,
        bool rightSide,
        IReadOnlyList<SignalPoint> reference,
        int smoothingCode)
    {
        // The requested side, never the active one: the opposite-side sum is built from this too.
        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        if (state.SpatialAverageFor(session.SpatialAverageMode) is not { } document ||
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
            // Own keeps the capture's correction; a named panel curve swaps exactly for single-file captures, mixed-calibration ones keep their own (see SpatialAverageHybrid).
            session.Calibration.SpatialAverageFor(),
            reference.Select(point => point.X).ToList(),
            smoothingCode);
    }

    /// <summary>The spatial average through no chain, on the impulse responses' level axis; null without a capture of the selected family.</summary>
    public IReadOnlyList<SignalPoint>? PreDspCurve(
        VirtualCrossoverChannel channel,
        bool rightSide,
        IReadOnlyList<SignalPoint> grid,
        int smoothingCode,
        double offsetDb)
    {
        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        if (state.SpatialAverageFor(session.SpatialAverageMode) is not { } document || grid.Count == 0)
        {
            return null;
        }

        IReadOnlyList<SignalPoint>? curve = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            channel.ProcessorSampleRateFor(rightSide),
            session.Calibration.SpatialAverageFor(),
            grid.Select(point => point.X).ToList(),
            smoothingCode);
        return curve == null ? null : ShiftedBy(curve, offsetDb);
    }

    /// <summary>Per-channel and set datums on the raw pair (capture without chain vs bypass response), so tuning cannot move them.</summary>
    /// <remarks>The set is this side, muted included, and the other side too when both are one set; the offset and the health
    /// checks read the same set. See docs/tech/spatial-average.md#set-offset-and-spread.</remarks>
    public (double?[] PerChannel, double SetOffsetDb, IReadOnlyList<SetDatum> SetDatums)
        ResolveRawOffsetsDb(
            IReadOnlyList<ProcessedChannel> processed,
            bool rightSide)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.HybridOffsets");
        var datums = new Dictionary<VirtualCrossoverChannel, double?>();
        List<SetDatum> setDatums = SideDatums(AllChannelsWith(processed), rightSide, datums);

        var perChannel = new double?[processed.Count];
        for (int i = 0; i < processed.Count; i++)
        {
            perChannel[i] = datums.TryGetValue(processed[i].Channel, out double? datum)
                ? datum
                : null;
        }

        if (SidesFormOneSet())
        {
            // A mono pair answers both sides with one state, so it counts once.
            var seen = setDatums.Select(entry => entry.Channel.SideState(entry.RightSide)).ToHashSet();
            setDatums.AddRange(SideDatums(session.Channels, !rightSide, new())
                .Where(entry => seen.Add(entry.Channel.SideState(entry.RightSide))));
        }

        List<double> known = setDatums
            .Where(entry => entry.DatumDb.HasValue)
            .Select(entry => entry.DatumDb!.Value)
            .ToList();
        SpatialAverageMethod method = session.SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray
            ? SpatialAverageMethod.MicArray
            : SpatialAverageMethod.MovingMic;
        return (perChannel, SpatialAverageOffsets.SetOffsetDb(method, known), setDatums);
    }

    // No capture: not part of the set. A capture that cannot compare stays as a named hole.
    private List<SetDatum> SideDatums(
        IEnumerable<VirtualCrossoverChannel> channels,
        bool rightSide,
        Dictionary<VirtualCrossoverChannel, double?> datums)
    {
        VirtualCrossoverSpatialAverageMode mode = session.SpatialAverageMode;
        var setDatums = new List<SetDatum>();
        foreach (VirtualCrossoverChannel channel in channels)
        {
            double? datum = ResolveRawDatumDb(channel, rightSide);
            datums[channel] = datum;
            if (channel.SideState(rightSide).SpatialAverageFor(mode) != null)
            {
                setDatums.Add(new SetDatum(channel, datum, rightSide));
            }
        }

        return setDatums;
    }

    // The session's blocks plus drawn channels it does not hold (harness-built).
    private IEnumerable<VirtualCrossoverChannel> AllChannelsWith(
        IReadOnlyList<ProcessedChannel> processed)
    {
        var seen = new HashSet<VirtualCrossoverChannel>();
        foreach (VirtualCrossoverChannel channel in session.Channels)
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

        if (state.SpatialAverageFor(session.SpatialAverageMode) is not { } document)
        {
            return null;
        }

        // An array is placed on calibrated curves, so it is compared as measured, the IR through its anchor's file;
        // a moving mic on canonical raw terms, which the spread threshold was calibrated on.
        bool array = session.SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray;
        AnalysisCurve rawIr = session.MagnitudeGate.CanonicalRaw(
            ir,
            state.TransferPeakIndex,
            state.SampleRate,
            state.MeasuredBand,
            array ? state.MicrophoneCalibrationCurve : null);
        IReadOnlyList<SignalPoint>? rawCapture = SpatialAverageHybrid.BuildChannelCurve(
            document,
            DspChannelChain.Identity,
            state.SampleRate,
            array ? SpatialAverageCalibration.Own : SpatialAverageCalibration.Off,
            rawIr.Points.Select(point => point.X).ToList(),
            smoothingCode: 0);
        return rawCapture == null
            ? null
            : SpatialAverageOffsets.ChannelDatumDb(rawCapture, rawIr.Points);
    }

    /// <summary>The shown side's hybrid Sum. Anchor and gate are recomputed as pure functions of the processed set and snapshot,
    /// so they match the measured Sum.</summary>
    public List<SignalPoint>? ActiveSum(
        IReadOnlyList<ProcessedChannel> processed,
        IReadOnlyList<AnalysisCurve> magnitudes,
        HybridMagnitudes hybrid,
        MagnitudeGateSnapshot? snapshot = null)
    {
        if (processed.Count == 0)
        {
            return null;
        }

        snapshot ??= session.MagnitudeGate;
        int anchorIndex = ProcessedChannels.SharedStartAnchorIndex(processed);
        return Sum(
            hybrid,
            processed,
            anchorIndex,
            snapshot,
            snapshot.ResolveGateOffsetMs(
                oppositeSide: false, anchorIndex, processed[0].SampleRate),
            magnitudes.Select(curve => (IReadOnlyList<SignalPoint>)curve.Points).ToList());
    }

    /// <summary>Opposite side's hybrid sum from its own captures and loss, but with the ACTIVE side's offset.</summary>
    /// <remarks>See docs/tech/virtual-dsp-panel.md#opposite-side-hybrid-sum.</remarks>
    public AnalysisCurve? OppositeSum(
        VirtualCrossoverSideSum side, double offsetDb, MagnitudeGateSnapshot? snapshot = null)
    {
        bool oppositeRight = !session.ActiveSideRight;
        if (!CanDrawOppositeSum(oppositeRight))
        {
            return null;
        }

        // One anchor and offset for channels AND sum, as on the active side.
        snapshot ??= session.MagnitudeGate;
        double gateOffsetMs = snapshot.OppositeOffsetMs(side);
        GatedMagnitude sum = snapshot.OppositeSum(side, session.Calibration.For);
        var channelMagnitudes = new List<GatedMagnitude>(side.Channels.Count);
        foreach (ProcessedChannel item in side.Channels)
        {
            channelMagnitudes.Add(snapshot.Channel(
                item.ImpulseResponse,
                side.AnchorIndex,
                item.SampleRate,
                gateOffsetMs,
                item.MeasuredBand,
                session.Calibration.For(item)));
        }

        HybridMagnitudes? hybrid = Build(
            side.Channels,
            channelMagnitudes.Select(curve => curve.Display).ToList(),
            oppositeRight,
            snapshot.SmoothingInverseOctaves);
        if (hybrid == null)
        {
            return null;
        }

        List<IReadOnlyList<SignalPoint>> operands = channelMagnitudes
            .Select(curve => (IReadOnlyList<SignalPoint>)curve.Unsmoothed.Points)
            .ToList();
        // Raw, smoothed only at the end (see Sum).
        List<SignalPoint> loss = VirtualCrossoverAnalysis.SumLossCurve(
            sum.Unsmoothed.Points, operands);
        // The sides share one offset once they form a set; passed so the two cannot drift.
        List<SignalPoint>? points = Sum(
            hybrid with { OffsetDb = offsetDb },
            side.Channels,
            side.AnchorIndex,
            snapshot,
            gateOffsetMs,
            channelMagnitudes.Select(curve => (IReadOnlyList<SignalPoint>)curve.Display.Points)
                .ToList());
        return points == null ? null : new AnalysisCurve("Sum opposite", points);
    }

    /// <summary>Hybrid channels summed as phasors: each gated spectrum rescaled per bin to its spatial-average level.</summary>
    /// <remarks>An estimate: the phase is from one mic position. See docs/tech/spatial-average.md#hybrid-sum.</remarks>
    public static List<SignalPoint>? Sum(
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
                    level > loudest - DropoutFloorDb)
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

    public static IReadOnlyList<SignalPoint> ShiftedBy(
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
}
