using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>The options object is written from outside (persisted settings, history restores), so the panel must re-read it.</summary>
public sealed class TimeAlignmentBandModeSyncTests
{
    [Fact]
    public void SettingsLoadedAfterThePanelWasBuiltReachTheRadios() => StaTest.Run(() =>
    {
        var options = new TimeAlignmentOptions
        {
            BandMode = TimeAlignmentBandMode.AutoBand
        };
        using Harness harness = Harness.Create(options);

        options.BandMode = TimeAlignmentBandMode.FullBand;
        harness.Controller.RefreshConfiguration();

        Assert.True(harness.Panel.BandModeFullRadio.Checked);
        Assert.False(harness.Panel.BandModeAutoRadio.Checked);
    });

    [Fact]
    public void ARestoredManualBandBringsItsNumbersWithIt() => StaTest.Run(() =>
    {
        var options = new TimeAlignmentOptions
        {
            BandMode = TimeAlignmentBandMode.AutoBand
        };
        using Harness harness = Harness.Create(options);

        options.BandMode = TimeAlignmentBandMode.ManualBand;
        options.BandpassCenterHz = 2_500;
        options.BandpassPassOctaves = 2;
        options.BandpassFadeOctaves = 1.5;
        harness.Controller.RefreshConfiguration();

        Assert.True(harness.Panel.BandModeManualRadio.Checked);
        Assert.Equal(2_500m, harness.Panel.BandpassCenterNumeric.Value);
        Assert.Equal(2m, harness.Panel.BandpassPassOctavesNumeric.Value);
        Assert.Equal(1.5m, harness.Panel.BandpassFadeOctavesNumeric.Value);
        Assert.True(harness.Panel.BandpassCenterNumeric.Enabled);
    });

    [Fact]
    public void ReReadingTheOptionsDoesNotRewriteThem() => StaTest.Run(() =>
    {
        var options = new TimeAlignmentOptions
        {
            BandMode = TimeAlignmentBandMode.AutoBand
        };
        using Harness harness = Harness.Create(options);

        options.BandMode = TimeAlignmentBandMode.FullBand;
        harness.Controller.RefreshConfiguration();

        // Filling controls raises the user-edit events; a refresh must not persist as an edit.
        Assert.Equal(TimeAlignmentBandMode.FullBand, options.BandMode);
        Assert.Equal(0, harness.Saves);
    });

    private sealed class Harness : IDisposable
    {
        private Harness(
            Form form, TimeAlignmentPanel panel, TimeAlignmentPanelController controller)
        {
            Form = form;
            Panel = panel;
            Controller = controller;
        }

        public Form Form { get; }

        public TimeAlignmentPanel Panel { get; }

        public TimeAlignmentPanelController Controller { get; }

        public int Saves { get; private set; }

        public static Harness Create(TimeAlignmentOptions options)
        {
            var form = new Form();
            var panel = new TimeAlignmentPanel();
            form.Controls.Add(panel);
            var measurement = new AnalyzerDocument();
            Harness? harness = null;
            var controller = new TimeAlignmentPanelController(
                form,
                panel,
                options,
                measurement,
                () => harness!.Saves++,
                new CompareSelection());
            harness = new Harness(form, panel, controller);
            return harness;
        }

        public void Dispose()
        {
            Controller.Dispose();
            Form.Dispose();
        }
    }
}
