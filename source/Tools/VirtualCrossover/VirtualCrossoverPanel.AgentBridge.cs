using Resonalyze.Dsp;
using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>Panel side of the Agent Bridge: the menu, the clipboard, the review and every message; the import itself runs in
/// <see cref="AgentImportRunner"/>, which reaches the controls through <see cref="IAgentImportHost"/>. See
/// docs/tech/agent-bridge.md.</summary>
public partial class VirtualCrossoverPanel : IAgentImportHost
{
    // One bridge operation at a time: a concurrent Copy or import would race the coordinator or move the settings being read.
    private bool agentBusy;

    private ContextMenuStrip? agentMenu;

    /// <summary>EQ Wizard Auto Tune settings an import fits a bank with; the wizard's opening values when unwired.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Func<EqAutoTunePolicy>? AutoTunePolicyProvider { get; set; }

    // The view the bridge's readings follow, off the controls.
    private AgentViewInputs AgentView() => new(
        SelectedGroupView,
        checkBoxHybrid.Checked,
        HybridRequested,
        session.Project.TargetLevelDb,
        targetCurve);

    /// <summary>The session as one hash for the review's staleness check. See docs/tech/agent-bridge.md#session-fingerprint.</summary>
    internal string ComputeAgentFingerprint() => agentImport.Fingerprint();

    /// <summary>The channels as the bridge names them, with live settings, plus the project figures engine requests are judged against.</summary>
    internal AgentSessionSnapshot BuildAgentSessionSnapshot() => agentImport.Snapshot();

    // Same two-state toggle as the Target menu; rebuilt per click so enabled states are current.
    private void ShowAgentMenu()
    {
        if (agentMenu is { Visible: true })
        {
            agentMenu.Close();
            return;
        }

        agentMenu?.Dispose();
        agentMenu = new ContextMenuStrip();
        agentMenu.Items.Add(new ToolStripMenuItem(
            "Copy for AI",
            null,
            async (_, _) => await CopyForAiAsync())
        {
            ToolTipText = ToolTipTextWrapper.Wrap(
                "Copies the current Virtual DSP settings, your notes and a diagnostic " +
                "summary to the clipboard, ready to paste into a chat assistant. " +
                "Nothing is sent anywhere: you paste it yourself.")
        });
        var diagnostics = new ToolStripMenuItem("Copy diagnostics for AI")
        {
            ToolTipText = ToolTipTextWrapper.Wrap(
                "Smaller texts the assistant may ask for by name, beside the package: " +
                "copy the one it named and paste it into the same chat.")
        };
        diagnostics.DropDownItems.Add(new ToolStripMenuItem(
            "Excess group delay",
            null,
            async (_, _) => await CopyExcessGroupDelayForAiAsync())
        {
            ToolTipText = ToolTipTextWrapper.Wrap(
                "Each measured channel's excess group delay, read through the project's " +
                "phase gate and window. Under FDW a windowed reading; excess that lives " +
                "only at a band edge can be the gate truncating a steep filter's ringing.")
        });
        agentMenu.Items.Add(diagnostics);
        agentMenu.Items.Add(new ToolStripMenuItem(
            "Import AI proposal…",
            null,
            (_, _) => ImportAiProposal())
        {
            ToolTipText = ToolTipTextWrapper.Wrap(
                "Reads the assistant's reply from the clipboard (copy the whole reply), " +
                "shows every proposed change against the current value, and applies " +
                "only the rows you tick.")
        });
        agentMenu.Items.Add(new ToolStripSeparator());
        agentMenu.Items.Add(new ToolStripMenuItem(
            "Undo AI import",
            null,
            (_, _) => UndoAiImport())
        {
            Enabled = agentImport.Undo != null,
            ToolTipText = ToolTipTextWrapper.Wrap(
                "Puts the channels back exactly as they were before the last import. " +
                "One step; gone once a session is loaded.")
        });
        ShowMenu(buttonAi, agentMenu);
    }

    private readonly AgentImportRunner agentImport;

    /// <summary>Import AI proposal. See docs/tech/agent-bridge.md#import-flow.</summary>
    private async void ImportAiProposal()
    {
        if (agentBusy)
        {
            return;
        }

        agentBusy = true;
        RefreshAutoActionsEnabled();
        var summary = new List<string>();
        try
        {
            if (!AgentClipboard.TryRead(out string? text, out string? error))
            {
                ShowError("The AI proposal was not imported.", error!);
                return;
            }

            AgentProposalParseResult parsed = AgentProposalParser.Parse(text);
            if (!parsed.Succeeded)
            {
                ShowError("The AI proposal was not imported.", parsed.Error!);
                return;
            }

            AgentProposal proposal = parsed.Proposal!;
            AgentSessionSnapshot reviewedSession = BuildAgentSessionSnapshot();
            AgentProposalReview review =
                AgentProposalValidator.Review(proposal, reviewedSession);
            HashSet<string> selected;
            using (var dialog = new AgentProposalDialog(review))
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                {
                    return;
                }

                selected = dialog.Selected.Select(verdict => verdict.Id).ToHashSet(StringComparer.Ordinal);
            }

            string? problem = AgentProposalApplier.Prepare(
                proposal, selected, reviewedSession.Fingerprint, BuildAgentSessionSnapshot(),
                out List<AgentOperationVerdict> toApply, out List<string> unseenWarnings);
            if (problem != null)
            {
                ShowError("Nothing was applied.", problem);
                return;
            }
            // The ticked subset can leave a state the review never showed: warn, do not refuse.
            if (unseenWarnings.Count > 0 &&
                ShowMessage("With only the ticked rows applied:" + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, unseenWarnings) + Environment.NewLine + Environment.NewLine +
                    "Apply anyway?",
                    "Import AI proposal",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            bool ran = await AgentProgressDialog.RunAsync(
                FindForm(),
                "Import AI proposal",
                "Reading the reply…",
                async progress =>
                {
                    // Probes run before any write: they answer about the tune as it stands.
                    await agentImport.ProbesAsync(toApply, summary, progress);

                    return await agentImport.CommitAsync(
                        proposal, selected, reviewedSession.Fingerprint,
                        review.Verdicts.Count, summary, progress);
                });

            SaveAndRedraw();
            ShowMessage(string.Join(Environment.NewLine, summary) + Environment.NewLine +
                Environment.NewLine + "Undo AI import is in the same menu.",
                "Import AI proposal",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Summary entries did happen; do not claim 'not imported' over them.
            ShowError(
                summary.Count == 0
                    ? "The AI proposal was not imported."
                    : "The AI proposal was imported only in part.",
                summary.Count == 0
                    ? exception.Message
                    : string.Join(Environment.NewLine, summary) + Environment.NewLine +
                        Environment.NewLine + "Then it stopped: " + exception.Message +
                        Environment.NewLine + Environment.NewLine +
                        "Undo AI import puts the whole import back.");
        }
        finally
        {
            agentBusy = false;
            RefreshAutoActionsEnabled();
        }
    }

    private void UndoAiImport()
    {
        if (agentImport.Undo == null || agentBusy)
        {
            return;
        }

        (AgentImportUndo? undo, string? refusal) = agentImport.TakeUndo();
        if (refusal != null)
        {
            ShowError("Nothing to undo.", refusal);
            return;
        }

        RestoreChannels(undo!);
    }

    // Excess group delay per measured channel, as a separate text beside the package (which already fills a chat), named after the last package so the two line up by channel id.
    private async Task CopyExcessGroupDelayForAiAsync()
    {
        if (agentBusy)
        {
            return;
        }

        agentBusy = true;
        RefreshAutoActionsEnabled();
        try
        {
            // Snapshot on the UI thread; the gated FFT and minimum-phase reconstruction per channel run off it.
            List<MeasuredChannel> measured = agentReader.MeasuredChannels();
            if (measured.Count == 0)
            {
                ShowError("The diagnostic was not copied.", "No channel has a measurement to read.");
                return;
            }

            PhaseAnalysisSettings gate = agentReader.GroupDelayWindow();
            // Tie to the package only while this is the session it was copied from.
            string? packageId = agentReader.PackageIdFor(ComputeAgentFingerprint());
            DateTimeOffset now = DateTimeOffset.UtcNow;
            (AgentDiagnosticBuildResult result, int count) = await AgentProgressDialog.RunAsync(
                FindForm(),
                "Copy diagnostics for AI",
                "Excess group delay…",
                progress => Task.Run(() =>
                {
                    var channels = new List<AgentDiagnosticChannel>(measured.Count);
                    foreach (MeasuredChannel channel in measured)
                    {
                        progress.Report($"Excess group delay: {channel.Id}…");
                        if (AgentSessionReader.ExcessGroupDelayCurve(channel, gate) is { } curve)
                        {
                            channels.Add(new AgentDiagnosticChannel(channel.Id, curve));
                        }
                    }

                    return (
                        AgentDiagnosticBuilder.BuildExcessGroupDelay(
                            channels, packageId, now, AgentDiagnosticWindow.From(gate)),
                        channels.Count);
                }));
            if (IsDisposed)
            {
                return;
            }
            if (!AgentClipboard.TryWrite(result.Text, out string? error))
            {
                ShowError("The diagnostic was not copied.", error!);
                return;
            }

            ShowMessage($"Excess group delay diagnostic copied ({(result.JsonBytes + 1023) / 1024} KB, " +
                $"{count} channel{(count == 1 ? "" : "s")}). Paste it into the " +
                "same chat as the package.",
                "Copy diagnostics for AI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("The diagnostic was not copied.", exception.Message);
        }
        finally
        {
            agentBusy = false;
            RefreshAutoActionsEnabled();
        }
    }

    /// <summary>Copy for AI: gathers at one revision (retrying once), builds off the UI thread, then writes the whole text to the clipboard at once, so a failure copies nothing.</summary>
    private async Task CopyForAiAsync()
    {
        if (agentBusy)
        {
            return;
        }

        agentBusy = true;
        RefreshAutoActionsEnabled();
        try
        {
            (AgentPackageInputs Inputs, string Fingerprint)? gathered =
                await AgentProgressDialog.RunAsync(
                    FindForm(),
                    "Copy for AI",
                    "Reading the tune…",
                    async progress =>
                    {
                        (AgentPackageInputs, string)? first = await GatherAgentPackageAsync();
                        if (first != null)
                        {
                            return first;
                        }

                        progress.Report("The tune moved while it was read — reading again…");
                        return await GatherAgentPackageAsync();
                    });
            if (IsDisposed)
            {
                return;
            }
            if (gathered is not { } package)
            {
                ShowError(
                    "The AI package was not copied.",
                    "The settings changed while the package was being gathered. Try again.");
                return;
            }

            (AgentPackageInputs inputs, string fingerprint) = package;
            Guid packageId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            AgentPackageBuildResult result = await Task.Run(
                () => AgentPackageBuilder.Build(inputs, packageId, now));
            if (IsDisposed)
            {
                return;
            }
            if (!result.Succeeded)
            {
                ShowError("The AI package was not copied.", result.Error!);
                return;
            }
            if (!AgentClipboard.TryWrite(result.Text!, out string? error))
            {
                ShowError("The AI package was not copied.", error!);
                return;
            }

            agentReader.RememberPackage(packageId.ToString("D"), fingerprint);
            string omitted = result.Omitted.Count > 0
                ? Environment.NewLine + "Left out to fit the size limit: " +
                    string.Join(", ", result.Omitted) + "."
                : string.Empty;
            ShowMessage($"AI package copied ({(result.JsonBytes + 1023) / 1024} KB). " +
                "Paste it into a chat assistant." + omitted,
                "Copy for AI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowError("The AI package was not copied.", exception.Message);
        }
        finally
        {
            agentBusy = false;
            RefreshAutoActionsEnabled();
        }
    }

    // The fingerprint covers what the coordinator revision does not (target level, Hybrid tick, gate pin), so it is taken on both sides of the gather; null when they differ, and the caller retries once.
    private async Task<(AgentPackageInputs Inputs, string Fingerprint)?> GatherAgentPackageAsync()
    {
        string before = ComputeAgentFingerprint();
        AgentPackageInputs? inputs = await agentReader.CaptureInputsAsync(AgentView());
        if (inputs == null || IsDisposed || ComputeAgentFingerprint() != before)
        {
            return null;
        }

        return (inputs, before);
    }
}
