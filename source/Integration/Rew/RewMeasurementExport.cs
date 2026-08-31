namespace Resonalyze.Integration.Rew;

/// <summary>What one measurement is worth sending as.</summary>
/// <param name="ImpulseResponse">The transfer IR, sample 0 being the loopback reference.</param>
/// <param name="PeakIndex">The arrival's index in that buffer.</param>
/// <param name="SampleRate">The rate it was measured at.</param>
/// <param name="Identifier">The name REW files it under.</param>
/// <param name="SplOffsetDb">This measurement's own dBr → dB SPL offset, or null.</param>
internal sealed record RewExportRequest(
    double[] ImpulseResponse,
    int PeakIndex,
    int SampleRate,
    string Identifier,
    double? SplOffsetDb);

/// <summary>
/// The result of a send. <see cref="Problem"/> is null when the import went through
/// and REW's copy agrees with what was sent — which is the ordinary case, and the
/// one the user is told nothing about.
/// </summary>
internal sealed record RewExportResult(string? Problem)
{
    public bool Verified => Problem == null;
}

/// <summary>
/// Sends one measurement to REW and checks that its copy landed on the time base it
/// was sent with.
/// </summary>
/// <remarks>
/// The check reads the new measurement's SUMMARY — a small JSON, no impulse payload
/// — and compares <c>timeOfIRPeakSeconds</c> with the arrival the payload was framed
/// to produce. It deliberately does not pin REW's version: that is a moving beta, and
/// requiring a matching build would make the user's problem out of something this
/// comparison catches directly. The version REW announces goes into the failure text
/// instead, so a report says what it was talking to.
/// <para>
/// One measured caveat behind the choice of field (REW 5.40 Beta 132 / API 0.9.6):
/// the summary's <c>timeOfIRStartSeconds</c> is NOT the start time that was sent. It
/// is REW's own onset detection — a unit impulse sent with a start time of −20.8 ms
/// reads back 0, and a band-limited arrival reads back six samples before its peak.
/// <c>timeOfIRPeakSeconds</c> is the field that carries the start time back, because
/// REW locates the peak on the same rule this application does (the largest absolute
/// sample), so the two agree on the index and the difference is the start time alone.
/// </para>
/// </remarks>
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

    /// <summary>
    /// The version REW announces, or null when it is not answering, within
    /// <paramref name="timeout"/>. Asked before the export dialog opens, so a REW
    /// that is not running is a line the user can act on rather than an exception
    /// after the click.
    /// </summary>
    /// <remarks>
    /// The deadline is owned here rather than by the caller for a reason. An address
    /// that drops packets instead of refusing the connection reaches the timeout, and
    /// a caller-supplied token that has just been cancelled is indistinguishable from
    /// the caller having given up — so the cancellation escaped as an unhandled
    /// exception on the UI thread. Cancelling a token this method owns keeps the two
    /// apart: the caller's own cancellation still propagates, as it should.
    /// </remarks>
    public async Task<string?> ProbeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            return await client.TryGetVersionAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our deadline, not the caller's: a REW that neither answers nor refuses
            // in the time allowed is a REW that is not answering.
            return null;
        }
    }

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

        RewMeasurementSummary? filed = await WaitForNewMeasurementAsync(
            known,
            request.Identifier,
            cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// The one comparison. A start time that arrived intact puts the arrival exactly
    /// where the payload was framed to put it; anything else is reported in samples,
    /// which is the unit the disagreement would be in.
    /// </summary>
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
        // Half a sample separates "the same sample, to double rounding" from "a
        // different sample": REW cannot report a peak between two of them.
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

    /// <summary>
    /// The measurement this send produced: new since the snapshot AND filed under the
    /// name it was sent with. Both halves are load-bearing. The UUID alone would pick
    /// up a measurement the user made in REW while this one was being filed — REW
    /// stays usable throughout — and verify its timing instead. The title alone would
    /// pick the older of two measurements sharing a name.
    /// </summary>
    private async Task<RewMeasurementSummary?> WaitForNewMeasurementAsync(
        HashSet<string> known,
        string identifier,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + FilingTimeout;
        while (true)
        {
            IReadOnlyDictionary<string, RewMeasurementSummary> current =
                await client.GetMeasurementsAsync(cancellationToken).ConfigureAwait(false);
            foreach (RewMeasurementSummary summary in current.Values)
            {
                if (!string.IsNullOrEmpty(summary.Uuid) &&
                    !known.Contains(summary.Uuid) &&
                    string.Equals(summary.Title, identifier, StringComparison.Ordinal))
                {
                    return summary;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                return null;
            }

            await Task.Delay(FilingPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Identifying the new measurement by UUID rather than by the name it was sent
    /// under: REW allows two measurements to share a title, and a second send of the
    /// same name would otherwise verify against the first one's numbers.
    /// </summary>
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
