using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A block's PEQ bank: the menu, the EQ Wizard handoff and its return, and saving or loading a bank in the
/// hardware formats.</summary>
public partial class VirtualCrossoverPanel
{
    private readonly EqWizardImportExportCoordinator peqExport = new();

    // The offset belongs to the capture SET; one handed-over channel could not re-derive it.
    private (long Revision, double OffsetDb)? lastHybrid;

    // Rebuilt per click: enabled states follow channel state.
    private void ShowPeqMenu(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings peqSettings = channel.Settings;
        bool hasPeq =
            peqSettings.PeqBands.Count > 0 || peqSettings.PeqPreampDb != 0;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Load from file…", null, (_, _) => LoadPeq(channel));
        var saveItem = new ToolStripMenuItem(
            "Save to file…",
            null,
            (_, _) => SavePeq(channel))
        {
            Enabled = hasPeq,
            ToolTipText =
                "Write this channel's bank out as an EQ profile (or a tuning\r\n" +
                "sheet PDF) — the whole tune can be built here without a file,\r\n" +
                "so this is where it leaves for the hardware."
        };
        menu.Items.Add(saveItem);
        menu.Items.Add(new ToolStripSeparator());

        bool hasMeasurement =
            channel.SideState(channel.ActiveRight).TransferImpulseResponse != null;
        // A bypassed block draws raw here, so say before the trip that the wizard's curve differs.
        bool bypassed = channel.Pair.Bypass;
        var editItem = new ToolStripMenuItem(
            bypassed
                ? "Edit in EQ Wizard (chain — block is bypassed)"
                : "Edit in EQ Wizard",
            null,
            (_, _) => RequestPeqHandoff(channel, withChain: true))
        {
            Enabled = hasMeasurement,
            ToolTipText = "Tune this channel's PEQ in the EQ Wizard against its\r\n" +
                "response through the DSP chain with the PEQ itself bypassed,\r\n" +
                "windowed as this plot windows it. A Return button brings\r\n" +
                "the result back to this channel." +
                (bypassed
                    ? "\r\nThis block is BYPASSED, so the plot is drawing its raw\r\n" +
                      "response — the wizard will show the chain instead, which is\r\n" +
                      "what the PEQ is for once bypass comes off."
                    : string.Empty)
        };
        menu.Items.Add(editItem);
        var editRawItem = new ToolStripMenuItem(
            "Edit raw in EQ Wizard",
            null,
            (_, _) => RequestPeqHandoff(channel, withChain: false))
        {
            Enabled = hasMeasurement,
            ToolTipText = "The same handoff against the raw measurement — the\r\n" +
                "driver before the DSP chain, as the Raw curve draws it."
        };
        menu.Items.Add(editRawItem);

        menu.Items.Add(new ToolStripSeparator());
        var clearItem = new ToolStripMenuItem(
            "Clear",
            null,
            (_, _) => ClearPeq(channel))
        {
            Enabled = hasPeq
        };
        menu.Items.Add(clearItem);

        DropDownMenu.ShowUnder(ControlFor(channel).PeqMenuButton, menu);
    }

    // The gate mirrors the magnitude view: shared template, active pin, last redraw's anchor.
    private void RequestPeqHandoff(VirtualCrossoverChannel channel, bool withChain)
    {
        if (EditPeqInWizardRequested is not { } requested)
        {
            return;
        }

        VirtualDspEqHandoffRequest? request = BuildPeqHandoffRequest(
            channel, withChain, HandoffSpatialAverage(channel, channel.ActiveRight));
        if (request != null)
        {
            requested(request);
        }
    }

    // Null when the side has no measurement.
    private VirtualDspEqHandoffRequest? BuildPeqHandoffRequest(
        VirtualCrossoverChannel channel,
        bool withChain,
        (LiveCaptureDocument? Capture, double OffsetDb) spatialAverage,
        // Written to the panel only once the fit has landed.
        double? targetLevelDb = null)
    {
        MagnitudeGateSnapshot snapshot = session.MagnitudeGate;
        // Only a render describing the CURRENT settings may place the window; stale -> the builder reads the channel's front.
        int? renderAnchor =
            lastProcessedRender is { Channels.Count: >= 2 } render &&
            processingCoordinator.IsCurrent(render.Revision)
                ? ProcessedChannels.SharedStartAnchorIndex(render.Channels)
                : null;
        VirtualDspEqHandoffRequest request;
        try
        {
            request = VirtualDspEqHandoff.Build(
                channel,
                channel.ActiveRight,
                withChain,
                session.ProcessorProfile,
                snapshot.Template,
                snapshot.PinnedOffsetMs,
                renderAnchor,
                CapturePhaseContext(channel),
                targetLevelDb ?? session.Project.TargetLevelDb,
                (double)VirtualCrossoverLimits.TargetLevel.Minimum,
                (double)VirtualCrossoverLimits.TargetLevel.Maximum,
                snapshot.SmoothingInverseOctaves,
                // The wizard pins what the panel rendered with, including per-channel Own calibration.
                session.Calibration.For(channel.SideState(channel.ActiveRight)),
                session.Calibration.NameFor(channel.SideState(channel.ActiveRight)),
                session.Calibration.SpatialAverageFor(),
                projectGeneration,
                spatialAverage.Capture,
                spatialAverage.OffsetDb,
                HybridRequested &&
                    session.SpatialAverageMode == VirtualCrossoverSpatialAverageMode.MicArray &&
                    spatialAverage.Capture == null);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return request;
    }

    /// <summary>Other drivers as processed IRs (not drawn curves, so the wizard can re-gate), plus window and τ.</summary>
    /// <remarks>Resolved over the set the wizard draws (<see cref="ProcessedChannels.PhaseNeighbourhood"/>); null when the render is stale.</remarks>
    private EqWizardPhaseContext? CapturePhaseContext(VirtualCrossoverChannel channel)
    {
        if (lastProcessedRender is not { } render ||
            !processingCoordinator.IsCurrent(render.Revision))
        {
            return null;
        }

        List<ProcessedChannel> drawn =
            ProcessedChannels.PhaseNeighbourhood(render.Channels, channel);
        int index = drawn.FindIndex(item => ReferenceEquals(item.Channel, channel));
        if (index < 0)
        {
            return null;
        }

        // Off the snapshot: a concurrent import rebinds channels and the live rate reads zero.
        int sampleRate = drawn[0].SampleRate;
        VirtualCrossoverPhaseGate gate = session.Gate;
        double referenceOffsetMs = gate.ReferenceOffsetMs(drawn, sampleRate);
        double detrendMs = gate.CommonDetrendMs(drawn, referenceOffsetMs, sampleRate);
        List<double> offsets = gate.PerCurveOffsets(drawn, referenceOffsetMs, sampleRate);

        return new EqWizardPhaseContext(
            // Curves render as Manual against one τ for the whole set, but the user's detrend mode must arrive intact.
            gate.Settings(
                referenceOffsetMs,
                gate.DetrendMode,
                detrendMs),
            offsets[index],
            detrendMs,
            gate.PinnedOffsetMs is not null,
            // The source responses travel too, so the wizard re-resolves placements the same way when its window changes.
            PlacementChannel.From(drawn[index]),
            sampleRate,
            drawn[index].Color,
            drawn
                .Select((item, position) => (item, position))
                .Where(entry => entry.position != index)
                .Select(entry => new EqWizardPhaseNeighbour(
                    entry.item.Channel.Name,
                    entry.item.Color,
                    PlacementChannel.From(entry.item),
                    offsets[entry.position]))
                .ToList());
    }

    /// <summary>False, writing nothing, when the channel is gone (removed or replaced by an import).</summary>
    internal bool TryApplyPeqFromWizard(
        VirtualDspEqReturnToken token,
        EqualizationCurve curve,
        double targetLevelDb)
    {
        MagnitudeGateSnapshot snapshot = session.MagnitudeGate;
        if (!VirtualDspEqHandoff.TryApplyReturn(
                session.Channels,
                token,
                curve,
                projectGeneration,
                // Per side: under Own the panel holds no single calibration, and null would refuse every return.
                session.Calibration.For(token.Channel.SideState(token.RightSide)),
                session.Calibration.SpatialAverageFor(),
                snapshot.Template,
                snapshot.PinnedOffsetMs,
                session.Project.TargetLevelDb,
                // Same decision the handoff recorded, so an in-flight redraw cannot turn a valid return into a refusal.
                HybridHandoffCapture(token.Channel, token.RightSide),
                session.ProcessorSampleRateHz))
        {
            return false;
        }

        // The guard above proved the level is still the wizard's starting point, so writing it overwrites nothing.
        SetTargetLevel(targetLevelDb);

        UpdatePeqReadouts(token.Channel);
        SaveAndRedraw();
        return true;
    }

    /// <summary>The spatial average handed to the EQ Wizard, or null when the hybrid is not drawn.</summary>
    /// <remarks>Cheap and redraw-independent so the handoff and the return guard cannot disagree mid-redraw.</remarks>
    private LiveCaptureDocument? HybridHandoffCapture(
        VirtualCrossoverChannel channel, bool rightSide) =>
        HybridRequested
            ? channel.SideState(rightSide).SpatialAverageFor(session.SpatialAverageMode)
            : null;

    /// <summary>The capture plus its offset onto the IR axis; resolved here when no current magnitude render carries it.</summary>
    private (LiveCaptureDocument? Capture, double OffsetDb) HandoffSpatialAverage(
        VirtualCrossoverChannel channel, bool rightSide)
    {
        if (HybridHandoffCapture(channel, rightSide) is not { } capture)
        {
            return (null, 0.0);
        }

        if (lastHybrid is { } cached && processingCoordinator.IsCurrent(cached.Revision))
        {
            return (capture, cached.OffsetDb);
        }

        if (lastProcessedRender is not { } render ||
            !processingCoordinator.IsCurrent(render.Revision))
        {
            return (null, 0.0);
        }

        (List<AnalysisCurve>? magnitudes, _, _) =
            metrics.BuildCurves(render.Channels, session.MagnitudeGate.SmoothingInverseOctaves);
        if (magnitudes == null ||
            hybridReader.Build(
                render.Channels,
                magnitudes,
                rightSide,
                session.MagnitudeGate.SmoothingInverseOctaves) is not { } hybrid)
        {
            return (null, 0.0);
        }

        return (capture, hybrid.OffsetDb);
    }

    // Same coordinator and formats as the EQ Wizard: this is the door to the hardware, not a second exporter.
    private void SavePeq(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        var curve = new EqualizationCurve(settings.PeqBands, settings.PeqPreampDb);
        string side = channel.Pair.Mono ? "mono" : channel.ActiveRight ? "R" : "L";
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = peqExport.DefaultExportExtension,
            FileName = $"channel-{channel.Name}-{side}",
            Filter = peqExport.ExportFilter,
            RestoreDirectory = true,
            Title = $"Save channel {channel.Name} PEQ"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        EqWizardExportTarget target = peqExport.ResolveExportTarget(dialog.FilterIndex);
        if (!ConfirmPeqExportLoss(EqExportWarnings.ShelvingBandsDropped(target, curve)) ||
            !ConfirmPeqExportLoss(EqExportWarnings.AllPassBandsDropped(target, curve)) ||
            !ConfirmPeqExportLoss(EqExportWarnings.PreampDropped(target, curve)))
        {
            return;
        }

        (double minHz, double maxHz) =
            VirtualDspEqHandoff.PassbandFor(settings) ?? (20.0, 20_000.0);
        EqWizardFileResult result = peqExport.Export(
            new EqWizardExportRequest(
                dialog.FileName,
                target,
                curve,
                // Stated for the device's rate and Q convention, not the measurement rate.
                session.ProcessorSampleRateHz,
                $"Channel {channel.Name} ({side})",
                minHz,
                maxHz,
                // Bands were not necessarily fitted here; no invented statistics.
                Stats: null,
                session.ProcessorProfile.QConvention));
        if (!result.Success)
        {
            ShowError("PEQ could not be exported.", result.Exception!.Message);
        }
    }

    private bool ConfirmPeqExportLoss(string? warning) =>
        warning == null ||
        MessageBox.Show(
            FindForm(),
            warning,
            "Virtual DSP",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;

    private void LoadPeq(VirtualCrossoverChannel channel)
    {
        IReadOnlyList<IEqProfileFormat> formats = EqProfileFormats.Importable;
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = EqFormatFileDialogs.BuildFilter(formats),
            Title = $"Load channel {channel.Name} PEQ"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        IEqProfileFormat chosen =
            EqFormatFileDialogs.ResolveFormat(formats, dialog.FilterIndex)!;
        EqualizationCurve curve;
        try
        {
            // An unrecognised file would silently clear the channel's PEQ below.
            if (!chosen.TryImport(File.ReadAllText(dialog.FileName), out curve))
            {
                ShowError(
                    "PEQ could not be imported.",
                    $"No equalizer settings were found. Check that the file really is a " +
                    $"{chosen.Name} profile.");
                return;
            }
        }
        catch (Exception exception)
        {
            ShowError("PEQ could not be imported.", exception.Message);
            return;
        }

        channel.Settings.PeqBands = curve.Bands
            .Take(EqualizationCurve.MaxBandCount)
            .ToList();
        channel.Settings.PeqPreampDb = curve.PreampDb;
        channel.Settings.PeqSourceName = Path.GetFileName(dialog.FileName);
        UpdatePeqReadouts(channel);
        SaveAndRedraw();
    }

    private void ClearPeq(VirtualCrossoverChannel channel)
    {
        channel.Settings.PeqBands = new List<PeqBand>();
        channel.Settings.PeqPreampDb = 0;
        channel.Settings.PeqSourceName = null;
        UpdatePeqReadouts(channel);
        SaveAndRedraw();
    }

    private void UpdatePeqReadouts(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        bool noPeq = settings.PeqBands.Count == 0 && settings.PeqPreampDb == 0;
        string text = noPeq
            ? "No PEQ"
            : $"{settings.PeqSourceName ?? "PEQ"}: {settings.PeqBands.Count} bands, " +
              $"preamp {settings.PeqPreampDb:0.0} dB";
        // The block keeps its gain readout in step with the preamp itself.
        VirtualCrossoverChannelControl control = ControlFor(channel);
        control.PeqPreampDb = settings.PeqPreampDb;
        Label peqInfoLabel = control.PeqInfoLabel;
        peqInfoLabel.Text = text;
        toolTip.SetToolTip(peqInfoLabel, noPeq ? string.Empty : text);
    }
}
