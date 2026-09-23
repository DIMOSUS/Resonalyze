using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Options;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>The Live Spectrum settings panel through its controls and the shell's calls beside a session driven the same
/// way: after every step each control, colour and tooltip shows what the session's readers say and Apply writes what
/// the session writes, so a field bound to anything else fails here.</summary>
public sealed class LiveSpectrumOptWiringTests
{
    [Fact]
    public void TheModes_PinMuteAndGiveBackTheUsersChoices()
    {
        StaTest.Run(() =>
        {
            using var panel = new Panel(options =>
            {
                options.NoiseColor = NoiseColor.Pink;
                options.WindowType = WindowType.FlatTop;
                options.OverlapPercent = 75;
                options.AveragingSpeed = AveragingSpeed.Slow;
                options.SmoothingInverseOctaves = 3;
            });

            panel.Click("checkInputMagnitude");
            panel.Click("checkSpl");
            panel.Click("checkTilt");
            panel.Click("radioModeRta");
            panel.Click("checkInputMagnitude");
            panel.Click("checkSpl");
            panel.Click("checkTilt");
            panel.Click("radioModeMmm");
            panel.Click("checkSpl");
            panel.Click("checkTilt");
            panel.Click("radioModeTransfer");
            panel.Click("radioModeRta");
            panel.Click("radioModeRta");
            panel.Click("checkMainCurve");
            panel.Click("radioModeTransfer");
            panel.Click("checkMainCurve");
            panel.Click("checkCoherence");
            panel.Click("checkPeakHold");
        });
    }

    [Fact]
    public void TheSignal_ForcesItsFieldsOnACommit_AndAnArrowKeyOnlyMovesTheList()
    {
        StaTest.Run(() =>
        {
            using var panel = new Panel(options =>
            {
                options.AnalysisMode = LiveAnalysisMode.Rta;
                options.NoiseColor = NoiseColor.Pink;
            });

            panel.Pick("windowComboBox", "Blackman-Harris");
            panel.Pick("overlapComboBox", "75%");
            panel.Pick("signalTypeComboBox", "Pink noise (periodic)");
            panel.Pick("signalTypeComboBox", "White noise");
            // Onto periodic pink: without a commit the window and overlap stay free.
            panel.Arrow("signalTypeComboBox", -3);
            panel.Arrow("windowComboBox", 1);
            panel.Arrow("overlapComboBox", -1);
            panel.Click("checkTilt");
            panel.Pick("signalTypeComboBox", "Silent");
            panel.Click("checkTilt");
            panel.Pick("averagingComboBox", "Infinite");
            panel.Arrow("averagingComboBox", -1);
            panel.Pick("comboSmoothingInverseOctaves", "1/12");
            panel.Arrow("comboSmoothingInverseOctaves", 1);
            panel.Pick("sequenceLengthComboBox", "512 — 11 ms");
            panel.Arrow("coherenceLimitComboBox", 2);
            panel.Pick("coherenceLimitComboBox", "Off");
            panel.Click("radioModeTransfer");
            panel.Click("radioModeRta");
        });
    }

    [Fact]
    public void TheShellsCalls_ForceTheModeAndTheScale_AndRecolourTheAvailability()
    {
        StaTest.Run(() =>
        {
            using var panel = new Panel(
                options =>
                {
                    options.AnalysisMode = LiveAnalysisMode.Rta;
                    options.MagnitudeScale = MagnitudeScale.SoundPressureLevel;
                },
                splAvailable: false,
                liveCurve: true,
                loopback: false);

            panel.Availability(splAvailable: false, liveCurve: false, loopback: false);
            panel.Force(LiveAnalysisMode.TransferFunction);
            panel.Availability(splAvailable: true, liveCurve: true, loopback: true);
            panel.Availability(splAvailable: false, liveCurve: true, loopback: false);
            panel.Force(LiveAnalysisMode.Mmm);
            panel.ForceSplOff();
            panel.Force(LiveAnalysisMode.Rta);
            panel.Force(LiveAnalysisMode.Rta);
            panel.Calibration("90° capsule 2");
            panel.Calibration(null);

            int resets = 0;
            panel.Form.ResetAverageRequested += () => resets++;
            panel.Click("buttonResetAverage");
            Assert.Equal(1, resets);
        });
    }

    [Fact]
    public void AnOpenedPanel_ShowsTheStoredOptions_AtTheRatesDurations()
    {
        StaTest.Run(() =>
        {
            using var mmm = new Panel(options =>
            {
                options.AnalysisMode = LiveAnalysisMode.Mmm;
                options.NoiseColor = NoiseColor.Brown;
                options.SequenceLength = 5_000;
                options.CoherenceThresholdPercent = 99;
                options.ShowMainCurve = false;
                options.PeakHold = true;
            }, rate: 96_000);
            using var silent = new Panel(options =>
            {
                options.NoiseColor = NoiseColor.Silent;
                options.WindowType = (WindowType)99;
                options.OverlapPercent = 30;
            }, rate: 0);
        });
    }

    private sealed class Panel : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly Color splNormal;
        private readonly Color transferNormal;

        public Panel(
            Action<LiveSpectrumOptions> edit,
            bool splAvailable = true,
            bool liveCurve = false,
            bool loopback = true,
            int rate = 48_000)
        {
            var options = new LiveSpectrumOptions();
            edit(options);
            Form = new LiveSpectrumOpt();
            splNormal = Find<Label>("labelSpl").ForeColor;
            transferNormal = Find<RadioButton>("radioModeTransfer").ForeColor;
            Form.Init(options, [], splAvailable, liveCurve, loopback, rate);
            Form.ShowCalibration("Off");
            Form.CreateControl();
            Shadow.Load(options, splAvailable, liveCurve, loopback, rate);
            Shadow.SetCalibration("Off");
            AssertShows();
        }

        public LiveSpectrumOpt Form { get; }

        public LiveSpectrumSettingsSession Shadow { get; } = new();

        /// <summary>A mouse click: a box that takes clicks toggles, then the click is raised.</summary>
        public void Click(string name)
        {
            var control = Find<ButtonBase>(name);
            switch (control)
            {
                case RadioButton radio:
                    Shadow.SelectMode(radio.Name switch
                    {
                        "radioModeMmm" => LiveAnalysisMode.Mmm,
                        "radioModeRta" => LiveAnalysisMode.Rta,
                        _ => LiveAnalysisMode.TransferFunction
                    });
                    break;
                case CheckBox box:
                    ClickShadow(box.Name);
                    break;
            }

            typeof(Control).GetMethod("OnClick", Hidden)!.Invoke(control, [EventArgs.Empty]);
            AssertShows();
        }

        /// <summary>A pick in the drop-down: the list moves, then the pick is committed.</summary>
        public void Pick(string name, string label)
        {
            ThemedComboBox combo = Find<ThemedComboBox>(name);
            int index = Labels(combo).IndexOf(label);
            Assert.True(index >= 0, $"no {label} in {name}");
            MoveShadow(name, combo.Items[index]!);
            CommitShadow(name);
            typeof(ThemedComboBox).GetMethod("CommitPopupSelection", Hidden)!.Invoke(combo, [index]);
            AssertShows();
        }

        /// <summary>An arrow key in the closed list: it moves without a commit.</summary>
        public void Arrow(string name, int delta)
        {
            ThemedComboBox combo = Find<ThemedComboBox>(name);
            for (int step = 0; step < Math.Abs(delta); step++)
            {
                int index = Math.Clamp(combo.SelectedIndex + Math.Sign(delta), 0, combo.Items.Count - 1);
                MoveShadow(name, combo.Items[index]!);
                typeof(ThemedComboBox).GetMethod("MoveSelection", Hidden)!.Invoke(combo, [Math.Sign(delta), false]);
            }

            AssertShows();
        }

        public void Force(LiveAnalysisMode mode)
        {
            Shadow.SelectMode(mode);
            Form.ForceAnalysisMode(mode);
            AssertShows();
        }

        public void ForceSplOff()
        {
            Shadow.ForceSplOff();
            Form.ForceSplScaleOff();
            AssertShows();
        }

        public void Availability(bool splAvailable, bool liveCurve, bool loopback)
        {
            Shadow.SetAvailability(splAvailable, liveCurve, loopback);
            Form.RefreshAvailability(splAvailable, liveCurve, loopback);
            AssertShows();
        }

        public void Calibration(string? text)
        {
            Shadow.SetCalibration(text);
            Form.ShowCalibration(text!);
            AssertShows();
        }

        public T Find<T>(string name) where T : Control =>
            (T)Form.Controls.Find(name, searchAllChildren: true).Single();

        public void Dispose() => Form.Dispose();

        private void AssertShows()
        {
            var shown = new LiveSpectrumOptions();
            Form.SetOptions(shown);
            var written = new LiveSpectrumOptions();
            Shadow.WriteTo(written);
            Assert.Equal(
                (written.AnalysisMode, written.NoiseColor, written.SequenceLength, written.OverlapPercent,
                    written.SmoothingInverseOctaves, written.WindowType, written.AveragingSpeed, written.ShowMainCurve,
                    written.ShowInputMagnitude, written.PeakHold, written.ShowCoherence, written.CoherenceThresholdPercent,
                    written.CompensateNoiseTilt, written.MagnitudeScale),
                (shown.AnalysisMode, shown.NoiseColor, shown.SequenceLength, shown.OverlapPercent,
                    shown.SmoothingInverseOctaves, shown.WindowType, shown.AveragingSpeed, shown.ShowMainCurve,
                    shown.ShowInputMagnitude, shown.PeakHold, shown.ShowCoherence, shown.CoherenceThresholdPercent,
                    shown.CompensateNoiseTilt, shown.MagnitudeScale));

            Assert.Equal(Shadow.Mode == LiveAnalysisMode.Mmm, Find<RadioButton>("radioModeMmm").Checked);
            Assert.Equal(Shadow.Mode == LiveAnalysisMode.Rta, Find<RadioButton>("radioModeRta").Checked);
            Assert.Equal(Shadow.Mode == LiveAnalysisMode.TransferFunction, Find<RadioButton>("radioModeTransfer").Checked);

            AssertList(
                "signalTypeComboBox",
                Shadow.Signals.Select(LiveSpectrumSettingsChoices.SignalLabel),
                LiveSpectrumSettingsChoices.SignalLabel(Shadow.Signal),
                Shadow.SignalEditable);
            AssertList(
                "sequenceLengthComboBox",
                LiveSpectrumSettingsChoices.SequenceLengths.Select(length =>
                    LiveSpectrumSettingsChoices.SequenceLengthLabel(length, Shadow.SampleRateHz)),
                LiveSpectrumSettingsChoices.SequenceLengthLabel(Shadow.SequenceLength, Shadow.SampleRateHz),
                true);
            AssertList(
                "windowComboBox",
                LiveSpectrumSettingsChoices.Windows.Select(window => window.Label),
                LiveSpectrumSettingsChoices.Windows.Single(window => window.Value == Shadow.Window).Label,
                Shadow.WindowEditable);
            AssertList(
                "overlapComboBox",
                LiveSpectrumSettingsChoices.OverlapPercents.Select(LiveSpectrumSettingsChoices.PercentLabel),
                LiveSpectrumSettingsChoices.PercentLabel(Shadow.OverlapPercent),
                Shadow.OverlapEditable);
            AssertList(
                "averagingComboBox",
                LiveSpectrumSettingsChoices.Averagings.Select(speed => speed.Label),
                LiveSpectrumSettingsChoices.Averagings.Single(speed => speed.Value == Shadow.Averaging).Label,
                Shadow.AveragingEditable);
            AssertList(
                "coherenceLimitComboBox",
                LiveSpectrumSettingsChoices.CoherenceLimits.Select(LiveSpectrumSettingsChoices.PercentLabel),
                LiveSpectrumSettingsChoices.PercentLabel(Shadow.CoherenceLimitPercent),
                Shadow.CoherenceLimitEditable);
            ThemedComboBox smoothing = Find<ThemedComboBox>("comboSmoothingInverseOctaves");
            Assert.Equal(Shadow.SmoothingInverseOctaves, smoothing.SelectedItem);
            Assert.Equal(Shadow.SmoothingEditable, smoothing.Enabled);
            ThemedComboBox calibration = Find<ThemedComboBox>("comboCalibration");
            Assert.Equal([Shadow.Calibration], calibration.Items.Cast<object>());
            Assert.Equal(0, calibration.SelectedIndex);
            Assert.False(calibration.Enabled);

            bool curvesMuted = LiveSpectrumSettingsLook.CurvesMuted(Shadow);
            AssertBox("checkMainCurve", Shadow.MainCurve, !curvesMuted, curvesMuted);
            AssertBox("checkInputMagnitude", Shadow.InputMagnitude, Shadow.InputMagnitudeInteractive, curvesMuted);
            AssertBox("checkCoherence", Shadow.Coherence, !curvesMuted, curvesMuted);
            AssertBox("checkPeakHold", Shadow.PeakHold, true, false);
            foreach (string label in new[] { "labelMainCurve", "labelInputMagnitude", "label9", "label10" })
            {
                Assert.Equal(curvesMuted, Find<Label>(label).ForeColor == UiPalette.TextDisabled);
            }

            LiveSettingTone scale = LiveSpectrumSettingsLook.Spl(Shadow);
            Assert.Equal(ColorOf(scale, splNormal), Find<Label>("labelSpl").ForeColor);
            AssertBox("checkSpl", Shadow.Spl, Shadow.SplInteractive, scale == LiveSettingTone.Muted);
            LiveSettingTone tilt = LiveSpectrumSettingsLook.Tilt(Shadow);
            Assert.Equal(tilt == LiveSettingTone.Muted, Find<Label>("labelTilt").ForeColor == UiPalette.TextDisabled);
            AssertBox("checkTilt", Shadow.Tilt, Shadow.TiltInteractive, tilt == LiveSettingTone.Muted);
            Assert.Equal(
                ColorOf(LiveSpectrumSettingsLook.Transfer(Shadow), transferNormal),
                Find<RadioButton>("radioModeTransfer").ForeColor);

            // The panel's tooltip wraps what it is given.
            string spl = ToolTipTextWrapper.Wrap(LiveSpectrumSettingsToolTips.Spl(Shadow));
            Assert.Equal(spl, Form.ToolTips.GetToolTip(Find<Label>("labelSpl")));
            Assert.Equal(spl, Form.ToolTips.GetToolTip(Find<CheckBox>("checkSpl")));
            Assert.Equal(
                ToolTipTextWrapper.Wrap(LiveSpectrumSettingsToolTips.Transfer(Shadow)),
                Form.ToolTips.GetToolTip(Find<RadioButton>("radioModeTransfer")));
        }

        private void AssertList(string name, IEnumerable<string> items, string selected, bool enabled)
        {
            ThemedComboBox combo = Find<ThemedComboBox>(name);
            Assert.Equal(items, Labels(combo));
            Assert.Equal(selected, combo.GetItemText(combo.SelectedItem));
            Assert.Equal(enabled, combo.Enabled);
        }

        private void AssertBox(string name, bool checkedState, bool interactive, bool muted)
        {
            CheckBox box = Find<CheckBox>(name);
            Assert.Equal((checkedState, interactive, interactive), (box.Checked, box.AutoCheck, box.TabStop));
            Assert.Equal(muted, box.ForeColor == UiPalette.TextDisabled);
        }

        private void ClickShadow(string name)
        {
            switch (name)
            {
                case "checkMainCurve" when !LiveSpectrumSettingsLook.CurvesMuted(Shadow):
                    Shadow.SetMainCurve(!Shadow.MainCurve);
                    break;
                case "checkCoherence" when !LiveSpectrumSettingsLook.CurvesMuted(Shadow):
                    Shadow.SetCoherence(!Shadow.Coherence);
                    break;
                case "checkPeakHold":
                    Shadow.SetPeakHold(!Shadow.PeakHold);
                    break;
                case "checkInputMagnitude":
                    if (Shadow.InputMagnitudeInteractive)
                    {
                        Shadow.SetInputMagnitude(!Shadow.InputMagnitude);
                    }

                    Shadow.ClickInputMagnitude();
                    break;
                case "checkSpl":
                    if (Shadow.SplInteractive)
                    {
                        Shadow.SetSpl(!Shadow.Spl);
                    }

                    Shadow.ClickSpl();
                    break;
                case "checkTilt":
                    if (Shadow.TiltInteractive)
                    {
                        Shadow.SetTilt(!Shadow.Tilt);
                    }

                    Shadow.ClickTilt();
                    break;
            }
        }

        private void MoveShadow(string name, object item)
        {
            string label = Labels(Find<ThemedComboBox>(name))[Find<ThemedComboBox>(name).Items.IndexOf(item)];
            switch (name)
            {
                case "signalTypeComboBox":
                    Shadow.MoveSignal(Shadow.Signals.Single(signal => LiveSpectrumSettingsChoices.SignalLabel(signal) == label));
                    break;
                case "sequenceLengthComboBox":
                    Shadow.MoveSequenceLength(LiveSpectrumSettingsChoices.SequenceLengths.Single(length =>
                        LiveSpectrumSettingsChoices.SequenceLengthLabel(length, Shadow.SampleRateHz) == label));
                    break;
                case "windowComboBox":
                    Shadow.MoveWindow(LiveSpectrumSettingsChoices.Windows.Single(window => window.Label == label).Value);
                    break;
                case "overlapComboBox":
                    Shadow.MoveOverlap(LiveSpectrumSettingsChoices.OverlapPercents.Single(percent =>
                        LiveSpectrumSettingsChoices.PercentLabel(percent) == label));
                    break;
                case "averagingComboBox":
                    Shadow.MoveAveraging(LiveSpectrumSettingsChoices.Averagings.Single(speed => speed.Label == label).Value);
                    break;
                case "coherenceLimitComboBox":
                    Shadow.MoveCoherenceLimit(LiveSpectrumSettingsChoices.CoherenceLimits.Single(percent =>
                        LiveSpectrumSettingsChoices.PercentLabel(percent) == label));
                    break;
                case "comboSmoothingInverseOctaves":
                    Shadow.MoveSmoothing((int)item);
                    break;
            }
        }

        private void CommitShadow(string name)
        {
            switch (name)
            {
                case "signalTypeComboBox":
                    Shadow.CommitSignal();
                    break;
                case "windowComboBox":
                    Shadow.CommitWindow();
                    break;
                case "overlapComboBox":
                    Shadow.CommitOverlap();
                    break;
                case "averagingComboBox":
                    Shadow.CommitAveraging();
                    break;
                case "comboSmoothingInverseOctaves":
                    Shadow.CommitSmoothing();
                    break;
            }
        }

        private static Color ColorOf(LiveSettingTone tone, Color normal) => tone switch
        {
            LiveSettingTone.Warning => UiPalette.Warning,
            LiveSettingTone.Muted => UiPalette.TextDisabled,
            _ => normal
        };

        private static List<string> Labels(ThemedComboBox combo) =>
            combo.Items.Cast<object>().Select(combo.GetItemText).ToList();
    }
}
