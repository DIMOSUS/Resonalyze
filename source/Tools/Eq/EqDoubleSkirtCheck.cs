namespace Resonalyze;

/// <summary>An imported target cut with the channel's crossover gets its slope twice once Crossover in target adds it.</summary>
internal static class EqDoubleSkirtCheck
{
    /// <summary>Fall across the skirt's own −3 → −18 dB span read as a skirt: ~15 dB for one, under 7 for the steepest shelf.</summary>
    public const double FallDb = 10;

    private const double PassbandDb = -3;
    private const double LowestHz = 10;
    private const double HighestHz = 24_000;
    private const int StepsPerOctave = 24;

    /// <summary>The frequencies of the skirts the imported curve repeats, or empty.</summary>
    public static IReadOnlyList<double> RepeatedSkirts(
        TargetCurveSpec spec,
        EqTargetSlope slope,
        int sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(slope);
        if (spec.Imported is not { } imported)
        {
            return [];
        }

        double step = Math.Pow(2, 1.0 / StepsPerOctave);
        var grid = new List<double>();
        for (double hz = LowestHz; hz <= HighestHz; hz *= step)
        {
            grid.Add(hz);
        }

        double[] shape = grid
            .Select(hz => EqTargetCrossover.ShapeDb(slope, hz, sampleRateHz))
            .ToArray();
        int centre = Array.IndexOf(shape, shape.Max());
        var repeated = new List<double>(2);
        foreach (int direction in new[] { -1, 1 })
        {
            int? pass = null;
            for (int i = centre; i >= 0 && i < grid.Count; i += direction)
            {
                if (pass == null && shape[i] <= PassbandDb)
                {
                    pass = i;
                }

                if (shape[i] <= -EqTargetCrossover.SlopeWindowFallDb)
                {
                    if (pass is { } edge &&
                        imported.Evaluate(grid[edge]) - imported.Evaluate(grid[i]) >= FallDb)
                    {
                        repeated.Add(grid[edge]);
                    }

                    break;
                }
            }
        }

        return repeated;
    }

    public static string? Warning(
        TargetCurveSpec spec,
        EqTargetSlope? slope,
        bool crossoverInTarget,
        int sampleRateHz)
    {
        if (!crossoverInTarget || slope == null)
        {
            return null;
        }

        IReadOnlyList<double> skirts = RepeatedSkirts(spec, slope, sampleRateHz);
        if (skirts.Count == 0)
        {
            return null;
        }

        string where = string.Join(" and ", skirts.Select(hz => hz >= 1_000 ? $"{hz / 1_000:0.#} kHz" : $"{hz:0} Hz"));
        return $"Crossover counted twice at {where}: the imported target already rolls off there and Crossover " +
            "in target adds it again. Import the curve without the crossover, or untick Crossover in target.";
    }
}
