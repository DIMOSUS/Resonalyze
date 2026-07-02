namespace Resonalyze.Dsp;

/// <summary>The driver class a measured response most resembles.</summary>
public enum DriverType
{
    Woofer,
    Midrange,
    Tweeter
}

/// <summary>
/// The usable band read from a driver's magnitude response: the outermost
/// frequencies still within the drop threshold of the reference level, the
/// average level inside that band, and the driver class it suggests.
/// </summary>
public sealed record DriverBandEstimate(
    double LowHz,
    double HighHz,
    double LevelDb,
    DriverType SuggestedType);

/// <summary>One wizard input: a channel's raw magnitude curve and its (confirmed) driver type.</summary>
public sealed record AutoSetupSource(
    IReadOnlyList<SignalPoint> MagnitudeDb,
    DriverType Type);

/// <summary>
/// The proposed DSP starting point for one channel: the crossover filters
/// (Linkwitz-Riley 24 dB/oct throughout) and a cut-only gain that levels the
/// channels against the quietest one.
/// </summary>
public sealed record CrossoverProposal(
    CrossoverKind Kind,
    CrossoverEdge? HighPassEdge,
    CrossoverEdge? LowPassEdge,
    double GainDb);

/// <summary>
/// The analytic part of the crossover wizard. Everything works on smoothed
/// magnitude curves sharing one frequency grid (index-aligned); phase is
/// deliberately ignored — the delay/polarity alignment is a separate step done
/// against the complex sum afterward.
/// </summary>
public static class CrossoverAutoSetup
{
    // A band edge is where the response falls this far below the reference
    // level. 8 dB sits between the -6 dB textbook edge and the -10 dB the
    // remaining room ripple of a 1/3-octave-smoothed in-room curve asks for.
    private const double BandEdgeDropDb = 8.0;

    // The proposed crossover keeps at least this margin (octaves) above the
    // upper driver's low edge — the excursion protection — and below the lower
    // driver's high edge.
    private const double CrossoverMarginOctaves = 1.0;

    private const int CrossoverSlopeDbPerOctave = 24;

    /// <summary>
    /// Reads the usable band from a (smoothed) magnitude curve and suggests the
    /// driver class. The reference is an upper percentile of the curve, robust
    /// against both narrow room dips and single peaks.
    /// </summary>
    public static DriverBandEstimate EstimateBand(IReadOnlyList<SignalPoint> magnitudeDb)
    {
        ArgumentNullException.ThrowIfNull(magnitudeDb);

        var levels = magnitudeDb
            .Where(point => double.IsFinite(point.Y))
            .Select(point => point.Y)
            .OrderBy(value => value)
            .ToList();
        if (levels.Count < 2)
        {
            throw new ArgumentException(
                "The magnitude curve is empty.",
                nameof(magnitudeDb));
        }

        double reference = levels[(int)(levels.Count * 0.85)];
        double threshold = reference - BandEdgeDropDb;

        double lowHz = double.NaN;
        double highHz = double.NaN;
        foreach (SignalPoint point in magnitudeDb)
        {
            if (!double.IsFinite(point.Y) || point.Y < threshold)
            {
                continue;
            }

            if (double.IsNaN(lowHz))
            {
                lowHz = point.X;
            }
            highHz = point.X;
        }

        if (double.IsNaN(lowHz) || highHz <= lowHz)
        {
            throw new ArgumentException(
                "The magnitude curve has no usable band.",
                nameof(magnitudeDb));
        }

        double level = AverageLevelDb(magnitudeDb, lowHz, highHz);
        return new DriverBandEstimate(lowHz, highHz, level, Classify(lowHz, highHz));
    }

    // Log-center classification with a hard tweeter override: a driver that only
    // starts at 1 kHz is a tweeter no matter how far its band extends.
    private static DriverType Classify(double lowHz, double highHz)
    {
        if (lowHz >= 1_000)
        {
            return DriverType.Tweeter;
        }

        double center = Math.Sqrt(lowHz * highHz);
        if (center < 300)
        {
            return DriverType.Woofer;
        }

        return center <= 2_000 ? DriverType.Midrange : DriverType.Tweeter;
    }

    /// <summary>
    /// Builds the crossover proposal for the given channels. Results are in the
    /// input order. Every channel must carry a distinct driver type — with two
    /// identical drivers there is no crossover to propose.
    /// </summary>
    public static IReadOnlyList<CrossoverProposal> Propose(
        IReadOnlyList<AutoSetupSource> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Count < 2)
        {
            throw new ArgumentException(
                "At least two channels are required.",
                nameof(channels));
        }
        if (channels.Select(channel => channel.Type).Distinct().Count() != channels.Count)
        {
            throw new ArgumentException(
                "Every channel needs a distinct driver type.",
                nameof(channels));
        }

        // Work from the lowest band up; remember the input positions to report
        // the results back in the caller's order.
        var ordered = channels
            .Select((channel, index) => (Channel: channel, Index: index))
            .OrderBy(item => item.Channel.Type)
            .ToList();
        var bands = ordered
            .Select(item => EstimateBand(item.Channel.MagnitudeDb))
            .ToList();

        // One crossover frequency per adjacent pair.
        var crossovers = new double[ordered.Count - 1];
        for (int i = 0; i < crossovers.Length; i++)
        {
            crossovers[i] = ProposeCrossoverFrequency(
                ordered[i].Channel.MagnitudeDb,
                bands[i],
                ordered[i + 1].Channel.MagnitudeDb,
                bands[i + 1]);
        }

        // Cut-only gains: every channel comes down to the quietest one, measured
        // inside the band it will actually cover.
        var bandLevels = new double[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            double from = i == 0 ? bands[i].LowHz : crossovers[i - 1];
            double to = i == ordered.Count - 1 ? bands[i].HighHz : crossovers[i];
            if (to <= from)
            {
                (from, to) = (bands[i].LowHz, bands[i].HighHz);
            }

            bandLevels[i] = AverageLevelDb(ordered[i].Channel.MagnitudeDb, from, to);
        }
        double target = bandLevels.Min();

        var results = new CrossoverProposal[channels.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            CrossoverEdge? highPass = i > 0
                ? new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley,
                    Math.Round(crossovers[i - 1]),
                    CrossoverSlopeDbPerOctave)
                : null;
            CrossoverEdge? lowPass = i < ordered.Count - 1
                ? new CrossoverEdge(
                    CrossoverFilterFamily.LinkwitzRiley,
                    Math.Round(crossovers[i]),
                    CrossoverSlopeDbPerOctave)
                : null;
            CrossoverKind kind = (highPass, lowPass) switch
            {
                (not null, not null) => CrossoverKind.BandPass,
                (not null, null) => CrossoverKind.HighPass,
                _ => CrossoverKind.LowPass
            };

            results[ordered[i].Index] = new CrossoverProposal(
                kind,
                highPass,
                lowPass,
                Math.Round(target - bandLevels[i], 1));
        }

        return results;
    }

    // The crossover for one adjacent pair: where the level-aligned curves
    // intersect inside the overlap region, clamped an octave away from both
    // drivers' band edges; the geometric mean when they never cross.
    private static double ProposeCrossoverFrequency(
        IReadOnlyList<SignalPoint> lowerCurve,
        DriverBandEstimate lowerBand,
        IReadOnlyList<SignalPoint> upperCurve,
        DriverBandEstimate upperBand)
    {
        double overlapLow = upperBand.LowHz;
        double overlapHigh = lowerBand.HighHz;
        double crossover = double.NaN;

        if (overlapHigh > overlapLow)
        {
            // The lower driver falls while the upper rises, so the aligned
            // difference decreases; the first sign change is the natural handover.
            int count = Math.Min(lowerCurve.Count, upperCurve.Count);
            double previousDiff = double.NaN;
            double previousX = double.NaN;
            for (int i = 0; i < count; i++)
            {
                double frequency = lowerCurve[i].X;
                if (frequency < overlapLow || frequency > overlapHigh)
                {
                    continue;
                }

                double diff =
                    (lowerCurve[i].Y - lowerBand.LevelDb) -
                    (upperCurve[i].Y - upperBand.LevelDb);
                if (!double.IsNaN(previousDiff) && previousDiff > 0 && diff <= 0)
                {
                    // Interpolate the zero crossing between the two grid points.
                    double t = previousDiff / (previousDiff - diff);
                    crossover = previousX + (frequency - previousX) * t;
                    break;
                }

                previousDiff = diff;
                previousX = frequency;
            }
        }

        if (double.IsNaN(crossover))
        {
            crossover = Math.Sqrt(
                Math.Max(20, overlapLow) * Math.Min(20_000, Math.Max(overlapLow + 1, overlapHigh)));
        }

        // Excursion protection for the upper driver and headroom for the lower
        // one; contradictory clamps fall back to the plain geometric mean.
        double margin = Math.Pow(2.0, CrossoverMarginOctaves);
        double minimum = upperBand.LowHz * margin;
        double maximum = lowerBand.HighHz / margin;
        crossover = minimum <= maximum
            ? Math.Clamp(crossover, minimum, maximum)
            : Math.Sqrt(upperBand.LowHz * lowerBand.HighHz);

        return Math.Clamp(crossover, 20, 20_000);
    }

    // Average level (dB over linear amplitude) of the curve inside a band.
    private static double AverageLevelDb(
        IReadOnlyList<SignalPoint> curve,
        double fromHz,
        double toHz)
    {
        double sum = 0;
        int count = 0;
        foreach (SignalPoint point in curve)
        {
            if (point.X < fromHz || point.X > toHz || !double.IsFinite(point.Y))
            {
                continue;
            }

            sum += DataHelper.DecibelsToAmplitude(point.Y);
            count++;
        }

        return count > 0
            ? DataHelper.AmplitudeToDecibels(sum / count)
            : double.NegativeInfinity;
    }
}
