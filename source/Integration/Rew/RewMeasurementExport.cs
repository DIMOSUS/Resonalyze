namespace Resonalyze.Integration.Rew;

/// <param name="ImpulseResponse">Transfer IR, sample 0 being the loopback reference.</param>
/// <param name="SplOffsetDb">This measurement's dBr to dB SPL offset, or null.</param>
internal sealed record RewExportRequest(
    double[] ImpulseResponse,
    int PeakIndex,
    int SampleRate,
    string Identifier,
    double? SplOffsetDb);

/// <summary><see cref="Problem"/> is null when the import went through and REW's copy agrees.</summary>
internal sealed record RewExportResult(string? Problem)
{
    public bool Verified => Problem == null;
}

/// <summary>Sends one measurement to REW and verifies its time base via <c>timeOfIRPeakSeconds</c> in the summary (REW version not pinned).</summary>
/// <remarks><c>timeOfIRStartSeconds</c> is REW's own onset detection, not the sent start time (REW 5.40 b132 / API 0.9.6);
/// the peak uses the same largest-|sample| rule as ours, so its difference is the start time alone.</remarks>
internal sealed class RewMeasurementExport
{
    private static readonly TimeSpan FilingTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FilingPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly RewApiClient client;

    public RewMeasurementExport(RewApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
    }

    public Task<string?> ProbeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        client.ProbeVersionAsync(timeout, cancellationToken);

    public async Task<RewExportResult> SendAsync(
        RewExportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? version = await client.TryGetVersionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (version == null)
        {
            return new RewExportResult(
                "REW is not answering. Check that it is running and that its API " +
                "server is enabled (Preferences -> API).");
        }

        RewImpulseResponseImport import = RewImpulseResponsePayload.Build(
            request.ImpulseResponse,
            request.PeakIndex,
            request.SampleRate,
            request.Identifier,
            request.SplOffsetDb);

        IReadOnlyDictionary<string, RewMeasurementSummary> before =
            await client.GetMeasurementsAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string> known = UuidsOf(before);

        await client.ImportImpulseResponseAsync(import.Body, cancellationToken)
            .ConfigureAwait(false);

        RewMeasurementSummary? filed;
        bool ambiguous;
        try
        {
            (filed, ambiguous) = await WaitForNewMeasurementAsync(
                known,
                request.Identifier,
                cancellationToken).ConfigureAwait(false);
        }
        catch (RewApiException exception)
        {
            throw new RewApiException(
                $"{exception.Message} The measurement was sent, but it could not be found in the list to check its timing.");
        }
        if (ambiguous)
        {
            return new RewExportResult(
                "REW filed more than one new measurement under this name while the " +
                "export was waiting, so which one to check could not be decided. The " +
                "measurement is in REW; its timing has not been verified. " +
                $"(REW reported: {version}.)");
        }

        if (filed == null)
        {
            string waited = FormattableString.Invariant(
                $"REW accepted the import but had not filed it after {FilingTimeout.TotalSeconds:0} seconds,");
            return new RewExportResult(
                waited +
                $" so its timing could not be checked. (REW reported: {version}.)");
        }

        return new RewExportResult(VerifyPeak(filed, import, request.SampleRate, version));
    }

    private static string? VerifyPeak(
        RewMeasurementSummary filed,
        RewImpulseResponseImport import,
        int sampleRate,
        string version)
    {
        if (filed.TimeOfIRPeakSeconds is not { } reported)
        {
            return $"REW filed the measurement but reported no peak time, so its " +
                $"timing could not be checked. (REW reported: {version}.)";
        }

        double differenceSeconds = reported - import.PeakTimeSeconds;
        // REW cannot report a peak between samples, so half a sample separates rounding from a different sample.
        if (Math.Abs(differenceSeconds) * sampleRate <= 0.5)
        {
            return null;
        }

        string numbers = FormattableString.Invariant(
            $"REW puts this measurement's arrival at {reported * 1000.0:0.####} ms, where it was sent to land at {import.PeakTimeSeconds * 1000.0:0.####} ms — a difference of {differenceSeconds * sampleRate:0.###} samples.");
        return numbers +
            " The measurement is in REW, but its time base is not the one it was sent " +
            $"with, so delays read from it are not this session's. (REW reported: {version}.)";
    }

    /// <summary>New since the snapshot AND filed under the sent name: the UUID alone catches a user's concurrent REW measurement, the title alone an older namesake.</summary>
    /// <remarks>Prefix match: REW truncates long titles by display width (54 chars -> 48, 64 -> 45 on 5.40 b132).</remarks>
    private async Task<(RewMeasurementSummary? Filed, bool Ambiguous)> WaitForNewMeasurementAsync(
        HashSet<string> known,
        string identifier,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + FilingTimeout;
        while (true)
        {
            IReadOnlyDictionary<string, RewMeasurementSummary> current =
                await client.GetMeasurementsAsync(cancellationToken).ConfigureAwait(false);

            // The least-cut title wins; an equal tie cannot be settled by waiting, and guessing verifies the wrong measurement.
            RewMeasurementSummary? best = null;
            bool ambiguous = false;
            foreach (RewMeasurementSummary summary in current.Values)
            {
                if (string.IsNullOrEmpty(summary.Uuid) ||
                    known.Contains(summary.Uuid) ||
                    !IsFiledAs(summary.Title, identifier))
                {
                    continue;
                }

                if (best == null || summary.Title!.Length > best.Title!.Length)
                {
                    best = summary;
                    ambiguous = false;
                }
                else if (summary.Title!.Length == best.Title!.Length)
                {
                    ambiguous = true;
                }
            }

            if (ambiguous)
            {
                return (null, true);
            }

            if (best != null)
            {
                return (best, false);
            }

            if (DateTime.UtcNow >= deadline)
            {
                return (null, false);
            }

            await Task.Delay(FilingPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Below the shortest observed REW truncation (40 whole, 47 -> 45, 64 -> 45), so a short user title is not taken for a cut of ours.</summary>
    private const int ShortestTruncation = 40;

    /// <summary>Exact match, or a prefix at least <see cref="ShortestTruncation"/> long; empty never matches.</summary>
    internal static bool IsFiledAs(string? filedTitle, string identifier)
    {
        if (string.IsNullOrEmpty(filedTitle))
        {
            return false;
        }

        if (string.Equals(filedTitle, identifier, StringComparison.Ordinal))
        {
            return true;
        }

        return filedTitle.Length >= ShortestTruncation &&
            identifier.StartsWith(filedTitle, StringComparison.Ordinal);
    }

    /// <summary>By UUID: REW allows two measurements to share a title.</summary>
    private static HashSet<string> UuidsOf(
        IReadOnlyDictionary<string, RewMeasurementSummary> measurements)
    {
        var uuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (RewMeasurementSummary summary in measurements.Values)
        {
            if (!string.IsNullOrEmpty(summary.Uuid))
            {
                uuids.Add(summary.Uuid);
            }
        }

        return uuids;
    }
}
