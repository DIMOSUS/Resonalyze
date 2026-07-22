using System.Numerics;
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
    private readonly Func<Complex[], int, int, AnalysisCurve> buildMagnitudeCurve;

    public VirtualCrossoverMetrics(
        VirtualCrossoverProcessingCoordinator coordinator,
        Func<Complex[], int, int, AnalysisCurve> buildMagnitudeCurve)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.buildMagnitudeCurve = buildMagnitudeCurve
            ?? throw new ArgumentNullException(nameof(buildMagnitudeCurve));
    }

    // The magnitude curves and complex sum the metric reads, built the same way
    // for the on-screen redraw and for a synchronous read (e.g. the Auto delay
    // log) so the two never disagree. Fewer than two channels yield no metric.
    public (List<AnalysisCurve>? Magnitudes, AnalysisCurve? Sum) BuildCurves(
        List<ProcessedChannel> processed)
    {
        if (processed.Count < 2)
        {
            return (null, null);
        }

        // Every curve — the channels AND the sum — shares one window anchor (the
        // earliest arrival): with per-channel anchors the gates capture slightly
        // different room content and the loss can poke above its 0 dB ceiling.
        // The summed envelope peak can sit between the arrivals or vanish under
        // cancellation, so the anchor is the earliest arrival, not the sum peak.
        int anchor = processed.Min(item => item.PeakIndex);
        // One windowed FFT + resample per channel; GetPrimarySpectrum allocates
        // its own buffers and reads only the (redraw-stable) options and
        // calibration, so the channels' spectra compute across cores. AsOrdered
        // keeps the result aligned with the channel list.
        List<AnalysisCurve> magnitudes = processed
            .AsParallel()
            .AsOrdered()
            .Select(item => buildMagnitudeCurve(
                item.ImpulseResponse, anchor, item.Channel.SampleRate))
            .ToList();
        Complex[] sum = VirtualCrossoverAnalysis.SumImpulseResponses(
            processed.Select(item => item.ImpulseResponse).ToList());
        AnalysisCurve sumCurve = buildMagnitudeCurve(
            sum, anchor, processed[0].Channel.SampleRate);
        return (magnitudes, sumCurve);
    }

    // Builds the sum-loss read-outs for a processed set without touching any
    // control, so they can feed the label, its tooltip, and the Auto delay log
    // from one computation. Empty when there is no metric (fewer than two channels).
    public List<VirtualCrossoverMetric.Entry> BuildEntries(
        List<ProcessedChannel> processed,
        List<AnalysisCurve>? magnitudes,
        AnalysisCurve? sumCurve)
    {
        var entries = new List<VirtualCrossoverMetric.Entry>();
        if (magnitudes == null || sumCurve == null)
        {
            return entries;
        }

        List<IReadOnlyList<SignalPoint>> channelPoints = magnitudes
            .Select(curve => (IReadOnlyList<SignalPoint>)curve.Points)
            .ToList();

        // Per-junction read-outs first, so an improvement at one crossover is
        // not averaged away by the other. Each junction reads the full sum
        // inside its own pair band; the out-of-pair channels are filtered so
        // far down there that their contribution is negligible.
        foreach (AdjacentPair pair in ProcessedChannels.GetAdjacentPairs(
            ProcessedChannels.OrderByBand(processed)))
        {
            double? pairLoss = VirtualCrossoverAnalysis.AverageSumLossDb(
                sumCurve.Points, channelPoints, pair.BandLowHz, pair.BandHighHz);
            double? pairDip = VirtualCrossoverAnalysis.MinimumSumLossDb(
                sumCurve.Points, channelPoints, pair.BandLowHz, pair.BandHighHz);
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

        (double minHz, double maxHz) = ProcessedChannels.GetCrossoverWindow(processed);
        double? loss = VirtualCrossoverAnalysis.AverageSumLossDb(
            sumCurve.Points, channelPoints, minHz, maxHz);
        double? dip = VirtualCrossoverAnalysis.MinimumSumLossDb(
            sumCurve.Points, channelPoints, minHz, maxHz);
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
                    item.ImpulseResponse, item.Channel.SampleRate);
                spectra.Add(item, spectrum);
            }

            return spectrum;
        }

        foreach (AdjacentPair pair in ProcessedChannels.GetAdjacentPairs(
            ProcessedChannels.OrderByBand(processed)))
        {
            if (pair.Lower.Channel.SampleRate != pair.Upper.Channel.SampleRate)
            {
                continue;
            }

            JunctionPhaseResult? result = JunctionPhaseAlignment.AnalyzeSpectra(
                SpectrumOf(pair.Lower),
                SpectrumOf(pair.Upper),
                pair.Lower.Channel.SampleRate,
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

            VirtualCrossoverChannelSettings leftSettings = channel.SideSettings(false);
            VirtualCrossoverChannelState leftState = channel.PhysicalSideState(false);
            if (!leftSettings.Enabled || leftSettings.Bypass ||
                leftState.ProcessingSource is not { } leftSource)
            {
                continue;
            }

            VirtualCrossoverChannelSettings rightSettings = channel.SideSettings(true);
            VirtualCrossoverChannelState rightState = channel.PhysicalSideState(true);
            if (!mono &&
                (!rightSettings.Enabled || rightSettings.Bypass ||
                    rightState.ProcessingSource is not { }))
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
                    Chain = settings.ToChain()
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
            object? arrivalCompleted = await coordinator.RunAuxiliaryAsync(
                revision,
                cancellationToken =>
            {
                foreach (StereoDeltaJob job in jobs)
                {
                    foreach (SideProcessJob side in job.Sides)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
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
    /// The complex-sum magnitude of the OPPOSITE side (dashed and translucent
    /// on the plot), so the two sides' tunes compare at a glance without
    /// flipping back and forth. Mono channels contribute their single response
    /// to both sides' sums, exactly as they do physically. Null when the
    /// opposite side has fewer than two participating channels — a "sum" of
    /// one driver is just that driver. Uses the coordinator cache, so it shares
    /// processed responses and staleness handling with the main redraw.
    /// </summary>
    public async Task<AnalysisCurve?> ComputeOppositeSumCurveAsync(
        IReadOnlyList<VirtualCrossoverChannel> channels,
        bool oppositeRight,
        long revision)
    {
        VirtualCrossoverSideSum? side = await ComputeSideSumAsync(
            channels, oppositeRight, revision, minimumChannels: 2);
        return side == null
            ? null
            : buildMagnitudeCurve(
                side.ImpulseResponse, side.AnchorIndex, side.SampleRate);
    }

    /// <summary>
    /// The complex sum of one side's participating channels, processed through
    /// their chains. Mono channels contribute their single response to both
    /// sides, exactly as they do physically. Null when the side has fewer than
    /// <paramref name="minimumChannels"/> participating channels, or when the
    /// render went stale. Uses the coordinator cache, so it shares processed
    /// responses and staleness handling with the main redraw.
    /// </summary>
    public async Task<VirtualCrossoverSideSum?> ComputeSideSumAsync(
        IReadOnlyList<VirtualCrossoverChannel> channels,
        bool rightSide,
        long revision,
        int minimumChannels)
    {
        var jobs = new List<SideProcessJob>();
        int nextId = 0;
        for (int channelIndex = 0; channelIndex < channels.Count; channelIndex++)
        {
            VirtualCrossoverChannel channel = channels[channelIndex];
            VirtualCrossoverChannelSettings settings =
                channel.SideSettings(rightSide);
            VirtualCrossoverChannelState state = channel.SideState(rightSide);
            if (!settings.Enabled ||
                state.ProcessingSource is not { } source)
            {
                continue;
            }

            DspChannelChain chain = settings.Bypass
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
                Chain = chain
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
            jobs.Min(side => side.ProcessedPeak),
            jobs[0].SampleRate,
            jobs.Count);
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
        public required DspChannelChain Chain { get; init; }
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
