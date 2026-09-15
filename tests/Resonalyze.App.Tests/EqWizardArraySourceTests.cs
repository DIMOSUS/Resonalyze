using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardArraySourceTests
{
    private const int SampleRate = 48_000;
    private static readonly IReadOnlyList<double> Grid = SpatialAverage.BuildGrid();

    private static ImpulseResponseFile FileWith(
        params IReadOnlyList<double>[] microphoneLevelsDb)
    {
        var microphones = new List<ImpulseResponseFile.ArrayMicrophoneFileEntry>();
        for (int i = 0; i < microphoneLevelsDb.Length; i++)
        {
            microphones.Add(new ImpulseResponseFile.ArrayMicrophoneFileEntry
            {
                ChannelOffset = i,
                IsMeasurementMicrophone = i == 0,
                AcceptedRunCount = 1,
                LevelsDb = microphoneLevelsDb[i].ToArray()
            });
        }

        return new ImpulseResponseFile
        {
            SampleRate = SampleRate,
            ArrayMicrophones = new ImpulseResponseFile.ArrayMicrophonesFileEntry
            {
                GridStartHz = Grid[0],
                GridStopHz = Grid[^1],
                Microphones = microphones
            }
        };
    }

    private static VirtualCrossoverCalibrationSettings Calibration(double correctionDb) =>
        VirtualCrossoverCalibrationSettings.From(
            CalibrationFile.FromPoints(
                [
                    new CalibrationPoint(20.0, correctionDb),
                    new CalibrationPoint(20_000.0, correctionDb)
                ],
                "flat"),
            $"flat {correctionDb:0.#}",
            null);

    private static ImpulseResponseFile FileWith(
        IReadOnlyList<double[]> microphoneLevelsDb,
        IReadOnlyList<VirtualCrossoverCalibrationSettings?> calibrations)
    {
        var microphones = new List<ImpulseResponseFile.ArrayMicrophoneFileEntry>();
        for (int i = 0; i < microphoneLevelsDb.Count; i++)
        {
            microphones.Add(new ImpulseResponseFile.ArrayMicrophoneFileEntry
            {
                ChannelOffset = i,
                IsMeasurementMicrophone = i == 0,
                AcceptedRunCount = 1,
                LevelsDb = microphoneLevelsDb[i].ToArray(),
                Calibration = calibrations[i]
            });
        }

        return new ImpulseResponseFile
        {
            SampleRate = SampleRate,
            ArrayMicrophones = new ImpulseResponseFile.ArrayMicrophonesFileEntry
            {
                GridStartHz = Grid[0],
                GridStopHz = Grid[^1],
                Microphones = microphones
            }
        };
    }

    private static double[] Flat(double levelDb) =>
        Enumerable.Repeat(levelDb, Grid.Count).ToArray();

    private static (double[] First, double[] Second, int Band) Disagreeing(double spreadDb)
    {
        int band = Grid.Count / 2;
        double[] first = Flat(70.0);
        double[] second = Flat(70.0);
        second[band] = 70.0 - spreadDb;
        return (first, second, band);
    }

    [Fact]
    public void AMeasurementWithoutAnArrayOffersNothing() =>
        Assert.Null(EqWizardSourceResolver.TryCreateFromArray(
            new ImpulseResponseFile { SampleRate = SampleRate }, "m", "d"));

    [Fact]
    public void TheSourceIsASpatialAverageNamedAfterTheMeasurement()
    {
        EqWizardCurveSource? source = EqWizardSourceResolver.TryCreateFromArray(
            FileWith(Flat(70.0), Flat(70.0)), "cabin sweep", "description");

        Assert.NotNull(source);
        Assert.Equal(EqWizardSourceKind.SpatialAverage, source!.Kind);
        Assert.Equal("cabin sweep", source.DisplayName);
        Assert.Equal("description", source.Description);
        Assert.Equal(SampleRate, source.SampleRateHz);
    }

    [Fact]
    public void TheCurveIsTheAverageOfThePositions()
    {
        EqWizardCurveSource source = EqWizardSourceResolver.TryCreateFromArray(
            FileWith(Flat(70.0), Flat(70.0)), "m", "d")!;

        Assert.Equal(Grid.Count, source.Points.Count);
        Assert.All(source.Points, point => Assert.Equal(70.0, point.Y, 6));
    }

    [Fact]
    public void PositionsThatAgreeMayBeBoosted()
    {
        (double[] first, double[] second, int band) = Disagreeing(12.0);

        EqWizardCurveSource source =
            EqWizardSourceResolver.TryCreateFromArray(FileWith(first, second), "m", "d")!;

        // Owner's seven-position sets sit at a median 11-12 dB spread; a gate refusing 12 dB refuses most measurements.
        Assert.NotNull(source.Coherence);
        Assert.Equal(1.0, source.Coherence![band].Y);
    }

    [Fact]
    public void PositionsThatDisagreeWildlyMayNot()
    {
        (double[] first, double[] second, int band) = Disagreeing(25.0);

        EqWizardCurveSource source =
            EqWizardSourceResolver.TryCreateFromArray(FileWith(first, second), "m", "d")!;

        // The average follows the loudest position; filling the other's dip spends every seat's headroom.
        Assert.Equal(0.0, source.Coherence![band].Y);
        Assert.Equal(1.0, source.Coherence[0].Y);
        Assert.Equal(Grid[band], source.Coherence[band].X, 6);
    }

    [Fact]
    public void ALoneMicrophoneIsNotOfferedAsASpatialAverage()
    {
        // One position is a point measurement under a volume's name, and its NaN spread leaves the gate nothing to gate on.
        Assert.Null(EqWizardSourceResolver.TryCreateFromArray(FileWith(Flat(70.0)), "m", "d"));
    }

    [Fact]
    public void ABandOnlyOneMicrophoneMeasuredRefusesABoost()
    {
        // One band with only one position's level: a non-finite confidence must not read as permission.
        double[] first = Flat(70.0);
        double[] second = Flat(70.0);
        second[5] = double.NaN;

        EqWizardCurveSource source =
            EqWizardSourceResolver.TryCreateFromArray(FileWith(first, second), "m", "d")!;

        Assert.NotNull(source.Coherence);
        Assert.True(double.IsFinite(source.Points[5].Y), "the average is still a level");
        Assert.Equal(0.0, source.Coherence![5].Y);
        Assert.Equal(1.0, source.Coherence[0].Y);
    }

    [Fact]
    public void AMixedArrayOffersOnlyTheCalibrationsItCanApplyExactly()
    {
        // Own and Off are exact because the correction is measured (corrected minus raw average).
        var mixed = new[] { Calibration(-2.0), Calibration(3.0) };
        var matched = new[] { Calibration(-2.0), Calibration(-2.0) };

        EqWizardCurveSource source = EqWizardSourceResolver.TryCreateFromArray(
            FileWith([Flat(70.0), Flat(70.0)], mixed), "m", "d")!;
        Assert.True(source.CalibrationIsAggregate);
        Assert.True(source.HasOwnCalibration, "own and off both stay available");

        EqWizardCurveSource shared = EqWizardSourceResolver.TryCreateFromArray(
            FileWith([Flat(70.0), Flat(70.0)], matched), "m", "d")!;
        Assert.False(
            shared.CalibrationIsAggregate,
            "one shared file is a correction that can be swapped exactly");
    }

    [Fact]
    public void AnUnmeasuredBandStaysUnmeasured()
    {
        double[] first = Flat(70.0);
        double[] second = Flat(70.0);
        first[0] = double.NaN;
        second[0] = double.NaN;

        EqWizardCurveSource source =
            EqWizardSourceResolver.TryCreateFromArray(FileWith(first, second), "m", "d")!;

        Assert.False(double.IsFinite(source.Points[0].Y));
        Assert.True(double.IsFinite(source.Points[1].Y));
    }

    [Fact]
    public void TheSpreadComesOutBesideTheAverage()
    {
        (double[] first, double[] second, int band) = Disagreeing(9.0);

        (LiveCaptureDocument? document, double[]? spread) =
            ArrayCaptureDocument.TryCreateWithSpread(
                FileWith(first, second).ArrayMicrophones!.ToCurves(), SampleRate, null);

        Assert.NotNull(document);
        Assert.NotNull(spread);
        Assert.Equal(Grid.Count, spread!.Length);
        Assert.Equal(9.0, spread[band], 6);
        Assert.Equal(0.0, spread[0], 6);
    }
}
