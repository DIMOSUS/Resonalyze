using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze;

/// <summary>What the upper plot is asked to show, read off the controls once per frame.</summary>
/// <param name="Target">The EQ target to hang on the Magnitude and Groups views; null when it is not shown.</param>
internal sealed record VirtualCrossoverViewState(
    AcousticView View,
    VirtualCrossoverGroupView GroupView,
    bool RightSide,
    bool ShowSum,
    SumLossWindow LossWindow,
    bool HybridRequested,
    EqTargetCurve? Target,
    double TargetLevelDb);

/// <summary>The magnitude read-outs one frame already computed, handed to the views that draw them.</summary>
internal sealed record AcousticFrameCurves(
    List<AnalysisCurve>? Magnitudes,
    AnalysisCurve? Sum,
    List<SignalPoint>? DrawnLoss,
    bool LossDirect,
    AnalysisCurve? OppositeSum,
    VirtualCrossoverSideSum? OppositeSide,
    HybridMagnitudes? Hybrid);

/// <summary>Builds the upper plot's five views and the Groups lines from a processed frame. UI-free: the panel hands it the
/// view state and draws what comes back.</summary>
internal sealed class AcousticViewBuilder(VirtualCrossoverSession session, VirtualCrossoverHybrid hybridReader)
{
    public const string NoSourcesHint =
        "Pick a measurement for at least one channel (Source...).\n" +
        "Every source needs a loopback transfer IR recorded at the same\n" +
        "microphone position and sample rate.";

    public const string LoadingHint = "Loading the previous session…";

    // A target is parametric, so it spans the audio band on its own grid.
    private const double TargetGridLowHz = 20;
    private const double TargetGridHighHz = 20_000;
    private const int TargetGridPoints = 512;

    public static string EmptyViewHint(VirtualCrossoverGroupView view) =>
        $"No channels in {VirtualCrossoverGroupViews.DisplayName(view)}." +
        Environment.NewLine +
        "Set a block's Zone to bring it into this view.";

    // While a session loads, processed is empty; keep the loading note instead of the no-sources hint.
    public AcousticRender Build(
        VirtualCrossoverViewState view,
        VirtualCrossoverFrame frame,
        AcousticFrameCurves curves,
        bool loading)
    {
        List<ProcessedChannel> shown = frame.Shown;
        string hint = loading
            ? LoadingHint
            : shown.Count == 0 ? NoSourcesHint : string.Empty;
        if (shown.Count == 0)
        {
            return new AcousticRender(hint, [], null);
        }

        return view.View switch
        {
            // Drawn set for traces, summed subset for the Sum, matching the magnitude view.
            AcousticView.Phase => new AcousticRender(
                hint, PhaseCurves(shown, frame.Summed, view.ShowSum), null),
            AcousticView.GroupDelay => new AcousticRender(
                hint, GroupDelayCurves(shown, frame.Summed, view.ShowSum), null),
            AcousticView.Impulse => new AcousticRender(
                hint, [], TraceRender(shown, step: false, frame.Summed, null, view.ShowSum, view.RightSide)),
            AcousticView.Step => new AcousticRender(
                hint, [], TraceRender(
                    shown, step: true, frame.Summed, curves.OppositeSide, view.ShowSum, view.RightSide)),
            _ when VirtualCrossoverGroupViews.DrawsGroupSums(view.GroupView) => new AcousticRender(
                hint, GroupSumCurves(shown, curves.Magnitudes, curves.Hybrid, view), null),
            _ => new AcousticRender(hint, MagnitudeCurves(shown, curves, view), null)
        };
    }

    private List<AcousticCurve> MagnitudeCurves(
        List<ProcessedChannel> processed,
        AcousticFrameCurves frame,
        VirtualCrossoverViewState view)
    {
        // A shown RAW curve is built here per channel; processed ones arrive prebuilt.
        using var _ = AppProfiler.Zone("VirtualDSP.BuildMagnitudeCurves");
        List<AnalysisCurve>? magnitudes = frame.Magnitudes;
        HybridMagnitudes? hybrid = frame.Hybrid;
        var curves = new List<AcousticCurve>();
        // First, so the curves draw on top of it.
        if (TargetCurve(view) is { } target)
        {
            curves.Add(target);
        }

        for (int i = 0; i < processed.Count; i++)
        {
            ProcessedChannel item = processed[i];
            if (item.Channel.Pair.ShowRawCurve)
            {
                AnalysisCurve raw = session.MagnitudeGate.Raw(
                    item.Channel.TransferImpulseResponse!,
                    item.Channel.TransferPeakIndex,
                    item.Channel.SampleRate,
                    item.MeasuredBand,
                    session.Calibration.For(item));
                curves.Add(new AcousticCurve(
                    $"{item.Channel.Name} raw",
                    raw.Points,
                    OxyColor.FromAColor(90, item.Color),
                    1.2,
                    LineStyle.Solid));
            }

            if (item.Channel.Pair.ShowProcessedCurve)
            {
                // Non-null here: magnitudes are withheld only for an empty set.
                AnalysisCurve curve = magnitudes![i];
                IReadOnlyList<SignalPoint> points = hybrid != null
                    ? VirtualCrossoverHybrid.ShiftedBy(hybrid.Channels[i], hybrid.OffsetDb)
                    : curve.Points;
                curves.Add(new AcousticCurve(
                    item.Channel.Name, points, item.Color, 1.8, LineStyle.Solid));
            }
        }

        if (magnitudes == null || frame.Sum == null)
        {
            return curves;
        }

        if (view.ShowSum)
        {
            // Hybrid: averages hold no phase, so cancellation comes from the IR loss curve (VirtualCrossoverHybrid.Sum).
            IReadOnlyList<SignalPoint> sumPoints =
                (hybrid != null ? hybridReader.ActiveSum(processed, magnitudes, hybrid) : null)
                ?? frame.Sum.Points;
            curves.Add(new AcousticCurve(
                "Sum", sumPoints, VirtualCrossoverColors.Sum, 2.4, LineStyle.Solid));
            if (frame.OppositeSum != null)
            {
                curves.Add(new AcousticCurve(
                    OppositeSumTitle(view.RightSide),
                    frame.OppositeSum.Points,
                    OxyColor.FromAColor(110, VirtualCrossoverColors.Sum),
                    1.8,
                    LineStyle.Dash));
            }
        }

        if (frame.DrawnLoss != null)
        {
            // Complex sum vs phase-blind magnitude sum (<= 0), from UNSMOOTHED magnitudes, smoothed as a ratio; the same list
            // the read-out averages. On the loss axis. Null under Disable; under FDW-8 the direct-sound loss.
            curves.Add(new AcousticCurve(
                frame.LossDirect ? "Sum loss (direct)" : "Sum loss",
                frame.DrawnLoss, VirtualCrossoverColors.Loss, 1.8, LineStyle.Dash, OnLossAxis: true));
        }

        return curves;
    }

    // One summed line per zone, all gated on ONE anchor across the shown channels.
    // See docs/tech/virtual-dsp-panel.md#groups-view.
    /// <param name="oppositeSide">Windowed through the other side's gate placement, for the shared scale.</param>
    internal List<AcousticCurve> GroupSumCurves(
        List<ProcessedChannel> shown,
        IReadOnlyList<AnalysisCurve>? magnitudes,
        HybridMagnitudes? hybrid,
        VirtualCrossoverViewState view,
        bool oppositeSide = false)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildGroupSumCurves");
        int anchor = ProcessedChannels.SharedStartAnchorIndex(shown);
        MagnitudeGateSnapshot snapshot = session.MagnitudeGate;
        double gateOffsetMs = snapshot.ResolveGateOffsetMs(
            oppositeSide, anchor, shown[0].SampleRate);
        // Check every list the slice indexes: the slice runs before VirtualCrossoverHybrid.Sum's own guard.
        bool drawHybrid = hybrid != null && magnitudes != null &&
            magnitudes.Count >= shown.Count &&
            hybrid.Channels.Count >= shown.Count &&
            hybrid.UnsmoothedChannels.Count >= shown.Count &&
            hybrid.ChannelOffsetsDb.Count >= shown.Count;
        var curves = new List<AcousticCurve>();
        foreach (VirtualCrossoverZone zone in VirtualCrossoverZones.All)
        {
            // By position: hybrid curves and magnitudes are indexed against the shown set.
            List<int> positions =
            [
                .. Enumerable.Range(0, shown.Count)
                    .Where(index => shown[index].Channel.Pair.Zone == zone)
            ];
            if (positions.Count == 0)
            {
                continue;
            }

            List<ProcessedChannel> members = [.. positions.Select(index => shown[index])];
            List<SignalPoint>? points = drawHybrid
                ? VirtualCrossoverHybrid.Sum(
                    hybrid!.Subset(positions),
                    members,
                    anchor,
                    snapshot,
                    gateOffsetMs,
                    [
                        .. positions.Select(index =>
                            (IReadOnlyList<SignalPoint>)magnitudes![index].Points)
                    ])
                : null;
            curves.Add(new AcousticCurve(
                VirtualCrossoverZones.DisplayName(zone),
                points ?? snapshot.MeasuredSum(
                    members,
                    anchor,
                    snapshot.ResolveGateOffsetMs(oppositeSide, anchor, members[0].SampleRate),
                    session.Calibration.For).Display.Points,
                VirtualCrossoverColors.Group(zone),
                2.0,
                LineStyle.Solid));
        }

        if (TargetCurve(view) is { } target)
        {
            curves.Insert(0, target);
        }

        return curves;
    }

    // Hung at the user's level, not fitted: transfer-function dB has no absolute reference to fit.
    private static AcousticCurve? TargetCurve(VirtualCrossoverViewState view)
    {
        if (view.Target is not { } target)
        {
            return null;
        }

        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(
            TargetGridLowHz, TargetGridHighHz, TargetGridPoints);
        var points = new SignalPoint[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            points[i] = new SignalPoint(
                grid[i], view.TargetLevelDb + target.Spec.Evaluate(grid[i]));
        }

        return new AcousticCurve(
            "Target",
            points,
            OxyColor.FromArgb(
                target.Color.A, target.Color.R, target.Color.G, target.Color.B),
            target.StrokeThickness,
            OverlayLineStyles.ToOxy(target.LineStyle));
    }

    // Spectra are gated ONCE per frame for curves and Sum; the Sum takes every SUMMING channel, hidden or not, as the
    // magnitude Sum does.
    private static (bool IncludeSum, List<ProcessedChannel> Gated) GatedSet(
        List<ProcessedChannel> processed,
        IReadOnlyList<ProcessedChannel> summed,
        bool showSum)
    {
        bool includeSum = summed.Count >= 2 && showSum;
        return (
            includeSum,
            [.. processed.Where(item => (includeSum && summed.Contains(item)) ||
                item.Channel.Pair.ShowProcessedCurve)]);
    }

    internal List<AcousticCurve> PhaseCurves(
        List<ProcessedChannel> processed,
        IReadOnlyList<ProcessedChannel> summed,
        bool showSum)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildPhaseCurves");
        VirtualCrossoverPhaseGate gate = session.Gate;
        // One shared absolute τ keeps relative phase; windows may follow each channel's arrival because BuildMeasuredPhase
        // re-references to τ (exact while no window cuts its own channel, enforced by VirtualCrossoverPhaseGate.PerCurveOffsets).
        int sampleRate = processed[0].SampleRate;
        double referenceOffsetMs = gate.ReferenceOffsetMs(processed, sampleRate);
        double detrendMs = gate.CommonDetrendMs(processed, referenceOffsetMs, sampleRate);
        (bool includeSum, List<ProcessedChannel> gatedChannels) =
            GatedSet(processed, summed, showSum);

        // Placements over the gated set only.
        List<double> offsets = gate.PerCurveOffsets(gatedChannels, referenceOffsetMs, sampleRate);
        double referenceSamples = detrendMs * sampleRate / 1_000.0;

        List<(ProcessedChannel Item, Complex[] Spectrum, int ExtractionStart)> gated =
            gatedChannels
                .Select((item, index) => (item, Settings: gate.Settings(
                    offsets[index], PhaseDetrendMode.Manual, detrendMs)))
                .AsParallel()
                .AsOrdered()
                .Select(input =>
                {
                    Complex[] spectrum = DataHelper.GetPhaseAnalysisSpectrum(
                        new ImpulseMeasurementView(
                            input.item.ImpulseResponse, 0, sampleRate),
                        input.Settings,
                        out int extractionStart);
                    return (input.item, spectrum, extractionStart);
                })
                .ToList();

        var jobs = new List<(string Title, OxyColor Color, double Thickness,
            Complex[] Spectrum, int ExtractionStart)>();
        foreach ((ProcessedChannel item, Complex[] spectrum, int extractionStart)
            in gated)
        {
            if (item.Channel.Pair.ShowProcessedCurve)
            {
                jobs.Add((
                    item.Channel.Name, item.Color, 1.8, spectrum, extractionStart));
            }
        }

        if (includeSum)
        {
            // Vector sum of individually gated SPECTRA, not a gate over the summed IR (FDW HF windows < arrival spread).
            List<(ProcessedChannel Item, Complex[] Spectrum, int ExtractionStart)>
                summedParts = [.. gated.Where(part => summed.Contains(part.Item))];
            if (summedParts.Count >= 2)
            {
                int targetExtractionStart =
                    summedParts.Min(part => part.ExtractionStart);
                Complex[] combined = DataHelper.SumGatedSpectra(
                    [.. summedParts.Select(part => (part.Spectrum, part.ExtractionStart))],
                    targetExtractionStart);
                jobs.Add(("Sum", VirtualCrossoverColors.Sum, 2.4, combined, targetExtractionStart));
            }
        }

        return jobs
            .AsParallel()
            .AsOrdered()
            .SelectMany(job =>
            {
                GatedPhaseCurve curve = GatedPhaseCurves.Read(
                    job.Spectrum,
                    job.ExtractionStart,
                    referenceSamples,
                    sampleRate,
                    job.Title,
                    job.Color,
                    job.Thickness);
                var curves = new List<AcousticCurve>(2);
                // Wrap verticals: faded, dashed, drawn under the curve; empty title keeps them out of plot labels.
                if (curve.WrapSegments.Count > 0)
                {
                    curves.Add(new AcousticCurve(
                        string.Empty,
                        curve.WrapSegments,
                        OxyColor.FromAColor(110, job.Color),
                        job.Thickness * 0.4,
                        LineStyle.Dash));
                }
                curves.Add(new AcousticCurve(
                    curve.Title,
                    curve.Points,
                    job.Color,
                    job.Thickness,
                    LineStyle.Solid));
                return curves;
            })
            .ToList();
    }

    // Same window and placement as the phase view; absolute ms, no detrend. Plain GD and the Sum only.
    // See docs/tech/virtual-dsp-panel.md#group-delay-view.
    internal List<AcousticCurve> GroupDelayCurves(
        List<ProcessedChannel> processed,
        IReadOnlyList<ProcessedChannel> summed,
        bool showSum)
    {
        using var _ = AppProfiler.Zone("VirtualDSP.BuildGroupDelayCurves");
        VirtualCrossoverPhaseGate gate = session.Gate;
        int sampleRate = processed[0].SampleRate;
        double referenceOffsetMs = gate.ReferenceOffsetMs(processed, sampleRate);
        (bool includeSum, List<ProcessedChannel> gatedChannels) =
            GatedSet(processed, summed, showSum);
        if (gatedChannels.Count == 0)
        {
            return [];
        }

        List<double> offsets = gate.PerCurveOffsets(gatedChannels, referenceOffsetMs, sampleRate);
        // Psychoacoustic smoothing is a level model: it reads as 1/12 octave here (what the AI diagnostic reads). The
        // code, not the stored width: the project stores psychoacoustic as base width plus a flag.
        double smoothingInverseOctaves =
            SpectrumSmoothing.IsPsychoacoustic(session.Project.SmoothingCode)
                ? FrequencyResponseOptions.DefaultGroupDelaySmoothingInverseOctaves
                : session.Project.SmoothingCode;
        List<(ProcessedChannel Item, PhaseAnalysisSettings Settings)> inputs = gatedChannels
            .Select((item, index) => (item, gate.Settings(
                offsets[index], PhaseDetrendMode.Off, manualDetrendMilliseconds: 0.0)))
            .ToList();

        List<(ProcessedChannel Item, PhaseAnalysisSettings Settings,
            GroupDelaySpectra Spectra, int ExtractionStart)> gated = inputs
            .AsParallel()
            .AsOrdered()
            .Select(input =>
            {
                GroupDelaySpectra spectra = DataHelper.GetGroupDelayAnalysisSpectra(
                    new ImpulseMeasurementView(input.Item.ImpulseResponse, 0, sampleRate),
                    input.Settings,
                    out int extractionStart);
                return (input.Item, input.Settings, spectra, extractionStart);
            })
            .ToList();

        var jobs = new List<(string Title, OxyColor Color, double Thickness,
            GroupDelaySpectra Spectra, int ExtractionStart, PhaseAnalysisSettings Settings,
            MeasuredBand Band, IReadOnlyList<ProcessedChannel>? MaskBy)>();
        foreach ((ProcessedChannel item, PhaseAnalysisSettings settings,
            GroupDelaySpectra spectra, int extractionStart) in gated)
        {
            if (item.Channel.Pair.ShowProcessedCurve)
            {
                jobs.Add((item.Channel.Name, item.Color, 1.8, spectra, extractionStart,
                    settings, item.MeasuredBand, null));
            }
        }

        if (includeSum)
        {
            // Sum of individually gated operand pairs re-referenced to one extraction start (as in the phase view).
            List<(ProcessedChannel Item, PhaseAnalysisSettings Settings,
                GroupDelaySpectra Spectra, int ExtractionStart)> summedParts =
                [.. gated.Where(part => summed.Contains(part.Item))];
            if (summedParts.Count >= 2)
            {
                int targetExtractionStart =
                    summedParts.Min(part => part.ExtractionStart);
                GroupDelaySpectra combined = DataHelper.SumGatedSpectraPairs(
                    [.. summedParts.Select(part => (part.Spectra, part.ExtractionStart))],
                    targetExtractionStart,
                    sampleRate);
                // Masked where no channel measured, like the magnitude Sum.
                jobs.Add(("Sum", VirtualCrossoverColors.Sum, 2.4, combined, targetExtractionStart,
                    summedParts[0].Settings, MeasuredBand.Everything,
                    [.. summedParts.Select(part => part.Item)]));
            }
        }

        // The Group Delay mode's validity gate blanks a crossover's stop band on purpose.
        return jobs
            .AsParallel()
            .AsOrdered()
            .Select(job =>
            {
                GroupDelayCurveSet curves = DataHelper.GetGroupDelayCurves(
                    job.Spectra,
                    job.ExtractionStart,
                    sampleRate,
                    job.Settings,
                    smoothingInverseOctaves,
                    PlotModelFactory.GroupDelayMagnitudeGateDb,
                    includeMinimumPhase: false,
                    job.Band.LowEdgeHz,
                    job.Band.HighEdgeHz);
                IReadOnlyList<SignalPoint> points = job.MaskBy == null
                    ? curves.Measured.Points
                    : ProcessedChannels.MeasuredBySomeChannel(curves.Measured.Points, job.MaskBy);
                return new AcousticCurve(
                    job.Title, points, job.Color, job.Thickness, LineStyle.Solid);
            })
            .ToList();
    }

    // The gate dialog's IR preview promoted to the main plot: the Impulse view normalizes each trace to its envelope's
    // in-window peak; the Step view draws all on one common scale, the Sum being the sample-wise sum of the SUMMING
    // channels' IRs (hidden too), so its step is the sum of steps. The opposite Sum needs the shown side's rate.
    // See docs/tech/virtual-dsp-panel.md#step-view.
    internal AcousticImpulseRender? TraceRender(
        List<ProcessedChannel> processed,
        bool step,
        IReadOnlyList<ProcessedChannel> summed,
        VirtualCrossoverSideSum? oppositeSide,
        bool showSum,
        bool rightSide)
    {
        using var _ = AppProfiler.Zone(step ? "VirtualDSP.BuildStepRender" : "VirtualDSP.BuildImpulseRender");
        // Only shown traces set the gate offset and axis window.
        List<ProcessedChannel> shown = processed
            .Where(item => item.Channel.Pair.ShowProcessedCurve)
            .ToList();
        if (shown.Count == 0)
        {
            return null;
        }

        VirtualCrossoverPhaseGate gate = session.Gate;
        int sampleRate = shown[0].SampleRate;
        var traces = shown
            .Select(item => new IrPreviewTrace(
                item.ImpulseResponse,
                item.Channel.Name,
                item.Color))
            .ToList();
        if (step && summed.Count >= 2 && showSum)
        {
            traces.Add(new IrPreviewTrace(
                VirtualCrossoverAnalysis.SumImpulseResponses(
                    [.. summed.Select(item => item.ImpulseResponse)]),
                "Sum",
                VirtualCrossoverColors.Sum,
                2.4));
            if (oppositeSide != null && oppositeSide.SampleRate == sampleRate)
            {
                traces.Add(new IrPreviewTrace(
                    oppositeSide.ImpulseResponse,
                    OppositeSumTitle(rightSide),
                    OxyColor.FromAColor(110, VirtualCrossoverColors.Sum),
                    1.0,
                    LineStyle.Dash));
            }
        }

        return new AcousticImpulseRender(
            traces,
            sampleRate,
            gate.ReferenceOffsetMs(shown, sampleRate),
            gate.LeftMs,
            gate.PlateauMs,
            gate.RightMs,
            Step: step);
    }

    private static string OppositeSumTitle(bool rightSide) => $"Sum {(rightSide ? "L" : "R")}";
}
