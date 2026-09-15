using System.Globalization;

namespace Resonalyze.Dsp;

/// <summary>REW's "Impulse response as text" export: the only REW format carrying the absolute time base (time of sample 0).</summary>
/// <remarks>Normalised exports are multiplied back by their stated peak; windowed and minimum-phase exports (not the IR) are refused.
/// Samples are fractions of full scale.</remarks>
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

    public double[] Samples { get; }

    public int SampleRate { get; private init; }

    public double SampleIntervalSeconds { get; private init; }

    /// <summary>Negative for loopback measurements: REW anchors the buffer on the microphone peak.</summary>
    public double StartTimeSeconds { get; private init; }

    /// <summary>Loopback arrival (t = 0) in samples; fractional in general.</summary>
    public double TimeZeroIndex => -StartTimeSeconds * SampleRate;

    public int PeakIndex { get; private init; }

    /// <summary>Peak minus reference before any timing offset; a negative value implies a REW timing offset larger than the arrival.</summary>
    public double ImpliedArrivalSamples => PeakIndex - TimeZeroIndex;

    /// <summary>The divisor REW normalised by; <see cref="Samples"/> of a normalised export are already multiplied back.</summary>
    public double PeakValueBeforeNormalisation { get; private init; }

    public bool WasNormalised { get; private init; }

    public double DataOffsetDb { get; private init; }

    public string? MeasurementName { get; private init; }

    public string? Source { get; private init; }

    /// <summary>The excitation line verbatim — sweep length, count, level and reference.</summary>
    public string? Excitation { get; private init; }

    /// <summary>Best effort: written in the exporting machine's number format; null rather than a parse failure.</summary>
    public double? LowFrequencyHz { get; private init; }

    public double? HighFrequencyHz { get; private init; }

    /// <summary>From the <c>512k</c> / <c>1M</c> label; the IR is often shorter than the sweep. Null when unfamiliar.</summary>
    public int? SweepLengthSamples { get; private init; }

    public int? SweepCount { get; private init; }

    /// <summary>The sweep's level from the excitation line (<c>at -12.0 dBFS</c>); null when the line states none.</summary>
    public double? SweepLevelDbfs { get; private init; }

    /// <summary>Re-referenced so sample 0 is the loopback arrival; REW's pre-roll wraps to the tail.</summary>
    public double[] ToLoopbackReferencedImpulseResponse() =>
        ToLoopbackReferencedImpulseResponse(0);

    /// <summary>Same, with a REW timing offset (seconds, REW's sign) taken back out: subtracted from the reference index, so the arrival moves later; samples untouched.</summary>
    public double[] ToLoopbackReferencedImpulseResponse(double timingOffsetSeconds) =>
        FractionalSampleShift.AdvanceCircular(
            Samples,
            ReferenceIndexWithOffset(timingOffsetSeconds));

    public double ReferenceIndexWithOffset(double timingOffsetSeconds) =>
        TimeZeroIndex - (timingOffsetSeconds * SampleRate);

    public double ArrivalSamplesWithOffset(double timingOffsetSeconds) =>
        PeakIndex - ReferenceIndexWithOffset(timingOffsetSeconds);

    public static bool TryParse(string text, out RewImpulseResponseTextFile? file, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(text);
        file = null;
        problem = null;

        bool markerSeen = false;
        bool dataStarted = false;
        bool normalised = false;
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
                    normalised = !note.Contains(" not ", StringComparison.OrdinalIgnoreCase);
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
        int peakIndexValue = values.TryGetValue("Peak index", out double peakIndex)
            ? (int)Math.Round(peakIndex)
            : 0;
        if (!(timeZero >= 0) || timeZero >= samples.Count)
        {
            problem = $"t = 0 falls at sample {timeZero:0.###} of {samples.Count}, outside the " +
                "buffer: these samples do not contain the reference arrival";
            return false;
        }

        bool statesPeak = values.TryGetValue("Peak value before normalisation", out double peak);
        if (normalised)
        {
            if (!statesPeak || !double.IsFinite(peak) || !(peak > 0))
            {
                problem = "the export is normalised and states no peak value before normalisation, so its " +
                    "level cannot be restored (export again with normalisation off)";
                return false;
            }

            // Multiplying back matched REW's API samples to -139 dB of the peak (REW 5.40 b134).
            for (int i = 0; i < samples.Count; i++)
            {
                samples[i] *= peak;
            }
        }

        // A negative implied arrival is not refused: it signals a REW timing offset, which the importer asks the user for.
        (int? sweepLength, int? sweepCount, double? sweepLevel) = ReadExcitation(excitation);

        file = new RewImpulseResponseTextFile([.. samples])
        {
            SampleRate = sampleRate,
            SampleIntervalSeconds = interval,
            StartTimeSeconds = startTime,
            PeakIndex = peakIndexValue,
            WasNormalised = normalised,
            PeakValueBeforeNormalisation = statesPeak ? peak : 0.0,
            DataOffsetDb = values.TryGetValue("Data offset (dB)", out double offset) ? offset : 0.0,
            MeasurementName = measurement,
            Source = source,
            Excitation = excitation,
            LowFrequencyHz = low,
            HighFrequencyHz = high,
            SweepLengthSamples = sweepLength,
            SweepCount = sweepCount,
            SweepLevelDbfs = sweepLevel
        };

        return true;
    }

    public static RewImpulseResponseTextFile Parse(string text)
    {
        if (!TryParse(text, out RewImpulseResponseTextFile? file, out string? problem) || file == null)
        {
            throw new FormatException(
                $"This REW impulse-response export cannot be imported — {problem}.");
        }

        return file;
    }

    /// <summary>Only a loopback reference gives REW's t = 0 the meaning of a synchronized-loopback capture.</summary>
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

    /// <summary>Reads "19,999.9" or "19.999,9": the token decides the decimal point (rightmost of two; a lone separator
    /// three digits from the end is a thousands group). No culture is the right authority for a foreign file.</summary>
    private static bool TryReadGrouped(string token, out double value)
    {
        value = 0;
        string trimmed = token.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        int lastDot = trimmed.LastIndexOf('.');
        int lastComma = trimmed.LastIndexOf(',');
        char? decimalSeparator;
        if (lastDot >= 0 && lastComma >= 0)
        {
            decimalSeparator = lastDot > lastComma ? '.' : ',';
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            char only = lastDot >= 0 ? '.' : ',';
            int at = lastDot >= 0 ? lastDot : lastComma;
            int occurrences = trimmed.Count(character => character == only);
            bool looksGrouped = occurrences > 1 ||
                (occurrences == 1 && trimmed.Length - at - 1 == 3);
            decimalSeparator = looksGrouped ? null : only;
        }
        else
        {
            decimalSeparator = null;
        }

        string normalized = decimalSeparator is char separator
            ? trimmed
                .Replace(separator == '.' ? "," : ".", string.Empty, StringComparison.Ordinal)
                .Replace(separator, '.')
            : trimmed
                .Replace(".", string.Empty, StringComparison.Ordinal)
                .Replace(",", string.Empty, StringComparison.Ordinal);

        return double.TryParse(
            normalized,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
    }

    // e.g. "512k Log Swept Sine, 1 sweep at -10.0 dBFS using a loopback as a timing reference"
    private static (int? Length, int? Count, double? LevelDbfs) ReadExcitation(string? excitation)
    {
        if (string.IsNullOrWhiteSpace(excitation))
        {
            return (null, null, null);
        }

        int? length = null;
        int? count = null;
        double? level = null;
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
        int dbfs = excitation.IndexOf(" dBFS", StringComparison.OrdinalIgnoreCase);
        if (dbfs > 0)
        {
            string before = excitation[..dbfs].Trim();
            string token = before[(before.LastIndexOf(' ') + 1)..];
            if (TryReadGrouped(token.TrimStart('-', '+'), out double magnitude) && double.IsFinite(magnitude))
            {
                level = token.StartsWith('-') ? -magnitude : magnitude;
            }
        }

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

        return (length, count, level);
    }

    private static string Excerpt(string line) =>
        line.Length <= 40 ? line : line[..40] + "…";
}
