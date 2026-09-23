using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>τ read off the open measurement's transfer IR through a snapshot of the Phase panel's gate and window: the
/// Auto value it shows, and the two estimates its buttons offer.</summary>
internal sealed class PhaseDetrendEstimate
{
    private (MeasurementResult Result, PhaseAnalysisSettings Settings, double? Value)? last;

    /// <summary>The slope-based excess delay, null without a transfer IR or a finite reading; memoized per snapshot.
    /// False while the document is busy: nothing is read, and the panel keeps what it shows.</summary>
    public bool TryResolveAuto(AnalyzerDocument document, PhaseAnalysisSettings settings, out double? autoMs)
    {
        ArgumentNullException.ThrowIfNull(document);
        autoMs = null;
        if (document.IsBusy)
        {
            return false;
        }

        if (document.Result is not { HasTransfer: true } result)
        {
            return true;
        }

        if (last is { } memo && ReferenceEquals(memo.Result, result) && memo.Settings == settings)
        {
            autoMs = memo.Value;
            return true;
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
        autoMs = value;
        return true;
    }

    /// <summary>Slope flattens the average excess-phase trend; peak references the dominant arrival. Null, as the
    /// Auto reading is, while the document is busy, and without a transfer IR.</summary>
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
