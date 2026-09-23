using System.Windows.Forms;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>Every field of each mode settings panel, edited through its control, reaches what Apply writes; the previews
/// draw what the fields say.</summary>
public sealed class ModeSettingsFieldsWiringTests
{
    [Fact]
    public void TheFrequencyResponseFieldsWriteWhatTheyShow()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            analyzer.Open(ModeSettingsWiringTests.Transfer(48_000, peak: 480));
            var options = new FrequencyResponseOptions();
            var visibility = new CurveVisibilityOptions();
            using var docked = new DockedSettingsPanel<FROptions>(
                () => new FROptions(),
                panel => panel.Init(
                    analyzer.Document,
                    48_000,
                    options,
                    visibility,
                    [new MicrophoneCalibrationEntry("other", "Other mic", true)]),
                panel => panel.SetOptions(options, visibility));

            docked.Pick("comboWindowMode", "FDW");
            docked.Pick("comboFdwCycles", "8");
            docked.Type("numericWindow", 2000);
            docked.Type("numericLeftWindow", 300);
            docked.Type("numericRightWindow", 400);
            docked.Pick("comboSmoothingInverseOctaves", "Psycho");
            docked.Pick("comboCalibration", "Other mic");
            docked.Click("radioMagnitudeSpl");
            Dictionary<string, Func<bool>> boxes = new()
            {
                ["checkBoxShowPrimary"] = () => visibility.ShowPrimary,
                ["checkBoxShowCoherence"] = () => visibility.ShowCoherence,
                ["checkBoxShowArrayAverage"] = () => visibility.ShowArrayAverage,
                ["checkBoxShowArrayMicrophones"] = () => visibility.ShowArrayMicrophones,
                ["checkBoxShowArraySpread"] = () => visibility.ShowArraySpread,
                ["checkBoxShowHd2"] = () => visibility.ShowHd2,
                ["checkBoxShowHd3"] = () => visibility.ShowHd3,
                ["checkBoxShowHd4"] = () => visibility.ShowHd4,
                ["checkBoxShowThdPlusNoise"] = () => visibility.ShowThdPlusNoise,
                ["checkBoxShowNoiseFloor"] = () => visibility.ShowNoiseFloor
            };
            ToggleEach(docked, boxes);

            Assert.Equal(
                (PhaseWindowMode.FrequencyDependent, 8, 2000, 300, 400, SpectrumSmoothing.PsychoacousticCode, "other",
                    MagnitudeScale.SoundPressureLevel),
                (options.MagnitudeWindowMode, options.MagnitudeFdwCycles, options.Window, options.LeftTukeyWindow,
                    options.RightTukeyWindow, (int)options.SmoothingInverseOctaves, options.CalibrationId, options.MagnitudeScale));
            Assert.Equal(
                Words(FrequencyResponseSplChoice.ToolTip(new ModeSettingsMeasurement(analyzer.Result, 48_000))),
                Words(docked.Panel.ToolTips.GetToolTip(docked.Find<RadioButton>("radioMagnitudeSpl"))));
            AssertPreview(docked, view =>
                ImpulseWindowPreview.Update(view, analyzer.Result, 2000, 300, 400, 0, IrPreviewSource.PrimaryAtStart));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePhaseAndGroupDelayFieldsWriteWhatTheyShow(bool phase)
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            analyzer.Open(ModeSettingsWiringTests.Transfer(48_000, peak: 480));
            MeasurementResult compare = ModeSettingsWiringTests.Transfer(48_000, peak: 960);
            var options = new FrequencyResponseOptions();
            var visibility = new CurveVisibilityOptions();
            CompareAnalysisSource source = new("compare", 48_000, compare.Transfer!.ImpulseResponse, 960);
            using var docked = new DockedSettingsPanel<Form>(
                () => phase ? new PROpt() : new GDOpt(),
                panel =>
                {
                    if (panel is PROpt phasePanel)
                    {
                        phasePanel.Init(analyzer.Document, 48_000, options, visibility, () => source);
                    }
                    else
                    {
                        ((GDOpt)panel).Init(analyzer.Document, 48_000, options, visibility, () => source);
                    }
                },
                panel =>
                {
                    if (panel is PROpt phasePanel)
                    {
                        phasePanel.SetOptions(options, visibility);
                    }
                    else
                    {
                        ((GDOpt)panel).SetOptions(options, visibility);
                    }
                });
            var defaults = phase ? GatedAnalysisSettingsSession.ForPhase().Defaults : GatedAnalysisSettingsSession.ForGroupDelay().Defaults;
            Assert.Equal(
                ((decimal?)defaults.LeftMs, (decimal?)defaults.PlateauMs, (decimal?)defaults.RightMs),
                (docked.Find<ThemedNumericUpDown>("numericLeftWindow").DefaultValue,
                    docked.Find<ThemedNumericUpDown>("numericWindow").DefaultValue,
                    docked.Find<ThemedNumericUpDown>("numericRightWindow").DefaultValue));
            Assert.Equal(phase ? null : "FDW", docked.Find<ThemedComboBox>("comboWindowMode").DefaultSelectedItem);
            decimal snapped = docked.Value("numericGateOffset");

            docked.Click("checkAutoFit");
            Assert.True(docked.Find<ThemedNumericUpDown>("numericGateOffset").Enabled);
            docked.Type("numericGateOffset", 12.5m);
            docked.Type("numericLeftWindow", 0.7m);
            docked.Type("numericWindow", 6.25m);
            docked.Type("numericRightWindow", 2.5m);
            docked.Pick("comboWindowMode", "Fixed");
            Assert.False(docked.Find<ThemedComboBox>("comboFdwCycles").Enabled);
            docked.Pick("comboWindowMode", "FDW");
            docked.Pick("comboFdwCycles", "4");
            docked.Pick("comboSmoothingInverseOctaves", "1/3");
            Assert.Equal(GateReadout.ReliableFrom(0.7, 6.25, 2.5), docked.Find<Label>("labelMinFrequency").Text);
            Dictionary<string, Func<bool>> boxes = phase
                ? new()
                {
                    ["checkBoxShowMeasured"] = () => visibility.ShowMeasuredPhase,
                    ["checkBoxShowMinimum"] = () => visibility.ShowMinimumPhase,
                    ["checkBoxShowExcess"] = () => visibility.ShowExcessPhase,
                    ["checkBoxShowCoherence"] = () => visibility.ShowCoherence,
                    ["checkBoxUnwrap"] = () => options.Unwrap
                }
                : new()
                {
                    ["checkBoxShowGroupDelay"] = () => visibility.ShowGroupDelay,
                    ["checkBoxShowMinimumPhaseGroupDelay"] = () => visibility.ShowMinimumPhaseGroupDelay,
                    ["checkBoxShowExcessGroupDelay"] = () => visibility.ShowExcessGroupDelay,
                    ["checkBoxShowCoherence"] = () => visibility.ShowCoherence
                };
            ToggleEach(docked, boxes);

            (bool auto, double offset, double left, double plateau, double right, PhaseWindowMode mode, int cycles) = phase
                ? (options.PhaseGateAutoFit, options.PhaseGateOffsetMs, options.PhaseLeftMs, options.PhasePlateauMs,
                    options.PhaseRightMs, options.PhaseWindowMode, options.PhaseFdwCycles)
                : (options.GroupDelayGateAutoFit, options.GroupDelayGateOffsetMs, options.GroupDelayLeftMs,
                    options.GroupDelayPlateauMs, options.GroupDelayRightMs, options.GroupDelayWindowMode,
                    options.GroupDelayFdwCycles);
            Assert.Equal(
                (false, 12.5, 0.7, 6.25, 2.5, PhaseWindowMode.FrequencyDependent, 4, 3.0),
                (auto, offset, left, plateau, right, mode, cycles, options.SmoothingInverseOctaves));
            AssertPreview(docked, view =>
                ImpulseWindowPreview.UpdateGated(view, analyzer.Result, 12.5, 0.7, 6.25, 2.5, IrPreviewSource.Primary, source));

            docked.Click("checkAutoFit");
            Assert.Equal(snapped, docked.Value("numericGateOffset"));
            Assert.False(docked.Find<ThemedNumericUpDown>("numericGateOffset").Enabled);
        });
    }

    [Fact]
    public void AManualTauIsTheUsersValue_AndOnlyManualOffersTheButtons()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            analyzer.Open(ModeSettingsWiringTests.Transfer(48_000, peak: 480));
            var options = new FrequencyResponseOptions { PhaseDetrendMode = PhaseDetrendMode.Off };
            var visibility = new CurveVisibilityOptions();
            using var docked = new DockedSettingsPanel<PROpt>(
                () => new PROpt(),
                panel => panel.Init(analyzer.Document, 48_000, options, visibility),
                panel => panel.SetOptions(options, visibility));
            AssertTauButtons(docked, false);

            docked.Pick("comboDetrendMode", "Manual");
            AssertTauButtons(docked, true);
            docked.TakeApplies();
            docked.Type("numericOffset", 1.5m);
            Assert.Equal(1, docked.TakeApplies());
            Assert.Equal((1.5, PhaseDetrendMode.Manual), (options.PhaseDetrendMs, options.PhaseDetrendMode));
            docked.Pick("comboDetrendMode", "Auto");
            AssertTauButtons(docked, false);
            Assert.NotEqual(1.5m, docked.Value("numericOffset"));
            docked.Pick("comboDetrendMode", "Manual");
            Assert.Equal(1.5m, docked.Value("numericOffset"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheWaterfallAndBurstFieldsWriteWhatTheyShow(bool burst)
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            analyzer.Open(ModeSettingsWiringTests.Transfer(48_000, peak: 480));
            var options = new WaterfallGenerateOptions();
            using var docked = new DockedSettingsPanel<Form>(
                () => burst ? new BDOpt() : new WaterfallOptions(),
                panel =>
                {
                    if (panel is BDOpt burstPanel)
                    {
                        burstPanel.Init(analyzer.Document, 48_000, options);
                    }
                    else
                    {
                        ((WaterfallOptions)panel).Init(analyzer.Document, 48_000, options);
                    }
                },
                panel =>
                {
                    if (panel is BDOpt burstPanel)
                    {
                        burstPanel.SetOptions(options);
                    }
                    else
                    {
                        ((WaterfallOptions)panel).SetOptions(options);
                    }
                });

            docked.Type("numericWindow", 4800);
            docked.Type("numericLeftWindow", 20);
            docked.Type("numericRightWindow", 300);
            docked.Type("numericDbRange", -80);
            docked.Type("numericOffset", -120);
            docked.Pick("comboSmoothingInverseOctaves", "1/12");
            if (burst)
            {
                docked.Type("numericPeriods", 12);
                Assert.Equal(100m, docked.Value("numericCaptureTime"));
            }
            else
            {
                docked.Type("numericSlices", 30);
                docked.Type("numericStep", 32);
                Assert.Equal(20m, docked.Value("numericCaptureTime"));
            }

            Assert.Equal(1, docked.TakeApplies());

            Assert.Equal(
                (4800, 20, 300, -80, -120, 12.0),
                (options.Window, options.LeftTukeyWindow, options.RightTukeyWindow, options.DbRange, options.Offset,
                    options.SmoothingInverseOctaves));
            Assert.Equal(burst ? (64, 4, 12.0) : (30, 32, 30.0), (options.SliceCount, options.Step, options.Periods));
            AssertPreview(docked, view =>
                ImpulseWindowPreview.Update(view, analyzer.Result, 4800, 20, 300, -120, IrPreviewSource.Primary));
        });
    }

    [Fact]
    public void TheImpulseFieldsWriteWhatTheyShow()
    {
        StaTest.Run(() =>
        {
            using var analyzer = new TestAnalyzer();
            analyzer.Open(ModeSettingsWiringTests.Transfer(48_000, peak: 480));
            var options = new ImpulseResponseOptions();
            using var docked = new DockedSettingsPanel<IROpt>(
                () => new IROpt(),
                panel => panel.Init(analyzer.Document, 48_000, options),
                panel => panel.SetOptions(options));
            Assert.False(docked.Find<ThemedComboBox>("comboBandCenter").Enabled);

            docked.Type("numericLength", 2048);
            docked.Type("numericEnvelopeSmoothing", 1.25m);
            docked.Pick("comboBandWidth", "1/3 octave");
            Assert.True(docked.Find<ThemedComboBox>("comboBandCenter").Enabled);
            docked.Pick("comboBandCenter", "2.5 kHz".Replace(".", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal));
            docked.Pick("comboAmplitudeScale", "dB re peak");
            docked.Pick("comboTimeUnit", "Samples");
            docked.Pick("comboTimeOrigin", "Peak");
            Dictionary<string, Func<bool>> boxes = new()
            {
                ["checkInvert"] = () => options.Invert,
                ["checkNormalizeStep"] = () => options.NormalizeStepToImpulsePeak,
                ["checkBoxShowImpulse"] = () => options.ShowImpulse,
                ["checkBoxShowEnvelope"] = () => options.ShowEnvelope,
                ["checkBoxShowStep"] = () => options.ShowStep
            };
            ToggleEach(docked, boxes);

            Assert.Equal(
                (2048, 1.25, 1.0 / 3.0, 2_500.0, ImpulseAmplitudeScale.Decibels, ImpulseTimeUnit.Samples, ImpulseTimeOrigin.Peak),
                (options.Length, options.EnvelopeSmoothingMs, options.BandFilterOctaves, options.BandCenterHz,
                    options.AmplitudeScale, options.TimeUnit, options.TimeOrigin));
        });
    }

    private static void ToggleEach<TPanel>(DockedSettingsPanel<TPanel> docked, Dictionary<string, Func<bool>> boxes)
        where TPanel : Form
    {
        docked.TakeApplies();
        foreach ((string name, Func<bool> written) in boxes)
        {
            bool before = docked.Find<CheckBox>(name).Checked;
            docked.Click(name);
            Assert.Equal(1, docked.TakeApplies());
            Assert.Equal(!before, written());
        }
    }

    // The tooltip wraps its lines.
    private static string Words(string? text) => System.Text.RegularExpressions.Regex.Replace(text ?? string.Empty, @"\s+", " ");

    private static void AssertTauButtons(DockedSettingsPanel<PROpt> docked, bool manual) =>
        Assert.Equal(
            (manual, manual, manual),
            (docked.Find<ThemedNumericUpDown>("numericOffset").Enabled, docked.Find<Button>("buttonTauSlope").Enabled,
                docked.Find<Button>("buttonTauPeak").Enabled));

    // The panel's preview against one drawn directly from what its fields say.
    private static void AssertPreview<TPanel>(DockedSettingsPanel<TPanel> docked, Action<PlotView> draw)
        where TPanel : Form
    {
        using var expected = new PlotView();
        draw(expected);
        PlotView shown = docked.Find<PlotView>("irPlotView");
        Assert.Equal(Points(expected), Points(shown));
    }

    private static List<string> Points(PlotView view) =>
        view.Model.Series.OfType<LineSeries>()
            .Select(series => series.Title + ":" + string.Join(";", series.Points.Select(point => $"{point.X:R},{point.Y:R}")))
            .ToList();
}
