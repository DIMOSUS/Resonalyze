using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A measurement's coherence read on a curve's own points.</summary>
internal static class CoherenceCurves
{
    // γ² on a linear grid (bin k -> k·rate/(2·(len−1))) averaged per 1/3-octave point to match the wizard's magnitude curve.
    public static IReadOnlyList<double> PerPoint(
        double[] coherence,
        IReadOnlyList<SignalPoint> points,
        int sampleRate)
    {
        int fftLength = 2 * (coherence.Length - 1);
        double lowFactor = Math.Pow(2.0, -1.0 / 6.0);
        double highFactor = Math.Pow(2.0, 1.0 / 6.0);
        var values = new double[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            double frequency = points[i].X;
            int lo = Math.Max(0, (int)Math.Floor(frequency * lowFactor * fftLength / sampleRate));
            int hi = Math.Min(
                coherence.Length - 1,
                (int)Math.Ceiling(frequency * highFactor * fftLength / sampleRate));
            double sum = 0;
            int count = 0;
            for (int bin = lo; bin <= hi; bin++)
            {
                sum += coherence[bin];
                count++;
            }

            values[i] = count > 0 ? sum / count : 1.0;
        }

        return values;
    }
}
