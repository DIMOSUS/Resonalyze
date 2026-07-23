namespace Resonalyze.Dsp;

/// <summary>Body styles with a bundled typical cabin transfer function.</summary>
public enum CabinBodyStyle
{
    Sedan,
    CompactSedan,
    Hatchback,
    Wagon,
    Suv,
    BmwF30SkiHatch
}

/// <summary>
/// A TYPICAL cabin transfer function — the bass rise a car interior adds to a
/// source's free-field response. Below the corner (the pressure-zone onset,
/// c/2L for the cabin's longest dimension L) the cabin behaves like a sealed
/// box around the listener and the gain climbs at a constant dB/octave with no
/// saturation: published measurements reach +17 dB at 20 Hz for an averaged
/// sedan, +23.5 dB for a hatchback, +27 dB for a compact sedan with the
/// enclosure coupled straight into the cabin. This is deliberately NOT a
/// target curve — targets sit far lower because the subwoofer's own
/// low-frequency roll-off eats most of the cabin rise.
/// <para>
/// Most presets are the two-parameter pressure-zone shape (corner + slope with
/// a soft knee). A trunk coupled through an opening does not fit it — the
/// trunk-plus-hatch path behaves as an acoustic low-pass and the measured
/// curve breaks over a steep cliff near the corner — so such presets are
/// tabulated from an actual measurement instead.
/// </para>
/// <para>
/// Either way the shape is the idealized envelope only. Real cabins ride
/// modal peaks and nulls on top of it, and those stay in the audition on
/// purpose: subtracting the typical envelope leaves exactly this car's
/// deviation from it audible.
/// </para>
/// </summary>
public sealed class CabinTransferFunction
{
    // Knee width of the parametric corner transition, chosen against the
    // published averaged-sedan table (17/8/3/1.3 dB at 20/40/63/80 Hz for a
    // 70 Hz corner): sharpness 3 reproduces those four anchors within ~0.5 dB.
    private const double KneeSharpness = 3.0;

    // Evaluation floor: the FIR design samples the curve at the 0 Hz bin,
    // where the log-frequency slope has no value. Clamping to 1 Hz keeps DC
    // finite while still attenuating it far below anything audible.
    private const double MinimumFrequencyHz = 1.0;

    private readonly Func<double, double> gainDb;

    private CabinTransferFunction(Func<double, double> gainDb)
    {
        this.gainDb = gainDb;
    }

    /// <summary>
    /// The typical curve for a body style. Corner tracks cabin length
    /// (c/2L: lower for the long SUV cabin, higher for short ones); slope
    /// tracks how directly the low-frequency source couples into how sealed a
    /// volume (theory says 12 dB/oct for a perfectly tight cabin, leaky
    /// trunk-isolated sedans measure nearer 9).
    /// </summary>
    public static CabinTransferFunction FromBodyStyle(CabinBodyStyle bodyStyle) =>
        bodyStyle switch
        {
            CabinBodyStyle.Sedan => Sloped(cornerHz: 70, slopeDbPerOctave: 9),
            CabinBodyStyle.CompactSedan => Sloped(80, 13.5),
            CabinBodyStyle.Hatchback => Sloped(80, 12),
            CabinBodyStyle.Wagon => Sloped(70, 11),
            CabinBodyStyle.Suv => Sloped(55, 10),
            // Read off an owner measurement (driver's seat vs nearfield at the
            // sub, normalized to the 80–150 Hz shelf): ~11 dB/oct below 50 Hz,
            // then a ~30 dB/oct cliff where the trunk-through-hatch coupling
            // gives out.
            CabinBodyStyle.BmwF30SkiHatch => Tabulated(
                (20, 34.0),
                (25, 30.0),
                (31.5, 27.5),
                (40, 23.5),
                (50, 19.5),
                (63, 11.0),
                (70, 6.5),
                (80, 1.0),
                (95, 0.0)),
            _ => throw new ArgumentOutOfRangeException(nameof(bodyStyle))
        };

    /// <summary>Cabin gain in dB at the given frequency (0 above the corner).</summary>
    public double Evaluate(double frequencyHz) =>
        gainDb(Math.Max(frequencyHz, MinimumFrequencyHz));

    private static CabinTransferFunction Sloped(
        double cornerHz, double slopeDbPerOctave) =>
        new(frequencyHz =>
            slopeDbPerOctave * SoftPlus(Math.Log2(cornerHz / frequencyHz)));

    // Piecewise-linear in log-frequency over measured (Hz, dB) anchors, given
    // in ascending frequency with the last gain 0: flat (0) above the last
    // point, the first segment's slope extended below the first.
    private static CabinTransferFunction Tabulated(
        params (double FrequencyHz, double GainDb)[] points) =>
        new(frequencyHz =>
        {
            if (frequencyHz >= points[^1].FrequencyHz)
            {
                return 0.0;
            }

            int upper = 1;
            while (upper < points.Length - 1 &&
                points[upper].FrequencyHz <= frequencyHz)
            {
                upper++;
            }

            (double lowHz, double lowDb) = points[upper - 1];
            (double highHz, double highDb) = points[upper];
            double position = Math.Log2(frequencyHz / lowHz) /
                Math.Log2(highHz / lowHz);
            return lowDb + (highDb - lowDb) * position;
        });

    // Softplus in octaves: → x well below the corner (constant dB/octave), → 0
    // well above it, smooth at the knee. Written in the overflow-safe form —
    // exp() only ever sees a non-positive argument.
    private static double SoftPlus(double octaves)
    {
        double scaled = KneeSharpness * octaves;
        return (Math.Max(scaled, 0) +
            Math.Log(1 + Math.Exp(-Math.Abs(scaled)))) / KneeSharpness;
    }
}
