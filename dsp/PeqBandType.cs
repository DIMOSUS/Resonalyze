namespace Resonalyze.Dsp;

/// <summary>
/// The shape of a filter slot: a bell, or one of the two shelves. All three take
/// the same three numbers — centre frequency, Q and gain — but read two of them
/// differently, which is the whole reason the type has to travel with the band.
/// </summary>
/// <remarks>
/// For both shelves the RBJ cookbook puts <see cref="PeqBand.FrequencyHz"/> at the
/// MIDDLE of the transition, where the response has reached half the shelf gain in
/// dB, and <see cref="PeqBand.Q"/> controls the knee rather than a bandwidth:
/// 1/sqrt(2) ~ 0.707 is the steepest shelf that stays monotonic, and anything
/// above it overshoots before settling. A shelf therefore has no "bandwidth", and
/// the Q conventions of <see cref="PeqQConvention"/> — which restate a bell's
/// bandwidth — do not apply to one.
/// </remarks>
public enum PeqBandType
{
    /// <summary>Peaking / bell, the default: a boost or cut centred on the band.</summary>
    Peaking,

    /// <summary>Low shelf: everything below the band is lifted or lowered.</summary>
    LowShelf,

    /// <summary>High shelf: everything above the band is lifted or lowered.</summary>
    HighShelf
}
