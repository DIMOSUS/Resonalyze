using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// A real panel driven through its controls, read by what the session holds, what it draws and what it tells the host.
/// The session and its readers have their own tests; these pin the wiring between the fields and the session, which
/// only the panel path exercises.
/// </summary>
public sealed class EqWizardPanelWiringTests
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int SampleRate = 48_000;

    private static readonly PhaseAnalysisSettings GateTemplate = new(
        PhaseWindowMode.Fixed,
        PhaseAnalysisSettings.DefaultFdwCycles,
        PhaseDetrendMode.Off,
        ManualDetrendMilliseconds: 0.0,
        GateOffsetMs: 0.0,
        LeftMs: 2.0,
        PlateauMs: 12.0,
        RightMs: 5.0,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);

    [Fact]
    public void TheAutoTuneFields_AreWhatTheFitIsGiven() => StaTest.Run(() =>
    {
        using var live = new LivePanel();

        live.Set<ThemedNumericUpDown>("numericQMax", box => box.Value = 2.5m);
        live.Set<CheckBox>("checkBoxCutsOnly", box => box.Checked = false);
        live.Set<CheckBox>("checkBoxShelves", box => box.Checked = true);
        live.Set<ThemedComboBox>("comboBoxBandsLimit", box => box.SelectedItem = 8);
        live.Set<ThemedNumericUpDown>("numericGainMin", box => box.Value = -9m);
        live.Set<ThemedNumericUpDown>("numericGainMax", box => box.Value = 3m);

        Assert.Equal(new EqAutoTunePolicy(8, -9, 3, 2.5, false, true), live.Panel.CurrentAutoTunePolicy);
    });

    [Fact]
    public void TheWindowFields_MoveTheFitWindowAndItsMarks() => StaTest.Run(() =>
    {
        using var live = new LivePanel();

        live.Set<ThemedNumericUpDown>("numericToHz", box => box.Value = 300m);
        // Past the upper edge: the upper edge is pushed along, and the field shows where it went.
        live.Set<ThemedNumericUpDown>("numericFromHz", box => box.Value = 400m);

        Assert.Equal((400.0, 401.0), live.Session.FrequencyWindow);
        Assert.Equal(401m, live.Control<ThemedNumericUpDown>("numericToHz").Value);
        Assert.Equal(
            [400.0, 401.0],
            live.Plot.Annotations.OfType<LineAnnotation>()
                .Where(line => line.LineStyle == LineStyle.Dash)
                .Select(line => line.X)
                .Order());
    });

    [Fact]
    public void TypingIntoAStrip_EditsTheBankAsTheFieldShowsIt_AndLandsAsOneStep() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Set<ThemedComboBox>("darkComboBoxBands", box => box.SelectedItem = 2);
        Assert.Equal(2, live.Session.Bank.Bands.Count);
        Assert.Equal(2, live.Strips.Count);

        live.Change(() => live.Strips[1].QInput.Value = 2.25m);
        live.Change(() => live.Strips[1].GainInput.Value = -3.5m);
        live.Change(() => live.Control<ThemedNumericUpDown>("NumericGain").Value = -1.5m);

        // Typed into the field: half away from zero, as the field itself rounds it.
        Assert.Equal(new PeqBand(20, 2.3, -3.5), live.Session.Bank.Bands[1]);
        Assert.Equal(-1.5, live.Session.Bank.PreampDb);

        live.Panel.CommitPendingBankEdit();

        Assert.True(live.Control<Button>("buttonUndo").Enabled);
        live.Click("buttonUndo");
        Assert.Equal(new PeqBand(20, 1, 0), live.Session.Bank.Bands[1]);
        Assert.Equal(1.0m, live.Strips[1].QInput.Value);
        Assert.Equal(0m, live.Control<ThemedNumericUpDown>("NumericGain").Value);
        Assert.True(live.Control<Button>("buttonRedo").Enabled);
    });

    [Fact]
    public void NarrowingMaxBoost_ClampsTheStrips_AndTheBankWithThem() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Set<ThemedComboBox>("darkComboBoxBands", box => box.SelectedItem = 1);
        live.Change(() => live.Strips[0].GainInput.Value = 6m);
        live.Panel.CommitPendingBankEdit();

        live.Set<ThemedNumericUpDown>("numericGainMax", box => box.Value = 3m);

        Assert.Equal(3m, live.Strips[0].GainInput.Maximum);
        Assert.Equal(3m, live.Strips[0].GainInput.Value);
        Assert.Equal(3, live.Session.Bank.Bands[0].GainDb);
    });

    [Fact]
    public void AddingAndSelectingABand_MarksItOnThePlot() => StaTest.Run(() =>
    {
        using var live = new LivePanel();

        live.Invoke("AddBand", PeqBandType.HighShelf);

        Assert.Equal(PeqBandType.HighShelf, Assert.Single(live.Session.Bank.Bands).Type);
        Assert.Equal(PeqBandType.HighShelf, live.Strips[0].BandType);
        LineAnnotation guide = Assert.Single(
            live.Plot.Annotations.OfType<LineAnnotation>(), line => line.LineStyle == LineStyle.Dot);
        Assert.Equal(live.Session.Bank.Bands[0].FrequencyHz, guide.X);
    });

    [Fact]
    public void TheViewToggles_ReachTheSessionAndThePlot() => StaTest.Run(() =>
    {
        using var live = new LivePanel();

        live.Set<CheckBox>("checkBoxEqCurve", box => box.Checked = false);
        Assert.False(live.Session.ShowEqCurve);
        Assert.DoesNotContain("EQ", EqWizardTestPlots.CurveTitles(live.Plot));

        live.Set<CheckBox>("checkBoxEqPhase", box => box.Checked = true);
        Assert.True(live.Session.PhaseMode);
        Assert.Equal("Phase (°)", live.Plot.Axes.First(axis => axis.Key == EqWizardPlot.EqGainAxisKey).Title);

        live.Set<CheckBox>("checkBoxBypass", box => box.Checked = true);
        Assert.True(live.Session.Bypass);
    });

    [Fact]
    public void TheSelectorsWithoutASource_AreTheUsersOwn_AndReachTheHost() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Panel.ConfigureCalibration(
            _ => null,
            [new MicrophoneCalibrationEntry("mic-a", "Mic A", Available: true)]);
        live.Set<ThemedComboBox>("darkComboBoxBands", box => box.SelectedItem = 1);
        int raised = live.SettingsRaised;

        live.Set<ThemedComboBox>("comboBoxSampleRate", box => box.SelectedItem = 96_000);
        live.Set<ThemedComboBox>("comboBoxQConvention", box => box.SelectedItem = PeqQConvention.Symmetric);
        live.Set<ThemedComboBox>("comboBoxCalibration", box => box.SelectedIndex = 1);
        live.Set<ThemedNumericUpDown>("NumericTargetOffset", box => box.Value = 5m);

        Assert.Equal(96_000, live.Session.ProcessorSampleRateHz);
        Assert.Equal(96_000, live.Strips[0].SampleRateHz);
        Assert.Equal(PeqQConvention.Symmetric, live.Panel.ManualQConvention);
        Assert.Equal("mic-a", live.Session.PreferredIrCalibrationId);
        MeasurementSettingsFile.EqWizardSettings saved = live.Panel.CaptureSettings();
        Assert.Equal(96_000, saved.ManualSampleRateHz);
        Assert.Equal("mic-a", saved.CalibrationId);
        Assert.Equal(5, saved.TargetOffsetDb);
        Assert.True(live.SettingsRaised >= raised + 4, $"raised {live.SettingsRaised - raised} times for four edits");
    });

    [Fact]
    public void RestoredSettings_AreWhatTheFieldsShow_AndNotAnEditToSave() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        MeasurementSettingsFile.EqWizardSettings settings = live.Panel.CaptureSettings();
        settings.GainMinDb = -9;
        settings.GainMaxDb = 3;
        settings.AutoTuneMaxQ = 3.5;
        settings.CutsOnly = false;
        settings.ShowEqCurve = false;
        settings.TargetOffsetDb = -30;
        settings.PreampDb = -2;
        settings.Bands = [new MeasurementSettingsFile.PeqBandSettings { FrequencyHz = 250, Q = 2, GainDb = 5, Type = PeqBandType.Peaking }];
        int raised = live.SettingsRaised;

        live.Panel.ApplyPersistedSettings(settings);
        live.Settle();

        Assert.Equal(raised, live.SettingsRaised);
        Assert.Equal(-9m, live.Control<ThemedNumericUpDown>("numericGainMin").Value);
        Assert.Equal(3m, live.Control<ThemedNumericUpDown>("numericGainMax").Value);
        Assert.Equal(3.5m, live.Control<ThemedNumericUpDown>("numericQMax").Value);
        Assert.Equal(-30m, live.Control<ThemedNumericUpDown>("NumericTargetOffset").Value);
        Assert.Equal(-2m, live.Control<ThemedNumericUpDown>("NumericGain").Value);
        Assert.False(live.Control<CheckBox>("checkBoxCutsOnly").Checked);
        Assert.False(live.Control<CheckBox>("checkBoxEqCurve").Checked);
        // Max Boost bounds the restored band as it bounds a typed one.
        PeqSlotControl strip = Assert.Single(live.Strips);
        Assert.Equal(3m, strip.GainInput.Value);
        Assert.False(live.Control<Button>("buttonUndo").Enabled);
    });

    [Fact]
    public void AHandoff_LocksItsProcessor_NarrowsTheLevel_AndReturnsTheEditedBank() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        (VirtualDspEqReturnToken Token, EqualizationCurve Bank, double Level)? returned = null;
        live.Panel.ReturnPeqRequested = (token, bank, level) => returned = (token, bank, level);
        VirtualDspEqHandoffRequest request = Handoff(DspProcessorProfile.Custom(96_000, PeqQConvention.Symmetric));

        live.Panel.BeginVirtualDspHandoff(request);
        live.Settle();

        Assert.Same(request.Token, live.Session.HandoffToken);
        Assert.False(live.Control<ThemedComboBox>("comboBoxSampleRate").Enabled);
        Assert.Equal(96_000, live.Control<ThemedComboBox>("comboBoxSampleRate").SelectedItem);
        Assert.False(live.Control<ThemedComboBox>("comboBoxQConvention").Enabled);
        Assert.Equal(PeqQConvention.Symmetric, live.Panel.TargetDspQConvention);
        var level = live.Control<ThemedNumericUpDown>("NumericTargetOffset");
        Assert.Equal(-120m, level.Minimum);
        Assert.Equal(-41m, level.Value);

        live.Invoke("AddBand", PeqBandType.Peaking);
        // Typed and still in the field: Return lands it before sending the bank.
        live.Strips[^1].GainInput.Controls.OfType<TextBox>().Single().Text = "-4.5";
        live.Click("buttonReturnToDsp");

        Assert.NotNull(returned);
        Assert.Same(request.Token, returned.Value.Token);
        Assert.Equal(-41, returned.Value.Level);
        Assert.Equal(live.Session.Bank.Curve.Bands, returned.Value.Bank.Bands);
        Assert.Equal(new PeqBand(1_000, 5, -4.5), returned.Value.Bank.Bands[^1]);
        Assert.Null(live.Session.HandoffToken);
        Assert.Equal(EqWizardLimits.TargetOffset.Minimum, level.Minimum);
    });

    [Fact]
    public void AGatedSource_DrawsItsCorrectedCurveOnceItLands() => StaTest.Run(() =>
    {
        using var live = new LivePanel();

        live.Panel.BeginVirtualDspHandoff(Handoff(DspProcessorProfile.Custom(SampleRate, PeqQConvention.Rbj)));
        live.Settle();

        Assert.True(live.Session.Source!.IsGated);
        Assert.Contains("Source + EQ", EqWizardTestPlots.CurveTitles(live.Plot));
        Assert.NotNull(live.Results);
    });

    private static VirtualDspEqHandoffRequest Handoff(DspProcessorProfile profile)
    {
        var impulseResponse = new Complex[4_096];
        for (int i = 0; i < 64; i++)
        {
            impulseResponse[480 + i] = Math.Exp(-i / 12.0) * Math.Cos(2 * Math.PI * i / 16.0);
        }

        var channel = new VirtualCrossoverChannel("A")
        {
            SampleRate = SampleRate,
            TransferImpulseResponse = impulseResponse,
            TransferPeakIndex = 480
        };
        channel.Settings.CrossoverKind = CrossoverKind.BandPass;
        channel.Settings.HighPassEdge = channel.Settings.HighPassEdge with { FrequencyHz = 80 };
        channel.Settings.LowPassEdge = channel.Settings.LowPassEdge with { FrequencyHz = 500 };
        return VirtualDspEqHandoff.Build(
            channel,
            channel.ActiveRight,
            withChain: true,
            profile,
            GateTemplate,
            pinnedGateOffsetMs: null,
            renderAnchorIndex: 480,
            phaseContext: null,
            targetLevelDb: -41,
            targetLevelMinDb: -120,
            targetLevelMaxDb: 60,
            smoothingInverseOctaves: 0,
            calibration: null,
            calibrationName: null,
            SpatialAverageCalibration.Specific(null),
            projectGeneration: 1,
            spatialAverage: null,
            spatialAverageOffsetDb: 0)!;
    }

    private sealed class LivePanel : IDisposable
    {
        private readonly Form host;

        public LivePanel()
        {
            Panel = new EqWizardPanel
            {
                Dock = DockStyle.Fill,
                ResultsChanged = stats => Results = stats
            };
            Panel.SettingsChanged += () => SettingsRaised++;
            // Shown off screen, as the tool is shown: gated previews wait for the handles.
            host = new Form
            {
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                ClientSize = new Size(1_400, 900)
            };
            host.Controls.Add(Panel);
            host.Show();
            Settle();
        }

        public EqWizardPanel Panel { get; }

        public EqWizardSession Session => Panel.Session;

        public EqTuneStats? Results { get; private set; }

        public int SettingsRaised { get; private set; }

        public IReadOnlyList<PeqSlotControl> Strips => Control<List<PeqSlotControl>>("peqSlots");

        public PlotModel Plot => Control<PlotView>("plotWizard").Model!;

        public T Control<T>(string name) => (T)typeof(EqWizardPanel).GetField(name, Hidden)!.GetValue(Panel)!;

        public void Set<T>(string name, Action<T> change) => Change(() => change(Control<T>(name)));

        public void Change(Action change)
        {
            KeepUiContext();
            change();
            Settle();
        }

        public void Invoke(string method, params object[] arguments) =>
            Change(() => typeof(EqWizardPanel).GetMethod(method, Hidden)!.Invoke(Panel, arguments));

        public void Click(string button) => Change(() => Control<Button>(button).PerformClick());

        public void Settle()
        {
            for (int attempt = 0; attempt < 2_000 && Session.Previews.Rendering; attempt++)
            {
                Pump();
                Thread.Sleep(5);
            }

            // The redraw a landed render asks for is posted after it; let it run.
            for (int pass = 0; pass < 5; pass++)
            {
                Pump();
            }

            Assert.False(Session.Previews.Rendering, "A preview did not land in time.");
        }

        public void Dispose() => host.Dispose();

        private static void Pump()
        {
            KeepUiContext();
            Application.DoEvents();
            KeepUiContext();
        }

        // DoEvents leaves a plain SynchronizationContext behind once a form has been shown, where the app's message loop
        // keeps the WinForms one: a render's continuation would then run on the thread pool and create a field's handle
        // there, and disposing the form would wait on that thread for ever.
        private static void KeepUiContext()
        {
            if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }
        }
    }
}
