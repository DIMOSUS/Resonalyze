using System.Globalization;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Resonalyze.Dsp;

namespace Resonalyze
{
    public class LogarithmicClipAxis : LogarithmicAxis
    {
        public double ClipValue { get; set; }

        public override void GetTickValues(out IList<double> majorLabelValues, out IList<double> majorTickValues, out IList<double> minorTickValues)
        {
            base.GetTickValues(out majorLabelValues, out majorTickValues, out minorTickValues);

            int majorClipInd = int.MaxValue;
            for (int i = 0; i < majorTickValues.Count; i++)
            {
                if (Math.Round(majorTickValues[i]) > ClipValue)
                {
                    majorClipInd = i;
                    break;
                }
            }

            int minorClipInd = int.MaxValue;
            for (int i = 0; i < minorTickValues.Count; i++)
            {
                if (Math.Round(minorTickValues[i]) > ClipValue)
                {
                    minorClipInd = i;
                    break;
                }
            }

            for (int i = (majorTickValues.Count - 1); i >= majorClipInd; i--)
            {
                majorTickValues.RemoveAt(i);
            }

            for (int i = minorTickValues.Count - 1; i >= minorClipInd; i--)
            {
                minorTickValues.RemoveAt(i);
            }
        }
    }

    public sealed class Slice
    {
        public List<DataPoint> Data { get; set; }
        public double SliceOffset { get; }
        public double SliceMinValidFrequency { get; }
        public double Frequency { get; }
        public int SampleRate { get; }

        public Slice(List<DataPoint> data, double sliceOffset, double frequency, double sliceMinValidFrequency, int sampleRate)
        {
            Data = data;
            SliceOffset = sliceOffset;
            SliceMinValidFrequency = sliceMinValidFrequency;
            Frequency = frequency;
            SampleRate = sampleRate;
        }
    }

    public enum WaterfallMode
    {
        Fourier,
        FourierCSD,
        BurstDecay
    }

    public sealed class WaterfallGenerateOptions
    {
        public int SliceCount { get; set; } = 64;
        public int Step { get; set; } = 4;
        public int Window { get; set; } = 4096;
        public int LeftTukeyWindow { get; set; } = 8;
        public int RightTukeyWindow { get; set; } = 512;
        public int DbRange { get; set; } = -60;
        public double SmoothingInverseOctaves { get; set; } = 6;
        public int Offset { get; set; }
        public WaterfallMode WaterfallMode { get; set; } = WaterfallMode.Fourier;
        public double Periods { get; set; } = 30;
    }

    public class WaterfallSeries : XYAxisSeries
    {
        public WaterfallSeries()
        {
            // {0} is the series title by convention; the coordinates are {1}/{2}.
            TrackerFormatString = "X: {1:0.000}\r\nY: {2:0.000}";
            RawSlices = new List<Slice>();
            ResampleSlices = new List<Slice>();
            GenerateOptions = new WaterfallGenerateOptions();
        }

        public List<Slice> RawSlices { get; }
        public List<Slice> ResampleSlices { get; }

        private int rawSlicesRevision;
        private int resampledRevision = -1;
        private double resampledMinFrequency = double.NaN;
        private double resampledMaxFrequency = double.NaN;
        private int resampledWidth = -1;

        /// <summary>
        /// Gets or sets the color axis.
        /// </summary>
        /// <value>The color axis.</value>
        /// <remarks>The Maximum value of the ColorAxis defines the maximum number of iterations.</remarks>
        public LinearColorAxis? ColorAxis { get; protected set; }

        /// <summary>
        /// Gets or sets the color axis key.
        /// </summary>
        /// <value>The color axis key.</value>
        public string? ColorAxisKey { get; set; }

        public OxyColor BackgroundColor { get; set; }

        public WaterfallGenerateOptions GenerateOptions { get; set; }

        /// <summary>
        /// Gets the point on the series that is nearest the specified point.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <param name="interpolate">Interpolate the series if this flag is set to <c>true</c>.</param>
        /// <returns>A TrackerHitResult for the current hit.</returns>
        public override TrackerHitResult GetNearestPoint(ScreenPoint point, bool interpolate)
        {
            var p = this.InverseTransform(point);
            //var it = this.Solve(p.X, p.Y, (int)this.ColorAxis.ActualMaximum + 1);
            return new TrackerHitResult
            {
                Series = this,
                DataPoint = p,
                Position = point,
                Index = -1,
                Text = StringHelper.Format(
                    this.ActualCulture,
                    this.TrackerFormatString,
                    string.Empty,
                    p.X,
                    p.Y)
            };
        }

        /// <summary>
        /// Renders the series on the specified render context.
        /// </summary>
        /// <param name="rc">The rendering context.</param>
        public override void Render(IRenderContext rc)
        {
            if (
                !(XAxis is LogarithmicClipAxis) ||
                RawSlices.Count < 8 ||
                ColorAxis is not LinearColorAxis colorAxis
                )
                return;

            LogarithmicClipAxis clippedAxis = (LogarithmicClipAxis)XAxis;

            double upperDb = (colorAxis.Minimum + colorAxis.Maximum) * 0.5;
            double lowerDb = colorAxis.Minimum;

            //        2__________________1
            //       / |                |
            //      /  |                |
            //    3/   |4_______________|5
            //     |  /                /
            //     | /                /
            //    0|/________________/6

            ScreenPoint p0 = this.Transform(XAxis.ActualMinimum, YAxis.ActualMinimum);
            ScreenPoint p1 = this.Transform(XAxis.ActualMaximum, YAxis.ActualMaximum);
            ScreenPoint p5 = this.Transform(XAxis.ActualMaximum, (YAxis.ActualMinimum + YAxis.ActualMaximum) * 0.5);
            ScreenPoint p6 = this.Transform(clippedAxis.ClipValue, YAxis.ActualMinimum);
            double slopeOffset = p5.X - p6.X;

            ScreenPoint p4 = new ScreenPoint(p0.X + slopeOffset, p5.Y);
            ScreenPoint p2 = new ScreenPoint(p4.X, p1.Y);
            ScreenPoint p3 = new ScreenPoint(p0.X, p4.Y);

            double w = (int)(p1.X - p0.X);
            double h = (int)(p0.Y - p1.Y);

            double thickness = 3;
            var pen = new OxyPen(OxyColor.FromRgb(0, 127, 32), thickness, LineStyle.Solid);

            void boundLine(ScreenPoint p0_, ScreenPoint p1_, double offsetX = 0, double offsetY = 0)
            {
                rc.DrawLine(p0_.X + offsetX, p0_.Y + offsetY, p1_.X + offsetX, p1_.Y + offsetY, pen, EdgeRenderingMode.Automatic);
            }

            boundLine(p0, p4);
            boundLine(p4, p5);
            boundLine(p4, p2);

            double minFrequency = Math.Round(XAxis.ActualMinimum);
            double maxFrequency = Math.Round(InverseTransform(p6).X);

            double dbMin = colorAxis.ActualMinimum;
            double dbMax = (colorAxis.ActualMaximum + colorAxis.ActualMinimum) * 0.5;
            double dbWindow = dbMax - dbMin;
            double dbScale = (p0.Y - p3.Y) / dbWindow;

            if (GenerateOptions.WaterfallMode == WaterfallMode.Fourier)
            {
                int width = (int)Math.Round(p6.X - p0.X);
                Resample(minFrequency, maxFrequency, width);

                double timeWindowStart = ResampleSlices[0].SliceOffset;
                double timeWindow = ResampleSlices[^1].SliceOffset - timeWindowStart;

                List<PointSeries> pointSeries = new List<PointSeries>(ResampleSlices.Count);

                for (int i = 0; i < ResampleSlices.Count; i++)
                {
                    var slice = ResampleSlices[i];
                    double timePosition = timeWindow == 0
                        ? 0
                        : (slice.SliceOffset - timeWindowStart) / timeWindow;
                    ScreenPoint corner = Lerp(p4, p0, timePosition);

                    List<ScreenPoint> points = new List<ScreenPoint> { };
                    List<OxyColor> colors = new List<OxyColor> { };
                    //
                    for (int ip = 0; ip < slice.Data.Count; ip++)
                    {
                        double xPos = corner.X + ip;

                        double dB = slice.Data[ip].Y;
                        colors.Add(ColorAxisExtensions.GetColor(colorAxis, dB));
                        dB = Math.Min(Math.Max(dB, dbMin), dbMax);

                        double dbPixOffset = (dB - dbMin) * dbScale;

                        double yPos = corner.Y - dbPixOffset;

                        points.Add(new ScreenPoint(xPos, yPos));
                    }

                    pointSeries.Add(new PointSeries(points, colors, corner, slice.SliceOffset));
                }

                var lPen = new OxyPen(OxyColor.FromRgb(0, 0, 0), 1, LineStyle.Solid);

                const double labelCount = 5.0;
                double labelStep = pointSeries.Count / labelCount;
                double labelAcc = 0;

                for (int i = 0; i < pointSeries.Count; i++)
                {
                    var pSeries = pointSeries[i];
                    var corner = pointSeries[i].Corner;

                    for (int k = 0; k < pSeries.Points.Count; k++)
                    {
                        var p0_ = pSeries.Points[k];
                        OxyRect rect = new OxyRect(p0_, new OxySize(1, corner.Y - p0_.Y));
                        rc.DrawRectangle(rect, (k == 0 || k == pSeries.Points.Count - 1) ? pSeries.Colors[k] : BackgroundColor, OxyColors.Undefined, 0, EdgeRenderingMode.Automatic);
                    }

                    for (int k = 0; k < pSeries.Points.Count - 1; k++)
                    {
                        var p0_ = pSeries.Points[k];
                        var p1_ = pSeries.Points[k + 1];

                        lPen.Color = pSeries.Colors[k];
                        rc.DrawLine(p0_.X, p0_.Y, p1_.X, p1_.Y, lPen, EdgeRenderingMode.Automatic);
                    }

                    labelAcc += 1;
                    if (labelAcc > labelStep)
                    {
                        labelAcc = 0;
                        rc.DrawText(
                            new ScreenPoint(corner.X + width, corner.Y),
                            (pointSeries[i].TimeOffset * 1000).ToString("0.##", CultureInfo.InvariantCulture) + "ms",
                            OxyColors.Aqua);
                    }
                }
            }

            if (GenerateOptions.WaterfallMode == WaterfallMode.BurstDecay)
            {
                int width = (int)Math.Round(p4.X - p0.X);
                Resample(minFrequency, maxFrequency, width);

                var lPen = new OxyPen(OxyColor.FromRgb(0, 0, 0), 2, LineStyle.Solid);

                for (int slice = 0; slice < ResampleSlices.Count; slice++)
                {
                    double freq = ResampleSlices[slice].Frequency;
                    double frequencyPosition = DataHelper.FrequencyToLogPosition(freq, minFrequency, maxFrequency);

                    List<ScreenPoint> upPoints = new List<ScreenPoint> { };
                    List<ScreenPoint> downPoints = new List<ScreenPoint> { };
                    List<OxyColor> colors = new List<OxyColor> { };

                    ScreenPoint cornerUp = Lerp(p4, p5, frequencyPosition);
                    ScreenPoint cornerDown = Lerp(p0, p6, frequencyPosition);

                    // A slice with no usable data resamples to an empty list;
                    // indexing it by pixel column would throw inside Render.
                    if (ResampleSlices[slice].Data.Count < width)
                    {
                        continue;
                    }

                    for (int pInd = 0; pInd < width; pInd++)
                    {
                        double xPos = cornerUp.X - pInd;

                        double dB = ResampleSlices[slice].Data[pInd].Y;
                        colors.Add(ColorAxisExtensions.GetColor(colorAxis, dB));

                        dB = Math.Min(Math.Max(dB, dbMin), dbMax);

                        double dbPixOffset = (dB - dbMin) * dbScale;

                        double yPosDown = Lerp(cornerUp, cornerDown, (double)pInd / width).Y;
                        double yPos = yPosDown - dbPixOffset;

                        upPoints.Add(new ScreenPoint(xPos, yPos));
                        downPoints.Add(new ScreenPoint(xPos, yPosDown));
                    }

                    for (int k = 0; k < upPoints.Count; k++)
                    {
                        var p0_ = upPoints[k];
                        var p1_ = downPoints[k];
                        OxyRect rect = new OxyRect(p0_, new OxySize(1, p1_.Y - p0_.Y));
                        rc.DrawRectangle(rect, BackgroundColor, OxyColors.Undefined, 0, EdgeRenderingMode.Automatic);
                    }

                    int segmentCount = Math.Min(upPoints.Count, colors.Count) - 1;
                    for (int k = 0; k < segmentCount; k++)
                    {
                        var p0_ = upPoints[k];
                        var p1_ = upPoints[k + 1];

                        lPen.Color = colors[k + 1];
                        rc.DrawLine(p0_.X, p0_.Y, p1_.X, p1_.Y, lPen, EdgeRenderingMode.Automatic);
                    }
                }


                const double labelCount = 5.0;
                double labelStep = GenerateOptions.Periods / labelCount;

                for (double label = labelStep; label <= GenerateOptions.Periods; label += labelStep)
                {
                    ScreenPoint pos = Lerp(p5, p6, label / GenerateOptions.Periods);

                    rc.DrawText(
                        pos,
                        Math.Round(label).ToString(CultureInfo.InvariantCulture) + " periods",
                        OxyColors.Aqua);
                }
            }

            boundLine(p1, p2, 0, thickness / 2);
            boundLine(p2, p3);
            boundLine(p3, p0, thickness / 2, 0);
            boundLine(p0, p6, 0, -thickness / 2);
            boundLine(p6, p5, -thickness / 2, 0);
            boundLine(p5, p1, -thickness / 2, 0);
        }

        class PointSeries
        {
            public List<ScreenPoint> Points;
            public List<OxyColor> Colors;
            public ScreenPoint Corner;
            public double TimeOffset;

            public PointSeries(List<ScreenPoint> points, List<OxyColor> colors, ScreenPoint corner, double timeOffset)
            {
                Points = points;
                Colors = colors;
                Corner = corner;
                TimeOffset = timeOffset;
            }
        }

        private static ScreenPoint Lerp(ScreenPoint p0, ScreenPoint p1, double t)
        {
            return new ScreenPoint(
                (1.0 - t) * p0.X + t * p1.X,
                (1.0 - t) * p0.Y + t * p1.Y);
        }

        public void FillFourierWaterfallData(IImpulseMeasurement measurement)
        {
            RawSlices.Clear();
            rawSlicesRevision++;

            int window = GenerateOptions.Window;
            int step = GenerateOptions.Step;
            int sliceCount = GenerateOptions.SliceCount;
            if (step == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(GenerateOptions.Step),
                    "Waterfall step cannot be zero.");
            }
            int windowFuncOffset = step >= 0 ? GenerateOptions.LeftTukeyWindow : window - GenerateOptions.RightTukeyWindow;

            double leftTukeyWindow = (double)GenerateOptions.LeftTukeyWindow / window * 2.0;
            double rightTukeyWindow = (double)GenerateOptions.RightTukeyWindow / window * 2.0;
            double[] windowFunction = Windowing.TukeyWindow(GenerateOptions.Window, leftTukeyWindow, rightTukeyWindow);

            if (GenerateOptions.WaterfallMode == WaterfallMode.Fourier)
            {
                for (int i = 0; i < sliceCount; i++)
                {
                    RawSlices.Add(new Slice(new List<DataPoint>(), 0, 0, 0, measurement.SampleRate));
                }

                Parallel.For(0, sliceCount, slice =>
                {
                    int offset = measurement.PeakIndex - windowFuncOffset + slice * step + GenerateOptions.Offset;
                    List<DataPoint> data = OxyPlotAdapter.ToDataPoints(
                        DataHelper.GetOversampledSpectrumData(measurement, offset, windowFunction));

                    double time;
                    if (step > 0)
                    {
                        time = (slice * step + GenerateOptions.Offset) / (double)measurement.SampleRate;
                    }
                    else
                    {
                        time = (slice * step + window - windowFuncOffset + GenerateOptions.Offset) / (double)measurement.SampleRate;
                    }
                    RawSlices[slice] = new Slice(data, time, 0, 0, measurement.SampleRate);
                });
            }

            if (GenerateOptions.WaterfallMode == WaterfallMode.BurstDecay)
            {
                int offset = measurement.PeakIndex - GenerateOptions.LeftTukeyWindow + GenerateOptions.Offset;
                double smoothingOctaves = GenerateOptions.SmoothingInverseOctaves > 0
                    ? 1.0 / GenerateOptions.SmoothingInverseOctaves
                    : 1.0 / 48.0;
                IReadOnlyList<BurstDecaySlice> slices = WaterfallAnalysis.BuildBurstDecayRawSlices(
                    measurement,
                    offset,
                    window,
                    windowFunction,
                    smoothingOctaves);

                for (int i = 0; i < slices.Count; i++)
                {
                    RawSlices.Add(new Slice(new List<DataPoint>(), 0, 0, 0, measurement.SampleRate));
                }

                Parallel.For(0, slices.Count, frequencyIndex =>
                {
                    BurstDecaySlice slice = slices[frequencyIndex];
                    RawSlices[frequencyIndex] = new Slice(
                        OxyPlotAdapter.ToDataPoints(slice.Data),
                        0,
                        slice.Frequency,
                        0,
                        measurement.SampleRate);
                });
            }
        }

        public void Resample(double minFrequency, double maxFrequency, int width)
        {
            // Render calls this on every plot invalidation (pan, resize, label
            // refresh); re-smoothing all slices is only needed when the view
            // window or the underlying data actually changed.
            if (resampledRevision == rawSlicesRevision &&
                resampledMinFrequency == minFrequency &&
                resampledMaxFrequency == maxFrequency &&
                resampledWidth == width)
            {
                return;
            }

            resampledRevision = rawSlicesRevision;
            resampledMinFrequency = minFrequency;
            resampledMaxFrequency = maxFrequency;
            resampledWidth = width;

            ResampleSlices.Clear();
            if (GenerateOptions.WaterfallMode == WaterfallMode.Fourier)
            {
                foreach (var rs in RawSlices)
                {
                    ResampleSlices.Add(
                        new Slice(new List<DataPoint>(), rs.SliceOffset, 0, 0, 0));
                }

                Parallel.For(0, RawSlices.Count, i =>
                {
                    double smoothingOctaves = GenerateOptions.SmoothingInverseOctaves > 0
                        ? 1.0 / GenerateOptions.SmoothingInverseOctaves
                        : 0.0;
                    List<SignalPoint> resampled = DataHelper.LogarithmicResample(
                        OxyPlotAdapter.ToSignalPoints(RawSlices[i].Data),
                        minFrequency,
                        maxFrequency,
                        width,
                        null,
                        smoothingOctaves);
                    ResampleSlices[i].Data = OxyPlotAdapter.ToDataPoints(resampled);
                });
            }

            if (GenerateOptions.WaterfallMode == WaterfallMode.BurstDecay)
            {
                ResampleSlices.Clear();
                for (int slice = 0; slice < RawSlices.Count; slice++)
                {
                    ResampleSlices.Add(
                        new Slice(new List<DataPoint>(), 0, 0, 0, RawSlices[slice].SampleRate));
                }

                Parallel.For(0, RawSlices.Count, slice =>
                {
                    BurstDecaySlice rawSlice = new BurstDecaySlice(
                        RawSlices[slice].Frequency,
                        OxyPlotAdapter.ToSignalPoints(RawSlices[slice].Data));

                    List<SignalPoint> resampled = WaterfallAnalysis.ResampleBurstDecaySlice(
                        rawSlice.Data,
                        rawSlice.Frequency,
                        RawSlices[slice].SampleRate,
                        width,
                        GenerateOptions.Periods).ToList();

                    ResampleSlices[slice] = new Slice(
                        OxyPlotAdapter.ToDataPoints(resampled),
                        RawSlices[slice].SliceOffset,
                        RawSlices[slice].Frequency,
                        RawSlices[slice].SliceMinValidFrequency,
                        RawSlices[slice].SampleRate);
                });
            }
        }

        /// <summary>
        /// Ensures that the axes of the series is defined.
        /// </summary>
        protected override void EnsureAxes()
        {
            base.EnsureAxes();
            this.ColorAxis = this.ColorAxisKey != null ?
                             this.PlotModel.GetAxis(this.ColorAxisKey) as LinearColorAxis :
                             this.PlotModel.DefaultColorAxis as LinearColorAxis;
        }
    }
}
