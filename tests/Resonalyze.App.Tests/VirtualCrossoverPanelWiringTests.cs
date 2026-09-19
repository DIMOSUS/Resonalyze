using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using OxyPlot;
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
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int SampleRate = 48_000;
    private const int PeakIndex = 480;
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
            live.Invoke("AddChannel");
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

    /// <summary>Three blocks, a low-pass, a band-pass and a high-pass, each with a measurement on both sides.</summary>
    private sealed class LivePanel : IDisposable
    {
        public LivePanel()
        {
            Panel = new VirtualCrossoverPanel
            {
                MetricChanged = (compact, _) => Metric = compact,
                WarningChanged = (text, _, _) => Warning = text
            };
            List<VirtualCrossoverChannel> channels = Session.Channels;
            for (int index = 0; index < channels.Count; index++)
            {
                channels[index].Pair = Session.Project.Pairs[index];
                foreach (bool rightSide in new[] { false, true })
                {
                    VirtualCrossoverChannelState state = channels[index].PhysicalSideState(rightSide);
                    var impulse = new Complex[16_384];
                    impulse[PeakIndex] = rightSide ? RightAmplitude : 1.0;
                    state.TransferImpulseResponse = impulse;
                    state.TransferPeakIndex = PeakIndex;
                    state.SampleRate = SampleRate;
                }
            }

            Crossover(channels[0], CrossoverKind.LowPass);
            Crossover(channels[1], CrossoverKind.BandPass);
            Crossover(channels[2], CrossoverKind.HighPass);
            Control<RadioButton>("radioViewMagnitude").Checked = true;
            Control<RadioButton>("radioDspMagnitude").Checked = true;
            Control<CheckBox>("checkBoxShowSum").Checked = true;
            Redraw();
        }

        public VirtualCrossoverPanel Panel { get; }

        public VirtualCrossoverSession Session => Panel.Session;

        public string Metric { get; private set; } = string.Empty;

        public string Warning { get; private set; } = string.Empty;

        public T Control<T>(string name) => (T)typeof(VirtualCrossoverPanel).GetField(name, Hidden)!.GetValue(Panel)!;

        public void Invoke(string method, params object[] arguments)
        {
            typeof(VirtualCrossoverPanel).GetMethod(method, Hidden)!.Invoke(Panel, arguments);
            Settle();
        }

        public void Redraw() => Invoke("RedrawAll");

        public void Set<T>(string name, Action<T> change)
        {
            change(Control<T>(name));
            Settle();
        }

        public void ShowRight()
        {
            Control<RadioButton>("radioSideLeft").Checked = false;
            Set<RadioButton>("radioSideRight", radio => radio.Checked = true);
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
            Settle();
        }

        public List<string> MainTitles() => Titles("mainPlotView");

        public List<string> DspTitles() => Titles("dspPlotView");

        public double LevelDb(string title, double frequencyHz)
        {
            var series = (LineSeries)Model("mainPlotView").Series.Single(item => item.Title == title);
            return series.Points.MinBy(point => Math.Abs(Math.Log(point.X / frequencyHz))).Y;
        }

        public void Dispose() => Panel.Dispose();

        private List<string> Titles(string plot) =>
            [.. Model(plot).Series.Select(series => series.Title).Where(title => !string.IsNullOrEmpty(title))];

        private PlotModel Model(string plot) => Control<PlotView>(plot).Model!;

        private void Settle()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                StaTest.Pump();
                // A loop that completes without awaiting leaves its finished task behind rather than null.
                if (Control<Task?>("redrawTask") is not { IsCompleted: false } &&
                    Control<Task?>("correlationRebuildTask") is not { IsCompleted: false })
                {
                    return;
                }

                Thread.Sleep(5);
            }

            Assert.Fail(
                $"The panel did not settle: redraw {Control<Task?>("redrawTask")?.Status}, " +
                $"correlation {Control<Task?>("correlationRebuildTask")?.Status}.");
        }

        private static void Crossover(VirtualCrossoverChannel channel, CrossoverKind kind)
        {
            foreach (bool rightSide in new[] { false, true })
            {
                VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
                settings.CrossoverKind = kind;
                settings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 300, 24);
                settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_000, 24);
            }

            if (kind == CrossoverKind.LowPass)
            {
                channel.SideSettings(false).LowPassEdge = channel.SideSettings(true).LowPassEdge =
                    new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 300, 24);
            }
            else if (kind == CrossoverKind.HighPass)
            {
                channel.SideSettings(false).HighPassEdge = channel.SideSettings(true).HighPassEdge =
                    new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 3_000, 24);
            }
        }
    }
}
