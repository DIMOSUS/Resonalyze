using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    public partial class LiveSpectrumOpt
    {
        private void FillLists()
        {
            Fill(sequenceLengthComboBox, LiveSpectrumSettingsChoices.SequenceLengths.Select(length =>
                (length, LiveSpectrumSettingsChoices.SequenceLengthLabel(length, session.SampleRateHz))));
            Fill(overlapComboBox, Percents(LiveSpectrumSettingsChoices.OverlapPercents));
            Fill(windowComboBox, LiveSpectrumSettingsChoices.Windows);
            Fill(averagingComboBox, LiveSpectrumSettingsChoices.Averagings);
            Fill(coherenceLimitComboBox, Percents(LiveSpectrumSettingsChoices.CoherenceLimits));
        }

        private static IEnumerable<(int, string)> Percents(IEnumerable<int> percents) =>
            percents.Select(percent => (percent, LiveSpectrumSettingsChoices.PercentLabel(percent)));

        private static void Fill<T>(ThemedComboBox combo, IEnumerable<(T Value, string Label)> choices)
        {
            combo.Items.Clear();
            foreach ((T value, string label) in choices)
            {
                combo.Items.Add(new Choice<T>(value, label));
            }
        }

        // Writes every control from the session; the controls' own change events are ignored meanwhile.
        private void Present()
        {
            presenting = true;
            try
            {
                // Assign all three: WinForms clears siblings, so the single true value wins in any order.
                radioModeMmm.Checked = session.Mode == LiveAnalysisMode.Mmm;
                radioModeRta.Checked = session.Mode == LiveAnalysisMode.Rta;
                radioModeTransfer.Checked = session.Mode == LiveAnalysisMode.TransferFunction;
                PresentSignals();
                Show(sequenceLengthComboBox, session.SequenceLength);
                Show(windowComboBox, session.Window);
                windowComboBox.Enabled = session.WindowEditable;
                Show(overlapComboBox, session.OverlapPercent);
                overlapComboBox.Enabled = session.OverlapEditable;
                if (!Equals(comboSmoothingInverseOctaves.SelectedItem, session.SmoothingInverseOctaves))
                {
                    comboSmoothingInverseOctaves.SelectedItem = session.SmoothingInverseOctaves;
                }

                comboSmoothingInverseOctaves.Enabled = session.RecipeEditable;
                Show(averagingComboBox, session.Averaging);
                averagingComboBox.Enabled = session.RecipeEditable;
                Show(coherenceLimitComboBox, session.CoherenceLimitPercent);
                coherenceLimitComboBox.Enabled = session.CoherenceLimitEditable;
                checkMainCurve.Checked = session.MainCurve;
                checkInputMagnitude.Checked = session.InputMagnitude;
                checkPeakHold.Checked = session.PeakHold;
                checkCoherence.Checked = session.Coherence;
                checkTilt.Checked = session.Tilt;
                checkSpl.Checked = session.Spl;
                PresentLooks();
                PresentCalibration();
            }
            finally
            {
                presenting = false;
            }
        }

        private void PresentSignals()
        {
            if (!signalTypeComboBox.Items.Cast<Choice<NoiseColor>>().Select(item => item.Value).SequenceEqual(session.Signals))
            {
                Fill(signalTypeComboBox, session.Signals.Select(signal => (signal, LiveSpectrumSettingsChoices.SignalLabel(signal))));
            }

            Show(signalTypeComboBox, session.Signal);
            signalTypeComboBox.Enabled = session.RecipeEditable;
        }

        private static void Show<T>(ThemedComboBox combo, T value)
        {
            for (int index = 0; index < combo.Items.Count; index++)
            {
                if (combo.Items[index] is Choice<T> choice && EqualityComparer<T>.Default.Equals(choice.Value, value))
                {
                    if (combo.SelectedIndex != index)
                    {
                        combo.SelectedIndex = index;
                    }

                    return;
                }
            }
        }

        // Mute rather than disable, for the theme's muted colour instead of system grey.
        private void PresentLooks()
        {
            bool curvesMuted = LiveSpectrumSettingsLook.CurvesMuted(session);
            UiStyle.SetTextEnabledLook(labelMainCurve, !curvesMuted);
            PresentBox(checkMainCurve, curvesMuted, !curvesMuted);
            UiStyle.SetTextEnabledLook(labelInputMagnitude, !curvesMuted);
            PresentBox(checkInputMagnitude, curvesMuted, session.InputMagnitudeInteractive);
            UiStyle.SetTextEnabledLook(label9, !curvesMuted);
            PresentBox(checkCoherence, curvesMuted, !curvesMuted);
            UiStyle.SetTextEnabledLook(label10, !curvesMuted);

            string splDescription = LiveSpectrumSettingsToolTips.Spl(session);
            toolTip.SetToolTip(labelSpl, splDescription);
            toolTip.SetToolTip(checkSpl, splDescription);
            // Set directly: SetTextEnabledLook memorizes the colour it replaces when muting and would restore a stale amber.
            LiveSettingTone spl = LiveSpectrumSettingsLook.Spl(session);
            labelSpl.ForeColor = ColorOf(spl, splChoiceReadyForeColor);
            PresentBox(checkSpl, spl == LiveSettingTone.Muted, session.SplInteractive);

            LiveSettingTone tilt = LiveSpectrumSettingsLook.Tilt(session);
            UiStyle.SetTextEnabledLook(labelTilt, tilt != LiveSettingTone.Muted);
            PresentBox(checkTilt, tilt == LiveSettingTone.Muted, session.TiltInteractive);

            radioModeTransfer.ForeColor = ColorOf(LiveSpectrumSettingsLook.Transfer(session), transferChoiceReadyForeColor);
            toolTip.SetToolTip(radioModeTransfer, LiveSpectrumSettingsToolTips.Transfer(session));
        }

        // A box that takes no click keeps its state; muted or not is only its colour.
        private static void PresentBox(CheckBox box, bool muted, bool interactive)
        {
            UiStyle.SetTextEnabledLook(box, !muted);
            box.AutoCheck = interactive;
            box.TabStop = interactive;
        }

        private static Color ColorOf(LiveSettingTone tone, Color normal) => tone switch
        {
            LiveSettingTone.Warning => UiPalette.Warning,
            LiveSettingTone.Muted => UiPalette.TextDisabled,
            _ => normal
        };

        private void PresentCalibration()
        {
            if (comboCalibration.Items.Count != 1 || !Equals(comboCalibration.Items[0], session.Calibration))
            {
                comboCalibration.Items.Clear();
                comboCalibration.Items.Add(session.Calibration);
                comboCalibration.SelectedIndex = 0;
            }

            comboCalibration.Enabled = false;
        }
    }
}
