using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What a side's magnitude curves span: the value axis range, and the deepest loss for the loss axis.</summary>
internal sealed record ScaleExtent(double LowDb, double HighDb, double? DeepestLossDb)
{
    public static ScaleExtent? Union(ScaleExtent? a, ScaleExtent? b) =>
        a == null ? b
        : b == null ? a
        : new(
            Lower(a.LowDb, b.LowDb),
            Higher(a.HighDb, b.HighDb),
            a.DeepestLossDb is { } x
                ? b.DeepestLossDb is { } y ? Math.Min(x, y) : x
                : b.DeepestLossDb);

    /// <summary>Finite levels of the curves, those on the loss axis apart; null when none has any.</summary>
    public static ScaleExtent? Of(IEnumerable<AcousticCurve> curves)
    {
        double low = double.NaN;
        double high = double.NaN;
        double? deepest = null;
        foreach (AcousticCurve curve in curves)
        {
            foreach (SignalPoint point in curve.Points)
            {
                if (!double.IsFinite(point.Y))
                {
                    continue;
                }

                if (curve.OnLossAxis)
                {
                    deepest = Math.Min(deepest ?? 0, point.Y);
                }
                else
                {
                    low = Lower(low, point.Y);
                    high = Higher(high, point.Y);
                }
            }
        }

        return double.IsNaN(low) && deepest == null ? null : new ScaleExtent(low, high, deepest);
    }

    /// <summary>Outward to whole steps, so two views' slightly different reads of one curve share limits.</summary>
    public (double Low, double High)? AxisRange(double stepDb, double floorDb, double ceilingDb)
    {
        if (double.IsNaN(LowDb) || double.IsNaN(HighDb))
        {
            return null;
        }

        double low = Math.Clamp(Math.Floor(LowDb / stepDb) * stepDb, floorDb, ceilingDb - stepDb);
        double high = Math.Clamp(Math.Ceiling(HighDb / stepDb) * stepDb, low + stepDb, ceilingDb);
        return (low, high);
    }

    private static double Lower(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Min(a, b);

    private static double Higher(double a, double b) => double.IsNaN(a) ? b : double.IsNaN(b) ? a : Math.Max(a, b);
}

/// <summary>One magnitude scale for both sides. See docs/tech/virtual-dsp-panel.md#one-scale-for-both-sides.</summary>
internal sealed class VirtualCrossoverSharedScale(
    VirtualCrossoverSession session,
    VirtualCrossoverProcessingCoordinator coordinator,
    VirtualCrossoverMetrics metrics,
    VirtualCrossoverHybrid hybridReader,
    AcousticViewBuilder viewBuilder)
{
    private readonly Dictionary<bool, (object Signature, ScaleExtent? Extent)> known = [];

    // What the last re-read measured, and its answer: an unchanged side (edits went to the shown one) is not read again.
    // The answer is kept apart from what the side remembered while shown, since that also spans the other side's
    // dashed sum as it was then.
    private (bool RightSide, object Signature, List<object> Inputs, ScaleExtent? Extent)? lastRead;

    /// <summary>A side's last extent, as long as it was taken under the view options now in force.</summary>
    public ScaleExtent? Known(bool rightSide, VirtualCrossoverViewState view) =>
        known.TryGetValue(rightSide, out var entry) && entry.Signature.Equals(Signature(view))
            ? entry.Extent
            : null;

    public void Remember(bool rightSide, VirtualCrossoverViewState view, ScaleExtent? extent) =>
        known[rightSide] = (Signature(view), extent);

    public void Forget()
    {
        known.Clear();
        lastRead = null;
    }

    /// <summary>The side not shown, read as it is drawn when shown. Not current once a newer frame started; a current read
    /// with no extent is a side with nothing to draw, which must replace what it drew before.</summary>
    public async Task<(bool Current, ScaleExtent? Extent)> MeasureOtherSideAsync(
        VirtualCrossoverViewState view, long revision)
    {
        bool rightSide = !view.RightSide;
        VirtualCrossoverSideSum? side = await metrics.ComputeSideSumAsync(
            session.Channels, rightSide, revision, minimumChannels: 1);
        if (!coordinator.IsCurrent(revision))
        {
            return (false, null);
        }

        var frame = side == null ? null : VirtualCrossoverFrame.Of(side.Channels, view.GroupView);
        if (frame == null || frame.Shown.Count == 0)
        {
            return (true, null);
        }

        object signature = Signature(view);
        List<object> inputs = Inputs(frame, rightSide);
        if (lastRead is { } last && last.RightSide == rightSide && last.Signature.Equals(signature) &&
            last.Inputs.SequenceEqual(inputs))
        {
            return (true, last.Extent);
        }

        int smoothing = session.MagnitudeGate.SmoothingInverseOctaves;
        VirtualCrossoverMetrics sideMetrics = VirtualCrossoverMetrics.Through(
            coordinator, () => session.MagnitudeGate, oppositeSide: true, session.Calibration.For);
        (List<AnalysisCurve>? magnitudes, AnalysisCurve? sum, List<SignalPoint>? loss) =
            sideMetrics.BuildCurves(frame.Shown, smoothing, frame.Summed);
        if (magnitudes == null)
        {
            return (true, null);
        }

        HybridMagnitudes? hybrid = view.HybridRequested
            ? hybridReader.Build(frame.Shown, magnitudes, rightSide, smoothing)
            : null;
        var curves = new List<AcousticCurve>();
        if (VirtualCrossoverGroupViews.DrawsGroupSums(view.GroupView))
        {
            curves.AddRange(viewBuilder.GroupSumCurves(frame.Shown, magnitudes, hybrid, view, oppositeSide: true));
        }
        else
        {
            for (int i = 0; i < frame.Shown.Count; i++)
            {
                ProcessedChannel item = frame.Shown[i];
                if (item.Channel.Pair.ShowRawCurve &&
                    item.Channel.SideState(rightSide) is { TransferImpulseResponse: { } ir } state)
                {
                    curves.Add(Level(session.MagnitudeGate.Raw(
                        ir, state.TransferPeakIndex, state.SampleRate, item.MeasuredBand,
                        session.Calibration.For(item)).Points));
                }

                if (item.Channel.Pair.ShowProcessedCurve)
                {
                    curves.Add(Level(hybrid != null
                        ? VirtualCrossoverHybrid.ShiftedBy(hybrid.Channels[i], hybrid.OffsetDb)
                        : magnitudes[i].Points));
                }
            }

            if (view.ShowSum && hybrid == null && sum != null)
            {
                curves.Add(Level(sum.Points));
            }
        }

        List<SignalPoint>? drawnLoss = !frame.QuotesJunctions ? null : view.LossWindow switch
        {
            SumLossWindow.Off => null,
            SumLossWindow.Direct => (await frame.ReadJunctionsAsync(
                sideMetrics,
                session.GateFor(rightSide),
                session.GateFor(rightSide).PinnedOffsetMs,
                smoothing,
                withDirectLoss: true)).DirectLoss,
            _ => loss
        };
        if (drawnLoss != null)
        {
            curves.Add(new AcousticCurve(string.Empty, drawnLoss, default, 0, default, OnLossAxis: true));
        }

        if (!coordinator.IsCurrent(revision))
        {
            return (false, null);
        }

        ScaleExtent? extent = ScaleExtent.Of(curves);
        lastRead = (rightSide, signature, inputs, extent);
        return (true, extent);
    }

    // The responses by identity (the coordinator hands an unchanged chain the same array), the rest by value.
    private List<object> Inputs(VirtualCrossoverFrame frame, bool rightSide)
    {
        var inputs = new List<object> { session.GateFor(rightSide).PinnedOffsetMs ?? double.NaN };
        foreach (ProcessedChannel item in frame.All)
        {
            VirtualCrossoverChannelState state = item.Channel.SideState(rightSide);
            inputs.Add(item.ImpulseResponse);
            inputs.Add(item.Channel.Pair.Zone);
            inputs.Add(item.Channel.Pair.ShowRawCurve);
            inputs.Add(item.Channel.Pair.ShowProcessedCurve);
            inputs.Add((object?)state.TransferImpulseResponse ?? string.Empty);
            inputs.Add((object?)state.SpatialAverageFor(session.SpatialAverageMode) ?? string.Empty);
        }

        return inputs;
    }

    private static AcousticCurve Level(IReadOnlyList<SignalPoint> points) =>
        new(string.Empty, points, default, 0, default);

    // What changes a curve's reach besides the responses. The gate's pins stay out: they swap places when the sides do.
    private object Signature(VirtualCrossoverViewState view) => (
        view.View,
        view.GroupView,
        view.HybridRequested,
        view.ShowSum,
        view.LossWindow,
        view.Target,
        view.TargetLevelDb,
        session.MagnitudeGate.Template,
        session.MagnitudeGate.SmoothingInverseOctaves,
        session.Calibration,
        session.SpatialAverageMode);
}
