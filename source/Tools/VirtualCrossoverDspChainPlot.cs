using OxyPlot;
using OxyPlot.Annotations;
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
/// The junction-correlation view's data, computed off the UI thread from two
/// adjacent PROCESSED channels (current delays, polarity, filters applied, so
/// lag 0 is "as currently aligned" and every lag reads as a correction to the
/// upper channel). Correlation and its GCC-PHAT twin show WHERE the comb
/// lobes sit (negative lobes: the same alignment with the upper channel
/// inverted); the two score curves show what the alignment engine THINKS of
/// every lag — the dip-penalized junction loss, honestly re-gated per point,
/// for both polarities. <see cref="ArrivalLagMs"/> marks the band-limited
/// envelope arrival difference — the physics-first estimate the searches
/// anchor on.
/// </summary>
internal sealed record JunctionCorrelationView(
    string PairTitle,
    string UpperName,
    double CrossoverHz,
    double BandLowHz,
    double BandHighHz,
    List<SignalPoint> Correlation,
    List<SignalPoint> Whitened,
    List<SignalPoint> ScoreNormal,
    List<SignalPoint> ScoreInverted,
    double ArrivalLagMs);

/// <summary>
/// The Virtual DSP lower plot: each enabled channel's DSP-chain response
/// (magnitude / phase / group delay). The bulk delay is excluded — its timing
/// effect shows on the acoustic plot, and drawing it here would wrap the phase
/// into an unreadable sawtooth and swamp the filter group delay. Owns the
/// PlotView's model, the single value axis it reconfigures per mode, and the
/// drawn series; the panel supplies the mode and the per-channel curves.
/// The junction-correlation mode swaps in its own, lag-domain model (see
/// <see cref="DrawCorrelation"/>); the two models keep their axes' zoom/pan
/// independently.
/// </summary>
internal sealed class VirtualCrossoverDspChainPlot
{
    private const string SeriesTag = "virtual-crossover:curve";
    private const string TrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.00}";
    private const string ValueAxisKey = "dsp-value";
    private const string LagAxisKey = "corr-lag";
    private const string CoefficientAxisKey = "corr-r";
    private const string ScoreAxisKey = "corr-score";
    private const string CorrelationTrackerFormat = "{0}\n{2:0.00} ms\n{4:0.00}";

    private readonly PlotView view;
    private readonly PlotModel chainModel;
    private readonly PlotModel correlationModel;

    // Tracks the mode the value axis range was last set for, so switching modes
    // resets the range to the new mode's default while an in-mode redraw (e.g.
    // editing a filter) preserves the user's zoom/pan.
    private DspPlotMode? valueAxisMode;

    // The pair and window the correlation axes were last configured for: a
    // junction switch resets the ranges, an in-pair redraw (delay edits)
    // preserves the user's zoom/pan.
    private (string Pair, double WindowMs)? correlationAxisState;

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
        chainModel = model;
        correlationModel = CreateCorrelationModel();
        view.Model = initialMode == DspPlotMode.Correlation
            ? correlationModel
            : chainModel;
        PlotInteraction.EnableDoubleClickAxisReset(view);
    }

    private static PlotModel CreateCorrelationModel()
    {
        var model = new PlotModel
        {
            IsLegendVisible = true
        };
        model.Legends.Add(new OxyPlot.Legends.Legend
        {
            LegendPosition = OxyPlot.Legends.LegendPosition.TopRight,
            LegendTextColor = OxyColor.FromRgb(210, 214, 222),
            LegendBackground = OxyColor.FromAColor(120, OxyColor.FromRgb(40, 44, 54))
        });
        model.Axes.Add(new LinearAxis
        {
            Key = LagAxisKey,
            Position = AxisPosition.Bottom,
            Title = "delay added to the upper channel (ms)",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        });
        model.Axes.Add(new LinearAxis
        {
            Key = CoefficientAxisKey,
            Position = AxisPosition.Left,
            Title = "r",
            Minimum = -1.05,
            Maximum = 1.05,
            MajorStep = 0.5,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        });
        model.Axes.Add(new LinearAxis
        {
            Key = ScoreAxisKey,
            Position = AxisPosition.Right,
            Title = "junction score (dB)",
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None
        });
        model.Annotations.Add(new PlotWatermarkAnnotation
        {
            Text = "Junction",
            TextColor = OxyColor.FromAColor(10, OxyColors.White),
            FontSize = 40,
            FontWeight = FontWeights.Bold
        });
        return model;
    }

    /// <summary>
    /// Swaps in the lag-domain model and draws one junction's correlation and
    /// score curves. Null clears the view (no measurable pair): the empty
    /// model with its watermark stays on screen.
    /// </summary>
    public void DrawCorrelation(JunctionCorrelationView? data)
    {
        view.Model = correlationModel;
        PlotModel model = correlationModel;
        RemoveSeries(model);
        for (int index = model.Annotations.Count - 1; index >= 0; index--)
        {
            if (Equals(model.Annotations[index].Tag, SeriesTag))
            {
                model.Annotations.RemoveAt(index);
            }
        }

        if (data == null)
        {
            model.Title = null;
            model.InvalidatePlot(true);
            return;
        }

        model.Title = $"{data.PairTitle}  ·  fc {data.CrossoverHz:0} Hz  ·  " +
            $"{data.BandLowHz:0}-{data.BandHighHz:0} Hz";
        model.TitleFontSize = 11;
        model.TitleColor = OxyColor.FromRgb(210, 214, 222);

        // A junction switch (or a window change from new crossover settings)
        // resets the axes; an in-pair redraw keeps the zoom.
        double windowMs = data.Correlation.Count > 0
            ? Math.Abs(data.Correlation[^1].X)
            : 3.0;
        if (correlationAxisState != (data.PairTitle, windowMs))
        {
            correlationAxisState = (data.PairTitle, windowMs);
            foreach (Axis axis in model.Axes)
            {
                if (axis.Key == LagAxisKey)
                {
                    axis.Minimum = -windowMs;
                    axis.Maximum = windowMs;
                }

                axis.Reset();
            }
        }

        AddCorrelationSeries(
            model, "corr", data.Correlation,
            OxyColor.FromRgb(130, 138, 152), CoefficientAxisKey,
            LineStyle.Solid, 1.2);
        AddCorrelationSeries(
            model, "PHAT", data.Whitened,
            OxyColor.FromRgb(79, 195, 247), CoefficientAxisKey,
            LineStyle.Solid, 1.8);
        AddCorrelationSeries(
            model, "score", data.ScoreNormal,
            OxyColor.FromRgb(124, 213, 124), ScoreAxisKey,
            LineStyle.Solid, 1.8);
        AddCorrelationSeries(
            model, "score inv", data.ScoreInverted,
            OxyColor.FromRgb(255, 169, 79), ScoreAxisKey,
            LineStyle.Dash, 1.8);

        // The "you are here" line: the channels enter PROCESSED, so lag 0 is
        // the currently applied alignment. The arrival marker is the envelope
        // estimate the searches anchor on — the gap between the two lines is
        // exactly what the sum searches bought (or paid) versus the physics.
        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Vertical,
            X = 0,
            Color = OxyColor.FromAColor(160, OxyColors.White),
            LineStyle = LineStyle.Solid,
            StrokeThickness = 1,
            Text = "current",
            TextColor = OxyColor.FromAColor(200, OxyColors.White),
            Tag = SeriesTag
        });
        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Vertical,
            X = data.ArrivalLagMs,
            Color = OxyColor.FromAColor(170, OxyColor.FromRgb(240, 200, 90)),
            LineStyle = LineStyle.Dash,
            StrokeThickness = 1,
            Text = "arrival",
            TextColor = OxyColor.FromAColor(220, OxyColor.FromRgb(240, 200, 90)),
            Tag = SeriesTag
        });

        model.InvalidatePlot(true);
    }

    private static void AddCorrelationSeries(
        PlotModel model,
        string title,
        List<SignalPoint> points,
        OxyColor color,
        string axisKey,
        LineStyle style,
        double thickness)
    {
        var series = new LineSeries
        {
            Color = color,
            StrokeThickness = thickness,
            LineStyle = style,
            Title = title,
            Tag = SeriesTag,
            TrackerFormatString = CorrelationTrackerFormat,
            XAxisKey = LagAxisKey,
            YAxisKey = axisKey
        };
        foreach (SignalPoint point in points)
        {
            series.Points.Add(new DataPoint(point.X, point.Y));
        }

        model.Series.Add(series);
    }

    public void Draw(DspPlotMode mode, IReadOnlyList<DspChainCurve> curves)
    {
        ArgumentNullException.ThrowIfNull(curves);
        view.Model = chainModel;
        PlotModel model = chainModel;

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
