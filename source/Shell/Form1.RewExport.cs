using Resonalyze.Integration.Rew;
using Resonalyze.Ui.Dialogs;

namespace Resonalyze;

public partial class Form1
{
    // One client for the session (a client per send leaks sockets); the probe uses its own much shorter timeout.
    private static readonly HttpClient RewHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly TimeSpan RewProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>A queued second click passes before the button disable takes effect; two exports would confuse each verification.</summary>
    private bool rewExportInFlight;

    private string RewBaseUrl =>
        string.IsNullOrWhiteSpace(measurementSettings.RewApiBaseUrl)
            ? RewApiClient.DefaultBaseUrl
            : measurementSettings.RewApiBaseUrl;

    /// <summary>Probes REW here so a missing REW is a line in the dialog that holds the address setting.</summary>
    private async void buttonRewExport_Click(object? sender, EventArgs e)
    {
        // The button is frozen in states this cannot serve; these are assertions.
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

    /// <summary>A finished transfer response owned by the IR side (in MMM Save/Load belong to the capture).</summary>
    internal bool CanExportToRew =>
        !LiveCaptureOwnsSaveLoad &&
        !analyzerDocument.IsBusy &&
        analyzerDocument.Result?.HasTransfer == true;

    private async Task SendToRewAsync(string? version)
    {
        if (analyzerDocument.Result is not { Transfer.ImpulseResponse.Length: > 0 } result)
        {
            return;
        }

        MeasurementImpulseResponse transfer = result.Transfer;
        double? splOffsetDb = result.SplOffsetDb;
        using var dialog = new RewExportDialog(
            SuggestRewMeasurementName(),
            RewBaseUrl,
            version,
            splOffsetDb,
            result.TimingReference);
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
            result.SampleRate,
            dialog.MeasurementName,
            splOffsetDb);

        UseWaitCursor = true;
        try
        {
            RewExportResult sent = await CreateRewExport(baseAddress!)
                .SendAsync(request, CancellationToken.None);
            if (!sent.Verified)
            {
                ReportRewProblem(sent.Problem!);
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

    private string SuggestRewMeasurementName()
    {
        string? fileName = plotModelFactory.ImpulseResponseFileName;
        return string.IsNullOrWhiteSpace(fileName)
            ? $"Resonalyze {DateTime.Now:yyyy-MM-dd HH-mm-ss}"
            : Path.GetFileNameWithoutExtension(fileName);
    }

    /// <summary>A send lasts up to 30 s with the form live; closing meanwhile must not crash on a disposed handle.</summary>
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
