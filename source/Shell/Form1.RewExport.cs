using Resonalyze.Integration.Rew;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

public partial class Form1
{
    // One client for the whole session, as the update checker keeps one: a new
    // HttpClient per send would leak sockets. The timeout is the transport's own
    // ceiling; the probe imposes a much shorter one of its own, because a REW that
    // is not there must not make the button wait.
    private static readonly HttpClient RewHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly TimeSpan RewProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether a send is already on its way. The handler is <c>async void</c> and the
    /// form stays live across both the probe and a send that may run for thirty
    /// seconds, so without this a second click starts a second export — two imports
    /// under one name, and each verification then has the other's measurement to pick
    /// between. Disabling the button is not enough on its own: the click that is
    /// already queued arrives after the check and before the disable takes effect.
    /// </summary>
    private bool rewExportInFlight;

    private string RewBaseUrl =>
        string.IsNullOrWhiteSpace(measurementSettings.RewApiBaseUrl)
            ? RewApiClient.DefaultBaseUrl
            : measurementSettings.RewApiBaseUrl;

    /// <summary>
    /// Sends the current measurement to REW, having first asked REW whether it is
    /// there. The question is asked here rather than behind the button, so a REW
    /// that is not running is a line in the dialog the user can act on — the
    /// address that would fix it is the setting that dialog holds.
    /// </summary>
    private async void buttonRewExport_Click(object? sender, EventArgs e)
    {
        // The button is frozen in every state this cannot serve, so these are
        // assertions rather than the guards they were when the gesture was hidden.
        if (!CanExportToRew || rewExportInFlight)
        {
            return;
        }

        rewExportInFlight = true;
        try
        {
            await RunRewExportAsync();
        }
        finally
        {
            rewExportInFlight = false;
        }
    }

    private async Task RunRewExportAsync()
    {
        string? version = null;
        if (RewApiClient.TryParseBaseAddress(RewBaseUrl, out Uri? baseAddress))
        {
            UseWaitCursor = true;
            try
            {
                version = await CreateRewExport(baseAddress!)
                    .ProbeAsync(RewProbeTimeout, CancellationToken.None);
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    UseWaitCursor = false;
                }
            }
        }

        if (IsDisposed || Disposing)
        {
            return;
        }

        await SendToRewAsync(version);
    }

    /// <summary>
    /// Whether this measurement can go to REW at all: a finished transfer response
    /// that the impulse-response side of the shell owns. In MMM the Save/Load pair
    /// belongs to that mode's own capture, not to the response this sends (see
    /// Form1.LiveCapture).
    /// </summary>
    internal bool CanExportToRew =>
        !LiveCaptureOwnsSaveLoad &&
        !expSweepMeasurement.InProgress &&
        expSweepMeasurement.Transfer is { ImpulseResponse.Length: > 0 };

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
            if (!IsDisposed && !Disposing)
            {
                UseWaitCursor = false;
            }
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

    /// <summary>
    /// Reports a problem, unless the window it would be shown over has gone. A send
    /// lives up to thirty seconds and the form stays interactive throughout, so
    /// closing the application inside that window would otherwise turn a message
    /// into a crash on a disposed handle.
    /// </summary>
    private void ReportRewProblem(string message)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        MessageBox.Show(
            this,
            message,
            "Send to REW",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
