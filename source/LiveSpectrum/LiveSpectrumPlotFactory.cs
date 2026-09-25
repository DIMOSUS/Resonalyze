using OxyPlot;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The live plot's frame and its series; the points come from <see cref="LiveSpectrumCurves"/>.</summary>
/// <remarks>Series are refilled in place at ~30 fps rather than rebuilt; see docs/tech/live-spectrum.md#redraw-loop.</remarks>
internal sealed class LiveSpectrumPlotFactory
{
    private readonly LiveSpectrumCurves curves;

    public LiveSpectrumPlotFactory(LiveSpectrumCurves curves)
    {
        this.curves = curves;
    }

    /// <param name="scaleOverride">Axis for a STORED capture, whose levels follow the anchor at capture time. Null follows the live state.</param>
    public static PlotModel CreateModel(LiveSpectrumDisplay display, MagnitudeScale? scaleOverride = null)
    {
        // An active tilt compensation is named in the title: the level is reshaped by the excitation spectrum.
        bool renderSpl =
            (scaleOverride ?? display.Scale) == MagnitudeScale.SoundPressureLevel;
        bool rtaOnly = display.RtaOnly;
        bool mmm = display.Mode.IsSpatialAverageCapture();
        string tiltSuffix = display.TiltModel != null ? " (noise-compensated)" : "";
        // An unanchored MMM capture is a valid spatial average but must not pass for absolute.
        PlotModel model = PlotModelStyle.CreateTitledModel(
            mmm
                ? (renderSpl
                    ? "Live Spectrum — MMM, dB SPL"
                    : "Live Spectrum — MMM, relative (no SPL anchor)") + tiltSuffix
                : renderSpl
                    ? "Live Spectrum — dB SPL" + tiltSuffix
                    : rtaOnly
                        ? "Live Spectrum (RTA)" + tiltSuffix
                        : "Live Transfer Function");

        PlotModelStyle.AddFrequencyAxis(model);
        if (renderSpl)
        {
            PlotModelStyle.AddDecibelAxis(
                model,
                "dB SPL",
                PlotModelStyle.SplDecibelMinimum,
                PlotModelStyle.SplDecibelMaximum,
                PlotModelStyle.SplDecibelAbsoluteMinimum,
                PlotModelStyle.SplDecibelAbsoluteMaximum);
        }
        else
        {
            PlotModelStyle.AddDecibelAxis(model);
            if (!rtaOnly && display.Options.ShowCoherence)
            {
                PlotModelFactory.AddCoherenceAxis(model);
            }
        }

        return model;
    }

    /// <summary>The live transfer function on the display grid, for <see cref="UpdateTransferSeries"/>, the coherence split
    /// and peak hold.</summary>
    public List<SignalPoint> TransferPoints(LiveSpectrumDisplay display, double[] magnitude) =>
        curves.Transfer(display, magnitude);

    /// <summary>The RTA on the display grid, for <see cref="UpdateInputMagnitudeSeries"/> and peak hold.</summary>
    public List<SignalPoint> RtaPoints(LiveSpectrumDisplay display, double[] inputMagnitude) =>
        curves.Rta(display, inputMagnitude);

    public static LineSeries BuildTransferSeries(List<SignalPoint> transferPoints)
    {
        var series = new LineSeries
        {
            Color = UiPalette.CurveLiveTransfer.ToOxy(),
            Title = "Live Transfer Function",
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        UpdateTransferSeries(series, transferPoints);
        return series;
    }

    // Refill in place at ~30 fps to avoid re-allocating plot objects.
    public static void UpdateTransferSeries(LineSeries series, List<SignalPoint> transferPoints) =>
        PlotModelFactory.FillPoints(series, transferPoints);

    /// <summary>A stored capture drawn as captured, not re-rendered from its bins: viewing must show what the author saw.</summary>
    public static LineSeries BuildLoadedCaptureSeries(LiveCaptureDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var series = new LineSeries
        {
            Color = UiPalette.CurveLiveInput.ToOxy(),
            Title = string.IsNullOrWhiteSpace(document.Title)
                ? "Loaded capture"
                : document.Title,
            // The document's unit: an unanchored capture is relative regardless of this machine's calibration.
            TrackerFormatString =
                document.Recipe.MagnitudeScale == MagnitudeScale.SoundPressureLevel
                    ? "{0}\n{2:0.0} Hz\n{4:0.00} dB SPL"
                    : "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };

        PlotModelFactory.FillPoints(series, document.ToCurvePoints());
        return series;
    }

    public static LineSeries BuildInputMagnitudeSeries(LiveSpectrumDisplay display, List<SignalPoint> rtaPoints)
    {
        var series = new LineSeries
        {
            Color = UiPalette.CurveLiveInput.ToOxy(),
            Title = "Input Spectrum (RTA)"
        };
        UpdateInputMagnitudeSeries(display, series, rtaPoints);
        return series;
    }

    // The RTA is the one live curve with an honest absolute level: in SPL it is band-power integrated (FFT-size independent) and offset.
    public static void UpdateInputMagnitudeSeries(LiveSpectrumDisplay display, LineSeries series, List<SignalPoint> rtaPoints)
    {
        series.TrackerFormatString = MagnitudeTracker(display);
        PlotModelFactory.FillPoints(series, rtaPoints);
    }

    public static LineSeries BuildPeakHoldSeries(LiveSpectrumDisplay display, List<SignalPoint> peakHoldPoints)
    {
        var series = new LineSeries
        {
            Color = OxyColor.FromAColor(170, UiPalette.CurvePeakHold.ToOxy()),
            LineStyle = LineStyle.Solid,
            StrokeThickness = 1.0,
            Title = "Peak Hold"
        };
        UpdatePeakHoldSeries(display, series, peakHoldPoints);
        return series;
    }

    public static void UpdatePeakHoldSeries(
        LiveSpectrumDisplay display,
        LineSeries series,
        List<SignalPoint> peakHoldPoints)
    {
        series.TrackerFormatString = MagnitudeTracker(display);
        PlotModelFactory.FillPoints(series, peakHoldPoints);
    }

    public static LineSeries BuildCoherenceSeries(LiveSpectrumDisplay display, double[] coherence) =>
        PlotModelFactory.BuildCoherenceSeries(
            coherence,
            display.Setup.SampleRate,
            display.Setup.SequenceLength,
            display.Options.SmoothingInverseOctaves);

    public void UpdateCoherenceSeries(LiveSpectrumDisplay display, LineSeries series, double[] coherence) =>
        PlotModelFactory.FillPoints(series, curves.Coherence(display, coherence));

    /// <summary>Trusted and low-coherence (dimmed, dashed) segments sharing boundary points.</summary>
    public (LineSeries Trusted, LineSeries Untrusted) BuildCoherenceSplitSeries(
        LiveSpectrumDisplay display,
        List<SignalPoint> transferPoints,
        double[] coherence,
        int thresholdPercent)
    {
        var trusted = new LineSeries
        {
            Color = UiPalette.CurveLiveTransfer.ToOxy(),
            Title = "Live Transfer Function",
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        var untrusted = new LineSeries
        {
            Color = OxyColor.FromAColor(140, UiPalette.CurveMuted.ToOxy()),
            LineStyle = LineStyle.Dash,
            StrokeThickness = 1.0,
            Title = "Low coherence",
            TrackerFormatString = "{0}\n{2:0.0} Hz\n{4:0.00} dB"
        };
        UpdateCoherenceSplitSeries(display, trusted, untrusted, transferPoints, coherence, thresholdPercent);
        return (trusted, untrusted);
    }

    public void UpdateCoherenceSplitSeries(
        LiveSpectrumDisplay display,
        LineSeries trusted,
        LineSeries untrusted,
        List<SignalPoint> transferPoints,
        double[] coherence,
        int thresholdPercent)
    {
        (List<SignalPoint> trustedPoints, List<SignalPoint> untrustedPoints) =
            curves.CoherenceSplit(display, transferPoints, coherence, thresholdPercent);
        PlotModelFactory.FillPoints(trusted, trustedPoints);
        PlotModelFactory.FillPoints(untrusted, untrustedPoints);
    }

    private static string MagnitudeTracker(LiveSpectrumDisplay display) =>
        display.RendersSpl
            ? "{0}\n{2:0.0} Hz\n{4:0.00} dB SPL"
            : "{0}\n{2:0.0} Hz\n{4:0.00} dB";
}
