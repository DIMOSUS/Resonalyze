using System.Globalization;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqBandHandlesTests
{
    [Theory]
    [InlineData(PeqBandType.Peaking, -6.0)]
    [InlineData(PeqBandType.LowShelf, -3.0)]
    [InlineData(PeqBandType.HighShelf, -3.0)]
    [InlineData(PeqBandType.AllPassFirstOrder, 0.0)]
    [InlineData(PeqBandType.AllPassSecondOrder, 0.0)]
    public void TheHandle_SitsOnTheBandsOwnCurveAtItsFrequency(PeqBandType type, double levelDb)
    {
        var band = new PeqBand(1000, 0.7, -6, type);

        Assert.Equal(levelDb, EqBandHandles.LevelDb(band));
        if (!type.IsAllPass())
        {
            Assert.Equal(levelDb, band.MagnitudeDbAt(1000), 6);
        }
    }

    [Theory]
    [InlineData(PeqBandType.Peaking, 4.0)]
    [InlineData(PeqBandType.HighShelf, 8.0)]
    [InlineData(PeqBandType.AllPassSecondOrder, -6.0)]
    public void MovingTheHandle_PutsItsLevelBackOnTheCurve(PeqBandType type, double gainDb)
    {
        var band = new PeqBand(1000, 0.7, -6, type);

        PeqBand moved = EqBandHandles.MoveTo(band, 2500, 4);

        Assert.Equal(new PeqBand(2500, 0.7, gainDb, type), moved);
    }

    [Fact]
    public void AWheelNotch_StepsQByASixthOfAnOctave_AndAtLeastOneFieldStep()
    {
        Assert.Equal(5.61, EqBandHandles.StepQ(new PeqBand(1000, 5, -3), 1).Q, 2);
        Assert.Equal(4.45, EqBandHandles.StepQ(new PeqBand(1000, 5, -3), -1).Q, 2);
        Assert.Equal(0.6, EqBandHandles.StepQ(new PeqBand(1000, 0.5, -3), 1).Q, 9);
        Assert.Equal(0.3, EqBandHandles.StepQ(new PeqBand(1000, 0.5, -3), -2).Q, 9);
    }

    [Fact]
    public void AFirstOrderAllPass_HasNoQForTheWheel()
    {
        var band = new PeqBand(1000, 1, 0, PeqBandType.AllPassFirstOrder);

        Assert.False(EqBandHandles.HasQ(band.Type));
        Assert.Equal(band, EqBandHandles.StepQ(band, 3));
    }

    [Theory]
    [InlineData(PeqBandType.Peaking, "3 PK   1250 Hz   -4.5 dB   Q 2.8")]
    [InlineData(PeqBandType.LowShelf, "3 LS   1250 Hz   -4.5 dB   Q 2.8")]
    [InlineData(PeqBandType.AllPassSecondOrder, "3 AP2   1250 Hz   Q 2.8")]
    [InlineData(PeqBandType.AllPassFirstOrder, "3 AP1   1250 Hz")]
    public void TheReadout_NamesWhatTheStripShows(PeqBandType type, string expected)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, EqBandHandles.Readout(3, new PeqBand(1250, 2.8, -4.5, type)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
