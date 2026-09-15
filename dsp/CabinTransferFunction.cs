namespace Resonalyze.Dsp;

public enum CabinBodyStyle
{
    Sedan,
    CompactSedan,
    Hatchback,
    Wagon,
    Suv,
    BmwF30SkiHatch
}

/// <summary>A TYPICAL cabin bass-rise envelope (not a target curve); modal detail stays audible on purpose.
/// See docs/tech/dsp-cabin-transfer-function.md.</summary>
public sealed class CabinTransferFunction
{
    // Fits the published averaged-sedan table within ~0.5 dB.
    private const double KneeSharpness = 3.0;

    // The FIR design samples 0 Hz, where the log slope is undefined.
    private const double MinimumFrequencyHz = 1.0;

    // Caps only the extrapolated infrasonic tail (above every preset's deepest anchor).
    private const double MaximumSubtractionDb = 40.0;

    private readonly Func<double, double> gainDb;

    private CabinTransferFunction(Func<double, double> gainDb)
    {
        this.gainDb = gainDb;
    }

    public static CabinTransferFunction FromBodyStyle(CabinBodyStyle bodyStyle) =>
        bodyStyle switch
        {
            CabinBodyStyle.Sedan => Sloped(cornerHz: 70, slopeDbPerOctave: 9),
            CabinBodyStyle.CompactSedan => Sloped(80, 13.5),
            CabinBodyStyle.Hatchback => Sloped(80, 12),
            CabinBodyStyle.Wagon => Sloped(70, 11),
            CabinBodyStyle.Suv => Sloped(55, 10),
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

    public double Evaluate(double frequencyHz) =>
        Math.Min(
            gainDb(Math.Max(frequencyHz, MinimumFrequencyHz)),
            MaximumSubtractionDb);

    private static CabinTransferFunction Sloped(
        double cornerHz, double slopeDbPerOctave) =>
        new(frequencyHz =>
            slopeDbPerOctave * SoftPlus(Math.Log2(cornerHz / frequencyHz)));

    private static CabinTransferFunction Tabulated(
        params (double FrequencyHz, double GainDb)[] points) =>
        new(frequencyHz =>
        {
            if (frequencyHz >= points[^1].FrequencyHz)
            {
                return 0.0;
            }
            if (frequencyHz <= points[0].FrequencyHz)
            {
                return points[0].GainDb;
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

    private static double SoftPlus(double octaves)
    {
        double scaled = KneeSharpness * octaves;
        return (Math.Max(scaled, 0) +
            Math.Log(1 + Math.Exp(-Math.Abs(scaled)))) / KneeSharpness;
    }
}
