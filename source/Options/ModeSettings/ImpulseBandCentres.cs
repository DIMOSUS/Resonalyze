using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>The Impulse panel's band choices: the widths, the octave and third-octave centres a rate can realize, where
/// a stored value lands on them, and their labels. Off is width zero, so "no band" and "which band" are one setting.</summary>
internal static class ImpulseBandCentres
{
    public const double OctaveBand = 1.0;
    public const double ThirdOctaveBand = 1.0 / 3.0;

    public static IReadOnlyList<double> Widths { get; } = [0.0, OctaveBand, ThirdOctaveBand];

    private static readonly double[] OctaveCentres =
        [31.5, 63, 125, 250, 500, 1_000, 2_000, 4_000, 8_000, 16_000];

    private static readonly double[] ThirdOctaveCentres =
    [
        25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800,
        1_000, 1_250, 1_600, 2_000, 2_500, 3_150, 4_000, 5_000, 6_300, 8_000,
        10_000, 12_500, 16_000, 20_000
    ];

    /// <summary>With the band on, only centres whose whole octave-symmetric passband fits under Nyquist (the rule of
    /// <see cref="ImpulseResponseOptions.HasBandFilter"/>); off, the octave list, which still holds the kept centre.</summary>
    public static IReadOnlyList<double> For(double octaves, int sampleRate)
    {
        bool active = octaves > 0.0;
        double[] centres = active && octaves < OctaveBand ? ThirdOctaveCentres : OctaveCentres;
        if (!active || sampleRate <= 0)
        {
            return centres;
        }

        double[] realizable = centres
            .Where(centre => new ImpulseResponseOptions
            {
                BandFilterOctaves = octaves,
                BandCenterHz = centre
            }.HasBandFilter(sampleRate))
            .ToArray();
        return realizable.Length > 0 ? realizable : centres;
    }

    public static double NearestWidth(double octaves) =>
        octaves <= 0.0
            ? 0.0
            : Nearest([OctaveBand, ThirdOctaveBand], octaves);

    /// <summary>Nearest in octaves: linear Hz reads 250 as nearer to 500 than to 125.</summary>
    public static double Nearest(IReadOnlyList<double> values, double wanted) =>
        values.MinBy(value => Math.Abs(Math.Log2(value / wanted)));

    public static string WidthLabel(double octaves) => octaves switch
    {
        OctaveBand => "1 octave",
        ThirdOctaveBand => "1/3 octave",
        _ => "Off"
    };

    public static string CentreLabel(double hertz) =>
        hertz >= 1_000.0
            ? $"{hertz / 1_000.0:0.###} kHz"
            : $"{hertz:0.#} Hz";
}
