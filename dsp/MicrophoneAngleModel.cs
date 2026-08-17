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
    /// The highest frequency any reference still covers after diameter scaling.
    /// Above it the estimate HOLDS its last value instead of extrapolating a
    /// diffraction curve it has no data for.
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
        // sits on or outside a tabulated size). Both are scaled onto the target
        // frequency axis, so what the pair adds is the SHAPE difference between
        // constructions, which is exactly what the spread measures.
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
        List<Candidate> preferred = matching
            .Where(candidate =>
                candidate.Curve.DiameterMm == below ||
                candidate.Curve.DiameterMm == above)
            .ToList();
        // Ordered by how far each reference is from the target in log-diameter:
        // above a reference's tabulated range the estimate steps to the nearest
        // SMALLER reference, which reaches further once scaled, rather than
        // extrapolating a curve past its data.
        List<Candidate> byCloseness = matching
            .OrderBy(candidate => Math.Abs(
                Math.Log(candidate.Curve.DiameterMm) - Math.Log(request.FrontDiameterMm)))
            .ThenBy(candidate => candidate.Curve.Label, StringComparer.Ordinal)
            .ToList();
        double highestSupported = matching.Max(candidate => candidate.HighestTargetFrequencyHz);

        return new MicrophoneAngleEstimate(
            request.AngleDegrees,
            preferred.Select(candidate => candidate.Curve.Label).ToList(),
            highestSupported,
            frequencyHz => Combine(
                CollectDeltas(
                    Math.Min(frequencyHz, highestSupported),
                    u,
                    preferred,
                    byCloseness)));
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
        double highestSupported = halfInch.Max(candidate => candidate.HighestTargetFrequencyHz);

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
    /// </summary>
    public static double SonarworksXref20Delta90Db(double frequencyHz)
    {
        double octavesAboveKnee = Math.Log2(frequencyHz / 4394.0);
        // Below the knee the two measured units showed no angular change at all,
        // and the fit is only defined above it. Return a positive zero so the
        // value reads as "no correction" everywhere it is printed.
        return octavesAboveKnee <= 0.0
            ? 0.0
            : -2.82 * Math.Pow(octavesAboveKnee, 1.248);
    }

    private static List<double> CollectDeltas(
        double frequencyHz,
        double u,
        List<Candidate> preferred,
        List<Candidate> byCloseness)
    {
        var deltas = new List<double>(preferred.Count);
        foreach (Candidate candidate in preferred)
        {
            if (candidate.TryGetDeltas(frequencyHz, out GrasAngleDeltas angleDeltas))
            {
                deltas.Add(InterpolateAngle(u, angleDeltas));
            }
        }

        if (deltas.Count > 0)
        {
            return deltas;
        }

        // Every preferred reference has run out of table here; fall back to the
        // closest diameter that still covers this frequency, and to that diameter
        // alone, so the spread stays a comparison of like constructions.
        double? fallbackDiameter = null;
        foreach (Candidate candidate in byCloseness)
        {
            if (!candidate.TryGetDeltas(frequencyHz, out GrasAngleDeltas angleDeltas))
            {
                continue;
            }

            fallbackDiameter ??= candidate.Curve.DiameterMm;
            if (candidate.Curve.DiameterMm != fallbackDiameter)
            {
                break;
            }

            deltas.Add(InterpolateAngle(u, angleDeltas));
        }

        return deltas;
    }

    private static MicrophoneAngleBounds Combine(List<double> deltas)
    {
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
