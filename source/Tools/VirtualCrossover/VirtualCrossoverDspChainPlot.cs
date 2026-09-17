using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A channel's chain curve, drawn without bulk delay at the PROCESSOR's rate (this plot shows the device's filters).</summary>
internal readonly record struct DspChainCurve(
    string Title,
    DspChannelChain Chain,
    int ProcessorSampleRate,
    OxyColor Color);

/// <summary>Junction-correlation data from two PROCESSED channels: lag 0 is the applied alignment, lags correct the upper channel.
/// See docs/tech/virtual-dsp-analysis.md#plots.</summary>
internal sealed record JunctionCorrelationView(
    string PairTitle,
    string UpperName,
    double CrossoverHz,
    double BandLowHz,
    double BandHighHz,
    List<SignalPoint> Whitened,
    List<SignalPoint> WhitenedDirect,
    List<SignalPoint> ScoreNormal,
    List<SignalPoint> ScoreInverted,
    // Band-limited envelope fronts, lower minus upper: exported to the agent package, not drawn.
    double ArrivalLagMs);

/// <summary>One junction's arrival-coherence ladder from the same processed pair; lag 0 is the applied alignment.</summary>
internal sealed record JunctionCoherenceView(
    string PairTitle,
    string UpperName,
    double CrossoverHz,
    double BandLowHz,
    double BandHighHz,
    List<VirtualCrossoverAnalysis.ArrivalCoherencePoint> Ladder);

/// <summary>Virtual DSP lower plot: chain magnitude/phase/group delay without the bulk delay (it would wrap phase and swamp GD),
/// plus the junction correlation and coherence models.</summary>
internal sealed class VirtualCrossoverDspChainPlot
{
    private const string SeriesTag = "virtual-crossover:curve";
    private const string TrackerFormat = "{0}\n{2:0.0} Hz\n{4:0.00}";
    private const string ValueAxisKey = "dsp-value";
    private const string LagAxisKey = "corr-lag";
    private const string CoefficientAxisKey = "corr-r";
    private const string ScoreAxisKey = "corr-score";
    private const string CorrelationTrackerFormat = "{0}\n{2:0.00} ms\n{4:0.00}";
    private const string CoherenceLagAxisKey = "coh-lag";
    private const string CoherenceCoefficientAxisKey = "coh-r";
    private const string CoherenceTrackerFormat = "{0}\n{2:0} Hz\n{4:0.00}";

    private readonly PlotView view;
    private readonly PlotModel chainModel;
    private readonly PlotModel correlationModel;
    private readonly PlotModel coherenceModel;

    // Mode switches reset the range; in-mode redraws keep the user's zoom.
    private DspPlotMode? valueAxisMode;

    // A junction switch resets the ranges; in-pair redraws keep the zoom.
    private (string Pair, double WindowMs)? correlationAxisState;

    // Lag limit bucketed to 0.5 ms; the band is included because a crossover edit keeps the title and often the bucket
    // while the fitted frequency axis must follow.
    private (string Pair, double LagLimitMs, double BandLowHz, double BandHighHz)?
        coherenceAxisState;

    public VirtualCrossoverDspChainPlot(PlotView view, DspPlotMode initialMode)
    {
        ArgumentNullException.ThrowIfNull(view);
        this.view = view;

        var model = new PlotModel();
        PlotModelStyle.ApplyChrome(model);
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddAxis(model, new LinearAxis
        {
            Key = ValueAxisKey,
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        });
        model.Annotations.Add(new PlotWatermarkAnnotation
        {
            Text = "DSP chains",
            TextColor = OxyColor.FromAColor(10, UiPalette.GraphAxisText.ToOxy()),
            FontSize = 40,
            FontWeight = FontWeights.Bold
        });

        ConfigureValueAxis((LinearAxis)model.Axes[^1], initialMode);
        chainModel = model;
        correlationModel = CreateCorrelationModel();
        coherenceModel = CreateCoherenceModel();
        view.Model = initialMode switch
        {
            DspPlotMode.Correlation => correlationModel,
            DspPlotMode.Coherence => coherenceModel,
            _ => chainModel
        };
        PlotInteraction.Enable(view);
    }

    private static PlotModel CreateCorrelationModel()
    {
        var model = new PlotModel
        {
            IsLegendVisible = true
        };
        PlotModelStyle.ApplyChrome(model);
        model.Legends.Add(new OxyPlot.Legends.Legend
        {
            LegendPosition = OxyPlot.Legends.LegendPosition.TopRight,
            LegendTextColor = UiPalette.TextDefault.ToOxy(),
            LegendBackground = UiPalette.PlotLegendBackground.ToOxy()
        });
        PlotModelStyle.AddAxis(model, new LinearAxis
        {
            Key = LagAxisKey,
            Position = AxisPosition.Bottom,
            Title = "delay added to the upper channel (ms)",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        });
        PlotModelStyle.AddAxis(model, new LinearAxis
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
        PlotModelStyle.AddAxis(model, new LinearAxis
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
            TextColor = OxyColor.FromAColor(10, UiPalette.GraphAxisText.ToOxy()),
            FontSize = 40,
            FontWeight = FontWeights.Bold
        });
        return model;
    }

    /// <summary>Draws one junction's correlation and score curves; null leaves the empty model with its watermark.</summary>
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
        model.TitleColor = UiPalette.TextDefault.ToOxy();

        double windowMs = data.Whitened.Count > 0
            ? Math.Abs(data.Whitened[^1].X)
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

        // Envelope guides: the packet centre a lobe-skip is read against; the carrier answers which lobe.
        AddEnvelopeGuides(
            model, "PHAT envelope", data.Whitened,
            UiPalette.CurveChainA.ToOxy());
        AddEnvelopeGuides(
            model, "PHAT direct envelope", data.WhitenedDirect,
            UiPalette.CurveChainB.ToOxy());

        // PHAT: full-record comb; PHAT direct: driver wavefronts (polarity witness); scores: the searched surface.
        // No raw amplitude-weighted correlation: it follows whatever the cabin plays loudest.
        AddCorrelationSeries(
            model, "PHAT", data.Whitened,
            UiPalette.CurveChainA.ToOxy(), CoefficientAxisKey,
            LineStyle.Solid, 1.8);
        AddCorrelationSeries(
            model, "PHAT direct", data.WhitenedDirect,
            UiPalette.CurveChainB.ToOxy(), CoefficientAxisKey,
            LineStyle.Solid, 1.4);
        AddCorrelationSeries(
            model, "score", data.ScoreNormal,
            UiPalette.CurveChainC.ToOxy(), ScoreAxisKey,
            LineStyle.Solid, 1.8);
        AddCorrelationSeries(
            model, "score inv", data.ScoreInverted,
            UiPalette.CurveChainD.ToOxy(), ScoreAxisKey,
            LineStyle.Dash, 1.8);

        // Lag 0 is the applied alignment.
        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Vertical,
            X = 0,
            Color = OxyColor.FromAColor(160, UiPalette.CurveNeutral.ToOxy()),
            LineStyle = LineStyle.Solid,
            StrokeThickness = 1,
            Text = "current",
            TextColor = OxyColor.FromAColor(200, UiPalette.CurveNeutral.ToOxy()),
            Tag = SeriesTag
        });

        model.InvalidatePlot(true);
    }

    // Computed over the displayed window; edge wobble stays in the decayed outer samples.
    private static void AddEnvelopeGuides(
        PlotModel model,
        string title,
        List<SignalPoint> points,
        OxyColor color)
    {
        if (points.Count < 3)
        {
            return;
        }

        double[] envelope = SignalEnvelope.Envelope(
            points.Select(point => point.Y).ToList());
        foreach (int sign in new[] { 1, -1 })
        {
            var series = new LineSeries
            {
                Title = title,
                RenderInLegend = false,
                Color = OxyColor.FromAColor(90, color),
                StrokeThickness = 1.0,
                Tag = SeriesTag,
                TrackerFormatString = CorrelationTrackerFormat,
                XAxisKey = LagAxisKey,
                YAxisKey = CoefficientAxisKey
            };
            for (int i = 0; i < points.Count; i++)
            {
                series.Points.Add(new DataPoint(points[i].X, sign * envelope[i]));
            }

            model.Series.Add(series);
        }
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

    private static PlotModel CreateCoherenceModel()
    {
        var model = new PlotModel
        {
            IsLegendVisible = true
        };
        PlotModelStyle.ApplyChrome(model);
        model.Legends.Add(new OxyPlot.Legends.Legend
        {
            LegendPosition = OxyPlot.Legends.LegendPosition.TopRight,
            LegendTextColor = UiPalette.TextDefault.ToOxy(),
            LegendBackground = UiPalette.PlotLegendBackground.ToOxy()
        });
        PlotModelStyle.AddFrequencyAxis(model);
        PlotModelStyle.AddAxis(model, new LinearAxis
        {
            Key = CoherenceLagAxisKey,
            Position = AxisPosition.Left,
            Title = "delay to optimum (ms)",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        });
        PlotModelStyle.AddAxis(model, new LinearAxis
        {
            Key = CoherenceCoefficientAxisKey,
            Position = AxisPosition.Right,
            Title = "envelope r",
            Minimum = 0,
            Maximum = 1.05,
            MajorStep = 0.25,
            MajorGridlineStyle = LineStyle.None,
            MinorGridlineStyle = LineStyle.None
        });
        model.Annotations.Add(new PlotWatermarkAnnotation
        {
            Text = "Coherence",
            TextColor = OxyColor.FromAColor(10, UiPalette.GraphAxisText.ToOxy()),
            FontSize = 40,
            FontWeight = FontWeights.Bold
        });
        return model;
    }

    /// <summary>Draws one junction's coherence ladder; a ladder emptied by the level gate draws the title alone.</summary>
    public void DrawCoherence(JunctionCoherenceView? data)
    {
        view.Model = coherenceModel;
        PlotModel model = coherenceModel;
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
            model.Subtitle = null;
            model.InvalidatePlot(true);
            return;
        }

        model.Title = $"{data.PairTitle}  ·  fc {data.CrossoverHz:0} Hz  ·  " +
            $"{data.BandLowHz:0}-{data.BandHighHz:0} Hz";
        model.TitleFontSize = 11;
        model.TitleColor = UiPalette.TextDefault.ToOxy();
        model.Subtitle = data.CrossoverHz < 120
            ? "low junction: cabin modes can dominate this read"
            : null;
        model.SubtitleFontSize = 9;
        model.SubtitleColor = OxyColor.FromAColor(170, UiPalette.Warning.ToOxy());

        List<VirtualCrossoverAnalysis.ArrivalCoherencePoint> ladder = data.Ladder;
        double needed = Math.Max(
            1.0,
            1.15 * ladder
                .Select(point => Math.Max(
                    Math.Abs(point.LagMs), point.HalfPeriodMs))
                .DefaultIfEmpty(1.0)
                .Max());
        double lagLimit = Math.Ceiling(needed * 2.0) / 2.0;
        if (coherenceAxisState !=
            (data.PairTitle, lagLimit, data.BandLowHz, data.BandHighHz))
        {
            coherenceAxisState =
                (data.PairTitle, lagLimit, data.BandLowHz, data.BandHighHz);
            foreach (Axis axis in model.Axes)
            {
                if (axis.Key == CoherenceLagAxisKey)
                {
                    axis.Minimum = -lagLimit;
                    axis.Maximum = lagLimit;
                }
                else if (axis.Key == PlotModelFactory.FrequencyAxisKey)
                {
                    axis.Minimum = data.BandLowHz / 1.12;
                    axis.Maximum = data.BandHighHz * 1.12;
                }

                axis.Reset();
            }
        }

        // Past half a period the optimum belongs to the next lobe.
        var corridorUpper = NewCoherenceLine(
            "±T/2 (next lobe)", OxyColor.FromAColor(150, UiPalette.CurveMuted.ToOxy()),
            CoherenceLagAxisKey, LineStyle.Dash, 1.0);
        var corridorLower = NewCoherenceLine(
            null, OxyColor.FromAColor(150, UiPalette.CurveMuted.ToOxy()),
            CoherenceLagAxisKey, LineStyle.Dash, 1.0);
        // Gap between envelope at the optimum and at lag 0: coherence the applied tune leaves unused.
        var gap = new AreaSeries
        {
            Title = "r attainable",
            Tag = SeriesTag,
            Color = OxyColors.Transparent,
            Color2 = OxyColors.Transparent,
            Fill = OxyColor.FromAColor(50, UiPalette.CurveChainC.ToOxy()),
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            YAxisKey = CoherenceCoefficientAxisKey,
            TrackerFormatString = CoherenceTrackerFormat
        };
        var currentR = NewCoherenceLine(
            "r current", UiPalette.CurveChainC.ToOxy(),
            CoherenceCoefficientAxisKey, LineStyle.Dot, 1.8);
        var lagLine = NewCoherenceLine(
            "Δt to optimum", OxyColor.FromAColor(200, UiPalette.CurveNeutral.ToOxy()),
            CoherenceLagAxisKey, LineStyle.Solid, 1.2);
        // No polarity marker: the 2/3-octave probe cannot read polarity (see ArrivalCoherencePoint).
        var optimum = new ScatterSeries
        {
            Title = "optimum",
            Tag = SeriesTag,
            MarkerType = MarkerType.Circle,
            MarkerSize = 3.5,
            MarkerFill = UiPalette.CurveChainA.ToOxy(),
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            YAxisKey = CoherenceLagAxisKey,
            TrackerFormatString = CoherenceTrackerFormat
        };
        foreach (VirtualCrossoverAnalysis.ArrivalCoherencePoint point in ladder)
        {
            corridorUpper.Points.Add(
                new DataPoint(point.FrequencyHz, point.HalfPeriodMs));
            corridorLower.Points.Add(
                new DataPoint(point.FrequencyHz, -point.HalfPeriodMs));
            gap.Points.Add(new DataPoint(point.FrequencyHz, point.PeakR));
            gap.Points2.Add(new DataPoint(point.FrequencyHz, point.CurrentR));
            currentR.Points.Add(
                new DataPoint(point.FrequencyHz, point.CurrentR));
            lagLine.Points.Add(new DataPoint(point.FrequencyHz, point.LagMs));
            optimum.Points.Add(
                new ScatterPoint(point.FrequencyHz, point.LagMs));
        }

        model.Series.Add(gap);
        model.Series.Add(corridorUpper);
        model.Series.Add(corridorLower);
        model.Series.Add(currentR);
        model.Series.Add(lagLine);
        model.Series.Add(optimum);

        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal,
            Y = 0,
            YAxisKey = CoherenceLagAxisKey,
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            Color = OxyColor.FromAColor(160, UiPalette.CurveNeutral.ToOxy()),
            StrokeThickness = 1,
            Tag = SeriesTag
        });
        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Vertical,
            X = data.CrossoverHz,
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            YAxisKey = CoherenceLagAxisKey,
            Color = OxyColor.FromAColor(120, UiPalette.CurveMuted.ToOxy()),
            LineStyle = LineStyle.Dot,
            StrokeThickness = 1,
            Text = "fc",
            TextColor = OxyColor.FromAColor(170, UiPalette.CurveMuted.ToOxy()),
            Tag = SeriesTag
        });

        model.InvalidatePlot(true);
    }

    private static LineSeries NewCoherenceLine(
        string? title,
        OxyColor color,
        string axisKey,
        LineStyle style,
        double thickness) => new()
        {
            Title = title,
            Tag = SeriesTag,
            Color = color,
            LineStyle = style,
            StrokeThickness = thickness,
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            YAxisKey = axisKey,
            TrackerFormatString = CoherenceTrackerFormat
        };

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
            PreparedDspResponse response =
                PreparedDspResponse.Create(curve.Chain, curve.ProcessorSampleRate);
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
