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
        private readonly PlotModelFactory plotModelFactory;
        private readonly ModeController modeController;
        private readonly ChromeTitleBarController titleBarController;
        private readonly LiveSpectrumController liveSpectrumController;
        private readonly MeasurementSettingsFile measurementSettings;
        private bool hasCurrentImpulseResponse;
        private bool closingPrepared;
        private bool resourcesDisposed;

        public Form1()
        {
            InitializeComponent();
            measurementSettings = MeasurementSettingsFile.LoadOrDefault();
            titleBarController = new ChromeTitleBarController(
                this,
                plotView1,
                UpdateMaximizedBounds,
                CreateModeTabActions());
            overlayCollection = new OverlayCollection(this, overlays, plotView1, toolTip1);
            plotModelFactory = new PlotModelFactory(
                expSweepMeasurement,
                noiseMeasurement,
                calibration,
                frequencyResponseOptions,
                phaseResponseOptions,
                groupDelayOptions,
                impulseResponseOptions,
                waterfallGenOptions,
                burstDecayGenOptions);
            liveSpectrumController = new LiveSpectrumController(
                this,
                noiseMeasurement,
                plotView1,
                plotModelFactory,
                overlays,
                overlayCollection,
                () => CurrentMode,
                () => SelectModeAsync(ModeTab.LiveSpectrum),
                UpdateOverlayAvailability,
                UpdateDrawButtonText,
                UpdateClearButtonState);
            modeController = new ModeController(
                ChangeModeAsync,
                SetActiveModeTab,
                overlayCollection.HideAll,
                DrawSelectedMode,
                CanDrawCurrentMeasurement,
                UpdateDrawButtonText);

            measurementSettings.ApplyTo(
                expSweepMeasurement,
                frequencyResponseOptions,
                phaseResponseOptions,
                groupDelayOptions,
                impulseResponseOptions,
                waterfallGenOptions,
                burstDecayGenOptions);
            liveSpectrumController.ConfigureFrom(expSweepMeasurement);
            SetButtonFrozen(buttonSave, true);
            SetButtonFrozen(buttonLoad, false);
            SetButtonFrozen(buttonDraw, true);

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
                        //buttonRecord.BackColor = Color.FromArgb(192, 255, 192);
                        hasCurrentImpulseResponse = true;
                        SetButtonFrozen(buttonSave, false);
                        SetButtonFrozen(buttonLoad, false);
                    }
                    else
                    {
                        buttonRecord.Text = expSweepMeasurement.LastError == null ? "Aborted" : "Error";
                        //buttonRecord.BackColor = Color.FromArgb(255, 192, 192);
                        hasCurrentImpulseResponse = false;
                        SetButtonFrozen(buttonSave, true);
                        SetButtonFrozen(buttonLoad, false);
                    }
                    UpdateDrawButtonText();

                    if (success && CurrentMode != Mode.LiveSpectrum)
                    {
                        DrawSelectedMode(true);
                    }

                    UpdateClearButtonState();
                });
            };

            FormClosing += Form1_FormClosing;
            buttonFR_Click(this, new EventArgs());
        }

        public async Task ChangeModeAsync(Mode mode)
        {
            if (expSweepMeasurement.InProgress)
            {
                await expSweepMeasurement.AbortAsync();
            }

            await liveSpectrumController.AbortAsync();

            CurrentMode = mode;
            plotView1.Model = null;
            UpdateClearButtonState();

            if (OverlayCollection.SupportsMode(mode))
            {
                overlayCollection.Prepare(mode);
            }

            UpdateOverlayAvailability();
        }

        private async void buttonRecord_Click(object sender, EventArgs e)
        {
            if (liveSpectrumController.InProgress)
            {
                await liveSpectrumController.AbortAsync();
            }

            if (expSweepMeasurement.InProgress)
            {
                await expSweepMeasurement.AbortAsync();
            }
            else
            {
                buttonRecord.Text = "Running...";
                hasCurrentImpulseResponse = false;
                _ = expSweepMeasurement.RunAsync();
                //buttonRecord.BackColor = Color.FromArgb(192, 255, 255);
                SetButtonFrozen(buttonSave, true);
                SetButtonFrozen(buttonLoad, true);
                UpdateDrawButtonText();
            }
        }

        private async void buttonFR_Click(object sender, EventArgs e) =>
            await SelectModeAsync(ModeTab.Frequency);

        private async void buttonPR_Click(object sender, EventArgs e) =>
            await SelectModeAsync(ModeTab.Phase);

        private async void buttonWaterfall_Click(object sender, EventArgs e) =>
            await SelectModeAsync(ModeTab.Waterfall);

        private async void buttonGD_Click(object sender, EventArgs e) =>
            await SelectModeAsync(ModeTab.GroupDelay);

        private async void buttonBurstDecay_Click(object sender, EventArgs e) =>
            await SelectModeAsync(ModeTab.Burst);

        private async void buttonIR_Click(object sender, EventArgs e) =>
            await SelectModeAsync(ModeTab.Impulse);

        private Task SelectModeAsync(ModeTab tab) =>
            modeController.SelectAsync(tab);

        private void DrawSelectedMode(bool includeCurves)
        {
            switch (modeController.ActiveTab)
            {
                case ModeTab.Impulse:
                    DrawImpulseResponse(includeCurves);
                    break;
                case ModeTab.Frequency:
                    DrawFrequencyResponse(includeCurves);
                    break;
                case ModeTab.Phase:
                    DrawPhaseResponse(includeCurves);
                    break;
                case ModeTab.GroupDelay:
                    DrawGroupDelay(includeCurves);
                    break;
                case ModeTab.Waterfall:
                    DrawWaterfall(includeCurves);
                    break;
                case ModeTab.Burst:
                    DrawBurstDecay(includeCurves);
                    break;
                case ModeTab.LiveSpectrum:
                    plotView1.Model = plotModelFactory.CreateLiveSpectrum();
                    break;
                case ModeTab.Autocorrelation:
                    DrawAutocorrelation(includeCurves);
                    break;
            }

            UpdateClearButtonState();
        }

        private void DrawFrequencyResponse(bool includeCurves)
        {
            plotView1.Model =
                plotModelFactory.CreateFrequencyResponse(includeCurves);

            if (includeCurves)
            {
                overlayCollection.Show(CurrentMode);
            }
        }

        private void DrawPhaseResponse(bool includeCurves)
        {
            plotView1.Model = plotModelFactory.CreatePhaseResponse(includeCurves);

            if (includeCurves)
            {
                overlayCollection.Show(CurrentMode);
            }
        }

        private void DrawWaterfall(bool includeCurves)
        {
            plotView1.Model = plotModelFactory.CreateWaterfall(includeCurves);
        }

        private void DrawGroupDelay(bool includeCurves)
        {
            plotView1.Model = plotModelFactory.CreateGroupDelay(includeCurves);

            if (includeCurves)
            {
                overlayCollection.Show(CurrentMode);
            }
        }

        private void DrawBurstDecay(bool includeCurves)
        {
            plotView1.Model = plotModelFactory.CreateBurstDecay(includeCurves);
        }

        private void DrawImpulseResponse(bool includeCurves)
        {
            plotView1.Model =
                plotModelFactory.CreateImpulseResponse(includeCurves);

            if (includeCurves)
            {
                overlayCollection.Show(CurrentMode);
            }
        }

        private void DrawAutocorrelation(bool includeCurves)
        {
            plotView1.Model =
                plotModelFactory.CreateAutocorrelation(includeCurves);

            if (includeCurves)
            {
                overlayCollection.Show(CurrentMode);
            }
        }

        private void buttonRecordOpt_Click(object sender, EventArgs e)
        {
            using var opt = new MeasurementOptions();
            opt.Init(expSweepMeasurement);

            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                try
                {
                    opt.SetOptions(expSweepMeasurement);
                    liveSpectrumController.ConfigureFrom(expSweepMeasurement);
                    SaveMeasurementSettings();
                }
                catch (InvalidOperationException exception)
                {
                    MessageBox.Show(
                        this,
                        exception.Message,
                        "Measurement Options",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private void buttonWaterfallOpt_Click(object sender, EventArgs e)
        {
            using var opt = new WaterfallOptions();
            opt.Init(expSweepMeasurement, waterfallGenOptions);

            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(waterfallGenOptions);
                SaveMeasurementSettings();
            }
        }

        private void buttonFROpt_Click(object sender, EventArgs e)
        {
            using var opt = new FROptions();
            opt.Init(expSweepMeasurement, frequencyResponseOptions);
            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(frequencyResponseOptions);
                SaveMeasurementSettings();
            }
        }

        private void buttonBurstDecayOpt_Click(object sender, EventArgs e)
        {
            using var opt = new BDOpt();
            opt.Init(expSweepMeasurement, burstDecayGenOptions);

            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(burstDecayGenOptions);
                SaveMeasurementSettings();
            }
        }

        private void buttonGDOpt_Click(object sender, EventArgs e)
        {
            using var opt = new GDOpt();
            opt.Init(expSweepMeasurement, groupDelayOptions);
            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(groupDelayOptions);
                SaveMeasurementSettings();
            }
        }

        private void buttonPROpt_Click(object sender, EventArgs e)
        {
            using var opt = new PROpt();
            opt.Init(expSweepMeasurement, phaseResponseOptions);
            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(phaseResponseOptions);
                SaveMeasurementSettings();
            }
        }

        private void buttonImpOpt_Click(object sender, EventArgs e)
        {
            using var opt = new IROpt();
            opt.Init(expSweepMeasurement, impulseResponseOptions);
            if (ShowSettingsDialog(opt) == DialogResult.OK)
            {
                opt.SetOptions(impulseResponseOptions);
                SaveMeasurementSettings();
            }
        }

        private void SaveMeasurementSettings()
        {
            measurementSettings.CaptureFrom(
                expSweepMeasurement,
                frequencyResponseOptions,
                phaseResponseOptions,
                groupDelayOptions,
                impulseResponseOptions,
                waterfallGenOptions,
                burstDecayGenOptions);
            measurementSettings.Save();
        }

        private DialogResult ShowSettingsDialog(Form dialog)
        {
            dialog.StartPosition = FormStartPosition.CenterParent;
            return dialog.ShowDialog(this);
        }

        private async void buttonNoise_Click(object sender, EventArgs e) =>
            await SelectModeAsync(ModeTab.LiveSpectrum);

        private async void buttonClear_Click(object sender, EventArgs e)
        {
            if (CurrentMode == Mode.LiveSpectrum &&
                liveSpectrumController.InProgress)
            {
                await liveSpectrumController.AbortAsync();
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
            UpdateClearButtonState();
            UpdateOverlayAvailability();
        }

        private void UpdateOverlayAvailability()
        {
            bool available = OverlayCollection.SupportsMode(CurrentMode);
            if (CurrentMode == Mode.LiveSpectrum)
            {
                available &= !liveSpectrumController.InProgress &&
                    !liveSpectrumController.TimerEnabled;
            }

            overlays.Enabled = available;
            if (!available)
            {
                overlayCollection.HideAll();
            }
        }

        private async void buttonGetAutocorrelation_Click(object sender, EventArgs e) =>
            await SelectModeAsync(ModeTab.Autocorrelation);

        private Dictionary<ModeTab, Action> CreateModeTabActions() =>
            new()
            {
                [ModeTab.Impulse] = () => buttonIR_Click(this, EventArgs.Empty),
                [ModeTab.Frequency] = () => buttonFR_Click(this, EventArgs.Empty),
                [ModeTab.Phase] = () => buttonPR_Click(this, EventArgs.Empty),
                [ModeTab.GroupDelay] = () => buttonGD_Click(this, EventArgs.Empty),
                [ModeTab.Waterfall] = () => buttonWaterfall_Click(this, EventArgs.Empty),
                [ModeTab.Burst] = () => buttonBurstDecay_Click(this, EventArgs.Empty),
                [ModeTab.LiveSpectrum] = () => buttonNoise_Click(this, EventArgs.Empty),
                [ModeTab.Autocorrelation] = () =>
                    buttonGetAutocorrelation_Click(this, EventArgs.Empty)
            };

        private void UpdateMaximizedBounds()
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
        }

        private void SetActiveModeTab(ModeTab activeTab)
        {
            titleBarController.SetActiveModeTab(activeTab);
            UpdateCurrentModeSettingsButton();
            UpdateDrawButtonText();
        }

        private void UpdateDrawButtonText()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            buttonDraw.Text = modeController.ActiveTab == ModeTab.LiveSpectrum
                ? liveSpectrumController.InProgress ? "Stop Live" : "Start Live"
                : "Restore Curves";
            SetButtonFrozen(buttonDraw, ShouldFreezeDrawButton());
        }

        private void UpdateClearButtonState()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            bool hasCurves = plotView1.Model?.Series.Count > 0;
            SetButtonFrozen(buttonClear, !hasCurves);
        }

        private bool CanDrawCurrentMeasurement() =>
            hasCurrentImpulseResponse && !expSweepMeasurement.InProgress;

        private bool ShouldFreezeDrawButton()
        {
            if (modeController.ActiveTab == ModeTab.LiveSpectrum)
            {
                return false;
            }

            return !CanDrawCurrentMeasurement();
        }

        private void buttonCurrentModeSettings_Click(object sender, EventArgs e)
        {
            switch (modeController.ActiveTab)
            {
                case ModeTab.Impulse:
                    buttonImpOpt_Click(sender, e);
                    break;
                case ModeTab.Frequency:
                    buttonFROpt_Click(sender, e);
                    break;
                case ModeTab.Phase:
                    buttonPROpt_Click(sender, e);
                    break;
                case ModeTab.GroupDelay:
                    buttonGDOpt_Click(sender, e);
                    break;
                case ModeTab.Waterfall:
                    buttonWaterfallOpt_Click(sender, e);
                    break;
                case ModeTab.Burst:
                    buttonBurstDecayOpt_Click(sender, e);
                    break;
                case ModeTab.LiveSpectrum:
                case ModeTab.Autocorrelation:
                    System.Media.SystemSounds.Beep.Play();
                    break;
            }
        }

        private void UpdateCurrentModeSettingsButton()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            string modeName = modeController.ActiveTab switch
            {
                ModeTab.Impulse => "Impulse",
                ModeTab.Frequency => "Frequency",
                ModeTab.Phase => "Phase",
                ModeTab.GroupDelay => "Group Delay",
                ModeTab.Waterfall => "Waterfall",
                ModeTab.Burst => "Burst",
                ModeTab.LiveSpectrum => "Live Spectrum",
                ModeTab.Autocorrelation => "Autocorrelation",
                _ => "Mode"
            };
            bool hasSettings = modeController.ActiveTab is not (
                ModeTab.LiveSpectrum or
                ModeTab.Autocorrelation);
            /*
            buttonCurrentModeSettings.Text = hasSettings
                ? $"{modeName} Settings..."
                : "No Settings";*/
            SetButtonFrozen(buttonCurrentModeSettings, !hasSettings);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == ChromeTitleBarController.WmNcHitTest &&
                WindowState != FormWindowState.Maximized)
            {
                base.WndProc(ref m);
                if ((int)m.Result == ChromeTitleBarController.HtClient)
                {
                    Point point = PointToClient(
                        ChromeTitleBarController.GetPointFromLParam(m.LParam));
                    m.Result = ChromeTitleBarController.GetResizeHitTest(
                        point,
                        ClientSize);
                }
                return;
            }

            base.WndProc(ref m);
        }

        private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (closingPrepared)
            {
                return;
            }

            e.Cancel = true;
            Enabled = false;
            await Task.WhenAll(
                expSweepMeasurement.AbortAsync(),
                liveSpectrumController.AbortAsync());

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
            expSweepMeasurement.Dispose();
            liveSpectrumController.Dispose();
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
                SetButtonFrozen(buttonDraw, true);
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
                    //buttonRecord.BackColor = Color.FromArgb(192, 255, 192);
                    hasCurrentImpulseResponse = true;
                    await SelectModeAsync(ModeTab.Impulse);
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
                    UpdateDrawButtonText();
                }
            }
        }

        private static void SetButtonFrozen(Button button, bool frozen)
        {
            if (frozen)
            {
                button.Enabled = false;
                button.BackColor = Color.FromArgb(55, 60, 70);
                button.ForeColor = Color.FromArgb(120, 125, 135);
            }
            else
            {
                button.BackColor = Color.FromArgb(50,  55,  80);
                button.ForeColor = Color.FromArgb(255, 255, 255);
                button.Enabled = true;
            }
        }

        private async void buttonDraw_Click(object sender, EventArgs e)
        {
            if (modeController.ActiveTab == ModeTab.LiveSpectrum)
            {
                await liveSpectrumController.ToggleAsync();
                return;
            }

            if (ShouldFreezeDrawButton())
            {
                return;
            }

            DrawSelectedMode(includeCurves: true);
        }
    }
}
