using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>
/// The review of an assistant's reply: every proposed change against the value
/// the channel holds now, with a tick per admissible row. Nothing here changes
/// the session — the dialog answers which rows were ticked, and the panel
/// applies them after a second look at the live settings. A rejected row is
/// listed with its reason and cannot be ticked; a warning is a word in the Status
/// column, never only a colour.
/// </summary>
internal sealed partial class AgentProposalDialog : Form
{
    private static readonly Color RejectedText = Color.FromArgb(140, 146, 158);

    /// <summary>
    /// What stands where the reply's own prose would have been. The fields are
    /// wanted and not required — a reply that leaves them out is still read —
    /// so the review has to say the words are missing rather than show a blank
    /// that reads like there was nothing to say.
    /// </summary>
    private const string NoSummaryText = "(the reply gave no summary)";
    private const string NoReasonText = "(no reason given)";
    private static readonly Color WarningText = Color.FromArgb(230, 184, 0);

    private readonly AgentProposalReview review;

    public AgentProposalDialog(AgentProposalReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        InitializeComponent();
        this.review = review;

        StyleGrid();
        labelSummary.Text = review.Proposal.Summary ?? NoSummaryText;
        labelWarnings.Text = review.Warnings.Count > 0
            ? string.Join(Environment.NewLine, review.Warnings)
            : string.Empty;

        foreach (AgentOperationVerdict verdict in review.Verdicts)
        {
            int index = gridView.Rows.Add(
                verdict.Applicable && verdict.Ticked,
                verdict.ChannelLabel,
                verdict.Parameter,
                verdict.Current,
                verdict.Proposed,
                StatusWord(verdict.Status),
                // A row the assistant explained shows its sentence; one it did
                // not is marked, so the blank cannot be read as "no reason to
                // give". A row the parser refused carries an empty reason and
                // is left blank: its message IS the explanation.
                verdict.Reason ?? NoReasonText);
            DataGridViewRow row = gridView.Rows[index];
            row.Tag = verdict;
            if (!verdict.Applicable)
            {
                row.Cells[ColumnApply.Index].ReadOnly = true;
                row.DefaultCellStyle.ForeColor = RejectedText;
                row.DefaultCellStyle.SelectionForeColor = RejectedText;
            }
            else if (verdict.Status == AgentVerdictStatus.Warning)
            {
                row.Cells[ColumnStatus.Index].Style.ForeColor = WarningText;
                row.Cells[ColumnStatus.Index].Style.SelectionForeColor = WarningText;
            }
            row.Cells[ColumnStatus.Index].ToolTipText = verdict.Message;
            row.Cells[ColumnReason.Index].ToolTipText = verdict.Reason;
            // These two columns are fixed-width and an engine request states its
            // whole set of inputs in them, well past what the cell can show. The
            // detail box below repeats them for the selected row; the tooltip is
            // for reading down the table without moving the selection.
            row.Cells[ColumnCurrent.Index].ToolTipText = verdict.Current;
            row.Cells[ColumnProposed.Index].ToolTipText = verdict.Proposed;
        }

        // A click on the box is a click on the box: commit it so CellValueChanged
        // fires now rather than when the row loses focus.
        gridView.CellContentClick += (_, args) =>
        {
            if (args.RowIndex >= 0 && args.ColumnIndex == ColumnApply.Index)
            {
                gridView.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        gridView.CellValueChanged += (_, _) => UpdateApplyEnabled();
        gridView.SelectionChanged += (_, _) => ShowDetail();
        gridView.ClearSelection();
        if (gridView.Rows.Count > 0)
        {
            gridView.Rows[0].Selected = true;
        }

        ShowDetail();
        UpdateApplyEnabled();
    }

    /// <summary>The applicable rows the user left ticked, in the order shown.</summary>
    public IReadOnlyList<AgentOperationVerdict> Selected =>
        gridView.Rows
            .Cast<DataGridViewRow>()
            .Where(row => row.Tag is AgentOperationVerdict { Applicable: true } &&
                row.Cells[ColumnApply.Index].Value is true)
            .Select(row => (AgentOperationVerdict)row.Tag!)
            .ToList();

    private void StyleGrid()
    {
        gridView.EnableHeadersVisualStyles = false;
        gridView.GridColor = UiPalette.DialogBorder;
        gridView.DefaultCellStyle.BackColor = UiPalette.DialogBackground;
        gridView.DefaultCellStyle.ForeColor = UiPalette.TextPrimary;
        gridView.DefaultCellStyle.SelectionBackColor = UiPalette.ButtonPressedBackground;
        gridView.DefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
        gridView.ColumnHeadersDefaultCellStyle.BackColor = UiPalette.ControlSurface;
        gridView.ColumnHeadersDefaultCellStyle.ForeColor = UiPalette.TextPrimary;
        gridView.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiPalette.ControlSurface;
        gridView.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
    }

    private static string StatusWord(AgentVerdictStatus status) => status switch
    {
        AgentVerdictStatus.Valid => "OK",
        AgentVerdictStatus.Warning => "Warning",
        _ => "Rejected"
    };

    private void UpdateApplyEnabled() => buttonApply.Enabled = Selected.Count > 0;

    // The box under the table: the selected row's full status and reason (the
    // cells clip long text), and the assistant's advice — the changes it did not
    // put into an operation, which the reader acts on by hand.
    private void ShowDetail()
    {
        var lines = new List<string>();
        if (gridView.SelectedRows.Count > 0 &&
            gridView.SelectedRows[0].Tag is AgentOperationVerdict verdict)
        {
            lines.Add($"{verdict.Id} — {verdict.ChannelLabel} {verdict.Parameter}: " +
                $"{StatusWord(verdict.Status)}. {verdict.Message}");
            if (verdict.Current.Length > 0)
            {
                lines.Add("Current: " + verdict.Current);
            }
            if (verdict.Proposed.Length > 0)
            {
                lines.Add("Proposed: " + verdict.Proposed);
            }
            if (verdict.Reason is { Length: > 0 })
            {
                lines.Add("Reason: " + verdict.Reason);
            }
            else if (verdict.Reason == null)
            {
                lines.Add("Reason: " + NoReasonText);
            }
        }

        if (review.Proposal.Advice.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Advice (not applied automatically):");
            lines.AddRange(review.Proposal.Advice.Select(line => "• " + line));
        }
        if (review.Proposal.Sources.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Sources cited (shown as text, never opened):");
            lines.AddRange(review.Proposal.Sources.Select(source =>
                "• " + (string.IsNullOrWhiteSpace(source.Title) ? source.Url : $"{source.Title}: {source.Url}")));
        }

        textBoxDetail.Text = string.Join(Environment.NewLine, lines);
    }
}
