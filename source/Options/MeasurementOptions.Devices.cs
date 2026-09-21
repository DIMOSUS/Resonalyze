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
        private ThemedComboBox comboBoxPlaybackDevice => waveAudioBackendPanel.ComboBoxPlaybackDevice;

        private ThemedComboBox comboBoxRecordingDevice => waveAudioBackendPanel.ComboBoxRecordingDevice;

        private ThemedComboBox comboBoxWaveInputChannel => waveAudioBackendPanel.ComboBoxWaveInputChannel;

        private ThemedComboBox comboBoxWaveLoopbackChannel => waveAudioBackendPanel.ComboBoxWaveLoopbackChannel;

        private Label labelPlaybackDevice => waveAudioBackendPanel.LabelPlaybackDevice;

        private Label labelRecordingDevice => waveAudioBackendPanel.LabelRecordingDevice;

        private Label labelWaveInputChannel => waveAudioBackendPanel.LabelWaveInputChannel;

        private Label labelWaveLoopbackChannel => waveAudioBackendPanel.LabelWaveLoopbackChannel;

        private Label labelWaveLoopbackStatus => waveAudioBackendPanel.LabelWaveLoopbackStatus;

        private Label labelDeviceSettings => waveAudioBackendPanel.LabelDeviceSettings;

        private Button buttonDeviceSettings => waveAudioBackendPanel.ButtonDeviceSettings;

        private ThemedComboBox comboBoxAsioDriver => asioAudioBackendPanel.ComboBoxAsioDriver;

        private ThemedComboBox comboBoxAsioInputChannel => asioAudioBackendPanel.ComboBoxAsioInputChannel;

        private ThemedComboBox comboBoxAsioOutputChannel => asioAudioBackendPanel.ComboBoxAsioOutputChannel;

        private ThemedComboBox comboBoxAsioLoopbackChannel => asioAudioBackendPanel.ComboBoxAsioLoopbackChannel;

        private Button buttonAsioInputProbe => asioAudioBackendPanel.ButtonAsioInputProbe;

        private Button buttonAsioControlPanel => asioAudioBackendPanel.ButtonAsioControlPanel;

        private Label labelAsioDriver => asioAudioBackendPanel.LabelAsioDriver;

        private Label labelAsioInputChannel => asioAudioBackendPanel.LabelAsioInputChannel;

        private Label labelAsioOutputChannel => asioAudioBackendPanel.LabelAsioOutputChannel;

        private Label labelAsioLoopbackChannel => asioAudioBackendPanel.LabelAsioLoopbackChannel;

        private Label labelAsioSampleRate => asioAudioBackendPanel.LabelAsioSampleRate;

        private Label labelAsioSampleRateStatus => asioAudioBackendPanel.LabelAsioSampleRateStatus;

        private Label labelAsioPlaybackLatency => asioAudioBackendPanel.LabelAsioPlaybackLatency;

        private Label labelAsioPlaybackLatencyValue => asioAudioBackendPanel.LabelAsioPlaybackLatencyValue;

        private void PresentDevices()
        {
            RecordDeviceView view = RecordDeviceStatus.Read(session);
            bool useAsio = view.UseAsio;
            waveAudioBackendPanel.Visible = !useAsio;
            asioAudioBackendPanel.Visible = useAsio;
            comboBoxPlaybackDevice.Enabled = !useAsio;
            comboBoxRecordingDevice.Enabled = !useAsio;
            comboBoxWaveInputChannel.Enabled = !useAsio;
            comboBoxWaveLoopbackChannel.Enabled = view.WaveLoopbackEnabled;
            comboBoxAsioDriver.Enabled = view.AsioDriverEnabled;
            buttonAsioControlPanel.Enabled = view.AsioControlPanelEnabled;
            buttonAsioInputProbe.Enabled = view.AsioInputProbeEnabled;
            comboBoxAsioInputChannel.Enabled = view.AsioInputsEnabled;
            comboBoxAsioLoopbackChannel.Enabled = view.AsioInputsEnabled;
            comboBoxAsioOutputChannel.Enabled = view.AsioOutputEnabled;
            UiStyle.SetTextEnabledLook(labelAsioDriver, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioInputChannel, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioOutputChannel, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioLoopbackChannel, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioSampleRate, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioPlaybackLatency, useAsio);
            UiStyle.SetTextEnabledLook(labelAsioPlaybackLatencyValue, useAsio);
            UiStyle.SetTextEnabledLook(labelPlaybackDevice, !useAsio);
            UiStyle.SetTextEnabledLook(labelRecordingDevice, !useAsio);
            UiStyle.SetTextEnabledLook(labelWaveInputChannel, !useAsio);
            UiStyle.SetTextEnabledLook(labelWaveLoopbackChannel, !useAsio);
            labelDeviceSettings.Visible = view.UseWasapi;
            buttonDeviceSettings.Visible = view.UseWasapi;
            buttonDeviceSettings.Enabled = view.UseWasapi;
            labelPlaybackDevice.Text = view.PlaybackDeviceCaption;
            labelRecordingDevice.Text = view.RecordingDeviceCaption;
            labelWaveInputChannel.Text = view.WaveInputCaption;
            labelWaveLoopbackChannel.Text = view.WaveLoopbackCaption;
            labelWaveLoopbackStatus.Font = view.LoopbackStatus.Emphasized ? WarningStatusFont : NormalStatusFont;
            labelWaveLoopbackStatus.Text = view.LoopbackStatus.Text;
            labelWaveLoopbackStatus.ForeColor = view.LoopbackStatus.Color;
            // Only ASIO's own route has a verdict; the hidden line keeps the last one, greyed.
            if (!useAsio)
            {
                labelAsioSampleRateStatus.ForeColor = UiPalette.TextDisabled;
                return;
            }

            labelAsioSampleRateStatus.Text = view.AsioSampleRateStatus.Text;
            labelAsioSampleRateStatus.ForeColor = view.AsioSampleRateStatus.Color;
            labelAsioPlaybackLatencyValue.Text = view.AsioPlaybackLatency;
        }

        private void HandleEndpointsChanged()
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (IsDisposed)
                    {
                        return;
                    }

                    session.RefreshEndpoints();
                    Present();
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void buttonAsioControlPanel_Click(object? sender, EventArgs e)
        {
            try
            {
                session.ShowAsioControlPanel();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "ASIO Control Panel",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            Present();
        }

        private void buttonDeviceSettings_Click(object? sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Device Settings",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private async void buttonAsioInputProbe_Click(object? sender, EventArgs e)
        {
            if (session.AsioDriver.SelectedItem is not AsioDeviceInfo)
            {
                return;
            }

            try
            {
                buttonAsioInputProbe.Enabled = false;
                buttonAsioInputProbe.Text = "Testing...";
                IReadOnlyList<AsioInputProbeChannelResult> results = await session.ProbeAsioInputsAsync()!;
                // The panel can close during the ~1 s capture; touching a disposed form would crash the async void handler.
                if (IsDisposed)
                {
                    return;
                }
                MessageBox.Show(
                    this,
                    FormatAsioInputProbeResults(results),
                    "ASIO Input Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception exception)
            {
                if (IsDisposed)
                {
                    return;
                }
                MessageBox.Show(
                    this,
                    exception.Message,
                    "ASIO Input Test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                if (!IsDisposed)
                {
                    buttonAsioInputProbe.Text = "Test ASIO Inputs";
                    session.SettleRoute();
                    Present();
                }
            }
        }

        private static string FormatAsioInputProbeResults(
            IReadOnlyList<AsioInputProbeChannelResult> results)
        {
            if (results.Count == 0)
            {
                return "No ASIO input channels were recorded.";
            }

            return string.Join(
                Environment.NewLine,
                results.Select(result =>
                    $"{result.Offset + 1}: {result.Name}  " +
                    $"peak {result.PeakDbFs:0.0} dBFS, " +
                    $"RMS {result.RmsDbFs:0.0} dBFS, " +
                    $"corr ch1 {result.CorrelationToFirst:0.000}"));
        }

        private Font NormalStatusFont =>
            normalStatusFont ??= labelWaveLoopbackStatus.Font;

        private Font WarningStatusFont =>
            warningStatusFont ??= new Font(NormalStatusFont, FontStyle.Bold);
    }
}
