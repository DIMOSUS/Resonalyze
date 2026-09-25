using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardSheetStatsTests
{
    private const int SampleRate = 48_000;

    [Fact]
    public async Task UnderBypass_TheSheetsStatisticsAreTheBanksItPrints_NotTheBareSources()
    {
        var session = new EqWizardSession();
        session.Load(GatedHandoff());
        session.Bank.Load([new PeqBand(1_000, 1.0, -6.0)], 0);
        await LandPreview(session);
        EqTuneStats expected = Assert.IsType<EqTuneStats>(EqWizardRender.CurrentStats(session));

        session.SetBypass(true);
        // As the plot does under Bypass: the landed preview is now the empty bank's.
        await LandPreview(session);
        EqTuneStats sheet = Assert.IsType<EqTuneStats>(EqWizardRender.CurrentStats(session));

        Assert.True(expected.PeakCutDb < -5.5);
        Assert.Equal(expected.PeakCutDb, sheet.PeakCutDb, 9);
        // The window holds points, so both errors are stated.
        Assert.Equal(expected.RmsErrorDb!.Value, sheet.RmsErrorDb!.Value, 6);
        Assert.Equal(expected.MaxErrorDb!.Value, sheet.MaxErrorDb!.Value, 6);
    }

    private static async Task LandPreview(EqWizardSession session)
    {
        Task? preview = session.Previews.RequestGatedPreview(EqWizardRender.DisplayedEq(session));
        Assert.NotNull(preview);
        await preview;
    }

    private static EqWizardCurveSource GatedHandoff()
    {
        var response = new Complex[16_384];
        for (int i = 0; i < 96; i++)
        {
            response[600 + i] = Math.Exp(-i / 20.0) * Math.Cos(2 * Math.PI * i / 24.0);
        }

        return new EqWizardCurveSource
        {
            Kind = EqWizardSourceKind.VirtualDspChannel,
            DisplayName = "Ch C · R",
            Description = "Through its own chain, as the panel drew it.",
            Measurement = new ImpulseMeasurementView(response, 600, SampleRate),
            PreviewImpulseResponse = response,
            PreviewChain = DspChannelChain.Identity,
            GateSettings = new PhaseAnalysisSettings(
                PhaseWindowMode.Fixed,
                PhaseAnalysisSettings.DefaultFdwCycles,
                PhaseDetrendMode.Off,
                ManualDetrendMilliseconds: 0.0,
                GateOffsetMs: 600 * 1_000.0 / SampleRate,
                LeftMs: 1,
                PlateauMs: 30,
                RightMs: 10,
                Unwrap: false,
                SmoothingInverseOctaves: 0.0),
            SampleRateHz = SampleRate,
            CurveKind = AnalysisCurveKind.Primary
        };
    }
}
