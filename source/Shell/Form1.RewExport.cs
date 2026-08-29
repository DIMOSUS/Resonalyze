using Resonalyze.Integration.Rew;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

public partial class Form1
{
    // One client for the whole session, as the update checker keeps one: a new
    // HttpClient per send would leak sockets. The timeout is the transport's own
    // ceiling; the probe imposes a much shorter one of its own, because a REW that
    // is not there must not make a menu wait.
    private static readonly HttpClient RewHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly TimeSpan RewProbeTimeout = TimeSpan.FromSeconds(2);

    private string RewBaseUrl =>
        string.IsNullOrWhiteSpace(measurementSettings.RewApiBaseUrl)
            ? RewApiClient.DefaultBaseUrl
            : measurementSettings.RewApiBaseUrl;

    private async void buttonSave_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        await ShowRewExportMenuAsync();
    }

    /// <summary>
    /// Opens the Save button's secondary menu, having first asked REW whether it is
    /// there. The question is asked BEFORE the menu appears so an absent REW is a
    /// caption the user reads, not an exception after the click; on localhost the
    /// answer — including a refused connection — comes back in milliseconds.
    /// </summary>
    private async Task ShowRewExportMenuAsync()
    {
        // In MMM the Save/Load pair belongs to that mode's own capture, not to the
        // impulse response this export sends (see Form1.LiveCapture).
        if (LiveCaptureOwnsSaveLoad)
        {
            return;
        }

        // Nothing to offer without a transfer response. The Save button is frozen
        // in that state anyway, so this is the guard rather than a caption: an item
        // saying why would be one nobody can reach.
        if (expSweepMeasurement.Transfer is not { ImpulseResponse.Length: > 0 } ||
            expSweepMeasurement.InProgress)
        {
            return;
        }

        string? version = null;
        if (RewApiClient.TryParseBaseAddress(RewBaseUrl, out Uri? baseAddress))
        {
            using var probeTimeout = new CancellationTokenSource(RewProbeTimeout);
            version = await CreateRewExport(baseAddress!)
                .ProbeAsync(probeTimeout.Token);
        }

        if (IsDisposed || Disposing)
        {
            return;
        }

        rewExportMenu?.Dispose();
        rewExportMenu = new ContextMenuStrip
        {
            BackColor = UiPalette.ButtonBackground,
            ForeColor = UiPalette.TextPrimary,
            ShowImageMargin = false
        };

        // The probe's answer is a caption, not a gate: the address that would fix a
        // REW nobody can reach is the setting the dialog behind this item holds.
        var sendItem = new ToolStripMenuItem(
            version == null
                ? "Send to REW... (not answering)"
                : "Send to REW...")
        {
            ForeColor = UiPalette.TextPrimary
        };
        sendItem.Click += async (_, _) => await SendToRewAsync(version);
        rewExportMenu.Items.Add(sendItem);
        rewExportMenu.Show(buttonSave, new Point(0, buttonSave.Height));
    }

    private async Task SendToRewAsync(string? version)
    {
        if (expSweepMeasurement.Transfer is not { ImpulseResponse.Length: > 0 } transfer)
        {
            return;
        }

        double? splOffsetDb = new MeasurementPlotContext(expSweepMeasurement).SplOffsetDb;
        using var dialog = new RewExportDialog(
            SuggestRewMeasurementName(),
            RewBaseUrl,
            version,
            splOffsetDb,
            expSweepMeasurement.TimingReference);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(dialog.MeasurementName))
        {
            ReportRewProblem("The measurement needs a name to be filed under in REW.");
            return;
        }

        if (!RewApiClient.TryParseBaseAddress(dialog.BaseUrl, out Uri? baseAddress))
        {
            ReportRewProblem(
                $"\"{dialog.BaseUrl}\" is not an http address. REW's API normally " +
                $"listens on {RewApiClient.DefaultBaseUrl}");
            return;
        }

        measurementSettings.RewApiBaseUrl = dialog.BaseUrl;
        ScheduleMeasurementSettingsSave();

        var samples = new double[transfer.ImpulseResponse.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = transfer.ImpulseResponse[i].Real;
        }

        var request = new RewExportRequest(
            samples,
            transfer.PeakIndex,
            expSweepMeasurement.SampleRate,
            dialog.MeasurementName,
            splOffsetDb);

        UseWaitCursor = true;
        try
        {
            RewExportResult result = await CreateRewExport(baseAddress!)
                .SendAsync(request, CancellationToken.None);
            // Nothing is shown when the numbers agree: the measurement is in REW,
            // which is where the user is looking.
            if (!result.Verified)
            {
                ReportRewProblem(result.Problem!);
            }
        }
        catch (RewApiException exception)
        {
            ReportRewProblem(exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ReportRewProblem(
                $"REW stopped answering while the measurement was being sent.\r\n\r\n{exception.Message}");
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private RewMeasurementExport CreateRewExport(Uri baseAddress) =>
        new(new RewApiClient(RewHttpClient, baseAddress));

    /// <summary>
    /// What to call the measurement in REW: the file this response came from, so the
    /// two sides can be told apart there, or the clock when it came from a live run.
    /// </summary>
    private string SuggestRewMeasurementName()
    {
        string? fileName = plotModelFactory.ImpulseResponseFileName;
        return string.IsNullOrWhiteSpace(fileName)
            ? $"Resonalyze {DateTime.Now:yyyy-MM-dd HH-mm-ss}"
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private void ReportRewProblem(string message) =>
        MessageBox.Show(
            this,
            message,
            "Send to REW",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
}
