using System.Globalization;

namespace Resonalyze.Dsp;

/// <summary>
/// REW's <c>File → Export → Impulse response as text</c>, read back.
/// </summary>
/// <remarks>
/// <para>
/// Of the ways REW can hand an impulse response to another program, this is the only one
/// that carries the absolute time base: the header states the time of sample 0, so a
/// measurement taken against a loopback reference can be put back on the base it was
/// measured on. A WAV export carries the samples and no timing at all unless t = 0 is
/// pinned to a whole sample on the way out — which cannot state a fractional zero, and
/// which rewrites the measurement's own start time to make the cut.
/// </para>
/// <para>
/// Two things in the header decide whether the file is usable, and both have a safe
/// default that is not the one wanted here. An export made with <em>normalise</em> on has
/// its peak scaled to one, so it holds no level relation to any other channel; an export
/// with the IR window applied is not the impulse response but a view of it. Both are
/// refused rather than read, because neither announces itself in the samples.
/// </para>
/// <para>
/// The samples are fractions of full scale — the same numbers REW's API serves for
/// <c>?normalised=false</c>, to float precision.
/// </para>
/// </remarks>
public sealed class RewImpulseResponseTextFile
{
    private const string FileMarker = "Impulse Response data saved by REW";
    private const string DataStartMarker = "* Data start";

    private const string NoHeaders =
        "not a REW impulse-response text export, or one written without its headers " +
        "(the headers carry the time base, so a file without them cannot be placed in time)";

    private RewImpulseResponseTextFile(double[] samples)
    {
        Samples = samples;
    }

    /// <summary>The impulse response as REW served it, as a fraction of full scale.</summary>
    public double[] Samples { get; }

    public int SampleRate { get; private init; }

    public double SampleIntervalSeconds { get; private init; }

    /// <summary>
    /// The time of sample 0, negative for every loopback measurement: REW anchors the
    /// buffer on the microphone peak and lets the reference arrival fall where it falls.
    /// </summary>
    public double StartTimeSeconds { get; private init; }

    /// <summary>
    /// Where t = 0 — the loopback arrival — sits in this buffer, in samples. Fractional
    /// in general: REW pins the peak to a whole sample, not the reference.
    /// </summary>
    public double TimeZeroIndex => -StartTimeSeconds * SampleRate;

    /// <summary>The largest sample's index, as REW states it.</summary>
    public int PeakIndex { get; private init; }

    /// <summary>
    /// REW's peak before normalisation — <em>interpolated</em>, so it sits a little above
    /// the largest sample in the data (0.002 dB on the file this reader was written
    /// against). It is the sub-sample peak, not one of the numbers below it.
    /// </summary>
    public double PeakValueBeforeNormalisation { get; private init; }

    /// <summary>The SPL offset REW would add to turn these samples into dB SPL.</summary>
    public double DataOffsetDb { get; private init; }

    /// <summary>The measurement's name in REW, if the header carried one.</summary>
    public string? MeasurementName { get; private init; }

    /// <summary>The capture device line, if the header carried one.</summary>
    public string? Source { get; private init; }

    /// <summary>The excitation line verbatim — sweep length, count, level and reference.</summary>
    public string? Excitation { get; private init; }

    /// <summary>
    /// The swept band REW reports. Best effort: the header writes it in the exporting
    /// machine's number format, so a file that cannot be read here still parses and
    /// leaves this null rather than failing over metadata.
    /// </summary>
    public double? LowFrequencyHz { get; private init; }

    public double? HighFrequencyHz { get; private init; }

    /// <summary>
    /// The sweep's length in samples, from the excitation line's <c>512k</c> / <c>1M</c>
    /// label. The impulse response is often shorter than the sweep that produced it, so
    /// this is the honest source for a sweep duration; null when the label is unfamiliar.
    /// </summary>
    public int? SweepLengthSamples { get; private init; }

    /// <summary>How many sweeps REW averaged, from the excitation line.</summary>
    public int? SweepCount { get; private init; }

    /// <summary>
    /// The impulse response re-referenced so that <b>sample 0 is the loopback arrival</b>,
    /// which is the convention a transfer impulse response is stated in here. The whole
    /// part of the offset is a rotation, the fractional part an exact shift; REW's
    /// pre-roll wraps to the tail, where the harmonic images of a swept measurement
    /// belong.
    /// </summary>
    public double[] ToLoopbackReferencedImpulseResponse() =>
        FractionalSampleShift.AdvanceCircular(Samples, TimeZeroIndex);

    /// <summary>
    /// Reads an export, or explains in <paramref name="problem"/> why this one cannot be
    /// trusted on the time base it claims.
    /// </summary>
    public static bool TryParse(string text, out RewImpulseResponseTextFile? file, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(text);
        file = null;
        problem = null;

        bool markerSeen = false;
        bool dataStarted = false;
        string? measurement = null;
        string? source = null;
        string? excitation = null;
        double? low = null;
        double? high = null;
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var samples = new List<double>();

        foreach (string rawLine in text.TrimStart('\uFEFF').Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (dataStarted)
            {
                if (!EqTextNumbers.TryParse(line, out double sample) || !double.IsFinite(sample))
                {
                    problem = $"the data holds a value this reader cannot read: \"{Excerpt(line)}\"";
                    return false;
                }

                samples.Add(sample);
                continue;
            }

            if (line.StartsWith(DataStartMarker, StringComparison.OrdinalIgnoreCase))
            {
                dataStarted = true;
                continue;
            }

            if (line.StartsWith('*'))
            {
                string note = line.TrimStart('*').Trim();
                if (note.Contains(FileMarker, StringComparison.OrdinalIgnoreCase))
                {
                    markerSeen = true;
                }
                else if (Says(note, "IR is", "normalised") || Says(note, "IR is", "normalized"))
                {
                    // "IR is not normalised" is the export this reader wants; the other
                    // one has had its peak scaled to 1 and cannot be levelled against
                    // any other channel again.
                    if (!note.Contains(" not ", StringComparison.OrdinalIgnoreCase))
                    {
                        problem = "the export is normalised: its peak has been scaled to one, " +
                            "so it carries no level relation to any other measurement " +
                            "(export again with normalisation off)";
                        return false;
                    }
                }
                else if (Says(note, "IR window", "applied"))
                {
                    if (!note.Contains(" not ", StringComparison.OrdinalIgnoreCase))
                    {
                        problem = "the IR window has been applied: this is a windowed view of the " +
                            "impulse response, not the response (export again with the window off)";
                        return false;
                    }
                }
                else if (Says(note, "IR is", "min phase"))
                {
                    if (!note.Contains(" not ", StringComparison.OrdinalIgnoreCase))
                    {
                        problem = "the export is the minimum-phase version: its excess phase — the " +
                            "arrival time and everything derived from it — has been removed";
                        return false;
                    }
                }
                else if (note.StartsWith("Measurement:", StringComparison.OrdinalIgnoreCase))
                {
                    measurement = note["Measurement:".Length..].Trim();
                }
                else if (note.StartsWith("Source:", StringComparison.OrdinalIgnoreCase))
                {
                    source = note["Source:".Length..].Trim();
                }
                else if (note.StartsWith("Excitation:", StringComparison.OrdinalIgnoreCase))
                {
                    excitation = note["Excitation:".Length..].Trim();
                }
                else if (note.StartsWith("Response measured over:", StringComparison.OrdinalIgnoreCase))
                {
                    ReadBand(note["Response measured over:".Length..], out low, out high);
                }

                continue;
            }

            // "<number> // <label>" — the header's numeric half.
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment < 0)
            {
                problem = markerSeen
                    ? $"unexpected line before the data: \"{Excerpt(line)}\""
                    : NoHeaders;
                return false;
            }

            string label = line[(comment + 2)..].Trim();
            if (!EqTextNumbers.TryParse(line[..comment], out double value))
            {
                problem = $"the header's \"{Excerpt(label)}\" is not a number";
                return false;
            }

            values[label] = value;
        }

        if (!markerSeen || !dataStarted)
        {
            problem = NoHeaders;
            return false;
        }

        if (samples.Count == 0)
        {
            problem = "the export holds no samples";
            return false;
        }

        if (!values.TryGetValue("Sample interval (seconds)", out double interval) || !(interval > 0))
        {
            problem = "the header states no usable sample interval";
            return false;
        }

        double rate = 1.0 / interval;
        int sampleRate = (int)Math.Round(rate);
        if (Math.Abs(rate - sampleRate) > 1e-6 * Math.Max(1.0, rate))
        {
            problem = $"the sample interval states a rate of {rate:0.####} Hz, which is not a whole " +
                "number of samples per second";
            return false;
        }

        if (!values.TryGetValue("Start time (seconds)", out double startTime))
        {
            problem = "the header states no start time, so the samples cannot be placed in time " +
                "(this is the field the whole import rests on)";
            return false;
        }

        if (values.TryGetValue("Response length", out double declaredLength) &&
            (int)Math.Round(declaredLength) != samples.Count)
        {
            problem = $"the header declares {(int)Math.Round(declaredLength)} samples and the file " +
                $"holds {samples.Count}: the export is truncated or was edited";
            return false;
        }

        double timeZero = -startTime * sampleRate;
        if (!(timeZero >= 0) || timeZero >= samples.Count)
        {
            problem = $"t = 0 falls at sample {timeZero:0.###} of {samples.Count}, outside the " +
                "buffer: these samples do not contain the reference arrival";
            return false;
        }

        (int? sweepLength, int? sweepCount) = ReadExcitation(excitation);

        file = new RewImpulseResponseTextFile([.. samples])
        {
            SampleRate = sampleRate,
            SampleIntervalSeconds = interval,
            StartTimeSeconds = startTime,
            PeakIndex = values.TryGetValue("Peak index", out double peakIndex)
                ? (int)Math.Round(peakIndex)
                : 0,
            PeakValueBeforeNormalisation =
                values.TryGetValue("Peak value before normalisation", out double peak) ? peak : 0.0,
            DataOffsetDb = values.TryGetValue("Data offset (dB)", out double offset) ? offset : 0.0,
            MeasurementName = measurement,
            Source = source,
            Excitation = excitation,
            LowFrequencyHz = low,
            HighFrequencyHz = high,
            SweepLengthSamples = sweepLength,
            SweepCount = sweepCount
        };

        return true;
    }

    /// <summary>
    /// Reads an export, throwing with the reason when it cannot be trusted.
    /// </summary>
    public static RewImpulseResponseTextFile Parse(string text)
    {
        if (!TryParse(text, out RewImpulseResponseTextFile? file, out string? problem) || file == null)
        {
            throw new FormatException(
                $"This REW impulse-response export cannot be imported — {problem}.");
        }

        return file;
    }

    /// <summary>
    /// Whether the excitation line says the sweep was measured against a loopback — the
    /// only reference on which REW's t = 0 means what a synchronized-loopback capture
    /// means here. An acoustic reference, or none, gives a shape whose position is its
    /// own; it must not be placed on this base.
    /// </summary>
    public bool IsLoopbackReferenced =>
        Excitation != null &&
        Excitation.Contains("loopback", StringComparison.OrdinalIgnoreCase) &&
        Excitation.Contains("timing reference", StringComparison.OrdinalIgnoreCase);

    private static bool Says(string note, string head, string tail) =>
        note.StartsWith(head, StringComparison.OrdinalIgnoreCase) &&
        note.Contains(tail, StringComparison.OrdinalIgnoreCase);

    private static void ReadBand(string text, out double? low, out double? high)
    {
        low = null;
        high = null;
        string trimmed = text.Replace("Hz", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        int to = trimmed.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
        if (to < 0)
        {
            return;
        }

        if (TryReadGrouped(trimmed[..to], out double parsedLow) &&
            TryReadGrouped(trimmed[(to + 4)..], out double parsedHigh) &&
            parsedLow > 0 && parsedHigh > parsedLow)
        {
            low = parsedLow;
            high = parsedHigh;
        }
    }

    // The band line is written with the exporting machine's thousands separator, so
    // "19,999.9" and "19.999,9" are the same frequency on two different machines.
    private static bool TryReadGrouped(string token, out double value) =>
        double.TryParse(
            token.Trim(),
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value) ||
        double.TryParse(
            token.Trim(),
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.CurrentCulture,
            out value);

    // "512k Log Swept Sine, 1 sweep at -10.0 dBFS using a loopback as a timing reference"
    private static (int? Length, int? Count) ReadExcitation(string? excitation)
    {
        if (string.IsNullOrWhiteSpace(excitation))
        {
            return (null, null);
        }

        int? length = null;
        int? count = null;
        foreach (string token in excitation.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            if (length == null && token.Length > 1 &&
                (token.EndsWith('k') || token.EndsWith('K') || token.EndsWith('M')) &&
                int.TryParse(token[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int scaled))
            {
                length = scaled * (token.EndsWith('M') ? 1024 * 1024 : 1024);
            }
        }

        int sweeps = excitation.IndexOf(" sweep", StringComparison.OrdinalIgnoreCase);
        if (sweeps > 0)
        {
            string before = excitation[..sweeps].Trim();
            int space = before.LastIndexOf(' ');
            string token = space < 0 ? before : before[(space + 1)..];
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
                parsed > 0)
            {
                count = parsed;
            }
        }

        return (length, count);
    }

    private static string Excerpt(string line) =>
        line.Length <= 40 ? line : line[..40] + "…";
}
