using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardStatsTests
{
    [Fact]
    public void AWindowWithNoPointOfTheCurve_GradesNoError()
    {
        var session = new EqWizardSession();
        session.Load(Curve(20, 200));
        session.SetWindowFrom(1_000);
        session.SetWindowTo(10_000);

        EqTuneStats stats = EqWizardRender.CurrentStats(session)!;

        Assert.Null(stats.RmsErrorDb);
        Assert.Null(stats.MaxErrorDb);
    }

    [Fact]
    public void AWindowOverTheCurve_GradesItsError()
    {
        var session = new EqWizardSession();
        session.Load(Curve(20, 20_000));
        session.SetWindowFrom(100);
        session.SetWindowTo(10_000);

        EqTuneStats stats = EqWizardRender.CurrentStats(session)!;

        Assert.NotNull(stats.RmsErrorDb);
        Assert.NotNull(stats.MaxErrorDb);
    }

    private static EqWizardCurveSource Curve(double lowHz, double highHz) => new()
    {
        Kind = EqWizardSourceKind.TextCurve,
        DisplayName = "curve",
        Description = "test",
        Points = Enumerable.Range(0, 100)
            .Select(index => lowHz * Math.Pow(highHz / lowHz, index / 99.0))
            .Select(hz => new SignalPoint(hz, 0))
            .ToList(),
        Scale = MagnitudeScale.Relative
    };
}
