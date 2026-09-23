using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>τ read off the open measurement's transfer IR through a snapshot of the Phase panel's gate and window: the
/// Auto value it shows, and the two estimates its buttons offer.</summary>
internal sealed class PhaseDetrendEstimate
{
    private (MeasurementResult Result, PhaseAnalysisSettings Settings, double? Value)? last;

    /// <summary>The slope-based excess delay; null without a transfer IR or a finite reading. Memoized per snapshot.</summary>
    public double? ResolveAuto(AnalyzerDocument document, PhaseAnalysisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Result is not { HasTransfer: true } result)
        {
            return null;
        }

        if (last is { } memo && ReferenceEquals(memo.Result, result) && memo.Settings == settings)
        {
            return memo.Value;
        }

        double? value;
        try
        {
            double resolved = DataHelper.ResolvePhaseDetrendMilliseconds(
                new MeasurementPlotContext(document).CreatePrimaryMeasurement(),
                settings);
            value = double.IsFinite(resolved) ? resolved : null;
        }
        catch (InvalidOperationException)
        {
            value = null;
        }

        last = (result, settings, value);
        return value;
    }

    /// <summary>Slope flattens the average excess-phase trend; peak references the dominant arrival. Null while the
    /// document is busy or has no transfer IR.</summary>
    public static (double SlopeMs, double PeakMs)? Estimate(AnalyzerDocument document, PhaseAnalysisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Result?.HasTransfer != true || document.IsBusy)
        {
            return null;
        }

        try
        {
            return DataHelper.EstimatePhaseDetrend(
                new MeasurementPlotContext(document).CreatePrimaryMeasurement(),
                settings);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
