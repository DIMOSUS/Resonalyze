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
/// <param name="PreviewChain">
/// The chain the curve was built through, PEQ excluded — the thing the bank was
/// fitted against. Compared whole on return, with only the polarity normalized away.
/// <para>
/// Measured, not assumed. Through the real filter-then-window path, sampled across
/// the ranges the UI allows (delay to 100 ms; all-pass 10..2000 Hz with Q to 20 —
/// a sample, not a search for the true extremum), the worst SHAPE shift seen from a
/// single edit is: polarity exactly 0 at every rate
/// (|−x·w| = |x·w|); delay 0.000 dB at 48 kHz but 1.70 dB at 192 kHz, where the rate
/// clamps the window to 171 ms; all-pass 0.27 dB at 48 kHz and 4.77 dB at 192 kHz
/// (40 Hz, Q 20 — 318 ms of group delay against that window). Gain leaves the shape
/// alone at every setting, but the handoff carries an ABSOLUTE target level and the
/// bank's preamp was fitted against it, so a moved gain leaves the tune exactly that
/// many dB off the target. Only polarity survives all of it.
/// </para>
/// </param>
/// <param name="Peq">
/// The bank the session STARTED from. The chain comparison must exclude the PEQ —
/// it is the stage under edit — which leaves the channel's own PEQ unguarded: a
/// Load from file or a Clear in the panel, both reachable while a session is open,
/// would otherwise be silently overwritten by the older bank on return. A plain lost
/// update.
/// </param>
/// <param name="TargetLevelDb">
/// Where the target hung when the handoff was taken. The wizard may move it while
/// tuning — it is the user's knob — and the moved value travels back with the bank,
/// so the tune realizes the level it was fitted against. This records the level to
/// return INTO: if the panel's own has been changed independently since, the two
/// answers conflict and the return is refused rather than one of them silently
/// winning.
/// </param>
/// <param name="GateTemplate">
/// The magnitude window the curve was read through, and <paramref name="PinnedGateOffsetMs"/>
/// where the user pinned it. Moving the gate in the panel — reachable by a plain tab
/// switch — re-windows the channel while the wizard keeps drawing the old result, and
/// this PR has already produced enough windowing bugs to treat that as the same class
/// as a replaced measurement. The pin applies to a CHAIN handoff only; a raw curve is
/// anchored on its own peak and never read the pin.
/// <para>
/// What is deliberately NOT guarded is where an AUTO-placed window ended up: that
/// offset is the earliest arrival across all channels, so another channel's edit can
/// move it. Measured, it does not matter — the window opens ahead of the response and
/// runs far past it, so sliding its start changes the reading by 0.000 dB at 48 kHz
/// even for a 50 ms move, and 0.078 dB at 192 kHz, where the window is at its
/// shortest. Two orders below anything else this guard refuses over, and it would
/// fire on a co-channel mute. A guard that costs a finished tune has to earn it.
/// </para>
/// </param>
/// <param name="WithChain">
/// Whether the curve was built through the chain at all. A raw handoff is measured
/// before any of it, so chain edits cannot invalidate its bank — and its recorded
/// chain is the identity, which would otherwise compare unequal to a real one.
/// </param>
/// <param name="CalibrationId">
/// The microphone correction the curve was tuned under. The wizard's own selector is
/// DISABLED during a handoff precisely because a bank fitted under one correction and
/// summed under another is not the same bank — and the Virtual DSP panel's selector,
/// reachable by a plain tab switch, would otherwise walk around that lock from the
/// other side.
/// </param>
internal sealed record VirtualDspEqReturnToken(
    VirtualCrossoverChannel Channel,
    bool RightSide,
    long ProjectGeneration,
    int SourceRevision,
    bool Mono,
    DspChannelChain PreviewChain,
    bool WithChain,
    PeqBankState Peq,
    double TargetLevelDb,
    PhaseAnalysisSettings GateTemplate,
    double? PinnedGateOffsetMs,
    string? CalibrationId);

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
    double TargetLevelMinDb,
    double TargetLevelMaxDb,
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
        double targetLevelMinDb,
        double targetLevelMaxDb,
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
            ? PassbandFor(settings)
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
            // The range the panel can actually express. The wizard's own box is
            // wider (an absolute dB SPL curve needs the room), and a level outside
            // this would come back silently clamped — the returned tune realizing a
            // height it was not fitted to, which is exactly what carrying the level
            // was for.
            targetLevelMinDb,
            targetLevelMaxDb,
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
                channel.Pair.Mono,
                // From the preview chain, so it is exactly the crossover the wizard's
                // curve was built through — including "none" for a raw handoff.
                previewChain,
                withChain,
                new PeqBankState(settings.PeqBands, settings.PeqPreampDb),
                targetLevelDb,
                gateTemplate,
                pinnedGateOffsetMs,
                MicrophoneCalibrationIds.Normalize(calibrationId)));
    }

    /// <summary>
    /// Lands a finished bank back on the side it was taken from. False — and no write —
    /// when the channel is gone (removed) or the project it belonged to has been
    /// replaced since; the caller keeps the wizard open so the tune is not lost.
    /// </summary>
    /// <param name="calibrationId">
    /// The panel's CURRENT microphone calibration, compared against the one the bank
    /// was fitted under.
    /// </param>
    /// <param name="projectGeneration">
    /// The panel's CURRENT project generation. Checked first and on its own: the
    /// channel object survives a project bind when the channel count matches, so
    /// membership alone would happily write a bank tuned against one session into
    /// whatever session replaced it.
    /// </param>
    /// <remarks>
    /// What is refused is a return that would land somewhere ELSE, or against a curve
    /// that no longer exists — changes the user cannot see from the wizard, which
    /// still shows what it opened on. The line runs through the MAGNITUDE the bank was
    /// fitted to, or the LEVEL it was fitted against. A replaced measurement, a
    /// changed calibration and any change to the chain (gain, delay, crossover,
    /// all-pass) move one of those — measured across the UI's own ranges, see
    /// <see cref="VirtualDspEqReturnToken.PreviewChain"/> — so all of them refuse.
    /// A polarity flip is the single exception, because it changes neither.
    /// </remarks>
    public static bool TryApplyReturn(
        IReadOnlyList<VirtualCrossoverChannel> channels,
        VirtualDspEqReturnToken token,
        EqualizationCurve curve,
        long projectGeneration,
        string? calibrationId,
        PhaseAnalysisSettings gateTemplate,
        double? pinnedGateOffsetMs,
        double targetLevelDb)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(curve);

        if (token.ProjectGeneration != projectGeneration ||
            !channels.Contains(token.Channel))
        {
            return false;
        }

        // The correction the curve was read through must still be the one the sum is
        // predicted through. The wizard disables its own selector for exactly this
        // reason; the Virtual DSP panel's selector is reachable by a plain tab switch,
        // which a session deliberately survives, so the same rule has to hold here or
        // the lock is decorative.
        if (!string.Equals(
                token.CalibrationId,
                MicrophoneCalibrationIds.Normalize(calibrationId),
                StringComparison.OrdinalIgnoreCase))
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

        // The window the curve was read through must still be where it was, and the
        // level it was fitted against must still be the panel's answer too. The PIN
        // counts only for a chain handoff: a raw curve is anchored on its own peak
        // and ignores the processed view's pin by construction (see Build, and
        // VirtualCrossoverPanel.BuildRawMagnitudeCurve — a raw response lives in its
        // own time), so refusing a raw return because that pin moved would throw work
        // away over something its curve never read.
        if (!Equals(Comparable(token.GateTemplate), Comparable(gateTemplate)) ||
            (token.WithChain &&
                !Nullable.Equals(token.PinnedGateOffsetMs, pinnedGateOffsetMs)) ||
            !token.TargetLevelDb.Equals(targetLevelDb))
        {
            return false;
        }

        // And the chain must still be the one the curve was built through. Compared
        // WHOLE — every stage of it moves either the shape the bank corrects or the
        // level it was fitted against (see PreviewChain for the measured figures) —
        // with polarity normalized away, the one stage that provably changes neither.
        // DspChannelChain and its specs are records, so this is value equality; the
        // PEQ is null on both sides, so its reference equality never enters.
        if (token.WithChain &&
            !Equals(
                Comparable(token.PreviewChain),
                Comparable(
                    token.Channel.Pair.SideFor(token.RightSide).ToChain() with
                    {
                        Peq = null
                    })))
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

        // And the PEQ must still be the one the session opened on. The chain check
        // above cannot see this — it excludes the very stage under edit — so without
        // it a Load or Clear made in the panel meanwhile is a lost update.
        if (!token.Peq.Equals(new PeqBankState(settings.PeqBands, settings.PeqPreampDb)))
        {
            return false;
        }
        settings.PeqBands = curve.Bands.ToList();
        settings.PeqPreampDb = curve.PreampDb;
        settings.PeqSourceName = "EQ Wizard";
        return true;
    }

    // A gate reduced to what the MAGNITUDE actually reads. The template is shared
    // with the phase and impulse views, which is where FDW cycles, the detrend and
    // the unwrap belong — the magnitude forces Fixed and ignores them (see the
    // magnitudeGate rebuild in VirtualCrossoverPanel). Comparing them would refuse a
    // return because the user changed how the PHASE view reads, which the handoff's
    // curve never saw. The offset is carried separately, as the pin.
    private static PhaseAnalysisSettings Comparable(PhaseAnalysisSettings gate) =>
        gate with
        {
            FdwCycles = 0,
            DetrendMode = PhaseDetrendMode.Off,
            ManualDetrendMilliseconds = 0,
            GateOffsetMs = 0,
            Unwrap = false,
            SmoothingInverseOctaves = 0
        };

    // A chain reduced to what can invalidate a bank: everything except the polarity,
    // which is -1 at every frequency and so leaves both the shape and the level alone
    // (measured as exactly 0 at every rate).
    private static DspChannelChain Comparable(DspChannelChain chain) =>
        chain with { InvertPolarity = false };

    /// <summary>
    /// The band a channel's crossover confines it to: its passband, corner to corner.
    /// Null when it has no crossover — then it has no opinion, and a caller keeps
    /// whatever range it had (the wizard's Auto Tune window stays where the user left
    /// it; an exported tuning sheet states the full range).
    /// </summary>
    /// <remarks>
    /// Beyond the corners the chain is rolling the driver off on purpose, so a fit
    /// there would chase the slope rather than the driver.
    /// </remarks>
    internal static (double MinHz, double MaxHz)? PassbandFor(
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
