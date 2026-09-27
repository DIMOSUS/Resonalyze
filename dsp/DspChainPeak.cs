using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>The highest magnitude a chain reaches in a band: what a full-scale sine at that frequency leaves the
/// processor at. See docs/tech/dsp-chain-response.md#peak-gain.</summary>
public static class DspChainPeak
{
    // 1/48 octave over 20 Hz–20 kHz; every candidate is then refined, so the grid only has to land near each peak.
    private const int PointsPerOctave = 48;

    // A FIR's lobes are about rate / taps wide, so its bins are read at a quarter of that.
    private const int FirOversampling = 4;

    // Local maxima this close to the highest are refined too: a narrow peak (high Q, or a FIR ripple) can sit lower on
    // the grid than a broad one it actually tops. The highest few are enough.
    private const double CandidateWindowDb = 6.0;

    private const int MaximumCandidates = 8;

    private const int RefineIterations = 24;

    private const double AudioBandLowHz = 20;
    private const double AudioBandHighHz = 20_000;

    // Just under Nyquist, where a bilinear filter's response is still defined, so 44.1 kHz still reaches 20 kHz.
    private const double HighestRateFraction = 0.49;

    /// <summary><see cref="Find"/> over 20 Hz – 20 kHz, the headroom read-outs' band.</summary>
    public static (double FrequencyHz, double GainDb) InAudioBand(DspChannelChain chain, int processorRate) =>
        Find(chain, processorRate, AudioBandLowHz, Math.Min(AudioBandHighHz, HighestRateFraction * processorRate));

    /// <param name="processorRate">The rate the chain is realized at (its filters warp by it).</param>
    public static (double FrequencyHz, double GainDb) Find(
        DspChannelChain chain, int processorRate, double lowHz, double highHz)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (lowHz <= 0 || highHz <= lowHz)
        {
            throw new ArgumentException("Require 0 < lowHz < highHz.");
        }

        PreparedDspResponse response = PreparedDspResponse.Create(chain, processorRate);
        int count = Math.Max(3, (int)Math.Ceiling(Math.Log2(highHz / lowHz) * PointsPerOctave) + 1);
        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(lowHz, highHz, count);
        var candidates = new List<(double LowHz, double HighHz, double GainDb)>();
        AddLocalMaxima(candidates, grid, Gains(response.Responses(grid)));
        if (chain.Fir is { } fir)
        {
            AddFirBinMaxima(candidates, chain with { Fir = null }, fir, processorRate, lowHz, highHz);
        }

        double top = candidates.Max(candidate => candidate.GainDb);
        (double FrequencyHz, double GainDb) best = (0, double.NegativeInfinity);
        foreach ((double low, double high, double _) in candidates
            .Where(candidate => candidate.GainDb >= top - CandidateWindowDb)
            .OrderByDescending(candidate => candidate.GainDb)
            .Take(MaximumCandidates))
        {
            (double FrequencyHz, double GainDb) refined = Refine(response, low, high);
            if (refined.GainDb > best.GainDb)
            {
                best = refined;
            }
        }

        return best;
    }

    // The kernel's DFT at bins a quarter of a lobe apart, times the IIR stages read at those bins.
    private static void AddFirBinMaxima(
        List<(double LowHz, double HighHz, double GainDb)> candidates,
        DspChannelChain iirChain,
        FirFilter fir,
        int processorRate,
        double lowHz,
        double highHz)
    {
        int length = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(1_024, FirOversampling * fir.Length));
        double binHz = (double)processorRate / length;
        int first = Math.Max(1, (int)Math.Ceiling(lowHz / binHz));
        int last = Math.Min(length / 2, (int)Math.Floor(highHz / binHz));
        if (last - first < 2)
        {
            return;
        }

        Complex[] spectrum = fir.Spectrum(length);
        double[] frequencies = [.. Enumerable.Range(first, last - first + 1).Select(bin => bin * binHz)];
        Complex[] iir = PreparedDspResponse.Create(iirChain, processorRate).Responses(frequencies);
        double[] gains = new double[frequencies.Length];
        for (int i = 0; i < gains.Length; i++)
        {
            gains[i] = DataHelper.AmplitudeToDecibels(iir[i].Magnitude * spectrum[first + i].Magnitude);
        }

        AddLocalMaxima(candidates, frequencies, gains);
    }

    private static double[] Gains(IReadOnlyList<Complex> responses) =>
        [.. responses.Select(value => DataHelper.AmplitudeToDecibels(value.Magnitude))];

    private static void AddLocalMaxima(
        List<(double LowHz, double HighHz, double GainDb)> candidates,
        IReadOnlyList<double> frequencies,
        double[] gains)
    {
        int count = gains.Length;
        for (int i = 0; i < count; i++)
        {
            if ((i == 0 || gains[i] >= gains[i - 1]) && (i == count - 1 || gains[i] >= gains[i + 1]))
            {
                candidates.Add((frequencies[Math.Max(0, i - 1)], frequencies[Math.Min(count - 1, i + 1)], gains[i]));
            }
        }
    }

    // Golden-section search on log frequency between a local maximum's neighbours, which hold that one peak.
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

        // An edge maximum keeps its grid value when the band's end is higher than anything inside the bracket.
        double edgeLow = DataHelper.AmplitudeToDecibels(response.Response(lowHz).Magnitude);
        double edgeHigh = DataHelper.AmplitudeToDecibels(response.Response(highHz).Magnitude);
        (double FrequencyHz, double GainDb) inner = gainC >= gainD ? (Math.Exp(c), gainC) : (Math.Exp(d), gainD);
        return new[] { inner, (lowHz, edgeLow), (highHz, edgeHigh) }.MaxBy(point => point.Item2);
    }
}
