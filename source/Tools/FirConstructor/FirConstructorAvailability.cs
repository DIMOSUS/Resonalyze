using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Which design fields a draft reads: a corner only for its side of the type, the family and slope only for the
/// IIR magnitude, beta only for Kaiser.</summary>
internal readonly record struct FirConstructorFields(
    bool HighPass,
    bool HighPassShape,
    bool LowPass,
    bool LowPassShape,
    bool KaiserBeta);

/// <summary>A design on its way back to the side it was opened for.</summary>
internal sealed record FirConstructorReturn(
    FirConstructorReturnToken Token,
    FirFilter Kernel,
    FirCrossoverDesign Design);

/// <summary>What the constructor's fields and buttons take: the draft's fields, Export, and Return.</summary>
internal static class FirConstructorAvailability
{
    public static FirConstructorFields Fields(FirCrossoverDesign draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        bool usesHigh = draft.Kind is CrossoverKind.HighPass or CrossoverKind.BandPass;
        bool usesLow = draft.Kind is CrossoverKind.LowPass or CrossoverKind.BandPass;
        bool iir = draft.Method == FirCrossoverMethod.IirMagnitude;
        return new FirConstructorFields(
            usesHigh, usesHigh && iir, usesLow, usesLow && iir, draft.Window == FirWindow.Kaiser);
    }

    public static bool CanExport(FirConstructorSession session) =>
        session.Kernel != null && !session.RebuildPending;

    /// <summary>Only a landed design returns; a bare kernel file is imported on the Virtual DSP side, where it keeps its
    /// name.</summary>
    public static FirConstructorReturn? Return(FirConstructorSession session) =>
        !session.RebuildPending && session.Handoff is { Token: var token } &&
        session.Kernel is { } kernel && session.Design is { } design
            ? new FirConstructorReturn(token, kernel, design)
            : null;
}
