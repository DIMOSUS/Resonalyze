using Resonalyze.Dsp;

namespace Resonalyze;

internal enum GateCutKind
{
    OpensAfterArrival,

    ClosesBeforeArrival
}

/// <summary>LeadingEdgeLossDb (<see cref="DataHelper.GateLeadingEdgeLossDb"/>) is meaningful for <see cref="GateCutKind.OpensAfterArrival"/> only.</summary>
internal readonly record struct GateCutChannel(
    string Name,
    double StartMs,
    GateCutKind Kind,
    double LeadingEdgeLossDb);

/// <summary>Whether the magnitude view's window (an ABSOLUTE time) holds every processed channel; the automatic commands are
/// verified on that view, so a window that misses a channel refuses them. See docs/tech/virtual-dsp-panel.md#gate-placement-verdict.</summary>
internal sealed record GatePlacementVerdict(
    double OffsetMs,
    double PlateauMs,
    double RightMs,
    bool Pinned,
    bool RightSide,
    IReadOnlyList<GateCutChannel> Cut)
{
    /// <summary>3 dB: field misplacements are 44–70 dB apart on this comparison, short gates 0.0 dB.</summary>
    private const double MisplacementMarginDb = 3.0;

    public bool CutsChannels => Cut.Count > 0;

    public string SideLabel => RightSide ? "R" : "L";

    public double PlateauEndMs => OffsetMs + PlateauMs;

    /// <summary>Content between <see cref="PlateauEndMs"/> and here is attenuated, not absent.</summary>
    public double WindowEndMs => PlateauEndMs + RightMs;

    public bool Any(GateCutKind kind) => Cut.Any(item => item.Kind == kind);

    public static GatePlacementVerdict? Judge(
        IReadOnlyList<ProcessedChannel> processed,
        MagnitudeGateSnapshot snapshot,
        bool rightSide)
    {
        if (processed.Count == 0)
        {
            return null;
        }

        int sampleRate = processed[0].SampleRate;
        double offsetMs = snapshot.ResolveGateOffsetMs(
            oppositeSide: false,
            ProcessedChannels.SharedStartAnchorIndex(processed),
            sampleRate);
        var cut = new List<GateCutChannel>();
        foreach (ProcessedChannel item in processed)
        {
            double startMs = TransferIrStartCache.ResolveStartMs(
                item.ImpulseResponse, sampleRate, item.PeakIndex,
                item.ValidRange);
            var view = new ImpulseMeasurementView(item.ImpulseResponse, 0, sampleRate);
            double lossDb = Loss(offsetMs);
            if (JudgeCut(
                    startMs,
                    offsetMs,
                    snapshot.Template.PlateauMs,
                    snapshot.Template.RightMs,
                    lossDb,
                    Loss(startMs)) is { } kind)
            {
                cut.Add(new GateCutChannel(item.Channel.Name, startMs, kind, lossDb));
            }

            double Loss(double placementMs) => DataHelper.GateLeadingEdgeLossDb(
                view,
                placementMs,
                snapshot.Template.LeftMs,
                snapshot.Template.PlateauMs,
                snapshot.Template.RightMs);
        }

        return new GatePlacementVerdict(
            offsetMs,
            snapshot.Template.PlateauMs,
            snapshot.Template.RightMs,
            snapshot.PinnedOffsetMs is not null,
            rightSide,
            cut);
    }

    /// <summary>Null when the window holds the channel. Far side judged by geometry (front inside plateau + fade),
    /// near side by leading-edge loss vs the channel's own arrival. See docs/tech/virtual-dsp-panel.md#gate-placement-verdict.</summary>
    internal static GateCutKind? JudgeCut(
        double startMs,
        double gateOffsetMs,
        double plateauMs,
        double rightMs,
        double placementLossDb,
        double ownArrivalLossDb)
    {
        if (startMs >= gateOffsetMs + plateauMs + rightMs)
        {
            return GateCutKind.ClosesBeforeArrival;
        }

        return startMs < gateOffsetMs &&
            placementLossDb > PhaseGatePlacement.MaxLeadingEdgeLossDb &&
            placementLossDb > ownArrivalLossDb + MisplacementMarginDb
                ? GateCutKind.OpensAfterArrival
                : null;
    }

    public string FormatWarning()
    {
        string names = string.Join(", ", Cut.Select(item => item.Name));
        double earliest = Cut.Min(item => item.StartMs);
        double latest = Cut.Max(item => item.StartMs);
        bool one = Cut.Count == 1;
        string arrivals = one || Math.Abs(latest - earliest) < 0.005
            ? $"{earliest:0.00} ms"
            : $"{earliest:0.00}–{latest:0.00} ms";
        string curves = one ? "that curve reads" : "those curves read";
        string opening = $"⚠ {SideLabel} gate at {OffsetMs:0.00} ms ";
        if (!Any(GateCutKind.ClosesBeforeArrival))
        {
            return opening +
                $"opens after {names} {(one ? "arrives" : "arrive")} ({arrivals}) — " +
                $"{curves} the reverberant tail.";
        }

        if (!Any(GateCutKind.OpensAfterArrival))
        {
            return opening +
                $"is over before {names} {(one ? "arrives" : "arrive")} ({arrivals}) — " +
                $"{(one ? "that curve holds" : "those curves hold")} none of " +
                $"{(one ? "it" : "them")}.";
        }

        return opening + $"misses {names} (arriving {arrivals}) — " +
            $"{(one ? "that curve is" : "those curves are")} not the response.";
    }

    // Shared by the tooltip and the refusals so they cannot describe a placement differently.
    public string FormatDetail()
    {
        bool opensLate = Any(GateCutKind.OpensAfterArrival);
        bool closesEarly = Any(GateCutKind.ClosesBeforeArrival);
        bool one = Cut.Count == 1;
        var text = new System.Text.StringBuilder();
        text.Append($"The gate's plateau runs from {OffsetMs:0.00} to ")
            .Append($"{PlateauEndMs:0.00} ms")
            .Append(closesEarly
                ? $", with its fade-out over at {WindowEndMs:0.00} ms, "
                : ", ")
            .Append(one
                ? "and this channel falls outside it, so its curve — and the sum-loss " +
                    "read-out built from it — does not describe the driver:"
                : "and these channels fall outside it, so their curves — and the " +
                    "sum-loss read-out built from them — do not describe the drivers:")
            .AppendLine()
            .AppendLine();
        foreach (GateCutChannel item in Cut)
        {
            text.AppendLine(item.Kind == GateCutKind.OpensAfterArrival
                ? $"    {item.Name} — arrives {item.StartMs:0.00} ms, ahead of the plateau; " +
                    "the curve is the reverberant tail, leading-edge loss " +
                    FormatLeadingEdgeLossDb(item.LeadingEdgeLossDb)
                : $"    {item.Name} — arrives {item.StartMs:0.00} ms, after the window " +
                    $"closes at {WindowEndMs:0.00} ms; it is over before the " +
                    "channel starts and the curve holds none of it");
        }

        if (opensLate)
        {
            text.AppendLine()
                .Append("(Leading-edge loss is what the window throws away ahead of its ")
                .Append("plateau against what it keeps, so ")
                .Append(
                    $"{PhaseGatePlacement.MaxLeadingEdgeLossDb:0} dB is already the ceiling.)")
                .AppendLine();
        }

        text.AppendLine();
        if (opensLate)
        {
            text.Append(Pinned
                ? "Open Gate… and press Auto: the window then follows this side's own " +
                    "earliest arrival instead of a time fixed to other measurements. "
                : "The gate is on Auto and still opens late, which means a shoulder too " +
                    "short for these arrivals: widen the left fade in Gate…, or check " +
                    "what those channels' sources hold ahead of their front. ");
        }

        if (closesEarly)
        {
            double latest = Cut
                .Where(item => item.Kind == GateCutKind.ClosesBeforeArrival)
                .Max(item => item.StartMs);
            text.Append(opensLate ? "The window also has to be " : "The window has to be ")
                .Append("long enough to reach ")
                .Append(one ? "it" : "them")
                .Append(": raise the plateau in Gate… past ")
                .Append($"{latest:0.00} ms, or take back the delay that pushes ")
                .Append(one ? "it" : "them")
                .Append(" out of the window. ");
        }

        text.Append("Each side keeps its own gate placement, so the one fitted on ")
            .Append(SideLabel)
            .Append(" says nothing about ")
            .Append(RightSide ? "L" : "R")
            .Append(" — switch sides and check it too.");
        return text.ToString();
    }

    private static string FormatLeadingEdgeLossDb(double lossDb) =>
        double.IsFinite(lossDb) ? $"{lossDb:+0.0;-0.0} dB" : "∞ (the window holds none of it)";
}
