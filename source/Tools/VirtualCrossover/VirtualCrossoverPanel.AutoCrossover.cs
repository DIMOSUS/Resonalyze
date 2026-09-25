namespace Resonalyze;

/// <summary>The crossover wizard: opened on the blocks' driver curves, its proposal written back. What it reads and writes
/// is <see cref="VirtualCrossoverAutoSetup"/>.</summary>
public partial class VirtualCrossoverPanel
{
    /// <summary>Opens the crossover wizard and writes what it proposes (<see cref="VirtualCrossoverAutoSetup"/>).</summary>
    /// <returns>Null when written; otherwise a refusal phrase an import's summary can quote.</returns>
    private string? OpenAutoSetupWizard()
    {
        List<VirtualCrossoverChannel> participating = VirtualCrossoverAutoSetup.Participants(session);
        if (participating.Count < 2)
        {
            System.Media.SystemSounds.Beep.Play();
            return "fewer than two enabled channels have a measurement";
        }

        if (VirtualCrossoverAutoSetup.FirCrossoverRefusal(participating) is { } fir)
        {
            ShowError("Auto crossover cannot write over a FIR crossover.", char.ToUpperInvariant(fir[0]) + fir[1..] + ".");
            return fir;
        }

        // The band read is gate-independent, but the result is checked on gated views, so refuse a misplaced gate.
        if (RefuseOnMisplacedGate("Auto crossover"))
        {
            return "the phase gate is misplaced";
        }

        List<AutoSetupWizardChannel> dialogChannels;
        // Building an FDW curve per channel is the one stretch before the dialog appears; without this the button
        // looks like it did nothing for the best part of a second.
        UseWaitCursor = true;
        try
        {
            dialogChannels = VirtualCrossoverAutoSetup.ReadChannels(session, participating);
        }
        catch (ArgumentException exception)
        {
            ShowError("A channel's response has no usable band.", exception.Message);
            return "a channel's response has no usable band";
        }
        finally
        {
            UseWaitCursor = false;
        }

        using var dialog = new VirtualCrossoverAutoSetupDialog();
        dialog.Init(
            participating[0].SampleRate,
            session.ProcessorSampleRateHz,
            dialogChannels);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.Result is not { } proposals)
        {
            return "cancelled in the wizard";
        }

        int clearedRotations = VirtualCrossoverAutoSetup.Write(participating, proposals);
        foreach (VirtualCrossoverChannel channel in participating)
        {
            ApplySettingsToControl(channel);
        }

        if (dialog.ChainOrder is { } chainOrder)
        {
            ApplyChannelOrder(VirtualCrossoverAutoSetup.Reorder(session.Channels, participating, chainOrder));
        }

        // The wizard wrote both sides; the lock must not carry the shown side's other edge over.
        sideLock.Remember(session.Channels.Select(channel => channel.Pair));
        SaveAndRedraw();
        if (clearedRotations > 0)
        {
            MessageBox.Show(
                this,
                $"{clearedRotations} channel side" +
                (clearedRotations == 1 ? " had" : "s had") +
                " a phase rotation dialled in.\r\n\r\nThat control states its angle " +
                "at the channel's crossover, so the filter it built is not the one " +
                "the same number would build at the new corners — the rotations " +
                "were cleared rather than left meaning something else. Dial them " +
                "in again against the crossovers this run chose.",
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        return null;
    }
}
