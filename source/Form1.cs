using System.Windows.Forms;
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
        Autocorrelation,
        TimeAlignment
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
        private readonly TimeAlignmentOptions timeAlignmentOptions = new();
        private readonly PlotModelFactory plotModelFactory;
        private readonly ModeController modeController;
        private readonly ChromeTitleBarController titleBarController;
        private readonly LiveSpectrumController liveSpectrumController;
        private readonly TimeAlignmentPanelController timeAlignmentController;
        private readonly MainCommandController commandController;
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
            commandController = new MainCommandController(
                buttonSave,
                buttonLoad,
                buttonDraw,
                buttonClear,
                buttonCurrentModeSettings,
                () => modeController.ActiveTab,
                () => liveSpectrumController.InProgress,
                CanDrawCurrentMeasurement,
                () => plotView1.Model?.Series.Count > 0,
                () => IsHandleCreated);

            measurementSettings.ApplyTo(
                expSweepMeasurement,
                frequencyResponseOptions,
                phaseResponseOptions,
                groupDelayOptions,
                impulseResponseOptions,
                waterfallGenOptions,
                burstDecayGenOptions,
                timeAlignmentOptions);
            timeAlignmentController = new TimeAlignmentPanelController(
                this,
                expSweepMeasurement,
                timeAlignmentOptions,
                text => buttonRecord.Text = text,
                SaveMeasurementSettings,
                () => modeController.ActiveTab == ModeTab.TimeAlignment);
            liveSpectrumController.ConfigureFrom(expSweepMeasurement);
            commandController.Initialize();

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
                        commandController.SetSaveAvailable(true);
                        commandController.SetLoadAvailable(true);
                    }
                    else
                    {
                        buttonRecord.Text = expSweepMeasurement.LastError == null ? "Aborted" : "Error";
                        //buttonRecord.BackColor = Color.FromArgb(255, 192, 192);
                        hasCurrentImpulseResponse = false;
                        commandController.SetSaveAvailable(false);
                        commandController.SetLoadAvailable(true);
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
            _ = SelectModeAsync(ModeTab.Frequency);
        }

        public async Task ChangeModeAsync(Mode mode)
        {
            if (expSweepMeasurement.InProgress)
            {
                await expSweepMeasurement.AbortAsync();
            }
            if (timeAlignmentController.InProgress)
            {
                await timeAlignmentController.AbortAsync();
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
            if (CurrentMode == Mode.TimeAlignment)
            {
                await timeAlignmentController.ToggleAsync();
                return;
            }

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
                commandController.SetSaveAvailable(false);
                commandController.SetLoadAvailable(false);
                UpdateDrawButtonText();
            }
        }

        private void DrawSelectedMode(bool includeCurves)
        {
            switch (modeController.ActiveTab)
            {
                case ModeTab.Impulse:
                    ShowPlotModel(
                        plotModelFactory.CreateImpulseResponse(includeCurves),
                        includeCurves,
                        showOverlay: true);
                    break;
                case ModeTab.Frequency:
                    ShowPlotModel(
                        plotModelFactory.CreateFrequencyResponse(includeCurves),
                        includeCurves,
                        showOverlay: true);
                    break;
                case ModeTab.Phase:
                    ShowPlotModel(
                        plotModelFactory.CreatePhaseResponse(includeCurves),
                        includeCurves,
                        showOverlay: true);
                    break;
                case ModeTab.GroupDelay:
                    ShowPlotModel(
                        plotModelFactory.CreateGroupDelay(includeCurves),
                        includeCurves,
                        showOverlay: true);
                    break;
                case ModeTab.Waterfall:
                    ShowPlotModel(
                        plotModelFactory.CreateWaterfall(includeCurves),
                        includeCurves,
                        showOverlay: false);
                    break;
                case ModeTab.Burst:
                    ShowPlotModel(
                        plotModelFactory.CreateBurstDecay(includeCurves),
                        includeCurves,
                        showOverlay: false);
                    break;
                case ModeTab.LiveSpectrum:
                    ShowPlotModel(
                        plotModelFactory.CreateLiveSpectrum(),
                        includeCurves: false,
                        showOverlay: false);
                    break;
                case ModeTab.Autocorrelation:
                    ShowPlotModel(
                        plotModelFactory.CreateAutocorrelation(includeCurves),
                        includeCurves,
                        showOverlay: true);
                    break;
            }

            UpdateClearButtonState();
        }

        private void ShowPlotModel(
            PlotModel model,
            bool includeCurves,
            bool showOverlay)
        {
            plotView1.Model = model;

            if (includeCurves && showOverlay)
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
            ShowModeOptions(
                new WaterfallOptions(),
                opt => opt.Init(expSweepMeasurement, waterfallGenOptions),
                opt => opt.SetOptions(waterfallGenOptions));
        }

        private void buttonFROpt_Click(object sender, EventArgs e)
        {
            ShowModeOptions(
                new FROptions(),
                opt => opt.Init(expSweepMeasurement, frequencyResponseOptions),
                opt => opt.SetOptions(frequencyResponseOptions));
        }

        private void buttonBurstDecayOpt_Click(object sender, EventArgs e)
        {
            ShowModeOptions(
                new BDOpt(),
                opt => opt.Init(expSweepMeasurement, burstDecayGenOptions),
                opt => opt.SetOptions(burstDecayGenOptions));
        }

        private void buttonGDOpt_Click(object sender, EventArgs e)
        {
            ShowModeOptions(
                new GDOpt(),
                opt => opt.Init(expSweepMeasurement, groupDelayOptions),
                opt => opt.SetOptions(groupDelayOptions));
        }

        private void buttonPROpt_Click(object sender, EventArgs e)
        {
            ShowModeOptions(
                new PROpt(),
                opt => opt.Init(expSweepMeasurement, phaseResponseOptions),
                opt => opt.SetOptions(phaseResponseOptions));
        }

        private void buttonImpOpt_Click(object sender, EventArgs e)
        {
            ShowModeOptions(
                new IROpt(),
                opt => opt.Init(expSweepMeasurement, impulseResponseOptions),
                opt => opt.SetOptions(impulseResponseOptions));
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
                burstDecayGenOptions,
                timeAlignmentOptions);
            measurementSettings.Save();
        }

        private DialogResult ShowSettingsDialog(Form dialog)
        {
            dialog.StartPosition = FormStartPosition.CenterParent;
            return dialog.ShowDialog(this);
        }

        private void ShowModeOptions<TDialog>(
            TDialog dialog,
            Action<TDialog> initialize,
            Action<TDialog> apply)
            where TDialog : Form
        {
            using (dialog)
            {
                initialize(dialog);
                if (ShowSettingsDialog(dialog) != DialogResult.OK)
                {
                    return;
                }

                apply(dialog);
                SaveMeasurementSettings();
            }
        }

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

        private Task SelectModeAsync(ModeTab tab) => modeController.SelectAsync(tab);

        private Dictionary<ModeTab, Action> CreateModeTabActions() =>
            new()
            {
                [ModeTab.Impulse] = () => _ = SelectModeAsync(ModeTab.Impulse),
                [ModeTab.Frequency] = () => _ = SelectModeAsync(ModeTab.Frequency),
                [ModeTab.Phase] = () => _ = SelectModeAsync(ModeTab.Phase),
                [ModeTab.GroupDelay] = () => _ = SelectModeAsync(ModeTab.GroupDelay),
                [ModeTab.Waterfall] = () => _ = SelectModeAsync(ModeTab.Waterfall),
                [ModeTab.Burst] = () => _ = SelectModeAsync(ModeTab.Burst),
                [ModeTab.LiveSpectrum] = () => _ = SelectModeAsync(ModeTab.LiveSpectrum),
                [ModeTab.Autocorrelation] = () => _ = SelectModeAsync(ModeTab.Autocorrelation),
                [ModeTab.TimeAlignment] = () => _ = SelectModeAsync(ModeTab.TimeAlignment)
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
            PlotViewVisible();
            OverlayVisible();
            TimeAlignmentPanelVisible();
        }

        private void UpdateDrawButtonText()
        {
            commandController.UpdateDrawButton();
        }

        private void PlotViewVisible()
        {
            plotView1.Visible = modeController.ActiveTab != ModeTab.TimeAlignment;
        }

        private void OverlayVisible()
        {
            overlays.Visible = 
                modeController.ActiveTab != ModeTab.TimeAlignment &&
                modeController.ActiveTab != ModeTab.Burst &&
                modeController.ActiveTab != ModeTab.Waterfall;
        }

        private void TimeAlignmentPanelVisible()
        {
            timeAlignmentController.SetVisible(modeController.ActiveTab == ModeTab.TimeAlignment);
        }

        private void UpdateClearButtonState()
        {
            commandController.UpdateClearButton();
        }

        private bool CanDrawCurrentMeasurement() =>
            hasCurrentImpulseResponse && !expSweepMeasurement.InProgress;

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
            commandController.UpdateModeSettingsButton();
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
                timeAlignmentController.AbortAsync(),
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
            timeAlignmentController.Dispose();
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

                commandController.FreezeSaveLoadDraw();
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
                    commandController.SetSaveAvailable(true);
                    commandController.SetLoadAvailable(true);
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

                commandController.SetSaveAvailable(false);
                commandController.SetLoadAvailable(false);
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
                    commandController.SetSaveAvailable(
                        expSweepMeasurement.ImpulseResponse != null);
                    commandController.SetLoadAvailable(true);
                    UpdateDrawButtonText();
                }
            }
        }

        private async void buttonDraw_Click(object sender, EventArgs e)
        {
            if (modeController.ActiveTab == ModeTab.LiveSpectrum)
            {
                await liveSpectrumController.ToggleAsync();
                return;
            }

            if (commandController.IsDrawFrozen)
            {
                return;
            }

            DrawSelectedMode(includeCurves: true);
        }
    }
}
