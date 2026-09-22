using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Which of a block's crossover fields take input: only the edges its role runs, and the ripple only on a
/// Chebyshev edge that runs.</summary>
internal readonly record struct VirtualCrossoverChannelAvailability(
    bool HighPass,
    bool LowPass,
    bool HighPassRipple,
    bool LowPassRipple)
{
    public static VirtualCrossoverChannelAvailability Of(
        CrossoverKind kind,
        CrossoverFilterFamily? highPassFamily,
        CrossoverFilterFamily? lowPassFamily)
    {
        bool highPass = kind is CrossoverKind.HighPass or CrossoverKind.BandPass;
        bool lowPass = kind is CrossoverKind.LowPass or CrossoverKind.BandPass;
        return new VirtualCrossoverChannelAvailability(
            highPass,
            lowPass,
            highPass && highPassFamily is CrossoverFilterFamily.Chebyshev,
            lowPass && lowPassFamily is CrossoverFilterFamily.Chebyshev);
    }
}
