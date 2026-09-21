using System.Windows.Forms;
using Resonalyze.Options;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>
/// Record Settings driven through its controls on an in-memory machine, read by what the controls show, what the live
/// apply writes and what Apply hands the engine. The rules have their own tests; these pin the wiring to them.
/// </summary>
public sealed class MeasurementOptionsWiringTests
{
    [Fact]
    public void OpeningShowsTheSavedRoute() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();

        Assert.Equal("MME Compatibility", live.Text("comboBoxAudioBackend"));
        Assert.Equal("Speakers", live.Text("comboBoxPlaybackDevice"));
        Assert.Equal("Line in", live.Text("comboBoxRecordingDevice"));
        Assert.Equal("Right", live.Text("comboBoxWaveLoopbackChannel"));
        Assert.Equal("48000", live.Text("comboBoxSampleRate"));
        Assert.Equal("Stereo", live.Text("comboBoxChannel"));
        Assert.Equal(100m, live.Number("numericUpDownLowFrequency").Value);
        Assert.Equal("Stereo input available for Wave loopback.", live.Control<Label>("labelWaveLoopbackStatus").Text);
        Assert.Equal("Speakers", live.ToolTip("comboBoxPlaybackDevice"));
        ThemedComboBox recording = live.Combo("comboBoxRecordingDevice");
        Assert.True(recording.DropDownWidth > recording.Width, "a long device name widens the drop-down");
        Assert.True(live.Control<Control>("waveAudioBackendPanel").Visible);
        Assert.False(live.Control<Control>("asioAudioBackendPanel").Visible);
    });

    [Fact]
    public void AMonoRecordingDevice_DropsTheLoopbackAndSaysWhy() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();

        live.Pick("comboBoxRecordingDevice", "Mono mic");

        Assert.Equal("None", live.Text("comboBoxWaveLoopbackChannel"));
        Assert.False(live.Combo("comboBoxWaveLoopbackChannel").Enabled);
        Label status = live.Control<Label>("labelWaveLoopbackStatus");
        Assert.StartsWith("⚠ Loopback channel is REQUIRED", status.Text, StringComparison.Ordinal);
        Assert.True(status.Font.Bold);
        Assert.Equal("Mono mic", live.ToolTip("comboBoxRecordingDevice"));

        live.Pick("comboBoxRecordingDevice", "Line in");
        Assert.Equal("Right", live.Text("comboBoxWaveLoopbackChannel"));
        Assert.False(status.Font.Bold);
    });

    [Fact]
    public void TheBackendSwitchesTheDeviceGroup_AndApplyTakesTheAsioRoute() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();

        live.Pick("comboBoxAudioBackend", "Asio");

        Assert.False(live.Control<Control>("waveAudioBackendPanel").Visible);
        Assert.True(live.Control<Control>("asioAudioBackendPanel").Visible);
        Assert.Equal("Card", live.Text("comboBoxAsioDriver"));
        Assert.Equal("48000 Hz supported", live.Control<Label>("labelAsioSampleRateStatus").Text);
        Assert.Equal("256 samples", live.Control<Label>("labelAsioPlaybackLatencyValue").Text);

        live.Pick("comboBoxAsioInputChannel", "3: In 3");
        live.Pick("comboBoxAsioLoopbackChannel", "4: In 4");
        live.Pick("comboBoxAsioOutputChannel", "2: Out 2");
        live.Apply();

        Assert.Equal(AudioBackend.Asio, live.Engine.AudioBackend);
        Assert.Equal("Card", live.Engine.AsioDriverName);
        Assert.Equal(2, live.Engine.AsioInputChannelOffset);
        Assert.Equal(3, live.Engine.AsioLoopbackInputChannelOffset);
        Assert.Equal(1, live.Engine.AsioOutputChannelOffset);
    });

    [Fact]
    public void TheWaveChannelsAndRate_ReachTheEngineOnApply() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();

        live.Pick("comboBoxWaveLoopbackChannel", "Left");
        live.Pick("comboBoxWaveInputChannel", "Right");
        live.Pick("comboBoxSampleRate", "96000");
        live.Pick("comboBoxPlaybackDevice", "Default playback device");
        live.Apply();

        Assert.Equal(1, live.Engine.WaveInputChannelOffset);
        Assert.Equal(0, live.Engine.WaveLoopbackInputChannelOffset);
        Assert.Equal(96_000, live.Engine.SampleRate);
        Assert.Equal(-1, live.Engine.OutputDeviceNumber);
        Assert.Equal(1, live.Engine.InputDeviceNumber);
    });

    [Fact]
    public void TheBandFields_KeepTheBandUpright_AndApplyAsEdited() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();
        string before = live.Control<Label>("labelActualRangeCaption").Text;

        live.Number("numericUpDownHighFrequency").Value = 50m;

        Assert.Equal(49m, live.Number("numericUpDownLowFrequency").Value);
        Assert.Equal(1, live.SweepChanges);
        Assert.Equal(49.0, live.Applied().LowFrequencyHz);
        Assert.Equal(50.0, live.Applied().HighFrequencyHz);
        Assert.NotEqual(before, live.Control<Label>("labelActualRangeCaption").Text);
    });

    [Fact]
    public void AShortPace_WarnsOnTheBandLine() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();

        live.Number("numericUpDownRequestedDuration").Value = 5m;

        Label band = live.Control<Label>("labelActualRangeCaption");
        Assert.Equal(UiPalette.Warning, band.ForeColor);
        Assert.StartsWith("⚠ Full amplitude only from", live.ToolTip("labelActualRangeCaption"), StringComparison.Ordinal);
        Assert.Equal(1, live.SweepChanges);
    });

    [Fact]
    public void TheHighPassFields_ChooseTheSlopes_AndApplyAsEdited() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();
        Assert.False(live.Number("numericUpDownProtectiveHighPassFrequency").Enabled);

        live.Pick("comboBoxProtectiveHighPassKind", "Linkwitz-Riley");
        live.Pick("comboBoxProtectiveHighPassSlope", "48 dB/oct");
        live.Number("numericUpDownProtectiveHighPassFrequency").Value = 80m;

        Assert.True(live.Number("numericUpDownProtectiveHighPassFrequency").Enabled);
        Assert.True(live.Combo("comboBoxProtectiveHighPassSlope").Enabled);
        MeasurementSettingsFile.SweepMeasurementSettings applied = live.Applied();
        Assert.Equal(ProtectiveHighPassKind.LinkwitzRiley, applied.ProtectiveHighPassKind);
        Assert.Equal(48, applied.ProtectiveHighPassSlopeDbPerOctave);
        Assert.Equal(80.0, applied.ProtectiveHighPassFrequencyHz);
    });

    [Fact]
    public void TheAveragingAndPlaybackChannel_ApplyAsEdited() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();

        live.Number("numericUpDownAverageRunCount").Value = 5m;
        live.Pick("comboBoxChannel", "Left");

        MeasurementSettingsFile.SweepMeasurementSettings applied = live.Applied();
        Assert.Equal(5, applied.AverageRunCount);
        Assert.Equal(PlaybackChannel.Left, applied.PlaybackChannel);
        Assert.Equal(2, live.SweepChanges);
    });

    [Fact]
    public void TheMeasurementCalibrationPick_AppliesAsEdited() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();

        live.Pick("comboBoxMicrophoneCalibration", "extra");

        Assert.Equal("cal-extra", live.Applied().MicrophoneCalibrationId);
        Assert.Equal(1, live.SweepChanges);
        Assert.StartsWith("The calibration the measurement microphone", live.ToolTip("comboBoxMicrophoneCalibration"), StringComparison.Ordinal);
    });

    [Fact]
    public void ClearingTheCalibrations_AnnouncesThemAndResetsTheButtons() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();
        Assert.StartsWith("94 dB · +124", live.Control<Button>("buttonSplCalibration").Text, StringComparison.Ordinal);

        live.Control<Button>("buttonClearSplCalibration").PerformClick();
        live.Control<Button>("buttonClearCalibration0").PerformClick();

        Assert.Equal(2, live.Calibrations.Count);
        Assert.Null(live.Calibrations[0].SplCalibration);
        Assert.Null(live.Calibrations[1].MicrophoneCalibration0DegreesPath);
        Assert.Equal("Calibrate...", live.Control<Button>("buttonSplCalibration").Text);
        Assert.False(live.Control<Button>("buttonClearSplCalibration").Enabled);
        Assert.Equal("Select file...", live.Control<Button>("buttonCalibration0").Text);
        Assert.Equal("No calibration file selected.", live.ToolTip("buttonCalibration0"));
    });

    [Fact]
    public void TheSplAnchor_GoesStaleWhenTheMicrophoneMoves() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();
        Button spl = live.Control<Button>("buttonSplCalibration");
        Assert.Equal(UiPalette.TextPrimary, spl.ForeColor);

        live.Pick("comboBoxWaveLoopbackChannel", "Left");
        live.Pick("comboBoxWaveInputChannel", "Right");

        Assert.Equal(UiPalette.Warning, spl.ForeColor);
        Assert.EndsWith("recalibrate.", live.ToolTip("buttonSplCalibration"), StringComparison.Ordinal);
    });

    [Fact]
    public void CalibrationsAdoptedBehindThePanel_ShowAndApply() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();

        live.Form.AdoptAdditionalCalibrations(
        [
            new MicrophoneCalibrationDefinition { Id = "cal-extra", Name = "extra", Kind = MicrophoneCalibrationKind.File, Path = "extra.cal" },
            new MicrophoneCalibrationDefinition { Id = "cal-new", Name = "new", Kind = MicrophoneCalibrationKind.File, Path = "new.cal" }
        ]);

        Assert.Equal("Manage... (2)", live.Control<Button>("buttonCalibrationExtra").Text);
        Assert.Contains("new", live.Items("comboBoxMicrophoneCalibration"));
        live.Apply();
        Assert.Equal(2, live.Settings.AdditionalMicrophoneCalibrations.Count);
    });

    [Fact]
    public void AnEndpointChange_RebuildsTheOpenWasapiRoute() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings(settings =>
        {
            settings.AudioBackend = AudioBackend.WasapiShared;
            settings.WasapiCaptureEndpointId = "{capture}";
            settings.WasapiCaptureEndpointName = "Interface in";
            settings.WasapiRenderEndpointId = "{render}";
        });
        Assert.Equal("Output endpoint", live.Control<Label>("labelPlaybackDevice").Text);
        Assert.Equal("Interface in (Default)", live.Text("comboBoxRecordingDevice"));

        live.Devices.Capture.Clear();
        live.Devices.RaiseEndpointsChanged();
        live.Settle();

        Assert.Equal("[Unavailable] Interface in", live.Text("comboBoxRecordingDevice"));
        Assert.StartsWith("⚠ A saved endpoint is unavailable", live.Control<Label>("labelWaveLoopbackStatus").Text, StringComparison.Ordinal);
    });

    [Fact]
    public void OneEditPresentsOnce_SoTheHardwareIsAskedOnce() => StaTest.Run(() =>
    {
        static void Exclusive(MeasurementSettingsFile.SweepMeasurementSettings settings)
        {
            settings.AudioBackend = AudioBackend.WasapiExclusive;
            settings.WasapiCaptureEndpointId = "{capture}";
            settings.WasapiRenderEndpointId = "{render}";
        }

        using var live = new LiveRecordSettings(Exclusive);
        int before = live.Devices.ExclusiveChecks;
        live.Pick("comboBoxRecordingDevice", "Four in");
        int byTheForm = live.Devices.ExclusiveChecks - before;

        FakeRecordDevices bare = LiveRecordSettings.Machine();
        var session = new RecordSettingsSession(bare);
        session.Load(live.Settings);
        before = bare.ExclusiveChecks;
        session.RecordingDevice.SelectedIndex = session.RecordingDevice.Items.ToList().FindIndex(item => item.ToString() == "Four in");
        int bySession = bare.ExclusiveChecks - before;

        // The session's own re-probes, plus one read of the status line.
        Assert.Equal(bySession + 1, byTheForm);
        Assert.Equal("Input 1", live.Text("comboBoxWaveInputChannel"));
    });

    [Fact]
    public void APresentFromOutsideAnEdit_KeepsTextBeingTyped() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();
        TextBox editor = live.Number("numericUpDownLowFrequency").Controls.OfType<TextBox>().Single();

        editor.Text = "150";
        live.Form.AdoptAdditionalCalibrations([]);

        Assert.Equal("150", editor.Text);
    });

    [Fact]
    public void TheAsioInputTest_StaysDisabledWhileItRuns() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings(settings => settings.AudioBackend = AudioBackend.Asio);
        live.Devices.PendingProbe = new TaskCompletionSource<IReadOnlyList<AsioInputProbeChannelResult>>();
        Button probe = live.Control<Button>("buttonAsioInputProbe");
        Assert.True(probe.Enabled);

        probe.PerformClick();
        live.Number("numericUpDownAverageRunCount").Value = 4m;

        Assert.False(probe.Enabled);
        Assert.Equal("Testing...", probe.Text);
    });

    [Fact]
    public void ARouteRuleThatThrows_StillLeavesTheControlsShowingTheSession() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings();
        live.Devices.RatesThrow = true;

        Assert.Throws<InvalidOperationException>(() => live.Pick("comboBoxRecordingDevice", "Mono mic"));

        Assert.Equal("Mono mic", live.Text("comboBoxRecordingDevice"));
        Assert.Equal("None", live.Text("comboBoxWaveLoopbackChannel"));
    });

    [Fact]
    public void AnEditOutsideTheRoute_DoesNotAskTheHardwareAgain() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings(settings =>
        {
            settings.AudioBackend = AudioBackend.WasapiExclusive;
            settings.WasapiCaptureEndpointId = "{capture}";
            settings.WasapiRenderEndpointId = "{render}";
        });
        int before = live.Devices.ExclusiveChecks;

        live.Number("numericUpDownRequestedDuration").Value = 500m;
        live.Number("numericUpDownLowFrequency").Value = 40m;

        Assert.Equal(before, live.Devices.ExclusiveChecks);
    });

    [Fact]
    public void TheAsioControlPanel_ReReadsTheDriver() => StaTest.Run(() =>
    {
        using var live = new LiveRecordSettings(settings => settings.AudioBackend = AudioBackend.Asio);
        int opens = live.Devices.AsioOpenCount;

        live.Control<Button>("buttonAsioControlPanel").PerformClick();

        Assert.Equal(["Card"], live.Devices.ControlPanels);
        Assert.True(live.Devices.AsioOpenCount > opens);
    });

    [Fact]
    public void ClosingThePanel_LetsGoOfTheDevices() => StaTest.Run(() =>
    {
        var live = new LiveRecordSettings();

        live.Dispose();

        Assert.True(live.Devices.Disposed);
    });

    private sealed class LiveRecordSettings : IDisposable
    {
        public LiveRecordSettings(Action<MeasurementSettingsFile.SweepMeasurementSettings>? edit = null)
        {
            Settings = new MeasurementSettingsFile.SweepMeasurementSettings
            {
                AudioBackend = AudioBackend.Wave,
                SampleRate = 48_000,
                OutputDeviceNumber = 0,
                InputDeviceNumber = 1,
                WaveInputChannelOffset = 0,
                WaveLoopbackInputChannelOffset = 1,
                LowFrequencyHz = 100,
                HighFrequencyHz = 20_000,
                RequestedDurationSeconds = 2,
                PlaybackChannel = PlaybackChannel.Stereo,
                AsioDriverName = "Card",
                AsioInputChannelOffset = 0,
                AsioLoopbackInputChannelOffset = 1,
                AdditionalMicrophoneCalibrations =
                [
                    new MicrophoneCalibrationDefinition { Id = "cal-extra", Name = "extra", Kind = MicrophoneCalibrationKind.File, Path = "extra.cal" }
                ],
                MicrophoneCalibration0DegreesPath = "zero.cal",
                SplCalibration = new SplCalibration
                {
                    ReferenceLevelDbSpl = 94,
                    MeasuredLevelDbFs = -30,
                    MeasuredFrequencyHz = 1_000,
                    Backend = AudioBackend.Wave,
                    SampleRate = 48_000,
                    Bits = 24,
                    MicrophoneChannelOffset = 0,
                    InputDeviceNumber = 1
                }
            };
            edit?.Invoke(Settings);
            Engine = new ExpSweepMeasurement(new FakeAudioSessionFactory());
            Engine.Init(new SweepMeasurementConfiguration(
                new SweepSignalConfiguration(20, 20_000, 48_000, 24, 0.2, PlaybackChannel.Mono),
                new SweepAudioConfiguration(WaveInputChannelOffset: 0, WaveLoopbackInputChannelOffset: 1),
                new SweepAveragingConfiguration(1)));
            Form = new MeasurementOptions(new FakeAudioSessionFactory(), Devices);
            Form.SweepSettingsChanged += () => SweepChanges++;
            Form.CalibrationChanged += selection => Calibrations.Add(selection);
            Form.Init(Engine, Settings);
            Form.Show();
            Settle();
        }

        public FakeRecordDevices Devices { get; } = Machine();
        public MeasurementSettingsFile.SweepMeasurementSettings Settings { get; }
        public ExpSweepMeasurement Engine { get; }
        public MeasurementOptions Form { get; }
        public int SweepChanges { get; private set; }
        public List<RecordCalibrationSelection> Calibrations { get; } = [];

        public T Control<T>(string name) where T : Control =>
            All(Form).OfType<T>().Single(control => control.Name == name);

        public ThemedComboBox Combo(string name) => Control<ThemedComboBox>(name);

        public ThemedNumericUpDown Number(string name) => Control<ThemedNumericUpDown>(name);

        public string Text(string name)
        {
            ThemedComboBox combo = Combo(name);
            return combo.GetItemText(combo.SelectedItem);
        }

        public List<string> Items(string name)
        {
            ThemedComboBox combo = Combo(name);
            return combo.Items.Cast<object>().Select(combo.GetItemText).ToList();
        }

        public string ToolTip(string name) =>
            Form.ToolTips.GetToolTip(Control<Control>(name)) ?? string.Empty;

        public void Pick(string name, string text)
        {
            ThemedComboBox combo = Combo(name);
            int index = Items(name).IndexOf(text);
            Assert.True(index >= 0, $"no '{text}' in {name}: {string.Join(", ", Items(name))}");
            combo.SelectedIndex = index;
            Settle();
        }

        public void Apply() => Form.SetOptions(Engine, Settings);

        public MeasurementSettingsFile.SweepMeasurementSettings Applied()
        {
            var applied = new MeasurementSettingsFile.SweepMeasurementSettings { SampleRate = 48_000 };
            Form.ApplySweepSettings(applied);
            return applied;
        }

        public static FakeRecordDevices Machine()
        {
            var devices = new FakeRecordDevices();
            devices.Playback[1] = new AudioDeviceInfo(0, "Speakers", 2);
            devices.Recording.Add(new AudioDeviceInfo(2, "Line in 3-4 (A Very Long Interface Name That Needs A Wide Drop-Down List)", 2));
            devices.Capture.Add(FakeRecordDevices.Endpoint("{four}", "Four in", AudioEndpointDirection.Capture, 48_000, 4));
            devices.AsioDrivers["Card"] = FakeRecordDevices.Asio("Card", 4, 2, 44_100, 48_000);
            return devices;
        }

        public void Settle()
        {
            for (int i = 0; i < 5; i++)
            {
                StaTest.Pump();
            }
        }

        public void Dispose()
        {
            Form.Dispose();
            Engine.Dispose();
        }

        private static IEnumerable<Control> All(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in All(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
