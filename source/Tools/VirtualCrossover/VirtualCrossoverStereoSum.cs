using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>How the Front + Sub view adds the two sides into one L+R curve. See docs/tech/virtual-dsp-panel.md#lr-sum.</summary>
public enum StereoSumMode
{
    Off,

    /// <summary>Complex sum of both sides' channels, a mono channel once.</summary>
    Vector,

    /// <summary>|Σ front L|² + |Σ front R|² + |Σ sub|²: each group a vector sum, the groups added by power.</summary>
    Energy
}

public static class StereoSumModes
{
    public static readonly IReadOnlyList<StereoSumMode> All =
        [StereoSumMode.Off, StereoSumMode.Vector, StereoSumMode.Energy];

    public static string DisplayName(StereoSumMode mode) => mode switch
    {
        StereoSumMode.Vector => "Vector",
        StereoSumMode.Energy => "Energy",
        _ => "Off"
    };

    public static bool Applies(VirtualCrossoverGroupView view) =>
        view == VirtualCrossoverGroupView.FrontAndSub;
}

/// <summary>What the L+R curve sums: the shown side's summing channels, then the opposite side's own ones, through one
/// window that opens at the earlier side's.</summary>
/// <param name="OppositePositions">Where each opposite channel sits in <see cref="VirtualCrossoverSideSum.Channels"/>.</param>
/// <param name="Groups">Each channel's power-sum group; null for one vector sum.</param>
internal sealed record StereoSumParts(
    List<ProcessedChannel> Channels,
    int ShownCount,
    List<int> OppositePositions,
    int AnchorIndex,
    double GateOffsetMs,
    List<int>? Groups);

internal static class VirtualCrossoverStereoSum
{
    private const int SubGroup = 2;

    /// <param name="shown">The shown side's summing channels.</param>
    /// <returns>Null when off, when the other side adds nothing of its own, or when the sides' rates differ.</returns>
    public static StereoSumParts? Parts(
        StereoSumMode mode,
        IReadOnlyList<ProcessedChannel> shown,
        VirtualCrossoverSideSum? opposite,
        MagnitudeGateSnapshot gate)
    {
        if (mode == StereoSumMode.Off ||
            shown.Count == 0 ||
            opposite == null ||
            opposite.SampleRate != shown[0].SampleRate)
        {
            return null;
        }

        // A mono channel is one response in both sides' lists: it plays once.
        List<int> positions = [.. Enumerable.Range(0, opposite.Channels.Count)
            .Where(index => !opposite.Channels[index].Channel.Pair.Mono)];
        if (positions.Count == 0)
        {
            return null;
        }

        List<ProcessedChannel> others = [.. positions.Select(index => opposite.Channels[index])];
        int shownAnchor = ProcessedChannels.SharedStartAnchorIndex(shown);
        double gateOffsetMs = Math.Min(
            gate.ResolveGateOffsetMs(oppositeSide: false, shownAnchor, shown[0].SampleRate),
            gate.OppositeOffsetMs(opposite));
        List<int>? groups = mode == StereoSumMode.Energy
            ? [.. shown.Select(item => GroupOf(item, side: 0)), .. others.Select(item => GroupOf(item, side: 1))]
            : null;
        return new StereoSumParts(
            [.. shown, .. others],
            shown.Count,
            positions,
            Math.Min(shownAnchor, opposite.AnchorIndex),
            gateOffsetMs,
            groups);
    }

    /// <summary>The L+R curve from the impulse responses.</summary>
    public static AnalysisCurve? Build(
        StereoSumMode mode,
        IReadOnlyList<ProcessedChannel> shown,
        VirtualCrossoverSideSum? opposite,
        MagnitudeGateSnapshot gate,
        Func<ProcessedChannel, CalibrationFile?> calibrationFor) =>
        Parts(mode, shown, opposite, gate) is not { } parts
            ? null
            : gate.MeasuredSum(
                parts.Channels, parts.AnchorIndex, parts.GateOffsetMs, calibrationFor, parts.Groups).Display;

    // Stereo subs share one group: they play the same bass and add as vectors.
    private static int GroupOf(ProcessedChannel item, int side) =>
        item.Channel.Pair.Zone == VirtualCrossoverZone.Sub ? SubGroup : side;
}
