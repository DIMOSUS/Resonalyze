using System.Drawing;
using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>Synthetic drivers for the crossover wizard: a flat band with 24 dB/oct skirts.</summary>
internal static class AutoSetupWizardFixtures
{
    public const double SampleRate = 48_000;

    public static List<SignalPoint> BandCurve(double lowHz, double highHz, double levelDb = 0)
    {
        var points = new List<SignalPoint>();
        foreach (double frequency in EqualizationCurve.LogFrequencyGrid(20, 20_000, 512))
        {
            double y = levelDb;
            if (frequency < lowHz)
            {
                y -= 24.0 * Math.Log2(lowHz / frequency);
            }
            else if (frequency > highHz)
            {
                y -= 24.0 * Math.Log2(frequency / highHz);
            }

            points.Add(new SignalPoint(frequency, y));
        }

        return points;
    }

    public static AutoSetupWizardChannel Channel(
        string name,
        VirtualCrossoverAlignmentStage group,
        double lowHz,
        double highHz,
        double levelDb = 0,
        double? highPassHz = null,
        double? lowPassHz = null,
        Complex[]? impulseResponse = null)
    {
        List<SignalPoint> curve = BandCurve(lowHz, highHz, levelDb);
        return new AutoSetupWizardChannel(
            name,
            Color.White,
            group,
            curve,
            null,
            null,
            CrossoverAutoSetup.EstimateBand(curve),
            highPassHz,
            lowPassHz,
            impulseResponse);
    }

    public static IReadOnlyList<AutoSetupWizardChannel> FourWay() =>
    [
        Channel("A sub", VirtualCrossoverAlignmentStage.FrontChain, 20, 90),
        Channel("B midbass", VirtualCrossoverAlignmentStage.FrontChain, 60, 900),
        Channel("C mid", VirtualCrossoverAlignmentStage.FrontChain, 250, 6_000),
        Channel("D tweeter", VirtualCrossoverAlignmentStage.FrontChain, 2_200, 20_000)
    ];

    // Handed in panel order; both subs measure the same, so only their preset corners order them.
    public static IReadOnlyList<AutoSetupWizardChannel> ReferenceCar() =>
    [
        Channel("A tweeter", VirtualCrossoverAlignmentStage.FrontChain, 2_200, 20_000),
        Channel("B mid", VirtualCrossoverAlignmentStage.FrontChain, 250, 6_000),
        Channel("C midbass", VirtualCrossoverAlignmentStage.FrontChain, 60, 900),
        Channel("D rear", VirtualCrossoverAlignmentStage.Rear, 120, 15_000, levelDb: 6),
        Channel("E center", VirtualCrossoverAlignmentStage.Center, 200, 18_000),
        Channel(
            "F front sub", VirtualCrossoverAlignmentStage.FrontChain, 20, 300,
            highPassHz: 50, lowPassHz: 110),
        Channel(
            "G rear sub", VirtualCrossoverAlignmentStage.FrontChain, 20, 300, lowPassHz: 50)
    ];

    public static AutoSetupWizardSession Session(IReadOnlyList<AutoSetupWizardChannel> channels) =>
        new(SampleRate, SampleRate, channels);

    /// <summary>What the dialog does after every change: a preview, whose measured elevation the session takes.</summary>
    public static AutoSetupPreview? Preview(AutoSetupWizardSession session)
    {
        AutoSetupPreview? preview = AutoSetupWizardFit.Preview(AutoSetupPreviewInputs.Of(session));
        if (preview != null)
        {
            session.TakeElevation(preview.ElevationCeiling, preview.ElevationValue);
        }

        return preview;
    }

    /// <summary>The magnitude-only proposal Apply writes, one per channel in the order handed in.</summary>
    public static CrossoverProposal[] Proposals(AutoSetupWizardSession session)
    {
        List<AutoSetupGroupFit>? fits = AutoSetupWizardFit.TryFit(session);
        Assert.NotNull(fits);
        return AutoSetupWizardFit.InInitOrder(fits!, session.Rows.Count);
    }
}
