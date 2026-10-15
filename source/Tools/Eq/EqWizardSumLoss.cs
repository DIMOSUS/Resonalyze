using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The channels the handed-over side sums with, frozen at handoff as PROCESSED responses, and the window the
/// panel's Sum loss was shown through (Disable reads as Full). See docs/tech/eq-auto-tuner.md#sum-loss.</summary>
internal sealed record EqWizardSumLossContext(
    IReadOnlyList<SumLossMember> Others,
    SumLossWindow Window)
{
    public string Title => Window == SumLossWindow.Direct ? "Sum loss (direct)" : "Sum loss";
}

/// <summary>The junction phase gate a direct-window loss reads through; null renders the full window.</summary>
internal sealed record EqWizardDirectLossGate(
    double? PinnedOffsetMs,
    double LeftMs,
    double PlateauMs,
    double RightMs);

/// <summary>Everything one Sum loss render needs, captured on the UI thread; the response is BEFORE the chain.</summary>
internal sealed record EqWizardSumLossRequest(
    Complex[] SourceImpulseResponse,
    DspChannelChain Chain,
    EqualizationCurve? Bank,
    int AnchorIndex,
    int SampleRate,
    int ProcessorSampleRate,
    PhaseAnalysisSettings Gate,
    MeasuredBand Band,
    CalibrationFile? Calibration,
    IReadOnlyList<SumLossMember> Others,
    double SmoothingInverseOctaves,
    EqWizardDirectLossGate? Direct);

/// <summary>
/// The side's Sum loss with the edited channel re-filtered through its chain and this bank, the others as they stood.
/// Same arithmetic as the Virtual DSP panel's curve (<see cref="SumLossCurves"/>); the window and anchor are the
/// handoff's, so the curve does not slide under the bank. Pure and thread-safe.
/// </summary>
internal static class EqWizardSumLossRender
{
    public static List<SignalPoint>? Render(EqWizardSumLossRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FromProcessed(
            VirtualCrossoverAnalysis.ApplyChain(
                request.SourceImpulseResponse,
                request.Chain with { Peq = request.Bank },
                request.SampleRate,
                request.ProcessorSampleRate),
            request);
    }

    /// <summary>From the channel already run through its chain and bank: the gated preview applies them once for both.</summary>
    public static List<SignalPoint>? FromProcessed(Complex[] processed, EqWizardSumLossRequest request)
    {
        ArgumentNullException.ThrowIfNull(processed);
        ArgumentNullException.ThrowIfNull(request);
        var edited = new SumLossMember(
            processed,
            VirtualCrossoverAnalysis.FindPeakIndex(processed),
            VirtualCrossoverAnalysis.ChainValidRange(
                request.SourceImpulseResponse.Length,
                request.Chain with { Peq = request.Bank },
                request.SampleRate,
                request.ProcessorSampleRate,
                processed.Length),
            request.SampleRate,
            request.Band,
            request.Calibration);
        List<SumLossMember> members = [edited, .. request.Others];
        return request.Direct is { } direct
            ? SumLossCurves.Direct(
                members,
                direct.PinnedOffsetMs,
                direct.LeftMs,
                direct.PlateauMs,
                direct.RightMs,
                request.SmoothingInverseOctaves)
            : SumLossCurves.Full(
                members, request.AnchorIndex, request.Gate, request.SmoothingInverseOctaves);
    }
}
