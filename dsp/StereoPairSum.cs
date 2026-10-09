using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>Where a stereo pair's two sides sum best: the split that puts them in phase at the listening position over
/// their band, with the runner-up optimum for the log.</summary>
/// <param name="LeftLaterMs">How much later than given the left side must play for the strongest sum.</param>
/// <param name="GainDb">The sum over the sides' power sum there: +3 dB is two equal sides fully in phase.</param>
/// <param name="SetAsideMs">A stronger optimum the cabin's geometry ruled out, a lobe away from it.</param>
public sealed record StereoPairSumReading(
    double LeftLaterMs,
    double GainDb,
    double? RunnerUpMs,
    double? RunnerUpGainDb,
    double? SetAsideMs = null,
    double? SetAsideGainDb = null)
{
    /// <summary>Whether the optimum may hold the pair: two sides that add (<see cref="StereoPairSum.DecisiveGainDb"/>) and
    /// a lobe that stands clear of the next (<see cref="StereoPairSum.DecisiveLobeMarginDb"/>). Unrelated sides sum to a
    /// few hundredths of a dB on a lobe no better than its neighbours, and a hard hold on such a lobe is a guess.</summary>
    public bool IsDecisive =>
        GainDb >= StereoPairSum.DecisiveGainDb &&
        (RunnerUpGainDb is not { } runnerUp || GainDb - runnerUp >= StereoPairSum.DecisiveLobeMarginDb);
}

/// <summary>The L/R split a low pair asks for, read from the pair itself: its arrivals are the cabin's least reliable
/// read, its sum is not. See docs/tech/auto-alignment.md#a-low-pair-stands-on-its-own-sum.</summary>
public static class StereoPairSum
{
    /// <summary>The split is scanned this far either way from the records' own relation: a cabin never puts a pair's
    /// sides further apart.</summary>
    public const double ScanReachMs = 6.0;

    /// <summary>The DSP's delay grid.</summary>
    public const double ScanStepMs = 0.01;

    /// <summary>Each side is read from the pair's earliest front for this long: the direct sound and the cabin's early
    /// decay, which is what the sum of two woofers is made of. A woofer's peak can stand tens of milliseconds into its
    /// modal build-up, so the window opens at the band-limited front, never later than the peak.</summary>
    public const double WindowMs = 250.0;

    private const double LeadMs = 20.0;
    private const int BinCount = 64;

    /// <summary>A pair's band is read no lower than this: below it the records hold rumble, not the pair.</summary>
    public const double FloorHz = 30.0;

    /// <summary>The least the sides must add at the optimum for it to hold them: a side 26 dB under its twin adds 0.4 dB
    /// and is not in the pair; the archive's pairs add 1.6-2.7.</summary>
    public const double DecisiveGainDb = 0.5;

    /// <summary>The least the optimum must stand above the runner-up lobe: the archive's runner-ups sit 1.8-2.5 dB under.</summary>
    public const double DecisiveLobeMarginDb = 0.5;

    /// <summary>How far from the cabin's geometry an optimum may stand, in periods of the band's centre: the sum's
    /// lobes repeat about every such period, and the archive's optima stand within 0.4 ms of the geometry.</summary>
    internal const double GeometryLobeReachPeriods = 0.5;

    internal static double GeometryLobeReachMs(double lowHz, double highHz) =>
        GeometryLobeReachPeriods * 1000.0 / Math.Sqrt(Math.Max(lowHz, FloorHz) * highHz);

    /// <summary>The strongest optimum of the sides' sum inside <see cref="ScanReachMs"/> of <paramref name="centreMs"/>,
    /// or null where the band holds no energy or the sum has no optimum there. <paramref name="invertRight"/> reads the
    /// right side flipped, as a side that inherits an inverted twin's polarity will play. A left side given with a delay
    /// passes minus that delay as the centre: the scan is around the records' relation, not the render's.
    /// <paramref name="geometryMs"/>, the split the cabin's geometry asks for in the same terms, picks the lobe: only an
    /// optimum within <see cref="GeometryLobeReachMs"/> of it counts.</summary>
    public static StereoPairSumReading? Read(
        Complex[] left,
        Complex[] right,
        int sampleRate,
        double lowHz,
        double highHz,
        bool invertRight = false,
        ValidSampleRange leftRange = default,
        ValidSampleRange rightRange = default,
        double centreMs = 0,
        double? geometryMs = null)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        lowHz = Math.Max(lowHz, FloorHz);
        if (!(highHz > lowHz) || left.Length == 0 || right.Length == 0)
        {
            return null;
        }

        int Front(Complex[] ir, ValidSampleRange range) =>
            VirtualCrossoverAnalysis.FindGateAnchor(
                ir, VirtualCrossoverAnalysis.FindPeakIndex(ir), sampleRate, lowHz, highHz, range);
        int start = Math.Max(0, Math.Min(Front(left, leftRange), Front(right, rightRange)) -
            (int)Math.Round(LeadMs / 1000.0 * sampleRate));
        int count = (int)Math.Round(WindowMs / 1000.0 * sampleRate);
        double[] frequencies = Enumerable.Range(0, BinCount)
            .Select(i => lowHz * Math.Pow(highHz / lowHz, i / (BinCount - 1.0)))
            .ToArray();
        Complex[] leftSpectrum = Spectrum(left, start, count, sampleRate, frequencies, 1.0);
        Complex[] rightSpectrum = Spectrum(right, start, count, sampleRate, frequencies, invertRight ? -1.0 : 1.0);
        double power = 0;
        for (int bin = 0; bin < BinCount; bin++)
        {
            power += leftSpectrum[bin].Magnitude * leftSpectrum[bin].Magnitude +
                rightSpectrum[bin].Magnitude * rightSpectrum[bin].Magnitude;
        }
        if (!(power > 0))
        {
            return null;
        }

        int ticks = (int)Math.Round(ScanReachMs / ScanStepMs);
        var splits = new double[2 * ticks + 1];
        var gains = new double[splits.Length];
        for (int i = 0; i < splits.Length; i++)
        {
            splits[i] = centreMs + (i - ticks) * ScanStepMs;
            double sum = 0;
            for (int bin = 0; bin < BinCount; bin++)
            {
                Complex moved = leftSpectrum[bin] * Complex.FromPolarCoordinates(
                    1.0, -Math.Tau * frequencies[bin] * splits[i] / 1000.0);
                sum += (moved + rightSpectrum[bin]).Magnitude * (moved + rightSpectrum[bin]).Magnitude;
            }

            gains[i] = 10.0 * Math.Log10(sum / power);
        }

        // Interior optima only: a rise toward the edge of the scan is no optimum, nor is a flat sum's rounding noise.
        List<int> optima = Enumerable.Range(1, splits.Length - 2)
            .Where(i => gains[i] > gains[i - 1] + 1e-9 && gains[i] >= gains[i + 1])
            .OrderByDescending(i => gains[i])
            .ToList();
        int? setAside = null;
        if (geometryMs is { } expectedMs)
        {
            double reachMs = GeometryLobeReachMs(lowHz, highHz);
            List<int> onLobe = [.. optima.Where(i => Math.Abs(splits[i] - expectedMs) <= reachMs)];
            setAside = optima.Count > 0 && (onLobe.Count == 0 || onLobe[0] != optima[0]) ? optima[0] : null;
            optima = onLobe;
        }

        if (optima.Count == 0)
        {
            return null;
        }

        return new StereoPairSumReading(
            splits[optima[0]],
            gains[optima[0]],
            optima.Count > 1 ? splits[optima[1]] : null,
            optima.Count > 1 ? gains[optima[1]] : null,
            setAside is { } aside ? splits[aside] : null,
            setAside is { } asideGain ? gains[asideGain] : null);
    }

    // The window's last quarter fades out; the lead before the front keeps a sloped front whole.
    private static Complex[] Spectrum(
        Complex[] impulseResponse, int start, int count, int sampleRate, double[] frequencies, double sign)
    {
        count = Math.Min(count, impulseResponse.Length - start);
        int fade = count / 4;
        var spectrum = new Complex[frequencies.Length];
        for (int bin = 0; bin < frequencies.Length; bin++)
        {
            double step = -Math.Tau * frequencies[bin] / sampleRate;
            Complex sum = Complex.Zero;
            for (int i = 0; i < count; i++)
            {
                double window = i < count - fade
                    ? 1.0
                    : 0.5 * (1.0 + Math.Cos(Math.PI * (i - (count - fade)) / fade));
                sum += sign * impulseResponse[start + i].Real * window *
                    Complex.FromPolarCoordinates(1.0, step * i);
            }

            spectrum[bin] = sum;
        }

        return spectrum;
    }
}
