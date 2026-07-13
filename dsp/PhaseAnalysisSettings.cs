namespace Resonalyze.Dsp;

public enum PhaseWindowMode
{
    Fixed,
    FrequencyDependent
}

public enum PhaseDetrendMode
{
    Off,
    Auto,
    Manual
}

public sealed record PhaseAnalysisSettings(
    PhaseWindowMode WindowMode,
    int FdwCycles,
    PhaseDetrendMode DetrendMode,
    double ManualDetrendMilliseconds,
    double GateOffsetMs,
    double LeftMs,
    double PlateauMs,
    double RightMs,
    bool Unwrap,
    double SmoothingInverseOctaves)
{
    public const int DefaultFdwCycles = 6;

    public int ValidatedFdwCycles => FdwCycles is 4 or 6 or 8
        ? FdwCycles
        : DefaultFdwCycles;
}
