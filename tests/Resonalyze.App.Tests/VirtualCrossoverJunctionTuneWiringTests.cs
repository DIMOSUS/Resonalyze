using System.Numerics;
using System.Windows.Forms;
using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze.App.Tests;

/// <summary>
/// Tune junction through a live panel: the button opens the real dialog, a timer in its modal loop asks and applies,
/// and the cards, the session and the dialog's Undo are read afterwards. The rules have their own tests; these pin the
/// glue between the dialog, <see cref="VirtualCrossoverJunctionTuneApply"/> and the cards.
/// </summary>
public sealed class VirtualCrossoverJunctionTuneWiringTests
{
    private const int SampleRate = 48_000;
    private const int PeakIndex = 480;

    [Fact]
    public void Apply_PutsTheResultOnBothCards_AndUndoPutsEveryChannelBack() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        VirtualCrossoverChannel lower = live.Session.Channels[0];
        VirtualCrossoverChannel upper = live.Session.Channels[1];
        List<(VirtualCrossoverChannelSettings Settings, VirtualCrossoverChannelSettings Before)> before = live.Session
            .Sides()
            .Select(side => side.Channel.SideSettings(side.RightSide))
            .Select(settings => (settings, AgentOperations.CloneEditable(settings)))
            .ToList();
        var asked = new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24);

        string status = live.Tune(dialog =>
        {
            Assert.False(dialog.Find<Button>("buttonUndo").Enabled);
            dialog.Find<RadioButton>("radioAcoustic").Checked = true;
            dialog.Find<ThemedComboBox>("comboBoxGoalSlope").SelectedItem = 24;
        }, apply: true);

        Assert.Contains("Apply writes", status, StringComparison.Ordinal);
        foreach (bool right in new[] { false, true })
        {
            Assert.Equal(asked, lower.SideSettings(right).AcousticLowPass);
            Assert.Equal(asked, upper.SideSettings(right).AcousticHighPass);
        }

        AssertCardShows(live.Card(lower), lower.Settings);
        AssertCardShows(live.Card(upper), upper.Settings);
        Assert.Equal("LR24", live.Card(lower).AcousticGoalButton.Text);
        Assert.Equal("LR24", live.Card(upper).AcousticGoalButton.Text);
        Assert.True(live.Session.Project.JunctionTune!.Acoustic);
        Assert.Equal(asked, live.Session.Project.JunctionTune.Goal);

        live.Tune(dialog =>
        {
            Button undo = dialog.Find<Button>("buttonUndo");
            Assert.True(undo.Enabled);
            undo.PerformClick();
        }, apply: false);

        foreach ((VirtualCrossoverChannelSettings settings, VirtualCrossoverChannelSettings kept) in before)
        {
            Assert.Equal(kept.LowPassEdge, settings.LowPassEdge);
            Assert.Equal(kept.HighPassEdge, settings.HighPassEdge);
            Assert.Equal(kept.CrossoverKind, settings.CrossoverKind);
            Assert.Equal(kept.AcousticLowPass, settings.AcousticLowPass);
            Assert.Equal(kept.AcousticHighPass, settings.AcousticHighPass);
        }

        AssertCardShows(live.Card(lower), lower.Settings);
        AssertCardShows(live.Card(upper), upper.Settings);
        Assert.Equal("—", live.Card(lower).AcousticGoalButton.Text);
        live.Tune(dialog => Assert.False(dialog.Find<Button>("buttonUndo").Enabled), apply: false);
    });

    [Fact]
    public void AGoalForAnEdgeSwitchedOff_IsNoLongerShownAsStated() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        VirtualCrossoverChannel lower = live.Session.Channels[0];
        live.Tune(dialog =>
        {
            dialog.Find<RadioButton>("radioAcoustic").Checked = true;
            dialog.Find<ThemedComboBox>("comboBoxGoalSlope").SelectedItem = 24;
        }, apply: true);
        Assert.Equal("LR24", live.Card(lower).AcousticGoalButton.Text);

        live.Card(lower).CrossoverKindComboBox.SelectedItem = CrossoverKind.Off;

        Assert.Equal(CrossoverKind.Off, lower.Settings.CrossoverKind);
        Assert.Equal("—", live.Card(lower).AcousticGoalButton.Text);
        Assert.Equal(new JunctionAcousticTarget(CrossoverFilterFamily.LinkwitzRiley, 24), lower.Settings.AcousticLowPass);
    });

    [Fact]
    public void Cancel_WritesNothing_ButTheDialogRemembersTheQuestion() => StaTest.Run(() =>
    {
        using var live = new LivePanel();
        string before = string.Join(";", live.Session.Sides()
            .Select(side => live.Session.Channels.IndexOf(side.Channel) + ":" +
                side.Channel.SideSettings(side.RightSide).LowPassEdge + side.Channel.SideSettings(side.RightSide).HighPassEdge));

        live.Tune(dialog =>
        {
            dialog.Find<CheckBox>("checkBessel").Checked = true;
            dialog.Find<CheckBox>("checkBoxSplitCorners").Checked = true;
        }, apply: false);

        Assert.Equal(before, string.Join(";", live.Session.Sides()
            .Select(side => live.Session.Channels.IndexOf(side.Channel) + ":" +
                side.Channel.SideSettings(side.RightSide).LowPassEdge + side.Channel.SideSettings(side.RightSide).HighPassEdge)));
        Assert.Contains(CrossoverFilterFamily.Bessel, live.Session.Project.JunctionTune!.Families);
        Assert.True(live.Session.Project.JunctionTune.SplitCorners);
        live.Tune(dialog => Assert.False(dialog.Find<Button>("buttonUndo").Enabled), apply: false);
    });

    private static void AssertCardShows(VirtualCrossoverChannelControl card, VirtualCrossoverChannelSettings settings)
    {
        if (settings.RunsLowPass)
        {
            Assert.Equal((decimal)settings.LowPassEdge.FrequencyHz, card.LowPassFrequencyInput.Value);
            Assert.Equal(settings.LowPassEdge.SlopeDbPerOctave, card.LowPassSlopeComboBox.SelectedItem);
        }

        if (settings.RunsHighPass)
        {
            Assert.Equal((decimal)settings.HighPassEdge.FrequencyHz, card.HighPassFrequencyInput.Value);
            Assert.Equal(settings.HighPassEdge.SlopeDbPerOctave, card.HighPassSlopeComboBox.SelectedItem);
        }

        Assert.Equal(settings.CrossoverKind, card.CrossoverKindComboBox.SelectedItem);
    }

    /// <summary>Three blocks — a low-pass, a band-pass and a high-pass — with a measurement on both sides.</summary>
    private sealed class LivePanel : IDisposable
    {
        private readonly Form host = new() { Width = 1_400, Height = 900 };
        private string metric = string.Empty;

        public LivePanel()
        {
            Panel = new VirtualCrossoverPanel { Dock = DockStyle.Fill, MetricChanged = (compact, _) => metric = compact };
            host.Controls.Add(Panel);
            List<VirtualCrossoverChannel> channels = Session.Channels;
            for (int index = 0; index < channels.Count; index++)
            {
                channels[index].Pair = Session.Project.Pairs[index];
                foreach (bool rightSide in new[] { false, true })
                {
                    VirtualCrossoverChannelState state = channels[index].PhysicalSideState(rightSide);
                    var impulse = new Complex[16_384];
                    impulse[PeakIndex] = 1.0;
                    state.TransferImpulseResponse = impulse;
                    state.TransferPeakIndex = PeakIndex;
                    state.SampleRate = SampleRate;
                    VirtualCrossoverChannelSettings settings = channels[index].SideSettings(rightSide);
                    settings.CrossoverKind = index switch
                    {
                        0 => CrossoverKind.LowPass,
                        1 => CrossoverKind.BandPass,
                        _ => CrossoverKind.HighPass
                    };
                    settings.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, index == 1 ? 300 : 3_000, 24);
                    settings.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, index == 0 ? 300 : 3_000, 24);
                }
            }

            host.Show();
            // Any view toggle redraws, and the metric names the junctions once the processed blocks are in.
            var sum = (CheckBox)Panel.Controls.Find("checkBoxShowSum", searchAllChildren: true).Single();
            sum.Checked = !sum.Checked;
            sum.Checked = !sum.Checked;
            Wait(() => metric.Contains("A/B", StringComparison.Ordinal) && metric.Contains("B/C", StringComparison.Ordinal));
        }

        public VirtualCrossoverPanel Panel { get; }

        public VirtualCrossoverSession Session => Panel.Session;

        public VirtualCrossoverChannelControl Card(VirtualCrossoverChannel channel) =>
            Panel.Controls.Find("channelListPanel", searchAllChildren: true).Single()
                .Controls.OfType<VirtualCrossoverChannelControl>()
                .Single(card => card.ChannelName == channel.Name);

        /// <summary>Opens Tune junction on the first junction; <paramref name="ask"/> sets the question. With
        /// <paramref name="apply"/> it searches and applies, otherwise the dialog is left by whatever ask did, or
        /// cancelled. Returns the status line the search left.</summary>
        public string Tune(Action<TuneDialog> ask, bool apply)
        {
            string status = string.Empty;
            bool asked = false;
            bool searching = false;
            Exception? failure = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(90);
            using var pilot = new System.Windows.Forms.Timer { Interval = 20 };
            pilot.Tick += (_, _) =>
            {
                if (Application.OpenForms.OfType<VirtualCrossoverJunctionTuneDialog>().FirstOrDefault(form => form.Visible)
                    is not { } open)
                {
                    return;
                }

                var dialog = new TuneDialog(open);
                try
                {
                    if (DateTime.UtcNow > deadline)
                    {
                        throw new TimeoutException("The junction tune did not finish.");
                    }

                    if (!asked)
                    {
                        asked = true;
                        ask(dialog);
                        if (!apply && open.Visible)
                        {
                            dialog.Find<Button>("buttonCancel").PerformClick();
                        }
                        else if (apply)
                        {
                            dialog.Find<Button>("buttonRun").PerformClick();
                            searching = true;
                        }

                        return;
                    }

                    if (searching && dialog.Find<Button>("buttonRun").Enabled)
                    {
                        searching = false;
                        status = dialog.Find<Label>("labelStatus").Text;
                        Button applyButton = dialog.Find<Button>("buttonApply");
                        Assert.True(applyButton.Enabled, status + Environment.NewLine + dialog.Find<Control>("textBoxReport").Text);
                        applyButton.PerformClick();
                    }
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                    open.DialogResult = DialogResult.Cancel;
                    open.Close();
                }
            };
            pilot.Start();
            Button tune = (Button)Panel.Controls.Find("buttonTuneJunction", searchAllChildren: true).Single();
            Assert.True(tune.Enabled);
            tune.PerformClick();
            pilot.Stop();
            if (failure != null)
            {
                throw new InvalidOperationException("The pilot failed.", failure);
            }

            Assert.True(asked, "The dialog never opened.");
            Wait(() => true);
            return status;
        }

        public void Dispose()
        {
            host.Close();
            host.Dispose();
        }

        private static void Wait(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                StaTest.Pump();
                Thread.Sleep(5);
            }

            Assert.True(condition(), "The panel did not settle.");
            for (int i = 0; i < 20; i++)
            {
                StaTest.Pump();
                Thread.Sleep(5);
            }
        }
    }

    private sealed class TuneDialog(Form form)
    {
        public T Find<T>(string name) where T : Control =>
            (T)form.Controls.Find(name, searchAllChildren: true).Single();
    }
}
