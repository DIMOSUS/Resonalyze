using System.Numerics;
using System.Text;

namespace Resonalyze.Dsp.Tests;

/// <summary>
/// The direct-sound cut behind the correlation view's "PHAT direct" curve and
/// the alignment engine's direct-coherence witness
/// (<see cref="VirtualCrossoverAnalysis.CutDirectSound"/>), and the witness's
/// bearing on a junction search.
/// </summary>
public sealed class DirectCoherenceTests
{
    private const int SampleRate = 48_000;
    private const int IrLength = 16_384;
    private const int BasePosition = 2_048;

    private sealed class Channel(string name) : IAlignmentChannel
    {
        public string Name { get; } = name;
        public int SampleRate => DirectCoherenceTests.SampleRate;
        public int ProcessorSampleRate => SampleRate;
    }

    private static Complex[] Impulse(double offsetMs = 0, double amplitude = 1.0)
    {
        var ir = new Complex[IrLength];
        ir[BasePosition + (int)Math.Round(offsetMs / 1000.0 * SampleRate)] += amplitude;
        return ir;
    }

    [Fact]
    public void CutDirectSound_KeepsTheFrontAndDropsTheReflection()
    {
        // A direct front with a strong reflection 4 crossover periods behind
        // it (2.67 ms at 1.5 kHz): the cut must keep the front at full weight
        // and remove the reflection entirely — that reflection owning the
        // whitened extremum is the very failure the cut exists to prevent.
        Complex[] ir = Impulse();
        Complex[] reflection = Impulse(4.0 * 1000.0 / 1_500, 0.9);
        for (int i = 0; i < ir.Length; i++)
        {
            ir[i] += reflection[i];
        }

        Complex[] cut = VirtualCrossoverAnalysis.CutDirectSound(
            ir, SampleRate, 750, 3_000, 1_500);

        int reflectionAt = BasePosition + (int)Math.Round(
            4.0 / 1_500 * SampleRate);
        Assert.Equal(1.0, cut[BasePosition].Magnitude, 2);
        Assert.Equal(0.0, cut[reflectionAt].Magnitude, 6);
        // One period behind the front is still inside the two-period plateau.
        int onePeriod = BasePosition + (int)Math.Round(
            1.0 / 1_500 * SampleRate);
        Assert.True(
            Math.Abs(1.0 - VirtualCrossoverAnalysis.CutDirectSound(
                Impulse(1000.0 / 1_500), SampleRate, 750, 3_000, 1_500)[onePeriod]
                .Magnitude) < 0.01,
            "content one period behind the front must pass at full weight");
    }

    [Fact]
    public void CutDirectSound_HonorsTheValidRange()
    {
        // An in-band artifact ahead of the record's valid content — the shape
        // a chain's group-delay padding or a capture glitch leaves. Without
        // the range the front detector marks the artifact and the cut windows
        // the wrong event; with the range the artifact is outside the
        // analysis and the cut lands on the real front. The engine's
        // snapshots always carry the range — this pins that the cut actually
        // takes it.
        Complex[] ir = Impulse(amplitude: 1.0);              // real front
        Complex[] artifact = Impulse(-10.0, amplitude: 0.6); // 10 ms earlier
        for (int i = 0; i < ir.Length; i++)
        {
            ir[i] += artifact[i];
        }
        int artifactAt = BasePosition - (int)Math.Round(10.0 / 1000 * SampleRate);
        var validRange = new ValidSampleRange(
            BasePosition - (int)Math.Round(2.0 / 1000 * SampleRate), IrLength);

        Complex[] blind = VirtualCrossoverAnalysis.CutDirectSound(
            ir, SampleRate, 750, 3_000, 1_500);
        Complex[] guarded = VirtualCrossoverAnalysis.CutDirectSound(
            ir, SampleRate, 750, 3_000, 1_500, validRange);

        // Blind, the window opens on the artifact and the real front sits
        // 10 ms behind it — far outside a two-period cut at 1.5 kHz.
        Assert.True(
            blind[artifactAt].Magnitude > 0.5,
            "without the range the artifact should anchor the cut");
        Assert.Equal(0.0, blind[BasePosition].Magnitude, 6);
        // Guarded, the artifact region is invisible to the detector and the
        // cut holds the real front at full weight.
        Assert.Equal(0.0, guarded[artifactAt].Magnitude, 6);
        Assert.Equal(1.0, guarded[BasePosition].Magnitude, 2);
    }

    [Fact]
    public void Compute_WeighsTheDirectCoherenceOnAPolarityTie()
    {
        // The archived C/D geometry: split corners (LP 1500, HP 1700, both
        // 48 dB/oct Butterworth) leave the pair half an octave of usable
        // overlap, where the summation score reads a lobe and its polarity
        // partner as a near-tie. The witness must be ON at such a junction —
        // its verdict in the log — and the settled lobe must never LOSE the
        // direct-coherence comparison by the witness's own acting margin:
        // whichever way the tie fell, the direct wavefronts agreed to within
        // it. (What the witness is worth on junctions where the full record
        // and the direct sound disagree is the session battery's business —
        // rooms do that, chains alone do not.)
        var midChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.LowPass,
            new CrossoverEdge(CrossoverFilterFamily.Butterworth, 1_500, 48)));
        var twChain = new DspChannelChain(Crossover: new CrossoverSpec(
            CrossoverKind.HighPass,
            HighPassEdge: new CrossoverEdge(
                CrossoverFilterFamily.Butterworth, 1_700, 48)));
        Complex[] midSrc = Impulse();
        Complex[] twSrc = Impulse();
        var mid = new Channel("C");
        var tw = new Channel("D");

        AlignmentSnapshot Snap(
            Channel channel, Complex[] source, DspChannelChain chain,
            AlignmentOverride over)
        {
            Complex[] ir = VirtualCrossoverAnalysis.ApplyChain(
                source,
                chain with
                {
                    DelayMs = over.DelayMs,
                    InvertPolarity = over.InvertPolarity
                },
                SampleRate, SampleRate, out ValidSampleRange range);
            return new AlignmentSnapshot(
                channel, ir, VirtualCrossoverAnalysis.FindPeakIndex(ir), range);
        }

        IReadOnlyList<AlignmentSnapshot> Reprocess(
            IReadOnlyDictionary<IAlignmentChannel, AlignmentOverride> overrides) =>
            [
                Snap(mid, midSrc, midChain, overrides.GetValueOrDefault(mid)),
                Snap(tw, twSrc, twChain, overrides.GetValueOrDefault(tw))
            ];

        IReadOnlyList<AlignmentSnapshot> initial = Reprocess(
            new Dictionary<IAlignmentChannel, AlignmentOverride>());
        var junctions = new List<AlignmentJunction>
        {
            new(initial[0], initial[1], 1_500, 750, 3_000)
        };
        var alignment = new Dictionary<IAlignmentChannel, AlignmentOverride>();
        var log = new StringBuilder();
        AutoAlignmentEngine.Compute(initial, junctions, Reprocess, alignment, log);

        Assert.Contains("direct coherence", log.ToString());

        // Recompute the witness's own figures for the SETTLED state through
        // the public APIs: the applied lobe against its polarity partner half
        // a period away, both read on the direct cuts.
        IReadOnlyList<AlignmentSnapshot> final = Reprocess(alignment);
        List<SignalPoint> curve = VirtualCrossoverAnalysis.BandLimitedCorrelationCurve(
            VirtualCrossoverAnalysis.CutDirectSound(
                final[0].ImpulseResponse, SampleRate, 750, 3_000, 1_500),
            VirtualCrossoverAnalysis.CutDirectSound(
                final[1].ImpulseResponse, SampleRate, 750, 3_000, 1_500),
            SampleRate, 1_500, Math.Log2(3_000.0 / 750.0),
            searchRangeMs: 1.0, centerLagMs: 0, phaseTransform: true);
        double halfPeriodMs = 500.0 / 1_500;
        double Coherence(bool inverted, double centerMs) => curve
            .Where(point => Math.Abs(point.X - centerMs) <= halfPeriodMs / 2)
            .Select(point => inverted ? -point.Y : point.Y)
            .DefaultIfEmpty(double.NegativeInfinity)
            .Max();
        bool settledInverted =
            alignment.GetValueOrDefault(mid).InvertPolarity
            ^ alignment.GetValueOrDefault(tw).InvertPolarity;
        double settled = Coherence(settledInverted, 0);
        double partner = Math.Max(
            Coherence(!settledInverted, halfPeriodMs),
            Coherence(!settledInverted, -halfPeriodMs));
        Assert.True(
            settled > 0.85,
            $"the settled lobe's direct coherence reads only {settled:0.00}");
        Assert.True(
            partner - settled < 0.05,
            $"the settled lobe loses the direct comparison by " +
            $"{partner - settled:0.00} — past the witness's own acting margin");
    }
}
