using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>The readings an AI reply asks for, taken on the tune as it stands and writing nothing. See
/// docs/tech/agent-bridge.md#probes.</summary>
internal static class AgentProbeReader
{
    // Readings come off snapshots, so the compute runs off the UI thread and the tune is never touched.
    public static async Task<AgentProbeReport> ReadAsync(
        ProbeOperation probe, AgentSessionReader reader, AgentViewInputs view, bool gateMisplaced)
    {
        if (probe.Probe == AgentProtocol.ExcessGroupDelayProbe)
        {
            IReadOnlyList<AgentDiagnosticSeries> channels = await reader.ExcessGroupDelaySeriesAsync();
            return new AgentProbeReport(
                probe.Id, probe.Probe, null, null, null,
                channels.Count == 0 ? "no channel has a measurement to read" : null,
                null, null, null, channels.Count == 0 ? null : channels);
        }

        AgentProbeReport Unavailable(string reason) => new(
            probe.Id, probe.Probe, probe.JunctionId, null, null, reason, null, null, null, null);

        if (probe.Probe == AgentProtocol.SeriesProbe)
        {
            // The package's own gather at the reply's density, so rows line up with the package by channel and junction id.
            AgentPackageInputs? inputs = await reader.CaptureInputsAsync(view);
            return inputs == null
                ? Unavailable("the session changed while the reading was taken")
                : AgentSeriesProbe.Build(probe, inputs);
        }

        string? problem = AgentProposalValidator.ResolveJunction(
            reader.Snapshot(view), probe.JunctionId ?? string.Empty,
            out AgentChannelSnapshot? lowerSnapshot, out AgentChannelSnapshot? upperSnapshot);
        if (problem != null)
        {
            return Unavailable(problem.TrimEnd('.'));
        }
        if (gateMisplaced)
        {
            return Unavailable("the phase gate is misplaced");
        }

        if (reader.Session.Block(lowerSnapshot!.Block) is not { } lower ||
            reader.Session.Block(upperSnapshot!.Block) is not { } upper)
        {
            return Unavailable("the blocks changed while the import ran");
        }

        // A probe reads the side its junction id names: variant changes are that side's settings.
        AgentChannelSide namedSide = AgentJunctionIds.TryParse(
            probe.JunctionId, out AgentChannelSide side, out _, out _)
            ? side
            : AgentChannelSide.Left;
        (List<JunctionTuneSide> sides, string? refusal) = JunctionTuneSides(
            lower, upper, namedSide == AgentChannelSide.Right);
        if (refusal != null)
        {
            return Unavailable(refusal);
        }

        int processorRate = reader.Session.ProcessorSampleRateHz;
        if (probe.Probe == AgentProtocol.JunctionDelayProbe)
        {
            IReadOnlyList<JunctionDelayProbeSide> read = await Task.Run(
                () => CrossoverJunctionTuner.ProbeAlignment(sides, processorRate));
            return new AgentProbeReport(
                probe.Id, probe.Probe, probe.JunctionId, lowerSnapshot!.Id, upperSnapshot!.Id,
                null, null, null,
                read.Select(item => new AgentProbeDelaySide(
                    item.Side,
                    [AgentCurveSampling.Frequency(item.BandLowHz), AgentCurveSampling.Frequency(item.BandHighHz)],
                    AgentCurveSampling.Round(item.SearchHalfWindowMs, 2),
                    item.Unavailable,
                    item.Candidates.Select(candidate => new AgentProbeDelayCandidate(
                        AgentCurveSampling.Round(candidate.ExtraDelayMs, 3),
                        candidate.InvertUpper,
                        AgentCurveSampling.Round(candidate.ScoreDb, 2),
                        AgentCurveSampling.Round(candidate.LossDb, 2),
                        AgentCurveSampling.Round(candidate.DipDb, 2),
                        candidate.Chosen)).ToList())).ToList(),
                null);
        }

        (List<JunctionProbeVariant> variants, string? variantProblem) = ProbeVariants(
            probe, sides, lowerSnapshot!, upperSnapshot!, reader.Snapshot(view));
        if (variantProblem != null)
        {
            return Unavailable(variantProblem);
        }

        JunctionProbeResult probed;
        try
        {
            probed = await Task.Run(() => CrossoverJunctionTuner.Probe(sides, processorRate, variants));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Unavailable(exception.Message.TrimEnd('.'));
        }

        // Per-entry affected junctions, not pooled over the probe: pooled lists would point at junctions the winning variant never touched. Entries follow ProbeVariants order, baseline first.
        AgentSessionSnapshot current = reader.Snapshot(view);
        IReadOnlyList<AgentProbeVariant> asked = probe.Variants ?? [];
        return new AgentProbeReport(
            probe.Id, probe.Probe, probe.JunctionId, lowerSnapshot!.Id, upperSnapshot!.Id, null,
            [
                AgentCurveSampling.Frequency(probed.SharedBandLowHz),
                AgentCurveSampling.Frequency(probed.SharedBandHighHz)
            ],
            probed.Entries.Select((entry, index) => ProbeEntryOf(
                entry, index,
                index == 0 || index > asked.Count
                    ? null
                    : AgentProposalValidator.NeighbourJunctionIds(
                        current, probe.JunctionId ?? string.Empty,
                        asked[index - 1].Changes
                            .Select(change => change.ChannelId)
                            .Distinct(StringComparer.Ordinal)
                            .ToList()))).ToList(),
            null,
            null);
    }

    // The baseline is identified by position, never by label: the reply's own labels may say "current" too.
    private static AgentProbeEntry ProbeEntryOf(
        JunctionProbeEntry entry, int index, IReadOnlyList<string>? affected) =>
        new(
            entry.Label,
            index == 0,
            affected is { Count: > 0 } ? affected : null,
            entry.LowerLowPass is { } low ? Edge(low) : null,
            entry.UpperHighPass is { } high ? Edge(high) : null,
            [
                AgentCurveSampling.Frequency(entry.BandLowHz),
                AgentCurveSampling.Frequency(entry.BandHighHz)
            ],
            entry.Unavailable,
            entry.Sides.Select((reading, index) => new AgentProbeSide(
                reading.Side,
                AgentCurveSampling.Round(reading.LossDb, 2),
                AgentCurveSampling.Round(reading.DipDb, 2),
                AgentCurveSampling.Round(reading.RippleDb, 2),
                index < entry.SharedBandSides.Count
                    ? new AgentProbeBandReading(
                        AgentCurveSampling.Round(entry.SharedBandSides[index].LossDb, 2),
                        AgentCurveSampling.Round(entry.SharedBandSides[index].DipDb, 2),
                        AgentCurveSampling.Round(entry.SharedBandSides[index].RippleDb, 2))
                    : null,
                entry.AfterDelay.FirstOrDefault(item => item.Side == reading.Side) is { } alignment
                    ? new AgentProbeAfterDelay(
                        AgentCurveSampling.Round(alignment.ExtraDelayMs, 3),
                        alignment.InvertUpper,
                        AgentCurveSampling.Round(alignment.LossDb, 2),
                        AgentCurveSampling.Round(alignment.DipDb, 2))
                    : null,
                entry.Phase.FirstOrDefault(item => item.Side == reading.Side)?.Result is { } phase
                    ? new AgentProbePhaseReading(
                        AgentCurveSampling.Round(phase.PhaseAtCrossoverDeg, 1),
                        AgentCurveSampling.Round(phase.PhaseConsistency, 2),
                        AgentCurveSampling.Round(phase.CurrentScore, 2),
                        AgentCurveSampling.Round(phase.BestScore, 2),
                        AgentCurveSampling.Round(phase.BestExtraDelayMs, 3),
                        phase.BestInvert,
                        AgentCurveSampling.Round(phase.FitRmsDeg, 1))
                    : null)).ToList());

    private static AgentPackageEdge Edge(CrossoverEdge edge) =>
        new(edge.Family.ToString(), AgentCurveSampling.Frequency(edge.FrequencyHz),
            edge.SlopeDbPerOctave, edge.RippleDb);

    /// <summary>Label of a probe's baseline entry; the baseline is marked by position, not by this text.</summary>
    internal const string CurrentLabel = "current";

    // Variant changes go onto copies of the two channels' settings, validated through the validator's path; no live setting is touched.
    private static (List<JunctionProbeVariant> Variants, string? Problem) ProbeVariants(
        ProbeOperation probe,
        IReadOnlyList<JunctionTuneSide> sides,
        AgentChannelSnapshot lower,
        AgentChannelSnapshot upper,
        AgentSessionSnapshot session)
    {
        var variants = new List<JunctionProbeVariant>
        {
            new(CurrentLabel,
                sides.Select(side => new JunctionProbeChains(side.LowerChain, side.UpperChain)).ToList())
        };
        int index = 1;
        foreach (AgentProbeVariant variant in probe.Variants ?? [])
        {
            VirtualCrossoverChannelSettings lowerCopy = AgentOperations.CloneEditable(lower.Settings);
            VirtualCrossoverChannelSettings upperCopy = AgentOperations.CloneEditable(upper.Settings);
            foreach (AgentProbeChange change in variant.Changes)
            {
                bool isLower = string.Equals(change.ChannelId, lower.Id, StringComparison.Ordinal);
                if (!isLower && !string.Equals(change.ChannelId, upper.Id, StringComparison.Ordinal))
                {
                    return ([], $"'{change.ChannelId}' is not one of the junction's channels");
                }

                string? problem = AgentProposalValidator.ApplyProbeChange(
                    change, session, isLower ? lowerCopy : upperCopy);
                if (problem != null)
                {
                    return ([], problem.TrimEnd('.'));
                }
            }

            // One side, and the snapshot's settings are that side's: nothing to merge.
            var chains = new JunctionProbeChains(
                lowerCopy.ToChain(lower.Zone),
                upperCopy.ToChain(upper.Zone));
            variants.Add(new JunctionProbeVariant(
                string.IsNullOrWhiteSpace(variant.Label) ? $"variant {index}" : variant.Label,
                sides.Select(_ => chains).ToList()));
            index++;
        }

        return (variants, null);
    }

    /// <summary>A junction's two blocks as the tuner reads them: every side carrying both measurements with its own chain, or the one side asked for. A mono block is routed to both sides; two mono blocks are read once, as the right side would repeat the left.</summary>
    internal static (List<JunctionTuneSide> Sides, string? Refusal) JunctionTuneSides(
        VirtualCrossoverChannel lower, VirtualCrossoverChannel upper, bool? rightSideOnly)
    {
        var sides = new List<JunctionTuneSide>();
        foreach (bool rightSide in new[] { false, true })
        {
            if (rightSideOnly is { } only && only != rightSide)
            {
                continue;
            }
            if (rightSide && lower.Pair.Mono && upper.Pair.Mono && rightSideOnly == null)
            {
                continue;
            }

            VirtualCrossoverChannelState lowerState = lower.SideState(rightSide && !lower.Pair.Mono);
            VirtualCrossoverChannelState upperState = upper.SideState(rightSide && !upper.Pair.Mono);
            if (lowerState.TransferImpulseResponse == null || upperState.TransferImpulseResponse == null)
            {
                continue;
            }
            if (lowerState.SampleRate != upperState.SampleRate)
            {
                return ([], "the two measurements on the " +
                    $"{(rightSide ? "right" : "left")} side have different sample rates");
            }

            sides.Add(new JunctionTuneSide(
                rightSide ? "right" : "left",
                lowerState.TransferImpulseResponse,
                lower.SideSettings(rightSide && !lower.Pair.Mono).ToChain(lower.Pair.Zone),
                upperState.TransferImpulseResponse,
                upper.SideSettings(rightSide && !upper.Pair.Mono).ToChain(upper.Pair.Zone),
                lowerState.SampleRate));
        }

        return sides.Count == 0
            ? ([], "no side has both blocks measured")
            : (sides, null);
    }

    /// <summary>The tune re-aligns the upper block, so only a mono upper block holds one delay for both sides; a
    /// mono lower block stays put while each side's upper block aligns to it on its own.</summary>
    internal static bool SharesOneAlignment(VirtualCrossoverChannel lower, VirtualCrossoverChannel upper) =>
        upper.Pair.Mono;

    /// <summary>Each channel's plant from its spatial average, where the EQ handoff would give Auto Tune one
    /// (<paramref name="mode"/> null while the hybrid is not drawn).</summary>
    internal static List<JunctionTuneSide> WithSpatialAverages(
        List<JunctionTuneSide> sides,
        VirtualCrossoverChannel lower,
        VirtualCrossoverChannel upper,
        VirtualCrossoverSpatialAverageMode? mode,
        SpatialAverageCalibration calibration,
        int processorSampleRateHz)
    {
        if (mode is not { } family)
        {
            return sides;
        }

        return sides
            .Select(side =>
            {
                bool rightSide = side.Name == "right";
                return side with
                {
                    LowerMagnitude = Plant(lower, rightSide, CrossoverJunctionTuner.WithoutLowPass(side.LowerChain)),
                    UpperMagnitude = Plant(upper, rightSide, CrossoverJunctionTuner.WithoutHighPass(side.UpperChain))
                };
            })
            .ToList();

        IReadOnlyList<SignalPoint>? Plant(VirtualCrossoverChannel channel, bool rightSide, DspChannelChain chain) =>
            channel.SideState(rightSide && !channel.Pair.Mono).SpatialAverageFor(family) is { } capture
                ? SpatialAverageHybrid.BuildChannelCurve(
                    capture,
                    chain with { Peq = null },
                    processorSampleRateHz,
                    calibration,
                    capture.ToCurvePoints().Select(point => point.X).ToList(),
                    smoothingCode: 0)
                : null;
    }
}
