using Resonalyze.Dsp;

namespace Resonalyze;

internal sealed partial class VirtualCrossoverAutoSetupDialog
{
    private sealed record JunctionRow(
        AutoSetupWizardJunction Junction,
        Label NameLabel,
        ThemedNumericUpDown MinHz,
        Label RangeDash,
        ThemedNumericUpDown MaxHz,
        ThemedComboBox MinSlope,
        Label SlopeDash,
        ThemedComboBox MaxSlope,
        CheckBox Split,
        Label Verdict,
        Label Notes)
    {
        public bool Suppressed { get; set; }
    }

    // One row per adjacent pair of every group, rebuilt whenever the chain order changes.
    private void RebuildJunctions()
    {
        foreach (JunctionRow junction in junctions)
        {
            foreach (Control control in JunctionControls(junction))
            {
                control.Dispose();
            }
        }

        junctions.Clear();
        foreach (AutoSetupWizardJunction junction in session.Junctions())
        {
            junctions.Add(BuildJunctionRow(junction));
        }

        PopulateJunctionTable();
        RefreshJunctionWindows();
    }

    private static IEnumerable<Control> JunctionControls(JunctionRow junction) =>
    [
        junction.NameLabel, junction.MinHz, junction.RangeDash, junction.MaxHz,
        junction.MinSlope, junction.SlopeDash, junction.MaxSlope, junction.Split,
        junction.Verdict, junction.Notes
    ];

    private JunctionRow BuildJunctionRow(AutoSetupWizardJunction junction)
    {
        ThemedNumericUpDown Frequency() => new()
        {
            Anchor = AnchorStyles.Left,
            BackColor = UiPalette.InputSurface,
            DecimalPlaces = 0,
            ForeColor = UiPalette.TextPrimary,
            Increment = 10m,
            LogarithmicFrequencyStep = true,
            Margin = new Padding(0, 1, 2, 1),
            Maximum = AutoSetupWizardPlan.FieldMaximumHz,
            Minimum = AutoSetupWizardPlan.FieldMinimumHz,
            MinimumSize = new Size(36, 19)
        };

        ThemedComboBox Slope()
        {
            var box = new ThemedComboBox
            {
                Anchor = AnchorStyles.Left,
                BackColor = UiPalette.ControlSurface,
                ForeColor = UiPalette.TextPrimary,
                Margin = new Padding(0, 1, 2, 1)
            };
            box.Items.AddRange(AutoSetupWizardPlan.SelectableSlopes.Cast<object>().ToArray());
            return box;
        }

        Label Dash() => new()
        {
            Anchor = AnchorStyles.Left,
            AutoSize = true,
            ForeColor = UiPalette.TextSecondary,
            Margin = new Padding(0, 4, 2, 4),
            Text = "–"
        };

        var row = new JunctionRow(
            junction,
            new Label
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                ForeColor = UiPalette.TextDefault,
                Margin = new Padding(0, 4, 16, 4),
                Text = AutoSetupWizardReport.JunctionName(junction)
            },
            Frequency(),
            Dash(),
            Frequency(),
            Slope(),
            Dash(),
            Slope(),
            new ReleaseClickCheckBox
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                ForeColor = UiPalette.TextPrimary,
                Margin = new Padding(12, 2, 0, 2),
                Text = "Split"
            },
            new Label
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                ForeColor = UiPalette.TextSecondary,
                Margin = new Padding(0, 0, 0, 2),
                Text = "—"
            },
            new Label
            {
                Anchor = AnchorStyles.Left,
                AutoSize = true,
                ForeColor = UiPalette.AccentMark,
                Margin = new Padding(0, 0, 0, 6),
                Visible = false
            });

        // Written as sentences: the tooltip wraps its own text at 64 characters, so a hand-broken line longer
        // than that gets broken a second time and comes out ragged.
        toolTip.SetToolTip(
            row.MinHz,
            "Lowest crossover the search may pick here. A value the drivers " +
            "cannot take is raised, and the row says why.");
        toolTip.SetToolTip(
            row.MaxHz,
            "Highest crossover the search may pick here. A value the drivers " +
            "cannot take is lowered, and the row says why.");
        toolTip.SetToolTip(
            row.MinSlope,
            "Gentlest slope the search may use here. 24 dB/oct always stays " +
            "inside the window.");
        toolTip.SetToolTip(
            row.MaxSlope,
            "Steepest slope the search may use here. The group-delay budget can " +
            "still rule out a slope this allows.");
        toolTip.SetToolTip(
            row.Split,
            "Lets the low-pass and the high-pass sit at different frequencies. " +
            "Off unless you ask for it.");

        // Before the handlers: showing what the user set is not an edit.
        AutoSetupJunctionEdits kept = session.EditsOf(junction);
        if (kept.MinHz is { } minHz)
        {
            row.MinHz.Value = Math.Clamp(minHz, row.MinHz.Minimum, row.MinHz.Maximum);
        }

        if (kept.MaxHz is { } maxHz)
        {
            row.MaxHz.Value = Math.Clamp(maxHz, row.MaxHz.Minimum, row.MaxHz.Maximum);
        }

        if (kept.MinSlope is { } minSlope)
        {
            row.MinSlope.SelectedItem = minSlope;
        }

        if (kept.MaxSlope is { } maxSlope)
        {
            row.MaxSlope.SelectedItem = maxSlope;
        }

        row.Split.Checked = kept.Split;

        row.MinHz.ValueChanged += (_, _) => JunctionEdited(row, edits => edits with { MinHz = row.MinHz.Value });
        row.MaxHz.ValueChanged += (_, _) => JunctionEdited(row, edits => edits with { MaxHz = row.MaxHz.Value });
        row.MinSlope.SelectedIndexChanged +=
            (_, _) => JunctionEdited(row, edits => edits with { MinSlope = (int)row.MinSlope.SelectedItem! });
        row.MaxSlope.SelectedIndexChanged +=
            (_, _) => JunctionEdited(row, edits => edits with { MaxSlope = (int)row.MaxSlope.SelectedItem! });
        row.Split.CheckedChanged += (_, _) => JunctionEdited(row, edits => edits with { Split = row.Split.Checked });
        return row;
    }

    // Writing the wizard's own answer back into a field must not read as the user setting it.
    private void JunctionEdited(JunctionRow row, Func<AutoSetupJunctionEdits, AutoSetupJunctionEdits> edit)
    {
        if (row.Suppressed)
        {
            return;
        }

        session.Edit(row.Junction, edit(session.EditsOf(row.Junction)));
        SchedulePreview();
    }

    private void PopulateJunctionTable()
    {
        tableJunctions.SuspendLayout();
        tableJunctions.Controls.Clear();
        tableJunctions.RowStyles.Clear();
        int line = 0;
        foreach (JunctionRow junction in junctions)
        {
            tableJunctions.Controls.Add(junction.NameLabel, 0, line);
            tableJunctions.Controls.Add(junction.MinHz, 1, line);
            tableJunctions.Controls.Add(junction.RangeDash, 2, line);
            tableJunctions.Controls.Add(junction.MaxHz, 3, line);
            tableJunctions.Controls.Add(junction.MinSlope, 4, line);
            tableJunctions.Controls.Add(junction.SlopeDash, 5, line);
            tableJunctions.Controls.Add(junction.MaxSlope, 6, line);
            tableJunctions.Controls.Add(junction.Split, 7, line);
            tableJunctions.Controls.Add(junction.Verdict, 8, line);
            line++;
            tableJunctions.Controls.Add(junction.Notes, 0, line);
            tableJunctions.SetColumnSpan(junction.Notes, 9);
            line++;
        }

        tableJunctions.RowCount = Math.Max(1, line);
        UiStyle.SetTextEnabledLook(labelJunctions, junctions.Count > 0);
        tableJunctions.ResumeLayout();
    }

    /// <summary>Writes the window the search will run on back into every row, with the reason for any bound that
    /// moved. Cheap enough to run on the UI thread on every keystroke, and it must: a row showing a window the search
    /// is not using is worse than a row showing nothing. A field the user has touched keeps its own value.</summary>
    private void RefreshJunctionWindows()
    {
        if (!initialized)
        {
            return;
        }

        foreach ((AutoSetupWizardJunction junction, JunctionWindowResolution window)
                 in AutoSetupWizardPlan.ResolvedWindows(session))
        {
            JunctionRow row = junctions.First(candidate => candidate.Junction == junction);
            AutoSetupJunctionEdits edits = session.EditsOf(junction);
            row.Suppressed = true;
            try
            {
                if (edits.MinHz == null)
                {
                    row.MinHz.Value = AutoSetupWizardPlan.FieldHz(window.LowHz);
                }

                if (edits.MaxHz == null)
                {
                    row.MaxHz.Value = AutoSetupWizardPlan.FieldHz(window.HighHz);
                }

                if (edits.MinSlope == null)
                {
                    row.MinSlope.SelectedItem = AutoSetupWizardPlan.NearestSlope(window.MinSlopeDbPerOctave);
                }

                if (edits.MaxSlope == null)
                {
                    row.MaxSlope.SelectedItem = AutoSetupWizardPlan.NearestSlope(window.MaxSlopeDbPerOctave);
                }
            }
            finally
            {
                row.Suppressed = false;
            }

            // The fact goes beside the row; the reasoning goes in the tooltip, where it is read only by
            // someone who wants it.
            row.Notes.Text = string.Join(
                "   ·   ", window.Notes.Select(note => note.Summary));
            row.Notes.Visible = window.Notes.Count > 0;
            toolTip.SetToolTip(
                row.Notes,
                string.Join(
                    Environment.NewLine + Environment.NewLine,
                    window.Notes.Select(note => note.Detail)));
        }

        // A note appearing, or a rebuilt junction set, changes the table's height under the options below it.
        FitToContents(growOnly: false);
    }

    /// <summary>The one part of a row that needs the fit, so the one part that arrives late.</summary>
    private void UpdateJunctionVerdicts(IReadOnlyList<AutoSetupGroupFit> fits)
    {
        foreach ((AutoSetupWizardJunction junction, string verdict)
                 in AutoSetupWizardReport.JunctionVerdicts(session, fits))
        {
            junctions.First(row => row.Junction == junction).Verdict.Text = verdict;
        }
    }
}
