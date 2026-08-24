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
/// upper channel). The whitened correlation shows WHERE the comb lobes sit on
/// the full records (negative lobes: the same alignment with the upper
/// channel inverted); its DIRECT twin reads the same comb on the channels'
/// direct sound alone — the drivers' wavefronts, the engine's polarity
/// witness; the two score curves show what the alignment engine THINKS of
/// every lag — the dip-penalized junction loss, probed by rotating the
/// windowed cuts, for both polarities.
/// <see cref="ArrivalLagMs"/> marks the band-limited
/// envelope arrival difference — the physics-first estimate the searches
/// anchor on.
/// </summary>
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
    double ArrivalLagMs);

/// <summary>
/// The junction-coherence view's data: one junction's arrival-coherence
/// ladder (see <see cref="VirtualCrossoverAnalysis.ArrivalCoherenceLadder"/>),
/// computed off the UI thread from the same PROCESSED pair as the correlation
/// view — lag 0 is the applied alignment, every band's lag a correction to
/// the upper channel.
/// </summary>
internal sealed record JunctionCoherenceView(
    string PairTitle,
    string UpperName,
    double CrossoverHz,
    double BandLowHz,
    double BandHighHz,
    List<VirtualCrossoverAnalysis.ArrivalCoherencePoint> Ladder);

/// <summary>
/// The Virtual DSP lower plot: each enabled channel's DSP-chain response
/// (magnitude / phase / group delay). The bulk delay is excluded — its timing
/// effect shows on the acoustic plot, and drawing it here would wrap the phase
/// into an unreadable sawtooth and swamp the filter group delay. Owns the
/// PlotView's model, the single value axis it reconfigures per mode, and the
/// drawn series; the panel supplies the mode and the per-channel curves.
/// The junction modes swap in their own models — lag-domain correlation (see
/// <see cref="DrawCorrelation"/>) and per-band coherence (see
/// <see cref="DrawCoherence"/>); the models keep their axes' zoom/pan
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
    private const string CoherenceLagAxisKey = "coh-lag";
    private const string CoherenceCoefficientAxisKey = "coh-r";
    private const string CoherenceTrackerFormat = "{0}\n{2:0} Hz\n{4:0.00}";

    private readonly PlotView view;
    private readonly PlotModel chainModel;
    private readonly PlotModel correlationModel;
    private readonly PlotModel coherenceModel;

    // Tracks the mode the value axis range was last set for, so switching modes
    // resets the range to the new mode's default while an in-mode redraw (e.g.
    // editing a filter) preserves the user's zoom/pan.
    private DspPlotMode? valueAxisMode;

    // The pair and window the correlation axes were last configured for: a
    // junction switch resets the ranges, an in-pair redraw (delay edits)
    // preserves the user's zoom/pan.
    private (string Pair, double WindowMs)? correlationAxisState;

    // Same idea for the coherence axes, over everything they are placed from.
    // The lag range depends on the data (a misaligned junction pushes optima
    // past the comb corridor), so it is bucketed to half-millisecond steps: a
    // delay edit that keeps the ladder in the same bucket preserves the user's
    // zoom, one that leaves it re-fits the axes instead of clipping the points
    // that moved. The BAND is here because the frequency axis is fitted from
    // it: editing the pair's crossover keeps the pair title, and the new
    // ladder's lag limit usually lands in the same bucket (both are typically
    // at the 1 ms floor), so a state of title-and-lag alone would leave the
    // frequency axis on the PREVIOUS band and clip most of the rebuilt ladder.
    private (string Pair, double LagLimitMs, double BandLowHz, double BandHighHz)?
        coherenceAxisState;

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

        // Each comb's analytic envelope first, as thin translucent ± guides
        // in the parent curve's own color, under everything and out of the
        // legend: the envelope's extremum is the coherence packet's center
        // with the carrier lobes stripped — the group optimum the eye can
        // read a lobe-skip against — while the carrier keeps answering which
        // LOBE the tune sits on.
        AddEnvelopeGuides(
            model, "PHAT envelope", data.Whitened,
            OxyColor.FromRgb(79, 195, 247));
        AddEnvelopeGuides(
            model, "PHAT direct envelope", data.WhitenedDirect,
            OxyColor.FromRgb(200, 130, 255));

        // Four curves, each with its own question: PHAT — the whitened
        // full-record comb (the honest read at bass junctions, where "direct
        // sound" is not a measurable notion); PHAT direct — the drivers'
        // wavefronts (the polarity witness); score both ways — the summation
        // surface the search optimizes. The raw amplitude-weighted
        // correlation used to be drawn too and answered nothing the others
        // do not: its weighting hands the lag to whatever the cabin plays
        // loudest, which is neither the comb, nor the drivers, nor the sum.
        AddCorrelationSeries(
            model, "PHAT", data.Whitened,
            OxyColor.FromRgb(79, 195, 247), CoefficientAxisKey,
            LineStyle.Solid, 1.8);
        // The same whitened read on the DIRECT sound alone: each channel cut
        // to a couple of crossover periods behind its own front, so the comb
        // shows the drivers' timing where the full-record twin shows whatever
        // the cabin's reflections correlate best (on a thin-overlap junction
        // the two disagree by whole periods).
        AddCorrelationSeries(
            model, "PHAT direct", data.WhitenedDirect,
            OxyColor.FromRgb(200, 130, 255), CoefficientAxisKey,
            LineStyle.Solid, 1.4);
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

    // The ± analytic envelope of one correlation comb, as legend-less thin
    // guides (the title still names them in the tracker). Computed over the
    // displayed window: the transform's edge wobble stays confined to the
    // outermost samples of a decayed comb, which a 35%-alpha guide is
    // entitled to.
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
        model.Legends.Add(new OxyPlot.Legends.Legend
        {
            LegendPosition = OxyPlot.Legends.LegendPosition.TopRight,
            LegendTextColor = OxyColor.FromRgb(210, 214, 222),
            LegendBackground = OxyColor.FromAColor(120, OxyColor.FromRgb(40, 44, 54))
        });
        PlotModelStyle.AddFrequencyAxis(model);
        model.Axes.Add(new LinearAxis
        {
            Key = CoherenceLagAxisKey,
            Position = AxisPosition.Left,
            Title = "delay to optimum (ms)",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot
        });
        model.Axes.Add(new LinearAxis
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
            TextColor = OxyColor.FromAColor(10, OxyColors.White),
            FontSize = 40,
            FontWeight = FontWeights.Bold
        });
        return model;
    }

    /// <summary>
    /// Swaps in the frequency-domain coherence model and draws one junction's
    /// arrival-coherence ladder. Null clears the view (no measurable pair);
    /// a view whose ladder the level gate emptied draws the title alone, so
    /// "nothing to measure" and "nothing selected" stay distinguishable.
    /// </summary>
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
        model.TitleColor = OxyColor.FromRgb(210, 214, 222);
        // Below the engine's own direct-coherence floor the band windows are
        // long enough for cabin modes to rule the correlation: the ladder
        // still reports HOW coherent the tune is, but its optima can sit on a
        // mode rather than on the drivers, and the plot says so.
        model.Subtitle = data.CrossoverHz < 120
            ? "low junction: cabin modes can dominate this read"
            : null;
        model.SubtitleFontSize = 9;
        model.SubtitleColor = OxyColor.FromAColor(170, OxyColor.FromRgb(240, 200, 90));

        List<VirtualCrossoverAnalysis.ArrivalCoherencePoint> ladder = data.Ladder;
        // The default lag range covers the comb corridor and every optimum,
        // bucketed so in-place delay edits keep the user's zoom (see the
        // state field).
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

        // The comb corridor: past half a period the optimum belongs to the
        // NEXT lobe — that band's arrivals differ by a whole cycle and no
        // single delay reconciles it with the bands inside the corridor.
        var corridorUpper = NewCoherenceLine(
            "±T/2 (next lobe)", OxyColor.FromAColor(150, OxyColors.Gray),
            CoherenceLagAxisKey, LineStyle.Dash, 1.0);
        var corridorLower = NewCoherenceLine(
            null, OxyColor.FromAColor(150, OxyColors.Gray),
            CoherenceLagAxisKey, LineStyle.Dash, 1.0);
        // The attainable-versus-collected gap: the area between the envelope
        // at the optimum and the envelope at lag 0 is the coherence the
        // applied tune leaves on the table — zero-height where the band is
        // centered.
        var gap = new AreaSeries
        {
            Title = "r attainable",
            Tag = SeriesTag,
            Color = OxyColors.Transparent,
            Color2 = OxyColors.Transparent,
            Fill = OxyColor.FromAColor(50, OxyColor.FromRgb(124, 213, 124)),
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            YAxisKey = CoherenceCoefficientAxisKey,
            TrackerFormatString = CoherenceTrackerFormat
        };
        var currentR = NewCoherenceLine(
            "r current", OxyColor.FromRgb(124, 213, 124),
            CoherenceCoefficientAxisKey, LineStyle.Dot, 1.8);
        var lagLine = NewCoherenceLine(
            "Δt to optimum", OxyColor.FromAColor(200, OxyColors.White),
            CoherenceLagAxisKey, LineStyle.Solid, 1.2);
        // Polarity is a carrier read and only means something inside the
        // central lobe: within a quarter period of zero the optimum IS the
        // lobe the tune sits on, past that the envelope peak lands between
        // opposite-signed lobes and the sign merely reports the grid. The
        // undecidable points stay neutral instead of flashing a polarity.
        var inPolarity = new ScatterSeries
        {
            Title = "optimum in polarity",
            Tag = SeriesTag,
            MarkerType = MarkerType.Circle,
            MarkerSize = 3.5,
            MarkerFill = OxyColor.FromRgb(79, 195, 247),
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            YAxisKey = CoherenceLagAxisKey,
            TrackerFormatString = CoherenceTrackerFormat
        };
        var invertedPolarity = new ScatterSeries
        {
            Title = "optimum inverted",
            Tag = SeriesTag,
            MarkerType = MarkerType.Square,
            MarkerSize = 3.5,
            MarkerFill = OxyColor.FromRgb(255, 169, 79),
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            YAxisKey = CoherenceLagAxisKey,
            TrackerFormatString = CoherenceTrackerFormat
        };
        var ambiguous = new ScatterSeries
        {
            Tag = SeriesTag,
            MarkerType = MarkerType.Circle,
            MarkerSize = 2.5,
            MarkerFill = OxyColors.Transparent,
            MarkerStroke = OxyColor.FromAColor(200, OxyColors.Gray),
            MarkerStrokeThickness = 1,
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
            ScatterSeries markers =
                Math.Abs(point.LagMs) < point.HalfPeriodMs / 2.0
                    ? point.OptimumInverted ? invertedPolarity : inPolarity
                    : ambiguous;
            markers.Points.Add(
                new ScatterPoint(point.FrequencyHz, point.LagMs));
        }

        model.Series.Add(gap);
        model.Series.Add(corridorUpper);
        model.Series.Add(corridorLower);
        model.Series.Add(currentR);
        model.Series.Add(lagLine);
        model.Series.Add(inPolarity);
        model.Series.Add(invertedPolarity);
        model.Series.Add(ambiguous);

        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Horizontal,
            Y = 0,
            YAxisKey = CoherenceLagAxisKey,
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            Color = OxyColor.FromAColor(160, OxyColors.White),
            StrokeThickness = 1,
            Tag = SeriesTag
        });
        model.Annotations.Add(new LineAnnotation
        {
            Type = LineAnnotationType.Vertical,
            X = data.CrossoverHz,
            XAxisKey = PlotModelFactory.FrequencyAxisKey,
            YAxisKey = CoherenceLagAxisKey,
            Color = OxyColor.FromAColor(120, OxyColors.Gray),
            LineStyle = LineStyle.Dot,
            StrokeThickness = 1,
            Text = "fc",
            TextColor = OxyColor.FromAColor(170, OxyColors.Gray),
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
