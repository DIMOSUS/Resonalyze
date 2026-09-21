using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Resonalyze.Dsp;

using Resonalyze.Ui;

namespace Resonalyze.Options
{
    public partial class MeasurementOptions
    {
        private void PresentCalibrations()
        {
            RecordCalibrationView view = RecordCalibrations.Read(session, audioSessionFactory != null);
            PresentCalibrationButton(buttonCalibration0, view.ZeroDegree);
            buttonClearCalibration0.Enabled = view.CanClearZeroDegree;
            deviceToolTip.SetToolTip(buttonClearCalibration0, view.ClearZeroDegreeToolTip);
            buttonCalibrationExtra.Text = view.ExtraButtonText;
            deviceToolTip.SetToolTip(buttonCalibrationExtra, RecordCalibrations.ExtraToolTip);
            deviceToolTip.SetToolTip(comboBoxMicrophoneCalibration, RecordCalibrations.MicrophoneCalibrationToolTip);
            comboBoxMicrophoneCalibration.Enabled = view.MicrophoneCalibrationEnabled;
            buttonArrayMicrophones.Text = view.ArrayButtonText;
            buttonSplCalibration.Enabled = view.SplEnabled;
            buttonClearSplCalibration.Enabled = view.CanClearSpl;
            PresentCalibrationButton(buttonSplCalibration, view.Spl);
            deviceToolTip.SetToolTip(buttonClearSplCalibration, view.ClearSplToolTip);
        }

        private void PresentCalibrationButton(Button button, RecordCalibrationButton view)
        {
            button.Text = view.Text;
            button.ForeColor = view.Color;
            deviceToolTip.SetToolTip(button, view.ToolTip);
        }

        private void buttonCalibration0_Click(object? sender, EventArgs e)
        {
            session.SetZeroDegreeCalibration(SelectCalibrationFile(session.MicrophoneCalibration0DegreesPath));
            Present();
        }

        private void buttonCalibrationExtra_Click(object? sender, EventArgs e)
        {
            using var dialog = new MicrophoneCalibrationsDialog(
                session.AdditionalMicrophoneCalibrations,
                session.MicrophoneCalibration0DegreesPath,
                SelectCalibrationFile);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            session.SetAdditionalCalibrations(dialog.Definitions);
            Present();
        }

        private void buttonClearCalibration0_Click(object? sender, EventArgs e)
        {
            session.SetZeroDegreeCalibration(null);
            Present();
        }

        private void buttonArrayMicrophones_Click(object? sender, EventArgs e)
        {
            (IReadOnlyList<int> channels, string source) = RecordArrayInputs.InputChannels(session);
            using var dialog = new ArrayMicrophonesDialog(
                session.ArrayMicrophones,
                session.CalibrationEntries(),
                channels,
                RecordArrayInputs.MicrophoneChannel(session),
                RecordArrayInputs.LoopbackChannel(session),
                source);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            session.SetArrayMicrophones(dialog.Microphones);
            Present();
        }

        private void buttonSplCalibration_Click(object? sender, EventArgs e)
        {
            if (audioSessionFactory == null)
            {
                return;
            }

            AudioSessionRequest request;
            try
            {
                request = RecordCalibrations.SplCaptureRequest(session);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "SPL calibration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using var dialog = new SplCalibrationDialog(audioSessionFactory, request, session.SplCalibration);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
            {
                session.SetSplCalibration(dialog.Result);
                Present();
            }
        }

        private void buttonClearSplCalibration_Click(object? sender, EventArgs e)
        {
            session.SetSplCalibration(null);
            Present();
        }

        private string? SelectCalibrationFile(string? currentPath)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select microphone calibration file",
                Filter =
                    "Microphone calibration files (*.txt;*.cal;*.frd;*.csv)|*.txt;*.cal;*.frd;*.csv|" +
                    "All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                dialog.FileName = currentPath;
                string? directory = Path.GetDirectoryName(currentPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }
            }

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return currentPath;
            }

            // Probe now: an unparsable file would otherwise silently leave measurements uncalibrated.
            var probe = new CalibrationFile(dialog.FileName);
            if (!probe.HasData)
            {
                MessageBox.Show(
                    this,
                    "The selected calibration file could not be loaded; measurements " +
                    $"will be shown uncalibrated until it is fixed.\r\n\r\n{probe.LoadError}",
                    "Microphone calibration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return dialog.FileName;
        }
    }
}
