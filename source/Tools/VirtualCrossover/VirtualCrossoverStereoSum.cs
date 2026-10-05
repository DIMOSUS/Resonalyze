using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>How the Front + Sub view adds the two sides into one L+R curve. See docs/tech/virtual-dsp-panel.md#lr-sum.</summary>
public enum StereoSumMode
{
    Off,

    /// <summary>Sum L + Sum R as vectors: one signal fed to both sides.</summary>
    Vector,

    /// <summary>|Sum L|² + |Sum R|²: uncorrelated signals in the two sides.</summary>
    Energy,

    /// <summary>Vector in the bass, where the sides stay coherent across the seat, Energy above, over one octave.</summary>
    Blend
}

public static class StereoSumModes
{
    public static readonly IReadOnlyList<StereoSumMode> All =
        [StereoSumMode.Off, StereoSumMode.Vector, StereoSumMode.Energy, StereoSumMode.Blend];

    public static string DisplayName(StereoSumMode mode) => mode switch
    {
        StereoSumMode.Vector => "Vector",
        StereoSumMode.Energy => "Energy",
        StereoSumMode.Blend => "Blend",
        _ => "Off"
    };

    public static bool Applies(VirtualCrossoverGroupView view) =>
        view == VirtualCrossoverGroupView.FrontAndSub;
}

/// <summary>What the L+R curve sums: the shown side's summing channels, then the opposite side's, through one window
/// that opens at the earlier side's. A mono block is in both, at its side share (docs/tech/virtual-dsp-panel.md#mono-side-share).</summary>
/// <param name="Groups">Each channel's side for the power sum; null for one vector sum.</param>
internal sealed record StereoSumParts(
    List<ProcessedChannel> Channels,
    int AnchorIndex,
    double GateOffsetMs,
    List<int>? Groups);

internal static class VirtualCrossoverStereoSum
{
    /// <param name="shown">The shown side's summing channels.</param>
    /// <returns>Null when off, when only one side has blocks of its own, or when the sides' rates differ.</returns>
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

        // Half an L+R when only one side has blocks of its own; mono blocks alone play for both sides.
        if (shown.Any(item => !item.Channel.Pair.Mono) != opposite.Channels.Any(item => !item.Channel.Pair.Mono))
        {
            return null;
        }

        int shownAnchor = ProcessedChannels.SharedStartAnchorIndex(shown);
        double gateOffsetMs = Math.Min(
            gate.ResolveGateOffsetMs(oppositeSide: false, shownAnchor, shown[0].SampleRate),
            gate.OppositeOffsetMs(opposite));
        List<int>? groups = mode == StereoSumMode.Energy
            ? [.. shown.Select(_ => 0), .. opposite.Channels.Select(_ => 1)]
            : null;
        return new StereoSumParts(
            [.. shown, .. opposite.Channels],
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
        Func<ProcessedChannel, CalibrationFile?> calibrationFor,
        double blendHz = VirtualCrossoverLimits.DefaultStereoBlendHz)
    {
        if (mode == StereoSumMode.Blend)
        {
            return Blended(
                Build(StereoSumMode.Vector, shown, opposite, gate, calibrationFor),
                Build(StereoSumMode.Energy, shown, opposite, gate, calibrationFor),
                blendHz);
        }

        return Parts(mode, shown, opposite, gate) is not { } parts
            ? null
            : gate.MeasuredSum(
                parts.Channels, parts.AnchorIndex, parts.GateOffsetMs, calibrationFor, parts.Groups).Display;
    }

    /// <summary>The Blend curve from its Vector and Energy curves; null unless both exist.</summary>
    public static AnalysisCurve? Blended(AnalysisCurve? vector, AnalysisCurve? energy, double blendHz) =>
        vector != null && energy != null
            ? vector with { Points = Blend(vector.Points, energy.Points, blendHz) }
            : null;

    /// <summary>Vector below, Energy above, their POWERS weighed across the octave centred on <paramref name="blendHz"/>:
    /// the L/R cross-term fades out, so a null between the sides fills in as coherence goes. Both curves are on one grid.</summary>
    public static List<SignalPoint> Blend(
        IReadOnlyList<SignalPoint> vector, IReadOnlyList<SignalPoint> energy, double blendHz)
    {
        int count = Math.Min(vector.Count, energy.Count);
        var points = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            double weight = Math.Clamp(Math.Log2(vector[i].X / blendHz) + 0.5, 0.0, 1.0);
            double level = weight switch
            {
                0.0 => vector[i].Y,
                1.0 => energy[i].Y,
                _ => 10.0 * Math.Log10(
                    (1.0 - weight) * Math.Pow(10.0, vector[i].Y / 10.0) +
                    weight * Math.Pow(10.0, energy[i].Y / 10.0))
            };
            points.Add(new SignalPoint(vector[i].X, level));
        }

        return points;
    }
}
