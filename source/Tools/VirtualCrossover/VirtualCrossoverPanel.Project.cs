using Resonalyze.Options;

namespace Resonalyze;

/// <summary>The session file: the stored project loaded on first show, bound to the controls, saved, imported, exported, and
/// its missing measurements relinked.</summary>
public partial class VirtualCrossoverPanel
{
    private bool initialized;

    // Awaited by anything that replaces the project; never faulted.
    private Task storedProjectLoad = Task.CompletedTask;

    private bool loadingProject;

    /// <summary>Called whenever the tab becomes active; the first call loads the saved project.</summary>
    internal void OnPanelShown()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        storedProjectLoad = LoadProjectSafelyAsync();
    }

    // Kept as a task so an import arriving right after can await it.
    private async Task LoadProjectSafelyAsync()
    {
        try
        {
            VirtualCrossoverProjectFile loaded = VirtualCrossoverProjectFile.LoadOrDefault();
            await ApplyProjectAsync(loaded, imported: false);
            NotifyIfProjectBackedUp(loaded.BackupNoticePath);
            NotifyIfMigrationCostAFilter(loaded.MigrationNoticeText);
        }
        catch (Exception exception)
        {
            // Silently opening on defaults would invite re-tuning over a discarded project.
            System.Diagnostics.Debug.WriteLine(
                $"Virtual DSP project load failed: {exception}");
            if (!IsDisposed && IsHandleCreated)
            {
                ShowError(
                    "The saved Virtual DSP project could not be loaded, so the tool " +
                    "opened with defaults. The file on disk has not been changed.",
                    exception.Message);
            }
        }
    }

    // A migration that dropped a filter is announced at load, before the next save makes it the file.
    private void NotifyIfMigrationCostAFilter(string? notice)
    {
        if (notice == null || IsDisposed)
        {
            return;
        }

        MessageBox.Show(
            this,
            notice,
            "Virtual DSP",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void NotifyIfProjectBackedUp(string? backupPath)
    {
        if (backupPath == null || IsDisposed)
        {
            return;
        }

        MessageBox.Show(
            this,
            "The saved Virtual DSP session could not be opened, so it was moved " +
            $"aside to:\r\n\r\n{backupPath}\r\n\r\nA fresh session was started; your " +
            "previous file is preserved there for recovery.",
            "Virtual DSP",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // Re-resolving sources takes seconds. The whole tree is disabled because a load rebuilds the blocks.
    private void SetProjectLoading(bool loading)
    {
        if (IsDisposed)
        {
            return;
        }

        loadingProject = loading;
        UseWaitCursor = loading;
        Enabled = !loading;
        if (loading)
        {
            acousticPlot.ShowHint(AcousticViewBuilder.LoadingHint);
            MetricChanged?.Invoke("Loading\r\nsession…", string.Empty);
        }
    }

    // imported: a session file may carry calibration ids from another machine.
    private async Task ApplyProjectAsync(VirtualCrossoverProjectFile newProject, bool imported)
    {
        SetProjectLoading(true);
        try
        {
            await BindProjectAsync(newProject, imported);
        }
        finally
        {
            // Before the redraw, so the final frame is the real plot, not the loading note.
            SetProjectLoading(false);
            RedrawAll();
        }
    }

    private async Task BindProjectAsync(VirtualCrossoverProjectFile newProject, bool imported)
    {
        // Read before the swap: a legacy session naming an unknown calibration keeps the panel's selection.
        string? previousCalibrationId =
            MicrophoneCalibrationComboHelper.GetSelectedCalibrationId(comboBoxCalibration);
        VirtualCrossoverSessionCalibration? previousSession = sessionCalibration;
        session.Project = newProject;
        session.RelinkDirectory = null;
        // The previous import's undo would restore into settings nobody displays.
        agentUndo = null;
        // Channel objects are reused across binds; this tells an EQ Wizard handoff which project it came from.
        projectGeneration++;
        SetChannelCount(session.Project.Pairs.Count);

        suppressProjectEvents = true;
        try
        {
            comboBoxSumLoss.SelectedItem = session.Project.SumLossWindowMode;
            // Intent only: captures attach as sources resolve, and HybridRequested also needs coverage.
            checkBoxHybrid.Checked = session.Project.ShowHybridCurves;
            checkBoxShowTarget.Checked = session.Project.ShowTargetCurve;
            numericTargetLevel.Value =
                numericTargetLevel.ClampValue(session.Project.TargetLevelDb);
            // Each newer view flag is written beside the older one it falls back to.
            radioViewStep.Checked = session.Project.ShowStepView;
            radioViewImpulse.Checked =
                !session.Project.ShowStepView && session.Project.ShowImpulseView;
            radioViewGroupDelay.Checked =
                !session.Project.ShowStepView && !session.Project.ShowImpulseView &&
                session.Project.ShowGroupDelayView;
            radioViewPhase.Checked =
                !session.Project.ShowStepView && !session.Project.ShowImpulseView &&
                !session.Project.ShowGroupDelayView && session.Project.ShowPhaseView;
            radioViewMagnitude.Checked =
                !session.Project.ShowStepView && !session.Project.ShowImpulseView &&
                !session.Project.ShowGroupDelayView && !session.Project.ShowPhaseView;
            // After the radios: the Sum toggle is remembered per view.
            ApplySumToggleForView();
            ApplyProjectTarget();
            radioSideRight.Checked = session.ActiveSideRight;
            radioSideLeft.Checked = !session.ActiveSideRight;
            acousticPlot.ConfigureForView(CurrentAcousticView());
            comboBoxSmoothing.SelectedItem =
                OverlaySmoothing.IsValid(session.Project.SmoothingCode)
                    ? session.Project.SmoothingCode
                    : 12;
            comboBoxGroupView.SelectedItem = session.Project.GroupView;
            if (VirtualCrossoverGroupViews.DrawsGroupSums(session.Project.GroupView))
            {
                radioViewMagnitude.Checked = true;
            }
            radioDspMagnitude.Checked =
                session.Project.EffectiveDspPlotMode == DspPlotMode.Magnitude;
            radioDspPhase.Checked =
                session.Project.EffectiveDspPlotMode == DspPlotMode.Phase;
            radioDspGroupDelay.Checked =
                session.Project.EffectiveDspPlotMode == DspPlotMode.GroupDelay;
            radioDspCorrelation.Checked =
                session.Project.EffectiveDspPlotMode == DspPlotMode.Correlation;
            radioDspCoherence.Checked =
                session.Project.EffectiveDspPlotMode == DspPlotMode.Coherence;
            comboBoxCorrelationPair.Enabled = JunctionPlotModeSelected() &&
                comboBoxCorrelationPair.Items.Count > 0;

            // Before filling blocks: it re-pins their height once instead of per block.
            RefreshProcessorRowAvailability();
            for (int i = 0; i < session.Channels.Count; i++)
            {
                session.Channels[i].Pair = session.Project.Pairs[i];
                ApplySettingsToControl(session.Channels[i]);
            }
        }
        finally
        {
            suppressProjectEvents = false;
        }

        // Restart the lock from the loaded pairs, or the first edit is recorded as a starting state.
        sideLock.Remember(session.Channels.Select(channel => channel.Pair));

        // Selector events were silenced above; refresh the view-muted controls from the landed state.
        UpdateViewDependentControls();

        BindCalibrationSelection(imported, previousCalibrationId, previousSession);

        await session.RestoreSourcesAsync(
            (channel, rightSide) => ResolveSourceAsync(channel, rightSide, showErrors: false),
            UpdateSourceButton);

        // After sources (an array brings one): settle the averaging method once and redraw the buttons drawn earlier.
        if (session.SettleSpatialAverageMode())
        {
            foreach (VirtualCrossoverChannel channel in session.Channels)
            {
                RefreshSpatialAverageStatus(channel);
            }

            RefreshHybridAvailability();
            ScheduleSave();
        }

        UpdateSideRadioTexts();
        // Again after sources: a project with no rate takes the measurements', and the phase read-out solves at it.
        RefreshProcessorRowAvailability();
    }

    private void ExportSession()
    {
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "json",
            FileName = "virtual-dsp-session",
            Filter = "Virtual DSP session (*.json)|*.json|All files (*.*)|*.*",
            Title = "Save Virtual DSP session"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            session.Project.SaveTo(dialog.FileName);
        }
        catch (Exception exception)
        {
            ShowError("The session could not be saved.", exception.Message);
        }
    }

    // The import immediately becomes the new internal autosave.
    private async Task ImportSessionAsync()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Virtual DSP session (*.json)|*.json|All files (*.*)|*.*",
            Title = "Load Virtual DSP session"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        await ImportSessionFileAsync(dialog.FileName);
    }

    /// <summary>Imports the session at <paramref name="path"/> like Load; reports its own failures.</summary>
    /// <remarks>Waits for the stored project load a tab switch just started, which would otherwise restore the replaced session.</remarks>
    internal async Task ImportSessionFileAsync(string path)
    {
        await storedProjectLoad;

        VirtualCrossoverProjectFile imported;
        try
        {
            imported = VirtualCrossoverProjectFile.LoadFrom(path);
        }
        catch (Exception exception)
        {
            ShowError("The session could not be loaded.", exception.Message);
            return;
        }

        await ApplyProjectAsync(imported, imported: true);
        NotifyIfMigrationCostAFilter(imported.MigrationNoticeText);
        ScheduleSave();
        await RelinkMissingSourcesAsync();
        ShowCalibrationNotice();
    }

    // Stored paths come from the measuring machine; one folder answers for all missing sources and stays a search root.
    private async Task RelinkMissingSourcesAsync()
    {
        List<(VirtualCrossoverChannel Channel, bool RightSide)> missing =
            session.MissingSourceSides().ToList();
        if (missing.Count == 0 || IsDisposed)
        {
            return;
        }

        if (MessageBox.Show(
                FindForm(),
                $"{DescribeMissingSources(missing)}\r\n\r\nThey were saved with this " +
                "session's own paths, which do not exist on this computer. Point at " +
                "the folder holding the measurements?",
                "Virtual DSP",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the folder holding this session's measurements",
            UseDescriptionForTitle = true,
            SelectedPath = session.Project.ProjectDirectory ?? string.Empty
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        session.RelinkDirectory = dialog.SelectedPath;
        SetProjectLoading(true);
        try
        {
            foreach ((VirtualCrossoverChannel channel, bool rightSide) in missing)
            {
                await ResolveSourceAsync(channel, rightSide, showErrors: false);
            }

            foreach (VirtualCrossoverChannel channel in
                missing.Select(item => item.Channel).Distinct())
            {
                UpdateSourceButton(channel);
            }

            UpdateSideRadioTexts();
        }
        finally
        {
            SetProjectLoading(false);
            RedrawAll();
        }

        ScheduleSave();

        List<(VirtualCrossoverChannel Channel, bool RightSide)> remaining =
            session.MissingSourceSides().ToList();
        if (remaining.Count > 0 && !IsDisposed)
        {
            MessageBox.Show(
                FindForm(),
                $"{DescribeMissingSources(remaining)}\r\n\r\nThe folder holds no file " +
                "under the name each channel was saved with. Pick those measurements " +
                "with the channel's Source button, or import the session again to " +
                "choose a different folder.",
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private static string DescribeMissingSources(
        IReadOnlyList<(VirtualCrossoverChannel Channel, bool RightSide)> missing)
    {
        string sides = string.Join(
            ", ",
            missing.Select(item => item.Channel.SideLabel(item.RightSide)));
        return missing.Count == 1
            ? $"The measurement of channel {sides} was not found."
            : $"{missing.Count} measurements were not found: {sides}.";
    }
}
