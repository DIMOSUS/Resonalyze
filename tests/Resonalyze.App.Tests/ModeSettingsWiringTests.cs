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
    [Trait("Category", "Slow")]
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

    [Fact]
    public void AShrunkWindowClampsTheFadesOnScreen_AndGrowingItBackReturnsThem()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            var options = new FrequencyResponseOptions { Window = 8192, LeftTukeyWindow = 256, RightTukeyWindow = 256 };
            var visibility = new CurveVisibilityOptions();
            using var docked = new DockedSettingsPanel<FROptions>(
                () => new FROptions(),
                panel => panel.Init(analyzer.Document, 48_000, options, visibility, []),
                panel => panel.SetOptions(options, visibility));

            docked.Type("numericWindow", 300);
            AssertFades(docked, 300, 256, 44);
            Assert.Equal(1, docked.TakeApplies());
            Assert.Equal((256, 44), (options.LeftTukeyWindow, options.RightTukeyWindow));
            docked.Type("numericWindow", 100);
            AssertFades(docked, 100, 100, 0);
            docked.Type("numericLeftWindow", 20);
            AssertFades(docked, 100, 20, 80);
            docked.Type("numericWindow", 8192);
            AssertFades(docked, 8192, 20, 256);
            Assert.Equal(1, docked.TakeApplies());
            Assert.Equal((20, 256), (options.LeftTukeyWindow, options.RightTukeyWindow));
        });
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void TheRateAndCaptureTimeFollowTheOpenMeasurement()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            analyzer.Open(Transfer(48_000, peak: 480));
            var waterfall = new WaterfallGenerateOptions { SliceCount = 64, Step = 96 };
            var burst = new WaterfallGenerateOptions { Window = 9600 };
            using var waterfallPanel = new DockedSettingsPanel<WaterfallOptions>(
                () => new WaterfallOptions(),
                panel => panel.Init(analyzer.Document, 44_100, waterfall));
            using var burstPanel = new DockedSettingsPanel<BDOpt>(
                () => new BDOpt(),
                panel => panel.Init(analyzer.Document, 44_100, burst));
            Assert.Equal((48_000m, 128m), (waterfallPanel.Value("numericSampleRate"), waterfallPanel.Value("numericCaptureTime")));
            Assert.Equal((48_000m, 200m), (burstPanel.Value("numericSampleRate"), burstPanel.Value("numericCaptureTime")));

            analyzer.Open(Transfer(96_000, peak: 960));
            waterfallPanel.Settle();

            Assert.Equal((96_000m, 64m), (waterfallPanel.Value("numericSampleRate"), waterfallPanel.Value("numericCaptureTime")));
            Assert.Equal((96_000m, 100m), (burstPanel.Value("numericSampleRate"), burstPanel.Value("numericCaptureTime")));
            Assert.Equal(0, waterfallPanel.TakeApplies() + burstPanel.TakeApplies());

            analyzer.Open(Transfer(384_000, peak: 3_840, length: 65_536));
            waterfallPanel.Settle();

            Assert.Equal((384_000m, 16m), (waterfallPanel.Value("numericSampleRate"), waterfallPanel.Value("numericCaptureTime")));
        });
    }

    [Fact]
    public void TheBandCentresFollowTheOpenMeasurementsRate()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            analyzer.Open(Transfer(96_000, peak: 960));
            var options = new ImpulseResponseOptions { BandFilterOctaves = 1.0 / 3.0, BandCenterHz = 20_000 };
            using var docked = new DockedSettingsPanel<IROpt>(
                () => new IROpt(),
                panel => panel.Init(analyzer.Document, 48_000, options),
                panel => panel.SetOptions(options));
            Assert.Equal("20 kHz", docked.Selected("comboBandCenter"));

            analyzer.Open(Transfer(44_100, peak: 441));
            docked.Settle();

            Assert.Equal("16 kHz", docked.Items("comboBandCenter")[^1]);
            Assert.Equal("16 kHz", docked.Selected("comboBandCenter"));
            Assert.Equal(0, docked.TakeApplies());
            docked.Click("checkInvert");
            Assert.Equal(1, docked.TakeApplies());
            Assert.Equal(16_000, options.BandCenterHz);
        });
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void TheAutoTauIsNotReadWhileTheDocumentIsBusy()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            analyzer.Open(Transfer(48_000, peak: 480, echo: 480));
            var options = new FrequencyResponseOptions
            {
                PhaseGateAutoFit = false,
                PhaseGateOffsetMs = 10.0,
                PhasePlateauMs = 2.0,
                PhaseRightMs = 0.5,
                PhaseWindowMode = PhaseWindowMode.Fixed,
                PhaseDetrendMode = PhaseDetrendMode.Auto
            };
            var refusals = 0;
            using var docked = new DockedSettingsPanel<PROpt>(
                () => new PROpt(),
                panel =>
                {
                    panel.PlayRefusal = () => refusals++;
                    panel.Init(analyzer.Document, 48_000, options, new CurveVisibilityOptions());
                });
            decimal before = docked.Value("numericOffset");

            using (analyzer.Document.TryAcquire())
            {
                docked.Type("numericWindow", 15.0m);
                Assert.Equal(before, docked.Value("numericOffset"));
            }

            docked.Settle();
            Assert.NotEqual(before, docked.Value("numericOffset"));
            Assert.Equal(0, refusals);
        });
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void TheTauButtonsTakeAnEstimateAsTheUsersValue_OrRefuse()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            var options = new FrequencyResponseOptions
            {
                PhaseGateAutoFit = false,
                PhaseGateOffsetMs = 10.0,
                PhasePlateauMs = 15.0,
                PhaseDetrendMode = PhaseDetrendMode.Manual,
                PhaseDetrendMs = 0.25
            };
            var visibility = new CurveVisibilityOptions();
            var refusals = 0;
            using var docked = new DockedSettingsPanel<PROpt>(
                () => new PROpt(),
                panel =>
                {
                    panel.PlayRefusal = () => refusals++;
                    panel.Init(analyzer.Document, 48_000, options, visibility);
                },
                panel => panel.SetOptions(options, visibility));

            docked.Click("buttonTauSlope");
            Assert.Equal((1, 0, 0.25m), (refusals, docked.TakeApplies(), docked.Value("numericOffset")));

            var ringing = new Complex[16_384];
            for (int i = 0; i < 2_000; i++)
            {
                ringing[480 + i] = new Complex(Math.Exp(-i / 200.0) * Math.Cos(i * 0.05), 0.0);
            }

            analyzer.Open(TestMeasurementResults.Restored(
                20, 20_000, 48_000, 24, 1.0, PlaybackChannel.Mono, ringing, 480,
                SweepMeasurementMode.LoopbackTransfer, ringing, 480));
            docked.Settle();
            GatedAnalysisSettingsSession shadow = GatedAnalysisSettingsSession.ForPhase();
            shadow.Load(options, visibility);
            (double slopeMs, double peakMs) = PhaseDetrendEstimate.Estimate(analyzer.Document, shadow.DetrendReading())!.Value;
            Assert.NotEqual(ModeSettingsLimits.DetrendMs.Clamp(slopeMs), ModeSettingsLimits.DetrendMs.Clamp(peakMs));
            docked.Click("buttonTauPeak");
            Assert.Equal(ModeSettingsLimits.DetrendMs.Clamp(peakMs), docked.Value("numericOffset"));
            Assert.Equal(1, docked.TakeApplies());
            Assert.Equal((double)ModeSettingsLimits.DetrendMs.Clamp(peakMs), options.PhaseDetrendMs);
            docked.Click("buttonTauSlope");
            Assert.Equal(ModeSettingsLimits.DetrendMs.Clamp(slopeMs), docked.Value("numericOffset"));
            Assert.Equal(1, docked.TakeApplies());
            docked.Click("buttonTauSlope");
            Assert.Equal(0, docked.TakeApplies());

            using (analyzer.Document.TryAcquire())
            {
                docked.Click("buttonTauPeak");
            }

            Assert.Equal(2, refusals);
            docked.Pick("comboDetrendMode", "Off");
            Assert.Equal((0m, false), (docked.Value("numericOffset"), docked.Find<Button>("buttonTauPeak").Enabled));
            docked.Pick("comboDetrendMode", "Manual");
            Assert.Equal(ModeSettingsLimits.DetrendMs.Clamp(slopeMs), docked.Value("numericOffset"));
        });
    }

    [Fact]
    public void TheAutocorrelationSwitchWritesOnlyItself()
    {
        StaTest.Run(() =>
        {
            var options = new ImpulseResponseOptions { ShowAutocorrelation = true, Length = 1234 };
            using var docked = new DockedSettingsPanel<ACOpt>(
                () => new ACOpt(),
                panel => panel.Init(options),
                panel =>
                {
                    options.Length = 7;
                    panel.SetOptions(options);
                });

            docked.Click("checkBoxShowAutocorrelation");

            Assert.Equal(1, docked.TakeApplies());
            Assert.Equal((false, 7), (options.ShowAutocorrelation, options.Length));
        });
    }

    internal static MeasurementResult Transfer(int sampleRate, int peak, int length = 16_384, int? echo = null)
    {
        var impulse = new Complex[length];
        impulse[peak] = Complex.One;
        impulse[peak + (echo ?? sampleRate / 1_000)] = new Complex(echo == null ? -0.4 : 1.0, 0.0);
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
