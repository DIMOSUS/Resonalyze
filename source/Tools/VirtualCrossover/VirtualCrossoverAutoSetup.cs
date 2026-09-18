using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The crossover wizard's side of the session: the blocks it reads, the driver curves it opens on, and the
/// proposal written back. See docs/tech/crossover-auto-setup.md.</summary>
internal static class VirtualCrossoverAutoSetup
{
    /// <summary>FDW cycles for the wizard's driver curves. 8 is the longest the phase bank offers, so it keeps the
    /// most of the low end while still cutting the room.</summary>
    private const int FdwCycles = 8;

    /// <summary>Outer gate for those curves, in milliseconds so it means the same thing at 44.1 and 192 kHz. The
    /// plateau is long enough that FDW-8 is really 8 cycles down to about 45 Hz.</summary>
    private const double GateLeftMs = 2.0;

    private const double GatePlateauMs = 180.0;

    private const double GateRightMs = 20.0;

    /// <summary>The enabled blocks holding a measurement on the shown side, in chain order.</summary>
    public static List<VirtualCrossoverChannel> Participants(VirtualCrossoverSession session) =>
        session.Channels
            .Where(channel => channel.Pair.Enabled &&
                channel.SideState(session.ActiveSideRight).TransferImpulseResponse != null)
            .ToList();

    /// <summary>Each participant's driver curve, band and corners as the wizard reads them, off the shown side.</summary>
    /// <exception cref="ArgumentException">A response has no usable band.</exception>
    public static List<AutoSetupWizardChannel> ReadChannels(
        VirtualCrossoverSession session, IReadOnlyList<VirtualCrossoverChannel> participating)
    {
        // Psychoacoustic smoothing and an 8-cycle FDW: the wizard judges what the ear resolves and cuts most of the
        // room out of the driver curves before it ever gets to the band read. The window is stated in MILLISECONDS,
        // not the default 4096 samples — in samples the FDW collapses to a short fixed gate from ~95 Hz up at 96 kHz
        // and from ~380 Hz at 192 kHz, which is most of the band the wizard cares about.
        // Scoped to the per-channel curves: the coherent readings (post-check, junction tuner) keep their own gate.
        // See docs/tech/crossover-auto-setup.md#curve-source.
        var options = new FrequencyResponseOptions
        {
            SmoothingInverseOctaves = SpectrumSmoothing.PsychoacousticCode,
            MagnitudeWindowMode = PhaseWindowMode.FrequencyDependent,
            MagnitudeFdwCycles = FdwCycles
        };
        bool rightSide = session.ActiveSideRight;
        int sampleRate = participating[0].SideState(rightSide).SampleRate;
        (options.Window, options.LeftTukeyWindow, options.RightTukeyWindow) =
            FrequencyResponseOptions.TrimGateToFft(
                (int)Math.Round(GateLeftMs / 1_000.0 * sampleRate),
                (int)Math.Round(GatePlateauMs / 1_000.0 * sampleRate),
                (int)Math.Round(GateRightMs / 1_000.0 * sampleRate));

        var channels = new List<AutoSetupWizardChannel>(participating.Count);
        foreach (VirtualCrossoverChannel channel in participating)
        {
            VirtualCrossoverChannelState state = channel.SideState(rightSide);
            AnalysisCurve curve = DataHelper.GetPrimarySpectrum(
                new ImpulseMeasurementView(
                    state.TransferImpulseResponse!,
                    state.TransferPeakIndex,
                    state.SampleRate)
                {
                    // Or the band read runs down the window's leakage an octave below the real low corner.
                    LowestMeasuredFrequencyHz = state.MeasuredBand.LowEdgeHz,
                    HighestMeasuredFrequencyHz = state.MeasuredBand.HighEdgeHz
                },
                options,
                session.Calibration.For(state));
            // Discount frequencies the measurement's coherence did not trust.
            IReadOnlyList<double>? coherence =
                state.TransferCoherence is { Length: > 1 } linear
                    ? CoherenceCurves.PerPoint(linear, curve.Points, state.SampleRate)
                    : null;
            IReadOnlyList<SignalPoint>? distortion = state.DistortionCurve;

            // With two similar drivers, existing corners decide which plays lower.
            VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
            channels.Add(new AutoSetupWizardChannel(
                $"{channel.Name} — {settings.DisplayName}",
                VirtualCrossoverColors.ChannelAccent(session.Channels.IndexOf(channel)),
                VirtualCrossoverAlignmentStages.StageOf(channel.Pair.Zone),
                curve.Points,
                coherence,
                distortion,
                CrossoverAutoSetup.EstimateBand(curve.Points, coherence, distortion),
                // FIR corners stand in where the IIR crossover is off.
                settings.EffectiveHighPassHz,
                settings.EffectiveLowPassHz,
                state.TransferImpulseResponse));
        }

        return channels;
    }

    /// <summary>Writes the proposal: corners, families, slopes, cut-only gains and the polarity the crossover itself
    /// implies, on both sides. Delay stays Auto delay's job, and Auto delay may flip the polarity again.</summary>
    /// <returns>How many sides lost a phase rotation.</returns>
    public static int Write(
        IReadOnlyList<VirtualCrossoverChannel> participating, IReadOnlyList<CrossoverProposal> proposals)
    {
        int clearedRotations = 0;
        for (int i = 0; i < participating.Count; i++)
        {
            VirtualCrossoverChannel channel = participating[i];
            CrossoverProposal proposal = proposals[i];
            // A crossover is one electrical filter: both sides get the same frequencies, families, slopes and gain.
            foreach (bool rightSide in new[] { false, true })
            {
                if (channel.Pair.Mono && rightSide)
                {
                    continue;
                }

                VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
                settings.CrossoverKind = proposal.Kind;
                if (proposal.HighPassEdge is { } highPass)
                {
                    settings.HighPassEdge = highPass;
                }
                if (proposal.LowPassEdge is { } lowPass)
                {
                    settings.LowPassEdge = lowPass;
                }
                settings.GainDb = proposal.GainDb;
                // A crossover of a given family and order puts a fixed phase relationship across the junction, so the
                // polarity that makes it sum is the crossover's to state. Auto delay runs afterwards and composes its
                // own flip over this one. See docs/tech/crossover-auto-setup.md#polarity.
                settings.InvertPolarity = proposal.InvertPolarity;
                // The phase angle is stated AT the crossover, so a wizard rewrite resets rotations (and says so).
                if (settings.PhaseRotationDegrees != 0)
                {
                    settings.PhaseRotationDegrees = 0;
                    clearedRotations++;
                }
            }
        }

        return clearedRotations;
    }

    /// <summary>The block order after the wizard's chain order: its participants trade places, the rest stay.</summary>
    /// <returns>Each new slot's index in the current order.</returns>
    public static List<int> Reorder(
        List<VirtualCrossoverChannel> channels,
        IReadOnlyList<VirtualCrossoverChannel> participating,
        IReadOnlyList<int> chainOrder)
    {
        IReadOnlyList<VirtualCrossoverChannel> sorted = ReorderIntoSlots(
            channels,
            chainOrder.Select(index => participating[index]).ToList());
        return sorted.Select(channel => channels.IndexOf(channel)).ToList();
    }

    /// <summary>Only the reordered members' slots are reused; blocks the wizard did not look at keep theirs.</summary>
    internal static IReadOnlyList<T> ReorderIntoSlots<T>(
        IReadOnlyList<T> all,
        IReadOnlyList<T> reordered)
        where T : class
    {
        var slots = new HashSet<T>(reordered);
        var result = new List<T>(all.Count);
        int next = 0;
        foreach (T item in all)
        {
            result.Add(slots.Contains(item) ? reordered[next++] : item);
        }

        return result;
    }
}
