using Resonalyze.Options;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>The calibration buttons and the SPL anchor's verdict, read from a session.</summary>
public sealed class RecordCalibrationsTests
{
    private static SplCalibration CalibrationOn(int deviceNumber, int rate, int channel) =>
        new()
        {
            ReferenceLevelDbSpl = 94,
            MeasuredLevelDbFs = -30,
            MeasuredFrequencyHz = 1_000,
            CapturedAtUtc = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
            Backend = AudioBackend.Wave,
            SampleRate = rate,
            Bits = 24,
            MicrophoneChannelOffset = channel,
            InputDeviceNumber = deviceNumber
        };

    private static RecordSettingsSession Load(SplCalibration? spl = null, string? zeroDegreePath = null)
    {
        var session = new RecordSettingsSession(new FakeRecordDevices());
        session.Load(new MeasurementSettingsFile.SweepMeasurementSettings
        {
            AudioBackend = AudioBackend.Wave,
            SampleRate = 48_000,
            OutputDeviceNumber = 0,
            InputDeviceNumber = 1,
            WaveInputChannelOffset = 0,
            WaveLoopbackInputChannelOffset = 1,
            MicrophoneCalibration0DegreesPath = zeroDegreePath,
            SplCalibration = spl
        });
        return session;
    }

    [Fact]
    public void NoCalibrationsOfferToSelectAndCalibrate()
    {
        RecordCalibrationView view = RecordCalibrations.Read(Load(), canCalibrateSpl: true);

        Assert.Equal("Select file...", view.ZeroDegree.Text);
        Assert.False(view.CanClearZeroDegree);
        Assert.Equal("Manage...", view.ExtraButtonText);
        Assert.Equal("Calibrate...", view.Spl.Text);
        Assert.False(view.CanClearSpl);
        Assert.StartsWith("Measure the offset from a 1 kHz", view.Spl.ToolTip);
    }

    [Fact]
    public void WithoutAudioSessionsTheSplButtonIsUnavailable()
    {
        RecordCalibrationView view = RecordCalibrations.Read(Load(), canCalibrateSpl: false);

        Assert.False(view.SplEnabled);
        Assert.Equal("SPL calibration is unavailable.", view.Spl.ToolTip);
    }

    [Fact]
    public void AnUnreadableZeroDegreeFileShowsItsProblem()
    {
        string path = Path.Combine(Path.GetTempPath(), $"record-settings-{Guid.NewGuid():N}.cal");
        File.WriteAllText(path, "not a calibration");
        try
        {
            RecordCalibrationView view = RecordCalibrations.Read(Load(zeroDegreePath: path), canCalibrateSpl: true);

            Assert.Equal(Path.GetFileName(path), view.ZeroDegree.Text);
            Assert.Equal(UiPalette.Error, view.ZeroDegree.Color);
            Assert.NotEqual(path, view.ZeroDegree.ToolTip);
            Assert.True(view.CanClearZeroDegree);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheSplAnchorGoesStaleWhenAnyPartOfTheInputMoves()
    {
        RecordSettingsSession session = Load(CalibrationOn(deviceNumber: 1, rate: 48_000, channel: 0));
        Assert.True(RecordCalibrations.MatchesSelectedInput(session, session.SplCalibration!));
        Assert.DoesNotContain("recalibrate", RecordCalibrations.Read(session, true).Spl.ToolTip, StringComparison.Ordinal);

        session.WaveInput.SelectedIndex = 1;

        RecordCalibrationView view = RecordCalibrations.Read(session, canCalibrateSpl: true);
        Assert.False(RecordCalibrations.MatchesSelectedInput(session, session.SplCalibration!));
        Assert.Equal(UiPalette.Warning, view.Spl.Color);
        Assert.EndsWith("recalibrate.", view.Spl.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSplCaptureListensOnTheMicrophoneAlone()
    {
        RecordSettingsSession session = Load();

        AudioSessionRequest request = RecordCalibrations.SplCaptureRequest(session);

        Assert.Equal(AudioBackend.Wave, request.Backend);
        Assert.Equal(48_000, request.SampleRate);
        Assert.Equal(0, request.Routing.MicrophoneChannel);
        Assert.Null(request.Routing.LoopbackChannel);
        Assert.Equal(1, request.WaveInputDeviceNumber);
    }
}
