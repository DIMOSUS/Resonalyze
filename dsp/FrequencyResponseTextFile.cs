using System.Globalization;

namespace Resonalyze.Dsp;

/// <summary>A magnitude response as text: REW's "Export measurement as text" (with or without phase), or any
/// "frequency level" table. A third column is ignored; header lines start with <c>*</c> or <c>#</c>.</summary>
public sealed class FrequencyResponseTextFile
{
    private const string ImpulseResponseMarker = "Impulse Response data saved by REW";

    private static readonly char[] Separators = [' ', '\t', ';'];

    // Units a header may write bare after the level's name ("Frequency Level V"); dB ones pass, the rest refuse.
    private static readonly HashSet<string> BareUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "dB", "dBFS", "dBSPL", "dBV", "dBu", "V", "mV", "Pa", "mPa", "ohm", "ohms", "\u03a9", "ms", "%", "deg", "degrees", "rad"
    };

    private FrequencyResponseTextFile(double[] frequenciesHz, double[] levelsDb)
    {
        FrequenciesHz = frequenciesHz;
        LevelsDb = levelsDb;
    }

    /// <summary>Strictly ascending, positive.</summary>
    public double[] FrequenciesHz { get; }

    public double[] LevelsDb { get; }

    public bool WrittenByRew { get; private init; }

    public string? MeasurementName { get; private init; }

    public string? Source { get; private init; }

    /// <summary>The header's smoothing verbatim ("None", "1/6 octave"); null when the file states none.</summary>
    public string? Smoothing { get; private init; }

    public bool StatesSmoothing =>
        Smoothing != null && !Smoothing.Equals("None", StringComparison.OrdinalIgnoreCase);

    /// <summary>The level column's header ("SPL(dB)"); null without a column line.</summary>
    public string? LevelColumn { get; private init; }

    public int? SampleRateHz { get; private init; }

    /// <summary>The header's date verbatim.</summary>
    public string? Dated { get; private init; }

    /// <summary>Points per octave across the file's own span; how finely it resolves the response.</summary>
    public double PointsPerOctave =>
        FrequenciesHz.Length < 2
            ? 0.0
            : (FrequenciesHz.Length - 1) / Math.Log2(FrequenciesHz[^1] / FrequenciesHz[0]);

    /// <summary>Points per octave inside [<paramref name="lowHz"/>, <paramref name="highHz"/>]; the coarsest
    /// octave counts, because a linear export is dense at the top and sparse at the bottom.</summary>
    public double CoarsestPointsPerOctave(double lowHz, double highHz)
    {
        double coarsest = double.PositiveInfinity;
        for (double octave = Math.Max(lowHz, FrequenciesHz[0]);
             octave * 2.0 <= Math.Min(highHz, FrequenciesHz[^1]) * 1.000001;
             octave *= 2.0)
        {
            double top = octave * 2.0;
            int count = FrequenciesHz.Count(hz => hz >= octave && hz < top);
            coarsest = Math.Min(coarsest, count);
        }

        return double.IsPositiveInfinity(coarsest) ? PointsPerOctave : coarsest;
    }

    public static bool TryParse(string text, out FrequencyResponseTextFile? file, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(text);
        file = null;
        problem = null;

        bool rew = false;
        string? measurement = null;
        string? source = null;
        string? smoothing = null;
        string? levelColumn = null;
        string? levelUnit = null;
        string? dated = null;
        int? sampleRate = null;
        var frequencies = new List<double>();
        var levels = new List<double>();

        foreach (string rawLine in text.TrimStart('﻿').Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] is '*' or '#')
            {
                string note = line.TrimStart('*', '#').Trim();
                if (note.Contains(ImpulseResponseMarker, StringComparison.OrdinalIgnoreCase))
                {
                    problem = "this is REW's impulse-response export, not a frequency response " +
                        "(export the measurement as text instead)";
                    return false;
                }

                if (note.Contains("by REW", StringComparison.OrdinalIgnoreCase))
                {
                    rew = true;
                }

                if (Value(note, "Measurement:") is { } name)
                {
                    measurement = name;
                }
                else if (Value(note, "Source:") is { } sourceName)
                {
                    source = sourceName;
                }
                else if (Value(note, "Smoothing:") is { } smoothed)
                {
                    smoothing = smoothed;
                }
                else if (Value(note, "Dated:") is { } date)
                {
                    dated = date;
                }
                else if (Value(note, "Format:") is { } format)
                {
                    sampleRate = ReadSampleRate(format) ?? sampleRate;
                }
                else if (ColumnLabel(note) is { } column)
                {
                    (levelColumn, levelUnit) = column;
                }

                continue;
            }

            string[] tokens = Split(line);
            if (tokens.Length >= 2 &&
                EqTextNumbers.TryParse(tokens[0], out double hz) &&
                EqTextNumbers.TryParse(tokens[1], out double level))
            {
                if (!(hz > 0) || !double.IsFinite(hz) || !double.IsFinite(level))
                {
                    continue;
                }

                if (frequencies.Count > 0 && hz <= frequencies[^1])
                {
                    problem = $"the frequencies do not ascend at {hz.ToString("0.###", CultureInfo.InvariantCulture)} Hz";
                    return false;
                }

                frequencies.Add(hz);
                levels.Add(level);
                continue;
            }

            if (frequencies.Count > 0)
            {
                problem = $"unexpected line in the data: \"{Excerpt(line)}\"";
                return false;
            }

            // An unmarked header row, as other tools write one.
            if (ColumnLabel(line) is { } header)
            {
                (levelColumn, levelUnit) = header;
            }
        }

        // Only a stated unit refuses: "Magnitude" says nothing, "Impedance(ohms)" or "Level V" says it is not a level in dB.
        if (levelUnit != null && !levelUnit.Contains("dB", StringComparison.OrdinalIgnoreCase))
        {
            problem = $"the second column is \"{levelColumn}\" in {levelUnit}, not a level in dB " +
                "(export the frequency response, not impedance, group delay or distortion)";
            return false;
        }

        if (frequencies.Count < 2)
        {
            problem = "the file holds fewer than two \"frequency level\" rows";
            return false;
        }

        file = new FrequencyResponseTextFile(frequencies.ToArray(), levels.ToArray())
        {
            WrittenByRew = rew,
            MeasurementName = measurement,
            Source = source,
            Smoothing = smoothing,
            LevelColumn = levelColumn,
            SampleRateHz = sampleRate,
            Dated = dated
        };
        return true;
    }

    private static string[] Split(string line)
    {
        // "20.5, 75.3": a comma beside a separator belongs to the separator.
        string[] tokens = line.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim(','))
            .Where(token => token.Length > 0)
            .ToArray();
        // "20.5,75.3": a comma separates columns only when nothing else does.
        return tokens.Length == 1
            ? line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : tokens;
    }

    private static string? Value(string note, string key) =>
        note.StartsWith(key, StringComparison.OrdinalIgnoreCase)
            ? note[key.Length..].Trim() is { Length: > 0 } value ? value : null
            : null;

    // "Freq(Hz) SPL(dB) Phase(degrees)" → ("SPL(dB)", "dB"); "Frequency Level V" → ("Level", "V"); "Frequency Magnitude" → ("Magnitude", null).
    private static (string Label, string? Unit)? ColumnLabel(string note)
    {
        var columns = new List<string>();
        foreach (string token in note.Split([' ', '\t', ';', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            // A unit written apart belongs to the name before it.
            if (token.StartsWith('(') && columns.Count > 0)
            {
                columns[^1] += " " + token;
            }
            else
            {
                columns.Add(token);
            }
        }

        if (columns.Count < 2 || !columns[0].StartsWith("Freq", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? unit = Unit(columns[1]) ??
            (columns.Count > 2 && BareUnits.Contains(columns[2]) ? columns[2] : null);
        return (columns[1], unit);
    }

    // "SPL(dB)" → "dB"; null without parentheses.
    private static string? Unit(string? column)
    {
        int open = column?.LastIndexOf('(') ?? -1;
        int close = column?.LastIndexOf(')') ?? -1;
        return open >= 0 && close > open ? column![(open + 1)..close] : null;
    }

    // "Imported Impulse Response, 96000.0 Hz sampling".
    private static int? ReadSampleRate(string format)
    {
        int unit = format.IndexOf(" Hz sampling", StringComparison.OrdinalIgnoreCase);
        if (unit <= 0)
        {
            return null;
        }

        int start = format.LastIndexOfAny([' ', ','], unit - 1) + 1;
        return EqTextNumbers.TryParse(format[start..unit], out double rate) && rate >= 1 && rate < 10_000_000
            ? (int)Math.Round(rate)
            : null;
    }

    private static string Excerpt(string line) =>
        line.Length <= 60 ? line : line[..57] + "...";
}
