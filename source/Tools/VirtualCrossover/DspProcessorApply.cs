using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What the DSP processor dialog says under its fields about the processor on screen.</summary>
internal static class DspProcessorStatus
{
    public static string Text(DspProcessorSession choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        int processorRate = choice.SampleRateHz;
        int measurementRate = choice.MeasurementSampleRateHz;
        string band = measurementRate > 0
            ? $"Filters are designed at {processorRate / 1000.0:0.###} kHz; the " +
              $"measurements stay at {measurementRate / 1000.0:0.###} kHz, so " +
              $"the simulation speaks for everything up to " +
              $"{Math.Min(processorRate, measurementRate) / 2000.0:0.#} kHz."
            : $"Filters are designed at {processorRate / 1000.0:0.###} kHz. The project " +
              "has no measurement yet, so nothing bounds the simulated band.";
        string convention = choice.QConvention == PeqQConvention.Rbj
            ? "Q is stated as the RBJ cookbook defines it, which is what the bands here are."
            : $"Tuning sheets restate Q as {PeqQConventions.DescribeShort(choice.QConvention)} " +
              "for this device; the filters themselves do not move.";
        string follow = choice.FollowsMeasurements
            ? "\r\nThe rate is not stated: it follows the project's measurements, " +
              "including after they are replaced at another rate."
            : string.Empty;
        string phase = choice.PhaseControl
            ? "\r\nEach block gets a Phase field, stated at that channel's own " +
              "crossover: move the crossover and the same angle builds another filter."
            : string.Empty;
        string fir = choice.FirFilters
            ? $"\r\nEach block gets a FIR button. A kernel is convolved at {processorRate / 1000.0:0.###} kHz " +
              "whatever rate its file states, so design it for this processor."
            : string.Empty;
        return band + "\r\n" + convention + follow + phase + fir;
    }
}

/// <summary>What confirming the processor dialog wrote: whether the processor changed, and the rotations and kernels a
/// device without the control lost.</summary>
internal sealed record DspProcessorWrite(bool Changed, int ClearedRotations, int ClearedFirFilters);

/// <summary>Writes a confirmed processor into the project. The comparison is of intent ("follow the measurements" equals
/// 48 kHz only until they are replaced); notes alone are a save, never a re-run. See docs/tech/virtual-dsp-panel.md#processor-rate.</summary>
internal static class DspProcessorApply
{
    /// <summary>True when the notes changed, which the caller saves.</summary>
    public static bool WriteNotes(VirtualCrossoverProjectFile project, DspProcessorSession choice)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(choice);
        if (string.Equals(choice.Notes, project.AiNotes, StringComparison.Ordinal))
        {
            return false;
        }

        project.AiNotes = choice.Notes;
        return true;
    }

    /// <summary>Confirming stores the phase and FIR answers shown, so a later model change cannot take away a control in
    /// use; a device without either drops the rotations and kernels, which would bend curves with no field on screen.</summary>
    public static DspProcessorWrite WriteProcessor(VirtualCrossoverSession session, DspProcessorSession choice)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(choice);
        VirtualCrossoverProjectFile project = session.Project;
        DspProcessorProfile profile = choice.Profile;
        bool follows = choice.FollowsMeasurements;
        if (profile == session.ProcessorProfile &&
            follows == project.DspProcessorRateFollowsMeasurements &&
            project.DspProcessorPhaseControl == choice.PhaseControl &&
            project.DspProcessorFirFilters == choice.FirFilters)
        {
            return new DspProcessorWrite(false, 0, 0);
        }

        project.DspProcessorPhaseControl = choice.PhaseControl;
        project.DspProcessorFirFilters = choice.FirFilters;
        project.SetDspProcessor(profile, follows);
        return new DspProcessorWrite(
            true, project.ClearUnavailablePhaseRotations(), project.ClearUnavailableFirFilters());
    }

    /// <summary>The message naming what the device could not keep; null when it kept everything.</summary>
    public static string? Notice(DspProcessorWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var notices = new List<string>();
        if (write.ClearedRotations > 0)
        {
            notices.Add(
                $"{write.ClearedRotations} channel side" +
                (write.ClearedRotations == 1 ? " had" : "s had") +
                " a phase rotation dialled in, and this processor has no such " +
                "control.\r\n\r\nThe angle" +
                (write.ClearedRotations == 1 ? " was" : "s were") +
                " cleared: left in place it would go on bending the curves with " +
                "nothing on screen to explain it, and the tuning sheet would go on " +
                "naming a control this device does not have.");
        }
        if (write.ClearedFirFilters > 0)
        {
            notices.Add(
                $"{write.ClearedFirFilters} channel side" +
                (write.ClearedFirFilters == 1 ? " had" : "s had") +
                " a FIR filter loaded, and this processor has no FIR stage.\r\n\r\n" +
                "The kernel" + (write.ClearedFirFilters == 1 ? " was" : "s were") +
                " detached: left in place it would go on shaping the curves with " +
                "nothing on screen to explain it, and the tuning sheet would go on " +
                "naming a file this device cannot take.");
        }

        return notices.Count > 0 ? string.Join("\r\n\r\n", notices) : null;
    }
}
