using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze;

// Choosing a source (menus, file dialogs, async loads) and the selectors that say how it is read: calibration,
// smoothing, processor rate and Q convention. Also the target's menu and dialog, and persisted settings.
public partial class EqWizardPanel
{
    private readonly EqWizardSourceResolver sourceResolver = new();
    private int sourceLoadGeneration;
    private ContextMenuStrip? sourceMenu;
    private ContextMenuStrip? targetMenu;

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal MeasurementHistoryService? HistoryService { get; set; }

    // Rebuilt on every click: history and overlay slots change while the panel is open.
    private void ShowSourceMenu()
    {
        if (sourceMenu is { Visible: true })
        {
            sourceMenu.Close();
            return;
        }

        sourceMenu?.Dispose();
        sourceMenu = BuildSourceMenu();
        DropDownMenu.ShowUnder(buttonSource, sourceMenu);
    }

    private ContextMenuStrip BuildSourceMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Impulse response from file…", null, (_, _) => _ = LoadIrFromFileAsync());

        var historyItem = new ToolStripMenuItem("Impulse response from history");
        PopulateHistoryMenu(historyItem);
        menu.Items.Add(historyItem);

        menu.Items.Add(new ToolStripSeparator());

        var slotItem = new ToolStripMenuItem("Curve from overlay slot");
        PopulateSlotMenu(slotItem);
        menu.Items.Add(slotItem);

        menu.Items.Add(
            // Moving-mic captures and mic-array measurements are both spatial averages; this entry takes either.
            "Curve from spatial average…",
            null,
            (_, _) => _ = LoadCurveFromSpatialAverageAsync());
        menu.Items.Add("Curve from text file…", null, (_, _) => LoadCurveFromTextFile());
        return menu;
    }

    private void PopulateHistoryMenu(ToolStripMenuItem historyItem)
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
            var entryItem = new ToolStripMenuItem(MenuText.Trim(entry.FileNameOrDisplayName))
            {
                Tag = entry.Id,
                ToolTipText = MeasurementHistoryToolTip.Build(entry.Metadata, entry.Timestamp)
            };
            entryItem.Click += (_, _) =>
            {
                if (entryItem.Tag is Guid entryId)
                {
                    _ = LoadIrFromHistoryAsync(entryId, entry.FileNameOrDisplayName);
                }
            };
            historyItem.DropDownItems.Add(entryItem);
        }
    }

    private void PopulateSlotMenu(ToolStripMenuItem slotItem)
    {
        IReadOnlyList<EqWizardSlotOption> slots = sourceResolver.ListEligibleSlots();
        if (slots.Count == 0)
        {
            slotItem.Enabled = false;
            slotItem.ToolTipText =
                "No overlay slot holds a captured frequency-response or RTA curve.";
            return;
        }

        foreach (EqWizardSlotOption slot in slots)
        {
            var item = new ToolStripMenuItem(MenuText.Trim($"{slot.Slot}: {slot.Title}"))
            {
                // ToolStrip draws item tooltips itself (no app wrapping), and a description can carry a full path.
                ToolTipText = ToolTipTextWrapper.Wrap(slot.Description)
            };
            item.Click += (_, _) => LoadCurveFromSlot(slot.Slot);
            slotItem.DropDownItems.Add(item);
        }
    }

    private async Task LoadIrFromFileAsync()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Resonalyze impulse response (*.json)|*.json|All files (*.*)|*.*",
            Title = "Load impulse response"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        // A slow earlier load must not overwrite a newer selection when it lands.
        int generation = ++sourceLoadGeneration;
        ImpulseResponseFile file;
        try
        {
            file = await ImpulseResponseFile.LoadAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            if (generation == sourceLoadGeneration && !IsDisposed)
            {
                ShowFileError("The impulse response could not be loaded.", exception);
            }

            return;
        }

        if (generation != sourceLoadGeneration || IsDisposed)
        {
            return;
        }

        ApplyMeasurementSource(
            file,
            System.IO.Path.GetFileNameWithoutExtension(dialog.FileName),
            $"Impulse response: {dialog.FileName}",
            EqWizardSourceResolver.DescribeArray(file, dialog.FileName));
    }

    /// <summary>
    /// Applies a measurement as a source, offering (not forcing) its microphone array first when it carries one;
    /// an average has no IR, so substituting it silently would also drop the gate preview.
    /// </summary>
    private void ApplyMeasurementSource(
        ImpulseResponseFile file,
        string displayName,
        string description,
        string arrayDescription)
    {
        EqWizardCurveSource? array =
            EqWizardSourceResolver.TryCreateFromArray(file, displayName, arrayDescription);
        if (array != null && AskToEqualizeArray(file))
        {
            LoadSource(array);
            return;
        }

        LoadSource(EqWizardSourceResolver.CreateFromImpulseResponse(
            file, displayName, description));
    }

    private bool AskToEqualizeArray(ImpulseResponseFile file)
    {
        int count = file.ArrayMicrophones?.Microphones.Count ?? 0;
        string positions = count == 1 ? "1 position" : $"{count} positions";
        return MessageBox.Show(
            FindForm(),
            $"This measurement was recorded with a microphone array of {positions}." +
                Environment.NewLine + Environment.NewLine +
                "Equalize the array's average over the listening volume, rather than " +
                "the response measured at the one position its impulse response came " +
                "from?" + Environment.NewLine + Environment.NewLine +
                "The average is the shape a tune belongs on: a single position " +
                "carries dips that are a property of its own few centimetres. The " +
                "point measurement keeps the gate preview; the average has no " +
                "impulse response behind it.",
            "EQ Wizard",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1) == DialogResult.Yes;
    }

    private async Task LoadIrFromHistoryAsync(Guid entryId, string displayName)
    {
        if (HistoryService == null)
        {
            return;
        }

        int generation = ++sourceLoadGeneration;
        MeasurementResult? result;
        try
        {
            result = await HistoryService.GetResultAsync(entryId);
        }
        catch (Exception exception)
        {
            if (generation == sourceLoadGeneration && !IsDisposed)
            {
                ShowFileError("The history entry could not be loaded.", exception);
            }

            return;
        }

        if (generation != sourceLoadGeneration || IsDisposed)
        {
            return;
        }

        // Deleted between opening the menu and choosing: silent no-op, like the Compare picker.
        if (result == null)
        {
            return;
        }

        ImpulseResponseFile file = ImpulseResponseFile.From(result);
        ApplyMeasurementSource(
            file,
            displayName,
            $"History: {displayName}",
            EqWizardSourceResolver.DescribeArray(file, $"History: {displayName}"));
    }

    private void LoadCurveFromSlot(int slot)
    {
        sourceLoadGeneration++;
        EqWizardCurveSource? source = sourceResolver.TryCreateFromOverlaySlot(slot);
        if (source == null)
        {
            MessageBox.Show(
                FindForm(),
                $"Overlay slot {slot} no longer holds a curve that can be equalized.",
                "EQ Wizard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        LoadSource(source);
    }

    private async Task LoadCurveFromSpatialAverageAsync()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter =
                "Spatial average (*.json)|*.json|All files (*.*)|*.*",
            Title = "Load spatial average"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        int generation = ++sourceLoadGeneration;
        EqWizardCurveSource? source;
        try
        {
            source = await ResolveSpatialAverageAsync(dialog.FileName);
        }
        catch (Exception exception)
        {
            if (generation == sourceLoadGeneration && !IsDisposed)
            {
                ShowFileError("The spatial average could not be loaded.", exception);
            }

            return;
        }

        if (generation != sourceLoadGeneration || IsDisposed)
        {
            return;
        }

        if (source == null)
        {
            MessageBox.Show(
                FindForm(),
                "That file carries no spatial average. Load a moving-microphone " +
                    "capture, or a measurement recorded with a microphone array.",
                "EQ Wizard",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        LoadSource(source);
    }

    private static async Task<EqWizardCurveSource?> ResolveSpatialAverageAsync(string path)
    {
        if (LiveCaptureDocument.TryLoad(path, out LiveCaptureDocument document))
        {
            return EqWizardSourceResolver.CreateFromSpatialAverage(
                document,
                EqWizardSourceResolver.DescribeSpatialAverage(document, path));
        }

        ImpulseResponseFile file = await ImpulseResponseFile.LoadAsync(path);
        return EqWizardSourceResolver.TryCreateFromArray(
            file,
            System.IO.Path.GetFileNameWithoutExtension(path),
            EqWizardSourceResolver.DescribeArray(file, path));
    }

    private void LoadCurveFromTextFile()
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Measured curve (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Load measured curve"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        sourceLoadGeneration++;
        EqWizardCurveSource source;
        try
        {
            source = EqWizardSourceResolver.CreateFromTextCurve(
                OverlayTextFile.ImportCurve(dialog.FileName),
                dialog.FileName);
        }
        catch (Exception exception)
        {
            ShowFileError("The curve could not be loaded.", exception);
            return;
        }

        LoadSource(source);
    }

    private void LoadSource(EqWizardCurveSource source)
    {
        session.Load(source);
        PresentSource();
        Redraw();
    }

    // Everything a new source settles: its selectors, the phase gate button, the axis it needs and the handoff it ended.
    private void PresentSource()
    {
        Present(() => comboBoxSmooth.Enabled = session.SmoothingSelectable);
        PresentCalibration();
        PresentSampleRate();
        PresentQConvention();
        buttonPhaseGate.Enabled = session.PhaseContext != null;
        PresentCrossoverTarget();
        plot.FitSourceAxis(session);
        PresentHandoff();
        if (session.Source is { } source)
        {
            buttonSource.Text = source.DisplayName;
            toolTip.SetToolTip(
                buttonSource,
                $"{source.Description}\r\nClick to load another source.");
        }
    }

    private void OnTargetOffsetChanged()
    {
        if (presenting)
        {
            return;
        }

        session.SetTargetOffset(NumericTargetOffset.Value);
        Redraw();
    }

    private void PresentTargetOffset() => Present(() =>
    {
        NumericTargetOffset.ApplyFieldRange(session.TargetOffsetRange);
        NumericTargetOffset.Value = session.TargetOffsetDb;
    });

    /// <summary>The target as one value; the host shares it with the Virtual DSP tool, which edits it back via <see cref="ApplyTargetCurve"/>.</summary>
    internal EqTargetCurve TargetCurve => session.Target;

    /// <summary>Takes a target edited elsewhere; ignores an equal value so the host can push on every change without looping.</summary>
    internal void ApplyTargetCurve(EqTargetCurve value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (session.Target == value)
        {
            return;
        }

        session.SetTarget(value);
        Redraw();
    }

    private void ShowTargetMenu()
    {
        if (targetMenu is { Visible: true })
        {
            targetMenu.Close();
            return;
        }

        targetMenu?.Dispose();
        targetMenu = TargetCurveMenu.Build(
            session.Target.Spec.Imported,
            OpenTargetSettings,
            ImportTargetCurve);
        DropDownMenu.ShowUnder(buttonOverlaySettings, targetMenu);
    }

    private void ImportTargetCurve()
    {
        if (TargetCurveImport.Prompt(FindForm()) is not { } imported)
        {
            return;
        }

        ApplyTargetCurve(session.Target with
        {
            Spec = session.Target.Spec with { Imported = imported }
        });
        (double reachMinDb, double reachMaxDb) = plot.MagnitudeReach();
        if (TargetCurveImport.OfferLevel(
                FindForm(),
                imported,
                (double)session.TargetOffsetDb,
                NumericTargetOffset.FieldRange(),
                reachMinDb,
                reachMaxDb) is { } levelDb)
        {
            NumericTargetOffset.Value = levelDb;
        }
    }

    // Isolated overlay target dialog; Cancel reverts the preview. An imported curve rides as a preset entry so edits keep it.
    private void OpenTargetSettings()
    {
        EqTargetCurve before = session.Target;
        using var dialog = new OverlayTargetSettingsDialog(
            Mode.EqWizard,
            "EQ target",
            0,
            before.Preset,
            before.Spec,
            before.ToleranceDb,
            before.DeviationMode,
            before.Color,
            before.StrokeThickness,
            before.LineStyle,
            100,
            before.SmoothingInverseOctaves,
            Array.Empty<OverlaySlotOption>(),
            preview =>
            {
                session.PreviewTarget(preview);
                Redraw();
            },
            isolatedTarget: true);

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            session.RestoreTarget(before);
            Redraw();
            return;
        }

        session.SetTarget(new EqTargetCurve(
            dialog.Preset,
            dialog.Spec,
            dialog.ToleranceDb,
            dialog.DeviationMode,
            dialog.SelectedColor,
            dialog.StrokeThickness,
            dialog.LineStyle,
            dialog.SmoothingInverseOctaves));
        Redraw();
    }

    internal void ConfigureCalibration(
        Func<string?, CalibrationFile?> resolver,
        IReadOnlyList<MicrophoneCalibrationEntry> entries)
    {
        session.ConfigureCalibration(resolver, entries);
        PresentCalibration();
        Redraw();
    }

    private void PresentCalibration() => Present(() =>
    {
        comboBoxCalibration.Items.Clear();
        comboBoxCalibration.DropDownStyle = ComboBoxStyle.DropDownList;
        List<EqWizardCalibrationOption> options = session.CalibrationOptions.ToList();
        foreach (EqWizardCalibrationOption option in options)
        {
            comboBoxCalibration.Items.Add(option);
        }

        // The session settled its choice onto one of these; the first match, as a list with a repeat would select.
        comboBoxCalibration.SelectedIndex = Math.Max(0, options.FindIndex(option => option.Choice == session.CalibrationChoice));
        comboBoxCalibration.Enabled = session.CalibrationSelectable;
        toolTip.SetToolTip(
            comboBoxCalibration,
            session.Source is { Kind: EqWizardSourceKind.VirtualDspChannel }
                ? "Follows the Virtual DSP panel's calibration selector while a " +
                  "DSP channel is loaded — change it there."
                : string.Empty);
    });

    private void OnCalibrationChanged()
    {
        if (presenting)
        {
            return;
        }

        session.SelectCalibration(
            comboBoxCalibration.SelectedItem is EqWizardCalibrationOption option
                ? option.Choice
                : EqWizardCalibrationChoice.Off);
        Redraw();
    }

    /// <summary>
    /// The DSP's peaking-band Q convention. Moves the tuning-sheet numbers ONLY; fit, plot and profile exports stay RBJ.
    /// </summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal PeqQConvention TargetDspQConvention
    {
        get => session.QConvention;
        set
        {
            session.RestoreManualQConvention(value);
            PresentQConvention();
        }
    }

    /// <summary>The user's selected convention (persisted); a handoff's processor convention must never be saved over it.</summary>
    internal PeqQConvention ManualQConvention => session.ManualQConvention;

    private void InitializeQConventionComboBox()
    {
        comboBoxQConvention.Format += (_, args) =>
        {
            if (args.ListItem is PeqQConvention convention)
            {
                args.Value = PeqQConventions.DescribeShort(convention);
            }
        };
        foreach (PeqQConvention convention in DspProcessorCatalog.SelectableQConventions)
        {
            comboBoxQConvention.Items.Add(convention);
        }

        // Selected before the handler is attached, so construction is not a user change.
        comboBoxQConvention.SelectedItem = session.QConvention;
        comboBoxQConvention.SelectedIndexChanged += (_, _) =>
        {
            if (!presenting && comboBoxQConvention.SelectedItem is PeqQConvention convention)
            {
                session.SetManualQConvention(convention);
            }
        };
    }

    // A handoff locks the convention to its project's processor, like the rate.
    private void PresentQConvention() => Present(() =>
    {
        comboBoxQConvention.SelectedItem = session.QConvention;
        comboBoxQConvention.Enabled = !session.QConventionLocked;
    });

    private void InitializeSampleRateComboBox()
    {
        comboBoxSampleRate.Format += (_, args) =>
        {
            if (args.ListItem is int rate)
            {
                args.Value = $"{rate / 1000.0:0.###} kHz";
            }
        };
        comboBoxSampleRate.SelectedIndexChanged += (_, _) => OnSampleRateChanged();
        PresentSampleRate();
    }

    private void PresentSampleRate() => Present(() =>
    {
        comboBoxSampleRate.Items.Clear();
        foreach (int rate in session.SampleRateChoices)
        {
            comboBoxSampleRate.Items.Add(rate);
        }

        comboBoxSampleRate.SelectedItem = session.ProcessorSampleRateHz;
        comboBoxSampleRate.Enabled = !session.SampleRateLocked;
    });

    private void OnSampleRateChanged()
    {
        if (presenting)
        {
            return;
        }

        if (comboBoxSampleRate.SelectedItem is int rate)
        {
            session.SetManualSampleRate(rate);
        }

        Redraw();
    }

    internal void ApplyPersistedSettings(MeasurementSettingsFile.EqWizardSettings settings)
    {
        session.ApplySettings(settings);
        // Restored settings are not an edit anyone should undo into.
        bankEditTimer.Stop();
        PresentTargetOffset();
        PresentFitSettings();
        PresentViewSettings();
        Present(() => comboBoxSmooth.SelectedItem = session.SourceSmoothingInverseOctaves);
        PresentBank(keepSelection: true);
        PresentGainRange();
        PresentCalibration();
        PresentSampleRate();
        PresentQConvention();
        Redraw();
    }

    internal MeasurementSettingsFile.EqWizardSettings CaptureSettings() => session.CaptureSettings();
}
