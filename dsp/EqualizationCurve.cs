namespace Resonalyze.Dsp;

/// <summary>
/// One parametric (peaking / bell) EQ band, described the way a PEQ slot exposes
/// it: a centre frequency, a quality factor and a gain. The magnitude response is
/// the analog peaking prototype, which is sample-rate independent and therefore
/// suitable for plotting an EQ curve across the audible range.
/// </summary>
public readonly record struct PeqBand(double FrequencyHz, double Q, double GainDb)
{
    /// <summary>
    /// True for a band that contributes nothing: zero gain or degenerate frequency/Q
    /// (e.g. a half-filled PEQ slot). Such bands are skipped when the curve is
    /// evaluated or realized as biquads.
    /// </summary>
    public bool IsTransparent => GainDb == 0 || Q <= 0 || FrequencyHz <= 0;

    /// <summary>
    /// Magnitude contribution of this band at <paramref name="frequencyHz"/>, in dB.
    /// Returns 0 for a transparent or degenerate band (no gain, non-positive Q,
    /// centre or query frequency).
    /// </summary>
    public double MagnitudeDbAt(double frequencyHz)
    {
        if (IsTransparent || frequencyHz <= 0)
        {
            return 0;
        }

        // Analog peaking prototype H(j2pi f); evaluating |H|^2 in normalised
        // frequency x = f / f0 keeps it independent of any sample rate.
        //   |H|^2 = ((1 - x^2)^2 + (A x / Q)^2) / ((1 - x^2)^2 + (x / (A Q))^2)
        // with A = 10^(gain / 40). At x = 1 this evaluates to A^4, i.e. exactly the
        // band gain in dB; far from f0 it tends to unity (0 dB).
        double a = Math.Pow(10.0, GainDb / 40.0);
        double x = frequencyHz / FrequencyHz;
        double oneMinusXSquared = 1.0 - x * x;
        double baseline = oneMinusXSquared * oneMinusXSquared;

        double numeratorImag = a * x / Q;
        double denominatorImag = x / (a * Q);
        double numerator = baseline + numeratorImag * numeratorImag;
        double denominator = baseline + denominatorImag * denominatorImag;

        return 10.0 * Math.Log10(numerator / denominator);
    }
}

/// <summary>
/// A logical equalization curve: the combined magnitude response of a set of PEQ
/// bands (up to 32) plus an overall preamp offset. The curve is the sum of the
/// individual band responses in the dB domain, which is the standard way PEQ
/// stages combine.
/// </summary>
public sealed class EqualizationCurve
{
    /// <summary>Maximum number of bands a curve may hold, matching the PEQ panel.</summary>
    public const int MaxBandCount = 32;

    private readonly PeqBand[] bands;

    public EqualizationCurve(IEnumerable<PeqBand> bands, double preampDb = 0)
    {
        ArgumentNullException.ThrowIfNull(bands);

        this.bands = bands.ToArray();
        if (this.bands.Length > MaxBandCount)
        {
            throw new ArgumentException(
                $"An equalization curve supports at most {MaxBandCount} bands.",
                nameof(bands));
        }

        PreampDb = preampDb;
    }

    public IReadOnlyList<PeqBand> Bands => bands;

    /// <summary>Constant gain (dB) applied across the whole curve.</summary>
    public double PreampDb { get; }

    /// <summary>Combined magnitude of every band plus the preamp, in dB.</summary>
    public double MagnitudeDbAt(double frequencyHz)
    {
        double total = PreampDb;
        foreach (PeqBand band in bands)
        {
            total += band.MagnitudeDbAt(frequencyHz);
        }

        return total;
    }

    /// <summary>
    /// Samples the curve at the supplied frequencies, returning (Hz, dB) points.
    /// </summary>
    public IReadOnlyList<SignalPoint> Sample(IReadOnlyList<double> frequenciesHz)
    {
        ArgumentNullException.ThrowIfNull(frequenciesHz);

        var points = new SignalPoint[frequenciesHz.Count];
        for (int i = 0; i < frequenciesHz.Count; i++)
        {
            double frequency = frequenciesHz[i];
            points[i] = new SignalPoint(frequency, MagnitudeDbAt(frequency));
        }

        return points;
    }

    /// <summary>
    /// Builds a logarithmically spaced frequency grid, the natural sampling for an
    /// EQ curve drawn on a log frequency axis.
    /// </summary>
    public static IReadOnlyList<double> LogFrequencyGrid(
        double minHz,
        double maxHz,
        int count)
    {
        if (minHz <= 0 || maxHz <= minHz)
        {
            throw new ArgumentException(
                "Require 0 < minHz < maxHz for a logarithmic frequency grid.");
        }
        if (count < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "A frequency grid needs at least two points.");
        }

        var grid = new double[count];
        double logMin = Math.Log10(minHz);
        double logStep = (Math.Log10(maxHz) - logMin) / (count - 1);
        for (int i = 0; i < count; i++)
        {
            grid[i] = Math.Pow(10.0, logMin + i * logStep);
        }

        return grid;
    }
}
