using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardHeadroomTests
{
    private const int SampleRate = 48_000;

    // An RBJ bell reaches exactly its gain at its centre, so the chain's gain plus the bell's is the truth.
    [Fact]
    public void AHandedOffChannelsGainCountsTowardsTheHeadroom_NotTowardsTheEqBoost()
    {
        var session = new EqWizardSession();
        session.Load(Handoff(new DspChannelChain(GainDb: 4)));
        var eq = new EqualizationCurve([new PeqBand(1_234.5, 8, 3)]);

        double headroom = EqWizardHeadroom.Of(EqWizardHeadroom.Input(session, eq)!);

        Assert.Equal(-7, headroom, 2);
        Assert.Equal(3, EqWizardRender.Stats(session, EqWizardRender.RenderSet(session, eq), eq, headroom)!.PeakBoostDb, 1);
    }

    [Fact]
    public void WithoutAChannelChain_TheHeadroomIsTheBanksOwn()
    {
        var session = new EqWizardSession();
        var eq = new EqualizationCurve([new PeqBand(1_234.5, 8, 3)], preampDb: -5);

        Assert.Equal(2, EqWizardHeadroom.Of(EqWizardHeadroom.Input(session, eq)!), 2);
    }

    [Fact]
    public void TheSheetStatesTheWholeChainsHeadroom()
    {
        var session = new EqWizardSession();
        session.Load(Handoff(new DspChannelChain(GainDb: 4)));
        session.Bank.Load([new PeqBand(1_234.5, 8, 3)], 0);

        EqTuneStats stats = Assert.IsType<EqTuneStats>(EqWizardRender.CurrentStats(session));

        Assert.Equal(-7, stats.HeadroomDb!.Value, 2);
    }

    [Fact]
    public void AChainWithoutAFir_IsReadAtOnce()
    {
        var reader = new EqWizardHeadroom();
        var key = new DspChainResponseKey(new DspChannelChain(GainDb: -3), SampleRate);

        Assert.Equal(3, reader.Read(key)!.Value, 6);
    }

    [Fact]
    public async Task AFirChain_LandsInTheBackground_AndOnlyTheLatestRequestLands()
    {
        var reader = new EqWizardHeadroom();
        DspChainResponseKey older = FirKey(gainDb: -1);
        DspChainResponseKey latest = FirKey(gainDb: -4);
        var landed = new List<double>();

        Assert.Null(reader.Read(older));
        Assert.Null(reader.Read(latest));
        await reader.FillAsync(landed.Add);

        double value = Assert.Single(landed);
        Assert.Equal(EqWizardHeadroom.Of(latest), value, 9);
        Assert.Equal(value, reader.Read(latest)!.Value, 9);
    }

    private static DspChainResponseKey FirKey(double gainDb)
    {
        double[] kernel = new double[64];
        kernel[32] = 1;
        return new DspChainResponseKey(new DspChannelChain(GainDb: gainDb, Fir: new FirFilter(kernel)), SampleRate);
    }

    private static EqWizardCurveSource Handoff(DspChannelChain chain)
    {
        var response = new Complex[4_096];
        response[600] = Complex.One;
        return new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.VirtualDspChannel,
            DisplayName = "Ch C · R",
            Description = "Through its own chain.",
            Measurement = new ImpulseMeasurementView(response, 600, SampleRate),
            PreviewImpulseResponse = response,
            PreviewChain = chain,
            SampleRateHz = SampleRate,
            CurveKind = AnalysisCurveKind.Primary
        };
    }
}
