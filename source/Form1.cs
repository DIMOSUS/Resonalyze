using NAudio.Wave;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot;
using System.Numerics;
using System.Diagnostics.Metrics;
using MathNet.Numerics.IntegralTransforms;
using OxyPlot.WindowsForms;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using Resonalyze.Options;
using MathNet.Numerics.Providers.LinearAlgebra;
using MathNet.Numerics;
using System.Threading;
using System.Xml.Linq;
using Resonalyze.Dsp;

namespace Resonalyze
{
    public enum Mode : int
    {
        None = 0,
        ImpulseResponce,
        FrequencyResponse,
        PhaseResponse,
        GroupDelay,
        CumulativeSpectrumDecay,
        BurstDecay,
        Noise,
        Autocorrelation
    }

    public partial class Form1 : Form
    {
        public Mode CurrentMode { get; private set; }

        public OverlayCollection Overlays;

        ExpSweepMeasurement expSweepMeasurement = new ExpSweepMeasurement();
        NoiseMeasurement noiseMeasurement = new NoiseMeasurement();
        readonly System.Windows.Forms.Timer noiseGraphTimer = new() { Interval = 100 };
        bool closingPrepared;
        bool resourcesDisposed;

        CalibrationFile Calibration = new CalibrationFile("calibration.txt");

        WterfallGenerateOptions waterfallGenOptions = new WterfallGenerateOptions()
        {
            WaterfallMode = WaterfallMode.Fourier,
        };
        WterfallGenerateOptions burstDecayGenOptions = new WterfallGenerateOptions()
        {
            WaterfallMode = WaterfallMode.BurstDecay,
            Window = 1024,
            LeftTukeyWindow = 8,
            RightTukeyWindow = 128,
            SmothInvOctaves = 6,
        };

        FRGenerateOptions fRGenOptions = new FRGenerateOptions();
        FRGenerateOptions pRGenOptions = new FRGenerateOptions()
        {
            Window = 2048,
            LeftTukeyWindow = 16,
            RightTukeyWindow = 256,
            SmothInvOctaves = 12,
            Offset = 0,
        };
        FRGenerateOptions gDGenOptions = new FRGenerateOptions()
        {
            Window = 2048,
            LeftTukeyWindow = 16,
            RightTukeyWindow = 256,
            SmothInvOctaves = 12,
            Offset = 0,
        };
        IRGenerateOptions iRGenerateOptions = new IRGenerateOptions();

        public Form1()
        {
            InitializeComponent();
            Overlays = new OverlayCollection(this, overlays, plotView1);

            expSweepMeasurement.Init(12, 44100, 24, 1.0, Chanels.Mono);

            expSweepMeasurement.CompleteNotify += (bool Succes) =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }
                BeginInvoke((MethodInvoker)delegate {
                    if (Succes)
                    {
                        buttonRecord.Text = "Ready";
                    }
                    else
                    {
                        buttonRecord.Text = expSweepMeasurement.LastError == null ? "Aborted" : "Error";
                    }
                });
            };

            noiseMeasurement.Init(44100, 24, 60, Chanels.Mono, 2048);
            noiseMeasurement.CompleteNotify += (bool Succes) =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }
                BeginInvoke((MethodInvoker)delegate
                {
                    noiseGraphTimer.Stop();
                    buttonNoise.Text = "Noise (Stoped)";
                });
            };

            noiseGraphTimer.Tick += NoiseGraphTimer_Tick;
            FormClosing += Form1_FormClosing;
            buttonFR_Click(this, new EventArgs());
            /*
            var enumerator = new MMDeviceEnumerator();
            foreach (var wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.All))
            {
                Console.WriteLine($"{wasapi.DataFlow} {wasapi.FriendlyName} {wasapi.DeviceFriendlyName} {wasapi.State}");
            }*/
        }

        public async Task ChangeModeAsync(Mode mode)
        {
            noiseGraphTimer.Stop();
            if (expSweepMeasurement.InProgress)
            {
                await expSweepMeasurement.AbortAsync();
            }

            if (noiseMeasurement.InProgress)
            {
                await noiseMeasurement.AbortAsync();
            }

            CurrentMode = mode;
            plotView1.Model = null;

            if (mode == Mode.CumulativeSpectrumDecay)
            {
                overlays.Enabled = false;
            }
            else
            {
                Overlays.Prepare(mode);

                overlays.Enabled = true;
            }
        }

        private async void buttonRecord_Click(object sender, EventArgs e)
        {
            if (noiseMeasurement.InProgress)
            {
                await noiseMeasurement.AbortAsync();
            }

            if (expSweepMeasurement.InProgress)
            {
                await expSweepMeasurement.AbortAsync();
            }
            else
            {
                buttonRecord.Text = "Running...";
                _ = expSweepMeasurement.RunAsync();
            }
        }

        private async void buttonFR_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.FrequencyResponse);

            var model = new PlotModel { Title = "Frequency Response" };

            if (expSweepMeasurement.ImpulseResponce != null && !expSweepMeasurement.InProgress)
            {
                var series = DataHelper.GetSpectrum(expSweepMeasurement, fRGenOptions, Calibration);
                foreach (var s in series)
                {
                    model.Series.Add(s);
                }
            }

            /*
            LineSeries LS = new LineSeries();
            List<DataPoint> data = new List<DataPoint> { };

            int steps = 1000;
            for (int i = 1; i < steps; i++)
            {
                double frequence = GraphPlotter.Log10ToFrequence(i / (steps - 1.0), 20, 20000);

                data.Add(new (frequence, Calibration.dBCorrection(frequence)));
            }

            LS.Points.AddRange(data);
            LS.Color = OxyColor.FromRgb(255, 0, 127);
            LS.Title = "Micro";
            model.Series.Add(LS);
            */

            model.Axes.Add(new LogarithmicAxis
            {
                Position = AxisPosition.Bottom,
                AbsoluteMinimum = 20,
                AbsoluteMaximum = 20000,
                Minimum = 20,
                Maximum = 20000,
                IsZoomEnabled = false,
                MajorGridlineStyle = LineStyle.Solid,
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                AbsoluteMinimum = -120,
                AbsoluteMaximum = 10,
                MajorStep = 10,
                Minimum = -90,
                Maximum = 0,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                Title = "dB",
            });

            plotView1.Model = model;

            Overlays.Show(CurrentMode);
        }

        private async void buttonPR_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.PhaseResponse);

            var model = new PlotModel { Title = "Phase Response" };

            if (expSweepMeasurement.ImpulseResponce != null && !expSweepMeasurement.InProgress)
            {
                var series = DataHelper.GetPhase(expSweepMeasurement,
                    pRGenOptions.Window, pRGenOptions.LeftTukeyWindow, pRGenOptions.RightTukeyWindow, pRGenOptions.Offset, pRGenOptions.SmothInvOctaves, pRGenOptions.Unwrap);
                foreach (var s in series)
                {
                    model.Series.Add(s);
                }
            }

            model.Axes.Add(new LogarithmicAxis
            {
                Position = AxisPosition.Bottom,
                AbsoluteMinimum = 20,
                AbsoluteMaximum = 20000,
                Minimum = 20,
                Maximum = 20000,
                IsZoomEnabled = false,
                MajorGridlineStyle = LineStyle.Solid,
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                AbsoluteMinimum = -720,
                AbsoluteMaximum = 720,
                Minimum = -180,
                Maximum = 180,
                MajorStep = 45,
                MajorGridlineStyle = LineStyle.Solid,
                MinorStep = 15,
                MinorGridlineStyle = LineStyle.Dot,
            });

            plotView1.Model = model;

            Overlays.Show(CurrentMode);
        }

        private async void buttonWaterfall_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.CumulativeSpectrumDecay);

            if (expSweepMeasurement.ImpulseResponce != null && !expSweepMeasurement.InProgress)
            {
                var model = new PlotModel { Title = "Fourier Waterfall" };
                
                model.Axes.Add(new LinearAxis
                {
                    Position = AxisPosition.Left,
                    Minimum = -1.0,
                    Maximum = 1.0,
                    IsAxisVisible = false,
                    IsPanEnabled = false,
                    IsZoomEnabled = false,
                });
                var log = new LogarithmicClipAxis
                {
                    Position = AxisPosition.Bottom,
                    Minimum = 20,
                    Maximum = 60000,
                    ClipValue = 20000,
                    IsPanEnabled = false,
                    IsZoomEnabled = false,
                    //IsAxisVisible = false
                };
                model.Axes.Add(log);

                model.Axes.Add(
                    new LinearColorAxis
                    {
                        Position = AxisPosition.Left,
                        Minimum = waterfallGenOptions.dBRange,
                        Maximum = -waterfallGenOptions.dBRange,
                        //Palette = OxyPalettes.Jet(64),
                        Palette = OxyPalette.Interpolate(512, OxyColors.DarkBlue, OxyColors.Cyan, OxyColors.Yellow, OxyColors.Orange, OxyColors.DarkRed, OxyColors.White, OxyColors.White, OxyColors.White, OxyColors.White),
                        HighColor = OxyColors.Black
                    });

                var waterfall = new WaterfallSeries()
                {
                    BackgroundColor = OxyColor.FromRgb(30, 0, 50),
                    GenerateOptions = waterfallGenOptions,
                };

                waterfall.FillFourierWaterfallData(expSweepMeasurement);

                model.Series.Add(waterfall);
                plotView1.Model = model;
            }
        }

        private async void buttonGD_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.GroupDelay);

            var model = new PlotModel { Title = "Group Delay" };

            if (expSweepMeasurement.ImpulseResponce != null && !expSweepMeasurement.InProgress)
            {
                var series = DataHelper.GetGroupDelay(expSweepMeasurement, gDGenOptions.Window, gDGenOptions.LeftTukeyWindow, gDGenOptions.RightTukeyWindow, gDGenOptions.Offset, gDGenOptions.SmothInvOctaves);
                foreach (var s in series)
                {
                    model.Series.Add(s);
                }
            }

            model.Axes.Add(new LogarithmicAxis
            {
                Position = AxisPosition.Bottom,
                AbsoluteMinimum = 20,
                AbsoluteMaximum = 20000,
                Minimum = 20,
                Maximum = 20000,
                IsZoomEnabled = false,
                MajorGridlineStyle = LineStyle.Solid,
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                AbsoluteMinimum = -20,
                AbsoluteMaximum = 20,
                Minimum = -10,
                Maximum = 10,
                MajorStep = 1,
                MajorGridlineStyle = LineStyle.Solid,
                Title = "ms"
            });

            plotView1.Model = model;

            Overlays.Show(CurrentMode);
        }

        private async void buttonBurstDecay_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.CumulativeSpectrumDecay);

            if (expSweepMeasurement.ImpulseResponce != null && !expSweepMeasurement.InProgress)
            {
                var model = new PlotModel { Title = "Burst Decay" };

                model.Axes.Add(new LinearAxis
                {
                    Position = AxisPosition.Left,
                    Minimum = -1.0,
                    Maximum = 1.0,
                    IsAxisVisible = false,
                    IsPanEnabled = false,
                    IsZoomEnabled = false,
                });
                var log = new LogarithmicClipAxis
                {
                    Position = AxisPosition.Bottom,
                    Minimum = 20,
                    Maximum = 60000,
                    ClipValue = 20000,
                    IsPanEnabled = false,
                    IsZoomEnabled = false,
                    //IsAxisVisible = false
                };
                model.Axes.Add(log);

                model.Axes.Add(
                    new LinearColorAxis
                    {
                        Position = AxisPosition.Left,
                        Minimum = burstDecayGenOptions.dBRange,
                        Maximum = -burstDecayGenOptions.dBRange,
                        //Palette = OxyPalettes.Jet(64),
                        Palette = OxyPalette.Interpolate(512, OxyColors.DarkBlue, OxyColors.Cyan, OxyColors.Yellow, OxyColors.Orange, OxyColors.DarkRed, OxyColors.White, OxyColors.White, OxyColors.White, OxyColors.White),
                        HighColor = OxyColors.Black
                    });

                var waterfall = new WaterfallSeries()
                {
                    BackgroundColor = OxyColor.FromRgb(30, 0, 50),
                    GenerateOptions = burstDecayGenOptions,
                };

                waterfall.FillFourierWaterfallData(expSweepMeasurement);

                model.Series.Add(waterfall);
                plotView1.Model = model;
            }
        }

        private async void buttonIR_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.ImpulseResponce);

            var model = new PlotModel { Title = "Impulse Responce" };

            if (expSweepMeasurement.ImpulseResponce != null && !expSweepMeasurement.InProgress)
            {
                var series = DataHelper.GetImpulse(expSweepMeasurement, iRGenerateOptions);
                foreach (var s in series)
                {
                    model.Series.Add(s);
                }
            }

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                MajorGridlineStyle = LineStyle.Solid,
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
            });

            plotView1.Model = model;

            Overlays.Show(CurrentMode);
        }

        private void buttonRecordOpt_Click(object sender, EventArgs e)
        {
            MeasurementOptions opt = new MeasurementOptions();
            opt.Init(expSweepMeasurement);

            if (opt.ShowDialog() == DialogResult.OK)
            {
                opt.SetOptions(expSweepMeasurement);
            }
        }

        private void buttonWaterfallOpt_Click(object sender, EventArgs e)
        {
            WaterfallOptions opt = new WaterfallOptions();
            opt.Init(expSweepMeasurement, waterfallGenOptions);

            if (opt.ShowDialog() == DialogResult.OK)
            {
                opt.SetOptions(waterfallGenOptions);
            }
        }

        private void buttonFROpt_Click(object sender, EventArgs e)
        {
            FROptions opt = new FROptions();
            opt.Init(expSweepMeasurement, fRGenOptions);
            if (opt.ShowDialog() == DialogResult.OK)
            {
                opt.SetOptions(fRGenOptions);
            }
        }

        private void buttonBurstDecayOpt_Click(object sender, EventArgs e)
        {
            BDOpt opt = new BDOpt();
            opt.Init(expSweepMeasurement, burstDecayGenOptions);

            if (opt.ShowDialog() == DialogResult.OK)
            {
                opt.SetOptions(burstDecayGenOptions);
            }
        }

        private void buttonGDOpt_Click(object sender, EventArgs e)
        {
            GDOpt opt = new GDOpt();
            opt.Init(expSweepMeasurement, gDGenOptions);
            if (opt.ShowDialog() == DialogResult.OK)
            {
                opt.SetOptions(gDGenOptions);
            }
        }

        private void buttonPROpt_Click(object sender, EventArgs e)
        {
            PROpt opt = new PROpt();
            opt.Init(expSweepMeasurement, pRGenOptions);
            if (opt.ShowDialog() == DialogResult.OK)
            {
                opt.SetOptions(pRGenOptions);
            }
        }

        private void buttonImpOpt_Click(object sender, EventArgs e)
        {
            IROpt opt = new IROpt();
            opt.Init(expSweepMeasurement, iRGenerateOptions);
            if (opt.ShowDialog() == DialogResult.OK)
            {
                opt.SetOptions(iRGenerateOptions);
            }
        }

        private async void buttonNoise_Click(object sender, EventArgs e)
        {
            bool wasRunning = noiseMeasurement.InProgress;
            await ChangeModeAsync(Mode.FrequencyResponse);
            double[]? finalSnapshot = wasRunning ? noiseMeasurement.GetAccDataSnapshot() : null;

            var model = new PlotModel { Title = "Noise Frequency Response" };

            model.Axes.Add(new LogarithmicAxis
            {
                Position = AxisPosition.Bottom,
                AbsoluteMinimum = 20,
                AbsoluteMaximum = 20000,
                Minimum = 20,
                Maximum = 20000,
                IsZoomEnabled = false,
                MajorGridlineStyle = LineStyle.Solid,
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                AbsoluteMinimum = -120,
                AbsoluteMaximum = 10,
                MajorStep = 10,
                Minimum = -90,
                Maximum = 0,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot,
                Title = "dB",
            });

            plotView1.Model = model;

            if (wasRunning)
            {
                if (finalSnapshot != null)
                {
                    model.Series.Add(BuildNoiseSeries(finalSnapshot));
                }
                Overlays.Show(CurrentMode);
                return;
            }

            buttonNoise.Text = "Noise (Running)";
            _ = noiseMeasurement.RunAsync();
            noiseGraphTimer.Start();
        }

        private void NoiseGraphTimer_Tick(object? sender, EventArgs e)
        {
            double[]? snapshot = noiseMeasurement.GetAccDataSnapshot();
            PlotModel? model = plotView1.Model;
            if (snapshot == null || model?.Title != "Noise Frequency Response")
            {
                return;
            }

            model.Series.Clear();
            model.Series.Add(BuildNoiseSeries(snapshot));
            model.InvalidatePlot(true);
        }

        private LineSeries BuildNoiseSeries(double[] accumulatedData)
        {
            int length = noiseMeasurement.SequenceLength;
            int binCount = Math.Min(length / 2, accumulatedData.Length);
            List<DataPoint> data = new(binCount);

            for (int i = 1; i < binCount; i++)
            {
                double frequency = i * ((double)noiseMeasurement.SampleRate / length);
                data.Add(new DataPoint(frequency, DataHelper.ADCtoPdB(accumulatedData[i]) - 21.0));
            }

            data = DataHelper.LogarithmicResample(data, 20, 20000, 1024, Calibration, 1.0 / 6.0);
            var series = new LineSeries
            {
                Color = OxyColor.FromRgb(255, 0, 127),
                Title = "Noise"
            };
            series.Points.AddRange(data);
            return series;
        }

        private async void buttonGetAutocorrelation_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.Autocorrelation);

            var model = new PlotModel { Title = "Autocorrelation" };

            if (expSweepMeasurement.ImpulseResponce != null && !expSweepMeasurement.InProgress)
            {
                var series = DataHelper.GetAutocorrelation(expSweepMeasurement, iRGenerateOptions);
                foreach (var s in series)
                {
                    model.Series.Add(s);
                }
            }

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                MajorGridlineStyle = LineStyle.Solid,
                Title = "ms"
            });
            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
            });

            plotView1.Model = model;

            Overlays.Show(CurrentMode);
        }

        private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (closingPrepared)
            {
                return;
            }

            e.Cancel = true;
            Enabled = false;
            noiseGraphTimer.Stop();
            await Task.WhenAll(
                expSweepMeasurement.AbortAsync(),
                noiseMeasurement.AbortAsync());

            DisposeAppResources();
            closingPrepared = true;
            BeginInvoke((MethodInvoker)Close);
        }

        private void DisposeAppResources()
        {
            if (resourcesDisposed)
            {
                return;
            }

            resourcesDisposed = true;
            noiseGraphTimer.Stop();
            noiseGraphTimer.Tick -= NoiseGraphTimer_Tick;
            noiseGraphTimer.Dispose();
            expSweepMeasurement.Dispose();
            noiseMeasurement.Dispose();
        }
    }
}
