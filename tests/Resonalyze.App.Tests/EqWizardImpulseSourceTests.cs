using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class EqWizardImpulseSourceTests
{
    [Fact]
    public void CreateFromImpulseResponse_LoopbackTransfer_EqualizesTheTransferIrWithCoherence()
    {
        ImpulseResponseFile file = BuildFile(
            SweepMeasurementMode.LoopbackTransfer,
            // An 8-sample IR pairs with 5 coherence bins; extraction yields k = 1..4.
            transferIr: EightSampleImpulse,
            transferPeakIndex: 1,
            coherence: [1.0, 0.95, 0.9, 0.8, 0.7]);

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromImpulseResponse(
            file, "cabin", "History: cabin");

        Assert.Equal(EqWizardSourceKind.ImpulseResponse, source.Kind);
        Assert.NotNull(source.Measurement);
        Assert.Equal(48_000, source.SampleRateHz);
        Assert.Equal(AnalysisCurveKind.Primary, source.CurveKind);
        Assert.NotNull(source.Coherence);
        Assert.True(source.Coherence!.Count >= 2);
        Assert.All(source.Coherence, point =>
        {
            Assert.True(point.X > 0);
            Assert.InRange(point.Y, 0.0, 1.0);
        });
    }

    [Fact]
    public void CreateFromImpulseResponse_SweepDeconvolution_HasNoCoherence()
    {
        ImpulseResponseFile file = BuildFile(
            SweepMeasurementMode.SweepDeconvolution,
            transferIr: null,
            transferPeakIndex: null,
            coherence: null);

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromImpulseResponse(
            file, "sweep", "History: sweep");

        Assert.NotNull(source.Measurement);
        Assert.Equal(48_000, source.SampleRateHz);
        Assert.Null(source.Coherence);
    }

    [Fact]
    public void CreateFromImpulseResponse_LoopbackTransferWithoutCoherence_StillHasNoCoherenceCurve()
    {
        ImpulseResponseFile file = BuildFile(
            SweepMeasurementMode.LoopbackTransfer,
            transferIr: EightSampleImpulse,
            transferPeakIndex: 1,
            coherence: null);

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromImpulseResponse(
            file, "cabin", "History: cabin");

        Assert.NotNull(source.Measurement);
        Assert.Null(source.Coherence);
    }

    private static readonly Complex[] EightSampleImpulse =
    [
        new(0, 0), new(1, 0), new(0, 0), new(0, 0),
        new(0, 0), new(0, 0), new(0, 0), new(0, 0)
    ];

    private static ImpulseResponseFile BuildFile(
        SweepMeasurementMode mode,
        Complex[]? transferIr,
        int? transferPeakIndex,
        double[]? coherence)
    {
        (double lowHz, double highHz) = ImpulseResponseFile.ResolveSweepBand(0, 0, 10, 48_000);
        var result = new MeasurementResult
        {
            SampleRate = 48_000,
            Bits = 24,
            LowFrequencyHz = lowHz,
            HighFrequencyHz = highHz,
            SweepDurationSeconds = 1.0,
            MeasurementMode = mode,
            SweepDeconvolution = new MeasurementImpulseResponse([new(0, 0), new(1, 0), new(0, 0), new(0, 0)], 1),
            Transfer = transferIr == null
                ? null
                : new MeasurementImpulseResponse(transferIr, transferPeakIndex ?? 0),
            TransferCoherence = coherence
        };
        return ImpulseResponseFile.From(result);
    }
}
