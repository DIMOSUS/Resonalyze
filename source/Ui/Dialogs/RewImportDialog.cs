using Resonalyze.Integration.Rew;

namespace Resonalyze.Ui.Dialogs;

/// <summary>Picks a REW measurement and states REW's timing offset; closes with OK only once the chosen measurement has been
/// read and passed every check, so a refusal leaves the list open for another choice.</summary>
internal sealed partial class RewImportDialog : Form
{
    private const string NotAnswering =
        "Not answering. Start REW and enable its API server in Preferences -> API.";

    private readonly int configuredSampleRate;
    private readonly Func<Uri, CancellationToken, Task<RewMeasurementCatalog>> list;
    private readonly Func<Uri, RewMeasurementSummary, double?, double, CancellationToken, Task<RewImportPreparation>> prepare;
    private readonly CancellationTokenSource closing = new();

    /// <summary>What the user set for each measurement, by UUID, so no row inherits another row's offset.</summary>
    private readonly Dictionary<string, (decimal OffsetMs, bool Unknown)> offsetAnswers = new(StringComparer.Ordinal);
    private Uri? listedAddress;
    private string answeringStatus = string.Empty;
    private bool busy;
    private bool applyingOffset;

    /// <param name="prepare">Receives the offset in seconds (null for "I don't know") and the sweep level in dBFS.</param>
    public RewImportDialog(
        string baseUrl,
        int configuredSampleRate,
        Func<Uri, CancellationToken, Task<RewMeasurementCatalog>> list,
        Func<Uri, RewMeasurementSummary, double?, double, CancellationToken, Task<RewImportPreparation>> prepare)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(prepare);
        this.configuredSampleRate = configuredSampleRate;
        this.list = list;
        this.prepare = prepare;

        InitializeComponent();
        StyleGrid();
        textAddress.Text = baseUrl;
        buttonRefresh.Click += async (_, _) => await RefreshAsync();
        buttonImport.Click += async (_, _) => await ImportAsync();
        measurementGridView.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                await ImportAsync();
            }
        };
        // CurrentRow is what an import reads, and SelectionChanged can fire before it moves.
        measurementGridView.CurrentCellChanged += (_, _) => UpdateSelection();
        checkOffsetUnknown.CheckedChanged += (_, _) =>
        {
            RememberOffsetAnswer();
            UpdateControls();
        };
        numericOffset.ValueChanged += (_, _) => RememberOffsetAnswer();
        ShowLevelSource(null, answered: false);
        UpdateControls();
    }

    public RewPreparedImport? Import { get; private set; }

    /// <summary>The address whose list is shown; null until REW has answered.</summary>
    public string? AnsweringBaseUrl => listedAddress?.ToString();

    private RewMeasurementSummary? SelectedMeasurement =>
        measurementGridView.CurrentRow?.Tag as RewMeasurementSummary;

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await RefreshAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        closing.Cancel();
        base.OnFormClosed(e);
    }

    private async Task RefreshAsync()
    {
        if (busy)
        {
            return;
        }

        if (!RewApiClient.TryParseBaseAddress(textAddress.Text, out Uri? address))
        {
            ShowLevelSource(null, answered: false);
            ShowList(null, [], null);
            SetStatus(
                $"\"{textAddress.Text.Trim()}\" is not an http address. REW's API normally listens on {RewApiClient.DefaultBaseUrl}",
                warning: true);
            return;
        }

        string? keepUuid = SelectedMeasurement?.Uuid;
        SetBusy(true, "Asking REW...");
        RewMeasurementCatalog? catalog = null;
        string? failure = null;
        try
        {
            catalog = await list(address!, closing.Token);
        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested)
        {
            return;
        }
        catch (RewApiException exception)
        {
            failure = exception.Message;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            failure = $"REW stopped answering while the list was read. ({exception.Message})";
        }

        if (IsDisposed)
        {
            return;
        }

        busy = false;
        UseWaitCursor = false;
        if (catalog?.Version is not { } version)
        {
            ShowLevelSource(null, answered: false);
            ShowList(null, [], null);
            SetStatus(failure ?? NotAnswering, warning: true);
            return;
        }

        if (catalog.LevelDbfs is { } levelDbfs)
        {
            numericLevel.Value = Math.Round(
                Math.Clamp((decimal)levelDbfs, numericLevel.Minimum, numericLevel.Maximum),
                numericLevel.DecimalPlaces);
        }

        ShowLevelSource(catalog.Level, answered: true);

        ShowList(address, catalog.Measurements, keepUuid ?? catalog.SelectedUuid);
        SetStatus(
            catalog.Measurements.Count == 0
                ? $"Answering: REW {version}. It holds no measurements."
                : $"Answering: REW {version}",
            warning: false);
    }

    private async Task ImportAsync()
    {
        if (busy ||
            listedAddress is not { } address ||
            SelectedMeasurement is not { } measurement ||
            RewMeasurementImport.DescribeRateMismatch(measurement.SampleRate, configuredSampleRate) != null)
        {
            return;
        }

        double? offsetSeconds = checkOffsetUnknown.Checked
            ? null
            : (double)numericOffset.Value / 1000.0;
        labelProblem.Text = string.Empty;
        SetBusy(true, "Reading the impulse response from REW...");
        RewImportPreparation? preparation = null;
        string? failure = null;
        try
        {
            preparation = await prepare(address, measurement, offsetSeconds, (double)numericLevel.Value, closing.Token);
        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested)
        {
            return;
        }
        catch (RewApiException exception)
        {
            failure = exception.Message;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            failure = $"REW stopped answering while the impulse response was read. ({exception.Message})";
        }

        if (IsDisposed)
        {
            return;
        }

        busy = false;
        UseWaitCursor = false;
        SetStatus(answeringStatus, warning: false);
        if (preparation?.Import is { } import)
        {
            Import = import;
            DialogResult = DialogResult.OK;
            return;
        }

        // After UpdateControls: re-enabling the grid can move the current cell, which clears the problem line.
        UpdateControls();
        labelProblem.Text = failure ?? preparation?.Problem ?? string.Empty;
    }

    private void ShowList(Uri? address, IReadOnlyList<RewMeasurementSummary> measurements, string? selectUuid)
    {
        listedAddress = address;
        measurementGridView.Rows.Clear();
        foreach (RewMeasurementSummary measurement in measurements)
        {
            int index = measurementGridView.Rows.Add(
                measurement.Title ?? string.Empty,
                RewMeasurementImport.DescribeDate(measurement.Date),
                measurement.SampleRate is { } rate ? FormattableString.Invariant($"{rate:0.#}") : string.Empty,
                measurement.TimeOfIRPeakSeconds is { } peak
                    ? FormattableString.Invariant($"{peak * 1000.0:0.###}")
                    : string.Empty);
            DataGridViewRow row = measurementGridView.Rows[index];
            row.Tag = measurement;
            if (RewMeasurementImport.DescribeRateMismatch(measurement.SampleRate, configuredSampleRate) != null)
            {
                row.DefaultCellStyle.ForeColor = UiPalette.TextDisabled;
            }
        }

        DataGridViewRow? selected = measurementGridView.Rows
            .Cast<DataGridViewRow>()
            .FirstOrDefault(row => (row.Tag as RewMeasurementSummary)?.Uuid == selectUuid)
            ?? measurementGridView.Rows.Cast<DataGridViewRow>().FirstOrDefault();
        if (selected != null)
        {
            measurementGridView.CurrentCell = selected.Cells[0];
        }

        UpdateSelection();
    }

    private void UpdateSelection()
    {
        labelProblem.Text = string.Empty;
        if (SelectedMeasurement is not { } measurement)
        {
            labelSelection.Text = string.Empty;
            UpdateControls();
            return;
        }

        string peak = measurement.TimeOfIRPeakSeconds is { } peakSeconds
            ? FormattableString.Invariant(
                $"REW puts this measurement's peak at {peakSeconds * 1000.0:0.###} ms. A stated offset is taken back out, which moves the arrival later by that much.")
            : "REW did not report where this measurement's peak is.";
        bool recordsOffset = measurement.TimingOffsetSeconds is { } reportedOffset && double.IsFinite(reportedOffset);
        if (recordsOffset)
        {
            peak += FormattableString.Invariant(
                $" REW records a {measurement.TimingOffsetSeconds!.Value * 1000.0:0.####} ms timing offset for it.");
        }

        ApplyOffsetAnswer(measurement, recordsOffset);
        if (measurement.CumulativeIRShiftSeconds is { } shift && double.IsFinite(shift) && shift != 0)
        {
            peak += FormattableString.Invariant(
                $" REW has moved its t = 0 by {shift * 1000.0:0.####} ms (Offset t=0); that shift is taken back out with the offset.");
        }

        labelSelection.Text = peak;
        labelProblem.Text =
            RewMeasurementImport.DescribeRateMismatch(measurement.SampleRate, configuredSampleRate) ?? string.Empty;
        UpdateControls();
    }

    /// <summary>The user's own answer for this row if there is one; else REW's recorded offset; else 0.</summary>
    private void ApplyOffsetAnswer(RewMeasurementSummary measurement, bool recordsOffset)
    {
        (decimal offsetMs, bool unknown) =
            measurement.Uuid is { } uuid && offsetAnswers.TryGetValue(uuid, out (decimal, bool) answer)
                ? answer
                : (recordsOffset ? (decimal)(measurement.TimingOffsetSeconds!.Value * 1000.0) : 0m, false);
        applyingOffset = true;
        try
        {
            numericOffset.Value = Math.Round(
                Math.Clamp(offsetMs, numericOffset.Minimum, numericOffset.Maximum),
                numericOffset.DecimalPlaces);
            checkOffsetUnknown.Checked = unknown;
        }
        finally
        {
            applyingOffset = false;
        }
    }

    private void RememberOffsetAnswer()
    {
        if (!applyingOffset && SelectedMeasurement?.Uuid is { } uuid)
        {
            offsetAnswers[uuid] = (numericOffset.Value, checkOffsetUnknown.Checked);
        }
    }

    /// <param name="answered">False before REW has been asked, so nothing is claimed about the level yet.</param>
    private void ShowLevelSource(RewLevel? level, bool answered)
    {
        (string text, bool warning) = level switch
        {
            _ when !answered => (string.Empty, false),
            { Value: { } value, Unit: { } unit } when string.Equals(unit, "dBFS", StringComparison.OrdinalIgnoreCase) &&
                double.IsFinite(value) => ("REW's current setting", false),
            { Unit: { } unit } when !string.IsNullOrWhiteSpace(unit) =>
                ($"REW's level is set in {unit}: enter the dBFS it played at", true),
            _ => ("REW's level could not be read: enter the dBFS it played at", true)
        };
        labelLevelSource.Text = text;
        labelLevelSource.ForeColor = warning ? UiPalette.Warning : UiPalette.TextSecondary;
    }

    private void SetBusy(bool value, string status)
    {
        busy = value;
        UseWaitCursor = value;
        SetStatus(status, warning: false);
        UpdateControls();
    }

    private void SetStatus(string text, bool warning)
    {
        if (!busy)
        {
            answeringStatus = warning ? string.Empty : text;
        }

        labelStatus.Text = text;
        labelStatus.ForeColor = warning ? UiPalette.Warning : UiPalette.TextSecondary;
    }

    private void UpdateControls()
    {
        bool canImport = !busy &&
            listedAddress != null &&
            SelectedMeasurement is { } measurement &&
            RewMeasurementImport.DescribeRateMismatch(measurement.SampleRate, configuredSampleRate) == null;
        MainCommandController.SetButtonFrozen(buttonImport, !canImport);
        MainCommandController.SetButtonFrozen(buttonRefresh, busy);
        textAddress.ReadOnly = busy;
        measurementGridView.Enabled = !busy;
        numericOffset.Enabled = !busy && !checkOffsetUnknown.Checked;
        numericLevel.Enabled = !busy;
        UiStyle.SetTextEnabledLook(checkOffsetUnknown, !busy, interactive: true);
    }

    private void StyleGrid()
    {
        measurementGridView.EnableHeadersVisualStyles = false;
        measurementGridView.GridColor = UiPalette.Border;
        measurementGridView.DefaultCellStyle.BackColor = UiPalette.DialogBackground;
        measurementGridView.DefaultCellStyle.ForeColor = UiPalette.TextPrimary;
        measurementGridView.DefaultCellStyle.SelectionBackColor = UiPalette.ButtonPressedBackground;
        measurementGridView.DefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
        measurementGridView.ColumnHeadersDefaultCellStyle.BackColor = UiPalette.ControlSurface;
        measurementGridView.ColumnHeadersDefaultCellStyle.ForeColor = UiPalette.TextPrimary;
        measurementGridView.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiPalette.ControlSurface;
        measurementGridView.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
        ColumnRate.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        ColumnPeak.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
    }
}
