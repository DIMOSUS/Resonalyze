using System.Drawing;
using Resonalyze.Dsp;
using Resonalyze.Ui;

namespace Resonalyze.Options
{
    public partial class LiveSpectrumOpt
    {
        private void FillLists(int sampleRateHz)
        {
            sequenceLengthComboBox.Items.Clear();
            foreach (int sequenceLength in LiveSpectrumSettingsChoices.SequenceLengths)
            {
                sequenceLengthComboBox.Items.Add(new SequenceLengthOption(sequenceLength, sampleRateHz));
            }

            overlapComboBox.Items.Clear();
            foreach (int overlapPercent in LiveSpectrumSettingsChoices.OverlapPercents)
            {
                overlapComboBox.Items.Add(new OverlapOption(overlapPercent));
            }

            windowComboBox.Items.Clear();
            foreach ((WindowType window, string label) in LiveSpectrumSettingsChoices.Windows)
            {
                windowComboBox.Items.Add(new WindowOption(window, label));
            }

            averagingComboBox.Items.Clear();
            foreach ((AveragingSpeed speed, string label) in LiveSpectrumSettingsChoices.Averagings)
            {
                averagingComboBox.Items.Add(new AveragingOption(speed, label));
            }

            coherenceLimitComboBox.Items.Clear();
            foreach (int limit in LiveSpectrumSettingsChoices.CoherenceLimits)
            {
                coherenceLimitComboBox.Items.Add(new CoherenceLimitOption(limit));
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
                Select(sequenceLengthComboBox, item => item is SequenceLengthOption option && option.Length == session.SequenceLength);
                Select(windowComboBox, item => item is WindowOption option && option.WindowType == session.Window);
                windowComboBox.Enabled = session.WindowEditable;
                Select(overlapComboBox, item => item is OverlapOption option && option.Percent == session.OverlapPercent);
                overlapComboBox.Enabled = session.OverlapEditable;
                if (!Equals(comboSmoothingInverseOctaves.SelectedItem, session.SmoothingInverseOctaves))
                {
                    comboSmoothingInverseOctaves.SelectedItem = session.SmoothingInverseOctaves;
                }

                comboSmoothingInverseOctaves.Enabled = session.SmoothingEditable;
                Select(averagingComboBox, item => item is AveragingOption option && option.Speed == session.Averaging);
                averagingComboBox.Enabled = session.AveragingEditable;
                Select(coherenceLimitComboBox, item => item is CoherenceLimitOption option && option.Percent == session.CoherenceLimitPercent);
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
            if (!signalTypeComboBox.Items.Cast<object>().Select(item => ((NoiseColorOption)item).NoiseColor)
                    .SequenceEqual(session.Signals))
            {
                signalTypeComboBox.Items.Clear();
                foreach (NoiseColor signal in session.Signals)
                {
                    signalTypeComboBox.Items.Add(
                        new NoiseColorOption(signal, LiveSpectrumSettingsChoices.SignalLabel(signal)));
                }
            }

            Select(signalTypeComboBox, item => item is NoiseColorOption option && option.NoiseColor == session.Signal);
            signalTypeComboBox.Enabled = session.SignalEditable;
        }

        private static void Select(ThemedComboBox combo, Func<object, bool> matches)
        {
            for (int index = 0; index < combo.Items.Count; index++)
            {
                if (matches(combo.Items[index]!))
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
