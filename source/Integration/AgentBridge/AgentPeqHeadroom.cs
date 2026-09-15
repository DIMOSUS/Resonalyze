using Resonalyze.Dsp;

namespace Resonalyze.Integration.AgentBridge;

/// <summary>Peak of a PEQ bank's NET response at the processor's rate: above 0 dB a full-scale signal clips; individual band signs say nothing about headroom.</summary>
internal static class AgentPeqHeadroom
{
    private const int GridPoints = 512;
    private const int RefinementSteps = 24;

    /// <summary>Over 20 Hz to min(20 kHz, Nyquist). A log grid can step over a narrow bell (Q is unbounded), so band centres are sampled too and grid maxima refined.</summary>
    public static (double PeakDb, double PeakHz) Peak(
        double preampDb, IReadOnlyList<PeqBand> bands, int processorSampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(bands);

        if (bands.Count == 0)
        {
            return (preampDb, AgentCurveSampling.BroadbandLowHz);
        }

        PreparedDspResponse response = PreparedDspResponse.Create(
            new DspChannelChain(0, 0, false, CrossoverSpec.Off, new EqualizationCurve(bands, preampDb)),
            processorSampleRateHz);
        double lowHz = AgentCurveSampling.BroadbandLowHz;
        double highHz = Math.Min(AgentCurveSampling.BroadbandHighHz, processorSampleRateHz / 2.0 * 0.999);

        List<double> grid = [.. EqualizationCurve.LogFrequencyGrid(lowHz, highHz, GridPoints)];
        foreach (PeqBand band in bands)
        {
            if (band.FrequencyHz > lowHz && band.FrequencyHz < highHz)
            {
                grid.Add(band.FrequencyHz);
            }
        }
        grid.Sort();

        double[] levels = grid.Select(frequency => Decibels(response, frequency)).ToArray();
        double peakDb = double.NegativeInfinity;
        double peakHz = lowHz;
        for (int index = 0; index < grid.Count; index++)
        {
            bool localMaximum =
                (index == 0 || levels[index] >= levels[index - 1]) &&
                (index == grid.Count - 1 || levels[index] >= levels[index + 1]);
            if (!localMaximum)
            {
                continue;
            }

            (double hz, double db) = Refine(
                response,
                grid[Math.Max(0, index - 1)],
                grid[Math.Min(grid.Count - 1, index + 1)],
                grid[index],
                levels[index]);
            if (db > peakDb)
            {
                peakDb = db;
                peakHz = hz;
            }
        }

        return (peakDb, peakHz);
    }

    // Golden-section in log-frequency between a local maximum's grid neighbours.
    private static (double Hz, double Db) Refine(
        PreparedDspResponse response, double lowHz, double highHz, double bestHz, double bestDb)
    {
        const double ratio = 0.6180339887498949;
        double a = Math.Log(lowHz);
        double b = Math.Log(highHz);
        double x1 = b - ratio * (b - a);
        double x2 = a + ratio * (b - a);
        double f1 = Decibels(response, Math.Exp(x1));
        double f2 = Decibels(response, Math.Exp(x2));
        for (int step = 0; step < RefinementSteps; step++)
        {
            if (f1 > f2)
            {
                b = x2;
                x2 = x1;
                f2 = f1;
                x1 = b - ratio * (b - a);
                f1 = Decibels(response, Math.Exp(x1));
            }
            else
            {
                a = x1;
                x1 = x2;
                f1 = f2;
                x2 = a + ratio * (b - a);
                f2 = Decibels(response, Math.Exp(x2));
            }
        }

        (double hz, double db) = f1 > f2 ? (Math.Exp(x1), f1) : (Math.Exp(x2), f2);
        return db > bestDb ? (hz, db) : (bestHz, bestDb);
    }

    private static double Decibels(PreparedDspResponse response, double frequencyHz) =>
        DataHelper.AmplitudeToDecibels(response.Response(frequencyHz).Magnitude);
}
