namespace Resonalyze.Dsp;

/// <summary>
/// The driver class a measured response most resembles. Declared low to high in
/// frequency, so ordering channels by this enum orders them along the spectrum.
/// </summary>
public enum DriverType
{
    Subwoofer,
    Woofer,
    Midbass,
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
/// The proposed DSP starting point for one channel: the crossover filters and a
/// cut-only gain. Every field comes out of the magnitude-domain optimizer.
/// </summary>
public sealed record CrossoverProposal(
    CrossoverKind Kind,
    CrossoverEdge? HighPassEdge,
    CrossoverEdge? LowPassEdge,
    double GainDb);

/// <summary>
/// The choices the crossover wizard asks for before optimizing: which filter
/// families the optimizer may pick from, the frequency window crossovers must
/// fall inside, and whether the two sides of a junction may take different
/// slopes. The sample rate is needed because the optimizer evaluates the exact
/// digital biquad cascades the DSP runs.
/// </summary>
public sealed record CrossoverAutoSetupOptions(
    IReadOnlyList<CrossoverFilterFamily> Families,
    double MinCrossoverHz,
    double MaxCrossoverHz,
    bool IndependentSlopes,
    double SampleRateHz)
{
    /// <summary>All families, the full 20 Hz – 20 kHz window, matched slopes.</summary>
    public static CrossoverAutoSetupOptions Default(double sampleRateHz) =>
        new(
            [
                CrossoverFilterFamily.LinkwitzRiley,
                CrossoverFilterFamily.Butterworth,
                CrossoverFilterFamily.Bessel
            ],
            20,
            20_000,
            IndependentSlopes: false,
            sampleRateHz);
}

/// <summary>
/// The analytic part of the crossover wizard. Everything works on smoothed
/// magnitude curves; phase is deliberately ignored — the delay/polarity alignment
/// is a separate step done against the complex sum afterward.
///
/// <para>
/// <see cref="Propose"/> searches per-junction crossover frequency, filter
/// family and slope, plus per-channel cut-only gain, to make the summed magnitude
/// response as flat as the drivers allow. The junctions are summed the way each
/// family behaves acoustically once the alignment step has done its job: a
/// Linkwitz-Riley or Bessel handover (both drivers roughly in phase at the corner)
/// is an amplitude sum, a Butterworth handover (the drivers in quadrature) a power
/// sum. That is what lets the optimizer compare families honestly.
/// </para>
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

    // The log-frequency grid the optimizer scores flatness on, and the search
    // resolution for each junction's crossover frequency.
    private const int GridPointsPerOctave = 24;
    private const int CrossoverGridSteps = 21;

    // Adjacent crossovers keep at least this separation so a three-way search
    // cannot collapse two junctions onto the same frequency.
    private const double MinJunctionSeparationOctaves = 0.5;

    // Gain refinement: how far around the current value each channel is searched,
    // and the resolution. Cut-only is enforced afterward by referencing the loudest.
    private const double GainSearchRangeDb = 8.0;
    private const double GainSearchStepDb = 0.25;

    // Coordinate-descent passes; it converges well within this on two- and
    // three-way systems, and stops early once a pass stops helping.
    private const int MaxPasses = 6;
    private const double ConvergenceDb = 0.01;

    // A narrow suckout is far more audible than the same energy spread as ripple,
    // so the deepest dip below the mean is added to the RMS flatness score.
    private const double DipPenaltyWeight = 0.5;

    // Pure magnitude flatness is blind to band overlap: shallow filters let
    // adjacent drivers overlap widely, which averages out each other's ripple and
    // reads flat, but an engineer would never do it — wide overlap means lobing,
    // intermodulation and out-of-band excursion. So the score also penalizes how
    // many octaves two adjacent drivers meaningfully overlap (steeper filters =
    // narrower overlap), and the search never goes below a practical slope: a
    // first-order (6 dB/oct) filter protects nothing.
    private const double OverlapPenaltyDbPerOctave = 0.6;
    private const int MinPracticalSlopeDbPerOctave = 12;

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

    // Classifies by the band's log-center, using the geometric midpoints between
    // the neighbouring driver classes' own centers as thresholds. The class only
    // seeds the wizard's suggestion; the user confirms it before optimizing.
    private static DriverType Classify(double lowHz, double highHz)
    {
        double center = Math.Sqrt(lowHz * highHz);
        if (center < 63)
        {
            return DriverType.Subwoofer;
        }
        if (center < 141)
        {
            return DriverType.Woofer;
        }
        if (center < 450)
        {
            return DriverType.Midbass;
        }

        return center < 2_500 ? DriverType.Midrange : DriverType.Tweeter;
    }

    // The frequency range a driver of this class may sensibly play — and so the
    // band any crossover involving it must stay inside. A woofer measured in-room
    // still shows output near 850 Hz, but nobody crosses a woofer there; the class
    // caps the search to musically sane handovers.
    private static (double LowHz, double HighHz) SensibleRange(DriverType type) => type switch
    {
        DriverType.Subwoofer => (20, 80),
        DriverType.Woofer => (40, 250),
        DriverType.Midbass => (80, 500),
        DriverType.Midrange => (250, 4_000),
        DriverType.Tweeter => (2_000, 20_000),
        _ => (20, 20_000)
    };

    // The band a handover between two adjacent driver classes may sit in: at or
    // below the lower driver's sensible top and at or above the upper driver's
    // sensible bottom. When the classes do not overlap (a "skipped" pairing such
    // as a 2-way woofer + tweeter) the returned low exceeds the high, signalling
    // that no class-sensible band exists and the measured overlap should stand.
    private static (double LowHz, double HighHz) JunctionTypeBounds(
        DriverType lower,
        DriverType upper) =>
        (SensibleRange(upper).LowHz, SensibleRange(lower).HighHz);

    /// <summary>
    /// Builds the crossover proposal for the given channels, honouring the wizard
    /// <paramref name="options"/>. Results are in the input order. Every channel
    /// must carry a distinct driver type — with two identical drivers there is no
    /// crossover to propose.
    /// </summary>
    public static IReadOnlyList<CrossoverProposal> Propose(
        IReadOnlyList<AutoSetupSource> channels,
        CrossoverAutoSetupOptions options)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(options);
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

        options = Normalize(options);
        return new Optimizer(channels, options).Solve();
    }

    /// <summary>
    /// The magnitude-domain summed response the wizard predicts for a proposal,
    /// on the optimizer's own log grid. Each junction is combined the way its
    /// family sums (amplitude for Linkwitz-Riley/Bessel, power for Butterworth),
    /// so this is exactly the curve the optimizer scored. Used for the live
    /// preview and by the tests.
    /// </summary>
    public static IReadOnlyList<SignalPoint> SummedResponseDb(
        IReadOnlyList<AutoSetupSource> channels,
        IReadOnlyList<CrossoverProposal> proposals,
        double sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(proposals);
        if (channels.Count != proposals.Count)
        {
            throw new ArgumentException(
                "One proposal is required per channel.",
                nameof(proposals));
        }
        if (sampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }

        var ordered = channels
            .Select((channel, index) => (Channel: channel, Proposal: proposals[index]))
            .OrderBy(item => item.Channel.Type)
            .ToList();
        double[] grid = BuildGrid(sampleRateHz);

        double[] combined = new double[grid.Length];
        bool first = true;
        foreach ((AutoSetupSource channel, CrossoverProposal proposal) in ordered)
        {
            double gainLinear = DataHelper.DecibelsToAmplitude(proposal.GainDb);
            var spec = new CrossoverSpec(
                proposal.Kind,
                proposal.LowPassEdge,
                proposal.HighPassEdge);
            bool power = proposal.HighPassEdge?.Family == CrossoverFilterFamily.Butterworth;
            for (int k = 0; k < grid.Length; k++)
            {
                double driverDb = InterpolateDb(channel.MagnitudeDb, grid[k]);
                double amplitude = double.IsFinite(driverDb)
                    ? gainLinear
                        * DataHelper.DecibelsToAmplitude(driverDb)
                        * CrossoverFilter.Response(spec, grid[k], sampleRateHz).Magnitude
                    : 0;
                if (first)
                {
                    combined[k] = amplitude;
                }
                else if (power)
                {
                    combined[k] = Math.Sqrt(combined[k] * combined[k] + amplitude * amplitude);
                }
                else
                {
                    combined[k] += amplitude;
                }
            }

            first = false;
        }

        var result = new SignalPoint[grid.Length];
        for (int k = 0; k < grid.Length; k++)
        {
            result[k] = new SignalPoint(grid[k], DataHelper.AmplitudeToDecibels(combined[k]));
        }

        return result;
    }

    // Clamps the wizard options into a usable range: at least one family, a
    // positive window inside the Nyquist limit, min strictly below max.
    private static CrossoverAutoSetupOptions Normalize(CrossoverAutoSetupOptions options)
    {
        if (options.SampleRateHz <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The sample rate must be positive.");
        }

        var families = options.Families
            .Distinct()
            .ToList();
        if (families.Count == 0)
        {
            throw new ArgumentException(
                "At least one filter family must be allowed.",
                nameof(options));
        }

        double ceiling = options.SampleRateHz * 0.49;
        double max = Math.Clamp(options.MaxCrossoverHz, 20, ceiling);
        double min = Math.Clamp(options.MinCrossoverHz, 20, ceiling);
        if (min >= max)
        {
            min = Math.Max(20, max / 2);
        }

        return options with { Families = families, MinCrossoverHz = min, MaxCrossoverHz = max };
    }

    // The shared log-frequency grid: 20 Hz up to 20 kHz (or just under Nyquist),
    // sampled at GridPointsPerOctave.
    private static double[] BuildGrid(double sampleRateHz)
    {
        double low = 20;
        double high = Math.Min(20_000, sampleRateHz * 0.49);
        if (high <= low)
        {
            high = low * 2;
        }

        int count = Math.Max(
            2,
            (int)Math.Round(Math.Log2(high / low) * GridPointsPerOctave) + 1);
        return EqualizationCurve.LogFrequencyGrid(low, high, count).ToArray();
    }

    // Endpoint-clamped interpolation: measured driver curves span the whole audio
    // band, so a grid point outside the measured range means the driver has simply
    // rolled off — holding the endpoint value is the right behaviour, not dropping
    // the point.
    private static double InterpolateDb(IReadOnlyList<SignalPoint> points, double frequencyHz) =>
        CurveSampling.InterpolateDbLog(points, frequencyHz, clampEnds: true);

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

    // The crossover for one adjacent pair: where the level-aligned curves
    // intersect inside the overlap region, clamped an octave away from both
    // drivers' band edges; the geometric mean when they never cross. Used to seed
    // the optimizer.
    private static double ProposeCrossoverFrequency(
        IReadOnlyList<SignalPoint> lowerCurve,
        DriverBandEstimate lowerBand,
        DriverType lowerType,
        IReadOnlyList<SignalPoint> upperCurve,
        DriverBandEstimate upperBand,
        DriverType upperType)
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

        // Keep the seed inside the range sensible for both driver classes when
        // they overlap, so the initial handover is not up in the lower driver's
        // roll-off skirt (a woofer seeded at 850 Hz). Non-overlapping classes have
        // no such range and keep the measured seed.
        (double typeLow, double typeHigh) = JunctionTypeBounds(lowerType, upperType);
        if (typeLow <= typeHigh)
        {
            crossover = Math.Clamp(crossover, typeLow, typeHigh);
        }

        return Math.Clamp(crossover, 20, 20_000);
    }

    /// <summary>
    /// Coordinate-descent optimizer over one ordered set of drivers. State is the
    /// per-junction crossover frequency + family + slopes and the per-channel gain;
    /// each pass re-tunes every junction then every gain, scoring the summed
    /// magnitude flatness, until a pass stops improving.
    /// </summary>
    private sealed class Optimizer
    {
        private readonly CrossoverAutoSetupOptions options;
        private readonly int channelCount;
        private readonly int[] inputIndex;
        private readonly IReadOnlyList<SignalPoint>[] curves;
        private readonly DriverBandEstimate[] bands;
        private readonly DriverType[] types;
        private readonly double[] grid;
        private readonly double[][] driverAmplitude;
        private readonly int evalLow;
        private readonly int evalHigh;

        // Band-limit edges on the outer channels: a subsonic high-pass on the
        // lowest driver and a brickwall low-pass on the highest, added only where
        // the user narrowed the window past a driver that plays into it. They sit
        // at the window edges and are not part of the search.
        private readonly CrossoverEdge? lowLimitEdge;
        private readonly CrossoverEdge? highLimitEdge;

        private readonly double[] gainDb;
        private readonly double[] crossoverHz;
        private readonly CrossoverFilterFamily[] junctionFamily;
        private readonly int[] lowerSlope;
        private readonly int[] upperSlope;

        private readonly Dictionary<(CrossoverFilterFamily, int, long, bool), double[]> magnitudeCache =
            new();

        public Optimizer(IReadOnlyList<AutoSetupSource> channels, CrossoverAutoSetupOptions options)
        {
            this.options = options;
            channelCount = channels.Count;

            var ordered = channels
                .Select((channel, index) => (channel, index))
                .OrderBy(item => item.channel.Type)
                .ToArray();
            inputIndex = ordered.Select(item => item.index).ToArray();
            curves = ordered.Select(item => item.channel.MagnitudeDb).ToArray();
            bands = ordered.Select(item => EstimateBand(item.channel.MagnitudeDb)).ToArray();
            types = ordered.Select(item => item.channel.Type).ToArray();

            grid = BuildGrid(options.SampleRateHz);
            driverAmplitude = new double[channelCount][];
            for (int i = 0; i < channelCount; i++)
            {
                driverAmplitude[i] = new double[grid.Length];
                for (int k = 0; k < grid.Length; k++)
                {
                    double db = InterpolateDb(curves[i], grid[k]);
                    driverAmplitude[i][k] = double.IsFinite(db)
                        ? DataHelper.DecibelsToAmplitude(db)
                        : 0;
                }
            }

            // A subsonic high-pass / brickwall low-pass is added to the outer
            // channels when the user narrowed the window inside a driver that still
            // plays there — e.g. a 75 Hz lower limit on a woofer that reaches lower
            // gets a 75 Hz high-pass. Leaving the window at the full band adds
            // nothing.
            // The limit only counts when it sits at least a semitone inside the
            // driver edge, so float noise on the band edges and negligible cuts do
            // not sprout a filter.
            double margin = Math.Pow(2.0, 1.0 / 12.0);
            CrossoverFilterFamily limitFamily = PreferredFamily();
            int limitSlope = PreferredSlope(limitFamily);
            lowLimitEdge = options.MinCrossoverHz > bands[0].LowHz * margin
                ? new CrossoverEdge(limitFamily, Math.Round(options.MinCrossoverHz), limitSlope)
                : null;
            highLimitEdge = options.MaxCrossoverHz < bands[channelCount - 1].HighHz / margin
                ? new CrossoverEdge(limitFamily, Math.Round(options.MaxCrossoverHz), limitSlope)
                : null;

            // Flatness is only judged over the interior passband. The outermost
            // drivers' own roll-off skirts (or the band-limit edges, when set) are
            // unavoidable and identical for every candidate, so trimming half an
            // octave inside them keeps that constant floor from swamping the
            // crossover-region ripple the optimizer can actually change.
            double trim = Math.Pow(2.0, 0.5);
            double lowEdge = lowLimitEdge is { } low ? low.FrequencyHz : bands[0].LowHz;
            double highEdge = highLimitEdge is { } high
                ? high.FrequencyHz
                : bands[channelCount - 1].HighHz;
            double sysLow = lowEdge * trim;
            double sysHigh = highEdge / trim;
            evalLow = 0;
            evalHigh = grid.Length - 1;
            for (int k = 0; k < grid.Length; k++)
            {
                if (grid[k] < sysLow)
                {
                    evalLow = k + 1;
                }
            }
            for (int k = grid.Length - 1; k >= 0; k--)
            {
                if (grid[k] > sysHigh)
                {
                    evalHigh = k - 1;
                }
            }
            if (evalHigh <= evalLow)
            {
                evalLow = 0;
                evalHigh = grid.Length - 1;
            }

            gainDb = new double[channelCount];
            crossoverHz = new double[channelCount - 1];
            junctionFamily = new CrossoverFilterFamily[channelCount - 1];
            lowerSlope = new int[channelCount - 1];
            upperSlope = new int[channelCount - 1];
        }

        public IReadOnlyList<CrossoverProposal> Solve()
        {
            Initialize();

            double previous = Score();
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                for (int j = 0; j < channelCount - 1; j++)
                {
                    OptimizeJunction(j);
                }

                OptimizeGains();

                double current = Score();
                if (previous - current < ConvergenceDb)
                {
                    break;
                }

                previous = current;
            }

            NormalizeGainsCutOnly();
            return BuildProposals();
        }

        private void Initialize()
        {
            CrossoverFilterFamily family = PreferredFamily();
            int slope = PreferredSlope(family);
            for (int j = 0; j < channelCount - 1; j++)
            {
                double fc = ProposeCrossoverFrequency(
                    curves[j], bands[j], types[j], curves[j + 1], bands[j + 1], types[j + 1]);
                crossoverHz[j] = Math.Clamp(fc, options.MinCrossoverHz, options.MaxCrossoverHz);
                junctionFamily[j] = family;
                lowerSlope[j] = slope;
                upperSlope[j] = slope;
            }

            double separation = Math.Pow(2.0, MinJunctionSeparationOctaves);
            for (int j = 1; j < channelCount - 1; j++)
            {
                if (crossoverHz[j] <= crossoverHz[j - 1])
                {
                    crossoverHz[j] = Math.Min(
                        options.MaxCrossoverHz,
                        crossoverHz[j - 1] * separation);
                }
            }

            InitializeGains();
        }

        // Prefer the family an engineer would reach for first: Linkwitz-Riley, then
        // Bessel, then whatever is left.
        private CrossoverFilterFamily PreferredFamily()
        {
            if (options.Families.Contains(CrossoverFilterFamily.LinkwitzRiley))
            {
                return CrossoverFilterFamily.LinkwitzRiley;
            }

            return options.Families.Contains(CrossoverFilterFamily.Bessel)
                ? CrossoverFilterFamily.Bessel
                : options.Families[0];
        }

        private static int PreferredSlope(CrossoverFilterFamily family)
        {
            IReadOnlyList<int> slopes = PracticalSlopes(family);
            return slopes.Contains(CrossoverSlopeDbPerOctave)
                ? CrossoverSlopeDbPerOctave
                : slopes.MinBy(slope => Math.Abs(slope - CrossoverSlopeDbPerOctave));
        }

        // The slopes a family offers above the practical floor: an engineer does
        // not reach for a 6 dB/oct crossover — it protects nothing and leaves the
        // drivers overlapping over a huge span.
        private static IReadOnlyList<int> PracticalSlopes(CrossoverFilterFamily family) =>
            CrossoverFilter.SupportedSlopes(family)
                .Where(slope => slope >= MinPracticalSlopeDbPerOctave)
                .ToList();

        // Cut-only seed: bring every band down to the quietest, measured over the
        // band it will actually cover with the seeded crossovers.
        private void InitializeGains()
        {
            var level = new double[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                double from = i == 0 ? bands[i].LowHz : crossoverHz[i - 1];
                double to = i == channelCount - 1 ? bands[i].HighHz : crossoverHz[i];
                if (to <= from)
                {
                    from = bands[i].LowHz;
                    to = bands[i].HighHz;
                }

                level[i] = AverageLevelDb(curves[i], from, to);
            }

            double target = level.Min();
            for (int i = 0; i < channelCount; i++)
            {
                gainDb[i] = target - level[i];
            }
        }

        private void OptimizeJunction(int j)
        {
            // Keep the handover where both of its drivers actually produce output
            // (inside the requested window), and separated from the neighbouring
            // junctions.
            double separation = Math.Pow(2.0, MinJunctionSeparationOctaves);
            double low = Math.Max(options.MinCrossoverHz, bands[j + 1].LowHz);
            double high = Math.Min(options.MaxCrossoverHz, bands[j].HighHz);

            // Constrain the handover to a band sensible for BOTH driver classes
            // when their ranges overlap — a woofer must not cross up in its
            // roll-off skirt at 850 Hz. Adjacent classes that do not overlap (e.g.
            // a 2-way woofer + tweeter with no midrange between them) have no such
            // band, so the measured overlap stands.
            (double typeLow, double typeHigh) = JunctionTypeBounds(types[j], types[j + 1]);
            if (typeLow <= typeHigh)
            {
                low = Math.Max(low, typeLow);
                high = Math.Min(high, typeHigh);
            }

            if (j > 0)
            {
                low = Math.Max(low, crossoverHz[j - 1] * separation);
            }

            if (j < channelCount - 2)
            {
                high = Math.Min(high, crossoverHz[j + 1] / separation);
            }

            if (high < low)
            {
                // Bounds crossed (an over-tight user window, a measured/class
                // conflict, or neighbour separation): collapse to one sensible
                // frequency — the class band's center when the classes overlap,
                // otherwise the current seed — clamped to the requested window.
                double pinned = typeLow <= typeHigh
                    ? Math.Sqrt(typeLow * typeHigh)
                    : crossoverHz[j];
                low = high = Math.Clamp(
                    pinned, options.MinCrossoverHz, options.MaxCrossoverHz);
            }

            double[] fcGrid = LogSpace(low, high, CrossoverGridSteps);

            CrossoverFilterFamily bestFamily = junctionFamily[j];
            int bestLower = lowerSlope[j];
            int bestUpper = upperSlope[j];
            double bestFc = crossoverHz[j];
            double bestScore = Score();

            foreach (double fc in fcGrid)
            {
                foreach (CrossoverFilterFamily family in options.Families)
                {
                    IReadOnlyList<int> slopes = PracticalSlopes(family);
                    foreach (int lower in slopes)
                    {
                        if (options.IndependentSlopes)
                        {
                            foreach (int upper in slopes)
                            {
                                Set(j, family, fc, lower, upper);
                                double score = Score();
                                if (score < bestScore)
                                {
                                    bestScore = score;
                                    bestFamily = family;
                                    bestFc = fc;
                                    bestLower = lower;
                                    bestUpper = upper;
                                }
                            }
                        }
                        else
                        {
                            Set(j, family, fc, lower, lower);
                            double score = Score();
                            if (score < bestScore)
                            {
                                bestScore = score;
                                bestFamily = family;
                                bestFc = fc;
                                bestLower = lower;
                                bestUpper = lower;
                            }
                        }
                    }
                }
            }

            Set(j, bestFamily, bestFc, bestLower, bestUpper);
        }

        private void Set(
            int j,
            CrossoverFilterFamily family,
            double fc,
            int lower,
            int upper)
        {
            junctionFamily[j] = family;
            crossoverHz[j] = fc;
            lowerSlope[j] = lower;
            upperSlope[j] = upper;
        }

        private void OptimizeGains()
        {
            for (int i = 0; i < channelCount; i++)
            {
                double bestGain = gainDb[i];
                double bestScore = Score();
                double start = gainDb[i] - GainSearchRangeDb;
                double end = gainDb[i] + GainSearchRangeDb;
                for (double gain = start; gain <= end + 1e-9; gain += GainSearchStepDb)
                {
                    gainDb[i] = gain;
                    double score = Score();
                    if (score < bestScore - 1e-9)
                    {
                        bestScore = score;
                        bestGain = gain;
                    }
                }

                gainDb[i] = bestGain;
            }
        }

        // The flatness score is invariant to a global level shift, so the search
        // fixes only relative gains; reference them to the loudest for a cut-only,
        // headroom-safe result.
        private void NormalizeGainsCutOnly()
        {
            double max = gainDb.Max();
            for (int i = 0; i < channelCount; i++)
            {
                gainDb[i] -= max;
            }
        }

        private double Score()
        {
            var amplitudes = new double[channelCount][];
            for (int i = 0; i < channelCount; i++)
            {
                amplitudes[i] = ChannelAmplitude(i);
            }

            return Flatness(Combine(amplitudes)) + OverlapPenalty(amplitudes);
        }

        private double[] Combine(double[][] amplitudes)
        {
            double[] combined = (double[])amplitudes[0].Clone();
            for (int i = 1; i < channelCount; i++)
            {
                double[] amplitude = amplitudes[i];
                bool power = junctionFamily[i - 1] == CrossoverFilterFamily.Butterworth;
                for (int k = 0; k < combined.Length; k++)
                {
                    combined[k] = power
                        ? Math.Sqrt(combined[k] * combined[k] + amplitude[k] * amplitude[k])
                        : combined[k] + amplitude[k];
                }
            }

            return combined;
        }

        // RMS deviation of the summed magnitude from its own mean over the interior
        // passband, plus a heavier weight on the deepest suckout.
        private double Flatness(double[] combined)
        {
            double mean = 0;
            int count = evalHigh - evalLow + 1;
            for (int k = evalLow; k <= evalHigh; k++)
            {
                mean += DataHelper.AmplitudeToDecibels(combined[k]);
            }

            mean /= count;

            double sumSquares = 0;
            double worstDip = 0;
            for (int k = evalLow; k <= evalHigh; k++)
            {
                double deviation = DataHelper.AmplitudeToDecibels(combined[k]) - mean;
                sumSquares += deviation * deviation;
                if (-deviation > worstDip)
                {
                    worstDip = -deviation;
                }
            }

            return Math.Sqrt(sumSquares / count) + DipPenaltyWeight * worstDip;
        }

        // How many octaves adjacent drivers meaningfully overlap, summed over the
        // junctions. Each driver is normalized to its own passband peak (so gains
        // and levels drop out) and the overlap is the log-frequency integral of the
        // two normalized responses' product — near an octave for a clean LR24
        // handover, several octaves for shallow filters. Steeper slopes shrink it.
        private double OverlapPenalty(double[][] amplitudes)
        {
            double octavesPerBin = 1.0 / GridPointsPerOctave;
            double total = 0;
            for (int j = 0; j < channelCount - 1; j++)
            {
                double[] lower = amplitudes[j];
                double[] upper = amplitudes[j + 1];
                double peakLower = 0;
                double peakUpper = 0;
                for (int k = evalLow; k <= evalHigh; k++)
                {
                    peakLower = Math.Max(peakLower, lower[k]);
                    peakUpper = Math.Max(peakUpper, upper[k]);
                }

                if (peakLower <= 0 || peakUpper <= 0)
                {
                    continue;
                }

                double overlap = 0;
                for (int k = evalLow; k <= evalHigh; k++)
                {
                    overlap += lower[k] / peakLower * (upper[k] / peakUpper);
                }

                total += overlap * octavesPerBin;
            }

            return OverlapPenaltyDbPerOctave * total;
        }

        private double[] ChannelAmplitude(int i)
        {
            double gainLinear = DataHelper.DecibelsToAmplitude(gainDb[i]);
            double[]? highPass = i > 0
                ? EdgeMagnitude(junctionFamily[i - 1], crossoverHz[i - 1], upperSlope[i - 1], highPass: true)
                : lowLimitEdge is { } lowLimit ? EdgeMagnitude(lowLimit, highPass: true) : null;
            double[]? lowPass = i < channelCount - 1
                ? EdgeMagnitude(junctionFamily[i], crossoverHz[i], lowerSlope[i], highPass: false)
                : highLimitEdge is { } highLimit ? EdgeMagnitude(highLimit, highPass: false) : null;

            var amplitude = new double[grid.Length];
            for (int k = 0; k < grid.Length; k++)
            {
                double value = gainLinear * driverAmplitude[i][k];
                if (highPass != null)
                {
                    value *= highPass[k];
                }
                if (lowPass != null)
                {
                    value *= lowPass[k];
                }

                amplitude[k] = value;
            }

            return amplitude;
        }

        private double[] EdgeMagnitude(CrossoverEdge edge, bool highPass) =>
            EdgeMagnitude(edge.Family, edge.FrequencyHz, edge.SlopeDbPerOctave, highPass);

        private double[] EdgeMagnitude(
            CrossoverFilterFamily family,
            double frequencyHz,
            int slope,
            bool highPass)
        {
            long frequencyKey = (long)Math.Round(frequencyHz * 1000);
            var key = (family, slope, frequencyKey, highPass);
            if (magnitudeCache.TryGetValue(key, out double[]? cached))
            {
                return cached;
            }

            var edge = new CrossoverEdge(family, frequencyHz, slope);
            CrossoverSpec spec = highPass
                ? new CrossoverSpec(CrossoverKind.HighPass, HighPassEdge: edge)
                : new CrossoverSpec(CrossoverKind.LowPass, LowPassEdge: edge);
            var magnitude = new double[grid.Length];
            for (int k = 0; k < grid.Length; k++)
            {
                magnitude[k] = CrossoverFilter.Response(spec, grid[k], options.SampleRateHz).Magnitude;
            }

            magnitudeCache[key] = magnitude;
            return magnitude;
        }

        private IReadOnlyList<CrossoverProposal> BuildProposals()
        {
            var results = new CrossoverProposal[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                CrossoverEdge? highPass = i > 0
                    ? new CrossoverEdge(
                        junctionFamily[i - 1],
                        Math.Round(crossoverHz[i - 1]),
                        upperSlope[i - 1])
                    : lowLimitEdge;
                CrossoverEdge? lowPass = i < channelCount - 1
                    ? new CrossoverEdge(
                        junctionFamily[i],
                        Math.Round(crossoverHz[i]),
                        lowerSlope[i])
                    : highLimitEdge;
                CrossoverKind kind = (highPass, lowPass) switch
                {
                    (not null, not null) => CrossoverKind.BandPass,
                    (not null, null) => CrossoverKind.HighPass,
                    _ => CrossoverKind.LowPass
                };

                results[inputIndex[i]] = new CrossoverProposal(
                    kind,
                    highPass,
                    lowPass,
                    Math.Round(gainDb[i], 1));
            }

            return results;
        }

        private static double[] LogSpace(double low, double high, int count)
        {
            if (high <= low)
            {
                return [low];
            }

            return EqualizationCurve.LogFrequencyGrid(low, high, count).ToArray();
        }
    }
}
