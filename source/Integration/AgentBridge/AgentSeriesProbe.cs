namespace Resonalyze.Integration.AgentBridge;

/// <summary>
/// The <c>series</c> probe: the package's own rows again, at the density the
/// reply asked for and under no size target. A package over its target is
/// thinned before anything is dropped (see <see cref="AgentSampling"/>), and
/// this is how a reader gets the rows it was thinned out of — any of the
/// package's series, for the channels or the one junction it names, built by
/// the very methods the package is built by, so the columns, the ids and the
/// rounding are the package's.
/// </summary>
internal static class AgentSeriesProbe
{
    public static AgentProbeReport Build(ProbeOperation probe, AgentPackageInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(inputs);

        var wanted = new HashSet<string>(probe.Series ?? [], StringComparer.Ordinal);
        // One density for every frequency grid when the reply names one; the
        // protocol's nominal pair otherwise. The lag series share one row cap.
        int points = probe.PointsPerOctave ?? 0;
        var sampling = new AgentSampling(
            points > 0 ? points : AgentSampling.Nominal.BroadbandPointsPerOctave,
            points > 0 ? points : AgentSampling.Nominal.JunctionPointsPerOctave,
            probe.Rows ?? AgentSampling.Nominal.SweepRows,
            probe.Rows ?? AgentSampling.Nominal.CorrelationRows);
        HashSet<string>? channelFilter = probe.ChannelIds is { Count: > 0 } ids
            ? new HashSet<string>(ids, StringComparer.Ordinal)
            : null;

        List<AgentDiagnosticSeries>? channels = null;
        if (wanted.Contains(AgentProtocol.BroadbandSeries))
        {
            channels = inputs.Channels
                .Where(channel => channelFilter == null || channelFilter.Contains(channel.Id))
                .Select(channel => (channel.Id, Curves: AgentPackageBuilder.BuildChannelCurves(
                    channel, keepCoherence: true, sampling)))
                .Where(item => item.Curves != null)
                .Select(item => new AgentDiagnosticSeries(item.Id, item.Curves!.Broadband))
                .ToList();
        }

        AgentSeries? target = wanted.Contains(AgentProtocol.TargetSeries)
            ? AgentPackageBuilder.TargetSeries(inputs.Target, sampling)
            : null;

        List<AgentProbeSumSeries>? sums = null;
        if (wanted.Contains(AgentProtocol.SumSeries))
        {
            sums = inputs.Sides
                .Select(side => (Side: AgentChannelIds.SideName(side.Side),
                    Series: AgentPackageBuilder.SumSeries(side, sampling)))
                .Where(item => item.Series != null)
                .Select(item => new AgentProbeSumSeries(item.Side, item.Series!))
                .ToList();
        }

        List<AgentProbeJunctionSeries>? junctions = null;
        bool anyJunctionSeries =
            wanted.Contains(AgentProtocol.JunctionCurvesSeries) ||
            wanted.Contains(AgentProtocol.SweepSeries) ||
            wanted.Contains(AgentProtocol.CorrelationSeries) ||
            wanted.Contains(AgentProtocol.CoherenceLadderSeries);
        if (anyJunctionSeries)
        {
            junctions = [];
            foreach (AgentSideInputs side in inputs.Sides)
            {
                foreach (AgentJunctionInputs junction in side.Junctions)
                {
                    string id = AgentPackageBuilder.JunctionId(side, junction);
                    if (probe.JunctionId != null &&
                        !string.Equals(id, probe.JunctionId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    AgentSeries? curves = wanted.Contains(AgentProtocol.JunctionCurvesSeries)
                        ? AgentPackageBuilder.JunctionCurves(side, junction, sampling, withDirectLoss: true)
                        : null;
                    AgentSeries? sweep = wanted.Contains(AgentProtocol.SweepSeries) && junction.Correlation != null
                        ? AgentPackageBuilder.Sweep(junction.Correlation, sampling.SweepRows)
                        : null;
                    AgentSeries? correlation = wanted.Contains(AgentProtocol.CorrelationSeries) && junction.Correlation != null
                        ? AgentPackageBuilder.CorrelationCurve(junction.Correlation, sampling.CorrelationRows)
                        : null;
                    AgentSeries? ladder = wanted.Contains(AgentProtocol.CoherenceLadderSeries)
                        ? AgentPackageBuilder.LadderSeries(junction)
                        : null;
                    if (curves == null && sweep == null && correlation == null && ladder == null)
                    {
                        continue;
                    }

                    junctions.Add(new AgentProbeJunctionSeries(id, curves, sweep, correlation, ladder));
                }
            }
        }

        bool empty =
            channels is not { Count: > 0 } && target == null &&
            sums is not { Count: > 0 } && junctions is not { Count: > 0 };
        return new AgentProbeReport(
            probe.Id, probe.Probe, probe.JunctionId, null, null,
            empty ? "none of the series asked for has a reading in the current view" : null,
            null, null, null,
            channels is { Count: > 0 } ? channels : null,
            sampling,
            target,
            sums is { Count: > 0 } ? sums : null,
            junctions is { Count: > 0 } ? junctions : null);
    }
}
