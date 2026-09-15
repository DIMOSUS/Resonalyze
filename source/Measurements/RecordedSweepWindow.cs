namespace Resonalyze;

/// <summary><see cref="ExcitationStart"/> lies inside the span; completeness is judged from it, since the span counts lead-in too.</summary>
internal readonly record struct RecordedSweepSpan(int Start, int Length, int ExcitationStart)
{
    public int ExcitationLength => Start + Length - ExcitationStart;
}

/// <summary>Cuts a recording to excitation plus decay: every FFT is sized by its input. See docs/tech/sweep-measurement.md#importing-a-recorded-sweep.</summary>
internal static class RecordedSweepWindow
{
    /// <summary>Room for the gate's left shoulder before the arrival.</summary>
    private const double LeadInSeconds = 0.5;

    /// <summary>0.5 s covers a cabin, 2 s a live room.</summary>
    private const double TailSeconds = 2.0;

    /// <summary>Candidate spans, best match first, one attempt each; never empty (degenerate cases fall back to the bounded recording).</summary>
    public static IReadOnlyList<RecordedSweepSpan> LocateCandidates(
        float[] samples,
        float[] sweep,
        int sampleRate,
        int maximumCandidates = 3)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCandidates);
        if (sampleRate <= 0 || sweep.Length == 0)
        {
            return [new RecordedSweepSpan(0, samples.Length, 0)];
        }

        IReadOnlyList<SweepMatch> matches =
            RecordedSweepDetector.FindSweeps(samples, sweep, maximumCandidates);
        int lead = (int)(LeadInSeconds * sampleRate);
        int bound = sweep.Length + lead + (int)(TailSeconds * sampleRate);
        var spans = new List<RecordedSweepSpan>();
        foreach (SweepMatch match in matches)
        {
            // Cut away other takes (they read as huge reflections); only non-overlapping ones, since matches are half a sweep apart.
            int head = 0;
            int limit = samples.Length;
            foreach (SweepMatch other in matches)
            {
                if (other.Start >= match.Start + sweep.Length)
                {
                    limit = Math.Min(limit, other.Start);
                }
                else if (other.Start + sweep.Length <= match.Start)
                {
                    head = Math.Max(head, other.Start + sweep.Length);
                }
            }

            int start = Math.Clamp(match.Start - lead, head, samples.Length);
            int length = Math.Min(bound, limit - start);
            if (length > 0)
            {
                spans.Add(new RecordedSweepSpan(start, length, match.Start));
            }
        }

        return spans.Count > 0
            ? spans
            : [new RecordedSweepSpan(0, Math.Min(bound, samples.Length), 0)];
    }
}
