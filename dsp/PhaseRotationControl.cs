using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>A phase-control angle plus the configured crossover it is stated at; the same angle is a different filter elsewhere.</summary>
public readonly record struct PhaseRotationSpec(
    double Degrees,
    double ReferenceHz,
    bool ReferenceIsLowPass = false)
{
    public static PhaseRotationSpec None { get; }

    public bool IsTransparent =>
        !double.IsFinite(Degrees) || Degrees <= 0 ||
        !double.IsFinite(ReferenceHz) || ReferenceHz <= 0;
}

/// <summary>Audiotec-Fischer channel phase control: one Q=1 AP2 whose corner puts the angle on the configured crossover.
/// See docs/tech/dsp-helix-phase-control.md.</summary>
public static class PhaseRotationControl
{
    public const int StepCount = 64;

    /// <summary>Measured on a subwoofer channel only; assumed for mid/high.</summary>
    public const double StepDegrees = 360.0 / StepCount;

    public const double MaximumDegrees = 360.0 - StepDegrees;

    public const double SectionQ = 1.0;

    /// <summary>Measured at 96 kHz only (18 kHz); rate-relative reading assumed. See docs/tech/dsp-helix-phase-control.md#corner-ceiling.</summary>
    public const double MaximumCornerFraction = 3.0 / 16.0;

    public static double MaximumCornerHz(double sampleRateHz)
    {
        if (!(sampleRateHz > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }

        return sampleRateHz * MaximumCornerFraction;
    }

    public static double SnapToGrid(double degrees)
    {
        if (!double.IsFinite(degrees) || degrees <= 0)
        {
            return 0;
        }

        double snapped = Math.Round(degrees / StepDegrees, MidpointRounding.AwayFromZero) *
            StepDegrees;
        return Math.Clamp(snapped, 0, MaximumDegrees);
    }

    /// <summary>Null when transparent; a capped corner delivers less than asked (see <see cref="DeliveredDegrees"/>).</summary>
    public static AllPassSpec? Realize(PhaseRotationSpec rotation, double sampleRateHz)
    {
        if (!(sampleRateHz > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }
        if (rotation.IsTransparent)
        {
            return null;
        }

        return new AllPassSpec(
            AllPassType.SecondOrder,
            SolveCornerHz(
                Math.Min(rotation.Degrees, MaximumDegrees),
                rotation.ReferenceHz,
                sampleRateHz),
            SectionQ);
    }

    public static double DeliveredDegrees(PhaseRotationSpec rotation, double sampleRateHz) =>
        Realize(rotation, sampleRateHz) is { } realized
            ? RotationAt(realized.FrequencyHz, rotation.ReferenceHz, sampleRateHz)
            : 0;

    /// <summary>Lag in degrees over (0, 360), decreasing as the corner rises.</summary>
    public static double RotationAt(double cornerHz, double referenceHz, double sampleRateHz)
    {
        Complex response = AllPassFilter.Response(
            new AllPassSpec(AllPassType.SecondOrder, cornerHz, SectionQ),
            referenceHz,
            sampleRateHz);
        // Unfold the arctangent past 180°.
        double lag = -response.Phase * 180.0 / Math.PI;
        return lag < 0 ? lag + 360.0 : lag;
    }

    private static double SolveCornerHz(
        double degrees,
        double referenceHz,
        double sampleRateHz)
    {
        double high = MaximumCornerHz(sampleRateHz);
        if (RotationAt(high, referenceHz, sampleRateHz) >= degrees)
        {
            return high;
        }

        double low = referenceHz / 64.0;
        for (int i = 0; i < 8 && RotationAt(low, referenceHz, sampleRateHz) < degrees; i++)
        {
            low /= 8.0;
        }

        for (int i = 0; i < 100; i++)
        {
            double middle = Math.Sqrt(low * high);
            if (RotationAt(middle, referenceHz, sampleRateHz) >= degrees)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return Math.Sqrt(low * high);
    }
}
