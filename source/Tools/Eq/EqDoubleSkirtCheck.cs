namespace Resonalyze;

/// <summary>An imported target cut with the channel's crossover gets its slope twice once Crossover in target adds it.</summary>
internal static class EqDoubleSkirtCheck
{
    /// <summary>Fall over the octave past the channel's −3 dB point read as a skirt (LR24 ~20, steepest shelf under 7); an octave
    /// because a steep FIR skirt's own span is too narrow to read the file across.</summary>
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
                        imported.Evaluate(grid[edge]) -
                        imported.Evaluate(grid[edge] * Math.Pow(2, direction)) >= FallDb)
                    {
                        repeated.Add(grid[edge]);
                    }

                    break;
                }
            }
        }

        return repeated;
    }

    /// <summary>Remembers the last answer: a FIR skirt costs a pass over the kernel per frequency, too much for every redraw.</summary>
    internal sealed class Cache
    {
        private (ImportedTargetCurve? Imported, EqTargetSlope? Slope, bool CrossoverInTarget, int SampleRateHz)? key;
        private string? warning;

        public string? Warning(TargetCurveSpec spec, EqTargetSlope? slope, bool crossoverInTarget, int sampleRateHz)
        {
            var current = (spec.Imported, slope, crossoverInTarget, sampleRateHz);
            if (key is not { } last || !last.Equals(current))
            {
                warning = EqDoubleSkirtCheck.Warning(spec, slope, crossoverInTarget, sampleRateHz);
                key = current;
            }

            return warning;
        }
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
