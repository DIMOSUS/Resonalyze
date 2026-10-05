using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A curve of the upper plot offered to a Frequency Response overlay slot.</summary>
/// <param name="Key">What the curve is, the same on both sides: the export remembers the last one by it.</param>
/// <param name="SmoothingCode">The plot's smoothing, already in <paramref name="Points"/>.</param>
internal sealed record VirtualCrossoverOverlayCurve(
    string Key,
    string Label,
    string Title,
    IReadOnlyList<SignalPoint> Points,
    int SmoothingCode);

/// <summary>Tools... → Capture to overlay: every curve the magnitude view draws, or would with its toggle on, for the
/// shown side and group view, built as the plot builds them.</summary>
internal sealed class VirtualCrossoverOverlayExport(
    VirtualCrossoverSession session,
    VirtualCrossoverMetrics metrics,
    VirtualCrossoverHybrid hybridReader,
    AcousticViewBuilder viewBuilder)
{
    public const string ShownSumKey = "sum";

    /// <param name="processed">The shown side's processed blocks, as the redraw holds them.</param>
    public async Task<List<VirtualCrossoverOverlayCurve>> CurvesAsync(
        IReadOnlyList<ProcessedChannel> processed, long revision, VirtualCrossoverViewState view)
    {
        var frame = VirtualCrossoverFrame.Of(processed, view.GroupView);
        if (frame.Shown.Count == 0)
        {
            return [];
        }

        bool groups = VirtualCrossoverGroupViews.DrawsGroupSums(view.GroupView);
        VirtualCrossoverSideSum? opposite = groups
            ? null
            : await metrics.ComputeSideSumAsync(
                session.Channels, !view.RightSide, revision, minimumChannels: 1,
                includePair: pair => VirtualCrossoverGroupViews.ParticipatesInTotalSum(view.GroupView, pair.Zone));
        int smoothing = session.MagnitudeGate.SmoothingInverseOctaves;
        (List<GatedMagnitude>? gated, AnalysisCurve? sum, _) =
            metrics.BuildGatedCurves(frame.Shown, smoothing, frame.Summed);
        if (gated == null)
        {
            return [];
        }

        List<AnalysisCurve> magnitudes = [.. gated.Select(curve => curve.Display)];
        HybridMagnitudes? hybrid = view.HybridRequested
            ? hybridReader.Build(
                frame.Shown, magnitudes, view.RightSide, smoothing, [.. gated.Select(curve => curve.Unsmoothed)])
            : null;
        string method = hybrid != null ? " hybrid" : string.Empty;
        string side = view.RightSide ? "R" : "L";
        var curves = new List<VirtualCrossoverOverlayCurve>();
        void Add(string key, string label, IReadOnlyList<SignalPoint> points, string? detail = null) =>
            curves.Add(new VirtualCrossoverOverlayCurve(
                key, label + method + (detail is { Length: > 0 } ? " — " + detail : string.Empty),
                "vDSP " + label + method, points, smoothing));

        if (groups)
        {
            // In the order GroupSumCurves draws them: one line per zone with a shown block.
            List<List<ProcessedChannel>> members =
            [
                .. VirtualCrossoverZones.All
                    .Select(zone => frame.Shown.Where(item => item.Channel.Pair.Zone == zone).ToList())
                    .Where(zone => zone.Count > 0)
            ];
            List<AcousticCurve> lines =
                viewBuilder.GroupSumCurves(frame.Shown, magnitudes, hybrid, view with { Target = null });
            foreach ((List<ProcessedChannel> zone, AcousticCurve line) in members.Zip(lines))
            {
                Add(
                    "group:" + zone[0].Channel.Pair.Zone,
                    $"{line.Title} {side} ({Names(zone)})",
                    line.Points);
            }

            return curves;
        }

        for (int i = 0; i < frame.Shown.Count; i++)
        {
            ProcessedChannel item = frame.Shown[i];
            Add(
                "block:" + item.Channel.Name,
                item.Channel.SideLabel(view.RightSide),
                AcousticViewBuilder.ProcessedPoints(i, magnitudes, hybrid),
                item.Settings.DisplayName);
        }

        if (sum != null)
        {
            Add(
                ShownSumKey,
                $"Sum {side} ({Names(frame.Summed)})",
                viewBuilder.SumPoints(frame.Shown, frame.Summed, magnitudes, hybrid, sum));
        }

        if (opposite != null && viewBuilder.OppositeSum(opposite, hybrid) is { } oppositeSum)
        {
            Add(
                "sum-opposite",
                $"Sum {(view.RightSide ? "L" : "R")} ({Names(opposite.Channels)})",
                oppositeSum.Points);
        }

        if (StereoSumModes.Applies(view.GroupView) && opposite != null)
        {
            string both = Names(frame.Summed.Concat(opposite.Channels));
            AnalysisCurve? vector =
                viewBuilder.StereoSum(StereoSumMode.Vector, view.StereoBlendHz, frame, opposite, magnitudes, hybrid);
            AnalysisCurve? energy =
                viewBuilder.StereoSum(StereoSumMode.Energy, view.StereoBlendHz, frame, opposite, magnitudes, hybrid);
            foreach ((StereoSumMode mode, AnalysisCurve? stereo) in new[]
                     {
                         (StereoSumMode.Vector, vector),
                         (StereoSumMode.Energy, energy),
                         (StereoSumMode.Blend, VirtualCrossoverStereoSum.Blended(vector, energy, view.StereoBlendHz))
                     })
            {
                if (stereo == null)
                {
                    continue;
                }

                string name = StereoSumModes.DisplayName(mode).ToLowerInvariant();
                Add(
                    "stereo:" + mode,
                    mode == StereoSumMode.Blend
                        ? $"L+R {name} {view.StereoBlendHz:0} Hz ({both})"
                        : $"L+R {name} ({both})",
                    stereo.Points);
            }
        }

        return curves;
    }

    /// <summary>A free slot, else the one holding this curve's earlier capture, so it can be renewed; else none. A target
    /// or calculated slot is never offered, whatever its name.</summary>
    public static int? DefaultSlot(IReadOnlyList<OverlaySlotOccupant> slots, string title) =>
        slots.FirstOrDefault(slot => slot.IsFree)?.Slot ??
        slots.FirstOrDefault(slot => slot.Kind == OverlayKind.Captured && slot.Title == title)?.Slot;

    public static string SlotLabel(OverlaySlotOccupant slot) =>
        slot.Unreadable ? $"{slot.Slot}: unreadable file"
        : slot.Title is not { } title ? $"{slot.Slot}: free"
        : $"{slot.Slot}: {OverlaySlotName.Shorten(title, slot.Slot)}" + slot.Kind switch
        {
            OverlayKind.Operation => " (calculated)",
            OverlayKind.Target => " (target)",
            _ => string.Empty
        };

    // A mono block counts once in L+R.
    private static string Names(IEnumerable<ProcessedChannel> channels) =>
        string.Join("+", channels.Select(item => item.Channel.Name).Distinct());
}
