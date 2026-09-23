using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

/// <summary>
/// A real panel driven through its controls, read by what it draws and reports to the host. The readers have their
/// own tests; these pin the wiring between the controls, the session and the readers, which only the panel path
/// exercises. Synthetic measurements: the right side plays 6 dB below the left, so every reading names its side.
/// </summary>
public sealed class VirtualCrossoverPanelWiringTests
{
    private const int SampleRate = 48_000;
    private const double RightAmplitude = 0.5;

    [Fact]
    public void TheSumToggle_DrawsBothSidesSums_OnlyWhileTicked()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();

            Assert.Contains("Sum", live.MainTitles());
            Assert.Contains("Sum R", live.MainTitles());

            live.Set<CheckBox>("checkBoxShowSum", box => box.Checked = false);

            Assert.DoesNotContain("Sum", live.MainTitles());
            Assert.DoesNotContain("Sum R", live.MainTitles());
        });
    }

    [Fact]
    public void TheSideSelector_DrawsTheSideItNames_AndEveryBlockFollowsIt()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            double left = live.LevelDb("A", 100);

            live.ShowRight();

            Assert.Equal(20 * Math.Log10(RightAmplitude), live.LevelDb("A", 100) - left, 1);
            Assert.Contains("Sum L", live.MainTitles());
            live.Click("buttonAddChannel");
            Assert.All(live.Session.Channels, channel => Assert.True(channel.ActiveRight));
            Assert.Throws<InvalidOperationException>(() => live.Session.Channels[0].ActiveRight = false);
        });
    }

    [Fact]
    public void TheGroupView_FiltersTheDrawnBlocksAndTheQuotedJunctions()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            live.Session.Channels[2].Pair.Zone = VirtualCrossoverZone.Rear;
            live.Redraw();

            Assert.Contains("A", live.MainTitles());
            Assert.DoesNotContain("C", live.MainTitles());
            Assert.Contains("A/B", live.Metric);
            Assert.DoesNotContain("B/C", live.Metric);

            live.Set<ThemedComboBox>("comboBoxGroupView", box => box.SelectedItem = VirtualCrossoverGroupView.Everything);
            Assert.Contains("C", live.MainTitles());

            live.Set<ThemedComboBox>(
                "comboBoxGroupView", box => box.SelectedItem = VirtualCrossoverGroupView.GroupsCompared);
            Assert.Contains(VirtualCrossoverZones.DisplayName(VirtualCrossoverZone.Front), live.MainTitles());
            Assert.Contains(VirtualCrossoverZones.DisplayName(VirtualCrossoverZone.Rear), live.MainTitles());
            Assert.DoesNotContain("A", live.MainTitles());
        });
    }

    [Fact]
    public void TheLossSelector_DrawsTheChosenLossOrNone()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();

            live.Set<ThemedComboBox>("comboBoxSumLoss", box => box.SelectedItem = SumLossWindow.Full);
            Assert.Contains("Sum loss", live.MainTitles());

            live.Set<ThemedComboBox>("comboBoxSumLoss", box => box.SelectedItem = SumLossWindow.Direct);
            Assert.Contains("Sum loss (direct)", live.MainTitles());

            live.Set<ThemedComboBox>("comboBoxSumLoss", box => box.SelectedItem = SumLossWindow.Off);
            Assert.DoesNotContain(live.MainTitles(), title => title.StartsWith("Sum loss", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void TheTargetToggle_HangsTheTargetOnTheMagnitudeView()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            live.Panel.SetTargetCurve(new VirtualCrossoverTargetSettings().ToCurve());

            live.Set<CheckBox>("checkBoxShowTarget", box => box.Checked = true);
            Assert.Contains("Target", live.MainTitles());

            live.Set<CheckBox>("checkBoxShowTarget", box => box.Checked = false);
            Assert.DoesNotContain("Target", live.MainTitles());
        });
    }

    [Fact]
    public void TheViewRadios_DrawTheViewTheyName()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();

            live.PickView("radioViewPhase");
            Assert.Contains("Sum", live.MainTitles());
            Assert.DoesNotContain("Sum R", live.MainTitles());

            live.PickView("radioViewStep");
            Assert.Contains("Sum", live.MainTitles());
            Assert.Contains("Sum R", live.MainTitles());

            live.PickView("radioViewImpulse");
            Assert.Contains("A", live.MainTitles());
            Assert.DoesNotContain("Sum", live.MainTitles());
        });
    }

    [Fact]
    public void TheGateWarning_JudgesTheShownSidesGate()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            // Both pins far past every arrival: whichever side is judged, its window misses the channels.
            live.Session.Project.PhaseGateLeft.OffsetMs = 300;
            live.Session.Project.PhaseGateRight.OffsetMs = 300;
            live.Redraw();

            Assert.StartsWith("⚠ L gate at", live.Warning);

            live.ShowRight();

            Assert.StartsWith("⚠ R gate at", live.Warning);
        });
    }

    [Fact]
    public void OwnCalibration_ReadsTheShownSidesOwnFile()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            live.Session.Channels[0].PhysicalSideState(true).MicrophoneCalibration = FlatCalibration(6);
            live.Panel.ConfigureCalibration(_ => null, []);
            live.ShowRight();
            double off = live.LevelDb("A", 100);
            double uncalibrated = live.LevelDb("B", 1_000);

            live.SelectCalibration(VirtualCrossoverCalibrationSelection.OwnId);

            Assert.Equal(6, Math.Abs(live.LevelDb("A", 100) - off), 1);
            Assert.Equal(uncalibrated, live.LevelDb("B", 1_000), 3);
        });
    }

    // The right side plays 6 dB lower; without the Sum (whose dashed opposite curve spans both) each side alone scales differently.
    [Fact]
    public void BothSides_AreDrawnOnOneScale()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            live.Set<CheckBox>("checkBoxShowSum", box => box.Checked = false);
            live.ShowRight();
            live.ShowLeft();
            var left = live.AxisRanges();

            live.ShowRight();

            Assert.Equal(left, live.AxisRanges());
        });
    }

    // The louder left side, once emptied, must stop holding the right side's axis up.
    [Fact]
    public void AnEmptiedHiddenSide_StopsWideningTheScale()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            live.Set<CheckBox>("checkBoxShowSum", box => box.Checked = false);
            live.ShowRight();
            double shared = live.AxisRanges().Value.High;

            foreach (VirtualCrossoverChannel channel in live.Session.Channels)
            {
                channel.PhysicalSideState(false).Clear();
            }

            live.Redraw();
            live.WaitFor(() => live.AxisRanges().Value.High < shared, "drop the emptied side's range");
        });
    }

    [Fact]
    public void TheHybridToggle_QuotesTheSpatialAverage_OnTheShownSide()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();
            foreach (VirtualCrossoverChannel channel in live.Session.Channels)
            {
                channel.PhysicalSideState(false).SpatialAverage = FlatCapture();
                channel.PhysicalSideState(true).SpatialAverage = FlatCapture();
            }

            live.Session.Project.SpatialAverageMode = VirtualCrossoverSpatialAverageMode.MovingMic;
            live.ShowRight();

            live.Set<CheckBox>("checkBoxHybrid", box => box.Checked = true);
            Assert.Contains("Spatial average", live.Metric);

            live.Set<CheckBox>("checkBoxHybrid", box => box.Checked = false);
            Assert.DoesNotContain("Spatial average", live.Metric);
        });
    }

    [Fact]
    public void TheDspModes_DrawTheChainsOrListTheShownJunctions()
    {
        StaTest.Run(() =>
        {
            using var live = new LivePanel();

            live.PickDsp("radioDspPhase");
            Assert.Contains("A filter", live.DspTitles());

            live.PickDsp("radioDspCorrelation");
            Assert.Equal(2, live.Control<ThemedComboBox>("comboBoxCorrelationPair").Items.Count);

            live.Session.Channels[2].Pair.Zone = VirtualCrossoverZone.Rear;
            live.Redraw();
            Assert.Single(live.Control<ThemedComboBox>("comboBoxCorrelationPair").Items);
        });
    }

    private static VirtualCrossoverCalibrationSettings FlatCalibration(double correctionDb) =>
        VirtualCrossoverCalibrationSettings.From(
            CalibrationFile.FromPoints(
                [new CalibrationPoint(20.0, correctionDb), new CalibrationPoint(20_000.0, correctionDb)],
                "flat"),
            $"flat {correctionDb:0.#}",
            null);

    private static LiveCaptureDocument FlatCapture() => new()
    {
        SavedAtUtc = DateTimeOffset.UnixEpoch,
        Title = "flat mmm",
        CurveDb = Enumerable.Repeat(-20.0, 1_024).ToArray(),
        GridStartHz = 20,
        GridStopHz = 20_000,
        Recipe = new LiveCaptureRecipe
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            SampleRateHz = SampleRate
        }
    };

    /// <summary>The shared live panel, its right side 6 dB below the left, read by what it draws.</summary>
    private sealed class LivePanel : IDisposable
    {
        private readonly VirtualCrossoverLivePanel live = new(RightAmplitude);

        public LivePanel()
        {
            Set<RadioButton>("radioViewMagnitude", radio => radio.Checked = true);
            Set<RadioButton>("radioDspMagnitude", radio => radio.Checked = true);
            Set<CheckBox>("checkBoxShowSum", box => box.Checked = true);
        }

        public VirtualCrossoverPanel Panel => live.Panel;

        public VirtualCrossoverSession Session => live.Session;

        public string Metric => live.Metric;

        public string Warning => live.Warning;

        public T Control<T>(string name) where T : Control => live.Find<T>(name);

        public void Click(string button) => live.Click(button);

        // A view toggle and back redraws everything from the session as it now stands.
        public void Redraw()
        {
            CheckBox sum = Control<CheckBox>("checkBoxShowSum");
            sum.Checked = !sum.Checked;
            sum.Checked = !sum.Checked;
            live.Settle();
        }

        public void Set<T>(string name, Action<T> change) where T : Control
        {
            change(Control<T>(name));
            live.Settle();
        }

        public void ShowRight()
        {
            Control<RadioButton>("radioSideLeft").Checked = false;
            Set<RadioButton>("radioSideRight", radio => radio.Checked = true);
        }

        public void WaitFor(Func<bool> condition, string what) => live.Wait(condition, what);

        public void ShowLeft()
        {
            Control<RadioButton>("radioSideRight").Checked = false;
            Set<RadioButton>("radioSideLeft", radio => radio.Checked = true);
        }

        /// <summary>The ranges the main plot renders: the dB axis and the loss axis.</summary>
        public ((double Low, double High) Value, (double Low, double High) Loss) AxisRanges()
        {
            PlotModel model = Model("mainPlotView");
            ((IPlotModel)model).Update(true);
            Axis value = model.Axes.Single(axis => axis.Title == "dB");
            Axis loss = model.Axes.Single(axis => axis.Key == "virtual-crossover:loss");
            return ((value.ActualMinimum, value.ActualMaximum), (loss.ActualMinimum, loss.ActualMaximum));
        }

        public void PickView(string radio)
        {
            foreach (string other in new[]
                { "radioViewMagnitude", "radioViewPhase", "radioViewGroupDelay", "radioViewImpulse", "radioViewStep" })
            {
                Control<RadioButton>(other).Checked = false;
            }

            Set<RadioButton>(radio, button => button.Checked = true);
        }

        public void PickDsp(string radio)
        {
            foreach (string other in new[]
                { "radioDspMagnitude", "radioDspPhase", "radioDspGroupDelay", "radioDspCorrelation", "radioDspCoherence" })
            {
                Control<RadioButton>(other).Checked = false;
            }

            Set<RadioButton>(radio, button => button.Checked = true);
        }

        public void SelectCalibration(string calibrationId)
        {
            var combo = Control<ThemedComboBox>("comboBoxCalibration");
            int index = Enumerable.Range(0, combo.Items.Count).First(position =>
            {
                combo.SelectedIndex = position;
                return MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(combo) == calibrationId;
            });
            combo.SelectedIndex = index;
            live.Settle();
        }

        public List<string> MainTitles() => Titles("mainPlotView");

        public List<string> DspTitles() => Titles("dspPlotView");

        public double LevelDb(string title, double frequencyHz)
        {
            var series = (LineSeries)Model("mainPlotView").Series.Single(item => item.Title == title);
            return series.Points.MinBy(point => Math.Abs(Math.Log(point.X / frequencyHz))).Y;
        }

        public void Dispose() => live.Dispose();

        private List<string> Titles(string plot) =>
            [.. Model(plot).Series.Select(series => series.Title).Where(title => !string.IsNullOrEmpty(title))];

        private PlotModel Model(string plot) => Control<PlotView>(plot).Model!;
    }
}
