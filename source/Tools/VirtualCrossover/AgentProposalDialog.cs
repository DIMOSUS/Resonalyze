using Resonalyze.Integration.AgentBridge;

namespace Resonalyze;

/// <summary>Review of a reply; changes nothing, only answers which rows were ticked. A warning is a word in Status, never only a colour.</summary>
internal sealed partial class AgentProposalDialog : Form
{
    private static readonly Color RejectedText = Color.FromArgb(140, 146, 158);

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
            // An unexplained row is marked so the blank does not read as "no reason"; a parser-refused row stays blank (its message explains).
            string reasonText = verdict.Reason ?? NoReasonText;
            int index = gridView.Rows.Add(
                verdict.Applicable && verdict.Ticked,
                verdict.ChannelLabel,
                verdict.Parameter,
                verdict.Current,
                verdict.Proposed,
                StatusWord(verdict.Status),
                reasonText);
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
            row.Cells[ColumnReason.Index].ToolTipText = reasonText;
            // Engine inputs overflow these fixed-width cells: the detail box repeats them, the tooltip serves reading down the table.
            row.Cells[ColumnCurrent.Index].ToolTipText = verdict.Current;
            row.Cells[ColumnProposed.Index].ToolTipText = verdict.Proposed;
        }

        // Commit the tick now so CellValueChanged fires before the row loses focus.
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
