using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>The blocks and their sides: adding, removing, moving and resetting blocks, the L/R selector, the side lock and
/// copy, the processor dialog, and the two-way binding between a block's settings and its control.</summary>
public partial class VirtualCrossoverPanel
{
    // Min 2: the sum metric needs two channels; max = the project format's capacity.
    private const int MinChannelCount = 2;
    private const int MaxChannelCount = VirtualCrossoverProjectFile.MaximumChannelCount;
    private const int DefaultChannelCount = 3;

    // VirtualCrossoverChannel is UI-free; only the binding methods look up controls.
    private readonly Dictionary<VirtualCrossoverChannel, VirtualCrossoverChannelControl>
        channelControls = new();

    // Each side keeps its own processed-IR cache, so switching is cheap.
    private void OnActiveSideChanged()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        bool rightSide = radioSideRight.Checked;
        session.Project.ActiveSideRight = rightSide;
        suppressProjectEvents = true;
        try
        {
            foreach (VirtualCrossoverChannel channel in session.Channels)
            {
                ApplySettingsToControl(channel);
            }
        }
        finally
        {
            suppressProjectEvents = false;
        }

        UpdateSideRadioTexts();
        SaveAndRedraw();
    }

    // The source is never copied (each side has its own measurement); mono pairs are not offered.
    private void CopySideSettings(bool fromRight)
    {
        List<VirtualCrossoverChannel> candidates = session.Channels
            .Where(channel => !channel.Pair.Mono)
            .ToList();
        if (candidates.Count == 0)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        List<string> labels = candidates
            .Select(channel =>
            {
                string source = channel.SideSettings(fromRight).DisplayName;
                return string.IsNullOrWhiteSpace(source)
                    ? channel.Name
                    : $"{channel.Name} — {source}";
            })
            .ToList();
        using var dialog = new VirtualCrossoverCopySideDialog(fromRight, labels);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK ||
            dialog.SelectedIndices.Count == 0 ||
            dialog.Scope.IsEmpty)
        {
            return;
        }

        VirtualCrossoverCopyScope scope = dialog.Scope;
        bool targetSideShown = session.ActiveSideRight == !fromRight;
        foreach (int index in dialog.SelectedIndices)
        {
            VirtualCrossoverChannel channel = candidates[index];
            scope.Copy(channel.SideSettings(fromRight), channel.SideSettings(!fromRight));
            if (targetSideShown)
            {
                ApplySettingsToControl(channel);
            }
        }

        SaveAndRedraw();
    }

    // Engaging copies nothing; see docs/tech/virtual-dsp-panel.md#side-lock. On by default, not stored.
    private void OnSideLockChanged()
    {
        if (checkBoxSideLock.Checked)
        {
            sideLock.Engage(session.Channels.Select(channel => channel.Pair));
        }
        else
        {
            sideLock.Release();
        }
    }

    // ● has at least one source, ○ none.
    private void UpdateSideRadioTexts()
    {
        bool leftAny = session.Channels.Any(channel =>
            channel.SideState(false).TransferImpulseResponse != null);
        bool rightAny = session.Channels.Any(channel =>
            !channel.Pair.Mono &&
            channel.SideState(true).TransferImpulseResponse != null);
        radioSideLeft.Text = leftAny ? "L ●" : "L ○";
        radioSideRight.Text = rightAny ? "R ●" : "R ○";
    }

    private VirtualCrossoverChannel CreateChannel(int index)
    {
        // Keep the designer size (DPI-scaled); raw pixels would clip on high DPI.
        var control = new VirtualCrossoverChannelControl
        {
            BackColor = UiPalette.PanelSurface,
            Font = new Font("Segoe UI", 9F),
            ForeColor = UiPalette.TextPrimary,
            Margin = new Padding(0, 0, 0, 6),
            ChannelName = ChannelNameFor(index),
            // Before joining the list: the rows change its height and a re-pin makes the list jump.
            PhaseControlShown = session.Project.ResolveDspPhaseControl(),
            FirControlShown = session.Project.ResolveDspFirFilters(),
            ProcessorSampleRateHz = session.ProcessorSampleRateHz
        };

        control.SetAccentColor(VirtualCrossoverColors.ChannelAccent(index));

        // Per block, not in the constructor: blocks added later need tooltips too.
        control.ApplyTooltips(toolTip);

        var channel = new VirtualCrossoverChannel(ChannelNameFor(index))
        {
            // Read on demand: the processor can change at any time.
            ProcessorSampleRateProvider = () => session.ProcessorSampleRateHz,
            ActiveRightProvider = () => session.ActiveSideRight
        };
        channelControls[channel] = control;
        control.SettingsChanged += (_, _) => OnChannelSettingsChanged(channel);
        control.SourceClicked += (_, _) => ShowSourceMenu(channel);
        control.SpatialAverageClicked += (_, _) => ShowSpatialAverageMenu(channel);
        control.PeqMenuClicked += (_, _) => ShowPeqMenu(channel);
        control.FirClicked += (_, _) => ShowFirMenu(channel);
        control.CollapsedChanged += (_, _) => OnChannelCollapsedChanged(channel);
        control.MoveUpClicked += (_, _) => MoveChannel(channel, -1);
        control.MoveDownClicked += (_, _) => MoveChannel(channel, +1);
        return channel;
    }

    // The channel object moves with its resolved IRs; only position-derived letter, colour and pair order are rewritten.
    private void MoveChannel(VirtualCrossoverChannel channel, int delta)
    {
        int at = session.Channels.IndexOf(channel);
        int to = at + delta;
        if (at < 0 || to < 0 || to >= session.Channels.Count)
        {
            return;
        }

        var order = Enumerable.Range(0, session.Channels.Count).ToList();
        (order[at], order[to]) = (order[to], order[at]);
        ApplyChannelOrder(order);
        SaveAndRedraw();
    }

    /// <summary><c>order[newIndex]</c> is the block's current position.</summary>
    /// <remarks>The project's pairs are permuted by the same indices, not rebuilt from the channels: they are bound
    /// only once a project is applied. The pair list is the whole persisted order.</remarks>
    private void ApplyChannelOrder(IReadOnlyList<int> order)
    {
        List<VirtualCrossoverChannel> reordered =
            order.Select(index => session.Channels[index]).ToList();
        session.Channels.Clear();
        session.Channels.AddRange(reordered);
        if (session.Project.Pairs.Count == order.Count)
        {
            List<VirtualCrossoverChannelPairSettings> pairs =
                order.Select(index => session.Project.Pairs[index]).ToList();
            session.Project.Pairs.Clear();
            session.Project.Pairs.AddRange(pairs);
        }

        channelListPanel.SuspendLayout();
        for (int i = 0; i < session.Channels.Count; i++)
        {
            VirtualCrossoverChannel channel = session.Channels[i];
            VirtualCrossoverChannelControl control = ControlFor(channel);
            channel.Name = ChannelNameFor(i);
            control.ChannelName = channel.Name;
            control.SetAccentColor(VirtualCrossoverColors.ChannelAccent(i));
            channelListPanel.Controls.SetChildIndex(control, i);
        }

        channelListPanel.ResumeLayout(performLayout: true);
        UpdateChannelButtons();
    }

    private VirtualCrossoverChannelControl ControlFor(VirtualCrossoverChannel channel) =>
        channelControls[channel];

    // The processing rate keys the coordinator cache, so a change re-runs every channel.
    private void OpenDspProcessorDialog()
    {
        // The dialog gets the real measured rate, zero included, never a default.
        using var dialog = new DspProcessorDialog(
            session.ProcessorProfile,
            session.Project.DspProcessorRateFollowsMeasurements,
            session.MeasuredSampleRateHz ?? 0,
            session.Project.DspProcessorPhaseControl,
            session.Project.DspProcessorFirFilters)
        {
            Notes = session.Project.AiNotes
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        // Notes alone are a save, never a re-run.
        string? notes = dialog.Notes;
        bool notesChanged = !string.Equals(notes, session.Project.AiNotes, StringComparison.Ordinal);
        if (notesChanged)
        {
            session.Project.AiNotes = notes;
            ScheduleSave();
        }

        DspProcessorProfile profile = dialog.Profile;
        // Compare intent, not numbers: "follow measurements" equals 48 kHz only until they are replaced.
        bool follows = dialog.FollowsMeasurements;
        // Confirming stores the shown phase answer, so a later model change cannot remove a control in use.
        bool phaseControl = dialog.PhaseControl;
        bool phaseControlChanged = session.Project.DspProcessorPhaseControl != phaseControl;
        bool firFilters = dialog.FirFilters;
        bool firFiltersChanged = session.Project.DspProcessorFirFilters != firFilters;
        if (profile == session.ProcessorProfile && follows == session.Project.DspProcessorRateFollowsMeasurements &&
            !phaseControlChanged && !firFiltersChanged)
        {
            return;
        }

        session.Project.DspProcessorPhaseControl = phaseControl;
        session.Project.DspProcessorFirFilters = firFilters;
        session.Project.SetDspProcessor(profile, follows);
        // A device without phase control (or FIR) drops them: left in place they would bend curves with no field on screen.
        int clearedRotations = session.Project.ClearUnavailablePhaseRotations();
        int clearedFirFilters = session.Project.ClearUnavailableFirFilters();
        if (clearedRotations > 0 || clearedFirFilters > 0)
        {
            foreach (VirtualCrossoverChannel channel in session.Channels)
            {
                ApplySettingsToControl(channel);
            }
        }

        RefreshProcessorRowAvailability();

        SaveAndRedraw();
        var notices = new List<string>();
        if (clearedRotations > 0)
        {
            notices.Add(
                $"{clearedRotations} channel side" +
                (clearedRotations == 1 ? " had" : "s had") +
                " a phase rotation dialled in, and this processor has no such " +
                "control.\r\n\r\nThe angle" +
                (clearedRotations == 1 ? " was" : "s were") +
                " cleared: left in place it would go on bending the curves with " +
                "nothing on screen to explain it, and the tuning sheet would go on " +
                "naming a control this device does not have.");
        }
        if (clearedFirFilters > 0)
        {
            notices.Add(
                $"{clearedFirFilters} channel side" +
                (clearedFirFilters == 1 ? " had" : "s had") +
                " a FIR filter loaded, and this processor has no FIR stage.\r\n\r\n" +
                "The kernel" + (clearedFirFilters == 1 ? " was" : "s were") +
                " detached: left in place it would go on shaping the curves with " +
                "nothing on screen to explain it, and the tuning sheet would go on " +
                "naming a file this device cannot take.");
        }
        if (notices.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join("\r\n\r\n", notices),
                "Virtual DSP",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private static string ChannelNameFor(int index) =>
        VirtualCrossoverSheet.ChannelName(index);

    // Does not touch the project; callers own persistence.
    private void SetChannelCount(int count)
    {
        count = Math.Clamp(count, MinChannelCount, MaxChannelCount);

        while (session.Channels.Count > count)
        {
            VirtualCrossoverChannel removed = session.Channels[^1];
            // Invalidate BEFORE detaching: a pending source load then refuses to write back into the disposed control.
            removed.Invalidate();
            session.Channels.RemoveAt(session.Channels.Count - 1);
            VirtualCrossoverChannelControl control = ControlFor(removed);
            channelControls.Remove(removed);
            channelListPanel.Controls.Remove(control);
            control.Dispose();
        }

        while (session.Channels.Count < count)
        {
            VirtualCrossoverChannel added = CreateChannel(session.Channels.Count);
            session.Channels.Add(added);
            channelListPanel.Controls.Add(ControlFor(added));
        }

        UpdateChannelButtons();
    }

    private void UpdateChannelButtons()
    {
        buttonAddChannel.Enabled = session.Channels.Count < MaxChannelCount;
        buttonRemoveChannel.Enabled = session.Channels.Count > MinChannelCount;
        for (int i = 0; i < session.Channels.Count; i++)
        {
            ControlFor(session.Channels[i]).SetMoveAvailability(i > 0, i < session.Channels.Count - 1);
        }
    }

    private void AddChannel()
    {
        if (session.Channels.Count >= MaxChannelCount)
        {
            return;
        }

        var pair = new VirtualCrossoverChannelPairSettings();
        session.Project.Pairs.Add(pair);
        SetChannelCount(session.Channels.Count + 1);
        VirtualCrossoverChannel added = session.Channels[^1];
        added.Pair = pair;
        ApplySettingsToControl(added);

        SaveAndRedraw();
    }

    private void RemoveChannel()
    {
        if (session.Channels.Count <= MinChannelCount)
        {
            return;
        }

        SetChannelCount(session.Channels.Count - 1);
        if (session.Project.Pairs.Count > session.Channels.Count)
        {
            session.Project.Pairs.RemoveRange(
                session.Channels.Count, session.Project.Pairs.Count - session.Channels.Count);
        }

        SaveAndRedraw();
    }

    /// <summary>Resets to first-run defaults by binding a fresh project (one definition of "default").</summary>
    /// <remarks>The selected calibration and the shared target curve survive; see docs/tech/virtual-dsp-panel.md#reset.</remarks>
    private async Task ResetChannelsAsync()
    {
        // Wait for the load, or the reset binds a project the pending one replaces.
        await storedProjectLoad;
        if (IsDisposed)
        {
            return;
        }

        if (MessageBox.Show(
                FindForm(),
                "Reset every channel block to its default: the measurements come " +
                "off, the crossovers, delays, gains, polarity, PEQ banks and zones " +
                "go back to their defaults, and the list returns to " +
                $"{DefaultChannelCount} blocks." +
                Environment.NewLine + Environment.NewLine +
                "The panel's own settings go with them — target level, smoothing, " +
                "the gate, the Show view and the stereo scene offset. The " +
                "microphone calibration and the shared EQ target curve stay." +
                Environment.NewLine + Environment.NewLine +
                "The current session is copied to" + Environment.NewLine +
                VirtualCrossoverProjectFile.ResetBackupPath() +
                Environment.NewLine +
                "first, so Load session… brings it back. That copy is overwritten " +
                "by the next reset — for a tune worth keeping, export it with Save " +
                "session first." +
                Environment.NewLine + Environment.NewLine +
                "Reset now?",
                "Virtual DSP",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        // Back up the project in memory (the autosave lags a debounce), and only after the question.
        (_, string? backupError) = session.Project.SaveResetBackup();
        if (backupError != null && MessageBox.Show(
                FindForm(),
                "The copy of the current session could not be written:" +
                Environment.NewLine + backupError +
                Environment.NewLine + Environment.NewLine +
                "Resetting now would discard the tune with nothing to bring it " +
                "back from. Reset anyway?",
                "Virtual DSP",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        await ApplyProjectAsync(new VirtualCrossoverProjectFile(), imported: false);
        // The autosave makes the reset the state the tool opens on.
        ScheduleSave();
    }

    // Layout only: persist, no recompute or redraw.
    private void OnChannelCollapsedChanged(VirtualCrossoverChannel channel)
    {
        if (suppressProjectEvents)
        {
            return;
        }

        channel.Pair.Collapsed = ControlFor(channel).Collapsed;
        ScheduleSave();
    }

    private void OnChannelSettingsChanged(VirtualCrossoverChannel channel)
    {
        if (suppressProjectEvents)
        {
            return;
        }

        // Zone belongs to the pair: store before the mono branch, which may repaint the old zone (Center forces Mono).
        channel.Pair.Zone = ControlFor(channel).SelectedZone;

        // A mono pair answers with the left side, so values read under the old binding must not be written through the new one.
        bool wasMono = channel.Pair.Mono;
        bool monoNow = ControlFor(channel).MonoCheckBox.Checked;
        if (wasMono != monoNow && channel.ActiveRight)
        {
            channel.Pair.Mono = monoNow;
            suppressProjectEvents = true;
            try
            {
                ApplySettingsToControl(channel);
            }
            finally
            {
                suppressProjectEvents = false;
            }
        }
        else
        {
            ReadControlIntoSettings(channel);
        }

        if (wasMono != monoNow)
        {
            if (monoNow)
            {
                // Drop the unreachable right runtime; the right settings survive for unchecking.
                channel.PhysicalSideState(true).Clear();
            }
            else
            {
                // Re-resolve through validation rather than resurfacing the slot's old cache.
                ReresolveRightSide(channel);
            }

            UpdateSideRadioTexts();
        }

        SaveAndRedraw();
    }

    // Guarded async void: called from a synchronous handler.
    private async void ReresolveRightSide(VirtualCrossoverChannel channel)
    {
        try
        {
            channel.PhysicalSideState(true).Clear();
            await ResolveSourceAsync(channel, rightSide: true, showErrors: false);
            // The channel or panel may be gone after the disk read.
            if (IsDisposed || !channelControls.ContainsKey(channel))
            {
                return;
            }

            UpdateSourceButton(channel);
            UpdateSideRadioTexts();
            RedrawAll();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Virtual DSP right-side re-resolve failed: {exception}");
        }
    }

    private void ApplySettingsToControl(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        VirtualCrossoverChannelControl control = ControlFor(channel);
        control.RunBatchUpdate(() =>
        {
            control.GainInput.Value = control.GainInput.ClampValue(settings.GainDb);
            control.DelayInput.Value = control.DelayInput.ClampValue(settings.DelayMs);
            control.InvertCheckBox.Checked = settings.InvertPolarity;
            control.CrossoverKindComboBox.SelectedItem = settings.CrossoverKind;
            // Family first: it repopulates the slope list.
            control.HighPassFamilyComboBox.SelectedItem = settings.HighPassEdge.Family;
            control.HighPassFrequencyInput.Value = control.HighPassFrequencyInput
                .ClampValue(settings.HighPassEdge.FrequencyHz);
            control.HighPassSlopeComboBox.SelectedItem = settings.HighPassEdge.SlopeDbPerOctave;
            control.HighPassRippleInput.Value = control.HighPassRippleInput
                .ClampValue(settings.HighPassEdge.RippleDb);
            control.LowPassFamilyComboBox.SelectedItem = settings.LowPassEdge.Family;
            control.LowPassFrequencyInput.Value = control.LowPassFrequencyInput
                .ClampValue(settings.LowPassEdge.FrequencyHz);
            control.LowPassSlopeComboBox.SelectedItem = settings.LowPassEdge.SlopeDbPerOctave;
            control.LowPassRippleInput.Value = control.LowPassRippleInput
                .ClampValue(settings.LowPassEdge.RippleDb);
            control.PhaseInput.Value = control.PhaseInput
                .ClampValue(settings.PhaseRotationDegrees);
            // Block-wide settings come off the pair, so they read the same on either side.
            control.ShowRawCheckBox.Checked = channel.Pair.ShowRawCurve;
            control.ShowProcessedCheckBox.Checked = channel.Pair.ShowProcessedCurve;
            control.BypassCheckBox.Checked = channel.Pair.Bypass;
            control.ZoneComboBox.SelectedItem = channel.Pair.Zone;
            control.MonoCheckBox.Checked = channel.Pair.Mono;
            control.Muted = !channel.Pair.Enabled;
            control.Collapsed = channel.Pair.Collapsed;
        });

        UpdateSourceButton(channel);
        UpdatePeqReadouts(channel);
        UpdateFirReadout(channel);
    }

    // Also runs on redraw (catches a rate that follows replaced measurements); only a real change reaches the layout.
    private void RefreshProcessorRowAvailability()
    {
        bool phaseShown = session.Project.ResolveDspPhaseControl();
        bool firShown = session.Project.ResolveDspFirFilters();
        int rate = session.ProcessorSampleRateHz;
        // Before the early return, so newly added or loaded pairs get the FIR rate too (EffectiveCrossover reads it).
        foreach (VirtualCrossoverChannel channel in session.Channels)
        {
            channel.Pair.Left.FirRunSampleRateHz = rate;
            channel.Pair.Right.FirRunSampleRateHz = rate;
        }

        bool changed = channelControls.Values.Any(control =>
            control.PhaseControlShown != phaseShown ||
            control.FirControlShown != firShown ||
            control.ProcessorSampleRateHz != rate);
        if (!changed)
        {
            return;
        }

        channelListPanel.SuspendLayout();
        foreach (VirtualCrossoverChannelControl control in channelControls.Values)
        {
            control.ProcessorSampleRateHz = rate;
            control.PhaseControlShown = phaseShown;
            control.FirControlShown = firShown;
        }

        channelListPanel.ResumeLayout(performLayout: true);
    }

    private void ReadControlIntoSettings(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        VirtualCrossoverChannelControl control = ControlFor(channel);
        settings.GainDb = (double)control.GainInput.Value;
        settings.DelayMs = (double)control.DelayInput.Value;
        settings.InvertPolarity = control.InvertCheckBox.Checked;
        settings.CrossoverKind = control.SelectedCrossoverKind;
        settings.HighPassEdge = control.HighPassEdge;
        settings.LowPassEdge = control.LowPassEdge;
        settings.PhaseRotationDegrees = (double)control.PhaseInput.Value;
        channel.Pair.ShowRawCurve = control.ShowRawCheckBox.Checked;
        channel.Pair.ShowProcessedCurve = control.ShowProcessedCheckBox.Checked;
        channel.Pair.Enabled = !control.Muted;
        channel.Pair.Bypass = control.BypassCheckBox.Checked;
        channel.Pair.Zone = control.SelectedZone;
        channel.Pair.Mono = control.MonoCheckBox.Checked;
    }
}
