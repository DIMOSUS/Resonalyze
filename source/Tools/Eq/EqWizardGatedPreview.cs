using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed record EqWizardGatedPreviewRequest(
    Complex[] ImpulseResponse,
    DspChannelChain Chain,
    EqualizationCurve? Bank,
    int AnchorIndex,
    int SampleRate,
    int ProcessorSampleRate,
    PhaseAnalysisSettings Gate,
    CalibrationFile? Calibration,
    double SmoothingInverseOctaves,
    // Both curves stop where the panel's channel curve stops (otherwise the source broke and the preview did not).
    MeasuredBand Band = default);

/// <summary>
/// A Virtual DSP channel's curve as the plot builds it: whole chain with the bank substituted, then the gate.
/// Windowing does not commute with filtering. See docs/tech/eq-auto-tuner.md#gated-sources.
/// </summary>
internal static class EqWizardGatedPreview
{
    /// <summary>Pure and thread-safe: every input is in the request.</summary>
    public static IReadOnlyList<SignalPoint> Render(EqWizardGatedPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Chain carries no PEQ; one ApplyChain realises it with the edited bank (all-pass included), like the panel.
        DspChannelChain chain = request.Chain with { Peq = request.Bank };
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            request.ImpulseResponse, chain, request.SampleRate,
            request.ProcessorSampleRate);

        // Anchor fixed at handoff, not re-read, or the curve would slide under its own correction.
        return DataHelper.GetGatedPrimarySpectrum(
            new ImpulseMeasurementView(
                processed, request.AnchorIndex, request.SampleRate)
            {
                LowestMeasuredFrequencyHz = request.Band.LowEdgeHz,
                HighestMeasuredFrequencyHz = request.Band.HighEdgeHz
            },
            request.Gate,
            request.Calibration,
            request.SmoothingInverseOctaves).Points;
    }
}

/// <summary>Newest-wins renders off the UI thread (a fader drag asks for one per frame).</summary>
internal sealed class EqWizardPreviewOrchestrator
{
    private readonly Func<EqWizardGatedPreviewRequest, IReadOnlyList<SignalPoint>> render;
    private long revision;

    public EqWizardPreviewOrchestrator()
        : this(EqWizardGatedPreview.Render)
    {
    }

    internal EqWizardPreviewOrchestrator(
        Func<EqWizardGatedPreviewRequest, IReadOnlyList<SignalPoint>> render)
    {
        this.render = render ?? throw new ArgumentNullException(nameof(render));
    }

    public void Invalidate() => Interlocked.Increment(ref revision);

    /// <summary>Null when a newer render started meanwhile; callers keep the last landed curve.</summary>
    public async Task<IReadOnlyList<SignalPoint>?> RenderLatestAsync(
        EqWizardGatedPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        long requestRevision = Interlocked.Increment(ref revision);
        IReadOnlyList<SignalPoint> result =
            await Task.Run(() => render(request)).ConfigureAwait(false);
        return requestRevision == Interlocked.Read(ref revision) ? result : null;
    }
}
