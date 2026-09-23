namespace Resonalyze;

/// <summary>The target curve: shared with the EQ Wizard through the host, drawn on the magnitude view, edited here.</summary>
public partial class VirtualCrossoverPanel
{
    private EqTargetCurve? targetCurve;
    private ContextMenuStrip? targetMenu;

    // Kept so a toggle muted for a view has its live colour to return to.
    private Color targetToggleColor;

    /// <summary>Shared EQ target pushed by the host; an equal value is ignored (no redraw).</summary>
    internal void SetTargetCurve(EqTargetCurve value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (targetCurve == value)
        {
            return;
        }

        targetCurve = value;
        targetToggleColor = value.Color;
        StoreTargetInProject(value);
        UpdateTargetToggleLook();
        if (checkBoxShowTarget.Checked && radioViewMagnitude.Checked)
        {
            RedrawAll();
        }
    }

    // The project holds the target level; the field shows it and hands back the user's edit.
    private void OnTargetLevelEdited()
    {
        if (suppressProjectEvents)
        {
            return;
        }

        session.Project.TargetLevelDb = (double)numericTargetLevel.Value;
        OnViewChanged();
    }

    private void ShowTargetLevel()
    {
        bool suppressed = suppressProjectEvents;
        suppressProjectEvents = true;
        try
        {
            numericTargetLevel.Value = VirtualCrossoverLimits.TargetLevel.Clamp(session.Project.TargetLevelDb);
        }
        finally
        {
            suppressProjectEvents = suppressed;
        }
    }

    // A level written by code (a return, an import, a level offer) lands like an edit of the field.
    private void SetTargetLevel(double levelDb)
    {
        double held = (double)VirtualCrossoverLimits.TargetLevel.Clamp(levelDb);
        if (held.Equals(session.Project.TargetLevelDb))
        {
            return;
        }

        session.Project.TargetLevelDb = held;
        ShowTargetLevel();
        OnViewChanged();
    }

    // Disabled CheckBox text is near-black on this theme, so mute by hand; not via SetTextEnabledLook, which
    // memorizes a colour that follows the shared target.
    private void UpdateTargetToggleLook()
    {
        bool magnitude = radioViewMagnitude.Checked;
        checkBoxShowTarget.ForeColor =
            magnitude ? targetToggleColor : Ui.UiPalette.TextDisabled;
        checkBoxShowTarget.AutoCheck = magnitude;
        checkBoxShowTarget.TabStop = magnitude;
    }

    // Handed to the host (the EQ Wizard owns the one target). A session without a stored target starts carrying the current one.
    private void ApplyProjectTarget()
    {
        if (session.Project.Target is { } stored)
        {
            TargetCurveChanged?.Invoke(stored.ToCurve());
            return;
        }

        if (targetCurve is { } current)
        {
            session.Project.Target = VirtualCrossoverTargetSettings.FromCurve(current);
        }
    }

    // Same menu as the EQ Wizard's Target button; rebuilt per click.
    private void ShowTargetMenu()
    {
        if (targetCurve is not { } current)
        {
            return;
        }

        if (targetMenu is { Visible: true })
        {
            targetMenu.Close();
            return;
        }

        targetMenu?.Dispose();
        targetMenu = TargetCurveMenu.Build(
            current.Spec.Imported,
            OpenTargetSettings,
            ImportTargetCurve);
        DropDownMenu.ShowUnder(buttonTargetSettings, targetMenu);
    }

    private void ImportTargetCurve()
    {
        if (targetCurve is not { } before ||
            TargetCurveImport.Prompt(FindForm()) is not { } imported)
        {
            return;
        }

        radioViewMagnitude.Checked = true;
        checkBoxShowTarget.Checked = true;
        var edited = before with
        {
            Spec = before.Spec with { Imported = imported }
        };
        ApplyTargetLocally(edited);
        StoreTargetInProject(edited);
        TargetCurveChanged?.Invoke(edited);
        if (TargetCurveImport.OfferLevel(
                FindForm(),
                imported.Name,
                imported.PeakDb,
                session.Project.TargetLevelDb,
                VirtualCrossoverLimits.TargetLevel,
                VirtualCrossoverAcousticPlot.MagnitudeFloorDb,
                PlotModelStyle.RelativeDecibelAbsoluteMaximum) is { } levelDb)
        {
            SetTargetLevel((double)levelDb);
        }
    }

    // The EQ Wizard's isolated target dialog previewing on this plot; Save hands the curve to the host.
    private void OpenTargetSettings()
    {
        if (targetCurve is not { } before)
        {
            return;
        }

        // Put the target on screen: magnitude is the only view where a dB shape means anything.
        radioViewMagnitude.Checked = true;
        checkBoxShowTarget.Checked = true;
        // Opened as the EQ Wizard's dialog so the smoothing vocabulary matches from either button.
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
            [],
            ApplyTargetPreview,
            isolatedTarget: true);

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            ApplyTargetLocally(before);
            return;
        }

        var edited = new EqTargetCurve(
            dialog.Preset,
            dialog.Spec,
            dialog.ToleranceDb,
            dialog.DeviationMode,
            dialog.SelectedColor,
            dialog.StrokeThickness,
            dialog.LineStyle,
            dialog.SmoothingInverseOctaves);
        ApplyTargetLocally(edited);
        StoreTargetInProject(edited);
        TargetCurveChanged?.Invoke(edited);
    }

    // The preview carries no preset, so the current one rides through untouched.
    private void ApplyTargetPreview(OverlayTargetPreview preview)
    {
        if (targetCurve is not { } current)
        {
            return;
        }

        ApplyTargetLocally(current with
        {
            Spec = preview.Spec,
            ToleranceDb = preview.ToleranceDb,
            DeviationMode = preview.DeviationMode,
            Color = preview.Color,
            StrokeThickness = preview.StrokeThickness,
            LineStyle = preview.LineStyle,
            SmoothingInverseOctaves = preview.SmoothingInverseOctaves
        });
    }

    // Memory and plot only: the autosave ticks inside the modal loop and would write an uncommitted preview.
    private void ApplyTargetLocally(EqTargetCurve value)
    {
        targetCurve = value;
        targetToggleColor = value.Color;
        UpdateTargetToggleLook();
        RedrawAll();
    }

    private void StoreTargetInProject(EqTargetCurve value)
    {
        session.Project.Target = VirtualCrossoverTargetSettings.FromCurve(value);
        ScheduleSave();
    }
}
