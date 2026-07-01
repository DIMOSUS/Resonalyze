using System.Text;

namespace Resonalyze.Dsp;

/// <summary>
/// The one-line "GraphicEQ:" format used by Wavelet and JamesDSP (export only). The
/// parametric EQ is sampled to frequency/gain points, so it cannot be re-imported as
/// discrete bands.
/// </summary>
public sealed class GraphicEqFormat : IEqProfileFormat
{
    private const int PointCount = 64;

    public string Name => "GraphicEQ (Wavelet / JamesDSP)";
    public string Extension => "txt";
    public bool CanImport => false;
    public bool CanExport => true;

    public string Export(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);

        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, PointCount);
        var builder = new StringBuilder("GraphicEQ: ");
        for (int i = 0; i < grid.Count; i++)
        {
            if (i > 0)
            {
                builder.Append("; ");
            }

            double frequency = grid[i];
            builder
                .Append(EqTextNumbers.Format(frequency, "0"))
                .Append(' ')
                .Append(EqTextNumbers.Format(curve.MagnitudeDbAt(frequency), "0.0"));
        }

        return builder.ToString();
    }

    public EqualizationCurve Import(string text) =>
        throw new NotSupportedException("GraphicEQ files cannot be imported as bands.");
}
