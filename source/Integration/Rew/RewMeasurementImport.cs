using System.Globalization;
using System.Text.Json;
using Resonalyze.Dsp;

namespace Resonalyze.Integration.Rew;

/// <param name="Version">Null when REW did not answer; the list is then empty.</param>
/// <param name="LevelDbfs">REW's current level setting, null when REW states it in another unit.</param>
internal sealed record RewMeasurementCatalog(
    string? Version,
    IReadOnlyList<RewMeasurementSummary> Measurements,
    string? SelectedUuid,
    double? LevelDbfs = null);

/// <param name="Samples">The impulse response relative to the loopback, t = 0 at <see cref="TimeZeroIndex"/>.</param>
/// <param name="Referenced">Re-referenced so sample 0 is the loopback arrival, the stated offset taken out.</param>
/// <param name="SweepLevelDbfs">The level taken back out of REW's full-scale samples.</param>
/// <param name="BandFromRew">False when REW listed no usable range and 20 Hz to Nyquist stands in.</param>
/// <param name="SweepRate">Read from the harmonic packets; null when none stood above the noise.</param>
internal sealed record RewPreparedImport(
    RewMeasurementSummary Measurement,
    double[] Samples,
    double[] Referenced,
    int SampleRate,
    double TimeZeroIndex,
    RewImportTimingPlan Plan,
    double SweepLevelDbfs,
    double LowFrequencyHz,
    double HighFrequencyHz,
    bool BandFromRew,
    EssSweepRateEstimate? SweepRate)
{
    public int SweepLengthSamples =>
        RewMeasurementImport.SweepLengthSamples(SweepRate, LowFrequencyHz, HighFrequencyHz, SampleRate, Samples.Length);
}

/// <summary>Exactly one of <see cref="Import"/> and <see cref="Problem"/> is set.</summary>
internal sealed record RewImportPreparation(RewPreparedImport? Import, string? Problem);

/// <summary>Reads a REW measurement over the API and decides, before anything is installed, what it may claim about time.</summary>
/// <remarks>See docs/tech/sweep-measurement.md#rew-import-timing.</remarks>
internal sealed class RewMeasurementImport
{
    /// <summary>REW serves percent: a sample sent as 0.8 read back as 80 (REW 5.40 b134).</summary>
    public const double PercentOfFullScale = 100.0;

    private readonly RewApiClient client;

    public RewMeasurementImport(RewApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
    }

    public async Task<RewMeasurementCatalog> ListAsync(
        TimeSpan probeTimeout,
        CancellationToken cancellationToken)
    {
        string? version = await client.ProbeVersionAsync(probeTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (version == null)
        {
            return new RewMeasurementCatalog(null, [], null);
        }

        IReadOnlyDictionary<string, RewMeasurementSummary> measurements =
            await client.GetMeasurementsAsync(cancellationToken).ConfigureAwait(false);
        string? selected = await client.TryGetSelectedMeasurementUuidAsync(cancellationToken)
            .ConfigureAwait(false);
        double? level = await client.TryGetMeasurementLevelDbfsAsync(cancellationToken)
            .ConfigureAwait(false);

        // Keyed by REW's index as text, so "10" would sort before "2" as a string.
        List<RewMeasurementSummary> ordered = measurements
            .Where(pair => !string.IsNullOrEmpty(pair.Value.Uuid))
            .OrderBy(pair => int.TryParse(pair.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                ? index
                : int.MaxValue)
            .Select(pair => pair.Value)
            .ToList();
        return new RewMeasurementCatalog(version, ordered, selected, level);
    }

    /// <param name="sweepLevelDbfs">The level REW's loopback ran at: REW scales to digital full scale, a transfer function here
    /// divides by the loopback. Measured: a -12 dBFS REW sweep arrived 12.0 dB below the same position measured here.</param>
    public async Task<RewImportPreparation> PrepareAsync(
        RewMeasurementSummary measurement,
        double? statedOffsetSeconds,
        double sweepLevelDbfs,
        int configuredSampleRate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        if (DescribeRateMismatch(measurement.SampleRate, configuredSampleRate) is { } listedMismatch)
        {
            return Refuse(listedMismatch);
        }

        if (!double.IsFinite(sweepLevelDbfs) || sweepLevelDbfs > 0)
        {
            return Refuse("The sweep level must be a number of dBFS at or below 0.");
        }

        RewImpulseResponseBody body = await client
            .GetImpulseResponseAsync(measurement.Uuid!, cancellationToken)
            .ConfigureAwait(false);
        if (!TryReadBody(body, out double[] samples, out int sampleRate, out string? problem))
        {
            return Refuse($"REW's impulse response cannot be imported: {problem}.");
        }

        if (DescribeRateMismatch(sampleRate, configuredSampleRate) is { } servedMismatch)
        {
            return Refuse(servedMismatch);
        }

        TakeLevelOut(samples, sweepLevelDbfs);

        if (!IsLoopbackReferenced(body.TimingReference))
        {
            // Without a loopback reference the arrival time is meaningless and would be summed with real ones.
            return Refuse(
                "This measurement was not taken against a loopback timing reference" +
                (string.IsNullOrWhiteSpace(body.TimingReference)
                    ? " (REW did not say which reference it used)"
                    : $" (REW says: “{body.TimingReference}”)") +
                ". Its shape is real, but nothing ties its zero to anything outside its own " +
                "measurement, so it cannot be placed on this session's time base. Measure it in " +
                "REW with a loopback as the timing reference to import it.");
        }

        double timeZeroIndex = -body.StartTime!.Value * sampleRate;
        if (!(timeZeroIndex >= 0) || timeZeroIndex >= samples.Length)
        {
            return Refuse(FormattableString.Invariant(
                $"REW's impulse response cannot be imported: t = 0 falls at sample {timeZeroIndex:0.###} of {samples.Length}, outside the buffer, so these samples do not contain the reference arrival."));
        }

        if (!RewImportTiming.TryResolve(
                statedOffsetSeconds,
                timeZeroIndex,
                PeakIndexOf(samples),
                samples.Length,
                sampleRate,
                out RewImportTimingPlan? plan,
                out string? timingProblem) ||
            plan == null)
        {
            return Refuse($"This measurement cannot be imported — {timingProblem}.");
        }

        double[] referenced = await Task.Run(
            () => FractionalSampleShift.AdvanceCircular(samples, plan.ReferenceIndex),
            cancellationToken).ConfigureAwait(false);
        EssSweepRateEstimate? sweepRate = EstimateSweepRate(samples, sampleRate);
        (double lowHz, double highHz, bool bandFromRew) = ResolveBand(measurement, sampleRate);
        return new RewImportPreparation(
            new RewPreparedImport(
                measurement, samples, referenced, sampleRate, timeZeroIndex, plan, sweepLevelDbfs,
                lowHz, highHz, bandFromRew, sweepRate),
            null);
    }

    private const double MaxHarmonicLookBackSeconds = 2.0;

    /// <summary>REW scales to digital full scale, a transfer function here to the loopback: multiplies by 10^(-level/20) in place.</summary>
    public static void TakeLevelOut(double[] samples, double sweepLevelDbfs)
    {
        double toLoopback = Math.Pow(10.0, -sweepLevelDbfs / 20.0);
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= toLoopback;
        }
    }

    /// <summary>Reads REW's unwrapped buffer, whose pre-roll holds the harmonics; before any re-referencing wraps them.</summary>
    public static EssSweepRateEstimate? EstimateSweepRate(double[] samples, int sampleRate)
    {
        int peakIndex = PeakIndexOf(samples);
        return EssSweepRateEstimator.Estimate(
            samples, peakIndex, sampleRate, Math.Min(MaxHarmonicLookBackSeconds, peakIndex / (double)sampleRate));
    }

    /// <summary>A duration giving the measured rate over the stated band, which is all harmonic geometry reads; REW's real start
    /// frequency is not stated, so it is not the sweep's real length. <paramref name="fallback"/> without a rate.</summary>
    public static int SweepLengthSamples(EssSweepRateEstimate? rate, double lowHz, double highHz, int sampleRate, int fallback) =>
        rate is { } measured && lowHz > 0 && highHz > lowHz
            ? (int)Math.Round(measured.SecondsPerNeper * Math.Log(highHz / lowHz) * sampleRate)
            : fallback;

    public const double FallbackLowFrequencyHz = 20.0;

    /// <summary>REW's listed range, the top clamped to Nyquist; 20 Hz to Nyquist when REW lists none.</summary>
    public static (double LowHz, double HighHz, bool FromRew) ResolveBand(RewMeasurementSummary measurement, int sampleRate)
    {
        double nyquist = sampleRate / 2.0;
        if (measurement is { StartFrequencyHz: { } start, EndFrequencyHz: { } end } &&
            double.IsFinite(start) && double.IsFinite(end) && start > 0 && start < nyquist && end > start)
        {
            return (start, Math.Min(end, nyquist), true);
        }

        return (FallbackLowFrequencyHz, nyquist, false);
    }

    /// <summary>Null when the rates agree or REW did not state one; the sweep is generated at the configured rate.</summary>
    public static string? DescribeRateMismatch(double? measurementSampleRate, int configuredSampleRate)
    {
        if (measurementSampleRate is not { } rate || Math.Round(rate) == configuredSampleRate)
        {
            return null;
        }

        return FormattableString.Invariant(
            $"This measurement is {rate:0.#} Hz while the measurement is configured for {configuredSampleRate} Hz. Set the sample rate in the measurement settings to match, then import it.");
    }

    public static bool IsLoopbackReferenced(string? timingReference) =>
        timingReference?.Contains("loopback", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Largest |sample|, the rule REW's own peak uses.</summary>
    public static int PeakIndexOf(IReadOnlyList<double> samples)
    {
        int peak = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            if (Math.Abs(samples[i]) > Math.Abs(samples[peak]))
            {
                peak = i;
            }
        }

        return peak;
    }

    /// <summary>REW's date as sent: a string verbatim, anything else as its JSON text.</summary>
    public static string DescribeDate(JsonElement? date) =>
        date switch
        {
            { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
            { ValueKind: JsonValueKind.Undefined or JsonValueKind.Null } or null => string.Empty,
            { } element => element.GetRawText()
        };

    private static bool TryReadBody(
        RewImpulseResponseBody body,
        out double[] samples,
        out int sampleRate,
        out string? problem)
    {
        samples = [];
        sampleRate = 0;
        problem = null;

        double? rate = body.SampleRate is > 0
            ? body.SampleRate
            : body.SampleInterval is > 0 ? 1.0 / body.SampleInterval : null;
        if (rate is not { } statedRate)
        {
            problem = "it states no sample rate";
            return false;
        }

        sampleRate = (int)Math.Round(statedRate);
        if (Math.Abs(statedRate - sampleRate) > 1e-6 * Math.Max(1.0, statedRate))
        {
            problem = FormattableString.Invariant(
                $"its rate of {statedRate:0.####} Hz is not a whole number of samples per second");
            return false;
        }

        if (body.StartTime is not { } startTime || !double.IsFinite(startTime))
        {
            problem = "it states no start time, so the samples cannot be placed in time";
            return false;
        }

        if (string.IsNullOrEmpty(body.Data))
        {
            problem = "it holds no samples";
            return false;
        }

        // Percent is REW's documented default; any other unit is not a linear sample.
        if (body.Unit != null && !string.Equals(body.Unit, "percent", StringComparison.OrdinalIgnoreCase))
        {
            problem = $"it arrived in “{body.Unit}” rather than percent of full scale";
            return false;
        }

        try
        {
            samples = RewImpulseResponsePayload.DecodeSamples(body.Data);
        }
        catch (FormatException exception)
        {
            problem = exception.Message;
            return false;
        }

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] /= PercentOfFullScale;
        }

        if (samples.Length == 0)
        {
            problem = "it holds no samples";
            return false;
        }

        if (Array.Exists(samples, sample => !double.IsFinite(sample)))
        {
            problem = "it holds a sample that is not a finite number";
            return false;
        }

        return true;
    }

    private static RewImportPreparation Refuse(string problem) => new(null, problem);
}
