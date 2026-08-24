namespace Resonalyze.Dsp;

/// <summary>
/// One EQ band, described the way a PEQ slot exposes it: a centre frequency, a
/// quality factor, a gain and the shape those three describe (bell by default,
/// a shelf, or a phase-only all-pass — see <see cref="PeqBandType"/>). The
/// magnitude response is the analog prototype, which is sample-rate independent
/// and therefore suitable for plotting an EQ curve across the audible range.
/// </summary>
/// <remarks>
/// <see cref="Type"/> is the last parameter and defaults to
/// <see cref="PeqBandType.Peaking"/>, so a three-argument band is still a bell and
/// a settings or project file written before shelves existed reads back as one.
/// </remarks>
public readonly record struct PeqBand(
    double FrequencyHz,
    double Q,
    double GainDb,
    PeqBandType Type = PeqBandType.Peaking)
{
    /// <summary>
    /// True for a band that contributes nothing: degenerate frequency/Q (e.g. a
    /// half-filled PEQ slot), or zero gain on a band whose whole effect IS its gain.
    /// Such bands are skipped when the curve is evaluated or realized as biquads.
    /// An all-pass moves phase without carrying any gain, so its zero gain is not
    /// transparency — only a degenerate frequency or Q silences one.
    /// </summary>
    public bool IsTransparent =>
        Q <= 0 || FrequencyHz <= 0 || (GainDb == 0 && !Type.IsAllPass());

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
        // An all-pass has unity magnitude at every frequency, whatever gain the slot
        // may still carry from a type switch — never let it fall through to the bell.
        if (Type.IsAllPass())
        {
            return 0;
        }

        double a = Math.Pow(10.0, GainDb / 40.0);
        double x = frequencyHz / FrequencyHz;
        return Type switch
        {
            PeqBandType.LowShelf => ShelfMagnitudeDb(a, x, low: true),
            PeqBandType.HighShelf => ShelfMagnitudeDb(a, x, low: false),
            _ => PeakingMagnitudeDb(a, x)
        };
    }

    // Analog peaking prototype H(j2pi f); evaluating |H|^2 in normalised frequency
    // x = f / f0 keeps it independent of any sample rate.
    //   |H|^2 = ((1 - x^2)^2 + (A x / Q)^2) / ((1 - x^2)^2 + (x / (A Q))^2)
    // with A = 10^(gain / 40). At x = 1 this evaluates to A^4, i.e. exactly the
    // band gain in dB; far from f0 it tends to unity (0 dB).
    private double PeakingMagnitudeDb(double a, double x)
    {
        double oneMinusXSquared = 1.0 - x * x;
        double baseline = oneMinusXSquared * oneMinusXSquared;

        double numeratorImag = a * x / Q;
        double denominatorImag = x / (a * Q);
        double numerator = baseline + numeratorImag * numeratorImag;
        double denominator = baseline + denominatorImag * denominatorImag;

        return 10.0 * Math.Log10(numerator / denominator);
    }

    // The RBJ shelving prototypes the cookbook's biquads are derived from, with
    // s = jx normalised to f0:
    //   low  shelf  H(s) = A (s^2 + (sqrt(A)/Q) s + A) / (A s^2 + (sqrt(A)/Q) s + 1)
    //   high shelf  H(s) = A (A s^2 + (sqrt(A)/Q) s + 1) / (s^2 + (sqrt(A)/Q) s + A)
    // The two are mirror images: the low shelf reaches the full gain at DC and unity
    // far above f0, the high shelf the other way round, and both pass through half
    // the gain in dB exactly at f0 — which is what makes f0 the middle of a shelf
    // rather than its corner.
    private double ShelfMagnitudeDb(double a, double x, bool low)
    {
        double xSquared = x * x;
        double transition = Math.Sqrt(a) * x / Q;
        double transitionSquared = transition * transition;

        double lifted = a - xSquared;
        double flat = 1.0 - a * xSquared;
        double numeratorReal = low ? lifted : flat;
        double denominatorReal = low ? flat : lifted;

        double numerator = numeratorReal * numeratorReal + transitionSquared;
        double denominator = denominatorReal * denominatorReal + transitionSquared;
        return 20.0 * Math.Log10(a) + 10.0 * Math.Log10(numerator / denominator);
    }
}

/// <summary>
/// A logical equalization curve: PEQ parameters (up to 32 bands) plus preamp.
/// <see cref="MagnitudeDbAt"/> retains the sample-rate-independent analog model
/// for legacy comparisons; DSP fitting, preview and coefficient-oriented output
/// use <see cref="DigitalEqualizationResponse"/> so they match RBJ biquads.
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
