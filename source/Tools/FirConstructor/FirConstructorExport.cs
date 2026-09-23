using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>An export as the session held it when Export was pressed.</summary>
internal sealed record FirConstructorExportRequest(
    FirFilter Kernel,
    int RateHz,
    string? SourceName,
    string? Description,
    string SuggestedFileName);

/// <summary>What Export writes: the shown kernel at its rate, named after its design or the file it came from.</summary>
internal static class FirConstructorExport
{
    /// <summary>Null while nothing can be exported (no kernel, or a rebuild pending).</summary>
    public static FirConstructorExportRequest? Request(FirConstructorSession session)
    {
        if (!FirConstructorAvailability.CanExport(session) || session.Kernel is not { } kernel)
        {
            return null;
        }

        string fileName = session.Design is { } named
            ? $"FIR {FirCrossoverDescription.Short(named)}"
            : Path.GetFileNameWithoutExtension(session.KernelName) is { Length: > 0 } stem ? stem : "FIR";
        return new FirConstructorExportRequest(
            kernel,
            session.RateHz,
            session.KernelName,
            session.Design is { } described ? FirCrossoverDescription.Long(described) : null,
            fileName);
    }

    public static void Save(FirConstructorExportRequest request, string path) =>
        FirFilterFiles.Save(path, request.Kernel, request.RateHz, request.SourceName, request.Description);
}
