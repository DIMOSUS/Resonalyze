using System.Diagnostics;
using OxyPlot;
using OxyPlot.Series;

namespace Resonalyze;

/// <summary>
/// The overlay slots of the main plot: what each holds, whether it shows, and the rules that decide both. Persists each
/// slot to its file and draws it onto the plot model; <see cref="OverlayPanel"/> binds the controls and runs the dialogs.
/// </summary>
internal sealed class OverlaySession
{
    private readonly List<OverlaySlot> slots = [];
    private readonly NumericFieldRange offsetRange;
    private readonly Action<PlotModel> refreshPlot;
    private readonly Action plotChanged;
    // A loop over slots repaints once at its end, not once per slot: each repaint is a full update and render.
    private int batchDepth;
    private PlotModel? pendingRefresh;
    private readonly string? storageRoot;

    /// <param name="refreshPlot">Repaints the plot after a slot changed its series; also announces the change.</param>
    /// <param name="plotChanged">Announces a change of what the slots show (labels, Show/Hide All).</param>
    /// <param name="storageRoot">Where the slot files live; null = the application's data folder.</param>
    public OverlaySession(
        OverlayPlotSources sources,
        NumericFieldRange offsetRange,
        decimal defaultOffset,
        Action<PlotModel> refreshPlot,
        Action plotChanged,
        string? storageRoot = null)
    {
        this.storageRoot = storageRoot;
        Sources = sources;
        Curves = new OverlayCurves(this);
        this.offsetRange = offsetRange;
        this.refreshPlot = refreshPlot;
        this.plotChanged = plotChanged;
        for (int index = 1; index <= OverlayFile.MaximumSlotCount; index++)
        {
            slots.Add(new OverlaySlot(
                index,
                OverlaySlotState.Empty(OverlayModes.SlotDefaultColor(index), defaultOffset)));
        }
    }

    /// <summary>A slot's content or flags changed; its controls show them again.</summary>
    public event Action<OverlaySlot>? SlotChanged;

    public event Action<string, Exception>? StorageFailed;

    public IReadOnlyList<OverlaySlot> Slots => slots;

    public OverlayPlotSources Sources { get; }

    public OverlayCurves Curves { get; }

    public void FlushPendingSaves()
    {
        foreach (OverlaySlot slot in slots)
        {
            FlushPendingSave(slot);
        }
    }

    public void Prepare(Mode mode)
    {
        Mode overlayMode = OverlayModes.SlotModeFor(mode);
        foreach (OverlaySlot slot in slots)
        {
            Prepare(slot, overlayMode);
        }

        foreach (OverlaySlot slot in slots)
        {
            RefreshSources(slot);
        }
    }

    public void Show(Mode mode)
    {
        Mode overlayMode = OverlayModes.SlotModeFor(mode);
        Batched(() =>
        {
            foreach (OverlaySlot slot in slots)
            {
                if (slot.Checked && slot.SeriesMode == overlayMode)
                {
                    Show(slot);
                }
            }
        });

        plotChanged();
    }

    /// <summary>The Show all button: ticks every slot of the mode that can show, as if the user ticked each.</summary>
    public void ShowAll(Mode mode)
    {
        Mode overlayMode = OverlayModes.SlotModeFor(mode);
        Batched(() =>
        {
            foreach (OverlaySlot slot in slots)
            {
                if (slot.SeriesMode == overlayMode && slot.CheckEnabled && slot.Title.Length > 0)
                {
                    SetShown(slot, true);
                }
            }
        });

        plotChanged();
    }

    public void HideAll()
    {
        Batched(() =>
        {
            foreach (OverlaySlot slot in slots)
            {
                Hide(slot);
            }
        });

        plotChanged();
    }

    /// <summary>Redraws the targets that follow the current measurement, without repainting; true when any did.</summary>
    public bool RefreshCurrentMeasurementTargets()
    {
        bool any = false;
        foreach (OverlaySlot slot in slots)
        {
            any |= RedrawCurrentMeasurementTarget(slot);
        }

        return any;
    }

    public bool HasOverlays(Mode mode)
    {
        Mode overlayMode = OverlayModes.SlotModeFor(mode);
        return slots.Any(slot => slot.SeriesMode == overlayMode && slot.Title.Length > 0);
    }

    public List<int> CaptureActiveSlots(Mode mode)
    {
        Mode overlayMode = OverlayModes.SlotModeFor(mode);
        return slots
            .Where(slot => slot.Checked && slot.SeriesMode == overlayMode)
            .Select(slot => slot.Index)
            .ToList();
    }

    public void RestoreActiveSlots(Mode mode, IReadOnlyList<int>? activeSlots)
    {
        if (activeSlots == null || activeSlots.Count == 0)
        {
            return;
        }

        Mode overlayMode = OverlayModes.SlotModeFor(mode);
        Batched(() =>
        {
            foreach (int index in activeSlots)
            {
                OverlaySlot? slot = slots.FirstOrDefault(
                    candidate => candidate.Index == index && candidate.SeriesMode == overlayMode);
                // Armed first: Show leaves an off-axis slot checked but undrawn, so it returns with its own scale.
                if (slot != null)
                {
                    SetChecked(slot, true);
                    Show(slot);
                }
            }
        });

        plotChanged();
    }

    /// <summary>The captures a calculated overlay or target in <paramref name="forSlot"/> may read: never its own, which it replaces.</summary>
    public IReadOnlyList<OverlaySlotOption> CaptureSourceOptions(OverlaySlot forSlot)
    {
        Mode overlayMode = Sources.CurrentOverlayMode;
        return slots
            .Where(slot =>
                slot != forSlot &&
                slot.Kind == OverlayKind.Captured &&
                slot.SeriesMode == overlayMode &&
                slot.State.HasCaptureData)
            .Select(slot => new OverlaySlotOption(slot.Index, slot.Title, Curves.SlotSemantics(slot)))
            .ToArray();
    }

    public OverlaySlot? FindCaptureSlot(int index)
    {
        Mode overlayMode = Sources.CurrentOverlayMode;
        return slots.FirstOrDefault(slot =>
            slot.Index == index &&
            slot.Kind == OverlayKind.Captured &&
            slot.SeriesMode == overlayMode);
    }

    /// <summary>Whether the slot's settings can be opened: it belongs to the mode on screen and holds something.</summary>
    public bool CanConfigure(OverlaySlot slot) =>
        slot.SeriesMode == Sources.CurrentOverlayMode && slot.State.HasContent;

    public void Show(OverlaySlot slot)
    {
        PlotModel? model = Sources.Model;
        if (model == null || slot.Title == "")
        {
            SetChecked(slot, false);
            return;
        }

        // Every draw path lands here, so the axis rule is asked once. An off-axis slot stays checked and undrawn,
        // so flipping the axis back restores it; SetShown refuses the tick separately.
        if (!Curves.DrawsOnShownScale(slot))
        {
            if (OverlaySeries.Remove(model, slot.SeriesMode, slot.Index))
            {
                Refresh(model);
            }

            return;
        }

        OverlaySeries.Remove(model, slot.SeriesMode, slot.Index);
        OverlaySlotState state = slot.State;
        // An impulse capture is stored framing-free and every build may move the origin, unit or level scale.
        if (state.Kind == OverlayKind.Captured && state.Captured?.Impulse is { Samples.Count: > 1 })
        {
            UpdateDrawPoints(slot);
        }

        bool drawn = state.Kind switch
        {
            OverlayKind.Target => AddTarget(model, slot, state.Target!, state.SmoothingInverseOctaves, state.Appearance, state.Title),
            OverlayKind.Operation => OverlaySeries.AddCurve(
                model,
                slot.SeriesMode,
                slot.Index,
                Curves.OperationPoints(slot, state.Operation!, state.SmoothingInverseOctaves),
                state.Appearance,
                state.Title,
                Curves.SlotSemantics(slot).YAxisKey),
            _ => OverlaySeries.AddCurve(
                model,
                slot.SeriesMode,
                slot.Index,
                slot.DrawPoints,
                state.Appearance,
                state.Title,
                state.Captured?.YAxisKey)
        };

        if (drawn)
        {
            SetChecked(slot, true);
            Refresh(model);
        }
        else if (state.IsCurrentMeasurementTarget || state.ReferencesLiveCurve || state.IsComplexSumOperation)
        {
            // Live-sourced targets/operations stay armed while their source is absent; they redraw once data appears.
            SetChecked(slot, true);
            Refresh(model);
        }
        else
        {
            SetChecked(slot, false);
        }
    }

    public void Hide(OverlaySlot slot)
    {
        PlotModel? model = Sources.Model;
        // Nothing drawn, nothing to repaint.
        if (model != null && OverlaySeries.Remove(model, slot.SeriesMode, slot.Index))
        {
            Refresh(model);
        }

        SetChecked(slot, false);
    }

    /// <summary>The user ticked or cleared the slot's checkbox.</summary>
    public void SetShown(OverlaySlot slot, bool shown)
    {
        slot.Checked = shown;
        if (!shown)
        {
            Hide(slot);
            return;
        }

        // Same rule as the post-rebuild redraw; disagreement made a slot appear on Save yet refuse the checkbox.
        if (slot.SeriesMode == Sources.CurrentOverlayMode && Curves.DrawsOnShownScale(slot))
        {
            Show(slot);
        }
        else
        {
            SetChecked(slot, false);
        }
    }

    public void Capture(OverlaySlot slot, LineSeries selected)
    {
        if (selected.Points.Count < 2)
        {
            return;
        }

        CapturedCurve curve = OverlayCapture.FromSeries(selected, Sources, out int seedSmoothing);
        // An occupied slot keeps its (possibly user-renamed) name.
        string title = OverlaySlotName.ForSave(
            slot.State.HasContent, slot.Title, slot.Index, selected.Title ?? string.Empty);
        Mode mode = Sources.CurrentOverlayMode;

        Hide(slot);
        slot.State = slot.State.WithCaptured(curve, mode, title);
        // Psychoacoustic smoothing is magnitude-only, which the new content decides.
        slot.State = slot.State with
        {
            SmoothingInverseOctaves = Curves.MagnitudeSmoothingSemantics(slot)
                ? seedSmoothing
                : Dsp.SpectrumSmoothing.EquivalentInverseOctaves(seedSmoothing)
        };
        UpdateDrawPoints(slot);
        Present(slot);

        if (!TrySave(slot, "Overlay could not be saved."))
        {
            return;
        }

        SetAvailability(slot, true);
        Show(slot);
        NotifyCapturedOverlayChanged();
    }

    /// <param name="mode">The overlay mode the import was started in.</param>
    public void Import(OverlaySlot slot, OverlayTextCurve imported, string fileName, Mode mode)
    {
        // Same rule as a capture: an occupied slot keeps its name, only an empty one is named after the file.
        string title = OverlaySlotName.ForSave(
            slot.State.HasContent,
            slot.Title,
            slot.Index,
            Path.GetFileNameWithoutExtension(fileName));

        Hide(slot);
        slot.State = slot.State.WithCaptured(
            OverlayCapture.FromText(imported, Sources.CurrentMagnitudeScale), mode, title);
        UpdateDrawPoints(slot);
        Present(slot);

        if (!TrySave(slot, "Overlay could not be saved."))
        {
            return;
        }

        SetAvailability(slot, true);
        Show(slot);
        NotifyCapturedOverlayChanged();
    }

    public void ApplyCaptured(OverlaySlot slot, string title, OverlayAppearance appearance, int smoothing)
    {
        bool wasChecked = slot.Checked;
        Hide(slot);
        slot.State = slot.State with
        {
            Title = title,
            Appearance = appearance,
            SmoothingInverseOctaves = smoothing
        };
        UpdateDrawPoints(slot);
        Present(slot);
        TrySave(slot, "Overlay changes could not be saved.");

        if (wasChecked)
        {
            Show(slot);
        }
        NotifyCapturedOverlayChanged();
    }

    public void ApplyOperation(
        OverlaySlot slot,
        string title,
        OverlayOperationSettings operation,
        OverlayAppearance appearance,
        int smoothing)
    {
        Hide(slot);
        slot.State = slot.State with
        {
            Title = title,
            Appearance = appearance,
            SmoothingInverseOctaves = smoothing,
            Captured = null,
            Operation = operation,
            Target = null
        };
        Present(slot);

        TrySave(slot, "Overlay changes could not be saved.");
        RefreshSources(slot);
        if (slot.CheckEnabled)
        {
            Show(slot);
        }
    }

    public void ApplyTarget(
        OverlaySlot slot,
        string title,
        OverlayTargetSettings target,
        OverlayAppearance appearance,
        int smoothing)
    {
        Hide(slot);
        slot.State = slot.State with
        {
            Title = title,
            Appearance = appearance,
            SmoothingInverseOctaves = smoothing,
            Captured = null,
            Operation = null,
            Target = target
        };
        Present(slot);

        TrySave(slot, "Overlay changes could not be saved.");
        SetAvailability(slot, true);
        Show(slot);
    }

    /// <summary>The slot's own Clear, which empties only a slot of the mode on screen.</summary>
    public void Clear(OverlaySlot slot)
    {
        if (slot.SeriesMode != Sources.CurrentOverlayMode)
        {
            return;
        }

        try
        {
            OverlayFile.Delete(slot.SeriesMode, slot.Index, storageRoot);
        }
        catch (Exception exception)
        {
            StorageFailed?.Invoke("Overlay could not be deleted.", exception);
            return;
        }

        Hide(slot);
        Reset(slot, slot.SeriesMode);
        NotifyCapturedOverlayChanged();
    }

    /// <summary>The menu's Clear slot: deletes the current mode's file for this slot.</summary>
    public void ClearSlot(OverlaySlot slot)
    {
        Mode mode = Sources.CurrentOverlayMode;
        if (mode == Mode.None)
        {
            return;
        }

        try
        {
            OverlayFile.Delete(mode, slot.Index, storageRoot);
            // Dropped only after the delete succeeded, or a failed delete silently loses the offset.
            slot.SavePending = false;
        }
        catch (Exception exception)
        {
            StorageFailed?.Invoke("Overlay slot could not be cleared.", exception);
            return;
        }

        Hide(slot);
        Reset(slot, mode);
        NotifyCapturedOverlayChanged();
        // Hide() announced before the reset; announce again so Show/Hide All and labels see the cleared slot.
        plotChanged();
    }

    /// <summary>The user changed the offset field; true when a save is now pending.</summary>
    public bool SetOffset(OverlaySlot slot, decimal offset)
    {
        slot.State = slot.State with { Offset = offsetRange.Assign(offset) };
        if (slot.Kind == OverlayKind.Captured && slot.State.Captured == null)
        {
            return false;
        }

        bool wasChecked = slot.Checked;
        if (slot.Kind == OverlayKind.Captured)
        {
            UpdateDrawPoints(slot);
        }
        // Debounced: saving serializes every point and flushes to disk; the redraw stays immediate.
        slot.SavePending = true;
        if (wasChecked)
        {
            Show(slot);
        }
        // Only captured slots feed other overlays; a target/operation offset cannot change any input.
        if (slot.Kind == OverlayKind.Captured)
        {
            NotifyCapturedOverlayChanged();
        }

        return true;
    }

    public void FlushPendingSave(OverlaySlot slot)
    {
        if (!slot.SavePending)
        {
            return;
        }

        slot.SavePending = false;
        TrySave(slot, "Overlay changes could not be saved.");
    }

    public void BeginPreview(OverlaySlot slot) => slot.PreviewActive = true;

    public void EndPreview(OverlaySlot slot) => slot.PreviewActive = false;

    public void PreviewCaptured(OverlaySlot slot, OverlayCapturedPreview settings)
    {
        PlotModel? model = Sources.Model;
        if (model == null)
        {
            return;
        }

        OverlaySeries.Remove(model, slot.SeriesMode, slot.Index);
        OverlaySeries.AddCurve(
            model,
            slot.SeriesMode,
            slot.Index,
            Curves.DrawsOnShownScale(slot)
                ? Curves.CapturedPoints(slot, settings.SmoothingInverseOctaves)
                : null,
            new OverlayAppearance(settings.Color, settings.StrokeThickness, settings.LineStyle, settings.OpacityPercent),
            settings.Name.Length > 0 ? settings.Name : slot.Title,
            slot.State.Captured?.YAxisKey);
        Refresh(model);
    }

    public void PreviewOperation(OverlaySlot slot, OverlayOperationPreview settings)
    {
        PlotModel? model = Sources.Model;
        if (model == null)
        {
            return;
        }

        OverlaySeries.Remove(model, slot.SeriesMode, slot.Index);
        OverlayOperationSettings operation = OverlayOperationSettings.From(settings);
        OverlayCurveSemantics semantics = Curves.ResultFor(operation).Curve;
        DataPoint[]? points = semantics.DrawsOn(slot.SeriesMode, Sources.CurrentMagnitudeScale)
            ? Curves.OperationPoints(slot, operation, settings.SmoothingInverseOctaves)
            : null;
        if (points != null)
        {
            OverlaySeries.AddCurve(
                model,
                slot.SeriesMode,
                slot.Index,
                points,
                new OverlayAppearance(settings.Color, settings.StrokeThickness, settings.LineStyle, settings.OpacityPercent),
                settings.Name.Length > 0 ? settings.Name : slot.Title,
                semantics.YAxisKey);
        }

        Refresh(model);
    }

    public void PreviewTarget(OverlaySlot slot, OverlayTargetPreview settings)
    {
        PlotModel? model = Sources.Model;
        if (model == null)
        {
            return;
        }

        OverlaySeries.Remove(model, slot.SeriesMode, slot.Index);
        if (!Curves.DrawsOnShownScale(slot))
        {
            Refresh(model);
            return;
        }

        AddTarget(
            model,
            slot,
            new OverlayTargetSettings(
                settings.SourceSlot,
                TargetPreset.Custom,
                settings.Spec,
                settings.ToleranceDb,
                settings.DeviationMode),
            settings.SmoothingInverseOctaves,
            new OverlayAppearance(settings.Color, settings.StrokeThickness, settings.LineStyle, settings.OpacityPercent),
            settings.Name.Length > 0 ? settings.Name : slot.Title);
        Refresh(model);
    }

    public void RestoreAfterPreview(OverlaySlot slot, bool wasChecked)
    {
        PlotModel? model = Sources.Model;
        if (model == null)
        {
            return;
        }

        OverlaySeries.Remove(model, slot.SeriesMode, slot.Index);
        if (wasChecked)
        {
            Show(slot);
        }
        else
        {
            Refresh(model);
        }
    }

    private void Prepare(OverlaySlot slot, Mode mode)
    {
        // Flush first, or the last offset change is dropped when state is replaced from disk.
        FlushPendingSave(slot);
        Reset(slot, mode);
        if (mode == Mode.None)
        {
            return;
        }

        try
        {
            OverlayFile? file = OverlayFile.Load(mode, slot.Index, storageRoot);
            if (file != null)
            {
                Load(slot, file);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Failed to load overlay slot {slot.Index} for {mode}: {exception}");
            Reset(slot, mode);
            QuarantineCorruptSlot(slot, mode, exception);
        }
    }

    private void Load(OverlaySlot slot, OverlayFile file)
    {
        slot.State = OverlaySlotState.FromFile(file, offsetRange);
        if (slot.Kind == OverlayKind.Target)
        {
            SetAvailability(slot, true);
        }
        else if (slot.Kind == OverlayKind.Captured)
        {
            UpdateDrawPoints(slot);
            SetAvailability(slot, true);
        }

        Present(slot);
    }

    // Set aside so the next capture does not silently overwrite a damaged file, and it warns once.
    private void QuarantineCorruptSlot(OverlaySlot slot, Mode mode, Exception error)
    {
        try
        {
            string? quarantinePath = OverlayFile.QuarantineCorruptFile(mode, slot.Index, storageRoot);
            if (quarantinePath != null)
            {
                StorageFailed?.Invoke(
                    $"Overlay slot {slot.Index} for {mode} could not be loaded; " +
                    $"the file was kept as {Path.GetFileName(quarantinePath)}.",
                    error);
            }
        }
        catch
        {
        }
    }

    private void RefreshSources(OverlaySlot slot)
    {
        OverlaySlotState state = slot.State;
        if (state.Operation is { } operation)
        {
            // A live operand may be momentarily absent; keep a configured operation available. Slot-only ones need captures.
            bool available = Curves.OperationIsDefined(slot) &&
                (operation.ReferencesLiveCurve || operation.IsComplexSum || Curves.HasOperands(operation));
            ApplyCalculatedAvailability(slot, available);
        }
        else if (state.Target != null)
        {
            ApplyCalculatedAvailability(slot, available: true);
        }
    }

    private void ApplyCalculatedAvailability(OverlaySlot slot, bool available)
    {
        bool wasChecked = slot.Checked;
        slot.CheckEnabled = available;
        slot.OffsetEnabled = true;
        Present(slot);

        if (!available)
        {
            Hide(slot);
            return;
        }
        if (wasChecked)
        {
            Show(slot);
        }
    }

    private void NotifyCapturedOverlayChanged()
    {
        foreach (OverlaySlot slot in slots)
        {
            if (slot.Kind != OverlayKind.Captured)
            {
                RefreshSources(slot);
            }
        }
    }

    private bool RedrawCurrentMeasurementTarget(OverlaySlot slot)
    {
        // Asked anyway: this draws directly, and skipped axis checks are what put SPL on a relative axis.
        if (!slot.Checked ||
            !slot.State.IsCurrentMeasurementTarget ||
            slot.PreviewActive ||
            !Curves.DrawsOnShownScale(slot))
        {
            return false;
        }

        PlotModel? model = Sources.Model;
        if (model == null)
        {
            return false;
        }

        OverlaySeries.Remove(model, slot.SeriesMode, slot.Index);
        OverlaySlotState state = slot.State;
        AddTarget(model, slot, state.Target!, state.SmoothingInverseOctaves, state.Appearance, state.Title);
        return true;
    }

    private bool AddTarget(
        PlotModel model,
        OverlaySlot slot,
        OverlayTargetSettings target,
        int smoothing,
        OverlayAppearance appearance,
        string title)
    {
        if (Curves.Target(slot, target, smoothing) is not { } drawn)
        {
            return false;
        }

        OverlaySeries.AddTarget(
            model,
            slot.SeriesMode,
            slot.Index,
            drawn.Shape,
            drawn.Deviation,
            target.DeviationMode,
            appearance,
            title);
        return true;
    }

    private bool TrySave(OverlaySlot slot, string errorMessage)
    {
        if (slot.SeriesMode == Mode.None || slot.Title == "")
        {
            return false;
        }
        if (slot.Kind == OverlayKind.Captured && slot.State.Captured == null)
        {
            return false;
        }

        try
        {
            slot.State.ToFile(slot.Index).Save(storageRoot);
            return true;
        }
        catch (Exception exception)
        {
            StorageFailed?.Invoke(errorMessage, exception);
            return false;
        }
    }

    private void UpdateDrawPoints(OverlaySlot slot) =>
        slot.DrawPoints = Curves.CapturedPoints(slot, slot.State.SmoothingInverseOctaves);

    private void Refresh(PlotModel model)
    {
        if (batchDepth > 0)
        {
            pendingRefresh = model;
            return;
        }

        refreshPlot(model);
    }

    private void Batched(Action body)
    {
        batchDepth++;
        try
        {
            body();
        }
        finally
        {
            if (--batchDepth == 0 && pendingRefresh is { } model)
            {
                pendingRefresh = null;
                refreshPlot(model);
            }
        }
    }

    // An emptied slot stays in its mode, so what is put into it next is saved there.
    private void Reset(OverlaySlot slot, Mode mode)
    {
        slot.State = slot.Empty with { Mode = mode };
        slot.DrawPoints = null;
        slot.Checked = false;
        slot.CheckEnabled = false;
        slot.OffsetEnabled = false;
        Present(slot);
    }

    private void SetAvailability(OverlaySlot slot, bool available)
    {
        slot.CheckEnabled = available;
        slot.OffsetEnabled = available;
        if (!available)
        {
            slot.Checked = false;
        }

        Present(slot);
    }

    private void SetChecked(OverlaySlot slot, bool value)
    {
        slot.Checked = value;
        Present(slot);
    }

    private void Present(OverlaySlot slot) => SlotChanged?.Invoke(slot);
}
