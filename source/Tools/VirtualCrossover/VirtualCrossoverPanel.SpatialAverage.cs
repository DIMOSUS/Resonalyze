using Resonalyze.Dsp;

namespace Resonalyze;

// Hybrid magnitude view: spatial averages refine the drawn magnitude only; timing, polarity and loss still read the IRs.
// See docs/tech/spatial-average.md.
public partial class VirtualCrossoverPanel
{
    private void SetSpatialAverageMode(VirtualCrossoverSpatialAverageMode mode)
    {
        if (session.SpatialAverageMode == mode && session.Project.SpatialAverageMode == mode)
        {
            return;
        }

        session.Project.SpatialAverageMode = mode;
        foreach (VirtualCrossoverChannel channel in session.Channels)
        {
            RefreshSpatialAverageStatus(channel);
        }

        RefreshHybridAvailability();
        ScheduleSave();
        OnViewChanged();
    }

    private void ShowSpatialAverageMenu(VirtualCrossoverChannel channel)
    {
        var menu = new ContextMenuStrip();

        foreach ((VirtualCrossoverSpatialAverageMode mode, string label) in new[]
        {
            (VirtualCrossoverSpatialAverageMode.MicArray, "Use microphone arrays"),
            (VirtualCrossoverSpatialAverageMode.MovingMic, "Use attached MMM captures"),
            (VirtualCrossoverSpatialAverageMode.Off, "No spatial average")
        })
        {
            ToolStripMenuItem item = new(label)
            {
                Checked = session.SpatialAverageMode == mode,
                CheckOnClick = false
            };
            VirtualCrossoverSpatialAverageMode chosen = mode;
            item.Click += (_, _) => SetSpatialAverageMode(chosen);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem chooseItem = new("Attach capture...");
        chooseItem.Click += (_, _) => ChooseSpatialAverage(channel);
        menu.Items.Add(chooseItem);

        if (channel.SpatialAverage != null ||
            !string.IsNullOrWhiteSpace(channel.Settings.SpatialAveragePath))
        {
            ToolStripMenuItem detachItem = new("Detach");
            detachItem.Click += (_, _) =>
            {
                channel.SpatialAverage = null;
                channel.Settings.SpatialAveragePath = null;
                channel.Settings.SpatialAverageRelativePath = null;
                OnSpatialAverageChanged(channel);
            };
            menu.Items.Add(detachItem);
        }

        DropDownMenu.ShowAt(this, menu, Cursor.Position);
    }

    private void ChooseSpatialAverage(VirtualCrossoverChannel channel)
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Resonalyze moving-mic capture (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            RestoreDirectory = true,
            Title = $"Attach a spatial average to {channel.Name}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (!LiveCaptureDocument.TryLoad(dialog.FileName, out LiveCaptureDocument document))
            {
                MessageBox.Show(
                    this,
                    "That file is not a Resonalyze capture.",
                    "Attach spatial average",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            channel.SpatialAverage = document;
            channel.Settings.SpatialAveragePath = dialog.FileName;
            // The relative path names the previously imported capture; left standing it would steer the next search to it.
            channel.Settings.SpatialAverageRelativePath = null;
            OnSpatialAverageChanged(channel);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "The capture could not be loaded." +
                    Environment.NewLine + Environment.NewLine + exception.Message,
                "Attach spatial average",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void OnSpatialAverageChanged(VirtualCrossoverChannel channel)
    {
        session.SettleSpatialAverageMode();
        RefreshSpatialAverageStatus(channel);
        RefreshHybridAvailability();
        ScheduleSave();
        OnViewChanged();
    }

    private void RefreshSpatialAverageStatus(VirtualCrossoverChannel channel)
    {
        if (!channelControls.TryGetValue(channel, out VirtualCrossoverChannelControl? control))
        {
            return;
        }

        VirtualCrossoverSpatialAverageMode mode = session.SpatialAverageMode;
        LiveCaptureDocument? document =
            channel.SideState(channel.ActiveRight).SpatialAverageFor(mode);
        // Missing capture: name it from the stored path. Arrays are not attached by path, so only MovingMic can go missing.
        string? path = mode == VirtualCrossoverSpatialAverageMode.MovingMic
            ? channel.Settings.SpatialAveragePath
            : null;
        control.SetSpatialAverage(
            document?.Title
                ?? (string.IsNullOrWhiteSpace(path)
                    ? null
                    : Path.GetFileNameWithoutExtension(path)),
            document?.Recipe.IntegratedSeconds,
            resolved: document != null,
            mode,
            document?.SavedAtUtc);
    }

    /// <summary>Re-attaches a persisted capture via the same path ladder as measurements.</summary>
    /// <remarks>An unresolved capture leaves the stored path standing: it drives relink and the button's warning.</remarks>
    private void ResolveSpatialAverage(
        VirtualCrossoverChannelSettings settings,
        VirtualCrossoverChannelState state)
    {
        state.SpatialAverage = null;
        if (string.IsNullOrWhiteSpace(settings.SpatialAveragePath))
        {
            return;
        }

        string? path = session.Locate(
            settings.SpatialAveragePath, settings.SpatialAverageRelativePath);
        if (path == null)
        {
            return;
        }

        try
        {
            if (LiveCaptureDocument.TryLoad(path, out LiveCaptureDocument document))
            {
                state.SpatialAverage = document;
                // Pin the actual read location: the autosave copy has no session file beside it to search from.
                settings.SpatialAveragePath = path;
            }
        }
        catch (Exception exception)
        {
            _ = exception;
        }
    }

    /// <summary>Δ L−R level source in hybrid mode (spatial averages through chains), or null for point levels.</summary>
    /// <remarks>Follows hybrid intent, not the current view, so the basis does not flip on a view glance. See docs/tech/spatial-average.md#level-read-outs.</remarks>
    private Func<VirtualCrossoverChannel, double, double, double?>?
        HybridStereoLevelReader() =>
        checkBoxHybrid.Checked && hybridAvailable &&
        hybridReader.CanDrawOppositeSum(!session.Project.ActiveSideRight)
            ? hybridReader.StereoLevelDeltaDb
            : null;

    /// <summary>"vs Front" level source in hybrid mode, or null for point levels. Active side only, so the set offset cancels.</summary>
    private Func<IReadOnlyList<ProcessedChannel>, IReadOnlyList<ProcessedChannel>,
        double, double, double?>? HybridGroupLevelReader() =>
        checkBoxHybrid.Checked && hybridAvailable
            ? hybridReader.GroupLevelDeltaDb
            : null;

    // Cached set verdict; the toggle is muted by hand, so its Enabled state cannot stand in for this.
    private bool hybridAvailable;

    // Groups view included: a group line is the same hybrid sum construction over its members. See docs/tech/spatial-average.md#hybrid-toggle.
    private bool HybridRequested =>
        checkBoxHybrid.Checked &&
        hybridAvailable;

    private void RefreshHybridAvailability()
    {
        // The tick is intent and outlives coverage (like a pinned gate outlives its sources).
        LiveCaptureSetVerdict verdict = hybridReader.JudgeSide(session.Project.ActiveSideRight);
        hybridAvailable = verdict.Coherent;

        // Muted, not unticked or disabled: UiStyle.SetTextEnabledLook would memorize the reminder colour, and WinForms' disabled grey is unreadable here.
        bool live = hybridAvailable && radioViewMagnitude.Checked;
        checkBoxHybrid.ForeColor = !live
            ? UiPalette.TextDisabled
            // Available but unticked: the plot ignores attached captures, so warn in the error colour.
            : checkBoxHybrid.Checked ? hybridToggleColor : UiPalette.Error;
        checkBoxHybrid.AutoCheck = live;
        checkBoxHybrid.TabStop = live;
        toolTip.SetToolTip(
            checkBoxHybrid,
            !hybridAvailable
                ? verdict.Reason ?? "Needs a spatial average on every channel that " +
                    "plays. Attach one per channel with the MMM button."
                : live && !checkBoxHybrid.Checked
                ? "Every channel that plays has a spatial average attached and the " +
                    "plot is not using one: these curves are the response at a " +
                    "single microphone position, dips and all. Tick this to draw " +
                    "them from the averages instead." +
                    Environment.NewLine + Environment.NewLine +
                    "What that changes is below."
                : !radioViewMagnitude.Checked
                ? "The hybrid is a magnitude view: a spatial average carries no " +
                    "phase, so the phase and impulse views keep reading the impulse " +
                    "responses."
                : "Draw each channel's magnitude from its spatial average with this " +
                    "channel's DSP chain on top, instead of from the impulse response " +
                    "measured at one point. Both sums follow, adding the channels as " +
                    "phasors with the phase the impulse responses measure — the other " +
                    "side needs its own captures too, and its dashed sum is dropped " +
                    "rather than drawn by the other method. The read-out's level " +
                    "rows follow too: Level Δ L−R compares the sides' captures " +
                    "under the same condition, and the vs Front ΔdB compares the " +
                    "groups'. Timing, polarity and the " +
                    "sum-loss read-out are " +
                    "unaffected: they keep reading the impulse responses.\r\n\r\n" +
                    "The channel curves are exact — a filter does not depend on " +
                    "microphone position. The Sum is an estimate: it adds the " +
                    "channels as phasors, and the phase holding them together was " +
                    "measured at ONE position, so its peaks and dips can be either " +
                    "stronger or weaker than the volume's average. The gap tends to " +
                    "grow the faster the phase turns across that volume — generally " +
                    "small in the bass, largest at a crossover high up.");
    }


}
