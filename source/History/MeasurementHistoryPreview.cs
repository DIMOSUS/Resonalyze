using Resonalyze.Dsp;

namespace Resonalyze.History;

internal sealed class MeasurementHistoryPreview
{
    public int Window { get; set; } = 4096;
    public int LeftTukeyWindow { get; set; } = 256;
    public int RightTukeyWindow { get; set; } = 256;
    public int SmoothingInverseOctaves { get; set; } = 2;
    public double[] Frequencies { get; set; } = Array.Empty<double>();
    public double[] MagnitudesDb { get; set; } = Array.Empty<double>();

    public IReadOnlyList<SignalPoint> ToSignalPoints()
    {
        int count = Math.Min(Frequencies.Length, MagnitudesDb.Length);
        var result = new List<SignalPoint>(count);
        for (int i = 0; i < count; i++)
        {
            if (!double.IsFinite(Frequencies[i]) ||
                !double.IsFinite(MagnitudesDb[i]) ||
                Frequencies[i] <= 0)
            {
                continue;
            }

            result.Add(new SignalPoint(Frequencies[i], MagnitudesDb[i]));
        }

        return result;
    }
}
