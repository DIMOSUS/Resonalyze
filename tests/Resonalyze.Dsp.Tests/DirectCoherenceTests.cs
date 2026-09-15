using System.Numerics;
using System.Text;

namespace Resonalyze.Dsp.Tests;

/// <summary>The direct-sound cut (<see cref="VirtualCrossoverAnalysis.CutDirectSound"/>) behind 'PHAT direct' and the direct-coherence witness.</summary>
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
        // A reflection owning the whitened extremum is the failure the cut prevents.
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
        // An in-band artifact before the valid range: the cut must honour the range and land on the real front.
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

        Assert.True(
            blind[artifactAt].Magnitude > 0.5,
            "without the range the artifact should anchor the cut");
        Assert.Equal(0.0, blind[BasePosition].Magnitude, 6);
        Assert.Equal(0.0, guarded[artifactAt].Magnitude, 6);
        Assert.Equal(1.0, guarded[BasePosition].Magnitude, 2);
    }

    [Fact]
    public void Compute_WeighsTheDirectCoherenceOnAPolarityTie()
    {
        // Split corners (LP 1500 / HP 1700, BW48) leave a lobe/polarity-partner near-tie: the witness must run,
        // and the settled lobe must never lose the direct-coherence comparison by the witness's acting margin.
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
