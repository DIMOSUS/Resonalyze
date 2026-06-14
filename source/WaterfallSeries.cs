using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Mixer;
using System.Xml.Linq;
using System.Drawing;
using MathNet.Numerics;
using System.Security.Policy;
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

    public class Slice
    {
        public List<DataPoint> Data;
        public double SliceOffset;
        public double SliceMinValidFrequence;
        public double Frequence;
        public int SampleRate;

        public Slice(List<DataPoint> data, double sliceOffset, double frequence, double sliceMinValidFrequence, int sampleRate)
        {
            Data = data;
            SliceOffset = sliceOffset;
            SliceMinValidFrequence = sliceMinValidFrequence;
            Frequence = frequence;
            SampleRate = sampleRate;
        }
    }

    public enum WaterfallMode
    {
        Fourier,
        FourierCSD,
        BurstDecay
    }

    public class WterfallGenerateOptions
    {
        public int SliceCount = 64;
        public int Step = 4;
        public int Window = 4096;
        public int LeftTukeyWindow = 8;
        public int RightTukeyWindow = 512;
        public int dBRange = -60;
        public double SmothInvOctaves = 6;
        public int Offset = 0;
        public WaterfallMode WaterfallMode = WaterfallMode.Fourier;
        public double Periods = 30;
    }

    public class WaterfallSeries : XYAxisSeries
    {
        public WaterfallSeries()
        {
            this.TrackerFormatString = "X: {0:0.000}\r\nY: {1:0.000}\r\nIterations: {2}";
            RawSlices = new List<Slice> { };
            ResampleSlices = new List<Slice> { };
        }

        public List<Slice> RawSlices;
        public List<Slice> ResampleSlices;

        /// <summary>
        /// Gets or sets the color axis.
        /// </summary>
        /// <value>The color axis.</value>
        /// <remarks>The Maximum value of the ColorAxis defines the maximum number of iterations.</remarks>
        public LinearColorAxis ColorAxis { get; protected set; }

        /// <summary>
        /// Gets or sets the color axis key.
        /// </summary>
        /// <value>The color axis key.</value>
        public string ColorAxisKey { get; set; }

        public OxyColor BackgroundColor { get; set; }

        public WterfallGenerateOptions GenerateOptions { get; set; }

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
                Item = null,
                Index = -1,
                Text = StringHelper.Format(this.ActualCulture, this.TrackerFormatString, null, p.X, p.Y, 999)
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
                RawSlices.Count < 8
                )
                return;

            LogarithmicClipAxis lcAxis = (LogarithmicClipAxis)XAxis;

            double dbUp = (ColorAxis.Minimum + ColorAxis.Maximum) * 0.5;
            double dbDown = ColorAxis.Minimum;

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
            ScreenPoint p6 = this.Transform(lcAxis.ClipValue, YAxis.ActualMinimum);
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

            double minFr = Math.Round(XAxis.ActualMinimum);
            double maxFr = Math.Round(InverseTransform(p6).X);

            double dbMin = ColorAxis.ActualMinimum;
            double dbMax = (ColorAxis.ActualMaximum + ColorAxis.ActualMinimum) * 0.5;
            double dbWindow = dbMax - dbMin;
            double dbSacale = (p0.Y - p3.Y) / dbWindow;

            if (GenerateOptions.WaterfallMode == WaterfallMode.Fourier)
            {
                int width = (int)Math.Round(p6.X - p0.X);
                Resample(minFr, maxFr, width);

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
                        colors.Add(ColorAxisExtensions.GetColor(ColorAxis, dB));
                        dB = Math.Min(Math.Max(dB, dbMin), dbMax);

                        double dbPixOffset = (dB - dbMin) * dbSacale;

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
                        rc.DrawText(new ScreenPoint(corner.X + width, corner.Y), Math.Round(pointSeries[i].TimeOffset * 1000, 2).ToString() + "ms", OxyColors.Aqua);
                    }
                }
            }

            if (GenerateOptions.WaterfallMode == WaterfallMode.BurstDecay)
            {
                int width = (int)Math.Round(p4.X - p0.X);
                Resample(minFr, maxFr, width);

                var lPen = new OxyPen(OxyColor.FromRgb(0, 0, 0), 2, LineStyle.Solid);

                for (int slice = 0; slice < ResampleSlices.Count; slice++)
                {
                    double freq = ResampleSlices[slice].Frequence;
                    double freqInt = DataHelper.FrequenceToLog10(freq, minFr, maxFr);

                    List<ScreenPoint> upPoints = new List<ScreenPoint> { };
                    List<ScreenPoint> downPoints = new List<ScreenPoint> { };
                    List<OxyColor> colors = new List<OxyColor> { };

                    ScreenPoint cornerUp = Lerp(p4, p5, freqInt);
                    ScreenPoint cornerDown = Lerp(p0, p6, freqInt);

                    for (int pInd = 0; pInd < width; pInd++)
                    {
                        double xPos = cornerUp.X - pInd;

                        double dB = ResampleSlices[slice].Data[pInd].Y;
                        colors.Add(ColorAxisExtensions.GetColor(ColorAxis, dB));

                        dB = Math.Min(Math.Max(dB, dbMin), dbMax);

                        double dbPixOffset = (dB - dbMin) * dbSacale;

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

                for(double label = labelStep; label <= GenerateOptions.Periods; label += labelStep)
                {
                    ScreenPoint pos = Lerp(p5, p6, label / GenerateOptions.Periods);

                    rc.DrawText(pos, Math.Round(label).ToString() + " periods", OxyColors.Aqua);
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

        public void FillFourierWaterfallData(ExpSweepMeasurement measurement)
        {
            RawSlices.Clear();

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
            double[] winFunc = Windowing.TukeyWindow(GenerateOptions.Window, leftTukeyWindow, rightTukeyWindow);

            if (GenerateOptions.WaterfallMode == WaterfallMode.Fourier)
            {
                for (int i = 0; i < sliceCount; i++)
                {
                    RawSlices.Add(null);
                }

                //for (int slice = 0; slice < sliceCount; slice++)
                Parallel.For(0, sliceCount, slice =>
                {
                    int offset = measurement.MaxMagnitudeInd - windowFuncOffset + slice * step + GenerateOptions.Offset;

                    Complex[] fftSet = DataHelper.ExtractWindow(measurement, offset, window, winFunc);

                    Fourier.Forward(fftSet, FourierOptions.Matlab);

                    List<DataPoint> data = new List<DataPoint>(fftSet.Length / 2);
                    for (int i = 1; i < fftSet.Length / 2; i++)
                    {
                        double f = (double)i * ((double)measurement.SampleRate / (double)window);
                        //if (f >= 20 && f <= 20000)
                        data.Add(new DataPoint(f, Windowing.ADCtoPdB(fftSet[i].Magnitude)));
                    }

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
                int offset = measurement.MaxMagnitudeInd - GenerateOptions.LeftTukeyWindow + GenerateOptions.Offset;
                Complex[] fftSet = new Complex[window * 4];
                DataHelper.ExtractWindow(measurement, offset, window, winFunc).CopyTo(fftSet, 0);
                Fourier.Forward(fftSet, FourierOptions.Matlab);

                double smothOctaves = 1.0 / GenerateOptions.SmothInvOctaves;
                double fk = Math.Pow(2.0, 0.5 * smothOctaves);
                double fftStep = (double)measurement.SampleRate / fftSet.Length;

                double initFrequence = 20000;

                List<double> freqList = new List<double>(100);
                while (initFrequence >= fftStep * 4 && initFrequence >= 20)
                {
                    freqList.Add(initFrequence);
                    initFrequence /= fk;
                }
                freqList.Reverse();

                for (int i = 0; i < freqList.Count; i++)
                {
                    RawSlices.Add(null);
                }

                //for (int fInd = 0; fInd < freqList.Count; fInd++)
                Parallel.For(0, freqList.Count, fInd =>
                {
                    double frequence = freqList[fInd];

                    double w0 = (frequence / fftStep) * Math.PI * 2.0;

                    double fWin = Math.Pow(2.0, smothOctaves);
                    double t = 2.3548 / (w0 * (fWin - 1.0));

                    Complex[] morlet = new Complex[fftSet.Length];
                    double summ_ = 0;
                    for (int i = 0; i < morlet.Length; i++)
                    {
                        double w = (i <= morlet.Length / 2 ? i : i - morlet.Length) * Math.PI * 2.0;
                        morlet[i] = new Complex(Math.Exp(-Math.Pow((w - w0), 2.0) * t * t * 0.25), 0);// * (i % 2 > 0 ? -1.0 : 1.0);
                        summ_ += morlet[i].Magnitude;
                    }
                    double norm = morlet.Length / summ_;
                    for (int i = 0; i < morlet.Length; i++)
                    {
                        morlet[i] *= fftSet[i] * norm;
                    }

                    Fourier.Inverse(morlet, FourierOptions.Matlab);

                    List<DataPoint> data = new List<DataPoint>(morlet.Length);

                    for (int i = 0; i < morlet.Length / 2; i++)
                    {
                        data.Add(new DataPoint(i, morlet[i].Magnitude));
                    }

                    RawSlices[fInd] = new Slice(data, 0, frequence, 0, measurement.SampleRate);
                });
            }
        }

        public void Resample(double minFr, double maxFr, int width)
        {
            ResampleSlices.Clear();
            if (GenerateOptions.WaterfallMode == WaterfallMode.Fourier)
            {
                foreach (var rs in RawSlices)
                {
                    ResampleSlices.Add(new Slice(null, rs.SliceOffset, 0, 0, 0));
                }

                //for (int i = 0; i < RawSlices.Count; i++)
                Parallel.For(0, RawSlices.Count, i =>
                {
                    //-- TODO Calibration
                    ResampleSlices[i].Data = DataHelper.LogarithmicResample(RawSlices[i].Data, minFr, maxFr, width, null, 1.0 / GenerateOptions.SmothInvOctaves);
                });
            }

            if (GenerateOptions.WaterfallMode == WaterfallMode.BurstDecay)
            {
                ResampleSlices.Clear();
                for (int slice = 0; slice < RawSlices.Count; slice++)
                {
                    ResampleSlices.Add(null);
                }

                //for (int slice = 0; slice < RawSlices.Count; slice++)
                Parallel.For(0, RawSlices.Count, slice =>
                {
                    double SmothSample(double index)
                    {
                        int a = 2;
                        int cntrInd = (int)Math.Round(index);

                        double weightAcc = 0;
                        double resAcc = 0;

                        for (int smpl = Math.Max(cntrInd - a, 0); smpl <= Math.Min(cntrInd + a, RawSlices[slice].Data.Count - 1); smpl++)
                        {
                            double weight = DataHelper.LanczosKernel(index - smpl, a);

                            resAcc += RawSlices[slice].Data[smpl].Y * weight;
                            weightAcc += weight;
                        }
                        if (weightAcc < 0.00001)
                        {
                            return 0.0;
                        }    
                        return resAcc / weightAcc;
                    }

                    double frequence = RawSlices[slice].Frequence;

                    double periodsTime = GenerateOptions.Periods / frequence;
                    double periodsSamples = RawSlices[slice].SampleRate * periodsTime;

                    List<DataPoint> data = new List<DataPoint>(width);

                    for (int i = 0; i < width; i++)
                    {
                        double interp = (double)i / (double)width;
                        double sP = interp * periodsSamples;

                        data.Add(new DataPoint(interp * GenerateOptions.Periods, DataHelper.ADCtoPdB(SmothSample(sP))));
                    }

                    ResampleSlices[slice] = new Slice(data, RawSlices[slice].SliceOffset, RawSlices[slice].Frequence, RawSlices[slice].SliceMinValidFrequence, RawSlices[slice].SampleRate);
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
