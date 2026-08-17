using System.Globalization;
using System.Reflection;

namespace Resonalyze.Dsp;

/// <summary>
/// Whether the microphone wears its protection grid. The grid changes the front
/// geometry enough to move the high-frequency directivity by more than a
/// decibel, so it selects which GRAS reference variants may be used; Unknown
/// keeps both kinds as candidates and widens the uncertainty band instead of
/// guessing.
/// </summary>
public enum MicrophoneProtectionGrid
{
    Unknown,
    Fitted,
    Removed
}

/// <summary>The angular differences G(theta) - G(0 deg) of a reference curve, in dB.</summary>
public readonly record struct GrasAngleDeltas(double At30, double At60, double At90);

/// <summary>
/// One tabulated GRAS reference variant: free-field corrections at 0, 30, 60 and
/// 90 degrees of incidence for a microphone of a known outer diameter, with or
/// without its protection grid. Only the differences between angles are used by
/// the angle model, so the table's normalization (dB relative to the pressure
/// response at 250 Hz) cancels out.
/// </summary>
public sealed class GrasReferenceCurve
{
    private readonly double[] frequencies;
    // [angle][point], angle 0..3 = 0/30/60/90 degrees.
    private readonly double[][] levels;

    internal GrasReferenceCurve(
        string label,
        MicrophoneProtectionGrid grid,
        double diameterMm,
        double[] frequencies,
        double[][] levels)
    {
        Label = label;
        Grid = grid;
        DiameterMm = diameterMm;
        this.frequencies = frequencies;
        this.levels = levels;
    }

    /// <summary>Human-readable reference name, e.g. "Half-inch (Opt1), no grid".</summary>
    public string Label { get; }

    public MicrophoneProtectionGrid Grid { get; }

    /// <summary>Nominal outer diameter of the reference microphone, in millimetres.</summary>
    public double DiameterMm { get; }

    public double MinFrequencyHz => frequencies[0];

    public double MaxFrequencyHz => frequencies[^1];

    /// <summary>
    /// The angular differences at <paramref name="hz"/>, interpolated linearly in
    /// (log frequency, dB) — the reading the table is drawn for. Below the
    /// tabulated range the differences are zero: diffraction around the housing
    /// is negligible there, and the table itself starts at 0.01 dB. Above it the
    /// lookup FAILS rather than extrapolating, and the caller holds its last
    /// value: reading another size's curve instead would step the correction by
    /// several decibels mid-band.
    /// </summary>
    public bool TryGetAngleDeltas(double hz, out GrasAngleDeltas deltas)
    {
        if (hz > MaxFrequencyHz)
        {
            deltas = default;
            return false;
        }

        if (hz <= MinFrequencyHz)
        {
            deltas = default;
            return true;
        }

        int left = 0;
        int right = frequencies.Length - 1;
        while (right - left > 1)
        {
            int middle = (left + right) / 2;
            if (frequencies[middle] <= hz)
            {
                left = middle;
            }
            else
            {
                right = middle;
            }
        }

        double position =
            Math.Log(hz / frequencies[left]) /
            Math.Log(frequencies[right] / frequencies[left]);
        double zero = Interpolate(0, left, right, position);
        deltas = new GrasAngleDeltas(
            Interpolate(1, left, right, position) - zero,
            Interpolate(2, left, right, position) - zero,
            Interpolate(3, left, right, position) - zero);
        return true;
    }

    private double Interpolate(int angle, int left, int right, double position)
    {
        double low = levels[angle][left];
        return low + (levels[angle][right] - low) * position;
    }
}

/// <summary>
/// The GRAS free-field correction table, shipped as an embedded copy of the
/// published workbook (see THIRD-PARTY-NOTICES.md). Three blocks of the workbook
/// are deliberately absent: the 1-inch sheet's second block, which is a
/// normalized view of the same grid-fitted microphone rather than a second
/// variant; the 46BC sheet, whose construction has no stated outer diameter and
/// therefore cannot be frequency-scaled onto another microphone honestly; and
/// the 146AE "without grid" block, which is byte-identical to the half-inch Opt1
/// one and would give that single shape two votes in the median.
/// </summary>
public static class GrasFreeFieldCorrections
{
    private const string ResourceName = "Resonalyze.Dsp.Data.GrasFreeFieldCorrections.csv";

    private static readonly Lazy<IReadOnlyList<GrasReferenceCurve>> LazyCurves = new(Load);

    public static IReadOnlyList<GrasReferenceCurve> Curves => LazyCurves.Value;

    /// <summary>The distinct reference diameters, ascending.</summary>
    public static IReadOnlyList<double> Diameters => LazyCurves.Value
        .Select(curve => curve.DiameterMm)
        .Distinct()
        .OrderBy(diameter => diameter)
        .ToList();

    private static IReadOnlyList<GrasReferenceCurve> Load()
    {
        using Stream stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded GRAS correction table '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        var blocks = new List<(string Family, string Variant, double DiameterMm, List<double[]> Rows)>();
        while (reader.ReadLine() is string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            string[] fields = trimmed.Split(',');
            if (fields.Length != 8)
            {
                throw new InvalidOperationException(
                    $"Malformed GRAS correction row: {trimmed}");
            }

            string family = fields[0];
            string variant = fields[1];
            double[] row = new double[5];
            for (int index = 0; index < row.Length; index++)
            {
                row[index] = double.Parse(
                    fields[index + 3],
                    CultureInfo.InvariantCulture);
            }

            if (blocks.Count == 0 ||
                blocks[^1].Family != family ||
                blocks[^1].Variant != variant)
            {
                blocks.Add((
                    family,
                    variant,
                    double.Parse(fields[2], CultureInfo.InvariantCulture),
                    new List<double[]>()));
            }

            blocks[^1].Rows.Add(row);
        }

        var curves = new List<GrasReferenceCurve>(blocks.Count);
        foreach ((string family, string variant, double diameterMm, List<double[]> rows) in blocks)
        {
            rows.Sort((left, right) => left[0].CompareTo(right[0]));
            double[] frequencies = rows.Select(row => row[0]).ToArray();
            double[][] levels =
            [
                rows.Select(row => row[1]).ToArray(),
                rows.Select(row => row[2]).ToArray(),
                rows.Select(row => row[3]).ToArray(),
                rows.Select(row => row[4]).ToArray()
            ];
            curves.Add(new GrasReferenceCurve(
                $"{FamilyLabel(family)} ({VariantLabel(variant)})",
                variant == "NoGrid"
                    ? MicrophoneProtectionGrid.Removed
                    : MicrophoneProtectionGrid.Fitted,
                diameterMm,
                frequencies,
                levels));
        }

        return curves;
    }

    private static string FamilyLabel(string family) => family switch
    {
        "OneInch" => "1\"",
        "HalfInchOpt1" => "½\" (Opt1)",
        "HalfInchOpt2" => "½\" (Opt2)",
        "QuarterInch" => "¼\"",
        "OneEighthInch" => "⅛\"",
        "Rugged146AE" => "146AE",
        _ => family
    };

    private static string VariantLabel(string variant) => variant switch
    {
        "Grid" => "grid",
        "RuggedGrid" => "rugged",
        "NoGrid" => "bare",
        _ => variant
    };
}
