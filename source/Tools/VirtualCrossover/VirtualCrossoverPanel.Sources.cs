using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze;

/// <summary>A side's measurement: picked from a file or the history, assigned under the rate guard, resolved again on load,
/// cleared, and shown on the block's Source button.</summary>
public partial class VirtualCrossoverPanel
{
    private int pendingSourceLoads;

    private void ShowSourceMenu(VirtualCrossoverChannel channel)
    {
        var menu = new ContextMenuStrip();

        ToolStripMenuItem chooseFileItem = new("Choose file...");
        chooseFileItem.Click += async (_, _) => await ChooseSourceFileAsync(channel);
        menu.Items.Add(chooseFileItem);

        ToolStripMenuItem historyItem = new("History");
        PopulateHistoryMenu(historyItem, channel);
        menu.Items.Add(historyItem);

        menu.Items.Add(new ToolStripSeparator());

        // Enabled only when the reference still resolves.
        ToolStripMenuItem openItem = new("Open in analyzers");
        openItem.ToolTipText =
            "Load this side's measurement into the analysis modes\r\n" +
            "(lands on Frequency Response) — the full toolset on the\r\n" +
            "very measurement this channel is tuned on.";
        (Guid? entryId, string? filePath) = ResolveAnalyzerReference(channel.Settings);
        openItem.Enabled =
            OpenSourceInAnalyzersRequested != null && (entryId != null || filePath != null);
        openItem.Click += (_, _) =>
        {
            // Re-resolved at click time: the file can vanish while the menu is open.
            (Guid? id, string? path) = ResolveAnalyzerReference(channel.Settings);
            if (id != null || path != null)
            {
                OpenSourceInAnalyzersRequested?.Invoke(id, path);
            }
        };
        menu.Items.Add(openItem);

        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem clearItem = new("Clear");
        clearItem.Enabled = channel.Settings.HasSource;
        clearItem.Click += (_, _) => ClearSource(channel);
        menu.Items.Add(clearItem);

        DropDownMenu.ShowUnder(ControlFor(channel).SourceButton, menu);
    }

    // History entry first (survives file moves), else the located file path; (null, null) when nothing resolves.
    private (Guid? HistoryEntryId, string? FilePath) ResolveAnalyzerReference(
        VirtualCrossoverChannelSettings settings)
    {
        Guid? entryId =
            settings.HistoryEntryId is { } id && HistoryService?.FindById(id) != null
                ? id
                : null;
        return (entryId, session.Locate(settings.SourceFilePath, settings.SourceRelativePath));
    }

    private void PopulateHistoryMenu(ToolStripMenuItem historyItem, VirtualCrossoverChannel channel)
    {
        IReadOnlyList<MeasurementHistoryEntry> entries =
            HistoryService?.Entries ?? Array.Empty<MeasurementHistoryEntry>();
        if (entries.Count == 0)
        {
            historyItem.Enabled = false;
            return;
        }

        foreach (MeasurementHistoryEntry entry in entries)
        {
            ToolStripMenuItem entryItem = new(entry.FileNameOrDisplayName)
            {
                Tag = entry.Id
            };
            entryItem.Click += async (_, _) =>
            {
                if (entryItem.Tag is Guid entryId)
                {
                    await SelectHistoryEntryAsync(channel, entryId);
                }
            };
            historyItem.DropDownItems.Add(entryItem);
        }
    }

    private async Task ChooseSourceFileAsync(VirtualCrossoverChannel channel)
    {
        // Capture the concrete slot, settings and revision NOW: side, Mono or a session import can change during the load.
        // See docs/tech/virtual-dsp-panel.md#source-loading.
        bool rightSide = channel.ActiveRight;
        VirtualCrossoverChannelState targetState = channel.SideState(rightSide);
        VirtualCrossoverChannelSettings targetSettings = channel.SideSettings(rightSide);
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            RestoreDirectory = true,
            Title = $"Choose channel {channel.SideLabel(rightSide)} impulse response"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        int revision = targetState.BeginSourceLoad();
        pendingSourceLoads++;
        RefreshAutoActionsEnabled();
        try
        {
            ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(dialog.FileName);
            if (IsDisposed)
            {
                return;
            }

            if (TryAssignSource(targetState, revision, file.ToResult(), SourceConflictPolicy.Prompt))
            {
                OnSourceAssigned(
                    channel,
                    targetSettings,
                    new VirtualCrossoverSourceReference(
                        Path.GetFileName(dialog.FileName),
                        dialog.FileName,
                        HistoryEntryId: null));
            }
        }
        catch (Exception exception)
        {
            ShowError("Failed to load the impulse response.", exception.Message);
        }
        finally
        {
            pendingSourceLoads--;
            RefreshAutoActionsEnabled();
        }
    }

    private async Task SelectHistoryEntryAsync(VirtualCrossoverChannel channel, Guid entryId)
    {
        bool rightSide = channel.ActiveRight;
        VirtualCrossoverChannelState targetState = channel.SideState(rightSide);
        VirtualCrossoverChannelSettings targetSettings = channel.SideSettings(rightSide);
        int revision = targetState.BeginSourceLoad();
        pendingSourceLoads++;
        RefreshAutoActionsEnabled();
        try
        {
            MeasurementHistoryEntry? entry = HistoryService?.FindById(entryId);
            MeasurementResult? result = HistoryService == null
                ? null
                : await HistoryService.GetResultAsync(entryId);
            if (entry == null || result == null || IsDisposed)
            {
                return;
            }

            if (TryAssignSource(targetState, revision, result, SourceConflictPolicy.Prompt))
            {
                OnSourceAssigned(
                    channel,
                    targetSettings,
                    new VirtualCrossoverSourceReference(
                        entry.FileNameOrDisplayName,
                        entry.SourceFilePath,
                        entryId));
            }
        }
        catch (Exception exception)
        {
            ShowError("Failed to load the history entry.", exception.Message);
        }
        finally
        {
            pendingSourceLoads--;
            RefreshAutoActionsEnabled();
        }
    }

    private enum SourceConflictPolicy
    {
        Prompt,
        RejectSilently
    }

    // Shared by interactive pickers and silent restore; only the conflict policy differs.
    // targetState is the caller's pre-await capture; the revision refuses a landing the slot has moved past.
    private bool TryAssignSource(
        VirtualCrossoverChannelState targetState,
        int sourceRevision,
        MeasurementResult result,
        SourceConflictPolicy policy)
    {
        if (targetState.SourceRevision != sourceRevision)
        {
            return false;
        }

        if (ResolvedVirtualDspSource.FromResult(result) is not { } resolved)
        {
            if (policy == SourceConflictPolicy.Prompt)
            {
                ShowError(
                    "This measurement cannot be summed.",
                    "The virtual crossover sums loopback-referenced responses: it " +
                    "needs a transfer IR whose arrival is the tract's real delay. " +
                    VirtualCrossoverSourceRules.DescribeUnsummable(result));
            }

            return false;
        }

        // One sample rate per project: mixed rates are refused, checked against every resolved side.
        List<(VirtualCrossoverChannel Channel, bool RightSide, VirtualCrossoverChannelState State)> others =
            session.ResolvedSidesExcept(targetState).ToList();
        VirtualCrossoverSourceRules.Decision decision = VirtualCrossoverSourceRules.Evaluate(
            hasTransferIr: true,
            candidateSampleRate: resolved.SampleRate,
            otherResolvedSampleRates: others.Select(item => item.State.SampleRate));
        if (decision == VirtualCrossoverSourceRules.Decision.RejectSampleRateMismatch)
        {
            if (policy == SourceConflictPolicy.Prompt)
            {
                int projectSampleRate = others[0].State.SampleRate;
                ShowError(
                    $"This measurement is {resolved.SampleRate} Hz, but the project " +
                    $"already uses {projectSampleRate} Hz.",
                    "All channels in a Virtual DSP project must share one sample " +
                    "rate. Clear the existing channel sources first to switch the " +
                    "project to a different rate.");
            }

            return false;
        }

        resolved.ApplyTo(targetState);
        return true;
    }

    // A silent restore skips this: the reference is stored and the bind refreshes at the end.
    private void OnSourceAssigned(
        VirtualCrossoverChannel channel,
        VirtualCrossoverChannelSettings settings,
        VirtualCrossoverSourceReference reference)
    {
        reference.ApplyTo(settings);
        session.SettleSpatialAverageMode();
        UpdateSourceButton(channel);
        UpdateSideRadioTexts();
        SaveAndRedraw();
    }

    private void ClearSource(VirtualCrossoverChannel channel)
    {
        ClearSourceCore(channel, channel.ActiveRight);
        SaveAndRedraw();
    }

    private void ClearSourceCore(VirtualCrossoverChannel channel, bool rightSide)
    {
        channel.SideState(rightSide).Clear();
        VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
        settings.DisplayName = string.Empty;
        settings.SourceFilePath = null;
        settings.HistoryEntryId = null;
        // Without a measurement the spatial average refines nothing; a kept reference would only warn.
        settings.SpatialAveragePath = null;
        settings.SpatialAverageRelativePath = null;
        UpdateSourceButton(channel);
        UpdateSideRadioTexts();
    }

    // History entry, then file path, then beside an imported session; a missing source leaves the side unresolved.
    private async Task ResolveSourceAsync(
        VirtualCrossoverChannel channel, bool rightSide, bool showErrors)
    {
        VirtualCrossoverChannelSettings settings = channel.SideSettings(rightSide);
        VirtualCrossoverChannelState state = channel.SideState(rightSide);
        // Before the measurement's early exit: an average can come back while the source is still missing.
        ResolveSpatialAverage(settings, state);
        if (!settings.HasSource)
        {
            return;
        }

        // Rapid mono toggles leave several resolves airborne; only the latest (uncleared) one may land.
        int revision = state.BeginSourceLoad();
        pendingSourceLoads++;
        RefreshAutoActionsEnabled();
        try
        {
            (MeasurementResult? result, string? relocatedPath) =
                await LoadResultFromReferenceAsync(settings);
            if (result != null &&
                TryAssignSource(
                    state, revision, result, SourceConflictPolicy.RejectSilently) &&
                relocatedPath != null)
            {
                // Pin the relocated path only if the measurement landed: a stored path always wins, so pinning a refused file
                // would stop the next relink from searching the user's folder.
                settings.SourceFilePath = relocatedPath;
            }
        }
        catch (Exception exception) when (!showErrors)
        {
            _ = exception;
        }
        finally
        {
            pendingSourceLoads--;
            RefreshAutoActionsEnabled();
        }
    }

    // RelocatedPath is pinned by the caller only on acceptance: the autosave has no session file beside it to search again.
    private async Task<(MeasurementResult? Result, string? RelocatedPath)>
        LoadResultFromReferenceAsync(VirtualCrossoverChannelSettings settings)
    {
        if (settings.HistoryEntryId is { } entryId && HistoryService != null)
        {
            MeasurementResult? result = await HistoryService.GetResultAsync(entryId);
            if (result != null)
            {
                return (result, null);
            }
        }

        if (session.Locate(settings.SourceFilePath, settings.SourceRelativePath) is { } path)
        {
            ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(path);
            return (
                file.ToResult(),
                string.Equals(path, settings.SourceFilePath, StringComparison.Ordinal)
                    ? null
                    : path);
        }

        return (null, null);
    }

    private void UpdateSourceButton(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelControl control = ControlFor(channel);
        // Every path refreshing a source can change whether the channel has an average.
        RefreshSpatialAverageStatus(channel);
        RefreshHybridAvailability();
        string? name = channel.Settings.DisplayName;
        bool resolved = channel.TransferImpulseResponse != null;
        control.SourceButton.Text = string.IsNullOrWhiteSpace(name)
            ? "Source..."
            : resolved ? name : $"⚠ {name}";
        // As measured, from the raw transfer IR; Invert is a separate virtual stage.
        control.SetMeasuredPolarity(
            channel.TransferImpulseResponse is { } ir
                ? VirtualCrossoverAnalysis.EstimatePolarity(ir)
                : PolarityEstimate.Unknown);
        toolTip.SetToolTip(
            control.SourceButton,
            resolved
                ? channel.Settings.SourceFilePath ?? name
                : "Pick the channel's measurement: a saved impulse-response\r\n" +
                  "file or a history entry.\r\n" +
                  "Requires a loopback transfer IR.");
    }
}
