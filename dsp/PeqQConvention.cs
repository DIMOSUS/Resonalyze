namespace Resonalyze.Dsp;

/// <summary>
/// How a DSP defines peaking-band Q: <c>BW = m · f0 / Q</c>, differing only in <c>m</c> (REW's names).
/// See docs/tech/eq-auto-tuner.md#q-conventions.
/// </summary>
public enum PeqQConvention
{
    /// <summary>RBJ cookbook, what <see cref="PeakingBiquad"/> realizes: <c>m = 1</c> (constant Q).</summary>
    Rbj,

    /// <summary>Symmetric / Zölzer: <c>m = sqrt(|gain|)</c>; confirmed on AMP Panacea.</summary>
    Symmetric,

    /// <summary>Classic: <c>m = sqrt(gain)</c> with SIGNED gain, so cuts are narrower than RBJ. Rare (JL Audio TwK-88).</summary>
    Classic
}

/// <summary>Exact, invertible Q rescale between conventions (pinned by <c>PeqQConventionTests</c>).</summary>
public static class PeqQConventions
{
    /// <summary>Restates an RBJ-Q band as the numbers to dial into a device using <paramref name="convention"/>.</summary>
    public static PeqBand ToConvention(PeqBand band, PeqQConvention convention) =>
        band with { Q = band.Q * Scale(band, convention) };

    public static PeqBand ToRbj(PeqBand band, PeqQConvention convention) =>
        band with { Q = band.Q / Scale(band, convention) };

    public static string Describe(PeqQConvention convention) => convention switch
    {
        PeqQConvention.Symmetric => "Symmetric Q — Zölzer/DAFX (proportional)",
        PeqQConvention.Classic => "Classic Q (asymmetric: boost wider, cut narrower)",
        _ => "RBJ Q — cookbook (constant)"
    };

    /// <summary>Multipliers are <see cref="Scale"/> read at the named gains, so they cannot drift from the maths.</summary>
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

    public static string DescribeShort(PeqQConvention convention) => convention switch
    {
        PeqQConvention.Symmetric => "Symmetric",
        PeqQConvention.Classic => "Classic",
        _ => "RBJ"
    };

    // The multiplier m. Transparent bands, shelves (Q is a knee) and all-pass (no half-gain points) are not scaled.
    private static double Scale(PeqBand band, PeqQConvention convention)
    {
        if (band.IsTransparent || band.Type.IsShelving() || band.Type.IsAllPass())
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
