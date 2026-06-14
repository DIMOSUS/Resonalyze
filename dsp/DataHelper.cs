using MathNet.Numerics.IntegralTransforms;
using OxyPlot.Series;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Resonalyze.Dsp
{
    public class FRGenerateOptions
    {
        public int Window = 4096;
        public int LeftTukeyWindow = 256;
        public int RightTukeyWindow = 256;
        public double SmothInvOctaves = 6;
        public int Offset = 0;
        public bool Unwrap = true;
        public bool UseCalibration = true;
    }

    public class IRGenerateOptions
    {
        public int Length = 4096;
        public bool Logarithmic = false;
    }

    public static class DataHelper
    {
        //-- power
        public static double ADCtoPdB(double val)
        {
            return 20.0 * Math.Log10(Math.Max(val, 0.00000001));
        }

        public static double PdBtoADC(double val)
        {
            return Math.Pow(10.0, val / 20.0);
        }

        public static Complex[] ExtractWindow(IImpulseMeasurement measurement, int start, int length, double[]? window = null)
        {
            ArgumentNullException.ThrowIfNull(measurement);
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            Complex[] source = measurement.ImpulseResponce
                ?? throw new InvalidOperationException("Impulse response is not available.");
            Complex[] result = new Complex[length];

            for (int i = 0; i < length; i++)
            {
                int sourceIndex = start + i;
                if ((uint)sourceIndex < (uint)source.Length)
                {
                    result[i] = source[sourceIndex] *
                        (window is { Length: > 0 } && i < window.Length ? window[i] : 1.0);
                }
            }

            return result;
        }

        public static List<DataPoint> getSequenceData(IImpulseMeasurement measurement, int Start, int Length, double[]? windowFunc = null)
        {
            Complex[] fftSet = ExtractWindow(measurement, Start, Length, windowFunc);
            Fourier.Forward(fftSet, FourierOptions.Matlab);

            List<DataPoint> data = new List<DataPoint> { };
            for (int i = 1; i < Length / 2; i++)
            {
                float f = (float)i * ((float)measurement.SampleRate / (float)Length);
                //if (f >= 20 && f <= 20000)
                data.Add(new DataPoint(f, ADCtoPdB(fftSet[i].Magnitude)));
            }

            return data;
        }

        public static List<LineSeries> GetSpectrum(IImpulseMeasurement measurement, FRGenerateOptions fRGenOptions, CalibrationFile Calibration)
        {
            List<LineSeries> series = new List<LineSeries> { };

            int maxMagInd = measurement.MaxMagnitudeInd;

            {
                double leftTukeyWindow = (double)fRGenOptions.LeftTukeyWindow / fRGenOptions.Window * 2.0;
                double rightTukeyWindow = (double)fRGenOptions.RightTukeyWindow / fRGenOptions.Window * 2.0;

                double[] winFunc = Windowing.TukeyWindow(fRGenOptions.Window, leftTukeyWindow, rightTukeyWindow);

                int h1Length = fRGenOptions.Window;
                int h1Start = maxMagInd - fRGenOptions.LeftTukeyWindow;

                LineSeries LS = new LineSeries();
                var data = getSequenceData(measurement, h1Start, h1Length, winFunc);
                data = LogarithmicResample(data, 20, 20000, 1024, fRGenOptions.UseCalibration ? Calibration : null, 1.0 / fRGenOptions.SmothInvOctaves);

                LS.Points.AddRange(data);
                LS.Color = OxyColor.FromRgb(255, 127, 0);
                LS.Title = "Frequence Responce";
                series.Add(LS);
            }

            for (int h = 2; h < 5; h++)
            {
                int peak = maxMagInd - (int)measurement.HarmonicIROffset(h);

                int hStart = maxMagInd - (int)measurement.HarmonicIROffset(h + 0.03);
                int hEnd = maxMagInd - (int)measurement.HarmonicIROffset(h - 0.5);
                int hLength = hEnd - hStart;

                int leftOffset = peak - hStart;

                double leftTukeyWindow = (double)leftOffset / hLength * 2.0;
                double rightTukeyWindow = 0.5;
                double[] winFunc = Windowing.TukeyWindow(hLength, leftTukeyWindow, rightTukeyWindow);

                LineSeries LS = new LineSeries();
                LS.Title = $"HD{h}";

                var data = getSequenceData(measurement, hStart, hLength, winFunc);
                data = LogarithmicResample(data, 20, 20000, 1024, fRGenOptions.UseCalibration ? Calibration : null, 2.0 / fRGenOptions.SmothInvOctaves);

                LS.Points.AddRange(data);
                LS.Color = OxyColor.FromRgb((byte)(255 - 127 * (h - 2)), 64, (byte)(127 * (h - 2)));
                series.Add(LS);
            }

            {
                int hStart = maxMagInd - (int)measurement.HarmonicIROffset(5.5);
                int hEnd = maxMagInd - (int)measurement.HarmonicIROffset(1.5);
                int hLength = hEnd - hStart;

                double leftTukeyWindow = 0.05;
                double rightTukeyWindow = 0.05;
                double[] winFunc = Windowing.TukeyWindow(hLength, leftTukeyWindow, rightTukeyWindow);

                LineSeries LS = new LineSeries();
                LS.Title = "THD+N";

                var data = getSequenceData(measurement, hStart, hLength, winFunc);
                data = LogarithmicResample(data, 20, 20000, 1024, fRGenOptions.UseCalibration ? Calibration : null, 2.0 / fRGenOptions.SmothInvOctaves);

                LS.Points.AddRange(data);
                LS.Color = OxyColor.FromRgb(255, 255, 255);
                series.Add(LS);
            }
            return series;
        }

        public static List<DataPoint> getPhaseSequence(IImpulseMeasurement measurement, int Offset, int Length, double[] windowFunc, bool unwrap)
        {
            List<LineSeries> series = new List<LineSeries> { };

            Complex[] fftSet = ExtractWindow(measurement, measurement.MaxMagnitudeInd + Offset, Length, windowFunc);
            Fourier.Forward(fftSet, FourierOptions.Matlab);

            if (!unwrap)
            {
                List<DataPoint> data_ = new List<DataPoint> { };
                for (int i = 1; i < fftSet.Length / 2; i++)
                {
                    //double phaseOffset = (double)Offset * Math.PI * 2.0 * (double)i / fftSet.Length;

                    double f = (double)i * ((double)measurement.SampleRate / (double)Length);

                    double phase = fftSet[i].Phase;// - phaseOffset;

                    data_.Add(new DataPoint(f, phase));
                }
                return data_;
            }

            double accPhase = 0;
            double avgPhase = 0;

            List<DataPoint> dataPreFilt = new List<DataPoint> { };
            for (int i = 1; i < fftSet.Length / 2; i++)
            {
                float f = (float)i * ((float)measurement.SampleRate / (float)Length);

                double phaseOffset = (double)Offset * Math.PI * 2.0 * (double)i / fftSet.Length;
                double phase = fftSet[i].Phase + accPhase - phaseOffset;
                avgPhase += phase;
                dataPreFilt.Add(new DataPoint(f, phase));

                double s0 = fftSet[i].Phase;
                double s1 = fftSet[i + 1].Phase;

                if (s1 - s0 > Math.PI)
                {
                    accPhase -= Math.PI * 2;
                }
                if (s1 - s0 < -Math.PI)
                {
                    accPhase += Math.PI * 2;
                }
            }

            avgPhase /= (double)dataPreFilt.Count;

            double ShiftPhase = Math.Round(avgPhase / Math.Tau) * Math.Tau;

            for (int i = 0; i < dataPreFilt.Count; i++)
            {
                dataPreFilt[i] = new DataPoint(dataPreFilt[i].X, dataPreFilt[i].Y - ShiftPhase);
            }

            return dataPreFilt;
        }

        public static List<LineSeries> GetPhase(IImpulseMeasurement measurement, int length, int leftTukeyWindow, int rightTukeyWindow, int offset, double smothInvOctaves, bool unwrap)
        {
            int sOffset = -leftTukeyWindow + offset;

            double dLeftTukeyWindow = (double)leftTukeyWindow / length * 2.0;
            double dRightTukeyWindow = (double)rightTukeyWindow / length * 2.0;
            double[] winFunc = Windowing.TukeyWindow(length, dLeftTukeyWindow, dRightTukeyWindow);

            var phaseData = getPhaseSequence(measurement, sOffset, length, winFunc, unwrap);

            List<DataPoint> data = new List<DataPoint>(length / 2);
            for (int i = 0; i < phaseData.Count; i++)
            {
                data.Add(new DataPoint(phaseData[i].X, phaseData[i].Y / Math.PI * 180.0));
            }

            LineSeries LS = new LineSeries();
            //LS.Points.AddRange(data);
            LS.Points.AddRange(LinearSmoth(data, 1.0 / smothInvOctaves));
            LS.Color = OxyColor.FromRgb(255, 127, 0);
            LS.Title = "Phase";
            List<LineSeries> series = new List<LineSeries> { LS };

            return series;
        }

        public static List<LineSeries> GetImpulse(IImpulseMeasurement measurement, IRGenerateOptions opt)
        {
            List<LineSeries> series = new List<LineSeries> { };

            int offset = 512;
            int start = measurement.MaxMagnitudeInd - offset;

            int length = offset + opt.Length;
            Complex[] impulse = ExtractWindow(measurement, start, length);

            List<DataPoint> data = new List<DataPoint> { };

            if(opt.Logarithmic)
            {
                for (int i = 0; i < length; i++)
                {
                    data.Add(new DataPoint(i - offset, ADCtoPdB(impulse[i].Magnitude)));
                }
            }
            else
            {
                for (int i = 0; i < length; i++)
                {
                    data.Add(new DataPoint(i - offset, impulse[i].Real));
                }
            }

            LineSeries LS = new LineSeries();
            LS.Points.AddRange(data);
            LS.Color = OxyColor.FromRgb(255, 127, 0);
            LS.Title = "Impulse Responce";

            series.Add(LS);

            return series;
        }

        public static List<LineSeries> GetAutocorrelation(IImpulseMeasurement measurement, IRGenerateOptions opt)
        {
            int offset = 64;
            int length = 2048;
            float timeWindow = 3.0f; //-- ms

            List<LineSeries> series = new List<LineSeries> { };
            LineSeries LS = new LineSeries();
            List<DataPoint> data = new List<DataPoint> { };

            int start = measurement.MaxMagnitudeInd - offset;
            float fAvg = 0;

            //-- normalization
            Complex[] impulse = ExtractWindow(measurement, start, length);
            float[] impInSet = new float[length];
            for(int i = 0; i < length; i++)
            {
                impInSet[i] = (float)impulse[i].Real;
                fAvg += impInSet[i];
            }
            fAvg /= length;

            //-- self convolution
            float denominator = 0;
            for (int i = 0; i < length; i++)
            {
                denominator += (impInSet[i] - fAvg) * (impInSet[i] - fAvg);
            }

            float substep(int i, float step)
            {
                if (i < 1 || i > length - 3)
                    return impInSet[i];

                float wAcc = 0;
                float f = 0;
                for(int l = -1; l < 3; l++)
                {
                    float w = (float)LanczosKernel(l - step, 2.0);
                    wAcc += w;
                    f += w * impInSet[i + l];
                }

                return f / wAcc;
            }

            for (int k = 0; k < length; k++)
            {
                if (k / (float)measurement.SampleRate * 1000.0f > timeWindow)
                {
                    break;
                }

                //-- standart
                /*
                float numerator = 0;
                for (int i = 0; i < length - k; i++)
                {
                    numerator += (impInSet[i] - fAvg) * (impInSet[i + k] - fAvg);
                }
                
                float timeMs = k / (float)measurement.SampleRate * 1000.0f;
                
                data.Add(new DataPoint(timeMs, numerator / denominator));
                */

                //-- supersampling
                for (float sStep = 0; sStep < 1.0f; sStep += 0.1f)
                {
                    float numerator = 0;
                    for (int i = 0; i < length - k; i++)
                    {
                        numerator += (impInSet[i] - fAvg) * (substep(i + k, sStep) - fAvg);
                    }

                    float timeMs = (k + sStep) / (float)measurement.SampleRate * 1000.0f;
                    data.Add(new DataPoint(timeMs, denominator > float.Epsilon ? numerator / denominator : 0));
                }
            }

            LS.Points.AddRange(data);
            LS.Color = OxyColor.FromRgb(255, 127, 0);
            LS.Title = "Autocorrelation";

            series.Add(LS);

            return series;
        }

        public static List<LineSeries> GetGroupDelay(IImpulseMeasurement measurement, int length, int leftTukeyWindow, int rightTukeyWindow, int offset, double smothInvOctaves)
        {
            List<LineSeries> series = new List<LineSeries> { };

            int sOffset = -leftTukeyWindow + offset;

            double dLeftTukeyWindow = (double)leftTukeyWindow / length * 2.0;
            double dRightTukeyWindow = (double)rightTukeyWindow / length * 2.0;
            double[] winFunc = Windowing.TukeyWindowHalfZeroPadded(length, dLeftTukeyWindow, dRightTukeyWindow);

            /*
            var phaseData = getPhaseSequence(measurement, sOffset, length, winFunc, true);

            List<DataPoint> data = new List<DataPoint> { };
            for (int i = 0; i < phaseData.Count - 1; i++)
            {
                double f0 = phaseData[i].X;
                double f1 = phaseData[i + 1].X;

                double phaseDelta = phaseData[i].Y - phaseData[i + 1].Y;

                data.Add(new DataPoint(f0, phaseDelta / (f1 * Math.Tau - f0 * Math.Tau) * 1000)); //-- ms
                //data.Add(new DataPoint(f0, phaseData[i].Y / (f0 * Math.Tau)))yy;
            }
            */

            Complex[] windowedImpulse = ExtractWindow(measurement, measurement.MaxMagnitudeInd + sOffset, length, winFunc);
            Complex[] fftSet = new Complex[length];
            Complex[] fftSetScale = new Complex[length];
            for (int i = 0; i < length; i++)
            {
                var imp = windowedImpulse[i];
                fftSet[i] = imp;
                fftSetScale[i] = imp * (double)i / (double)measurement.SampleRate;
            }

            Fourier.Forward(fftSet, FourierOptions.Matlab);
            Fourier.Forward(fftSetScale, FourierOptions.Matlab);
            for (int i = 0; i < length; i++)
            {
                fftSet[i] = fftSet[i].Magnitude > 1e-12 ? fftSetScale[i] / fftSet[i] : Complex.Zero;
            }

            List<DataPoint>  data = new List<DataPoint> { };
            for (int i = 1; i < fftSet.Length; i++)
            {
                double f = (double)i * ((double)measurement.SampleRate / (double)length);

                data.Add(new DataPoint(f, (fftSet[i].Real + sOffset / (double)measurement.SampleRate) * 1000)); //-- ms
            }

            LineSeries LS = new LineSeries();
            //LS.Points.AddRange(data);
            LS.Points.AddRange(LinearSmoth(data, 1.0 / smothInvOctaves));
            LS.Color = OxyColor.FromRgb(255, 127, 0);
            LS.Title = "Group Delay";
            series.Add(LS);

            return series;
        }

        public static double Log10ToFrequence(double x, double start, double stop)
        {
            double val = x * Math.Log10(stop / start);
            return Math.Pow(10.0, val) * start;
        }

        public static double FrequenceToLog10(double frequence, double start, double stop)
        {
            return Math.Log10(frequence / start) / Math.Log10(stop / start);
        }

        public static double LanczosKernel(double x, double a = 1)
        {
            if (Math.Abs(x) < 0.00001f)
            {
                return 1.0f;
            }
            if (Math.Abs(x) <= a)
            {
                return (a * Math.Sin(Math.PI * x) * Math.Sin(Math.PI * x / a)) / (Math.PI * Math.PI * x * x);
            }
            return 0.0f;
        }

        public static List<DataPoint> LogarithmicResample(List<DataPoint> inData, double start, double stop, int steps, CalibrationFile? Calibration = null, double smothOctaves = 1.0 / 6.0, bool dBUnpack = true)
        {
            if (inData.Count < 2)
            {
                return new List<DataPoint>();
            }
            if (steps < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(steps));
            }

            List<DataPoint> outList = new List<DataPoint>(steps);

            double XStep = inData[1].X - inData[0].X;
            double a = 2.0;
            double fk = Math.Pow(2.0, smothOctaves * 0.5);

            int BinarySearchX(double searchedX)
            {
                searchedX += XStep * 0.5;
                int left = 0;
                int right = inData.Count - 1;

                if (searchedX <= inData[0].X)
                    return 0;
                if (searchedX >= inData[inData.Count - 1].X)
                    return inData.Count - 1;

                while (left <= right)
                {
                    var middle = (left + right) / 2;

                    if (searchedX >= inData[middle].X && searchedX < inData[middle + 1].X)
                    {
                        return middle;
                    }
                    else if (searchedX < inData[middle].X)
                    {
                        right = middle - 1;
                    }
                    else
                    {
                        left = middle + 1;
                    }
                }
                return -1;
            }

            DataPoint Sample(int index)
            {
                return inData[Math.Clamp(index, 0, inData.Count - 1)];
            }

            for (int i = 0; i < steps; i++)
            {
                double frequence = Log10ToFrequence(i / (steps - 1.0), start, stop);

                double halfDeltaFrequence = Math.Max(frequence * (fk - 1), XStep * a);
                double invHalfDeltaFrequence = 1.0 / halfDeltaFrequence * a;

                int cntIndex = BinarySearchX(frequence);
                int win = (int)Math.Ceiling(halfDeltaFrequence / XStep);

                double weightAcc = 0;
                double resAcc = 0;

                //int minIndex = BinarySearchX(frequence - halfDeltaFrequence);
                //int maxIndex = BinarySearchX(frequence + halfDeltaFrequence);
                //for(int smpl = minIndex; smpl <= maxIndex; smpl++)

                for (int smpl = Math.Max(cntIndex - win, 0); smpl <= cntIndex + win; smpl++)
                {
                    DataPoint samplePoint = Sample(smpl);
                    double weight = LanczosKernel((frequence - samplePoint.X) * invHalfDeltaFrequence, a);

                    if (dBUnpack)
                    {
                        resAcc += PdBtoADC(samplePoint.Y) * weight;
                    }
                    else
                    {
                        resAcc += samplePoint.Y * weight;
                    }
                    weightAcc += weight;
                }

                double filtredVal = 0;
                if (dBUnpack)
                {
                    filtredVal = ADCtoPdB(weightAcc > 1e-12 ? resAcc / weightAcc : 0);
                }
                else
                {
                    filtredVal = weightAcc > 1e-12 ? resAcc / weightAcc : 0;
                }

                if (Calibration != null)
                {
                    outList.Add(new DataPoint(frequence, filtredVal - Calibration.dBCorrection(frequence)));
                }
                else
                {
                    outList.Add(new DataPoint(frequence, filtredVal));
                }
            }

            return outList;
        }

        public static List<DataPoint> LinearSmoth(List<DataPoint> inData, double smothOctaves = 1.0 / 6.0)
        {
            if (inData.Count < 2)
            {
                return new List<DataPoint>(inData);
            }

            List<DataPoint> outList = new List<DataPoint>(inData.Count);

            double a = 2.0;
            double fk = Math.Pow(2.0, smothOctaves * 0.5);

            DataPoint Sample(int index)
            {
                return inData[Math.Clamp(index, 0, inData.Count - 1)];
            }

            double fStep = inData[1].X - inData[0].X;

            for (int i = 0; i < inData.Count; i++)
            {
                var centerPoint = Sample(i);
                double frequence = centerPoint.X;

                double halfDeltaFrequence = Math.Max(frequence * (fk - 1), fStep * a);

                int win = (int)Math.Max(2, Math.Ceiling(halfDeltaFrequence / fStep));

                double weightAcc = 0;
                double resAcc = 0;

                for (int smpl = Math.Max(i - win, 0); smpl <= i + win; smpl++)
                {
                    DataPoint samplePoint = Sample(smpl);
                    double weight = LanczosKernel((frequence - samplePoint.X) / halfDeltaFrequence, a);

                    resAcc += samplePoint.Y * weight;

                    weightAcc += weight;
                }

                double filtredVal = 0;

                filtredVal = weightAcc > 1e-12 ? resAcc / weightAcc : 0;

                outList.Add(new DataPoint(frequence, filtredVal));
            }

            return outList;
        }
    }
}
