using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// Everything one gated preview render needs, captured before it leaves the UI thread.
/// </summary>
internal sealed record EqWizardGatedPreviewRequest(
    Complex[] ImpulseResponse,
    DspChannelChain Chain,
    EqualizationCurve? Bank,
    int AnchorIndex,
    int SampleRate,
    int ProcessorSampleRate,
    PhaseAnalysisSettings Gate,
    CalibrationFile? Calibration,
    double SmoothingInverseOctaves);

/// <summary>
/// Builds a Virtual DSP channel's curve the way the Virtual DSP plot builds it: the
/// whole chain — the bank under edit included — through one
/// <see cref="VirtualCrossoverAnalysis.ApplyChain"/>, then the panel's own gate.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a window does not commute with a filter. Adding a band's IDEAL
/// magnitude to an already-gated curve — what an equalizer normally previews, and what
/// this wizard still does for its own sources — answers a different question than
/// filtering first and then windowing. The gap is not academic at the gate lengths the
/// Virtual DSP tool uses: with its default 6 ms window a Q 5 band at 100 Hz reads about
/// 4.4 dB apart between the two, and a Q 8 band at 60 Hz about 5.4 dB, because a window
/// that short cannot contain the filter's own ringing. Above roughly 1 kHz the two agree
/// to a few hundredths.
/// </para>
/// <para>
/// So the preview pays for a real convolution instead. Both curves of a gated source
/// come from here — the bare one with <c>Bank</c> null, the corrected one with the
/// edited bank — so they can never drift apart, and the corrected one is the same
/// arithmetic the panel runs for the same channel.
/// </para>
/// </remarks>
internal static class EqWizardGatedPreview
{
    /// <summary>
    /// The gated magnitude of the chain with <paramref name="request"/>'s bank
    /// substituted into it. Pure and thread-safe: every input is captured in the
    /// request, so this runs on a worker thread while the panel stays live.
    /// </summary>
    public static IReadOnlyList<SignalPoint> Render(EqWizardGatedPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Substituted, not layered: the request carries the channel's chain WITHOUT its
        // PEQ, so one pass realizes gain, delay, polarity, the crossover and the edited
        // bank together — the panel's own single ApplyChain for that channel. The bank
        // is where an all-pass lives, so it rides in with the rest of the filters.
        DspChannelChain chain = request.Chain with { Peq = request.Bank };
        Complex[] processed = VirtualCrossoverAnalysis.ApplyChain(
            request.ImpulseResponse, chain, request.SampleRate,
            request.ProcessorSampleRate);

        // The anchor is the one resolved when the handoff was taken, not re-read from
        // this response: the window has to stay put while the user turns a knob, or the
        // curve would slide under its own correction.
        return DataHelper.GetGatedPrimarySpectrum(
            new ImpulseMeasurementView(
                processed, request.AnchorIndex, request.SampleRate),
            request.Gate,
            request.Calibration,
            request.SmoothingInverseOctaves).Points;
    }
}

/// <summary>
/// Runs gated previews off the UI thread and accepts only the newest result — the same
/// newest-wins contract as <see cref="EqWizardAutoTuneOrchestrator"/>, because a fader
/// drag asks for one of these per frame and each costs a pair of transforms.
/// </summary>
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

    /// <summary>Orphans any render in flight, so a stale one cannot land.</summary>
    public void Invalidate() => Interlocked.Increment(ref revision);

    /// <summary>
    /// The render, or null when a newer one started while this was running. Callers
    /// keep the last landed curve on screen meanwhile: blanking it would flicker the
    /// preview on every keystroke.
    /// </summary>
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
