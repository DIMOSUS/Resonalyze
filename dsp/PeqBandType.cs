namespace Resonalyze.Dsp;

/// <summary>
/// Filter slot shape. Shelves put FrequencyHz at the transition MIDDLE and Q sets the knee (0.707 = steepest
/// monotonic), so Q conventions do not apply. See docs/tech/eq-auto-tuner.md#band-shapes.
/// </summary>
public enum PeqBandType
{
    Peaking,

    LowShelf,

    HighShelf,

    /// <summary>Unity magnitude, −90° at the corner. No gain or Q; Q stays positive for validators.</summary>
    AllPassFirstOrder,

    /// <summary>Unity magnitude, −180° at the corner; Q sets how abruptly phase turns (see <see cref="AllPassFilter"/>).</summary>
    AllPassSecondOrder
}

public static class PeqBandTypes
{
    /// <summary>Use this, not <c>!= Peaking</c>, so an undefined value is a bell everywhere.</summary>
    public static bool IsShelving(this PeqBandType type) =>
        type is PeqBandType.LowShelf or PeqBandType.HighShelf;

    public static bool IsAllPass(this PeqBandType type) =>
        type is PeqBandType.AllPassFirstOrder or PeqBandType.AllPassSecondOrder;
}
