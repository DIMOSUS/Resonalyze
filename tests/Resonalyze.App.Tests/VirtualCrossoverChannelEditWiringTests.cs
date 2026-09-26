using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// A block edit through a live panel writes only the field it changed: a loaded value finer than its field shows
/// survives an edit of another field, and the side Lock, which reads moves by difference, sees nothing to carry.
/// </summary>
public sealed class VirtualCrossoverChannelEditWiringTests
{
    [Fact]
    public void AGainEdit_KeepsTheLoadedCornerAndDelayItsFieldsRound_AndTheLockCarriesNothing() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverChannelPairSettings pair = live.Session.Project.Pairs[0];
        pair.Left.LowPassEdge = pair.Left.LowPassEdge with { FrequencyHz = 83.7 };
        pair.Left.DelayMs = 1.234;
        pair.Right.LowPassEdge = pair.Right.LowPassEdge with { FrequencyHz = 90 };
        Load(live);
        VirtualCrossoverChannel channel = live.Session.Channels[0];
        VirtualCrossoverChannelControl card = live.Card(channel);
        Assert.Equal(84m, card.LowPassFrequencyInput.Value);
        Assert.Equal(1.23m, card.DelayInput.Value);

        card.GainInput.Value = -3m;
        live.Settle();

        Assert.Equal(-3, channel.Pair.Left.GainDb);
        Assert.Equal(83.7, channel.Pair.Left.LowPassEdge.FrequencyHz);
        Assert.Equal(1.234, channel.Pair.Left.DelayMs);
        Assert.Equal(90, channel.Pair.Right.LowPassEdge.FrequencyHz);
    });

    [Fact]
    public void ACornerEdit_WritesTheShownCorner_AndTheLockCarriesTheCrossover() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverChannelPairSettings pair = live.Session.Project.Pairs[0];
        pair.Left.LowPassEdge = pair.Left.LowPassEdge with { FrequencyHz = 83.7 };
        pair.Left.GainDb = -1.25;
        pair.Right.LowPassEdge = pair.Right.LowPassEdge with { FrequencyHz = 90 };
        Load(live);
        VirtualCrossoverChannel channel = live.Session.Channels[0];

        live.Card(channel).LowPassFrequencyInput.Value = 85m;
        live.Settle();

        Assert.Equal(85, channel.Pair.Left.LowPassEdge.FrequencyHz);
        Assert.Equal(-1.25, channel.Pair.Left.GainDb);
        Assert.Equal(85, channel.Pair.Right.LowPassEdge.FrequencyHz);
    });

    // A hand-edited file only: the load ticks the Centre block's forced Mono box while its events are silenced.
    [Fact]
    public void ACentreBlockLoadedStereo_IsStoredMonoByItsNextEdit() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverChannelPairSettings pair = live.Session.Project.Pairs[2];
        pair.Zone = VirtualCrossoverZone.Center;
        pair.Mono = false;
        Load(live);
        VirtualCrossoverChannel channel = live.Session.Channels[2];
        Assert.True(live.Card(channel).MonoCheckBox.Checked);

        live.Card(channel).GainInput.Value = -3m;
        live.Settle();

        Assert.True(channel.Pair.Mono);
        Assert.Equal(-3, channel.Pair.Left.GainDb);
    });

    // One edit per field in turn, each checked against everything the block holds: a field raising another's name
    // would lose its own edit or rewrite a finer value.
    [Fact]
    public void EachFieldsEdit_WritesItsShownValue_AndLeavesEveryFinerValueTheOthersHold() => StaTest.Run(() =>
    {
        using var live = new VirtualCrossoverLivePanel();
        VirtualCrossoverChannelSettings stored = live.Session.Project.Pairs[1].Left;
        stored.GainDb = -1.25;
        stored.DelayMs = 1.234;
        stored.HighPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 83.7, 24, 0.25);
        stored.LowPassEdge = new CrossoverEdge(CrossoverFilterFamily.LinkwitzRiley, 2_612.5, 24, 0.05);
        Load(live);
        VirtualCrossoverChannel channel = live.Session.Channels[1];
        VirtualCrossoverChannelControl card = live.Card(channel);
        Dictionary<string, object> expected = VirtualCrossoverChannelEditTests.Values(channel.Pair, channel.Settings);

        void Edit(Action edit, params (string Key, object Value)[] writes)
        {
            edit();
            live.Settle();
            foreach ((string key, object value) in writes)
            {
                expected[key] = value;
            }

            Assert.Equal(expected, VirtualCrossoverChannelEditTests.Values(channel.Pair, channel.Settings));
        }

        Edit(() => card.GainInput.Value = -3m, ("GainDb", -3.0));
        Edit(() => card.DelayInput.Value = 2.5m, ("DelayMs", 2.5));
        Edit(() => card.InvertCheckBox.Checked = true, ("Inverted", true));
        Edit(() => card.HighPassFrequencyInput.Value = 90m, ("HighPassHz", 90.0));
        Edit(() => card.HighPassFamilyComboBox.SelectedItem = CrossoverFilterFamily.Butterworth,
            ("HighPassFamily", CrossoverFilterFamily.Butterworth));
        Edit(() => card.HighPassSlopeComboBox.SelectedItem = 12, ("HighPassSlope", 12));
        Edit(() => card.HighPassRippleInput.Value = 0.5m, ("HighPassRippleDb", 0.5));
        Edit(() => card.LowPassFrequencyInput.Value = 2_600m, ("LowPassHz", 2_600.0));
        Edit(() => card.LowPassFamilyComboBox.SelectedItem = CrossoverFilterFamily.Chebyshev,
            ("LowPassFamily", CrossoverFilterFamily.Chebyshev));
        Edit(() => card.LowPassRippleInput.Value = 1m, ("LowPassRippleDb", 1.0));
        Edit(() => card.CrossoverKindComboBox.SelectedItem = CrossoverKind.LowPass, ("CrossoverKind", CrossoverKind.LowPass));
        Edit(() => card.PhaseInput.Value = 90m, ("PhaseRotationDegrees", 90.0));
        Edit(() => card.ZoneComboBox.SelectedItem = VirtualCrossoverZone.Rear, ("Zone", VirtualCrossoverZone.Rear));
        Edit(() => card.BypassCheckBox.Checked = true, ("Bypass", true));
        Edit(() => card.ShowRawCheckBox.Checked = true, ("ShowRaw", true));
        Edit(() => card.ShowProcessedCheckBox.Checked = false, ("ShowProcessed", false));
        Edit(() => card.MuteButton.PerformClick(), ("Muted", true));
        Edit(() => card.MonoCheckBox.Checked = true, ("Mono", true));
    });

    // Written, loaded and bound as Load session does, so the blocks show the file's values through the load path.
    private static void Load(VirtualCrossoverLivePanel live)
    {
        string path = Path.Combine(Path.GetTempPath(), $"resonalyze-edit-{Guid.NewGuid():N}.json");
        try
        {
            live.Session.Project.SaveTo(path);
            Task load = live.Panel.ImportSessionFileAsync(path);
            live.Wait(() => load.IsCompleted, "load the session");
            load.GetAwaiter().GetResult();
        }
        finally
        {
            File.Delete(path);
        }

        Assert.DoesNotContain(live.Messages, message => message.Contains("could not", StringComparison.Ordinal));
    }
}
