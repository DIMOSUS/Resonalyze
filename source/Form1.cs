using System.Diagnostics.Metrics;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.Providers.LinearAlgebra;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze
{
    public enum Mode : int
    {
        None = 0,
        ImpulseResponse,
        FrequencyResponse,
        PhaseResponse,
        GroupDelay,
        CumulativeSpectrumDecay,
        BurstDecay,
        LiveSpectrum,
        Autocorrelation
    }

    public partial class Form1 : Form
    {
        public Mode CurrentMode { get; private set; }

        private readonly OverlayCollection overlayCollection;
        private readonly ExpSweepMeasurement expSweepMeasurement = new();
        private readonly NoiseMeasurement noiseMeasurement = new();
        private readonly System.Windows.Forms.Timer noiseGraphTimer = new() { Interval = 100 };
        private readonly CalibrationFile calibration = new(
            Path.Combine(AppContext.BaseDirectory, "calibration.txt"));
        private readonly WaterfallGenerateOptions waterfallGenOptions = new()
        {
            WaterfallMode = WaterfallMode.Fourier,
        };
        private readonly WaterfallGenerateOptions burstDecayGenOptions = new()
        {
            WaterfallMode = WaterfallMode.BurstDecay,
            Window = 1024,
            LeftTukeyWindow = 8,
            RightTukeyWindow = 128,
            SmoothingInverseOctaves = 6,
        };

        private readonly FrequencyResponseOptions frequencyResponseOptions = new();
        private readonly FrequencyResponseOptions phaseResponseOptions = new()
        {
            Window = 2048,
            LeftTukeyWindow = 16,
            RightTukeyWindow = 256,
            SmoothingInverseOctaves = 12,
            Offset = 0,
        };
        private readonly FrequencyResponseOptions groupDelayOptions = new()
        {
            Window = 2048,
            LeftTukeyWindow = 16,
            RightTukeyWindow = 256,
            SmoothingInverseOctaves = 12,
            Offset = 0,
        };
        private readonly ImpulseResponseOptions impulseResponseOptions = new();
        private bool closingPrepared;
        private bool resourcesDisposed;

        public Form1()
        {
            InitializeComponent();
            overlayCollection = new OverlayCollection(this, overlays, plotView1, toolTip1);

            expSweepMeasurement.Init(12, 44100, 24, 1.0, PlaybackChannel.Mono);
            SetButtonFrozen(buttonSave, true);
            SetButtonFrozen(buttonLoad, false);

            expSweepMeasurement.Completed += (bool success) =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }
                BeginInvoke((MethodInvoker)delegate
                {
                    if (success)
                    {
                        buttonRecord.Text = "Ready";
                        buttonRecord.BackColor = Color.FromArgb(192, 255, 192);
                        SetButtonFrozen(buttonSave, false);
                        SetButtonFrozen(buttonLoad, false);
                    }
                    else
                    {
                        buttonRecord.Text = expSweepMeasurement.LastError == null ? "Aborted" : "Error";
                        buttonRecord.BackColor = Color.FromArgb(255, 192, 192);
                        SetButtonFrozen(buttonSave, true);
                        SetButtonFrozen(buttonLoad, false);
                    }
                });
            };

            noiseMeasurement.Init(44100, 24, 60, PlaybackChannel.Mono, 2048);
            noiseMeasurement.Completed += (bool success) =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }
                BeginInvoke((MethodInvoker)delegate
                {
                    noiseGraphTimer.Stop();
                    buttonNoise.Text = "Live Spectrum";
                    UpdateOverlayAvailability();
                });
            };

            noiseGraphTimer.Tick += NoiseGraphTimer_Tick;
            FormClosing += Form1_FormClosing;
            buttonFR_Click(this, new EventArgs());
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

            if (OverlayCollection.SupportsMode(mode))
            {
                overlayCollection.Prepare(mode);
            }

            if (mode == Mode.LiveSpectrum)
            {
                overlays.Enabled = false;
                overlayCollection.HideAll();
            }
            else
            {
                UpdateOverlayAvailability();
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
                buttonRecord.BackColor = Color.FromArgb(192, 255, 255);
                SetButtonFrozen(buttonSave, true);
                SetButtonFrozen(buttonLoad, true);
            }
        }

        private async void buttonFR_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.FrequencyResponse);

            var model = new PlotModel { Title = "Frequency Response" };

            if (expSweepMeasurement.ImpulseResponse != null && !expSweepMeasurement.InProgress)
            {
                IReadOnlyList<AnalysisCurve> curves =
                    DataHelper.GetSpectrum(expSweepMeasurement, frequencyResponseOptions, calibration);
                foreach (AnalysisCurve curve in curves)
                {
                    model.Series.Add(OxyPlotAdapter.ToLineSeries(curve));
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

            overlayCollection.Show(CurrentMode);
        }

        private async void buttonPR_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.PhaseResponse);

            var model = new PlotModel { Title = "Phase Response" };

            if (expSweepMeasurement.ImpulseResponse != null && !expSweepMeasurement.InProgress)
            {
                AnalysisCurve curve = DataHelper.GetPhase(expSweepMeasurement,
                    phaseResponseOptions.Window, phaseResponseOptions.LeftTukeyWindow, phaseResponseOptions.RightTukeyWindow, phaseResponseOptions.Offset, phaseResponseOptions.SmoothingInverseOctaves, phaseResponseOptions.Unwrap);
                model.Series.Add(OxyPlotAdapter.ToLineSeries(curve));
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

            overlayCollection.Show(CurrentMode);
        }

        private async void buttonWaterfall_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.CumulativeSpectrumDecay);

            if (expSweepMeasurement.ImpulseResponse != null && !expSweepMeasurement.InProgress)
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
                        Minimum = waterfallGenOptions.DbRange,
                        Maximum = -waterfallGenOptions.DbRange,
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

            if (expSweepMeasurement.ImpulseResponse != null && !expSweepMeasurement.InProgress)
            {
                AnalysisCurve curve = DataHelper.GetGroupDelay(
                    expSweepMeasurement,
                    groupDelayOptions.Window,
                    groupDelayOptions.LeftTukeyWindow,
                    groupDelayOptions.RightTukeyWindow,
                    groupDelayOptions.Offset,
                    groupDelayOptions.SmoothingInverseOctaves);
                model.Series.Add(OxyPlotAdapter.ToLineSeries(curve));
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

            overlayCollection.Show(CurrentMode);
        }

        private async void buttonBurstDecay_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.CumulativeSpectrumDecay);

            if (expSweepMeasurement.ImpulseResponse != null && !expSweepMeasurement.InProgress)
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
                        Minimum = burstDecayGenOptions.DbRange,
                        Maximum = -burstDecayGenOptions.DbRange,
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
            await ChangeModeAsync(Mode.ImpulseResponse);

            var model = new PlotModel { Title = "Impulse Response" };

            if (expSweepMeasurement.ImpulseResponse != null && !expSweepMeasurement.InProgress)
            {
                AnalysisCurve curve =
                    DataHelper.GetImpulse(expSweepMeasurement, impulseResponseOptions);
                model.Series.Add(OxyPlotAdapter.ToLineSeries(curve));
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

            overlayCollection.Show(CurrentMode);
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
            opt.Init(expSweepMeasurement, frequencyResponseOptions);
            if (opt.ShowDialog() == DialogResult.OK)
            {
                opt.SetOptions(frequencyResponseOptions);
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
            opt.Init(expSweepMeasurement, groupDelayOptions);
            if (opt.ShowDialog() == DialogResult.OK)
            {
                opt.SetOptions(groupDelayOptions);
            }
        }

        private void buttonPROpt_Click(object sender, EventArgs e)
        {
            PROpt opt = new PROpt();
            opt.Init(expSweepMeasurement, phaseResponseOptions);
            if (opt.ShowDialog() == DialogResult.OK)
            {
                opt.SetOptions(phaseResponseOptions);
            }
        }

        private void buttonImpOpt_Click(object sender, EventArgs e)
        {
            IROpt opt = new IROpt();
            opt.Init(expSweepMeasurement, impulseResponseOptions);
            if (opt.ShowDialog() == DialogResult.OK)
            {
                opt.SetOptions(impulseResponseOptions);
            }
        }

        private async void buttonNoise_Click(object sender, EventArgs e)
        {
            bool wasRunning = noiseMeasurement.InProgress;
            await ChangeModeAsync(Mode.LiveSpectrum);
            double[]? finalSnapshot = wasRunning ? noiseMeasurement.GetAccumulatedSpectrumSnapshot() : null;

            var model = new PlotModel { Title = "Live Spectrum" };

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
                UpdateOverlayAvailability();
                overlayCollection.Show(CurrentMode);
                return;
            }

            overlays.Enabled = false;
            overlayCollection.HideAll();
            buttonNoise.Text = "Live Spectrum (Running)";
            _ = noiseMeasurement.RunAsync();
            noiseGraphTimer.Start();
        }

        private void NoiseGraphTimer_Tick(object? sender, EventArgs e)
        {
            double[]? snapshot = noiseMeasurement.GetAccumulatedSpectrumSnapshot();
            PlotModel? model = plotView1.Model;
            if (snapshot == null || model?.Title != "Live Spectrum")
            {
                return;
            }

            model.Series.Clear();
            model.Series.Add(BuildNoiseSeries(snapshot));
            model.InvalidatePlot(true);
        }

        private async void buttonClear_Click(object sender, EventArgs e)
        {
            if (CurrentMode == Mode.LiveSpectrum &&
                noiseMeasurement.InProgress)
            {
                noiseGraphTimer.Stop();
                await noiseMeasurement.AbortAsync();
                buttonNoise.Text = "Live Spectrum";
            }

            overlayCollection.HideAll();

            PlotModel? model = plotView1.Model;
            if (model == null)
            {
                return;
            }

            model.Series.Clear();
            model.InvalidatePlot(true);
            plotView1.Refresh();
            UpdateOverlayAvailability();
        }

        private void UpdateOverlayAvailability()
        {
            bool available = OverlayCollection.SupportsMode(CurrentMode);
            if (CurrentMode == Mode.LiveSpectrum)
            {
                available &= !noiseMeasurement.InProgress &&
                    !noiseGraphTimer.Enabled;
            }

            overlays.Enabled = available;
            if (!available)
            {
                overlayCollection.HideAll();
            }
        }

        private LineSeries BuildNoiseSeries(double[] accumulatedData)
        {
            int length = noiseMeasurement.SequenceLength;
            int binCount = Math.Min(length / 2, accumulatedData.Length);
            List<DataPoint> data = new(binCount);

            for (int i = 1; i < binCount; i++)
            {
                double frequency = i * ((double)noiseMeasurement.SampleRate / length);
                data.Add(new DataPoint(frequency, DataHelper.AmplitudeToDecibels(accumulatedData[i]) - 21.0));
            }

            List<SignalPoint> resampled = DataHelper.LogarithmicResample(
                OxyPlotAdapter.ToSignalPoints(data),
                20,
                20000,
                1024,
                calibration,
                1.0 / 6.0);
            var series = new LineSeries
            {
                Color = OxyColor.FromRgb(255, 0, 127),
                Title = "Live Spectrum"
            };
            series.Points.AddRange(OxyPlotAdapter.ToDataPoints(resampled));
            return series;
        }

        private async void buttonGetAutocorrelation_Click(object sender, EventArgs e)
        {
            await ChangeModeAsync(Mode.Autocorrelation);

            var model = new PlotModel { Title = "Autocorrelation" };

            if (expSweepMeasurement.ImpulseResponse != null && !expSweepMeasurement.InProgress)
            {
                AnalysisCurve curve =
                    DataHelper.GetAutocorrelation(expSweepMeasurement, impulseResponseOptions);
                model.Series.Add(OxyPlotAdapter.ToLineSeries(curve));
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

            overlayCollection.Show(CurrentMode);
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

        private async void buttonSave_Click(object sender, EventArgs e)
        {
            if (expSweepMeasurement.ImpulseResponse != null && !expSweepMeasurement.InProgress)
            {
                using var dialog = new SaveFileDialog
                {
                    AddExtension = true,
                    DefaultExt = "json",
                    Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
                    FileName = $"Resonalyze-IR-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json",
                    InitialDirectory = Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments),
                    RestoreDirectory = true,
                    Title = "Save impulse response"
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                SetButtonFrozen(buttonSave, true);
                SetButtonFrozen(buttonLoad, true);
                try
                {
                    ImpulseResponseFile file =
                        ImpulseResponseFile.Capture(expSweepMeasurement);
                    await file.SaveAsync(dialog.FileName);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        this,
                        $"Failed to save the impulse response.\r\n\r\n{exception.Message}",
                        "Save failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                finally
                {
                    SetButtonFrozen(buttonSave, false);
                    SetButtonFrozen(buttonLoad, false);
                }
            }
        }

        private async void buttonLoad_Click(object sender, EventArgs e)
        {
            if (!expSweepMeasurement.InProgress)
            {
                using var dialog = new OpenFileDialog
                {
                    CheckFileExists = true,
                    Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
                    InitialDirectory = Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments),
                    Multiselect = false,
                    RestoreDirectory = true,
                    Title = "Load impulse response"
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                SetButtonFrozen(buttonSave, true);
                SetButtonFrozen(buttonLoad, true);
                try
                {
                    ImpulseResponseFile file =
                        await ImpulseResponseFile.LoadAsync(dialog.FileName);
                    expSweepMeasurement.RestoreImpulseResponse(
                        file.Octaves,
                        file.SampleRate,
                        file.Bits,
                        file.SweepDurationSeconds,
                        file.PlayChannel,
                        file.GetImpulseResponse(),
                        file.PeakIndex);

                    buttonRecord.Text = "Loaded";
                    buttonRecord.BackColor = Color.FromArgb(192, 255, 192);
                    buttonIR_Click(this, EventArgs.Empty);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        this,
                        $"Failed to load the impulse response.\r\n\r\n{exception.Message}",
                        "Load failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                finally
                {
                    SetButtonFrozen(
                        buttonSave,
                        expSweepMeasurement.ImpulseResponse == null);
                    SetButtonFrozen(buttonLoad, false);
                }
            }
        }

        private static void SetButtonFrozen(Button button, bool frozen)
        {
            if (frozen)
            {
                button.Enabled = false;
                button.BackColor = Color.LightGray;
                button.ForeColor = Color.DarkGray;
            }
            else
            {
                button.BackColor = SystemColors.Control;
                button.ForeColor = SystemColors.ControlText;
                button.Enabled = true;
            }
        }
    }
}
