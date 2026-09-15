namespace Resonalyze.Dsp;

public enum MicrophoneAngleReference
{
    GrasGeometry,
    SonarworksXref20
}

/// <summary><paramref name="FrontDiameterMm"/> is the OUTER front diameter around the capsule (sets diffraction).
/// Diameter and grid are ignored when <paramref name="Reference"/> names a measured microphone.</summary>
public sealed record MicrophoneAngleRequest(
    double AngleDegrees,
    double FrontDiameterMm,
    MicrophoneProtectionGrid Grid = MicrophoneProtectionGrid.Unknown,
    MicrophoneAngleReference Reference = MicrophoneAngleReference.GrasGeometry);

/// <summary>Spread = how much the reference variants differ (half-inch GRAS differ by 2+ dB at 20 kHz), not a confidence interval.</summary>
public readonly record struct MicrophoneAngleBounds(
    double CenterDb,
    double LowerDb,
    double UpperDb);

/// <summary>An ESTIMATE from reference microphones of comparable geometry, never a measurement of this microphone.</summary>
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

    public IReadOnlyList<string> References { get; }

    /// <summary>Where the first reference runs out of table; above it references hold their last value rather than swap mid-curve (a swap steps ~9 dB).</summary>
    public double HighestSupportedFrequencyHz { get; }

    public MicrophoneAngleBounds Deltas(double frequencyHz) =>
        evaluate(frequencyHz);

    public double DeltaDb(double frequencyHz) => evaluate(frequencyHz).CenterDb;
}

/// <summary>Off-axis calibration of an axisymmetric omni end-address microphone from GRAS diffraction families: G(θ)−G(0),
/// frequency scaled by diameter (ka), angle interpolated in 1−cos θ, median ± spread. Not for cardioid, side-address, windscreens; no phase.</summary>
public static class MicrophoneAngleModel
{
    /// <summary>Measured on two XREF 20 units; a generic 12.7 mm estimate misses it by up to 2.2 dB at 20 kHz.</summary>
    public const double SonarworksXref20DiameterMm = 12.7;

    public const double SonarworksXref20HighestFittedHz = 20_000.0;

    // u = 1 − cos θ approaches zero quadratically like diffraction; linear degrees overshoot.
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

        // Each size aggregated separately then blended by log-diameter, so a hair above a size reads like that size.
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
        // Only reports the limit: a shared cut-off would let a negligibly weighted family truncate the dominant one.
        double reportedLimit = lower
            .Concat(upper)
            .Min(candidate => candidate.HighestTargetFrequencyHz);

        return new MicrophoneAngleEstimate(
            request.AngleDegrees,
            lower.Concat(upper).Select(candidate => candidate.Curve.Label).ToList(),
            reportedLimit,
            frequencyHz =>
            {
                MicrophoneAngleBounds atBelow = Aggregate(lower, frequencyHz, u);
                return upper.Count == 0
                    ? atBelow
                    : Interpolate(atBelow, Aggregate(upper, frequencyHz, u), blend);
            });
    }

    private static MicrophoneAngleEstimate EstimateFromSonarworksXref20(
        MicrophoneAngleRequest request,
        double u)
    {
        // Only 90° is measured: angle shape (1 − cos θ)^0.85 is an empirical fit; half-inch GRAS variants give the spread (zero at 90°).
        double analyticFactor = Math.Pow(u, 0.85);
        List<Candidate> halfInch = GrasFreeFieldCorrections.Curves
            .Where(curve => curve.DiameterMm == SonarworksXref20DiameterMm)
            .Select(curve => new Candidate(curve, 1.0))
            .ToList();
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
                foreach (Candidate candidate in halfInch)
                {
                    GrasAngleDeltas deltas = candidate.GetDeltas(frequencyHz);
                    if (Math.Abs(deltas.At90) >= MinimumUsableAngleDeltaDb)
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

    /// <summary>Measured 90°−0° difference of the XREF 20 in dB; held above 20 kHz (the power law would reach −18 dB at 96 kHz).</summary>
    public static double SonarworksXref20Delta90Db(double frequencyHz)
    {
        double octavesAboveKnee = Math.Log2(
            Math.Min(frequencyHz, SonarworksXref20HighestFittedHz) / 4394.0);
        // Positive zero below the knee (no angular change measured).
        return octavesAboveKnee <= 0.0
            ? 0.0
            : -2.82 * Math.Pow(octavesAboveKnee, 1.248);
    }

    // Same size: spread reads as construction variance, not size variance.
    private static MicrophoneAngleBounds Aggregate(
        List<Candidate> candidates,
        double frequencyHz,
        double u)
    {
        var deltas = new List<double>(candidates.Count);
        foreach (Candidate candidate in candidates)
        {
            deltas.Add(InterpolateAngle(u, candidate.GetDeltas(frequencyHz)));
        }

        deltas.Sort();
        int middle = deltas.Count / 2;
        double median = deltas.Count % 2 == 1
            ? deltas[middle]
            : (deltas[middle - 1] + deltas[middle]) / 2.0;
        return new MicrophoneAngleBounds(median, deltas[0], deltas[^1]);
    }

    // Log-diameter: ka scales with d, so the geometric mean is the behavioural midpoint.
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

        /// <summary><c>f_r = f * d_t / d_r</c> keeps <c>ka</c> equal.</summary>
        public double FrequencyScale { get; }

        public double HighestTargetFrequencyHz => Curve.MaxFrequencyHz / FrequencyScale;

        /// <summary>Holds the last tabulated value above range here, not at the caller: clamping via the diameter ratio can land one ulp past the end.</summary>
        public GrasAngleDeltas GetDeltas(double frequencyHz)
        {
            Curve.TryGetAngleDeltas(
                Math.Min(frequencyHz * FrequencyScale, Curve.MaxFrequencyHz),
                out GrasAngleDeltas deltas);
            return deltas;
        }
    }
}
