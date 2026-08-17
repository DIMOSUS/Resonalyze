namespace Resonalyze.Dsp;

/// <summary>
/// Which reference the angular estimate is built from: the GRAS geometry model
/// (diameter-scaled measurement families) or a microphone whose own 90° angular
/// difference has been measured.
/// </summary>
public enum MicrophoneAngleReference
{
    GrasGeometry,
    SonarworksXref20
}

/// <summary>
/// The microphone an angular calibration is estimated for.
/// <paramref name="FrontDiameterMm"/> is the OUTER diameter of the front around
/// the capsule — the scale that sets the diffraction, not the diameter of the
/// diaphragm or of the handle. Diameter and grid are ignored when
/// <paramref name="Reference"/> names a specific microphone, which carries its
/// own measured behaviour.
/// </summary>
public sealed record MicrophoneAngleRequest(
    double AngleDegrees,
    double FrontDiameterMm,
    MicrophoneProtectionGrid Grid = MicrophoneProtectionGrid.Unknown,
    MicrophoneAngleReference Reference = MicrophoneAngleReference.GrasGeometry);

/// <summary>
/// The estimated angular difference at one frequency: the central estimate and
/// the spread of the reference variants it was taken from. The spread is the
/// honest uncertainty of a geometric estimate — half-inch GRAS constructions
/// differ by more than 2 dB at 20 kHz — not a confidence interval.
/// </summary>
public readonly record struct MicrophoneAngleBounds(
    double CenterDb,
    double LowerDb,
    double UpperDb);

/// <summary>
/// An angular correction curve estimated for one microphone and one angle:
/// evaluate it at any frequency with <see cref="Deltas"/>. Everything it
/// returns is an ESTIMATE derived from reference microphones of comparable
/// geometry — never a measurement of the microphone in hand.
/// </summary>
public sealed class MicrophoneAngleEstimate
{
    private readonly Func<double, MicrophoneAngleBounds> evaluate;

    internal MicrophoneAngleEstimate(
        double angleDegrees,
        IReadOnlyList<string> references,
        double highestSupportedFrequencyHz,
        Func<double, MicrophoneAngleBounds> evaluate)
    {
        AngleDegrees = angleDegrees;
        References = references;
        HighestSupportedFrequencyHz = highestSupportedFrequencyHz;
        this.evaluate = evaluate;
    }

    public double AngleDegrees { get; }

    /// <summary>Labels of the reference curves the estimate was taken from.</summary>
    public IReadOnlyList<string> References { get; }

    /// <summary>
    /// The highest frequency EVERY reference behind this estimate still covers
    /// after diameter scaling. Above it the estimate HOLDS its last value
    /// instead of extrapolating a diffraction curve it has no data for — and
    /// the limit is the point where the FIRST reference runs out rather than the
    /// last, so the set of references never changes mid-curve. Swapping one
    /// reference for another at some frequency would step the correction (a 1"
    /// microphone read from a quarter-inch reference jumps by ~9 dB) and leave
    /// <see cref="References"/> naming curves the top of the band never used.
    /// </summary>
    public double HighestSupportedFrequencyHz { get; }

    public MicrophoneAngleBounds Deltas(double frequencyHz) =>
        evaluate(frequencyHz);

    public double DeltaDb(double frequencyHz) => evaluate(frequencyHz).CenterDb;
}

/// <summary>
/// Estimates the calibration of an axisymmetric, nominally omnidirectional
/// end-address measurement microphone at an off-axis angle, from its own 0°
/// calibration plus the geometry of its front.
/// <para>
/// The model reads the GRAS free-field correction families as measured
/// diffraction of known geometries, takes only the CHANGE with angle
/// (<c>G(theta) - G(0)</c>, so the table's 250 Hz normalization cancels), scales
/// the frequency axis of each reference by the diameter ratio (diffraction
/// follows <c>ka = pi*d*f/c</c>), interpolates the angle in <c>1 - cos(theta)</c>
/// through the tabulated 0/30/60/90 nodes, and reports the median of the
/// matching references as the estimate with their spread as the uncertainty.
/// </para>
/// <para>
/// It does NOT apply to cardioid, shotgun, side-address or boundary microphones,
/// to a microphone wearing a windscreen, or to any front that is not
/// axisymmetric — and it says nothing about phase.
/// </para>
/// </summary>
public static class MicrophoneAngleModel
{
    /// <summary>
    /// The 90° difference measured on two Sonarworks XREF 20 units (12.7 mm
    /// front), fitted to 0.05 dB RMS over 20 Hz - 20 kHz. Kept as a named model
    /// because a generic 12.7 mm estimate misses it by up to 2.2 dB at 20 kHz:
    /// physical size alone does not fix directivity.
    /// </summary>
    public const double SonarworksXref20DiameterMm = 12.7;

    /// <summary>The top of the band the Sonarworks difference was fitted over.</summary>
    public const double SonarworksXref20HighestFittedHz = 20_000.0;

    // u = 1 - cos(theta) at the tabulated angles 0, 30, 60 and 90 degrees. The
    // substitution is what makes the interpolation behave: it approaches zero
    // quadratically at small angles, the way diffraction does, where linear
    // interpolation in degrees overshoots.
    private static readonly double[] AngleNodes =
        [0.0, 1.0 - 0.86602540378443865, 0.5, 1.0];

    private const double MinimumUsableAngleDeltaDb = 0.05;

    public static MicrophoneAngleEstimate Estimate(MicrophoneAngleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AngleDegrees is < 0 or > 90)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.AngleDegrees,
                "The angle model covers 0 to 90 degrees of incidence.");
        }

        double u = 1.0 - Math.Cos(request.AngleDegrees * Math.PI / 180.0);
        return request.Reference == MicrophoneAngleReference.SonarworksXref20
            ? EstimateFromSonarworksXref20(request, u)
            : EstimateFromGeometry(request, u);
    }

    private static MicrophoneAngleEstimate EstimateFromGeometry(
        MicrophoneAngleRequest request,
        double u)
    {
        if (request.FrontDiameterMm <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.FrontDiameterMm,
                "The front diameter must be positive.");
        }

        List<Candidate> matching = GrasFreeFieldCorrections.Curves
            .Where(curve =>
                request.Grid == MicrophoneProtectionGrid.Unknown ||
                curve.Grid == request.Grid)
            .Select(curve => new Candidate(curve, request.FrontDiameterMm / curve.DiameterMm))
            .ToList();

        // The two reference sizes the target diameter falls between (one when it
        // sits on or outside a tabulated size). Each size is aggregated on its
        // own and the two are then blended by log-diameter, so a target sitting
        // a hair above a tabulated size reads almost exactly like that size
        // instead of suddenly averaging in a family twice its diameter.
        double[] diameters = matching
            .Select(candidate => candidate.Curve.DiameterMm)
            .Distinct()
            .OrderBy(diameter => diameter)
            .ToArray();
        double below = diameters.LastOrDefault(
            diameter => diameter <= request.FrontDiameterMm,
            diameters[0]);
        double above = diameters.FirstOrDefault(
            diameter => diameter >= request.FrontDiameterMm,
            diameters[^1]);
        List<Candidate> lower = matching
            .Where(candidate => candidate.Curve.DiameterMm == below)
            .ToList();
        List<Candidate> upper = above == below
            ? []
            : matching
                .Where(candidate => candidate.Curve.DiameterMm == above)
                .ToList();
        double blend = upper.Count == 0
            ? 0.0
            : (Math.Log(request.FrontDiameterMm) - Math.Log(below)) /
              (Math.Log(above) - Math.Log(below));
        double highestSupported = lower
            .Concat(upper)
            .Min(candidate => candidate.HighestTargetFrequencyHz);

        return new MicrophoneAngleEstimate(
            request.AngleDegrees,
            lower.Concat(upper).Select(candidate => candidate.Curve.Label).ToList(),
            highestSupported,
            frequencyHz =>
            {
                double bounded = Math.Min(frequencyHz, highestSupported);
                MicrophoneAngleBounds atBelow = Aggregate(lower, bounded, u);
                return upper.Count == 0
                    ? atBelow
                    : Interpolate(atBelow, Aggregate(upper, bounded, u), blend);
            });
    }

    private static MicrophoneAngleEstimate EstimateFromSonarworksXref20(
        MicrophoneAngleRequest request,
        double u)
    {
        // Only the 90° difference is measured for this microphone, so the angle
        // is taken from the compact (1 - cos theta)^0.85 shape rather than from a
        // table (the exponent is an empirical fit to the GRAS angular data, not a
        // GRAS formula). The half-inch GRAS variants supply the SPREAD of that
        // shape, so the band collapses to the fit at 90°, where the difference is
        // measured rather than modelled.
        double analyticFactor = Math.Pow(u, 0.85);
        List<Candidate> halfInch = GrasFreeFieldCorrections.Curves
            .Where(curve => curve.DiameterMm == SonarworksXref20DiameterMm)
            .Select(curve => new Candidate(curve, 1.0))
            .ToList();
        // Bounded by the FIRST thing that runs out — the fit's own band or the
        // half-inch references that shape the spread — so neither the measured
        // difference nor the set of references behind the band changes mid-curve.
        double highestSupported = Math.Min(
            SonarworksXref20HighestFittedHz,
            halfInch.Min(candidate => candidate.HighestTargetFrequencyHz));

        return new MicrophoneAngleEstimate(
            request.AngleDegrees,
            ["Sonarworks XREF 20 (measured 90°)"],
            highestSupported,
            frequencyHz =>
            {
                double delta90 = SonarworksXref20Delta90Db(frequencyHz);
                var factors = new List<double> { analyticFactor };
                double bounded = Math.Min(frequencyHz, highestSupported);
                foreach (Candidate candidate in halfInch)
                {
                    if (candidate.TryGetDeltas(bounded, out GrasAngleDeltas deltas) &&
                        Math.Abs(deltas.At90) >= MinimumUsableAngleDeltaDb)
                    {
                        factors.Add(InterpolateAngle(u, deltas) / deltas.At90);
                    }
                }

                return new MicrophoneAngleBounds(
                    analyticFactor * delta90,
                    factors.Min(factor => factor * delta90),
                    factors.Max(factor => factor * delta90));
            });
    }

    /// <summary>
    /// The measured 90°-minus-0° difference of the Sonarworks XREF 20, in dB.
    /// The fit was taken over 20 Hz - 20 kHz and HOLDS above that: it is a
    /// power law with no turnover, so continuing it would reach -13 dB at
    /// 48 kHz and -18 dB at 96 kHz on nothing but arithmetic — and those
    /// frequencies are reached, since the audition FIR samples the correction
    /// up to Nyquist.
    /// </summary>
    public static double SonarworksXref20Delta90Db(double frequencyHz)
    {
        double octavesAboveKnee = Math.Log2(
            Math.Min(frequencyHz, SonarworksXref20HighestFittedHz) / 4394.0);
        // Below the knee the two measured units showed no angular change at all,
        // and the fit is only defined above it. Return a positive zero so the
        // value reads as "no correction" everywhere it is printed.
        return octavesAboveKnee <= 0.0
            ? 0.0
            : -2.82 * Math.Pow(octavesAboveKnee, 1.248);
    }

    // One tabulated size, read at a frequency the caller has already bounded to
    // what every candidate covers: the median of its constructions with their
    // spread. Same size means comparable geometry, so the spread reads as "how
    // much do microphones this size differ", not "how much do sizes differ".
    private static MicrophoneAngleBounds Aggregate(
        List<Candidate> candidates,
        double frequencyHz,
        double u)
    {
        var deltas = new List<double>(candidates.Count);
        foreach (Candidate candidate in candidates)
        {
            if (candidate.TryGetDeltas(frequencyHz, out GrasAngleDeltas angleDeltas))
            {
                deltas.Add(InterpolateAngle(u, angleDeltas));
            }
        }

        if (deltas.Count == 0)
        {
            return default;
        }

        deltas.Sort();
        int middle = deltas.Count / 2;
        double median = deltas.Count % 2 == 1
            ? deltas[middle]
            : (deltas[middle - 1] + deltas[middle]) / 2.0;
        return new MicrophoneAngleBounds(median, deltas[0], deltas[^1]);
    }

    // Between the two tabulated sizes the target falls between, by log-diameter:
    // ka scales with the diameter, so the geometric mean of two sizes is the
    // midpoint of the behaviour, not the arithmetic one.
    private static MicrophoneAngleBounds Interpolate(
        MicrophoneAngleBounds below,
        MicrophoneAngleBounds above,
        double position) =>
        new(
            below.CenterDb + (above.CenterDb - below.CenterDb) * position,
            below.LowerDb + (above.LowerDb - below.LowerDb) * position,
            below.UpperDb + (above.UpperDb - below.UpperDb) * position);

    private static double InterpolateAngle(double u, GrasAngleDeltas deltas)
    {
        double[] values = [0.0, deltas.At30, deltas.At60, deltas.At90];
        if (u <= AngleNodes[0])
        {
            return 0.0;
        }

        for (int node = 1; node < AngleNodes.Length; node++)
        {
            if (u <= AngleNodes[node])
            {
                double position =
                    (u - AngleNodes[node - 1]) /
                    (AngleNodes[node] - AngleNodes[node - 1]);
                return values[node - 1] +
                    (values[node] - values[node - 1]) * position;
            }
        }

        return deltas.At90;
    }

    private sealed class Candidate
    {
        public Candidate(GrasReferenceCurve curve, double frequencyScale)
        {
            Curve = curve;
            FrequencyScale = frequencyScale;
        }

        public GrasReferenceCurve Curve { get; }

        /// <summary>
        /// Target frequency to reference frequency: <c>f_r = f * d_t / d_r</c>,
        /// the substitution that keeps <c>ka</c> equal between the two housings.
        /// </summary>
        public double FrequencyScale { get; }

        public double HighestTargetFrequencyHz => Curve.MaxFrequencyHz / FrequencyScale;

        public bool TryGetDeltas(double frequencyHz, out GrasAngleDeltas deltas) =>
            Curve.TryGetAngleDeltas(frequencyHz * FrequencyScale, out deltas);
    }
}
