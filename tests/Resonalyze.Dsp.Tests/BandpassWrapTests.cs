namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The zero-padding contract of the band-limited read: filtering a CUT of a
/// longer record must not wrap that cut's tail onto its own head.
/// </summary>
/// <remarks>
/// <see cref="BandpassWindow.Apply"/> states the requirement in its own doc, and
/// the arrival search is its most exposed caller: it walks the head of the
/// record looking for the FIRST arrival, so a skirt folded back from the tail is
/// read as an arrival before the driver played. The failure is not subtle —
/// unpadded, a lone impulse 64 samples from the end of a 32768-sample buffer
/// puts 65 % of its own peak into the first 40 ms, where nothing was played.
/// <para>
/// A length that is already a power of two is the case worth pinning: it needs
/// the padding exactly as much as any other, and it is the one an
/// implementation keyed on <c>NextPowerOfTwo(count)</c> alone silently skips —
/// <see cref="VirtualCrossoverAnalysis.ChainValidRange"/> hands out a crop of
/// exactly the record length whenever the chain delay is a whole number of
/// samples, zero included.
/// </para>
/// </remarks>
public sealed class BandpassWrapTests
{
    private const int SampleRate = 48_000;

    // Narrow and low: the kernel is longest where the band is lowest, so this is
    // where a short transform wraps hardest.
    private const double LowHz = 27.5;
    private const double HighHz = 110.0;

    [Theory]
    [InlineData(32_768)]
    [InlineData(65_536)]
    public void BandLimitedRead_DoesNotWrapTheTailOntoTheHead(int length)
    {
        var impulseResponse = new System.Numerics.Complex[length];
        // Near the very end, so anything appearing at the start can only have
        // come around the buffer.
        impulseResponse[length - 64] = 1.0;

        TimeAlignmentAnalysisResult result =
            VirtualCrossoverAnalysis.AnalyzeBandLimitedArrival(
                impulseResponse,
                SampleRate,
                LowHz,
                HighHz,
                new ValidSampleRange(0, length));

        double[] envelope = result.EnvelopeSamples;
        Assert.NotEmpty(envelope);
        double peak = envelope.Max();
        Assert.True(peak > 0, "the band carried no energy at all");
        // The first 40 ms is entirely before the impulse: the kernel's own
        // skirt reaches backwards from the tail, not forwards to here.
        int head = 40 * SampleRate / 1000;
        double headPeak = envelope.Take(head).Max();
        Assert.True(
            headPeak < peak * 0.01,
            $"the head holds {headPeak / peak * 100:0.0}% of the peak — the tail " +
            "wrapped around the transform");
    }
}
