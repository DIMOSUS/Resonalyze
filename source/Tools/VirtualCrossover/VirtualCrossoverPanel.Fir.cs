using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>A block's FIR kernel: the menu, import and export, and the FIR Constructor handoff and its return.</summary>
public partial class VirtualCrossoverPanel
{
    // Rebuilt per click like the PEQ menu. The kernel lives in the session; constructor and files are its ways in and out.
    private void ShowFirMenu(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(
            settings.HasFir ? "Open in FIR Constructor…" : "Design in FIR Constructor…",
            null,
            (_, _) => RequestFirHandoff(channel))
        {
            Enabled = EditFirInConstructorRequested != null,
            ToolTipText =
                "Design a linear-phase low-pass, high-pass or band-pass kernel for this\r\n" +
                "side and return it here. A kernel imported from a file opens as it is;\r\n" +
                "any change in the constructor replaces it with a designed one."
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(
            settings.HasFir ? "Import FIR filter (replace)…" : "Import FIR filter…",
            null,
            (_, _) => ImportFir(channel));
        var exportItem = new ToolStripMenuItem("Export FIR filter…", null, (_, _) => ExportFir(channel))
        {
            Enabled = settings.HasFir,
            ToolTipText =
                "Write this channel's kernel out as a 32-bit float WAV or a text file\r\n" +
                "(one coefficient per line), at the processor's rate — the kernel is\r\n" +
                "kept in the session, so this is where it leaves for the hardware."
        };
        menu.Items.Add(exportItem);
        menu.Items.Add(new ToolStripSeparator());
        var clearItem = new ToolStripMenuItem("Clear", null, (_, _) => ClearFir(channel))
        {
            Enabled = settings.HasFir
        };
        menu.Items.Add(clearItem);
        DropDownMenu.ShowUnder(ControlFor(channel).FirButton, menu);
    }

    private void ImportFir(VirtualCrossoverChannel channel)
    {
        using var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = FirFilterFiles.ImportFileDialogFilter,
            Title = $"Import channel {channel.Name} FIR filter"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        FirFilter kernel;
        try
        {
            // Not a kernel: the assignment below would silently drop the existing one.
            kernel = FirFilterFiles.Load(dialog.FileName);
        }
        catch (Exception exception)
        {
            ShowError("FIR filter could not be imported.", exception.Message);
            return;
        }

        VirtualCrossoverChannelSettings settings = channel.Settings;
        settings.Fir = kernel;
        settings.FirSourceName = Path.GetFileName(dialog.FileName);
        // A file is taps only, not a designed crossover.
        settings.FirDesign = null;
        UpdateFirReadout(channel);
        SaveAndRedraw();
    }

    private void ExportFir(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        if (settings.Fir is not { } kernel)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "wav",
            Filter = FirFilterFiles.ExportFileDialogFilter,
            FileName = settings.FirDesign is { } design
                ? $"{channel.Name} FIR {FirCrossoverDescription.Short(design)}"
                : Path.GetFileNameWithoutExtension(settings.FirSourceName) is { Length: > 0 } stem
                    ? stem
                    : $"{channel.Name} FIR",
            OverwritePrompt = true,
            Title = $"Export channel {channel.Name} FIR filter"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            // Taps mean the processor's rate in this session.
            FirFilterFiles.Save(
                dialog.FileName,
                kernel,
                session.ProcessorSampleRateHz,
                settings.FirSourceName,
                settings.FirDesign is { } designed ? FirCrossoverDescription.Long(designed) : null);
        }
        catch (Exception exception)
        {
            ShowError("FIR filter could not be exported.", exception.Message);
        }
    }

    private void ClearFir(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        settings.Fir = null;
        settings.FirSourceName = null;
        settings.FirDesign = null;
        UpdateFirReadout(channel);
        SaveAndRedraw();
    }

    private void UpdateFirReadout(VirtualCrossoverChannel channel)
    {
        VirtualCrossoverChannelSettings settings = channel.Settings;
        ControlFor(channel).SetFir(settings.Fir, settings.FirSourceName, settings.FirDesign);
    }

    private void RequestFirHandoff(VirtualCrossoverChannel channel)
    {
        if (EditFirInConstructorRequested is not { } requested)
        {
            return;
        }

        requested(FirConstructorHandoff.Build(
            channel, channel.ActiveRight, projectGeneration, session.ProcessorSampleRateHz));
    }

    /// <summary>False, writing nothing, when the side is no longer the one the session opened on.</summary>
    internal bool TryApplyFirFromConstructor(
        FirConstructorReturnToken token,
        FirFilter kernel,
        FirCrossoverDesign design)
    {
        if (!FirConstructorHandoff.TryApplyReturn(
                session.Channels,
                token,
                kernel,
                design,
                projectGeneration,
                session.ProcessorSampleRateHz,
                session.Project.ResolveDspFirFilters()))
        {
            return false;
        }

        UpdateFirReadout(token.Channel);
        SaveAndRedraw();
        return true;
    }
}
