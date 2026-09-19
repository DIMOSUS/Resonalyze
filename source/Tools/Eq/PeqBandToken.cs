using Resonalyze.Dsp;

namespace Resonalyze;

internal static class PeqBandToken
{
    /// <summary>A strip's header token; AP1/AP2 match Audiotec PC-Tool slot names.</summary>
    public static string Of(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf => "LS",
        PeqBandType.HighShelf => "HS",
        PeqBandType.AllPassFirstOrder => "AP1",
        PeqBandType.AllPassSecondOrder => "AP2",
        _ => "PK"
    };
}
