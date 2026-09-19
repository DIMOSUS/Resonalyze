using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The phase view's rules: the context a source opens with, a gate edited in the dialog, and what one render reads.
/// The edited channel's measured phase against a handoff's neighbours is the only view where all-pass bands show.
/// See docs/tech/eq-auto-tuner.md#phase-mode.
/// </summary>
internal static class EqWizardPhase
{
    /// <summary>A handoff brings its chain; a measurement opened directly uses the identity (the bank is everything).</summary>
    public static (Complex[] Response, DspChannelChain Chain)? SourceFor(EqWizardCurveSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Complex[]? response =
            source.PreviewImpulseResponse ?? source.Measurement?.ImpulseResponse;
        return response == null
            ? null
            : (response, source.PreviewChain ?? DspChannelChain.Identity);
    }

    /// <summary>
    /// A handoff's context as-is (resolved over every driver), or one built for a lone IR whose window and τ open on its
    /// own front; null for a magnitude-only source. Pinned says an absolute window was placed by hand.
    /// </summary>
    public static (EqWizardPhaseContext? Context, bool Pinned) Seed(EqWizardCurveSource? source)
    {
        if (source is not { Measurement: { } measurement } ||
            SourceFor(source) is not { } phaseSource)
        {
            return (null, false);
        }

        if (source.PhaseContext is { } handed)
        {
            // An absolute window placed by hand must not read as Auto here.
            return (handed, handed.PinnedOffset);
        }

        double startMs = ProcessedChannels.StartAnchorIndex(
            phaseSource.Response,
            measurement.PeakIndex,
            measurement.SampleRate) * 1_000.0 / measurement.SampleRate;
        return (new EqWizardPhaseContext(
            new PhaseAnalysisSettings(
                PhaseWindowMode.Fixed,
                PhaseAnalysisSettings.DefaultFdwCycles,
                PhaseDetrendMode.Manual,
                ManualDetrendMilliseconds: startMs,
                GateOffsetMs: startMs,
                FrequencyResponseOptions.DefaultPhaseLeftMs,
                FrequencyResponseOptions.DefaultPhasePlateauMs,
                FrequencyResponseOptions.DefaultPhaseRightMs,
                Unwrap: false,
                SmoothingInverseOctaves: 0.0),
            startMs,
            startMs,
            PinnedOffset: false,
            new PlacementChannel(
                phaseSource.Response, measurement.PeakIndex, default),
            measurement.SampleRate,
            EqWizardPhaseRender.EditedChannelColor,
            []), false);
    }

    /// <summary>
    /// A gate edited from the one the dialog opened on. Pinned: one absolute window for every curve; unpinned: each on its
    /// driver's arrival. Re-resolved with the Virtual DSP panel's arithmetic: per-curve placement depends on the lengths.
    /// </summary>
    public static EqWizardPhaseContext ApplyGate(
        EqWizardPhaseContext opened,
        double offsetMs,
        bool autoOffset,
        double leftMs,
        double plateauMs,
        double rightMs,
        PhaseWindowMode windowMode,
        int fdwCycles,
        PhaseDetrendMode detrendMode,
        double detrendMs)
    {
        ArgumentNullException.ThrowIfNull(opened);
        bool pinned = !autoOffset;
        PhaseAnalysisSettings gate = opened.Gate with
        {
            LeftMs = leftMs,
            PlateauMs = plateauMs,
            RightMs = rightMs,
            WindowMode = windowMode,
            FdwCycles = fdwCycles,
            DetrendMode = detrendMode
        };
        IReadOnlyList<PlacementChannel> set = opened.PlacementSet;
        double sharedOffsetMs = PhaseGatePlacement.ResolveSharedOffsetMs(
            set, opened.SampleRate, pinned ? offsetMs : null);
        List<double> offsets = PhaseGatePlacement.ResolvePerCurveOffsets(
            set,
            sharedOffsetMs,
            opened.SampleRate,
            pinned ? offsetMs : null,
            leftMs,
            plateauMs,
            rightMs);

        return new EqWizardPhaseContext(
            gate,
            offsets[0],
            // Same helper as the panel, over the window just resolved (not the offsets being replaced).
            PhaseGatePlacement.ResolveCommonDetrendMs(
                set,
                opened.SampleRate,
                gate with { GateOffsetMs = sharedOffsetMs },
                detrendMode,
                detrendMs),
            pinned,
            opened.Channel,
            opened.SampleRate,
            opened.ChannelColor,
            opened.Neighbours
                .Select((neighbour, index) => neighbour with
                {
                    GateOffsetMs = offsets[index + 1]
                })
                .ToList());
    }

    /// <summary>
    /// Auto snaps to the set's earliest front, read from the RESPONSES: under a pin the offsets are one absolute time and
    /// would make the dialog's preview disagree with the plot.
    /// </summary>
    public static double AutoGateFitOffsetMs(EqWizardPhaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return PhaseGatePlacement.EarliestStartMs(context.PlacementSet, context.SampleRate);
    }

    /// <summary>Everything one render needs, so the worker touches neither the session nor a control.</summary>
    public static EqWizardPhaseRequest Request(
        EqWizardCurveSource source,
        EqWizardPhaseContext context,
        EqualizationCurve? bank,
        int processorSampleRateHz)
    {
        (Complex[] response, DspChannelChain chain) = SourceFor(source)!.Value;
        return new EqWizardPhaseRequest(
            response,
            chain,
            bank,
            context.GateOffsetMs,
            context.Neighbours,
            context.Gate,
            context.DetrendMs,
            source.Measurement!.SampleRate,
            processorSampleRateHz);
    }

    /// <summary>The edited channel through its chain and this bank, as the gate dialog previews it.</summary>
    public static Complex[] EditedResponse(
        EqWizardCurveSource source,
        EqualizationCurve bank,
        int processorSampleRateHz)
    {
        (Complex[] response, DspChannelChain chain) = SourceFor(source)!.Value;
        return VirtualCrossoverAnalysis.ApplyChain(
            response,
            chain with { Peq = bank },
            source.Measurement!.SampleRate,
            processorSampleRateHz);
    }

    /// <summary>Why the phase view shows less than a chain handoff would; empty when nothing is missing.</summary>
    public static string Hint(EqWizardCurveSource? source, EqWizardPhaseContext? context)
    {
        if (source == null)
        {
            return string.Empty;
        }

        if (context == null)
        {
            return "This source is a magnitude curve — it carries no phase.\n" +
                "Only the EQ's own phase is drawn.";
        }

        return source.Kind == EqWizardSourceKind.VirtualDspChannel &&
            source.PhaseContext == null
            ? "Raw handoff: the neighbouring drivers are not drawn.\n" +
                "This curve has no crossover, delay or polarity in front of it and\n" +
                "they do, so lining it up against them would line up a system that\n" +
                "does not exist. Use Edit in EQ Wizard for junction work."
            : string.Empty;
    }
}
