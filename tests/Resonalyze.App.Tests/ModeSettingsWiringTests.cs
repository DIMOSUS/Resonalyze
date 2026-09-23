using System.Numerics;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>The mode settings panels in the docked host, driven through their controls: what a user's edit applies,
/// what code moving a field does not, and what the panels show when the open measurement changes.</summary>
public sealed class ModeSettingsWiringTests
{
    [Fact]
    public void AUserEditApplies_ACodeMoveDoesNot()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            var options = new FrequencyResponseOptions { MagnitudeScale = MagnitudeScale.SoundPressureLevel };
            var visibility = new CurveVisibilityOptions();
            using var docked = new DockedSettingsPanel<FROptions>(
                () => new FROptions(),
                panel => panel.Init(analyzer.Document, 48_000, options, visibility, []),
                panel => panel.SetOptions(options, visibility));

            docked.Click("radioMagnitudeRelative");
            Assert.Equal(1, docked.TakeApplies());
            docked.Click("radioMagnitudeSpl");
            Assert.Equal(1, docked.TakeApplies());
            docked.Panel.ForceRelativeScale();
            Assert.True(docked.Find<RadioButton>("radioMagnitudeRelative").Checked);
            Assert.Equal(0, docked.TakeApplies());
            docked.Arrow("comboWindowMode", 1);
            Assert.Equal(0, docked.TakeApplies());
            docked.Pick("comboWindowMode", "Fixed");
            Assert.Equal(1, docked.TakeApplies());
            docked.Type("numericWindow", 300);
            Assert.Equal(1, docked.TakeApplies());
            Assert.Equal((300, MagnitudeScale.Relative), (options.Window, options.MagnitudeScale));
        });
    }

    [Fact]
    public void TheAutoGateFollowsANewMeasurement_WithoutApplying()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            analyzer.Open(Transfer(48_000, peak: 480));
            var options = new FrequencyResponseOptions { GroupDelayGateAutoFit = true };
            var visibility = new CurveVisibilityOptions();
            using var docked = new DockedSettingsPanel<GDOpt>(
                () => new GDOpt(),
                panel => panel.Init(analyzer.Document, 48_000, options, visibility),
                panel => panel.SetOptions(options, visibility));
            decimal first = docked.Value("numericGateOffset");

            analyzer.Open(Transfer(48_000, peak: 2_400));
            docked.Settle();

            Assert.True(docked.Value("numericGateOffset") > first);
            Assert.Equal(0, docked.TakeApplies());
            docked.Click("checkBoxShowGroupDelay");
            Assert.Equal(1, docked.TakeApplies());
            Assert.Equal((double)docked.Value("numericGateOffset"), options.GroupDelayGateOffsetMs);
        });
    }

    [Fact]
    public void AWaterfallStepOntoZeroGoesPastIt_InOneApply()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            analyzer.Open(Transfer(48_000, peak: 480));
            var options = new WaterfallGenerateOptions { Step = 1, SliceCount = 48 };
            using var docked = new DockedSettingsPanel<WaterfallOptions>(
                () => new WaterfallOptions(),
                panel => panel.Init(analyzer.Document, 48_000, options),
                panel => panel.SetOptions(options));

            docked.Type("numericStep", 0);

            Assert.Equal(-1, docked.Value("numericStep"));
            Assert.Equal(-1.0m, docked.Value("numericCaptureTime"));
            Assert.Equal(1, docked.TakeApplies());
            Assert.Equal(-1, options.Step);
            docked.Type("numericStep", 0);
            Assert.Equal(1, docked.Value("numericStep"));
        });
    }

    [Fact]
    public void AStoredTukeyPairLoadsAsStored()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            var frequency = new FrequencyResponseOptions { Window = 4096, LeftTukeyWindow = 3900, RightTukeyWindow = 100 };
            using (var docked = new DockedSettingsPanel<FROptions>(
                       () => new FROptions(),
                       panel => panel.Init(analyzer.Document, 48_000, frequency, new CurveVisibilityOptions(), [])))
            {
                AssertFades(docked, 4096, 3900, 100);
            }

            foreach (bool burst in new[] { false, true })
            {
                var waterfall = new WaterfallGenerateOptions { Window = 1024, LeftTukeyWindow = 600, RightTukeyWindow = 100 };
                if (burst)
                {
                    using var docked = new DockedSettingsPanel<BDOpt>(
                        () => new BDOpt(),
                        panel => panel.Init(analyzer.Document, 48_000, waterfall));
                    AssertFades(docked, 1024, 600, 100);
                }
                else
                {
                    using var docked = new DockedSettingsPanel<WaterfallOptions>(
                        () => new WaterfallOptions(),
                        panel => panel.Init(analyzer.Document, 48_000, waterfall));
                    AssertFades(docked, 1024, 600, 100);
                }
            }
        });
    }

    internal static MeasurementResult Transfer(int sampleRate, int peak, int length = 16_384)
    {
        var impulse = new Complex[length];
        impulse[peak] = Complex.One;
        impulse[peak + (sampleRate / 1_000)] = new Complex(-0.4, 0.0);
        return TestMeasurementResults.Restored(
            20,
            20_000,
            sampleRate,
            24,
            1.0,
            PlaybackChannel.Mono,
            impulse,
            peak,
            SweepMeasurementMode.LoopbackTransfer,
            impulse,
            peak);
    }

    private static void AssertFades<TPanel>(DockedSettingsPanel<TPanel> docked, int window, int left, int right)
        where TPanel : Form =>
        Assert.Equal(
            (window, left, right, window - right, window - left),
            ((int)docked.Value("numericWindow"), (int)docked.Value("numericLeftWindow"),
                (int)docked.Value("numericRightWindow"),
                (int)docked.Find<ThemedNumericUpDown>("numericLeftWindow").Maximum,
                (int)docked.Find<ThemedNumericUpDown>("numericRightWindow").Maximum));
}
