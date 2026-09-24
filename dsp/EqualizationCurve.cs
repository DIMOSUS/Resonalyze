using System.Text.Json.Serialization;

namespace Resonalyze.Dsp;

/// <summary>
/// One PEQ band with an analog-prototype (rate-independent) magnitude. <see cref="Type"/> defaults to Peaking so
/// files written before shelves existed read back as bells. <see cref="Locked"/> is the tuner's, not the filter's: Auto
/// Tune keeps a locked band and fits around it; no response reads it, and a file writes it only when set.
/// </summary>
public readonly record struct PeqBand(
    double FrequencyHz,
    double Q,
    double GainDb,
    PeqBandType Type = PeqBandType.Peaking,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Locked = false)
{
    /// <summary>Contributes nothing: degenerate frequency/Q, or zero gain on a gain band (an all-pass is never gain-transparent).</summary>
    public bool IsTransparent =>
        Q <= 0 || FrequencyHz <= 0 || (GainDb == 0 && !Type.IsAllPass());

    public double MagnitudeDbAt(double frequencyHz)
    {
        if (IsTransparent || frequencyHz <= 0)
        {
            return 0;
        }
        // Unity for any gain a slot kept from a type switch.
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

    // |H|^2 = ((1 - x^2)^2 + (A x / Q)^2) / ((1 - x^2)^2 + (x / (A Q))^2), x = f / f0, A = 10^(gain / 40).
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

    // RBJ shelving prototypes in s = jx; both pass through half the gain (dB) exactly at f0, so f0 is the MIDDLE, not the corner.
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
/// PEQ bands plus preamp. <see cref="MagnitudeDbAt"/> is the analog model; fitting and preview use
/// <see cref="DigitalEqualizationResponse"/> to match RBJ biquads.
/// </summary>
public sealed class EqualizationCurve
{
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

    public double PreampDb { get; }

    public double MagnitudeDbAt(double frequencyHz)
    {
        double total = PreampDb;
        foreach (PeqBand band in bands)
        {
            total += band.MagnitudeDbAt(frequencyHz);
        }

        return total;
    }

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
