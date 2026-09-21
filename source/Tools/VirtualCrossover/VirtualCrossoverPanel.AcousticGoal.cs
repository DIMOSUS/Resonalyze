using Resonalyze.Dsp;

namespace Resonalyze;

public partial class VirtualCrossoverPanel
{
    /// <summary>Written to the shown side; the side Lock mirrors it as it does the crossover.</summary>
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
        ShowAcousticGoal(channel);
        OnChannelSettingsChanged(channel);
    }

    private void ShowAcousticGoal(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        ControlFor(channel).SetAcousticGoal(
            settings.AcousticHighPass, settings.AcousticLowPass, settings.RunsHighPass, settings.RunsLowPass);
    }
}
