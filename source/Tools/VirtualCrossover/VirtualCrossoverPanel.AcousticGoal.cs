using Resonalyze.Dsp;

namespace Resonalyze;

public partial class VirtualCrossoverPanel
{
    /// <summary>
    /// The channel card's acoustic-goal button: what driver and filter should add up to on this channel's edges.
    /// Written to the shown side, and mirrored by the side Lock exactly as the crossover itself is, since the wish
    /// describes that one filter. See docs/specs/acoustic-crossover-target.md.
    /// </summary>
    private void ShowAcousticGoalDialog(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        using var dialog = new VirtualCrossoverAcousticGoalDialog();
        dialog.Init(settings, channel.Name);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || IsDisposed)
        {
            return;
        }

        if (Equals(dialog.HighPassGoal, settings.AcousticHighPass) &&
            Equals(dialog.LowPassGoal, settings.AcousticLowPass))
        {
            return;
        }

        settings.AcousticHighPass = dialog.HighPassGoal;
        settings.AcousticLowPass = dialog.LowPassGoal;
        ControlFor(channel).SetAcousticGoal(settings.AcousticHighPass, settings.AcousticLowPass);
        // Through the channel's own change path: the side Lock carries it to the hidden side and the session saves.
        OnChannelSettingsChanged(channel);
    }
}
