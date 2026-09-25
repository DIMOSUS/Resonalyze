using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Ui;

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
        live.Set<ThemedComboBox>("comboBoxBoosts", box => box.SelectedIndex = 2);
        live.Set<CheckBox>("checkBoxShelves", box => box.Checked = true);
        live.Set<ThemedComboBox>("comboBoxBandsLimit", box => box.SelectedItem = 8);
        live.Set<ThemedNumericUpDown>("numericGainMin", box => box.Value = -9m);
        live.Set<ThemedNumericUpDown>("numericGainMax", box => box.Value = 3m);

        Assert.Equal(
            new EqAutoTunePolicy(8, -9, 3, 2.5, EqAutoTuneBoosts.Allowed, true, true),
            live.Panel.CurrentAutoTunePolicy);
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
    public void TheBandMenu_LocksAndDeletes_EachAsOneStep() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Set<ThemedComboBox>("darkComboBoxBands", box => box.SelectedItem = 2);
        PeqBand second = live.Session.Bank.Bands[1];

        live.ChooseFromBandMenu(live.Strips[0], "Lock (Auto Tune keeps it)");

        Assert.True(live.Session.Bank.Bands[0].Locked);
        Assert.True(live.Strips[0].Locked);
        Assert.Equal(UiPalette.BandLockedHeader, live.Strips[0].SlotLabel.BackColor);
        Assert.NotEqual(UiPalette.BandLockedHeader, live.Strips[1].SlotLabel.BackColor);

        // An edit typed into a locked strip keeps the lock.
        live.Change(() => live.Strips[0].GainInput.Value = -2m);
        Assert.True(live.Session.Bank.Bands[0].Locked);

        live.ChooseFromBandMenu(live.Strips[0], "Delete");

        Assert.Equal([second], live.Session.Bank.Bands);
        Assert.Single(live.Strips);

        live.Click("buttonUndo");
        Assert.Equal(2, live.Strips.Count);
        Assert.True(live.Strips[0].Locked);
        Assert.Equal(-2, live.Session.Bank.Bands[0].GainDb);
        Assert.True(live.Session.Bank.Bands[0].Locked);

        live.ChooseFromBandMenu(live.Strips[0], "Lock (Auto Tune keeps it)");
        Assert.False(live.Session.Bank.Bands[0].Locked);
        Assert.NotEqual(UiPalette.BandLockedHeader, live.Strips[0].SlotLabel.BackColor);
    });

    [Fact]
    public void Del_DeletesTheFilterPickedByItsPlate_ButNotWhileAFieldIsBeingTyped() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Set<ThemedComboBox>("darkComboBoxBands", box => box.SelectedItem = 2);
        PeqBand second = live.Session.Bank.Bands[1];

        live.Change(() => live.Strips[0].FrequencyInput.Focus());
        Assert.True(live.Strips[0].FrequencyInput.ContainsFocus);
        Assert.False(live.PressDelete());
        Assert.Equal(2, live.Session.Bank.Bands.Count);

        live.PickByPlate(live.Strips[0]);
        Assert.True(live.Control<TableLayoutPanel>("peqSlotTable").Focused);
        Assert.True(live.PressDelete());
        Assert.Equal([second], live.Session.Bank.Bands);

        // The selection went with the filter: another Del has nothing to delete.
        Assert.False(live.PressDelete());
        Assert.Single(live.Session.Bank.Bands);
    });

    [Fact]
    public void Del_WhileAHandleIsHeld_DeletesNothing_AndWorksOnceItIsLetGo() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Set<ThemedComboBox>("darkComboBoxBands", box => box.SelectedItem = 2);
        PeqBand second = live.Session.Bank.Bands[1];
        // Focus off every field, as a click on the plate leaves it.
        live.PickByPlate(live.Strips[0]);
        ScreenPoint start = live.HandleCenter(0);

        live.Press(start);
        Assert.Equal(0, live.Handles.Selected);
        Assert.False(live.PressDelete());
        Assert.Equal(2, live.Session.Bank.Bands.Count);
        live.Release(start);

        Assert.True(live.PressDelete());
        Assert.Equal([second], live.Session.Bank.Bands);
    });

    [Fact]
    public void AFit_KeepsALockedBand_AndFillsOnlyTheSlotsLeft() => StaTest.Run(() =>
    {
        using var live = FitReady();
        live.Set<ThemedComboBox>("comboBoxBandsLimit", box => box.SelectedItem = 4);
        live.Invoke("AddBand", PeqBandType.Peaking);
        live.Change(() => live.Strips[0].GainInput.Value = -3m);
        live.ChooseFromBandMenu(live.Strips[0], "Lock (Auto Tune keeps it)");
        PeqBand locked = live.Session.Bank.Bands[0];

        live.Control<Button>("buttonAutoTune").PerformClick();
        live.SettleFit();

        Assert.Equal(locked, Assert.Single(live.Session.Bank.Bands, band => band.Locked));
        Assert.InRange(live.Session.Bank.Bands.Count, 2, 4);
        Assert.Contains(live.Strips, strip => strip.Locked);
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
    public void AnAddedBand_IsSelectedOnItsHandle_AndTheGuideStandsInWhereHandlesAreHidden() => StaTest.Run(() =>
    {
        using var live = new LivePanel();

        live.Invoke("AddBand", PeqBandType.HighShelf);

        Assert.Equal(PeqBandType.HighShelf, Assert.Single(live.Session.Bank.Bands).Type);
        Assert.Equal(PeqBandType.HighShelf, live.Strips[0].BandType);
        Assert.Equal(live.Session.Bank.Bands, live.Handles.Bands);
        Assert.Equal(0, live.Handles.Selected);
        Assert.DoesNotContain(live.Plot.Annotations.OfType<LineAnnotation>(), line => line.LineStyle == LineStyle.Dot);

        live.Set<CheckBox>("checkBoxEqCurve", box => box.Checked = false);

        Assert.Empty(live.Handles.Bands);
        LineAnnotation guide = Assert.Single(
            live.Plot.Annotations.OfType<LineAnnotation>(), line => line.LineStyle == LineStyle.Dot);
        Assert.Equal(live.Session.Bank.Bands[0].FrequencyHz, guide.X);
    });

    [Fact]
    public void DraggingAHandle_SelectsItsBand_MovesIt_AndLandsAsOneStep() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Invoke("AddBand", PeqBandType.Peaking);
        live.Invoke("DeselectBand");
        PeqBand added = live.Session.Bank.Bands[0];
        ScreenPoint start = live.HandleCenter(0);

        live.Press(start);
        live.Move(new ScreenPoint(start.X + 40, start.Y - 30));
        live.Release(new ScreenPoint(start.X + 40, start.Y - 30));

        PeqBand moved = live.Session.Bank.Bands[0];
        Assert.Equal(0, live.Handles.Selected);
        Assert.True(moved.FrequencyHz > added.FrequencyHz);
        Assert.True(moved.GainDb > 0);
        Assert.Equal(added.Q, moved.Q);
        Assert.Equal((decimal)moved.FrequencyHz, live.Strips[0].FrequencyInput.Value);
        Assert.Equal((decimal)moved.GainDb, live.Strips[0].GainInput.Value);

        live.Click("buttonUndo");
        Assert.Equal(added, live.Session.Bank.Bands[0]);
    });

    [Fact]
    public void TheWheelOverTheSelectedHandle_StepsItsQ_AndOverAnotherZooms() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Invoke("AddBand", PeqBandType.Peaking);
        live.Invoke("AddBand", PeqBandType.LowShelf);
        live.Invoke("SelectSlot", live.Strips[0]);
        double bellQ = live.Session.Bank.Bands[0].Q;
        double shelfQ = live.Session.Bank.Bands[1].Q;

        live.Wheel(live.HandleCenter(0));
        live.Wheel(live.HandleCenter(1));

        Assert.Equal((double)EqWizardLimits.BandQ.Clamp(bellQ * Math.Pow(2, 1.0 / 6)), live.Session.Bank.Bands[0].Q);
        Assert.Equal((decimal)live.Session.Bank.Bands[0].Q, live.Strips[0].QInput.Value);
        Assert.Equal(shelfQ, live.Session.Bank.Bands[1].Q);
    });

    [Fact]
    public void AClickOnAHandle_KeepsItsBandSelected_AndOneOnEmptyGraphDeselects() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Invoke("AddBand", PeqBandType.Peaking);
        live.Invoke("DeselectBand");

        live.ClickPlot(live.HandleCenter(0));
        Assert.Equal(0, live.Handles.Selected);

        ScreenPoint center = live.HandleCenter(0);
        live.ClickPlot(new ScreenPoint(center.X - 100, center.Y + 60));
        Assert.Null(live.Handles.Selected);
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
        settings.AutoTuneBoosts = EqAutoTuneBoosts.Off;
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
        Assert.Equal("Off", live.Control<ThemedComboBox>("comboBoxBoosts").SelectedItem);
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

    [Fact]
    public void ACorrectedCurveDroppedByANewSmoothing_IsRenderedAgain() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Panel.BeginVirtualDspHandoff(Handoff(DspProcessorProfile.Custom(SampleRate, PeqQConvention.Rbj)));

        // Before the handoff's render lands: the new width makes it stale, and it must not be the last word.
        live.Set<ThemedComboBox>("comboBoxSmooth", box => box.SelectedItem = 3);

        Assert.NotNull(live.Session.Previews.GatedPreview);
        Assert.Contains("Source + EQ", EqWizardTestPlots.CurveTitles(live.Plot));
    });

    [Fact]
    public void AHandoffsCrossover_ShapesTheTarget_AndTheWindowFollowsTheBox() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Panel.BeginVirtualDspHandoff(Handoff(DspProcessorProfile.Custom(SampleRate, PeqQConvention.Rbj)));

        // The fixture's channel is a band-pass at 80 Hz and 500 Hz.
        Assert.True(live.Control<CheckBox>("checkBoxCrossoverTarget").Checked);
        Assert.True(live.Control<CheckBox>("checkBoxCrossoverTarget").Enabled);
        Assert.True(live.Session.WindowFromHz < 80, $"From is {live.Session.WindowFromHz}.");
        Assert.True(live.Session.WindowToHz > 500, $"To is {live.Session.WindowToHz}.");
        double passband = TargetAt(live, 200);
        Assert.True(TargetAt(live, 40) < passband - 15, "the target should follow the low skirt down.");
        Assert.True(TargetAt(live, 1_000) < passband - 15, "the target should follow the high skirt down.");

        live.Set<CheckBox>("checkBoxCrossoverTarget", box => box.Checked = false);

        Assert.Equal(80m, live.Session.WindowFromHz);
        Assert.Equal(500m, live.Session.WindowToHz);
        // Flat again, and a shade above what the shaped target read in the passband: two skirts 2.6 octaves apart
        // leave the middle of the band half a dB down.
        Assert.Equal(TargetAt(live, 200), TargetAt(live, 40), 0.01);
        Assert.True(TargetAt(live, 200) > passband);
        Assert.Equal(80m, live.Control<ThemedNumericUpDown>("numericFromHz").Value);
    });

    [Fact]
    public void AWindowEdgeTypedByHand_SurvivesTheCrossoverBox() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        live.Panel.BeginVirtualDspHandoff(Handoff(DspProcessorProfile.Custom(SampleRate, PeqQConvention.Rbj)));
        live.Set<ThemedNumericUpDown>("numericFromHz", box => box.Value = 60m);
        decimal toHz = live.Session.WindowToHz;

        live.Set<CheckBox>("checkBoxCrossoverTarget", box => box.Checked = false);

        Assert.Equal(60m, live.Session.WindowFromHz);
        Assert.Equal(toHz, live.Session.WindowToHz);
        Assert.False(live.Session.CrossoverInTarget);
    });

    [Fact]
    public void WithoutAHandoff_TheCrossoverBoxHasNothingToFollow() => StaTest.Run(() =>
    {
        using var live = new LivePanel();

        Assert.False(live.Control<CheckBox>("checkBoxCrossoverTarget").Enabled);
        Assert.Null(live.Session.TargetCrossover);
    });

    [Fact]
    public void AnUndisturbedFit_LandsInTheBank() => StaTest.Run(() =>
    {
        using var live = FitReady();
        PeqBankState before = live.Session.Bank.State;

        live.Control<Button>("buttonAutoTune").PerformClick();
        live.SettleFit();

        Assert.NotEqual(before, live.Session.Bank.State);
    });

    [Fact]
    public void LoweringMaxFiltersDuringAFit_DropsTheFit() => StaTest.Run(() =>
    {
        using var live = FitReady();
        PeqBankState before = live.Session.Bank.State;

        live.Control<Button>("buttonAutoTune").PerformClick();
        // While the worker fits: a result under the old budget could land more filters than the field now allows.
        live.Control<ThemedComboBox>("comboBoxBandsLimit").SelectedItem = 4;
        live.SettleFit();

        Assert.Equal(before, live.Session.Bank.State);
    });

    // A gated handoff with the target a little under the source, so the fit has cuts to make and asks nothing.
    private static LivePanel FitReady()
    {
        var live = new LivePanel();
        live.Panel.BeginVirtualDspHandoff(Handoff(DspProcessorProfile.Custom(SampleRate, PeqQConvention.Rbj)));
        live.Settle();
        (EqWizardCurve? source, EqWizardCurve target) = EqWizardRender.FitCurves(live.Session);
        (double minHz, double maxHz) = live.Session.FrequencyWindow;
        double above = EqTargetLevelCheck.TargetAboveSourceDb(
            Signal(source!), Signal(target), minHz, maxHz)!.Value;
        live.Set<ThemedNumericUpDown>(
            "NumericTargetOffset", box => box.Value = Math.Round(box.Value - (decimal)above - 2m));
        (source, target) = EqWizardRender.FitCurves(live.Session);
        // A question would open a modal box the test cannot answer.
        Assert.Null(EqWizardFit.LevelWarning(live.Session, Signal(source!), Signal(target)));
        return live;
    }

    private static List<SignalPoint> Signal(EqWizardCurve curve) =>
        curve.Points.Select(point => new SignalPoint(point.X, point.Y)).ToList();

    private static double TargetAt(LivePanel live, double hz) =>
        EqWizardRender.TargetCurve(live.Session, [hz]).Points[0].Y;

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

        public EqBandHandlesAnnotation Handles => Plot.Annotations.OfType<EqBandHandlesAnnotation>().Single();

        private PlotView View => Control<PlotView>("plotWizard");

        // An off-screen view never paints; a PNG export at its size lays the model out as a paint would.
        public ScreenPoint HandleCenter(int index)
        {
            using var stream = new MemoryStream();
            new PngExporter { Width = View.Width, Height = View.Height }.Export(Plot, stream);
            return Handles.Center(index);
        }

        public void Press(ScreenPoint at) => Change(() =>
            View.ActualController.HandleMouseDown(
                View,
                new OxyMouseDownEventArgs { ChangedButton = OxyMouseButton.Left, ClickCount = 1, Position = at }));

        public void Move(ScreenPoint at) => Change(() =>
            View.ActualController.HandleMouseMove(View, new OxyMouseEventArgs { Position = at }));

        public void Release(ScreenPoint at) => Change(() =>
            View.ActualController.HandleMouseUp(View, new OxyMouseEventArgs { Position = at }));

        public void Wheel(ScreenPoint at) => Change(() =>
            View.ActualController.HandleMouseWheel(View, new OxyMouseWheelEventArgs { Delta = 120, Position = at }));

        // Through the control's own handlers, so the panel sees the press and the click the way Windows delivers them.
        public void ClickPlot(ScreenPoint at) => Change(() =>
        {
            var args = new MouseEventArgs(MouseButtons.Left, 1, (int)at.X, (int)at.Y, 0);
            Raise("OnMouseDown", args);
            View.Capture = false;
            Raise("OnMouseUp", args);
            Raise("OnClick", EventArgs.Empty);
        });

        private void Raise(string handler, EventArgs args) =>
            typeof(Control).GetMethod(handler, Hidden)!.Invoke(View, [args]);

        public T Control<T>(string name) => (T)typeof(EqWizardPanel).GetField(name, Hidden)!.GetValue(Panel)!;

        public void Set<T>(string name, Action<T> change) => Change(() => change(Control<T>(name)));

        public void Change(Action change)
        {
            change();
            Settle();
        }

        public void Invoke(string method, params object[] arguments) =>
            Change(() => typeof(EqWizardPanel).GetMethod(method, Hidden)!.Invoke(Panel, arguments));

        public void Click(string button) => Change(() => Control<Button>(button).PerformClick());

        // Built as a right-click builds it, never shown: a popup would open on the desktop of whoever runs the tests.
        public void ChooseFromBandMenu(PeqSlotControl strip, string text) => Change(() =>
        {
            var menu = (ContextMenuStrip)typeof(EqWizardPanel).GetMethod("BuildBandMenu", Hidden)!
                .Invoke(Panel, [strip])!;
            menu.Items.OfType<ToolStripMenuItem>().Single(item => item.Text == text).PerformClick();
        });

        // A left press and click on the number plate, through the label's own handlers.
        public void PickByPlate(PeqSlotControl strip) => Change(() =>
        {
            Control plate = strip.SlotLabel;
            typeof(Control).GetMethod("OnMouseDown", Hidden)!.Invoke(
                plate, [new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0)]);
            typeof(Control).GetMethod("OnMouseUp", Hidden)!.Invoke(
                plate, [new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0)]);
            typeof(Control).GetMethod("OnClick", Hidden)!.Invoke(plate, [EventArgs.Empty]);
        });

        public bool PressDelete()
        {
            var message = new Message { Msg = 0x0100, WParam = (IntPtr)Keys.Delete };
            object[] arguments = [message, Keys.Delete];
            bool handled = (bool)typeof(EqWizardPanel).GetMethod("ProcessCmdKey", Hidden)!.Invoke(Panel, arguments)!;
            Settle();
            return handled;
        }

        public void Settle()
        {
            for (int attempt = 0; attempt < 2_000 && Session.Previews.Rendering; attempt++)
            {
                StaTest.Pump();
                Thread.Sleep(5);
            }

            // The redraw a landed render asks for is posted after it; let it run.
            for (int pass = 0; pass < 5; pass++)
            {
                StaTest.Pump();
            }

            Assert.False(Session.Previews.Rendering, "A preview did not land in time.");
        }

        // The Auto Tune button is off while its fit runs.
        public void SettleFit()
        {
            Button autoTune = Control<Button>("buttonAutoTune");
            for (int attempt = 0; attempt < 2_000 && !autoTune.Enabled; attempt++)
            {
                StaTest.Pump();
                Thread.Sleep(5);
            }

            Assert.True(autoTune.Enabled, "The fit did not finish in time.");
            Settle();
        }

        public void Dispose() => host.Dispose();
    }
}
