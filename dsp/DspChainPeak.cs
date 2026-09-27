namespace Resonalyze.Dsp;

/// <summary>The highest magnitude a chain reaches in a band: what a full-scale sine at that frequency leaves the
/// processor at. See docs/tech/dsp-chain-response.md#peak-gain.</summary>
public static class DspChainPeak
{
    // 1/48 octave over 20 Hz–20 kHz; every candidate is then refined, so the grid only has to land near each peak.
    private const int PointsPerOctave = 48;

    // Local grid maxima this close to the highest are refined too: a narrow peak (high Q, or a FIR ripple) can sit
    // lower on the grid than a broad one it actually tops. The highest few are enough.
    private const double CandidateWindowDb = 6.0;

    private const int MaximumCandidates = 8;

    private const int RefineIterations = 24;

    public static (double FrequencyHz, double GainDb) Find(PreparedDspResponse response, double lowHz, double highHz)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (lowHz <= 0 || highHz <= lowHz)
        {
            throw new ArgumentException("Require 0 < lowHz < highHz.");
        }

        int count = Math.Max(3, (int)Math.Ceiling(Math.Log2(highHz / lowHz) * PointsPerOctave) + 1);
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(lowHz, highHz, count);
        double[] gains = [.. response.Responses(grid).Select(value => DataHelper.AmplitudeToDecibels(value.Magnitude))];
        double gridBest = gains.Max();

        IEnumerable<int> candidates = Enumerable.Range(0, count)
            .Where(i => (i == 0 || gains[i] >= gains[i - 1]) && (i == count - 1 || gains[i] >= gains[i + 1]))
            .Where(i => gains[i] >= gridBest - CandidateWindowDb)
            .OrderByDescending(i => gains[i])
            .Take(MaximumCandidates);
        (double FrequencyHz, double GainDb) best = (grid[Array.IndexOf(gains, gridBest)], gridBest);
        foreach (int i in candidates)
        {
            (double FrequencyHz, double GainDb) refined =
                Refine(response, grid[Math.Max(0, i - 1)], grid[Math.Min(count - 1, i + 1)]);
            if (refined.GainDb > best.GainDb)
            {
                best = refined;
            }
        }

        return best;
    }

    // Golden-section search on log frequency; the bracket holds one peak because the grid resolves the chain.
    private static (double FrequencyHz, double GainDb) Refine(PreparedDspResponse response, double lowHz, double highHz)
    {
        double GainAt(double logHz) => DataHelper.AmplitudeToDecibels(response.Response(Math.Exp(logHz)).Magnitude);

        double ratio = (Math.Sqrt(5.0) - 1.0) / 2.0;
        double a = Math.Log(lowHz);
        double b = Math.Log(highHz);
        double c = b - ratio * (b - a);
        double d = a + ratio * (b - a);
        double gainC = GainAt(c);
        double gainD = GainAt(d);
        for (int iteration = 0; iteration < RefineIterations; iteration++)
        {
            if (gainC >= gainD)
            {
                b = d;
                d = c;
                gainD = gainC;
                c = b - ratio * (b - a);
                gainC = GainAt(c);
            }
            else
            {
                a = c;
                c = d;
                gainC = gainD;
                d = a + ratio * (b - a);
                gainD = GainAt(d);
            }
        }

        return gainC >= gainD ? (Math.Exp(c), gainC) : (Math.Exp(d), gainD);
    }
}
