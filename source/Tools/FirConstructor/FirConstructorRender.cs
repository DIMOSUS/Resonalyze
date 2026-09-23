using System.Numerics;
using OxyPlot;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>What the constructor draws of one kernel: the response against the design's target, the phase with the
/// kernel's delay taken out, and the impulse from its peak.</summary>
internal sealed record FirConstructorRendering(
    FirFilter Kernel,
    DataPoint[] Magnitude,
    DataPoint[] Target,
    DataPoint[] Phase,
    DataPoint[] Impulse,
    DataPoint[] ImpulseDb,
    double DeviationDb);

/// <summary>Builds a rebuild's kernel and draws it; runs off the UI thread and reads nothing but the rebuild.</summary>
internal static class FirConstructorRender
{
    // Phase is hidden this far below the peak: there it is tap rounding flipping between +-180.
    private const double PhaseFloorDb = 60;

    private const double ImpulseFloorDb = 120;

    public static FirConstructorRendering Run(FirConstructorRebuild rebuild, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(rebuild);
        return Draw(rebuild.BareKernel ?? rebuild.Design!.Build(), rebuild.Design, rebuild.RateHz, cancellation);
    }

    public static FirConstructorRendering Draw(
        FirFilter shown,
        FirCrossoverDesign? designed,
        int rate,
        CancellationToken cancellation)
    {
        double highHz = Math.Min(20_000, rate / 2.0);
        const int Points = 800;
        // Symmetric kernel: exact centre (N-1)/2 (half-sample off grid for even N); otherwise the peak.
        double referenceSamples = shown.IsSymmetric ? shown.LinearPhaseDelaySamples : shown.PeakIndex;
        var magnitude = new DataPoint[Points + 1];
        var target = new List<DataPoint>(designed is { HasTargetMagnitude: true } ? Points + 1 : 0);
        var phase = new DataPoint[Points + 1];
        double loudestDb = double.NegativeInfinity;
        for (int i = 0; i <= Points; i++)
        {
            if (i % 50 == 0)
            {
                cancellation.ThrowIfCancellationRequested();
            }

            double frequency = 20 * Math.Pow(highHz / 20, (double)i / Points);
            Complex response = shown.Response(frequency, rate);
            magnitude[i] = new DataPoint(frequency, 20 * Math.Log10(Math.Max(response.Magnitude, 1e-10)));
            loudestDb = Math.Max(loudestDb, magnitude[i].Y);
            if (designed is { HasTargetMagnitude: true })
            {
                double targetDb = 20 * Math.Log10(Math.Max(designed.TargetMagnitude(frequency), 1e-10));
                target.Add(new DataPoint(frequency, targetDb));
            }

            // Removing the linear-phase delay reads 0 deg in the passband instead of thousands of wraps.
            Complex aligned = response *
                Complex.FromPolarCoordinates(1, Math.Tau * frequency * referenceSamples / rate);
            phase[i] = new DataPoint(
                frequency,
                response.Magnitude > 1e-9 ? aligned.Phase * 180 / Math.PI : double.NaN);
        }

        for (int i = 0; i <= Points; i++)
        {
            if (magnitude[i].Y < loudestDb - PhaseFloorDb)
            {
                phase[i] = new DataPoint(phase[i].X, double.NaN);
            }
        }

        // Negative time is the pre-ringing a linear-phase kernel costs.
        ReadOnlySpan<double> taps = shown.Taps;
        double largest = Math.Max(Math.Abs(taps[shown.PeakIndex]), double.Epsilon);
        var impulse = new DataPoint[taps.Length];
        var impulseDb = new DataPoint[taps.Length];
        for (int i = 0; i < taps.Length; i++)
        {
            double timeMs = (i - shown.PeakIndex) * 1_000.0 / rate;
            impulse[i] = new DataPoint(timeMs, taps[i]);
            impulseDb[i] = new DataPoint(
                timeMs,
                Math.Max(20 * Math.Log10(Math.Abs(taps[i]) / largest), -ImpulseFloorDb));
        }

        cancellation.ThrowIfCancellationRequested();
        double deviation = designed?.WorstDeviationDb(shown) ?? double.NaN;
        return new FirConstructorRendering(shown, magnitude, target.ToArray(), phase, impulse, impulseDb, deviation);
    }
}
