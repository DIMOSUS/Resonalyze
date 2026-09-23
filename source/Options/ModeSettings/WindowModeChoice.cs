using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>Fixed or FDW and the FDW cycles, as the two lists of the Frequency Response, Phase and Group Delay panels
/// show them; a stored cycle count off the list reads as the default, in the settings file too.</summary>
internal readonly record struct WindowModeChoice(PhaseWindowMode Mode, int Cycles)
{
    public static IReadOnlyList<int> CycleChoices { get; } = [4, 6, 8];

    public static int ValidCycles(int cycles) =>
        CycleChoices.Contains(cycles) ? cycles : PhaseAnalysisSettings.DefaultFdwCycles;

    /// <summary>Any mode but Fixed shows as FDW.</summary>
    public static WindowModeChoice From(PhaseWindowMode mode, int cycles) =>
        new(mode == PhaseWindowMode.Fixed ? PhaseWindowMode.Fixed : PhaseWindowMode.FrequencyDependent, ValidCycles(cycles));

    /// <summary>The mode list holds Fixed, then FDW.</summary>
    public static PhaseWindowMode ModeAt(int index) =>
        index == 0 ? PhaseWindowMode.Fixed : PhaseWindowMode.FrequencyDependent;

    public int ModeIndex => Mode == PhaseWindowMode.Fixed ? 0 : 1;

    public bool CyclesEditable => Mode == PhaseWindowMode.FrequencyDependent;
}
