using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The write-back address of a PEQ handoff: which channel's which side the edited
/// bank belongs to, in which project. The side is recorded at handoff time — the
/// user can flip the L/R selector while the wizard is open, and the result must
/// still land where it was taken from, not where the panel happens to be looking.
/// </summary>
/// <param name="ProjectGeneration">
/// Which loaded project the address belongs to. The channel reference alone cannot
/// say: binding a project REUSES the runtime channel objects when the channel count
/// matches (<c>channels[i].Pair = project.Pairs[i]</c>), so after an import the same
/// object holds a different session's settings and a bank returned against it would
/// land, silently, on a channel the user never opened the wizard from.
/// </param>
/// <param name="SourceRevision">
/// The measurement the bank was tuned against, by the side state's own load counter.
/// A session survives a trip back to Virtual DSP by the tab, and picking a new
/// measurement for that side there leaves a bank computed from a curve that no longer
/// exists — invisible at the moment of Return, because the wizard still shows the old
/// one.
/// </param>
/// <param name="Mono">
/// Whether the pair was mono when the handoff was taken. It decides WHERE
/// <see cref="VirtualCrossoverChannelPairSettings.SideFor"/> delivers, so a pair that
/// changed since would route the bank to the other side's settings.
/// </param>
internal sealed record VirtualDspEqReturnToken(
    VirtualCrossoverChannel Channel,
    bool RightSide,
    long ProjectGeneration,
    int SourceRevision,
    bool Mono);

/// <summary>
/// Everything one Virtual DSP channel side sends into the EQ Wizard: the curve to
/// equalize (rendered through the DSP plot's own gate and calibration), the PEQ the
/// channel already holds (the wizard's bank starts from it), the Auto Tune window
/// the channel's crossover implies, where the shared target hangs on the DSP plot,
/// and the address the result returns to.
/// </summary>
/// <remarks>
/// <see cref="TargetLevelDb"/> is valid in the wizard VERBATIM: the source is
/// rendered in the same dB frame as the Virtual DSP plot (same gate, same
/// calibration), so "one target" means one height too — the wizard's Target Level
/// starts exactly where the user just saw the curve hang, instead of being
/// re-suggested against the channel and quietly drifting.
/// </remarks>
internal sealed record VirtualDspEqHandoffRequest(
    EqWizardCurveSource Source,
    EqualizationCurve BankSeed,
    double? AutoTuneMinHz,
    double? AutoTuneMaxHz,
    double TargetLevelDb,
    int SmoothingInverseOctaves,
    VirtualDspEqReturnToken Token);

/// <summary>
/// Builds and lands PEQ handoffs between the Virtual DSP tool and the EQ Wizard.
/// UI-free: the panel supplies its gate snapshot's pieces and shows the result; the
/// rules — what travels, how the curve is windowed, where the result may land — are
/// all here, where a test can hold them.
/// </summary>
internal static class VirtualDspEqHandoff
{
    /// <summary>
    /// Prepares one channel side for the wizard. With <paramref name="withChain"/> the
    /// curve is the side's measurement through its DSP chain WITHOUT the PEQ — gain,
    /// delay, polarity, crossover and all-pass stay — windowed by the gate the processed
    /// view draws with. Without it the curve is the raw measurement under the same gate
    /// anchored on its own peak: exactly the panel's Raw curve.
    /// <para>
    /// The request also carries what the wizard needs to redraw the CORRECTED curve the
    /// same way the panel would (<see cref="EqWizardCurveSource.PreviewImpulseResponse"/>):
    /// the whole chain including the edited bank, through one ApplyChain, then this gate.
    /// Adding the bank's ideal magnitude to the bare curve instead would part from the
    /// panel by several dB wherever a filter rings longer than the window.
    /// </para>
    /// </summary>
    /// <param name="gateTemplate">
    /// The magnitude gate's <see cref="PhaseAnalysisSettings"/> template; its offset is
    /// resolved here and overwritten.
    /// </param>
    /// <param name="pinnedGateOffsetMs">
    /// The user's pinned gate offset for the active side, or null when the gate is on
    /// Auto. Raw ignores it, like the panel's Raw curve does — a raw response lives in
    /// its own time and a processed-view pin would clip it into the left fade.
    /// </param>
    /// <param name="renderAnchorIndex">
    /// The window anchor the last redraw used for the active side's channel curves (the
    /// shared earliest-arrival start), so an unpinned gate opens exactly where the plot's
    /// does. Null when no render exists to follow — the curve then opens on its own
    /// front, the same rule the plot applies to a lone channel.
    /// </param>
    public static VirtualDspEqHandoffRequest Build(
        VirtualCrossoverChannel channel,
        bool rightSide,
        bool withChain,
        PhaseAnalysisSettings gateTemplate,
        double? pinnedGateOffsetMs,
        int? renderAnchorIndex,
        double targetLevelDb,
        int smoothingInverseOctaves,
        string? calibrationId,
        long projectGeneration)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(gateTemplate);

        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
        if (state.TransferImpulseResponse is null || state.ProcessingSource is null)
        {
            throw new InvalidOperationException(
                "The channel side has no measurement to hand to the EQ Wizard.");
        }

        int sampleRate = state.SampleRate;
        Complex[] response;
        int anchorIndex;
        double gateOffsetMs;
        // The chain the preview substitutes the edited bank into — and, with the bank
        // left out, the bare curve the wizard opens on. A raw handoff carries the
        // identity: it is the measurement before any of the chain, so an edited bank is
        // all there is to apply.
        DspChannelChain previewChain = withChain
            ? settings.ToChain() with { Peq = null }
            : DspChannelChain.Identity;
        if (withChain)
        {
            // The chain minus the very thing under edit. Delay and polarity are
            // magnitude-transparent but keep the response in the processed view's
            // time, where the pinned offset and the render anchor point.
            DspChannelChain chain = previewChain;
            response = state.ProcessingSource.Apply(chain, sampleRate);
            anchorIndex = renderAnchorIndex ?? ProcessedChannels.StartAnchorIndex(
                response,
                VirtualCrossoverAnalysis.FindPeakIndex(response),
                sampleRate,
                VirtualCrossoverAnalysis.ChainValidRange(
                    state.ProcessingSource.SampleCount,
                    chain,
                    sampleRate,
                    response.Length));
            gateOffsetMs = pinnedGateOffsetMs ?? anchorIndex * 1_000.0 / sampleRate;
        }
        else
        {
            response = state.TransferImpulseResponse;
            anchorIndex = state.TransferPeakIndex;
            gateOffsetMs = anchorIndex * 1_000.0 / sampleRate;
        }

        (double MinHz, double MaxHz)? window = withChain
            ? AutoTuneWindow(settings)
            : null;

        string side = channel.Pair.Mono ? "mono" : rightSide ? "R" : "L";
        string variant = withChain ? "DSP" : "raw";
        // A bypassed block contributes its RAW signal to the plot and the sum, so with
        // bypass on the panel is not drawing this chain at all. The chain is still what
        // the PEQ belongs to — a bank tuned against a crossover-less curve would be
        // wrong for the setup the moment bypass came off — so the handoff keeps
        // building it and says so instead of quietly showing a different curve.
        string bypassNote = withChain && channel.Pair.Bypass
            ? "\r\nThe block is BYPASSED on the plot right now, so the panel is drawing " +
              "its raw response — this curve is the chain the PEQ will live in."
            : string.Empty;
        var source = new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.VirtualDspChannel,
            DisplayName = $"Ch {channel.Name} · {side} ({variant})",
            Description =
                $"Virtual DSP channel {channel.Name}, {SideDescription(channel, rightSide)}" +
                (string.IsNullOrWhiteSpace(settings.DisplayName)
                    ? string.Empty
                    : $" — {settings.DisplayName}") +
                (withChain
                    ? "\r\nDSP chain applied (PEQ bypassed), windowed by the Virtual DSP gate."
                    : "\r\nRaw measurement, windowed by the Virtual DSP gate at its own peak.") +
                bypassNote,
            Measurement = new ImpulseMeasurementView(response, anchorIndex, sampleRate),
            Coherence = EqWizardSourceResolver.ExtractTransferCoherence(
                state.TransferCoherence, sampleRate),
            GateSettings = gateTemplate with { GateOffsetMs = gateOffsetMs },
            PinnedCalibrationId = calibrationId,
            // The ORIGINAL measurement and the chain around the bank, so the corrected
            // preview is one ApplyChain of the whole chain — the panel's own arithmetic
            // — rather than the bypassed response filtered a second time.
            PreviewImpulseResponse = state.ProcessingSource.CroppedImpulseResponse,
            PreviewChain = previewChain,
            SampleRateHz = sampleRate,
            CurveKind = AnalysisCurveKind.Primary
        };

        return new VirtualDspEqHandoffRequest(
            source,
            new EqualizationCurve(settings.PeqBands, settings.PeqPreampDb),
            window?.MinHz,
            window?.MaxHz,
            targetLevelDb,
            smoothingInverseOctaves,
            // A mono pair's handoff reads the single left set whichever side is
            // active, so the token says LEFT outright: if the pair stops being mono
            // while the wizard is open, the result still lands on the set the tune
            // was taken from, not on a right slot it never saw.
            new VirtualDspEqReturnToken(
                channel,
                rightSide && !channel.Pair.Mono,
                projectGeneration,
                state.SourceRevision,
                channel.Pair.Mono));
    }

    /// <summary>
    /// Lands a finished bank back on the side it was taken from. False — and no write —
    /// when the channel is gone (removed) or the project it belonged to has been
    /// replaced since; the caller keeps the wizard open so the tune is not lost.
    /// </summary>
    /// <param name="projectGeneration">
    /// The panel's CURRENT project generation. Checked first and on its own: the
    /// channel object survives a project bind when the channel count matches, so
    /// membership alone would happily write a bank tuned against one session into
    /// whatever session replaced it.
    /// </param>
    /// <remarks>
    /// What is refused is a return that would land somewhere ELSE, or against a
    /// measurement that no longer exists — changes the user cannot see from the
    /// wizard, which still shows the curve it opened on. Edits to the CHAIN
    /// (crossover, gain, delay, all-pass) are deliberately not refused: those are the
    /// user's own knobs on their own channel, the bank remains theirs to apply, and
    /// refusing it would throw away real work over a decision they made on purpose.
    /// </remarks>
    public static bool TryApplyReturn(
        IReadOnlyList<VirtualCrossoverChannel> channels,
        VirtualDspEqReturnToken token,
        EqualizationCurve curve,
        long projectGeneration)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(curve);

        if (token.ProjectGeneration != projectGeneration ||
            !channels.Contains(token.Channel))
        {
            return false;
        }

        // The pair's routing must still be the one the address was written for: mono
        // sends both sides to the left set, so a pair that changed since would deliver
        // the bank to settings the tune was never taken from.
        if (token.Mono != token.Channel.Pair.Mono)
        {
            return false;
        }

        // And the measurement must still be the one it was tuned against. The side is
        // read PHYSICALLY here: a mono token addresses the left slot, which is the
        // slot its curve came from, and the effective accessor would answer the same
        // — but only while the pair is still mono, which is no longer this check's
        // business to assume.
        VirtualCrossoverChannelState state =
            token.Channel.PhysicalSideState(token.RightSide);
        if (state.SourceRevision != token.SourceRevision)
        {
            return false;
        }

        // SideFor, not the active side: the user may have flipped L/R while editing.
        VirtualCrossoverChannelSettings settings = token.Channel.Pair.SideFor(token.RightSide);
        settings.PeqBands = curve.Bands.ToList();
        settings.PeqPreampDb = curve.PreampDb;
        settings.PeqSourceName = "EQ Wizard";
        return true;
    }

    // The Auto Tune window a channel's crossover implies: its passband, corner to
    // corner. Beyond the corners the chain is rolling the driver off on purpose and a
    // fit would chase the slope. No crossover means no opinion — the wizard's window
    // stays where the user left it.
    private static (double MinHz, double MaxHz)? AutoTuneWindow(
        VirtualCrossoverChannelSettings settings) =>
        settings.CrossoverKind switch
        {
            CrossoverKind.BandPass =>
                (settings.HighPassEdge.FrequencyHz, settings.LowPassEdge.FrequencyHz),
            CrossoverKind.HighPass => (settings.HighPassEdge.FrequencyHz, 20_000),
            CrossoverKind.LowPass => (20, settings.LowPassEdge.FrequencyHz),
            _ => null
        };

    private static string SideDescription(VirtualCrossoverChannel channel, bool rightSide) =>
        channel.Pair.Mono ? "mono" : rightSide ? "right side" : "left side";
}
