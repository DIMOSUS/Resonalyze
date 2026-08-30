using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Computes the Virtual DSP summed-response read-out for a set of channels: the
/// per-channel magnitude spectra and complex sum, the per-junction and total
/// sum-loss entries, the final per-pair stereo Δ timing and the opposite side's
/// sum curve. UI-free — it reads the channel model and the processing
/// coordinator and returns data; the panel owns the read-out label and the plot.
/// Heavy processed-response work runs through the coordinator, sharing its cache
/// and stale-result guard with the main redraw.
/// </summary>
internal sealed class VirtualCrossoverMetrics
{
    private readonly VirtualCrossoverProcessingCoordinator coordinator;
    private readonly Func<Complex[], int, int, MeasuredBand, CalibrationFile?, GatedMagnitude>
        buildMagnitudeCurve;

    // How a curve's microphone correction is chosen. Passed in rather than read from
    // a field, because under the panel's "Own (as measured)" it is a property of each
    // MEASUREMENT and not of the project: one channel's file may name a calibration
    // its neighbour's does not, and a sum of two such channels names neither.
    private readonly Func<ProcessedChannel, CalibrationFile?> channelCalibration;

    // The SUM is not the gated total of the summed responses, though the arithmetic
    // says it is: one shared window makes the transform linear, so that total carries
    // every channel's window leakage into ranges that channel never measured. Built
    // by the panel, which owns the gate placement.
    private readonly Func<IReadOnlyList<ProcessedChannel>, int, GatedMagnitude>?
        buildSumCurve;

    public VirtualCrossoverMetrics(
        VirtualCrossoverProcessingCoordinator coordinator,
        Func<Complex[], int, int, MeasuredBand, CalibrationFile?, GatedMagnitude>
            buildMagnitudeCurve,
        Func<ProcessedChannel, CalibrationFile?>? channelCalibration = null,
        Func<IReadOnlyList<ProcessedChannel>, int, GatedMagnitude>? buildSumCurve = null)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.buildMagnitudeCurve = buildMagnitudeCurve
            ?? throw new ArgumentNullException(nameof(buildMagnitudeCurve));
        // No correction by default: a caller that does not draw for a user has no
        // microphone to correct for, and the metric is a comparison between curves
        // built the same way rather than an absolute reading.
        this.channelCalibration = channelCalibration ?? (_ => null);
        this.buildSumCurve = buildSumCurve;
    }

    // The magnitude curves, complex sum and summation loss the metric reads,
    // built the same way for the on-screen redraw and for a synchronous read
    // (e.g. the Auto delay log) so the two never disagree. The drawn magnitudes
    // carry the display smoothing; the loss is divided out of the UNSMOOTHED
    // pair and smoothed afterwards (see VirtualCrossoverAnalysis.SumLossCurve),
    // which is why the builder hands back both widths.
    // Fewer than two channels yield no METRIC — a sum of one channel is that
    // channel, and its summation loss is zero by definition. The per-channel
    // magnitudes are not a metric: they are what the plot draws, and one channel is
    // drawn like any other. Withholding them was what made the hybrid view stop
    // working when every channel but one was muted, in the panel and in the EQ
    // handoff alike, both of which gate on these being present.
    /// <param name="summed">
    /// The subset of <paramref name="processed"/> that enters the SUM, when the
    /// two differ — a grouped view draws a centre beside the front stage without
    /// adding it to anything. Null means every drawn channel sums, which is what
    /// a single-stage project always did. Both share one window anchor, taken
    /// from the drawn set: a sum anchored differently from the curves it is drawn
    /// over stops being their vector sum.
    /// </param>
    public (List<AnalysisCurve>? Magnitudes, AnalysisCurve? Sum, List<SignalPoint>? Loss)
        BuildCurves(
            List<ProcessedChannel> processed,
            int smoothingInverseOctaves,
            IReadOnlyList<ProcessedChannel>? summed = null)
    {
        if (processed.Count == 0)
        {
            return (null, null, null);
        }

        summed ??= processed;

        // Every curve — the channels AND the sum — shares one window anchor
        // (the earliest arrival): with per-channel anchors the gates capture
        // slightly different room content, the drawn Sum stops being the
        // vector sum of the drawn channels, and the loss can poke above its
        // 0 dB ceiling. The summed envelope peak can sit between the arrivals
        // or vanish under cancellation, so the anchor is the earliest arrival,
        // not the sum peak. (With the gate pinned in the dialog the offset is
        // absolute and shared by construction; the anchor is the
        // Auto-placement fallback. The magnitude always reads the FIXED gate —
        // FDW would need per-channel windows here, exactly what the shared
        // window exists to prevent — so FDW shapes the phase view only.)
        // The arrival is each channel's estimated START, not its peak: a
        // crossover's group delay puts the peak behind the front, and the
        // window has to open ahead of every channel's front, not of its
        // loudest moment (see ProcessedChannels.StartAnchorIndex). The phase
        // view's Auto placement and the junction gate the Auto delay search
        // places both read the front too, so the three no longer differ in
        // RULE — only in span, which is what each of them is for.
        int anchor = ProcessedChannels.SharedStartAnchorIndex(processed);
        // One gated build per channel, resampled at both widths; the panel's
        // magnitude builder reads only its immutable UI-thread snapshots and the
        // calibration, so the channels' spectra compute across cores. AsOrdered
        // keeps the result aligned with the channel list.
        List<GatedMagnitude> magnitudes = processed
            .AsParallel()
            .AsOrdered()
            .Select(item => buildMagnitudeCurve(
                item.ImpulseResponse,
                anchor,
                item.SampleRate,
                item.MeasuredBand,
                channelCalibration(item)))
            .ToList();
        // The METRIC needs two summing channels; the drawn curves do not, and a
        // view showing one driver beside an unsummed centre still draws both.
        List<int> summedIndices = [.. Enumerable.Range(0, processed.Count)
            .Where(index => summed.Contains(processed[index]))];
        if (summedIndices.Count < 2)
        {
            return (magnitudes.Select(curve => curve.Display).ToList(), null, null);
        }

        List<ProcessedChannel> summedChannels =
            [.. summedIndices.Select(index => processed[index])];

        // Each channel contributing only where it measured, then added as phasors,
        // each through its OWN microphone correction. Without a builder — a caller
        // that only wants the metric arithmetic — the old total stands: the sum plays
        // wherever ANY of its channels does, so its band is the UNION of theirs, and
        // the per-frequency mask covers a hole between two channels whose sweeps do
        // not overlap. That fallback sums impulse responses in the time domain, so it
        // has nowhere to put a per-channel correction and takes none; it is not a
        // path the panel uses.
        GatedMagnitude sumCurve = buildSumCurve?.Invoke(summedChannels, anchor)
            ?? buildMagnitudeCurve(
                VirtualCrossoverAnalysis.SumImpulseResponses(
                    summedChannels.Select(item => item.ImpulseResponse).ToList()),
                anchor,
                summedChannels[0].SampleRate,
                ProcessedChannels.UnionOfMeasuredBands(summedChannels),
                null)
                .MeasuredBySomeChannel(summedChannels);
        // The loss divides the complex sum by the incoherent sum of the very
        // channels that built it — a drawn-but-unsummed curve in the denominator
        // would report a cancellation that the sum never suffered.
        List<IReadOnlyList<SignalPoint>> operands = summedIndices
            .Select(index => (IReadOnlyList<SignalPoint>)magnitudes[index].Unsmoothed.Points)
            .ToList();
        List<SignalPoint> loss = VirtualCrossoverAnalysis.SumLossCurve(
            sumCurve.Unsmoothed.Points, operands, smoothingInverseOctaves);
        return (
            magnitudes.Select(curve => curve.Display).ToList(),
            sumCurve.Display,
            loss);
    }

    // Builds the sum-loss read-outs for a processed set without touching any
    // control, so they can feed the label, its tooltip, and the Auto delay log
    // from one computation. Reads the very curve the plot draws, so the label
    // and the trace can never quote different numbers. Empty when there is no
    // metric (fewer than two channels).
    public List<VirtualCrossoverMetric.Entry> BuildEntries(
        List<ProcessedChannel> processed,
        List<SignalPoint>? lossCurve)
    {
        var entries = new List<VirtualCrossoverMetric.Entry>();
        if (lossCurve == null)
        {
            return entries;
        }

        // Per-junction read-outs first, so an improvement at one crossover is
        // not averaged away by the other. Each junction reads the full sum
        // inside its own pair band; the out-of-pair channels are filtered so
        // far down there that their contribution is negligible.
        foreach (AdjacentPair pair in ProcessedChannels.GetAdjacentPairs(
            ProcessedChannels.OrderByBand(processed)))
        {
            double? pairLoss = VirtualCrossoverAnalysis.AverageSumLossDb(
                lossCurve, pair.BandLowHz, pair.BandHighHz);
            double? pairDip = VirtualCrossoverAnalysis.MinimumSumLossDb(
                lossCurve, pair.BandLowHz, pair.BandHighHz);
            if (pairLoss.HasValue)
            {
                entries.Add(new VirtualCrossoverMetric.Entry(
                    $"{pair.Lower.Channel.Name}/" +
                    $"{pair.Upper.Channel.Name}",
                    pairLoss.Value,
                    pairDip,
                    pair.BandLowHz,
                    pair.BandHighHz,
                    IsTotal: false));
            }
        }

        // A total only where the set IS one chain. With a hole in it — the
        // reference car's two subwoofers and then a rear fill from 290 Hz — the
        // per-junction rows above are still real and worth reading, but a single
        // figure over the whole window would average them with a span only one
        // member plays in, and present that as the chain's summation loss.
        if (!ProcessedChannels.IsContinuousChain(processed))
        {
            return entries;
        }

        (double minHz, double maxHz) = ProcessedChannels.GetCrossoverWindow(processed);
        double? loss = VirtualCrossoverAnalysis.AverageSumLossDb(
            lossCurve, minHz, maxHz);
        double? dip = VirtualCrossoverAnalysis.MinimumSumLossDb(
            lossCurve, minHz, maxHz);
        if (loss.HasValue)
        {
            entries.Add(new VirtualCrossoverMetric.Entry(
                "total", loss.Value, dip, minHz, maxHz, IsTotal: true));
        }

        return entries;
    }

    /// <summary>
    /// The per-junction phase read-outs: each adjacent pair's steady-state
    /// cross-phase analysis (the phase score, the phase at the crossover, the
    /// score-maximizing extra delay and polarity on the lower channel, and the
    /// lobe margin). Purely informative — nothing here feeds the alignment
    /// engine. One analysis spectrum is built per channel and shared by the
    /// junctions it participates in. Empty when there is no junction to read.
    /// </summary>
    public List<VirtualCrossoverMetric.PhaseEntry> BuildPhaseEntries(
        List<ProcessedChannel> processed)
    {
        var entries = new List<VirtualCrossoverMetric.PhaseEntry>();
        if (processed.Count < 2)
        {
            return entries;
        }

        var spectra = new Dictionary<ProcessedChannel, Complex[]>();
        Complex[] SpectrumOf(ProcessedChannel item)
        {
            if (!spectra.TryGetValue(item, out Complex[]? spectrum))
            {
                spectrum = JunctionPhaseAlignment.BuildAnalysisSpectrum(
                    item.ImpulseResponse, item.SampleRate);
                spectra.Add(item, spectrum);
            }

            return spectrum;
        }

        foreach (AdjacentPair pair in ProcessedChannels.GetAdjacentPairs(
            ProcessedChannels.OrderByBand(processed)))
        {
            if (pair.Lower.SampleRate != pair.Upper.SampleRate)
            {
                continue;
            }

            JunctionPhaseResult? result = JunctionPhaseAlignment.AnalyzeSpectra(
                SpectrumOf(pair.Lower),
                SpectrumOf(pair.Upper),
                pair.Lower.SampleRate,
                pair.CrossoverHz,
                pair.BandLowHz,
                pair.BandHighHz);
            if (result != null)
            {
                entries.Add(new VirtualCrossoverMetric.PhaseEntry(
                    $"{pair.Lower.Channel.Name}/{pair.Upper.Channel.Name}",
                    pair.Lower.Channel.Name,
                    pair.CrossoverHz,
                    pair.BandLowHz,
                    pair.BandHighHz,
                    result));
            }
        }

        return entries;
    }

    /// <summary>
    /// The final per-pair L−R timing: both sides' fully processed responses
    /// (current delays included) get their band-limited envelope arrival read
    /// in the pair's shared band, and the difference (positive: right leads —
    /// the scene-offset convention) feeds the metric read-out. A mono channel
    /// (the shared sub) has one response, so it reports that single arrival in
    /// its own band with "—" for the right side and the delta; a stereo pair
    /// needs both sides present and unbypassed.
    /// </summary>
    /// <summary>
    /// Each compared group against the front stage, on the ALREADY PROCESSED
    /// responses this frame is drawing: their summed impulse responses give one
    /// arrival and one level per group, and the difference is what the read-out
    /// quotes where a summation loss would be meaningless.
    /// </summary>
    /// <remarks>
    /// Cheaper than the stereo block by construction — nothing has to be
    /// re-rendered, because a group's response is the sum of channels this frame
    /// already computed. The arrival analysis still costs FFTs, so it runs on the
    /// coordinator's auxiliary path and drops silently when superseded, exactly
    /// as the stereo deltas do.
    /// </remarks>
    public async Task<IReadOnlyList<VirtualCrossoverMetric.GroupDelta>> ComputeGroupDeltasAsync(
        IReadOnlyList<ProcessedChannel> shown,
        VirtualCrossoverGroupView view,
        long revision)
    {
        IReadOnlyList<VirtualCrossoverZone> compared =
            VirtualCrossoverGroupViews.ComparedAgainstFront(view);
        if (compared.Count == 0)
        {
            return [];
        }

        List<ProcessedChannel> front = ZoneMembers(shown, VirtualCrossoverZone.Front);
        if (front.Count == 0)
        {
            // Nothing to compare against. A rear-only project is a legitimate
            // thing to look at; it just has no front stage to be late relative to.
            return [];
        }

        var jobs = new List<(VirtualCrossoverZone Zone, List<ProcessedChannel> Members)>();
        foreach (VirtualCrossoverZone zone in compared)
        {
            List<ProcessedChannel> members = ZoneMembers(shown, zone);
            if (members.Count > 0)
            {
                jobs.Add((zone, members));
            }
        }

        if (jobs.Count == 0)
        {
            return [];
        }

        (double frontLow, double frontHigh) = GroupBand(front);
        int sampleRate = front[0].SampleRate;
        List<VirtualCrossoverMetric.GroupDelta>? deltas =
            await coordinator.RunAuxiliaryAsync(revision, cancellationToken =>
            {
                Complex[] frontIr = VirtualCrossoverAnalysis.SumImpulseResponses(
                    [.. front.Select(item => item.ImpulseResponse)]);
                var results = new List<VirtualCrossoverMetric.GroupDelta>();
                foreach ((VirtualCrossoverZone zone, List<ProcessedChannel> members) in jobs)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return null;
                    }

                    (double zoneLow, double zoneHigh) = GroupBand(members);
                    double lowHz = Math.Max(frontLow, zoneLow);
                    double highHz = Math.Min(frontHigh, zoneHigh);
                    if (highHz < lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
                    {
                        // Too little overlap to time anything — a rear pair crossed
                        // entirely above the front stage would land here. Reported as
                        // an unmeasurable row rather than dropped, so the group does
                        // not silently vanish from the read-out.
                        results.Add(new VirtualCrossoverMetric.GroupDelta(
                            zone, null, null, lowHz, highHz));
                        continue;
                    }

                    Complex[] zoneIr = VirtualCrossoverAnalysis.SumImpulseResponses(
                        [.. members.Select(item => item.ImpulseResponse)]);
                    TimeAlignmentAnalysisResult frontArrival =
                        VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                            frontIr, sampleRate, lowHz, highHz);
                    TimeAlignmentAnalysisResult zoneArrival =
                        VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                            zoneIr, sampleRate, lowHz, highHz);
                    bool timed = Reliable(frontArrival) && Reliable(zoneArrival);
                    results.Add(new VirtualCrossoverMetric.GroupDelta(
                        zone,
                        timed
                            ? zoneArrival.FirstArrivalDelayMilliseconds -
                                frontArrival.FirstArrivalDelayMilliseconds
                            : null,
                        VirtualCrossoverAnalysis.MeasureBandLevelDb(
                            zoneIr, sampleRate, lowHz, highHz) -
                            VirtualCrossoverAnalysis.MeasureBandLevelDb(
                                frontIr, sampleRate, lowHz, highHz),
                        lowHz,
                        highHz));
                }

                return results;
            });
        return deltas ?? [];

        static bool Reliable(TimeAlignmentAnalysisResult arrival) =>
            arrival.IsValid &&
            arrival.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb;
    }

    private static List<ProcessedChannel> ZoneMembers(
        IReadOnlyList<ProcessedChannel> shown,
        VirtualCrossoverZone zone) =>
        [.. shown.Where(item => item.Channel.Pair.Zone == zone)];

    // The span a group actually plays: the union of its members' crossover bands.
    // The subwoofers are not in it — they belong to whichever stage is on screen,
    // and dragging a group's low edge down to 20 Hz would hand the comparison a
    // band where only one side plays.
    private static (double LowHz, double HighHz) GroupBand(
        IReadOnlyList<ProcessedChannel> members)
    {
        double low = double.MaxValue;
        double high = double.MinValue;
        foreach (ProcessedChannel member in members)
        {
            (double memberLow, double memberHigh) =
                VirtualCrossoverJunctions.GetChannelBand(member.Channel.Settings);
            low = Math.Min(low, memberLow);
            high = Math.Max(high, memberHigh);
        }

        return (low, high);
    }

    public async Task<List<VirtualCrossoverMetric.StereoDelta>> ComputeStereoDeltasAsync(
        IReadOnlyList<VirtualCrossoverChannel> channels,
        long revision)
    {
        var jobs = new List<StereoDeltaJob>();
        int nextId = 0;
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            VirtualCrossoverChannel channel = channels[channelIndex];
            bool mono = channel.Pair.Mono;

            // Mute and Bypass belong to the block, so they answer for both sides at
            // once; only the measurements are per side.
            if (!channel.Pair.Enabled || channel.Pair.Bypass)
            {
                continue;
            }

            VirtualCrossoverChannelSettings leftSettings = channel.SideSettings(false);
            VirtualCrossoverChannelState leftState = channel.PhysicalSideState(false);
            if (leftState.ProcessingSource is not { } leftSource)
            {
                continue;
            }

            VirtualCrossoverChannelSettings rightSettings = channel.SideSettings(true);
            VirtualCrossoverChannelState rightState = channel.PhysicalSideState(true);
            if (!mono && rightState.ProcessingSource is not { })
            {
                continue;
            }

            (double leftLow, double leftHigh) =
                VirtualCrossoverJunctions.GetChannelBand(leftSettings);
            double lowHz, highHz;
            if (mono)
            {
                lowHz = leftLow;
                highHz = leftHigh;
            }
            else
            {
                (double rightLow, double rightHigh) =
                    VirtualCrossoverJunctions.GetChannelBand(rightSettings);
                lowHz = Math.Max(leftLow, rightLow);
                highHz = Math.Min(leftHigh, rightHigh);
            }
            if (highHz < lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
            {
                continue;
            }

            SideProcessJob Snapshot(
                VirtualCrossoverChannelState state,
                VirtualCrossoverChannelSettings settings,
                VirtualCrossoverSourceSnapshot source) =>
                new()
                {
                    Id = nextId++,
                    SlotId = new ProcessingSlotId(
                        channelIndex,
                        !channel.Pair.Mono && ReferenceEquals(
                            state,
                            channel.PhysicalSideState(true))),
                    State = state,
                    Source = source,
                    SampleRate = state.SampleRate,
                    ProcessorSampleRate = channel.ProcessorSampleRate,
                    Chain = settings.ToChain(),
                    Channel = channel
                };

            SideProcessJob leftJob = Snapshot(leftState, leftSettings, leftSource);
            SideProcessJob rightJob = mono
                ? leftJob
                : Snapshot(
                    rightState,
                    rightSettings,
                    rightState.ProcessingSource!);
            jobs.Add(new StereoDeltaJob(
                channel.Name,
                lowHz,
                highHz,
                leftJob,
                rightJob,
                mono));
        }

        List<SideProcessJob> sides = jobs.SelectMany(job => job.Sides).ToList();
        if (sides.Count > 0)
        {
            VirtualCrossoverRenderResult? render = await coordinator.ProcessAsync(
                new VirtualCrossoverProcessingSnapshot(
                    revision,
                    sides.Select(side => new VirtualCrossoverChannelSnapshot(
                        side.Id,
                        side.SlotId,
                        side.Source,
                        side.SampleRate,
                        side.ProcessorSampleRate,
                        side.Chain))));
            if (render == null)
            {
                return [];
            }

            Dictionary<int, SideProcessJob> byId = sides.ToDictionary(side => side.Id);
            foreach (VirtualCrossoverProcessedChannel processed in render.Channels)
            {
                SideProcessJob side = byId[processed.Id];
                side.ProcessedIr = processed.ImpulseResponse;
                side.ProcessedPeak = processed.PeakIndex;
                side.ProcessedValidRange = processed.ValidRange;
            }
        }

        foreach (StereoDeltaJob job in jobs)
        {
            foreach (SideProcessJob side in job.Sides)
            {
                if (side.State.ArrivalCache is { } arrival &&
                    ReferenceEquals(arrival.ProcessedIr, side.ProcessedIr) &&
                    arrival.LowHz == job.LowHz && arrival.HighHz == job.HighHz)
                {
                    side.Arrival = arrival.Result;
                    side.LevelDb = arrival.LevelDb;
                    side.Latched = arrival.Latched;
                    side.ArrivalFromCache = true;
                }
            }
        }

        bool anyArrivalWork = jobs.Any(job => job.Sides.Any(side => side.Arrival == null));
        if (anyArrivalWork)
        {
            object? arrivalCompleted = await coordinator.RunAuxiliaryAsync<object>(
                revision,
                cancellationToken =>
            {
                foreach (StereoDeltaJob job in jobs)
                {
                    foreach (SideProcessJob side in job.Sides)
                    {
                        // Silent cancellation (see RunAuxiliaryAsync): null
                        // says "superseded", and the caller drops the read-out
                        // exactly as it did for the thrown version.
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return null;
                        }

                        if (side.Arrival == null)
                        {
                            side.Arrival =
                                VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                                    side.ProcessedIr!, side.SampleRate,
                                    job.LowHz, job.HighHz,
                                    side.ProcessedValidRange);
                            side.LevelDb = VirtualCrossoverAnalysis.MeasureBandLevelDb(
                                side.ProcessedIr!, side.SampleRate,
                                job.LowHz, job.HighHz);
                            side.Latched = IsModalLatched(
                                side, job.LowHz, job.HighHz, side.Arrival.Value);
                        }
                    }
                }
                return new object();
            });
            if (arrivalCompleted == null)
            {
                return [];
            }

            foreach (StereoDeltaJob job in jobs)
            {
                foreach (SideProcessJob side in job.Sides)
                {
                    if (!side.ArrivalFromCache)
                    {
                        side.State.ArrivalCache =
                            (side.ProcessedIr!, job.LowHz, job.HighHz,
                                side.Arrival!.Value, side.LevelDb, side.Latched);
                    }
                }
            }
        }

        static bool Reliable(TimeAlignmentAnalysisResult arrival) =>
            arrival.IsValid &&
            arrival.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb;

        return jobs
            .Select(job =>
            {
                TimeAlignmentAnalysisResult left = job.Left.Arrival!.Value;
                bool leftReliable = Reliable(left);
                double? leftMs = leftReliable
                    ? left.FirstArrivalDelayMilliseconds
                    : null;
                if (job.Mono)
                {
                    return new VirtualCrossoverMetric.StereoDelta(
                        job.Channel, leftMs, null, job.LowHz, job.HighHz, null,
                        LeftLatched: job.Left.Latched);
                }

                TimeAlignmentAnalysisResult right = job.Right.Arrival!.Value;
                bool rightReliable = Reliable(right);
                return new VirtualCrossoverMetric.StereoDelta(
                    job.Channel,
                    leftMs,
                    rightReliable ? right.FirstArrivalDelayMilliseconds : null,
                    job.LowHz,
                    job.HighHz,
                    leftReliable && rightReliable &&
                    job.Left.LevelDb is { } leftLevel &&
                    job.Right.LevelDb is { } rightLevel
                        ? leftLevel - rightLevel
                        : null,
                    LeftLatched: job.Left.Latched,
                    RightLatched: job.Right.Latched);
            })
            .ToList();
    }

    // The alignment engine's modal-latch detection, applied to one side's
    // read-out arrival: the SAME response measured in the band's upper half
    // (from the geometric-mean frequency up) must agree with the full-band
    // read to within the dispersion one direct wave packet can show — half a
    // period at the probe's low edge. A full-band read landing far BEHIND its
    // own upper-half read means the envelope latched onto the in-room modal
    // build-up instead of the direct rise, and the row's L/R difference then
    // compares different features. The probe only VOTES on the full band's
    // honesty; its own number is never a substitute.
    private static bool IsModalLatched(
        SideProcessJob side,
        double lowHz,
        double highHz,
        TimeAlignmentAnalysisResult fullBand)
    {
        if (!fullBand.IsValid ||
            fullBand.SignalToNoiseDecibels < AutoAlignmentEngine.MinimumArrivalSnrDb)
        {
            return false;
        }

        double probeLowHz = Math.Sqrt(lowHz * highHz);
        if (highHz < probeLowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
        {
            return false;
        }

        TimeAlignmentAnalysisResult probe =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                side.ProcessedIr!, side.SampleRate, probeLowHz, highHz,
                side.ProcessedValidRange);
        if (!probe.IsValid ||
            probe.SignalToNoiseDecibels < AutoAlignmentEngine.MinimumArrivalSnrDb)
        {
            return false;
        }

        double toleranceMs = Math.Max(1.0, 500.0 / probeLowHz);
        return fullBand.FirstArrivalDelayMilliseconds
            - probe.FirstArrivalDelayMilliseconds > toleranceMs;
    }

    /// <summary>
    /// The complex sum of one side's participating channels, processed through
    /// their chains. Mono channels contribute their single response to both
    /// sides, exactly as they do physically. Null when the side has fewer than
    /// <paramref name="minimumChannels"/> participating channels, or when the
    /// render went stale. Uses the coordinator cache, so it shares processed
    /// responses and staleness handling with the main redraw.
    /// </summary>
    /// <param name="includePair">
    /// Which blocks the sum is of, or null for all of them. The grouped views use
    /// it so the opposite side's sum is the SAME part of the installation as the
    /// one on screen — comparing a front stage against the other side's whole
    /// system would read as an L/R difference that is really a scope difference.
    /// </param>
    public async Task<VirtualCrossoverSideSum?> ComputeSideSumAsync(
        IReadOnlyList<VirtualCrossoverChannel> channels,
        bool rightSide,
        long revision,
        int minimumChannels,
        Func<VirtualCrossoverChannelPairSettings, bool>? includePair = null)
    {
        var jobs = new List<SideProcessJob>();
        int nextId = 0;
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            VirtualCrossoverChannel channel = channels[channelIndex];
            VirtualCrossoverChannelSettings settings =
                channel.SideSettings(rightSide);
            VirtualCrossoverChannelState state = channel.SideState(rightSide);
            if (!channel.Pair.Enabled ||
                includePair?.Invoke(channel.Pair) == false ||
                state.ProcessingSource is not { } source)
            {
                continue;
            }

            DspChannelChain chain = channel.Pair.Bypass
                ? DspChannelChain.Identity
                : settings.ToChain();
            jobs.Add(new SideProcessJob
            {
                Id = nextId++,
                SlotId = new ProcessingSlotId(
                    channelIndex,
                    !channel.Pair.Mono && rightSide),
                State = state,
                Source = source,
                SampleRate = state.SampleRate,
                ProcessorSampleRate = channel.ProcessorSampleRate,
                Chain = chain,
                Channel = channel
            });
        }

        if (jobs.Count < minimumChannels)
        {
            return null;
        }

        VirtualCrossoverRenderResult? render = await coordinator.ProcessAsync(
            new VirtualCrossoverProcessingSnapshot(
                revision,
                jobs.Select(side => new VirtualCrossoverChannelSnapshot(
                    side.Id,
                    side.SlotId,
                    side.Source,
                    side.SampleRate,
                    side.ProcessorSampleRate,
                    side.Chain))));
        if (render == null)
        {
            return null;
        }

        Dictionary<int, SideProcessJob> byId = jobs.ToDictionary(side => side.Id);
        foreach (VirtualCrossoverProcessedChannel processed in render.Channels)
        {
            SideProcessJob side = byId[processed.Id];
            side.ProcessedIr = processed.ImpulseResponse;
            side.ProcessedPeak = processed.PeakIndex;
            side.ProcessedValidRange = processed.ValidRange;
        }

        Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses(
            jobs.Select(side => side.ProcessedIr!).ToList());
        return new VirtualCrossoverSideSum(
            sum,
            // The same placement rule as the shown side's (see BuildCurves):
            // the earliest FRONT of the channels that went into this sum.
            jobs.Min(side => ProcessedChannels.StartAnchorIndex(
                side.ProcessedIr!, side.ProcessedPeak, side.SampleRate,
                side.ProcessedValidRange)),
            jobs[0].SampleRate,
            // The parts the sum was made of, so a caller that has to rebuild it
            // differently — the hybrid sum adds magnitudes, not vectors — is not
            // left with only the finished total. The colour is not this method's to
            // know: it belongs to the channel's slot in the panel, and nothing that
            // reads a side sum draws these curves in their own right.
            jobs.Select(side => new ProcessedChannel(
                side.Channel,
                side.ProcessedIr!,
                side.ProcessedPeak,
                side.SampleRate,
                OxyColors.Transparent,
                side.ProcessedValidRange,
                side.State.MeasuredBand,
                side.State.MicrophoneCalibrationCurve)).ToList());
    }

    // One channel side snapshotted on the UI thread for background processing
    // (the stereo Δ read-out and the opposite-side sum): the background pass
    // reads nothing mutable. Processed responses come exclusively from the
    // coordinator cache; only the cheaper arrival analysis is cached per side.
    private sealed class SideProcessJob
    {
        public required int Id { get; init; }
        public required ProcessingSlotId SlotId { get; init; }
        public required VirtualCrossoverChannelState State { get; init; }
        public required VirtualCrossoverSourceSnapshot Source { get; init; }
        public required int SampleRate { get; init; }

        // Snapshotted like everything else here: the user may pick another
        // processor while a metric rebuild is in flight, and the rate the chain
        // was realized at has to be the one this job started with.
        public required int ProcessorSampleRate { get; init; }

        public required DspChannelChain Chain { get; init; }
        public required VirtualCrossoverChannel Channel { get; init; }
        public Complex[]? ProcessedIr { get; set; }
        public int ProcessedPeak { get; set; }
        public ValidSampleRange ProcessedValidRange { get; set; }
        public TimeAlignmentAnalysisResult? Arrival { get; set; }
        public double? LevelDb { get; set; }
        public bool Latched { get; set; }
        public bool ArrivalFromCache { get; set; }
    }

    private sealed record StereoDeltaJob(
        string Channel,
        double LowHz,
        double HighHz,
        SideProcessJob Left,
        SideProcessJob Right,
        bool Mono = false)
    {
        // A mono job's Left and Right are the same instance; iterate the left
        // slot alone so the shared response is processed once.
        public IEnumerable<SideProcessJob> Sides =>
            Mono ? new[] { Left } : new[] { Left, Right };
    }
}
