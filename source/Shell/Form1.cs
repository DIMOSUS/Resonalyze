using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
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
        private const string PeakInfoAnnotationTag = "PeakInfoAnnotation";

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
        private readonly PlotLabelsPanelController plotLabelsPanelController;
        private readonly InputLevelMeterController inputLevelMeterController;
        private readonly DockedModeSettingsHost dockedModeSettingsHost;
        private bool hasCurrentImpulseResponse;
        private bool closingPrepared;
        private bool resourcesDisposed;

        public Form1()
        {
            InitializeComponent();
            toolTip1.SetToolTip(
                inputLevelMeterPanel,
                "Input level meter.\r\n" +
                "Numbers are shown as Peak / RMS in dBFS.\r\n" +
                "The bar shows the filtered RMS level.\r\n" +
                "The bright vertical marker is Peak Hold.");
            measurementSettings = MeasurementSettingsFile.LoadOrDefault();
            titleBarController = new ChromeTitleBarController(
                this,
                plotView1,
                UpdateMaximizedBounds,
                CreateModeTabActions());
            overlayCollection = new OverlayCollection(
                this,
                overlays,
                plotView1,
                toolTip1,
                UpdatePlotLabelsPanel);
            plotLabelsPanelController = new PlotLabelsPanelController(
                plotView1,
                () => CurrentMode);
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
                UpdateRecordButtonForCurrentMode,
                UpdateClearButtonState,
                UpdatePlotLabelsPanel);
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
            inputLevelMeterController = new InputLevelMeterController(
                this,
                inputLevelMeterPanel,
                expSweepMeasurement,
                noiseMeasurement,
                timeAlignmentController.Measurement);
            dockedModeSettingsHost = new DockedModeSettingsHost(this, plotView1);
            liveSpectrumController.ConfigureFrom(expSweepMeasurement);
            commandController.Initialize();
            UpdatePeakInfo();

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
                    UpdatePeakInfo();
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
            UpdatePlotLabelsPanel();

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

            if (CurrentMode == Mode.LiveSpectrum)
            {
                await liveSpectrumController.ToggleAsync();
                UpdateRecordButtonForCurrentMode();
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
                UpdatePeakInfo();
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
            UpdatePeakInfo();

            if (includeCurves && showOverlay)
            {
                overlayCollection.Show(CurrentMode);
            }
            else
            {
                UpdatePlotLabelsPanel();
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
                    timeAlignmentController.RefreshConfiguration();
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
            ToggleModeOptions(
                ModeTab.Waterfall,
                () => new WaterfallOptions(),
                opt => opt.Init(expSweepMeasurement, waterfallGenOptions),
                opt => opt.SetOptions(waterfallGenOptions));
        }

        private void buttonFROpt_Click(object sender, EventArgs e)
        {
            ToggleModeOptions(
                ModeTab.Frequency,
                () => new FROptions(),
                opt => opt.Init(expSweepMeasurement, frequencyResponseOptions),
                opt => opt.SetOptions(frequencyResponseOptions));
        }

        private void buttonBurstDecayOpt_Click(object sender, EventArgs e)
        {
            ToggleModeOptions(
                ModeTab.Burst,
                () => new BDOpt(),
                opt => opt.Init(expSweepMeasurement, burstDecayGenOptions),
                opt => opt.SetOptions(burstDecayGenOptions));
        }

        private void buttonGDOpt_Click(object sender, EventArgs e)
        {
            ToggleModeOptions(
                ModeTab.GroupDelay,
                () => new GDOpt(),
                opt => opt.Init(expSweepMeasurement, groupDelayOptions),
                opt => opt.SetOptions(groupDelayOptions));
        }

        private void buttonPROpt_Click(object sender, EventArgs e)
        {
            ToggleModeOptions(
                ModeTab.Phase,
                () => new PROpt(),
                opt => opt.Init(expSweepMeasurement, phaseResponseOptions),
                opt => opt.SetOptions(phaseResponseOptions));
        }

        private void buttonImpOpt_Click(object sender, EventArgs e)
        {
            ToggleModeOptions(
                ModeTab.Impulse,
                () => new IROpt(),
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

        private void ToggleModeOptions<TDialog>(
            ModeTab tab,
            Func<TDialog> create,
            Action<TDialog> initialize,
            Action<TDialog> apply)
            where TDialog : Form
        {
            dockedModeSettingsHost.Toggle(
                tab,
                create,
                initialize,
                dialog =>
                {
                    IReadOnlyList<AxisViewport> axisViewports = CaptureAxisViewports();
                    apply(dialog);
                    SaveMeasurementSettings();
                    RefreshCurrentModePlot();
                    RestoreAxisViewports(axisViewports);
                });
        }

        private void RefreshCurrentModePlot()
        {
            bool includeCurves =
                modeController.ActiveTab is not (ModeTab.LiveSpectrum or ModeTab.TimeAlignment) &&
                CanDrawCurrentMeasurement();
            DrawSelectedMode(includeCurves);
        }

        private IReadOnlyList<AxisViewport> CaptureAxisViewports()
        {
            PlotModel? model = plotView1.Model;
            if (model == null)
            {
                return Array.Empty<AxisViewport>();
            }

            var viewports = new List<AxisViewport>(model.Axes.Count);
            foreach (Axis axis in model.Axes)
            {
                viewports.Add(new AxisViewport(
                    axis.Position,
                    axis.GetType(),
                    axis.ActualMinimum,
                    axis.ActualMaximum));
            }

            return viewports;
        }

        private void RestoreAxisViewports(IReadOnlyList<AxisViewport> viewports)
        {
            if (viewports.Count == 0 || plotView1.Model == null)
            {
                return;
            }

            foreach (Axis axis in plotView1.Model.Axes)
            {
                AxisViewport? viewport = viewports.FirstOrDefault(
                    item => item.Position == axis.Position &&
                        item.AxisType == axis.GetType());
                if (viewport == null)
                {
                    continue;
                }

                axis.Zoom(viewport.Minimum, viewport.Maximum);
            }

            plotView1.Model.InvalidatePlot(false);
            plotView1.Refresh();
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
                UpdatePlotLabelsPanel();
                return;
            }

            model.Series.Clear();
            model.InvalidatePlot(true);
            plotView1.Refresh();
            UpdateClearButtonState();
            UpdateOverlayAvailability();
            UpdatePlotLabelsPanel();
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

        private static bool HasDockedModeSettings(ModeTab tab) =>
            tab is
                ModeTab.Impulse or
                ModeTab.Frequency or
                ModeTab.Phase or
                ModeTab.GroupDelay or
                ModeTab.Waterfall or
                ModeTab.Burst;

        private void ShowDockedModeSettingsForActiveTab()
        {
            switch (modeController.ActiveTab)
            {
                case ModeTab.Impulse:
                    buttonImpOpt_Click(this, EventArgs.Empty);
                    break;
                case ModeTab.Frequency:
                    buttonFROpt_Click(this, EventArgs.Empty);
                    break;
                case ModeTab.Phase:
                    buttonPROpt_Click(this, EventArgs.Empty);
                    break;
                case ModeTab.GroupDelay:
                    buttonGDOpt_Click(this, EventArgs.Empty);
                    break;
                case ModeTab.Waterfall:
                    buttonWaterfallOpt_Click(this, EventArgs.Empty);
                    break;
                case ModeTab.Burst:
                    buttonBurstDecayOpt_Click(this, EventArgs.Empty);
                    break;
            }
        }

        private void SyncDockedModeSettingsOnModeChange()
        {
            if (!dockedModeSettingsHost.IsOpen)
            {
                return;
            }

            if (HasDockedModeSettings(modeController.ActiveTab))
            {
                ShowDockedModeSettingsForActiveTab();
            }
            else
            {
                dockedModeSettingsHost.Close();
            }
        }

        private void UpdateMaximizedBounds()
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
        }

        private void SetActiveModeTab(ModeTab activeTab)
        {
            titleBarController.SetActiveModeTab(activeTab);
            UpdateCurrentModeSettingsButton();
            UpdateDrawButtonText();
            UpdateRecordButtonForCurrentMode();
            PlotViewVisible();
            OverlayVisible();
            TimeAlignmentPanelVisible();
            SyncDockedModeSettingsOnModeChange();
            UpdatePlotLabelsPanel();
        }

        private void UpdateDrawButtonText()
        {
            commandController.UpdateDrawButton();
        }

        private void UpdateRecordButtonForCurrentMode()
        {
            if (modeController.ActiveTab != ModeTab.LiveSpectrum)
            {
                return;
            }

            buttonRecord.Text = liveSpectrumController.InProgress ? "Stop" : "Start";
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

        private void UpdatePlotLabelsPanel()
        {
            plotLabelsPanelController.Refresh();
        }

        private void UpdatePeakInfo()
        {
            PlotModel? model = plotView1.Model;
            if (model == null)
            {
                return;
            }

            for (int index = model.Annotations.Count - 1; index >= 0; index--)
            {
                if (model.Annotations[index] is OverlayTextAnnotation
                    {
                        Tag: PeakInfoAnnotationTag
                    })
                {
                    model.Annotations.RemoveAt(index);
                }
            }

            if (modeController.ActiveTab is not (ModeTab.Phase or ModeTab.GroupDelay))
            {
                model.InvalidatePlot(false);
                return;
            }

            string transferPeak = expSweepMeasurement.TransferImpulseResponse == null
                ? "--"
                : expSweepMeasurement.TransferPeakIndex.ToString();
            string text = expSweepMeasurement.InProgress ? "Peaks: measuring..." : "Transfer IR Peak: " + transferPeak + " samples";
            model.Annotations.Add(new OverlayTextAnnotation
            {
                Tag = PeakInfoAnnotationTag,
                Text = text,
                TextPosition = new DataPoint(0.01, 0),
                TextFlowDirection = TextFlowDirection.TopDown,
                FontSize = 12,
                FontWeight = 700,
                TextColor = OxyColors.White,
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Left
            });
            model.InvalidatePlot(false);
        }

        private bool CanDrawCurrentMeasurement() =>
            hasCurrentImpulseResponse && !expSweepMeasurement.InProgress;

        private void buttonCurrentModeSettings_Click(object sender, EventArgs e)
        {
            if (!HasDockedModeSettings(modeController.ActiveTab))
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            ShowDockedModeSettingsForActiveTab();
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
            dockedModeSettingsHost.Dispose();
            inputLevelMeterController.Dispose();
            expSweepMeasurement.Dispose();
            timeAlignmentController.Dispose();
            liveSpectrumController.Dispose();
        }

        private async void buttonSave_Click(object sender, EventArgs e)
        {
            if (expSweepMeasurement.HasImpulseResponse && !expSweepMeasurement.InProgress)
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
                        file.GetSweepDeconvolutionImpulseResponse(),
                        file.SweepDeconvolutionPeakIndex,
                        file.MeasurementMode,
                        file.GetTransferImpulseResponse(),
                        file.TransferPeakIndex);
                    liveSpectrumController.ConfigureFrom(expSweepMeasurement);
                    timeAlignmentController.RefreshConfiguration();
                    SaveMeasurementSettings();

                    //buttonRecord.Text = "Loaded";
                    //buttonRecord.BackColor = Color.FromArgb(192, 255, 192);
                    hasCurrentImpulseResponse = true;
                    UpdatePeakInfo();
                    RefreshCurrentModePlot();
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
                        expSweepMeasurement.HasImpulseResponse);
                    commandController.SetLoadAvailable(true);
                    UpdatePeakInfo();
                    UpdateDrawButtonText();
                }
            }
        }

        private async void buttonDraw_Click(object sender, EventArgs e)
        {
            if (commandController.IsDrawFrozen)
            {
                return;
            }

            DrawSelectedMode(includeCurves: true);
        }

        private sealed record AxisViewport(
            AxisPosition Position,
            Type AxisType,
            double Minimum,
            double Maximum);
    }
}
