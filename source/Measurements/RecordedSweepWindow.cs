namespace Resonalyze;

/// <summary>
/// The stretch of a recorded sweep file that is worth analyzing: the excitation
/// plus room for what arrives before and decays after it.
/// <see cref="ExcitationStart"/> is where the excitation itself was found, which
/// sits <em>inside</em> the span — the caller needs it to tell a complete take
/// from one the file cuts short, since the span's own length counts the lead-in
/// silence too.
/// </summary>
internal readonly record struct RecordedSweepSpan(int Start, int Length, int ExcitationStart)
{
    /// <summary>Samples available from where the excitation begins.</summary>
    public int ExcitationLength => Start + Length - ExcitationStart;
}

/// <summary>
/// Cuts a recording down to the part worth analyzing. The natural way to make one
/// is to start the recorder, walk to the listening position, play the sweep and
/// walk back, which leaves tens of seconds of silence on both sides — and every
/// FFT in the analysis is sized by the recording, not by the sweep. A five-minute
/// file holding a two-second sweep costs gigabytes of transient spectra and leaves
/// behind a transfer IR of the same length, which then has to be held in memory
/// and written into any saved measurement. Cutting the analysis down to the
/// excitation and its decay bounds all of that by the sweep's own length.
/// </summary>
internal static class RecordedSweepWindow
{
    /// <summary>
    /// Kept before the excitation, so the arrival does not land at index zero and
    /// the gate's left shoulder has somewhere to sit.
    /// </summary>
    private const double LeadInSeconds = 0.5;

    /// <summary>
    /// Kept after the excitation ends, for the decay that follows it. Half a
    /// second covers a car cabin and two seconds a live room; anything longer is
    /// below the noise floor of a recording made this way.
    /// </summary>
    private const double TailSeconds = 2.0;

    /// <summary>
    /// Spans that may hold the excitation, best match first. The sweep is located
    /// by matching it against the recording (see
    /// <see cref="RecordedSweepDetector"/>), so the span sits on where the
    /// excitation actually is rather than on where the recording happens to be
    /// loud. More than one is offered because a take can hold more than one
    /// attempt, and because a match is evidence rather than proof — the caller
    /// analyzes them in order and keeps the one that produces a credible impulse
    /// response.
    /// </summary>
    /// <remarks>
    /// Always returns at least one span. Degenerate cases — a silent file, no
    /// match at all, a recording already shorter than the bound — fall back to as
    /// much of the recording as the bound allows; the caller's credibility check
    /// is what judges the result either way.
    /// </remarks>
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
        // A recording that already fits the bound is analyzed whole — there is
        // nothing to cut away — but it still needs the excitation start, or a take
        // that holds a pre-roll and then RUNS OUT mid-sweep reads as long enough.
        // Every match is still offered: a short take can hold a complete sweep and
        // a louder half of a second attempt, and answering with only the strongest
        // would refuse the file over the one that was cut short.
        if (samples.Length <= bound)
        {
            foreach (SweepMatch match in matches)
            {
                if (!spans.Exists(span => span.ExcitationStart == match.Start))
                {
                    spans.Add(new RecordedSweepSpan(0, samples.Length, match.Start));
                }
            }

            return spans.Count > 0 ? spans : [new RecordedSweepSpan(0, samples.Length, 0)];
        }

        foreach (SweepMatch match in matches)
        {
            int start = Math.Clamp(match.Start - lead, 0, samples.Length);
            int length = Math.Min(bound, samples.Length - start);
            if (!spans.Exists(span => span.Start == start))
            {
                spans.Add(new RecordedSweepSpan(start, length, match.Start));
            }
        }

        return spans.Count > 0 ? spans : [new RecordedSweepSpan(0, bound, 0)];
    }
}
