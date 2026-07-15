using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// One channel's curve on the DSP-chain plot: its chain (drawn without the bulk
/// delay, so the filters' own shape stays readable), its sample rate and its
/// plot color. The panel hands these ready to draw; the presenter owns the
/// OxyPlot mechanics.
/// </summary>
internal readonly record struct DspChainCurve(
    string Title,
    DspChannelChain Chain,
    int SampleRate,
    OxyColor Color);

/// <summary>
/// The Virtual DSP lower plot: each enabled channel's DSP-chain response
/// (magnitude / phase / group delay). The bulk delay is excluded — its timing
/// effect shows on the acoustic plot, and drawing it here would wrap the phase
/// into an unreadable sawtooth and swamp the filter group delay. Owns the
/// PlotView's model, the single value axis it reconfigures per mode, and the
/// drawn series; the panel supplies the mode and the per-channel curves.
/// </summary>
internal sealed class VirtualCrossoverDspChainPlot
{
    private const string SeriesTag = "virtual-crossover:curve";
    private const string TrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.00}";
    private const string ValueAxisKey = "dsp-value";

    private readonly PlotView view;

    // Tracks the mode the value axis range was last set for, so switching modes
    // resets the range to the new mode's default while an in-mode redraw (e.g.
    // editing a filter) preserves the user's zoom/pan.
    private DspPlotMode? valueAxisMode;

    public VirtualCrossoverDspChainPlot(PlotView view, DspPlotMode initialMode)
    {
        ArgumentNullException.ThrowIfNull(view);
        this.view = view;

        var model = new PlotModel();
        PlotModelStyle.AddFrequencyAxis(model);
        // One value axis, reconfigured per plot mode (magnitude / phase / group
        // delay). A single axis keeps each mode readable on its own scale.
        model.Axes.Add(new LinearAxis
        {
            Key = ValueAxisKey,
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        });
        model.Annotations.Add(new PlotWatermarkAnnotation
        {
            Text = "DSP chains",
            TextColor = OxyColor.FromAColor(10, OxyColors.White),
            FontSize = 40,
            FontWeight = FontWeights.Bold
        });

        ConfigureValueAxis((LinearAxis)model.Axes[^1], initialMode);
        view.Model = model;
        PlotInteraction.EnableDoubleClickAxisReset(view);
    }

    public void Draw(DspPlotMode mode, IReadOnlyList<DspChainCurve> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);
        if (view.Model is not { } model)
        {
            return;
        }

        RemoveSeries(model);
        if (model.Axes.FirstOrDefault(axis => axis.Key == ValueAxisKey) is LinearAxis valueAxis)
        {
            ConfigureValueAxis(valueAxis, mode);
        }

        IReadOnlyList<double> grid = EqualizationCurve.LogFrequencyGrid(20, 20_000, 512);
        foreach (DspChainCurve curve in curves)
        {
            PreparedDspResponse response = PreparedDspResponse.Create(curve.Chain, curve.SampleRate);
            var points = new List<DataPoint>(grid.Count);
            foreach (double frequency in grid)
            {
                points.Add(new DataPoint(frequency, Value(response, frequency, mode)));
            }

            AddSeries(model, curve.Title, points, curve.Color);
        }

        model.InvalidatePlot(true);
    }

    private static double Value(
        PreparedDspResponse response,
        double frequency,
        DspPlotMode mode) => mode switch
        {
            DspPlotMode.Phase => response.Response(frequency).Phase / Math.PI * 180.0,
            DspPlotMode.GroupDelay => response.GroupDelayMs(frequency),
            _ => DataHelper.AmplitudeToDecibels(response.Response(frequency).Magnitude)
        };

    // Titles the value axis and, only when the mode actually changed, resets its
    // range to that mode's sensible default.
    private void ConfigureValueAxis(LinearAxis axis, DspPlotMode mode)
    {
        axis.Title = mode switch
        {
            DspPlotMode.Phase => "deg",
            DspPlotMode.GroupDelay => "ms",
            _ => "dB"
        };

        if (valueAxisMode == mode)
        {
            return;
        }

        valueAxisMode = mode;
        switch (mode)
        {
            case DspPlotMode.Phase:
                axis.Minimum = -190;
                axis.Maximum = 190;
                axis.MajorStep = 90;
                break;
            case DspPlotMode.GroupDelay:
                // Group delay range varies widely with the filters, so let it
                // auto-scale to the drawn curves.
                axis.Minimum = double.NaN;
                axis.Maximum = double.NaN;
                axis.MajorStep = double.NaN;
                break;
            default:
                axis.Minimum = -60;
                axis.Maximum = 20;
                axis.MajorStep = double.NaN;
                break;
        }

        axis.Reset();
    }

    private static void RemoveSeries(PlotModel model)
    {
        for (int index = model.Series.Count - 1; index >= 0; index--)
        {
            if (Equals(model.Series[index].Tag, SeriesTag))
            {
                model.Series.RemoveAt(index);
            }
        }
    }

    private static void AddSeries(
        PlotModel model,
        string title,
        List<DataPoint> points,
        OxyColor color)
    {
        var series = new LineSeries
        {
            Color = color,
            StrokeThickness = 1.8,
            LineStyle = LineStyle.Solid,
            Title = title,
            Tag = SeriesTag,
            TrackerFormatString = TrackerFormat,
            YAxisKey = ValueAxisKey
        };
        series.Points.AddRange(points);
        model.Series.Add(series);
    }
}
