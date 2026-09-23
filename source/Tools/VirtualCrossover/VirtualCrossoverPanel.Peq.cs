using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A block's PEQ bank: the menu, the EQ Wizard handoff and its return, and saving or loading a bank in the
/// hardware formats.</summary>
public partial class VirtualCrossoverPanel
{
    private readonly EqWizardImportExportCoordinator peqExport = new();

    private readonly VirtualCrossoverEqHandoff eqHandoff;

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

        ShowMenu(ControlFor(channel).PeqMenuButton, menu);
    }

    // The gate mirrors the magnitude view: shared template, active pin, last redraw's anchor.
    private void RequestPeqHandoff(VirtualCrossoverChannel channel, bool withChain)
    {
        if (EditPeqInWizardRequested is not { } requested)
        {
            return;
        }

        VirtualDspEqHandoffRequest? request = eqHandoff.Request(
            channel,
            withChain,
            HybridRequested,
            eqHandoff.SpatialAverage(channel, channel.ActiveRight, HybridRequested));
        if (request != null)
        {
            requested(request);
        }
    }

    /// <summary>False, writing nothing, when the channel is gone (removed or replaced by an import).</summary>
    internal bool TryApplyPeqFromWizard(
        VirtualDspEqReturnToken token,
        EqualizationCurve curve,
        double targetLevelDb)
    {
        // Same decision the handoff recorded, so an in-flight redraw cannot turn a valid return into a refusal.
        if (!eqHandoff.TryReturn(
                token, curve, eqHandoff.HybridCapture(token.Channel, token.RightSide, HybridRequested)))
        {
            return false;
        }

        // The guard above proved the level is still the wizard's starting point, so writing it overwrites nothing.
        SetTargetLevel(targetLevelDb);
        UpdatePeqReadouts(token.Channel);
        SaveAndRedraw();
        return true;
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
