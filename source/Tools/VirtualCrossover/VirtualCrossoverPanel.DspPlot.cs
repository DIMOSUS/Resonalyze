using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The lower plot: each chain's own response, or a junction's correlation or coherence with its pair picker.</summary>
public partial class VirtualCrossoverPanel
{
    // Single-flight like the main redraw: stacked tasks would each burn a full sweep of inverse FFTs.
    private Task? correlationRebuildTask;
    private bool correlationRebuildPending;

    private bool suppressCorrelationPairEvents;

    private DspPlotMode CurrentDspPlotMode() =>
        radioDspPhase.Checked ? DspPlotMode.Phase
        : radioDspGroupDelay.Checked ? DspPlotMode.GroupDelay
        : radioDspCorrelation.Checked ? DspPlotMode.Correlation
        : radioDspCoherence.Checked ? DspPlotMode.Coherence
        : DspPlotMode.Magnitude;

    // Both junction modes share the pair selector, rebuild loop and inputs.
    private bool JunctionPlotModeSelected() =>
        radioDspCorrelation.Checked || radioDspCoherence.Checked;

    private static bool IsJunctionMode(DspPlotMode mode) =>
        mode is DspPlotMode.Correlation or DspPlotMode.Coherence;

    // Retract the junction radios in the other container first (see the wiring).
    private void OnChainDspModeChecked()
    {
        radioDspCorrelation.Checked = false;
        radioDspCoherence.Checked = false;
        OnDspPlotModeChanged();
    }

    private void OnDspPlotModeChanged()
    {
        comboBoxCorrelationPair.Enabled =
            JunctionPlotModeSelected() && comboBoxCorrelationPair.Items.Count > 0;
        if (suppressProjectEvents)
        {
            return;
        }

        session.Project.SetDspPlotMode(CurrentDspPlotMode());
        ScheduleSave();
        RedrawDspPlot();
    }

    private void OnCorrelationPairChanged()
    {
        if (suppressProjectEvents || suppressCorrelationPairEvents)
        {
            return;
        }

        session.Project.CorrelationPairIndex =
            Math.Max(0, comboBoxCorrelationPair.SelectedIndex);
        ScheduleSave();
        RedrawDspPlot();
    }

    private void RedrawDspPlot()
    {
        if (IsJunctionMode(CurrentDspPlotMode()))
        {
            UpdateCorrelationPairChoices();
            RequestCorrelationRedraw();
            return;
        }

        using var _ = AppProfiler.Zone("VirtualDSP.RedrawDspPlot");
        var curves = new List<DspChainCurve>();
        for (int i = 0; i < session.Channels.Count; i++)
        {
            VirtualCrossoverChannel channel = session.Channels[i];
            if (!channel.Pair.Enabled || channel.TransferImpulseResponse == null)
            {
                continue;
            }

            // Drawn without the delay term: a bulk delay wraps phase into a sawtooth and swamps the filter GD.
            DspChannelChain chain = channel.Pair.Bypass
                ? DspChannelChain.Identity
                : channel.Settings.ToChain(channel.Pair.Zone) with { DelayMs = 0 };
            curves.Add(new DspChainCurve(
                $"{channel.Name} filter", chain, session.ProcessorSampleRateHz, VirtualCrossoverColors.Channel(i)));
        }

        dspChainPlot.Draw(CurrentDspPlotMode(), curves);
    }

    // From the last processed snapshot, narrowed to the view's summing chain (ProcessedChannels.JunctionsInView).
    private List<AdjacentPair> CurrentCorrelationPairs() =>
        lastProcessedRender is { } render
            ? ProcessedChannels.JunctionsInView(render.Channels, SelectedGroupView)
            : [];

    private void UpdateCorrelationPairChoices()
    {
        List<AdjacentPair> pairs = CurrentCorrelationPairs();
        List<string> labels = pairs
            .Select(pair => $"{pair.Lower.Channel.Name}-{pair.Upper.Channel.Name}")
            .ToList();
        bool changed = comboBoxCorrelationPair.Items.Count != labels.Count;
        for (int i = 0; !changed && i < labels.Count; i++)
        {
            changed = !Equals(comboBoxCorrelationPair.Items[i], labels[i]);
        }

        int wanted = Math.Clamp(
            session.Project.CorrelationPairIndex, 0, Math.Max(0, labels.Count - 1));
        if (!changed && comboBoxCorrelationPair.SelectedIndex == wanted)
        {
            return;
        }

        suppressCorrelationPairEvents = true;
        try
        {
            if (changed)
            {
                comboBoxCorrelationPair.Items.Clear();
                foreach (string label in labels)
                {
                    comboBoxCorrelationPair.Items.Add(label);
                }
            }

            if (labels.Count > 0)
            {
                comboBoxCorrelationPair.SelectedIndex = wanted;
            }
        }
        finally
        {
            suppressCorrelationPairEvents = false;
        }

        comboBoxCorrelationPair.Enabled =
            JunctionPlotModeSelected() && labels.Count > 0;
    }

    private void RequestCorrelationRedraw()
    {
        if (correlationRebuildTask is { IsCompleted: false })
        {
            correlationRebuildPending = true;
            return;
        }

        correlationRebuildTask = RunCorrelationRebuildLoopAsync();
    }

    private async Task RunCorrelationRebuildLoopAsync()
    {
        do
        {
            correlationRebuildPending = false;
            await RedrawCorrelationPlotAsync();
        }
        while (correlationRebuildPending && !dspPlotView.IsDisposed &&
            IsJunctionMode(CurrentDspPlotMode()));

        correlationRebuildTask = null;
    }

    // Shared by both junction modes; a result is dropped if the user switched mode mid-compute.
    private async Task RedrawCorrelationPlotAsync()
    {
        DspPlotMode mode = CurrentDspPlotMode();
        List<AdjacentPair> pairs = CurrentCorrelationPairs();
        if (pairs.Count == 0)
        {
            if (mode == DspPlotMode.Coherence)
            {
                dspChainPlot.DrawCoherence(null);
            }
            else
            {
                dspChainPlot.DrawCorrelation(null);
            }

            return;
        }

        AdjacentPair pair = pairs[Math.Clamp(
            session.Project.CorrelationPairIndex, 0, pairs.Count - 1)];
        JunctionCorrelationView? correlation = null;
        JunctionCoherenceView? coherence = null;
        try
        {
            List<ProcessedChannel> scope = lastProcessedRender is { } render
                ? render.Channels.ToList()
                : [pair.Lower, pair.Upper];
            if (mode == DspPlotMode.Coherence)
            {
                coherence = await Task.Run(() => JunctionViews.BuildCoherenceView(pair, scope));
            }
            else
            {
                correlation = await Task.Run(() => JunctionViews.BuildCorrelationView(pair, scope));
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Junction view rebuild failed: {exception}");
        }

        if (dspPlotView.IsDisposed || CurrentDspPlotMode() != mode)
        {
            return;
        }

        if (correlationRebuildPending)
        {
            return;
        }

        if (coherence != null)
        {
            dspChainPlot.DrawCoherence(coherence);
        }
        else if (correlation != null)
        {
            dspChainPlot.DrawCorrelation(correlation);
        }
    }
}
