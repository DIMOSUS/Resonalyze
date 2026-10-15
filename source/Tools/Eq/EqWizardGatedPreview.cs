using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <param name="SumLoss">The side's Sum loss to render from the same processed response; null leaves it out.</param>
/// <param name="Magnitude">False for a hybrid source, whose magnitude is its spatial average: only the loss is rendered.</param>
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
    MeasuredBand Band = default,
    EqWizardSumLossRequest? SumLoss = null,
    bool Magnitude = true);

/// <summary>What one render of the request lands: each part null where the request left it out.</summary>
internal sealed record EqWizardGatedRender(
    IReadOnlyList<SignalPoint>? Magnitude,
    List<SignalPoint>? SumLoss);

/// <summary>
/// A Virtual DSP channel's curve as the plot builds it: whole chain with the bank substituted, then the gate.
/// Windowing does not commute with filtering. See docs/tech/eq-auto-tuner.md#gated-sources.
/// </summary>
internal static class EqWizardGatedPreview
{
    /// <summary>The corrected magnitude alone. Pure and thread-safe: every input is in the request.</summary>
    public static IReadOnlyList<SignalPoint> Render(EqWizardGatedPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Gate(Process(request), request);
    }

    /// <summary>Magnitude and Sum loss from ONE application of the chain: the loss would otherwise apply it again.</summary>
    public static EqWizardGatedRender RenderAll(EqWizardGatedPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var _ = AppProfiler.Zone("EqWizard.GatedPreview");
        Complex[] processed;
        using (AppProfiler.Zone("EqWizard.ApplyChain"))
        {
            processed = Process(request);
        }

        IReadOnlyList<SignalPoint>? magnitude = request.Magnitude ? Gate(processed, request) : null;
        List<SignalPoint>? sumLoss = null;
        if (request.SumLoss != null)
        {
            using (AppProfiler.Zone("EqWizard.SumLoss"))
            {
                sumLoss = EqWizardSumLossRender.FromProcessed(processed, request.SumLoss);
            }
        }

        return new EqWizardGatedRender(magnitude, sumLoss);
    }

    // Chain carries no PEQ; one ApplyChain realises it with the edited bank (all-pass included), like the panel.
    private static Complex[] Process(EqWizardGatedPreviewRequest request) =>
        VirtualCrossoverAnalysis.ApplyChain(
            request.ImpulseResponse,
            request.Chain with { Peq = request.Bank },
            request.SampleRate,
            request.ProcessorSampleRate);

    // Anchor fixed at handoff, not re-read, or the curve would slide under its own correction.
    private static IReadOnlyList<SignalPoint> Gate(Complex[] processed, EqWizardGatedPreviewRequest request) =>
        DataHelper.GetGatedPrimarySpectrum(
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

/// <summary>Newest-wins renders off the UI thread (a fader drag asks for one per frame).</summary>
internal sealed class EqWizardPreviewOrchestrator
{
    private readonly Func<EqWizardGatedPreviewRequest, EqWizardGatedRender> render;
    private long revision;

    public EqWizardPreviewOrchestrator()
        : this(EqWizardGatedPreview.RenderAll)
    {
    }

    internal EqWizardPreviewOrchestrator(
        Func<EqWizardGatedPreviewRequest, EqWizardGatedRender> render)
    {
        this.render = render ?? throw new ArgumentNullException(nameof(render));
    }

    public void Invalidate() => Interlocked.Increment(ref revision);

    /// <summary>Null when a newer render started meanwhile; callers keep the last landed curves.</summary>
    public async Task<EqWizardGatedRender?> RenderLatestAsync(
        EqWizardGatedPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        long requestRevision = Interlocked.Increment(ref revision);
        EqWizardGatedRender result =
            await Task.Run(() => render(request)).ConfigureAwait(false);
        return requestRevision == Interlocked.Read(ref revision) ? result : null;
    }
}
