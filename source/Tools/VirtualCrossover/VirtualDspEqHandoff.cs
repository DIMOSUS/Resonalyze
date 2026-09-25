using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Write-back address of a PEQ handoff plus everything that must still match for the bank to land.
/// Each field guards one invisible change; see docs/tech/virtual-dsp-session-file.md#eq-wizard-handoff.
/// </summary>
internal sealed record VirtualDspEqReturnToken(
    VirtualCrossoverChannel Channel,
    bool RightSide,
    long ProjectGeneration,
    int SourceRevision,
    bool Mono,
    DspChannelChain PreviewChain,
    bool WithChain,
    // What the target was shaped with; a goal changed on the card moves this and nothing else.
    CrossoverSpec? TargetCrossover,
    PeqBankState Peq,
    double TargetLevelDb,
    PhaseAnalysisSettings GateTemplate,
    double? PinnedGateOffsetMs,
    // The side whose gate the curve was read through: a mono token addresses LEFT but was gated by the side shown.
    bool GateRightSide,
    CalibrationFile? Calibration,
    LiveCaptureDocument? SpatialAverage,
    // How the capture was read (Off/Own/Specific): two of those switches leave the calibration guard satisfied.
    SpatialAverageCalibration SpatialAverageCalibration,
    int ProcessorSampleRateHz);

/// <summary>What one channel side sends into the EQ Wizard. <see cref="TargetLevelDb"/> is valid verbatim: same gate and calibration as the Virtual DSP plot.</summary>
internal sealed record VirtualDspEqHandoffRequest(
    EqWizardCurveSource Source,
    EqualizationCurve BankSeed,
    double? AutoTuneMinHz,
    double? AutoTuneMaxHz,
    double TargetLevelDb,
    double TargetLevelMinDb,
    double TargetLevelMaxDb,
    int SmoothingInverseOctaves,
    VirtualDspEqReturnToken Token);

/// <summary>UI-free rules for building and landing PEQ handoffs between Virtual DSP and the EQ Wizard.</summary>
internal static class VirtualDspEqHandoff
{
    /// <summary>
    /// Prepares one side for the wizard: through the chain without PEQ (<paramref name="withChain"/>) or the raw measurement,
    /// under the panel's gate. The corrected preview reruns the whole chain, not the bank's ideal magnitude.
    /// </summary>
    public static VirtualDspEqHandoffRequest Build(
        VirtualCrossoverChannel channel,
        bool rightSide,
        bool withChain,
        DspProcessorProfile processorProfile,
        PhaseAnalysisSettings gateTemplate,
        double? pinnedGateOffsetMs,
        int? renderAnchorIndex,
        EqWizardPhaseContext? phaseContext,
        double targetLevelDb,
        double targetLevelMinDb,
        double targetLevelMaxDb,
        int smoothingInverseOctaves,
        CalibrationFile? calibration,
        string? calibrationName,
        SpatialAverageCalibration spatialAverageCalibration,
        long projectGeneration,
        LiveCaptureDocument? spatialAverage,
        double spatialAverageOffsetDb,
        bool pointMeasured = false)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(gateTemplate);
        ArgumentNullException.ThrowIfNull(processorProfile);

        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
        if (state.TransferImpulseResponse is null || state.ProcessingSource is null)
        {
            throw new InvalidOperationException(
                "The channel side has no measurement to hand to the EQ Wizard.");
        }

        int sampleRate = state.SampleRate;
        // Chain realized at the processor's rate, the record stays at the measurement's; the preview needs both.
        int processorSampleRate = processorProfile.SampleRateHz;
        Complex[] response;
        int anchorIndex;
        double gateOffsetMs;
        DspChannelChain previewChain = withChain
            ? settings.ToChain(channel.Pair.Zone) with { Peq = null }
            : DspChannelChain.Identity;
        if (withChain)
        {
            // Delay and polarity are magnitude-transparent but keep the response in the processed view's time.
            DspChannelChain chain = previewChain;
            response = state.ProcessingSource.Apply(
                chain, sampleRate, processorSampleRate);
            anchorIndex = renderAnchorIndex ?? ProcessedChannels.StartAnchorIndex(
                response,
                VirtualCrossoverAnalysis.FindPeakIndex(response),
                sampleRate,
                VirtualCrossoverAnalysis.ChainValidRange(
                    state.ProcessingSource.SampleCount,
                    chain,
                    sampleRate,
                    processorSampleRate,
                    response.Length));
            gateOffsetMs = pinnedGateOffsetMs ?? anchorIndex * 1_000.0 / sampleRate;
        }
        else
        {
            // Anchored on its own start, like the panel's Raw curve.
            response = state.TransferImpulseResponse;
            anchorIndex = ProcessedChannels.StartAnchorIndex(
                response, state.TransferPeakIndex, sampleRate);
            gateOffsetMs = anchorIndex * 1_000.0 / sampleRate;
        }

        (double MinHz, double MaxHz)? window = withChain
            ? PassbandFor(settings)
            : null;

        string side = channel.Pair.Mono ? "mono" : rightSide ? "R" : "L";
        string? average = spatialAverage == null
            ? null
            : spatialAverage.Method == SpatialAverageMethod.MicArray ? "Array" : "MMM";
        string variant = average != null
            ? withChain ? $"DSP, {average}" : average
            : withChain ? "DSP" : "raw";
        // Bypass: the PEQ still belongs to the chain, so it is built and the note says so.
        // No array here: coherence and EqBoostabilityMask still gate boosts. See docs/tech/virtual-dsp-session-file.md#eq-wizard-handoff.
        string pointNote = pointMeasured
            ? "\r\nThis channel has NO spatial average: the rest of the set is drawn " +
              "from arrays and this one from its single measurement position. Its " +
              "dips may belong to those few centimetres rather than to the seat."
            : string.Empty;
        string bypassNote = withChain && channel.Pair.Bypass
            ? "\r\nThe block is BYPASSED on the plot right now, so the panel is drawing " +
              "its raw response — this curve is the chain the PEQ will live in."
            : string.Empty;
        var source = new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.VirtualDspChannel,
            DisplayName = $"Ch {channel.Name} · {side} ({variant})",
            Description =
                $"Virtual DSP channel {channel.Name}, {SideDescription(channel, rightSide)}" +
                (string.IsNullOrWhiteSpace(settings.DisplayName)
                    ? string.Empty
                    : $" — {settings.DisplayName}") +
                (spatialAverage != null
                    ? withChain
                        ? "\r\nSpatial average with the DSP chain applied (PEQ " +
                          "bypassed). No window: an average is a steady-state curve." +
                          "\r\nPhase still reads the impulse response, through the " +
                          "Virtual DSP gate."
                        : "\r\nSpatial average, as measured with the DSP bypassed." +
                          "\r\nPhase still reads the impulse response, through the " +
                          "Virtual DSP gate."
                    : withChain
                        ? "\r\nDSP chain applied (PEQ bypassed), windowed by the Virtual DSP gate."
                        : "\r\nRaw measurement, windowed by the Virtual DSP gate at its own arrival.") +
                pointNote +
                bypassNote,
            // The measured band travels, or the wizard would draw past where the panel's curve stops.
            Measurement = new ImpulseMeasurementView(response, anchorIndex, sampleRate)
            {
                LowestMeasuredFrequencyHz = state.MeasuredBand.LowEdgeHz,
                HighestMeasuredFrequencyHz = state.MeasuredBand.HighEdgeHz
            },
            // For an array, the positions' agreement is the witness that gates boosts.
            Coherence = spatialAverage is { Method: SpatialAverageMethod.MicArray }
                ? EqWizardSourceResolver.BuildAgreementCurve(
                    spatialAverage, state.ArraySpreadDb)
                : EqWizardSourceResolver.ExtractTransferCoherence(
                    state.TransferCoherence, sampleRate),
            GateSettings = gateTemplate with { GateOffsetMs = gateOffsetMs },
            PinnedCalibration = calibration,
            PinnedCalibrationName = calibration == null ? null : calibrationName,
            SpatialAverageCalibration = spatialAverageCalibration,
            PreviewImpulseResponse = state.ProcessingSource.CroppedImpulseResponse,
            PreviewChain = previewChain,
            TargetCrossover = withChain ? TargetCrossoverFor(settings) : null,
            ElectricalCrossover = withChain ? ElectricalCrossoverFor(settings) : null,
            // A designed crossover kernel describes its own slope; the design's corners do not (a windowed sinc's
            // slope is its window and length). FirDesign is what tells a crossover FIR from a correction one.
            TargetCrossoverFir = withChain && settings.HasFirCrossover ? settings.Fir : null,
            // Neighbours only for a chain handoff: a raw curve against processed neighbours describes no real system.
            PhaseContext = withChain ? phaseContext : null,
            // Hybrid: the average replaces the fitted magnitude; the impulse response still serves phase.
            SpatialAverage = spatialAverage,
            SpatialAverageOffsetDb = spatialAverageOffsetDb,
            SampleRateHz = sampleRate,
            ProcessorProfile = processorProfile,
            CurveKind = AnalysisCurveKind.Primary
        };

        return new VirtualDspEqHandoffRequest(
            source,
            new EqualizationCurve(settings.PeqBands, settings.PeqPreampDb),
            window?.MinHz,
            window?.MaxHz,
            targetLevelDb,
            // The panel's range: a level outside it would come back silently clamped.
            targetLevelMinDb,
            targetLevelMaxDb,
            smoothingInverseOctaves,
            // A mono pair's token says LEFT outright, so a later un-mono still lands on the source set.
            new VirtualDspEqReturnToken(
                channel,
                rightSide && !channel.Pair.Mono,
                projectGeneration,
                state.SourceRevision,
                channel.Pair.Mono,
                previewChain,
                withChain,
                source.TargetCrossover,
                new PeqBankState(settings.PeqBands, settings.PeqPreampDb),
                targetLevelDb,
                gateTemplate,
                pinnedGateOffsetMs,
                rightSide,
                calibration,
                spatialAverage,
                spatialAverageCalibration,
                processorSampleRate));
    }

    /// <summary>Lands a finished bank on the side it came from; false and no write when any guard fails, so the wizard stays open.</summary>
    /// <remarks>Of chain edits, only a polarity flip passes a chain handoff (raw handoffs ignore the chain). See docs/tech/virtual-dsp-session-file.md#eq-wizard-handoff.</remarks>
    public static bool TryApplyReturn(
        IReadOnlyList<VirtualCrossoverChannel> channels,
        VirtualDspEqReturnToken token,
        EqualizationCurve curve,
        long projectGeneration,
        CalibrationFile? calibration,
        SpatialAverageCalibration spatialAverageCalibration,
        PhaseAnalysisSettings gateTemplate,
        double? pinnedGateOffsetMs,
        double targetLevelDb,
        LiveCaptureDocument? spatialAverage,
        int processorSampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(curve);

        if (token.ProjectGeneration != projectGeneration ||
            !channels.Contains(token.Channel))
        {
            return false;
        }

        if (!CalibrationFile.SameCurve(token.Calibration, calibration))
        {
            return false;
        }

        if (token.ProcessorSampleRateHz != processorSampleRateHz)
        {
            return false;
        }

        if (token.Mono != token.Channel.Pair.Mono)
        {
            return false;
        }

        // Reference identity: a re-attached file is a different capture even with identical bytes.
        if (!ReferenceEquals(token.SpatialAverage, spatialAverage))
        {
            return false;
        }

        // Only where there is a capture; for an impulse-response curve the mode describes nothing.
        if (token.SpatialAverage != null &&
            !token.SpatialAverageCalibration.Matches(spatialAverageCalibration))
        {
            return false;
        }

        // The pin counts only for a chain handoff: a raw curve never read it.
        if (!Equals(Comparable(token.GateTemplate), Comparable(gateTemplate)) ||
            (token.WithChain &&
                !Nullable.Equals(token.PinnedGateOffsetMs, pinnedGateOffsetMs)) ||
            !token.TargetLevelDb.Equals(targetLevelDb))
        {
            return false;
        }

        if (token.WithChain &&
            !Equals(
                Comparable(token.PreviewChain),
                Comparable(
                    token.Channel.Pair.ToChain(token.RightSide) with
                    {
                        Peq = null
                    })))
        {
            return false;
        }

        // Read the PHYSICAL side: the token may be mono while the pair no longer is.
        VirtualCrossoverChannelState state =
            token.Channel.PhysicalSideState(token.RightSide);
        if (state.SourceRevision != token.SourceRevision)
        {
            return false;
        }

        // SideFor, not the active side: the user may have flipped L/R while editing.
        VirtualCrossoverChannelSettings settings = token.Channel.Pair.SideFor(token.RightSide);

        // The chain check reads the electrical filter; a goal changed meanwhile means a different target.
        if (token.WithChain &&
            !Equals(token.TargetCrossover, TargetCrossoverFor(settings)))
        {
            return false;
        }

        // The chain check excludes the PEQ, so a Load or Clear in the panel would otherwise be a lost update.
        if (!token.Peq.Equals(new PeqBankState(settings.PeqBands, settings.PeqPreampDb)))
        {
            return false;
        }
        // The channel's own bank back: nothing to write, and it keeps the name it was loaded under.
        if (token.Peq.Equals(new PeqBankState(curve.Bands, curve.PreampDb)))
        {
            return true;
        }

        settings.PeqBands = curve.Bands.ToList();
        settings.PeqPreampDb = curve.PreampDb;
        settings.PeqSourceName = "EQ Wizard";
        return true;
    }

    // Only what the magnitude reads: it forces Fixed and ignores FDW/detrend/unwrap; the offset is compared as the pin.
    private static PhaseAnalysisSettings Comparable(PhaseAnalysisSettings gate) =>
        gate with
        {
            FdwCycles = 0,
            DetrendMode = PhaseDetrendMode.Off,
            ManualDetrendMilliseconds = 0,
            GateOffsetMs = 0,
            Unwrap = false,
            SmoothingInverseOctaves = 0
        };

    // Polarity is -1 at every frequency: neither shape nor level changes.
    private static DspChannelChain Comparable(DspChannelChain chain) =>
        chain with { InvertPolarity = false };

    /// <summary>Per edge, the stated acoustic crossover at the electrical corner, else the electrical filter. See
    /// docs/tech/crossover-auto-setup.md#acoustic-slope-target.</summary>
    internal static CrossoverSpec GoalCrossoverFor(VirtualCrossoverChannelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        CrossoverSpec electrical = settings.EffectiveCrossover;
        // Only edges the kind reads.
        CrossoverEdge? lowPass = electrical.LowPassHz == null
            ? null
            : Asked(settings.AcousticLowPass, electrical.LowPassEdge);
        CrossoverEdge? highPass = electrical.HighPassHz == null
            ? null
            : Asked(settings.AcousticHighPass, electrical.HighPassEdge);
        return lowPass == null && highPass == null
            ? electrical
            : new CrossoverSpec(
                electrical.Kind,
                lowPass ?? electrical.LowPassEdge,
                highPass ?? electrical.HighPassEdge);
    }

    /// <summary>Null with the IIR crossover off: EffectiveCrossover would then be the FIR design, which travels
    /// separately.</summary>
    internal static CrossoverSpec? TargetCrossoverFor(VirtualCrossoverChannelSettings settings) =>
        settings.CrossoverKind != CrossoverKind.Off ? GoalCrossoverFor(settings) : null;

    /// <summary>The filter the channel runs, where the target follows a stated goal instead; else null.</summary>
    internal static CrossoverSpec? ElectricalCrossoverFor(VirtualCrossoverChannelSettings settings) =>
        TargetCrossoverFor(settings) is { } target && !Equals(target, settings.EffectiveCrossover)
            ? settings.EffectiveCrossover
            : null;

    private static CrossoverEdge? Asked(JunctionAcousticTarget? goal, CrossoverEdge? electrical) =>
        goal is { } asked && electrical is { } edge
            ? new CrossoverEdge(asked.Family, edge.FrequencyHz, asked.SlopeDbPerOctave)
            : null;

    /// <summary>Passband where the channel plays — the narrower of the IIR crossover's corners and a designed FIR's — or null when neither filters (callers keep their range).</summary>
    internal static (double MinHz, double MaxHz)? PassbandFor(
        VirtualCrossoverChannelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // Both stages can filter at once, and then both narrow the band: EffectiveCrossover answers the IIR while it
        // is on, so the FIR design is read beside it. With the IIR off the two are the same spec and this is a no-op.
        CrossoverSpec? fir = settings.FirDesignCrossover;
        double? highPass = Inner(settings.EffectiveHighPassHz, fir?.HighPassHz, Math.Max);
        double? lowPass = Inner(settings.EffectiveLowPassHz, fir?.LowPassHz, Math.Min);
        return highPass is null && lowPass is null
            ? null
            : (highPass ?? 20, lowPass ?? 20_000);
    }

    private static double? Inner(double? stage, double? other, Func<double, double, double> narrower) =>
        stage is { } one
            ? other is { } two ? narrower(one, two) : one
            : other;

    private static string SideDescription(VirtualCrossoverChannel channel, bool rightSide) =>
        channel.Pair.Mono ? "mono" : rightSide ? "right side" : "left side";
}
