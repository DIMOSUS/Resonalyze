using System.Numerics;

namespace Resonalyze.Dsp;

/// <summary>
/// One setting of a processor's channel PHASE control: how far that channel's phase
/// is turned, and the crossover frequency the turn is stated at.
/// </summary>
/// <remarks>
/// Unlike every other filter in this library the user does not choose a corner
/// frequency here — they choose an ANGLE, and the device works out the corner that
/// delivers it at the channel's own crossover. Both numbers therefore have to travel
/// together: the same 90° means a different filter on a channel crossed elsewhere,
/// which is exactly what makes this control unlike a PEQ band and unlike the all-pass
/// bands of <see cref="PeqBandType.AllPassSecondOrder"/>.
/// </remarks>
/// <param name="Degrees">
/// The rotation asked for, 0 to <see cref="PhaseRotationControl.MaximumDegrees"/>.
/// Zero is transparent. The devices step this on a grid (see
/// <see cref="PhaseRotationControl.StepDegrees"/>); the DSP accepts any angle in
/// range, so a hand-edited project does not become unopenable, and the editors do
/// the snapping.
/// </param>
/// <param name="ReferenceHz">
/// The crossover frequency the angle is measured at — the channel's own, AS
/// CONFIGURED. Zero (no reference) makes the setting transparent.
/// </param>
/// <param name="ReferenceIsLowPass">
/// WHICH of the channel's two corners <see cref="ReferenceHz"/> is: its low-pass
/// on a subwoofer channel, its high-pass on every other one. The frequency alone
/// cannot say, and a search that moves one corner has to know whether this is the
/// one that moves — a chain carries only the corners its kind engages, so a
/// channel whose reference sits on a filter it is not currently using would
/// otherwise be unreadable.
/// </param>
public readonly record struct PhaseRotationSpec(
    double Degrees,
    double ReferenceHz,
    bool ReferenceIsLowPass = false)
{
    /// <summary>No rotation at all.</summary>
    public static PhaseRotationSpec None { get; }

    /// <summary>
    /// True when this setting builds no filter: no angle, or no reference to state it
    /// against.
    /// </summary>
    public bool IsTransparent =>
        !double.IsFinite(Degrees) || Degrees <= 0 ||
        !double.IsFinite(ReferenceHz) || ReferenceHz <= 0;
}

/// <summary>
/// The channel phase control of an Audiotec-Fischer processor (HELIX / MATCH / BRAX),
/// modelled as the hardware realizes it: a single second-order all-pass with
/// <see cref="SectionQ"/> = 1, whose corner the device places so that the filter's
/// phase at the channel's crossover frequency equals the angle the user dialled.
/// </summary>
/// <remarks>
/// <para>
/// The law is not folklore — it was measured on a DSP ULTRA S over sixty-odd electrical
/// sweeps (issue #88): nine ratio curves fit a single RBJ all-pass at 0.11-0.17° rms
/// with Q = 1.0000, magnitude flat to 0.02 dB, and the fitted corners land on the
/// solution of "phase = the setting at the crossover" to 0.2 % (7961 Hz measured
/// against 7977 solved for 90° at a 5 kHz reference; 4999 against 5000 for 180°; 3109
/// against 3107 for 270°). Audiotec-Fischer's own knowledge base states the same rule
/// in prose, and adds the grid and the range this class carries as constants.
/// </para>
/// <para>
/// Two facts here are measurement rather than documentation, and both change what an
/// implementation has to do. The reference is the crossover AS CONFIGURED, not as
/// active: Bypass and slope = OFF leave the phase reference in place, so reading the
/// live crossover gives the wrong corner on every channel whose filter is switched
/// off. And the corner is CAPPED (see <see cref="MaximumCornerFraction"/>), which the
/// documentation does not mention at all: at a high crossover the smallest steps of
/// the control collapse onto one filter and deliver an angle that is not even on the
/// control's own grid.
/// </para>
/// <para>
/// Everything is solved at the PROCESSOR's rate, like every other filter here: the
/// same 90° at a 5 kHz reference is a 7977 Hz corner on a 96 kHz device and 7674 Hz on
/// a 48 kHz one, because the corner is placed in the digital domain.
/// </para>
/// </remarks>
public static class PhaseRotationControl
{
    /// <summary>The positions the control offers over a full turn.</summary>
    public const int StepCount = 64;

    /// <summary>
    /// The grid the control steps on: 360/64 = 5.625°. Audiotec-Fischer document it;
    /// the measurement confirms it where nothing is capped (one step read -5.54°
    /// against a nominal 5.625 at a 500 Hz reference, two steps -11.29 against 11.25).
    /// </summary>
    public const double StepDegrees = 360.0 / StepCount;

    /// <summary>
    /// The largest rotation the control reaches: 63 steps, 354.375°. A full 360° is
    /// not offered — it would be the same filter as 0 anyway.
    /// </summary>
    public const double MaximumDegrees = 360.0 - StepDegrees;

    /// <summary>
    /// The Q of the section, measured as 1.0000 on six independent curves across the
    /// whole reachable range (45°, 270°, 354.375° and the two working steps of a
    /// 500 Hz block). It is a constant of the control, not something the user sets —
    /// which is what separates this from the AP2 band of a PEQ bank.
    /// </summary>
    public const double SectionQ = 1.0;

    /// <summary>
    /// How high the device will place the corner, as a fraction of the processing
    /// rate: 3/16, i.e. 18 kHz at 96 kHz.
    /// </summary>
    /// <remarks>
    /// Measured, and measured only at 96 kHz: three independent recoveries at two
    /// reference frequencies put the ceiling at 18007-18011 Hz. That is 3/16 of the
    /// rate to within the spread, and an absolute 18 kHz to within the same spread —
    /// one rate cannot separate the two, and no 48 kHz unit was available to settle
    /// it. This library takes the rate-relative reading because the rest of the
    /// implementation is normalized to the rate (the corner itself is placed in the
    /// digital domain), and one coefficient generator serving both generations of
    /// device would most naturally clamp there too. On a 96 kHz processor — every
    /// HELIX unit the control matters most on — the two readings are the same number,
    /// so the choice only bites on the 48 kHz models, and only at a high crossover.
    /// If a 48 kHz unit is ever measured, this constant is the one line to correct.
    /// <para>
    /// Whether the ceiling is deliberate or a firmware defect is not established;
    /// Audiotec-Fischer's documentation says nothing about it. It is modelled because
    /// it is what the hardware does, and <see cref="DeliveredDegrees"/> exists so the
    /// user can see when it bites rather than being told what they asked for.
    /// </para>
    /// </remarks>
    public const double MaximumCornerFraction = 3.0 / 16.0;

    /// <summary>The highest corner the control will place at this processing rate.</summary>
    public static double MaximumCornerHz(double sampleRateHz)
    {
        if (!(sampleRateHz > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        }

        return sampleRateHz * MaximumCornerFraction;
    }

    /// <summary>
    /// The nearest setting the control can actually hold, clamped into its range. The
    /// editors snap through this so a project cannot be dialled to an angle the device
    /// has no position for.
    /// </summary>
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

    /// <summary>
    /// The all-pass this setting realizes at the given processing rate, or null when
    /// it is transparent. The corner is the one that puts the requested angle on the
    /// reference — or <see cref="MaximumCornerHz"/> when that would need a higher one,
    /// in which case the filter delivers less than was asked for and
    /// <see cref="DeliveredDegrees"/> says how much.
    /// </summary>
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

    /// <summary>
    /// The rotation this setting really produces at its reference — the number the
    /// user asked for, unless the corner hit the ceiling, and then the smaller one the
    /// device can deliver. Zero for a transparent setting.
    /// </summary>
    public static double DeliveredDegrees(PhaseRotationSpec rotation, double sampleRateHz) =>
        Realize(rotation, sampleRateHz) is { } realized
            ? RotationAt(realized.FrequencyHz, rotation.ReferenceHz, sampleRateHz)
            : 0;

    /// <summary>
    /// How far a <see cref="SectionQ"/> all-pass with this corner turns the phase at
    /// <paramref name="referenceHz"/>, as a positive lag in degrees over (0, 360).
    /// Monotonically DECREASING in the corner: the higher the corner, the less of the
    /// section's turn has happened by the reference.
    /// </summary>
    public static double RotationAt(double cornerHz, double referenceHz, double sampleRateHz)
    {
        Complex response = AllPassFilter.Response(
            new AllPassSpec(AllPassType.SecondOrder, cornerHz, SectionQ),
            referenceHz,
            sampleRateHz);
        // The section's own turn runs to a full 360°, and an arctangent reports it
        // folded into (-180, 180]. Everything past half a turn therefore comes back
        // positive and has to be unfolded, or the solver would see the curve jump from
        // 180 back to 0 in the middle of its search.
        double lag = -response.Phase * 180.0 / Math.PI;
        return lag < 0 ? lag + 360.0 : lag;
    }

    // Bisection on the log of the corner. The curve is smooth and monotone over the
    // whole range (checked against the closed form at 2000 points), so there is no
    // cleverer root to find; a hundred halvings put the corner well inside the 0.2 %
    // the bench itself resolves.
    private static double SolveCornerHz(
        double degrees,
        double referenceHz,
        double sampleRateHz)
    {
        double high = MaximumCornerHz(sampleRateHz);
        if (RotationAt(high, referenceHz, sampleRateHz) >= degrees)
        {
            // Even the highest corner the device will build turns the phase further
            // than this: the setting is capped, and every smaller one lands here too.
            return high;
        }

        // Far enough below the reference that the section has turned nearly all of its
        // 360° by the time it gets there. One step is enough for any angle the control
        // offers; the loop is a guard, not a search.
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
