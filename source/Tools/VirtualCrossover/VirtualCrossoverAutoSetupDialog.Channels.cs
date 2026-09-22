using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed partial class VirtualCrossoverAutoSetupDialog
{
    private sealed record ChannelRow(
        AutoSetupWizardRow Row,
        Label PositionLabel,
        Label NameLabel,
        Label BandLabel,
        ThemedComboBox TypeComboBox,
        Button Up,
        Button Down);

    private ChannelRow BuildRow(AutoSetupWizardRow source)
    {
        AutoSetupWizardChannel channel = source.Source;
        var positionLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            ForeColor = UiPalette.TextDisabled,
            Margin = new Padding(0, 4, 8, 4)
        };
        var nameLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204),
            ForeColor = channel.Accent,
            Margin = new Padding(0, 4, 24, 4),
            Text = channel.Name
        };
        var bandLabel = new Label
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            ForeColor = UiPalette.TextSecondary,
            Margin = new Padding(0, 4, 24, 4),
            Text = AutoSetupWizardReport.BandText(channel)
        };
        toolTip.SetToolTip(bandLabel, AutoSetupWizardReport.BandTooltip(channel));
        var typeComboBox = new ThemedComboBox
        {
            Anchor = AnchorStyles.Left,
            BackColor = UiPalette.ControlSurface,
            ForeColor = UiPalette.TextPrimary,
            Margin = new Padding(0, 1, 0, 1),
            TabIndex = source.InitIndex
        };
        typeComboBox.Items.AddRange(
        [
            DriverType.Subwoofer,
            DriverType.Woofer,
            DriverType.Midbass,
            DriverType.Midrange,
            DriverType.Tweeter
        ]);
        typeComboBox.SelectedItem = source.Type;
        // A type change moves the class bounds, so the junction rows are re-resolved, not just re-scored.
        typeComboBox.SelectedIndexChanged += (_, _) =>
        {
            source.Type = typeComboBox.SelectedItem is DriverType type ? type : DriverType.Woofer;
            SchedulePreview();
        };

        var row = new ChannelRow(
            source, positionLabel, nameLabel, bandLabel, typeComboBox,
            BuildArrow("▲"), BuildArrow("▼"));
        row.Up.Click += (_, _) => MoveInChain(row, -1);
        row.Down.Click += (_, _) => MoveInChain(row, +1);
        foreach (Button arrow in new[] { row.Up, row.Down })
        {
            toolTip.SetToolTip(
                arrow,
                "Where this driver sits in its group's chain: the one above hands\r\n" +
                "over to the one below. The order starts from what each channel\r\n" +
                "measured (narrowed by any crossover corner it already carries) —\r\n" +
                "move it when two drivers are too alike for that to decide, as a\r\n" +
                "pair of subwoofers measured full-range will be.");
        }

        return row;
    }

    private Button BuildArrow(string glyph) =>
        new ReleaseClickButton
        {
            Anchor = AnchorStyles.Left,
            BackColor = UiPalette.InputSurface,
            FlatStyle = FlatStyle.Popup,
            ForeColor = UiPalette.TextPrimary,
            Margin = new Padding(2, 1, 0, 1),
            Text = glyph,
            UseVisualStyleBackColor = false
        };

    private List<ChannelRow> MembersOf(VirtualCrossoverAlignmentStage group) =>
        session.MembersOf(group).Select(row => rows[row]).ToList();

    // Re-run after a reorder with the same controls, so device-unit sizing from LayoutBelowChannelTable survives.
    private void PopulateTable()
    {
        bool headers = session.GroupsInOrder().Count() > 1;
        tableChannels.SuspendLayout();
        tableChannels.Controls.Clear();
        tableChannels.RowStyles.Clear();
        int line = 0;
        foreach (VirtualCrossoverAlignmentStage group in session.GroupsInOrder())
        {
            List<ChannelRow> members = MembersOf(group);
            if (headers)
            {
                Label header = GroupHeader(group);
                header.Text = members.Count > 1
                    ? VirtualCrossoverAlignmentStages.DisplayName(group) +
                        "   —   lowest first, handing over downwards"
                    : VirtualCrossoverAlignmentStages.DisplayName(group);
                tableChannels.Controls.Add(header, 0, line);
                tableChannels.SetColumnSpan(header, 6);
                line++;
            }

            for (int i = 0; i < members.Count; i++)
            {
                ChannelRow member = members[i];
                // A group of one is not a chain: no number, no arrows.
                member.PositionLabel.Text = members.Count > 1 ? $"{i + 1}." : string.Empty;
                tableChannels.Controls.Add(member.PositionLabel, 0, line);
                tableChannels.Controls.Add(member.NameLabel, 1, line);
                tableChannels.Controls.Add(member.BandLabel, 2, line);
                tableChannels.Controls.Add(member.TypeComboBox, 3, line);
                if (members.Count > 1)
                {
                    member.Up.Enabled = i > 0;
                    member.Down.Enabled = i < members.Count - 1;
                    tableChannels.Controls.Add(member.Up, 4, line);
                    tableChannels.Controls.Add(member.Down, 5, line);
                }

                line++;
            }
        }

        tableChannels.RowCount = Math.Max(1, line);
        tableChannels.ResumeLayout(true);
    }

    private Label GroupHeader(VirtualCrossoverAlignmentStage group)
    {
        if (!groupHeaders.TryGetValue(group, out Label? header))
        {
            header = new Label
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                Font = new Font(
                    "Segoe UI Semibold", 9F, FontStyle.Regular, GraphicsUnit.Point, 204),
                ForeColor = UiPalette.TextDefault,
                Margin = new Padding(0, 8, 0, 2)
            };
            groupHeaders[group] = header;
        }

        return header;
    }

    private void MoveInChain(ChannelRow row, int delta)
    {
        if (!session.MoveInChain(row.Row, delta))
        {
            return;
        }

        PopulateTable();
        RebuildJunctions();
        SchedulePreview();
    }

    // Amber: order undetermined; red: chain runs backwards.
    private void MarkChainOrder()
    {
        Dictionary<AutoSetupWizardRow, VirtualCrossoverChainOrder> marks = AutoSetupWizardChainOrder.Marks(session);
        foreach (AutoSetupWizardRow row in session.Rows)
        {
            rows[row].BandLabel.ForeColor = marks.TryGetValue(row, out VirtualCrossoverChainOrder verdict)
                ? verdict == VirtualCrossoverChainOrder.Reversed ? UiPalette.Danger : UiPalette.Warning
                : UiPalette.TextSecondary;
        }
    }
}
