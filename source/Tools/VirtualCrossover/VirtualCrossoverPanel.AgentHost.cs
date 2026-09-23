namespace Resonalyze;

/// <summary>What the AI import runner asks of the controls: the view, the dialogs an engine opens, and showing what it
/// wrote.</summary>
public partial class VirtualCrossoverPanel
{
    // What the import runner asks of the controls.
    AgentViewInputs IAgentImportHost.View() => AgentView();

    GatePlacementVerdict? IAgentImportHost.GatePlacement => gatePlacement;

    bool IAgentImportHost.HybridRequested => HybridRequested;

    EqAutoTunePolicy IAgentImportHost.AutoTunePolicy() => AutoTunePolicyProvider?.Invoke() ?? EqAutoTunePolicy.Default;

    string? IAgentImportHost.OpenAutoSetupWizard() => OpenAutoSetupWizard();

    Task IAgentImportHost.ApplyAutoDelayAsync(AutoDelayRunResult result) => ApplyConfirmedAutoDelayAsync(result);

    void IAgentImportHost.ShowChannel(VirtualCrossoverChannel channel) => ShowChannel(channel);

    void IAgentImportHost.ShowBank(VirtualCrossoverChannel channel) => UpdatePeqReadouts(channel);

    void IAgentImportHost.SetTargetLevel(double levelDb) => SetTargetLevel(levelDb);

    void IAgentImportHost.RememberSides() => sideLock.Remember(session.Channels.Select(channel => channel.Pair));

    void IAgentImportHost.SaveAndRedraw() => SaveAndRedraw();

    // Mode and tick together: either alone leaves the point measurement in charge. Project events are suppressed so the import saves and redraws once.
    void IAgentImportHost.UseSpatialAverage(VirtualCrossoverSpatialAverageMode mode)
    {
        bool suppressed = suppressProjectEvents;
        suppressProjectEvents = true;
        try
        {
            SetSpatialAverageMode(mode);
            checkBoxHybrid.Checked = true;
            session.Project.ShowHybridCurves = true;
        }
        finally
        {
            suppressProjectEvents = suppressed;
        }

        // SetSpatialAverageMode returns early on an unchanged mode; the tick alone still changes what can be drawn.
        RefreshHybridAvailability();
    }

    bool IVirtualCrossoverWorkHost.IsGone => IsDisposed;

    IDisposable IVirtualCrossoverWorkHost.Busy(bool disable)
    {
        bool wasEnabled = Enabled;
        if (disable)
        {
            Enabled = false;
        }

        UseWaitCursor = true;
        return new DisposeAction(() =>
        {
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                if (disable)
                {
                    Enabled = wasEnabled;
                }
            }
        });
    }

    private void ShowChannel(VirtualCrossoverChannel channel)
    {
        ApplySettingsToControl(channel);
        UpdatePeqReadouts(channel);
    }
}
