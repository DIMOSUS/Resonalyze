using System.Windows.Forms;

namespace Resonalyze.App.Tests;

/// <summary>
/// The Time Alignment band selector against the options object it draws. That object is
/// shared and is written from outside the panel — the persisted settings land in it
/// after the controllers were built, and every history entry restores its own session
/// into it — so the panel has to re-read it, not assume it still says what it said when
/// the radios were first filled.
/// </summary>
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

        // What ApplyPersistedSettings and ApplySessionSnapshot both do: write the
        // shared object, touching no control.
        options.BandMode = TimeAlignmentBandMode.FullBand;
        harness.Controller.RefreshConfiguration();

        // The old failure read exactly like a detection that did not happen: the radio
        // still said Auto, the band caption sat on "-", and the analysis ran over the
        // whole spectrum — because the options underneath said FullBand all along.
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
        // Only the manual mode owns those boxes.
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

        // Filling the controls raises the same events a user's edits do, and those
        // read the controls back into the options and persist them. A refresh must not
        // count as an edit — or restoring a session would immediately overwrite the
        // settings file with it.
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
            var measurement = new ExpSweepMeasurement(new FakeAudioSessionFactory());
            Harness? harness = null;
            var controller = new TimeAlignmentPanelController(
                form,
                panel,
                options,
                measurement,
                () => harness!.Saves++,
                () => null,
                () => null);
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
