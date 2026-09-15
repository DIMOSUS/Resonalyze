using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>UI-free Virtual DSP read-outs: channel curves and sum, sum-loss entries, stereo and group deltas, opposite-side sum.
/// See docs/tech/virtual-dsp-analysis.md.</summary>
internal sealed class VirtualCrossoverMetrics
{
    private readonly VirtualCrossoverProcessingCoordinator coordinator;
    private readonly Func<Complex[], int, int, MeasuredBand, CalibrationFile?, GatedMagnitude>
        buildMagnitudeCurve;

    // Per channel: under "Own (as measured)" calibration belongs to each measurement, not the project.
    private readonly Func<ProcessedChannel, CalibrationFile?> channelCalibration;

    // Not the gated total of the summed IR: a shared window would carry every channel's leakage into ranges it never measured.
    private readonly Func<IReadOnlyList<ProcessedChannel>, int, GatedMagnitude>?
        buildSumCurve;

    // Last group-delta result, so view switches do not rerun arrival FFTs; UI thread only.
    // See docs/tech/virtual-dsp-analysis.md#group-delta-read-out.
    private (GroupDeltaKey Key, IReadOnlyList<VirtualCrossoverMetric.GroupDelta> Deltas)?
        groupDeltas;

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
        this.channelCalibration = channelCalibration ?? (_ => null);
        this.buildSumCurve = buildSumCurve;
    }

    // The loss is divided out of the UNSMOOTHED pair (see VirtualCrossoverAnalysis.SumLossCurve), hence both widths.
    // Per-channel magnitudes come back even for one channel; only the metric needs two.
    /// <param name="summed">Subset entering the SUM (null = all drawn). Both share one window anchor from the drawn set.</param>
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

        // One shared anchor at the earliest START for channels and sum, or the sum stops being their vector sum.
        // See docs/tech/virtual-dsp-analysis.md#magnitude-curves-and-the-shared-window.
        int anchor = ProcessedChannels.SharedStartAnchorIndex(processed);
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
        List<int> summedIndices = [.. Enumerable.Range(0, processed.Count)
            .Where(index => summed.Contains(processed[index]))];
        if (summedIndices.Count < 2)
        {
            return (magnitudes.Select(curve => curve.Display).ToList(), null, null);
        }

        List<ProcessedChannel> summedChannels =
            [.. summedIndices.Select(index => processed[index])];

        // Without a builder: time-domain sum over the union of bands, no per-channel calibration (not a panel path).
        GatedMagnitude sumCurve = buildSumCurve?.Invoke(summedChannels, anchor)
            ?? buildMagnitudeCurve(
                VirtualCrossoverAnalysis.SumImpulseResponses(
                    summedChannels.Select(item => item.ImpulseResponse).ToList()),
                anchor,
                summedChannels[0].SampleRate,
                ProcessedChannels.UnionOfMeasuredBands(summedChannels),
                null)
                .MeasuredBySomeChannel(summedChannels);
        // Denominator uses only summed channels, or a drawn-but-unsummed curve reports a fake cancellation.
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

    public List<VirtualCrossoverMetric.Entry> BuildEntries(
        List<ProcessedChannel> processed,
        List<SignalPoint>? lossCurve)
    {
        var entries = new List<VirtualCrossoverMetric.Entry>();
        if (lossCurve == null)
        {
            return entries;
        }

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

        // No total over a set with a hole in its chain. See docs/tech/virtual-dsp-analysis.md#measured-bands-and-junctions.
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

    /// <summary>Per-junction phase read-outs; informative only, nothing feeds the alignment engine.</summary>
    /// <param name="buildSpectra">Windowed spectra of the band-ordered set under the panel's gate; invoked off the UI thread.</param>
    public List<VirtualCrossoverMetric.PhaseEntry> BuildPhaseEntries(
        List<ProcessedChannel> processed,
        Func<IReadOnlyList<ProcessedChannel>, IReadOnlyList<Complex[]>> buildSpectra)
    {
        ArgumentNullException.ThrowIfNull(buildSpectra);
        var entries = new List<VirtualCrossoverMetric.PhaseEntry>();
        if (processed.Count < 2)
        {
            return entries;
        }

        List<ProcessedChannel> ordered = ProcessedChannels.OrderByBand(processed);
        IReadOnlyList<Complex[]> spectra = buildSpectra(ordered);
        if (spectra.Count != ordered.Count)
        {
            throw new InvalidOperationException(
                "The spectrum builder must answer one spectrum per channel.");
        }

        // By reference: equal-valued records would collide.
        Complex[] SpectrumOf(ProcessedChannel item)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ReferenceEquals(ordered[i], item))
                {
                    return spectra[i];
                }
            }

            throw new InvalidOperationException(
                "The junction names a channel outside the ordered set.");
        }

        foreach (AdjacentPair pair in ProcessedChannels.GetAdjacentPairs(ordered))
        {
            if (pair.Lower.SampleRate != pair.Upper.SampleRate)
            {
                continue;
            }

            JunctionPhaseResult? result = JunctionPhaseAlignment.AnalyzeWindowedSpectra(
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

    /// <summary>Direct-sound sum loss from per-channel 8-cycle windows rotated into one time frame (≤ 0 dB).
    /// Null for fewer than two channels or mixed rates. See docs/tech/virtual-dsp-analysis.md#sum-loss-window-full-vs-direct.</summary>
    public List<SignalPoint>? BuildDirectLossCurve(
        IReadOnlyList<ProcessedChannel> channels,
        IReadOnlyList<Complex[]> spectra,
        int smoothingInverseOctaves)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(spectra);
        if (channels.Count < 2 || spectra.Count != channels.Count)
        {
            return null;
        }

        int sampleRate = channels[0].SampleRate;
        if (sampleRate <= 0 || channels.Any(channel => channel.SampleRate != sampleRate))
        {
            return null;
        }

        var operands = new List<IReadOnlyList<SignalPoint>>(channels.Count);
        var bands = new List<(double LowestHz, double HighestHz)>(channels.Count);
        var calibrations = new List<CalibrationFile?>(channels.Count);
        for (int i = 0; i < channels.Count; i++)
        {
            MeasuredBand band = channels[i].MeasuredBand;
            CalibrationFile? calibration = channelCalibration(channels[i]);
            operands.Add(DataHelper.GetGatedMagnitude(
                spectra[i], sampleRate, band.LowEdgeHz, band.HighEdgeHz,
                calibration, smoothingInverseOctaves: 0).Points);
            bands.Add((band.LowEdgeHz, band.HighEdgeHz));
            calibrations.Add(calibration);
        }

        (_, AnalysisCurve sum) = DataHelper.GetGatedMeasuredMagnitudeSumPair(
            spectra, sampleRate, bands, calibrations, smoothingInverseOctaves: 0);
        return VirtualCrossoverAnalysis.SumLossCurve(
            ProcessedChannels.MeasuredBySomeChannel(sum.Points, channels),
            operands,
            smoothingInverseOctaves);
    }

    /// <summary>Each compared group against the front stage (arrival and level), from this frame's processed responses.</summary>
    /// <param name="hybridGroupLevelDeltaDb">Spatial-average level delta, invoked during synchronous assembly; part of the cache key.</param>
    public async Task<IReadOnlyList<VirtualCrossoverMetric.GroupDelta>> ComputeGroupDeltasAsync(
        IReadOnlyList<ProcessedChannel> shown,
        VirtualCrossoverGroupView view,
        long revision,
        Func<IReadOnlyList<ProcessedChannel>, IReadOnlyList<ProcessedChannel>,
            double, double, double?>? hybridGroupLevelDeltaDb = null)
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
            return [];
        }

        (double frontLow, double frontHigh) = GroupBand(front);
        var jobs = new List<GroupDeltaJob>();
        foreach (VirtualCrossoverZone zone in compared)
        {
            List<ProcessedChannel> members = ZoneMembers(shown, zone);
            if (members.Count > 0)
            {
                // Band is part of the cache key.
                (double zoneLow, double zoneHigh) = GroupBand(members);
                double lowHz = Math.Max(frontLow, zoneLow);
                double highHz = Math.Min(frontHigh, zoneHigh);
                jobs.Add(new GroupDeltaJob(
                    zone,
                    members,
                    lowHz,
                    highHz,
                    // UI-thread state: read here, not in the worker.
                    highHz >= lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio
                        ? hybridGroupLevelDeltaDb?.Invoke(members, front, lowHz, highHz)
                        : null));
            }
        }

        if (jobs.Count == 0)
        {
            return [];
        }

        int sampleRate = front[0].SampleRate;
        var key = new GroupDeltaKey(front, jobs, sampleRate);
        if (groupDeltas is { } cached && cached.Key.Matches(key))
        {
            return coordinator.IsCurrent(revision) ? cached.Deltas : [];
        }

        List<VirtualCrossoverMetric.GroupDelta>? deltas =
            await coordinator.RunAuxiliaryAsync(revision, cancellationToken =>
            {
                Complex[] frontIr = VirtualCrossoverAnalysis.SumImpulseResponses(
                    [.. front.Select(item => item.ImpulseResponse)]);
                var frontByBand =
                    new Dictionary<(double LowHz, double HighHz),
                        (TimeAlignmentAnalysisResult Arrival, double? LevelDb)>();
                var results = new List<VirtualCrossoverMetric.GroupDelta>();
                foreach (GroupDeltaJob job in jobs)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return null;
                    }

                    double lowHz = job.LowHz;
                    double highHz = job.HighHz;
                    if (highHz < lowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
                    {
                        // Reported as unmeasurable rather than dropped, so the group does not vanish.
                        results.Add(new VirtualCrossoverMetric.GroupDelta(
                            job.Zone, null, null, lowHz, highHz));
                        continue;
                    }

                    if (!frontByBand.TryGetValue(
                            (lowHz, highHz),
                            out (TimeAlignmentAnalysisResult Arrival, double? LevelDb) frontRead))
                    {
                        frontRead = (
                            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                                frontIr, sampleRate, lowHz, highHz),
                            VirtualCrossoverAnalysis.MeasureBandLevelDb(
                                frontIr, sampleRate, lowHz, highHz));
                        frontByBand[(lowHz, highHz)] = frontRead;
                    }

                    Complex[] zoneIr = VirtualCrossoverAnalysis.SumImpulseResponses(
                        [.. job.Members.Select(item => item.ImpulseResponse)]);
                    TimeAlignmentAnalysisResult zoneArrival =
                        VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                            zoneIr, sampleRate, lowHz, highHz);
                    bool timed = Reliable(frontRead.Arrival) && Reliable(zoneArrival);
                    results.Add(new VirtualCrossoverMetric.GroupDelta(
                        job.Zone,
                        timed
                            ? zoneArrival.FirstArrivalDelayMilliseconds -
                                frontRead.Arrival.FirstArrivalDelayMilliseconds
                            : null,
                        job.HybridLevelDeltaDb ??
                            VirtualCrossoverAnalysis.MeasureBandLevelDb(
                                zoneIr, sampleRate, lowHz, highHz) - frontRead.LevelDb,
                        lowHz,
                        highHz,
                        LevelFromSpatialAverage: job.HybridLevelDeltaDb.HasValue));
                }

                return results;
            });
        if (deltas == null)
        {
            // Superseded: cache nothing.
            return [];
        }

        // Read-only: this instance answers many frames.
        IReadOnlyList<VirtualCrossoverMetric.GroupDelta> remembered = deltas.AsReadOnly();
        groupDeltas = (key, remembered);
        return remembered;

        static bool Reliable(TimeAlignmentAnalysisResult arrival) =>
            arrival.IsValid &&
            arrival.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb;
    }

    private sealed record GroupDeltaJob(
        VirtualCrossoverZone Zone,
        List<ProcessedChannel> Members,
        double LowHz,
        double HighHz,
        double? HybridLevelDeltaDb = null);

    /// <summary>Inputs a group-delta set depends on. Responses compare by REFERENCE: the coordinator returns a new array
    /// exactly when anything feeding a channel changed.</summary>
    private sealed class GroupDeltaKey
    {
        private readonly Complex[][] front;
        private readonly JobKey[] jobs;
        private readonly int sampleRate;

        public GroupDeltaKey(
            IReadOnlyList<ProcessedChannel> front,
            IReadOnlyList<GroupDeltaJob> jobs,
            int sampleRate)
        {
            this.front = Responses(front);
            this.jobs =
            [
                .. jobs.Select(job => new JobKey(
                    job.Zone, Responses(job.Members), job.LowHz, job.HighHz,
                    job.HybridLevelDeltaDb))
            ];
            this.sampleRate = sampleRate;
        }

        public bool Matches(GroupDeltaKey other)
        {
            if (sampleRate != other.sampleRate ||
                jobs.Length != other.jobs.Length ||
                !SameResponses(front, other.front))
            {
                return false;
            }

            for (int i = 0; i < jobs.Length; i++)
            {
                // The hybrid level is part of the answer: responses stand still while it toggles.
                if (jobs[i].Zone != other.jobs[i].Zone ||
                    jobs[i].LowHz != other.jobs[i].LowHz ||
                    jobs[i].HighHz != other.jobs[i].HighHz ||
                    !Nullable.Equals(
                        jobs[i].HybridLevelDeltaDb,
                        other.jobs[i].HybridLevelDeltaDb) ||
                    !SameResponses(jobs[i].Members, other.jobs[i].Members))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed record JobKey(
            VirtualCrossoverZone Zone,
            Complex[][] Members,
            double LowHz,
            double HighHz,
            double? HybridLevelDeltaDb);

        private static Complex[][] Responses(IReadOnlyList<ProcessedChannel> members) =>
            [.. members.Select(item => item.ImpulseResponse)];

        private static bool SameResponses(Complex[][] left, Complex[][] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (!ReferenceEquals(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static List<ProcessedChannel> ZoneMembers(
        IReadOnlyList<ProcessedChannel> shown,
        VirtualCrossoverZone zone) =>
        [.. shown.Where(item => item.Channel.Pair.Zone == zone)];

    // Union of members' crossover bands, excluding subwoofers (they belong to whichever stage is shown).
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

    /// <summary>Final per-pair L−R arrival delta (positive: right leads) and level delta. Instrument and modal-latch probe follow
    /// the engine's cross-side link rule. See docs/tech/virtual-dsp-analysis.md#stereo-delta-read-out.</summary>
    /// <param name="channels">EVERY project channel: list position is the coordinator cache slot. Narrow with <paramref name="includePair"/>.</param>
    public async Task<List<VirtualCrossoverMetric.StereoDelta>> ComputeStereoDeltasAsync(
        IReadOnlyList<VirtualCrossoverChannel> channels,
        long revision,
        Func<VirtualCrossoverChannelPairSettings, bool>? includePair = null,
        Func<VirtualCrossoverChannel, double, double, double?>? hybridLevelDeltaDb = null)
    {
        static bool Reliable(TimeAlignmentAnalysisResult arrival) =>
            arrival.IsValid &&
            arrival.SignalToNoiseDecibels >= AutoAlignmentEngine.MinimumArrivalSnrDb;

        var jobs = new List<StereoDeltaJob>();
        int nextId = 0;
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            VirtualCrossoverChannel channel = channels[channelIndex];
            bool mono = channel.Pair.Mono;

            if (!channel.Pair.Enabled || channel.Pair.Bypass ||
                includePair?.Invoke(channel.Pair) == false)
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
                    Chain = settings.ToChain(channel.Pair.Zone),
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
                mono,
                // UI-thread state: read here, never in the auxiliary pass.
                mono ? null : hybridLevelDeltaDb?.Invoke(channel, lowHz, highHz)));
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
                    side.Probe = arrival.Probe;
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
                            // Weak full reads get no probe; the cached probe drops its envelope.
                            side.Probe = Reliable(side.Arrival.Value)
                                ? ReadUpperHalf(side, job.LowHz, job.HighHz)
                                    is { } probe
                                    ? probe with { EnvelopeSamples = [] }
                                    : null
                                : null;
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
                                side.Arrival!.Value, side.LevelDb, side.Probe);
                    }
                }
            }
        }

        return jobs
            .Select(job =>
            {
                TimeAlignmentAnalysisResult left = job.Left.Arrival!.Value;
                bool leftReliable = Reliable(left);
                if (job.Mono)
                {
                    return new VirtualCrossoverMetric.StereoDelta(
                        job.Channel,
                        leftReliable ? left.FirstArrivalDelayMilliseconds : null,
                        null, job.LowHz, job.HighHz, null,
                        LeftLatched: IsModalLatched(
                            left, job.Left.Probe, job.LowHz, job.HighHz,
                            energyOnset: false));
                }

                TimeAlignmentAnalysisResult right = job.Right.Arrival!.Value;
                bool rightReliable = Reliable(right);
                // One instrument for both sides and their probes, so a delta never subtracts a peak from an onset.
                bool energyOnset = left.IsValid && right.IsValid &&
                    AutoAlignmentEngine.LinkReadsEnergyOnset(
                        job.LowHz, job.HighHz,
                        left.SignalToNoiseDecibels, right.SignalToNoiseDecibels);
                static double Arrival(TimeAlignmentAnalysisResult read, bool onset) =>
                    (onset ? AutoAlignmentEngine.AsEnergyOnset(read) : read)
                        .FirstArrivalDelayMilliseconds;
                // A spatial-average level outranks the point measurement and its reliability gate.
                double? levelDelta = job.HybridLevelDeltaDb ??
                    (leftReliable && rightReliable &&
                    job.Left.LevelDb is { } leftLevel &&
                    job.Right.LevelDb is { } rightLevel
                        ? leftLevel - rightLevel
                        : null);
                return new VirtualCrossoverMetric.StereoDelta(
                    job.Channel,
                    leftReliable ? Arrival(left, energyOnset) : null,
                    rightReliable ? Arrival(right, energyOnset) : null,
                    job.LowHz,
                    job.HighHz,
                    levelDelta,
                    LeftLatched: IsModalLatched(
                        left, job.Left.Probe, job.LowHz, job.HighHz, energyOnset),
                    RightLatched: IsModalLatched(
                        right, job.Right.Probe, job.LowHz, job.HighHz, energyOnset),
                    LevelFromSpatialAverage: job.HybridLevelDeltaDb.HasValue,
                    EnergyOnset: energyOnset,
                    EnergyOnsetWithheld: !energyOnset && leftReliable && rightReliable &&
                        AutoAlignmentEngine.LinkBandReadsEnergyOnset(job.LowHz, job.HighHz));
            })
            .ToList();
    }

    private static TimeAlignmentAnalysisResult? ReadUpperHalf(
        SideProcessJob side,
        double lowHz,
        double highHz)
    {
        double probeLowHz = Math.Sqrt(lowHz * highHz);
        if (highHz < probeLowHz * VirtualCrossoverAnalysis.MinimumArrivalBandRatio)
        {
            return null;
        }

        return VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
            side.ProcessedIr!, side.SampleRate, probeLowHz, highHz,
            side.ProcessedValidRange);
    }

    // Full-band read far behind its upper-half probe (beyond half a low-edge period, min 1 ms) latched onto modal build-up.
    // The probe only votes; its number never substitutes.
    private static bool IsModalLatched(
        TimeAlignmentAnalysisResult fullBand,
        TimeAlignmentAnalysisResult? probe,
        double lowHz,
        double highHz,
        bool energyOnset)
    {
        if (probe is not { } upperHalf)
        {
            return false;
        }

        double probeLowHz = Math.Sqrt(lowHz * highHz);
        double toleranceMs = Math.Max(1.0, 500.0 / probeLowHz);
        return AutoAlignmentEngine.ClassifyLinkArrival(
            energyOnset ? AutoAlignmentEngine.AsEnergyOnset(fullBand) : fullBand,
            energyOnset ? AutoAlignmentEngine.AsEnergyOnset(upperHalf) : upperHalf,
            toleranceMs,
            energyOnset) == AutoAlignmentEngine.ArrivalCertificate.Latched;
    }

    /// <summary>Complex sum of one side's participating channels (mono channels feed both sides); null when too few or stale.</summary>
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
                : settings.ToChain(channel.Pair.Zone);
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
            jobs.Min(side => ProcessedChannels.StartAnchorIndex(
                side.ProcessedIr!, side.ProcessedPeak, side.SampleRate,
                side.ProcessedValidRange)),
            jobs[0].SampleRate,
            // Parts kept so the hybrid sum (magnitudes, not vectors) can rebuild it.
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

    // UI-thread snapshot of one channel side for background processing.
    private sealed class SideProcessJob
    {
        public required int Id { get; init; }
        public required ProcessingSlotId SlotId { get; init; }
        public required VirtualCrossoverChannelState State { get; init; }
        public required VirtualCrossoverSourceSnapshot Source { get; init; }
        public required int SampleRate { get; init; }

        // Snapshotted: the processor may change while a rebuild is in flight.
        public required int ProcessorSampleRate { get; init; }

        public required DspChannelChain Chain { get; init; }
        public required VirtualCrossoverChannel Channel { get; init; }
        public Complex[]? ProcessedIr { get; set; }
        public int ProcessedPeak { get; set; }
        public ValidSampleRange ProcessedValidRange { get; set; }
        public TimeAlignmentAnalysisResult? Arrival { get; set; }
        public double? LevelDb { get; set; }
        public TimeAlignmentAnalysisResult? Probe { get; set; }
        public bool ArrivalFromCache { get; set; }
    }

    private sealed record StereoDeltaJob(
        string Channel,
        double LowHz,
        double HighHz,
        SideProcessJob Left,
        SideProcessJob Right,
        bool Mono = false,
        double? HybridLevelDeltaDb = null)
    {
        // Mono: Left and Right are one instance; process it once.
        public IEnumerable<SideProcessJob> Sides =>
            Mono ? new[] { Left } : new[] { Left, Right };
    }
}
