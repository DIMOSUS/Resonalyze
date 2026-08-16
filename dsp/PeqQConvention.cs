namespace Resonalyze.Dsp;

/// <summary>
/// How a target DSP defines the Q of a peaking band. All three describe the same
/// biquad family and differ only in what the Q number means, so the same figure typed
/// into two devices produces two different bandwidths. Named after the conventions
/// REW uses, since that is the vocabulary a tuner meets elsewhere.
/// </summary>
/// <remarks>
/// Each convention states the bandwidth between the half-gain points as
/// <c>BW = m · f0 / Q</c> and differs only in the multiplier <c>m</c>, so converting a
/// band is a pure rescale of Q — see <see cref="PeqQConventions"/>.
/// </remarks>
public enum PeqQConvention
{
    /// <summary>
    /// RBJ Audio EQ Cookbook, what <see cref="PeakingBiquad"/> realizes: <c>BW = f0 / Q</c>,
    /// so the bandwidth does not depend on how deep the band cuts or boosts ("constant
    /// Q"). Equalizer APO, CamillaDSP and REW's own Generic/Extended equalisers read Q
    /// this way, as do Audiotec Fischer (HELIX / MATCH / BRAX), Audison/Hertz, Mosconi,
    /// miniDSP, QSC DSP-30 and StormAudio.
    /// </summary>
    Rbj,

    /// <summary>
    /// Symmetric Q, equivalently the Zölzer / DAFX peak filter: <c>BW = sqrt(|gain|) · f0 / Q</c>,
    /// so a band widens as it deepens ("proportional Q") and boost and cut of the same
    /// depth are mirror images. Used by Behringer DCX2496, Rockford Fosgate 3Sixty.3,
    /// Hypex Input EQ, rePhase, Crown USM810 and DSPeaker Anti-Mode Dual Core; confirmed
    /// on AMP Panacea (Cirrus Logic CS47048C). At small gains it is nearly
    /// indistinguishable from <see cref="Rbj"/>; at 15 dB the same Q number is over twice
    /// as wide.
    /// </summary>
    Symmetric,

    /// <summary>
    /// Classic Q: <c>BW = sqrt(gain) · f0 / Q</c> with the SIGNED gain, so the multiplier
    /// falls below 1 for a cut. Unlike the other two this is asymmetric — at one Q number
    /// a boost comes out wider than RBJ and a cut narrower. Rare: the JL Audio TwK-88 is
    /// the one processor REW documents for it, and JL's own VXi does not match it, so
    /// this is a property of the model rather than of the maker.
    /// </summary>
    Classic
}

/// <summary>
/// Translates the Q of a <see cref="PeqBand"/> between <see cref="PeqQConvention"/>s.
/// </summary>
/// <remarks>
/// The conventions are exactly reconcilable, not approximately: they are one biquad
/// family reached through different Q scales, so a band restated by
/// <see cref="ToConvention"/> has an identical magnitude response on the target device
/// and <see cref="ToRbj"/> restores it unchanged. <c>PeqQConventionTests</c> pins this
/// against an independently written Zölzer section and against REW's published
/// bandwidth figures.
///
/// Because the factor grows with the gain, it only matters for deep bands — ×1.19 at
/// 3 dB but ×2.37 at 15 dB — which is why a mismatched convention shows up as an EQ
/// that measures broader (or, under <see cref="PeqQConvention.Classic"/>, narrower)
/// than it was designed rather than as an obvious error.
/// </remarks>
public static class PeqQConventions
{
    /// <summary>
    /// Restates <paramref name="band"/> — whose Q is in the <see cref="PeqQConvention.Rbj"/>
    /// convention used throughout the library — as the numbers to dial into a device
    /// that reads Q per <paramref name="convention"/>. Frequency and gain are unchanged.
    /// </summary>
    public static PeqBand ToConvention(PeqBand band, PeqQConvention convention) =>
        band with { Q = band.Q * Scale(band, convention) };

    /// <summary>
    /// The inverse of <see cref="ToConvention"/>: takes a band as a device reading Q per
    /// <paramref name="convention"/> would realize it and restates it in the library's
    /// RBJ convention.
    /// </summary>
    public static PeqBand ToRbj(PeqBand band, PeqQConvention convention) =>
        band with { Q = band.Q / Scale(band, convention) };

    /// <summary>
    /// Full label naming the convention on a tuning sheet, so a printed sheet says which
    /// device its Q column was computed for.
    /// </summary>
    public static string Describe(PeqQConvention convention) => convention switch
    {
        PeqQConvention.Symmetric => "Symmetric Q — Zölzer/DAFX (proportional)",
        PeqQConvention.Classic => "Classic Q (asymmetric: boost wider, cut narrower)",
        _ => "RBJ Q — cookbook (constant)"
    };

    /// <summary>
    /// One line stating the bandwidth this convention realizes and how it moves with the
    /// gain, for a chooser that has to explain what the pick changes. The multipliers are
    /// <see cref="Scale"/> read at the named gains, so they cannot drift from the maths.
    /// </summary>
    public static string DescribeBandwidth(PeqQConvention convention) => convention switch
    {
        PeqQConvention.Symmetric =>
            "BW = √|gain| · Fc / Q. A band widens as it deepens, boost and cut alike: " +
            "at the same Q number it comes out 1.19× wider than RBJ at 3 dB, 2.00× at " +
            "12 dB and 2.37× at 15 dB.",
        PeqQConvention.Classic =>
            "BW = √gain · Fc / Q with the SIGNED gain, so the two directions disagree: " +
            "at the same Q number a boost comes out 2.00× wider than RBJ at +12 dB, " +
            "while a cut comes out 0.50× as wide — narrower — at −12 dB.",
        _ =>
            "BW = Fc / Q. The width between the half-gain points is the same whatever " +
            "the band's gain, which is why this one is called constant Q."
    };

    /// <summary>
    /// The processors known to read Q this way, so the convention can be picked by
    /// recognising the device rather than by knowing the maths. Following REW's
    /// equaliser reference; these are conventions of a MODEL, not of a manufacturer.
    /// </summary>
    public static string DescribeDevices(PeqQConvention convention) => convention switch
    {
        PeqQConvention.Symmetric =>
            "AMP Panacea (Cirrus Logic CS47048C), Behringer DCX2496, Rockford Fosgate " +
            "3Sixty.3, Hypex Input EQ, rePhase, Crown USM810, DSPeaker Anti-Mode Dual Core.",
        PeqQConvention.Classic =>
            "JL Audio TwK-88 — the one processor REW documents for it, and JL's own VXi " +
            "does not match it, so this is a property of the model rather than the maker.",
        _ =>
            "Equalizer APO, CamillaDSP, REW Generic/Extended, Audiotec Fischer (HELIX / " +
            "MATCH / BRAX), Audison/Hertz, Mosconi, miniDSP, QSC DSP-30, StormAudio."
    };

    /// <summary>
    /// Short label for a filter card or a combo box, where the full description does not
    /// fit. Matches REW's naming so the two can be cross-referenced.
    /// </summary>
    public static string DescribeShort(PeqQConvention convention) => convention switch
    {
        PeqQConvention.Symmetric => "Symmetric",
        PeqQConvention.Classic => "Classic",
        _ => "RBJ"
    };

    // The bandwidth multiplier m in BW = m * f0 / Q, which is exactly the factor Q must be
    // scaled by for the target to realize the same shape. Classic takes the SIGNED gain,
    // so its factor drops below 1 for a cut; Symmetric takes the magnitude, so boost and
    // cut scale alike.
    //
    // Transparent bands carry no gain and no meaningful Q, so they are left exactly as
    // they are rather than pushed through a scale that would still round-trip their
    // placeholder Q.
    //
    // Shelves are left alone for a stronger reason: the conventions restate a
    // BANDWIDTH between half-gain points, and a shelf has none — its Q sets the knee
    // (see PeqBandType). Scaling it by the gain would print a shelf that overshoots
    // where the designed one does not, so a shelf's Q goes to the sheet as it is.
    private static double Scale(PeqBand band, PeqQConvention convention)
    {
        if (band.IsTransparent || band.Type.IsShelving())
        {
            return 1.0;
        }

        return convention switch
        {
            PeqQConvention.Symmetric => Math.Pow(10.0, Math.Abs(band.GainDb) / 40.0),
            PeqQConvention.Classic => Math.Pow(10.0, band.GainDb / 40.0),
            _ => 1.0
        };
    }
}
