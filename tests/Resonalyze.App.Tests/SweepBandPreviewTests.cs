using Resonalyze.Audio;
using Resonalyze.Options;
using Resonalyze.Ui;

namespace Resonalyze.App.Tests;

/// <summary>The achieved-band line under the sweep fields, and the sweep file they describe.</summary>
public sealed class SweepBandPreviewTests
{
    private static RecordSettingsSession Load(FakeRecordDevices? devices = null)
    {
        var session = new RecordSettingsSession(devices ?? new FakeRecordDevices());
        session.Load(new MeasurementSettingsFile.SweepMeasurementSettings
        {
            AudioBackend = AudioBackend.Wave,
            SampleRate = 48_000,
            OutputDeviceNumber = 0,
            InputDeviceNumber = 1,
            WaveLoopbackInputChannelOffset = 1,
            LowFrequencyHz = 20,
            HighFrequencyHz = 20_000,
            RequestedDurationSeconds = 2.0
        });
        return session;
    }

    [Fact]
    public void AnEasySweepCoversItsBand()
    {
        RecordSettingsSession session = Load();
        session.OctavePaceMilliseconds.Value = 300;

        SweepBandView view = SweepBandPreview.Read(session);

        Assert.Equal(UiPalette.Success, view.Color);
        Assert.Equal(SweepBandPreview.CoveredToolTip, view.ToolTip);
        Assert.Contains(" oct · ", view.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AShortPaceFallsShortAtTheBottom()
    {
        RecordSettingsSession session = Load();
        session.OctavePaceMilliseconds.Value = 5;

        SweepBandView view = SweepBandPreview.Read(session);

        Assert.Equal(UiPalette.Warning, view.Color);
        Assert.StartsWith("⚠ Full amplitude only from ", view.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongPaceIsCappedAndSaysSo()
    {
        RecordSettingsSession session = Load();
        session.OctavePaceMilliseconds.Value = 20_000;

        SweepBandView view = SweepBandPreview.Read(session);

        Assert.StartsWith("⚠ Capped at ", view.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAnOpeningRateThereIsNothingToDescribe()
    {
        var devices = new FakeRecordDevices();
        devices.Rates.Clear();

        SweepBandView view = SweepBandPreview.Read(Load(devices));

        Assert.Equal("—", view.Text);
        Assert.StartsWith("No sample rate opens", view.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExportIsTheSweepTheFieldsDescribe()
    {
        RecordSettingsSession session = Load();
        session.OctavePaceMilliseconds.Value = 100;
        session.PlaybackChannel.SelectedIndex = (int)PlaybackChannel.Right;

        SweepFileExport export = SweepBandPreview.Export(session);

        Assert.Equal(48_000, export.SampleRate);
        Assert.Equal(PlaybackChannel.Right, export.Channel);
        Assert.Equal(session.RequestedDurationSeconds(48_000), export.TotalSeconds);
        Assert.EndsWith(".wav", export.SuggestedFileName, StringComparison.Ordinal);
    }
}
