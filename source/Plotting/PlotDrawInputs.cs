namespace Resonalyze;

/// <summary>What a main-plot draw reads of the open measurement and the compare selection, so a change that moves none
/// of it draws nothing and a new name only retitles.</summary>
internal readonly record struct PlotDrawInputs(
    Mode Mode,
    MeasurementResult? Result,
    bool IncludesCurves,
    CompareMeasurementSelection? Compare,
    string? SourceName)
{
    /// <param name="compare">Kept only where the draw shows it: with curves, in a mode that draws the compared record.</param>
    public static PlotDrawInputs Read(
        Mode mode,
        AnalyzerDocument document,
        bool includesCurves,
        CompareMeasurementSelection? compare) =>
        new(
            mode,
            document.Result,
            includesCurves,
            includesCurves && DrawsCompare(mode) ? compare : null,
            document.SourceName);

    public PlotRedraw RedrawFrom(PlotDrawInputs? drawn) =>
        drawn is not { } previous || previous with { SourceName = SourceName } != this
            ? PlotRedraw.Rebuild
            : previous.SourceName != SourceName
                ? PlotRedraw.Retitle
                : PlotRedraw.None;

    private static bool DrawsCompare(Mode mode) =>
        mode is Mode.FrequencyResponse or Mode.PhaseResponse or Mode.GroupDelay or Mode.ImpulseResponse;
}

internal enum PlotRedraw
{
    None,
    Retitle,
    Rebuild
}
