using System.Numerics;
using Resonalyze.Dsp;
using Resonalyze.History;

namespace Resonalyze.App.Tests;

// Covers the impulse-response factory of the EQ Wizard source resolver: the
// transfer-vs-sweep selection and the coherence extraction that gates Auto Tune boosts.
public sealed class EqWizardImpulseSourceTests
{
    [Fact]
    public void CreateFromImpulseResponse_LoopbackTransfer_EqualizesTheTransferIrWithCoherence()
    {
        ImpulseResponseFile file = BuildFile(
            SweepMeasurementMode.LoopbackTransfer,
            // An 8-sample transfer IR pairs with a 5-bin (8/2 + 1) coherence array;
            // extraction reads fftLength 8 and yields four points (k = 1..4).
            transferIr: EightSampleImpulse,
            transferPeakIndex: 1,
            coherence: [1.0, 0.95, 0.9, 0.8, 0.7]);

        EqWizardCurveSource source = EqWizardSourceResolver.CreateFromImpulseResponse(
            file, "cabin", "History: cabin");

        Assert.Equal(EqWizardSourceKind.ImpulseResponse, source.Kind);
        Assert.NotNull(source.Measurement);
        Assert.Equal(48_000, source.SampleRateHz);
        Assert.Equal(AnalysisCurveKind.Primary, source.CurveKind);
        // Only a loopback-transfer measurement carries coherence; it is what gates the
        // Auto Tune boost mask, so losing it here would silently change tuning.
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
        // A plain sweep deconvolution has no coherence, so boosts fall back to
        // null-detection alone rather than a coherence gate.
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
        // Round-trip through the snapshot, exactly as the wizard's history path does.
        var snapshot = new MeasurementHistorySnapshot
        {
            SampleRate = 48_000,
            Bits = 24,
            Octaves = 10,
            SweepDurationSeconds = 1.0,
            MeasurementMode = mode,
            SweepDeconvolutionImpulseResponse = [new(0, 0), new(1, 0), new(0, 0), new(0, 0)],
            SweepDeconvolutionPeakIndex = 1,
            TransferImpulseResponse = transferIr,
            TransferPeakIndex = transferPeakIndex,
            TransferCoherence = coherence,
            MeterSnapshot = InputLevelMeterSnapshot.Empty,
            Preview = new MeasurementHistoryPreview()
        };
        return snapshot.ToImpulseResponseFile();
    }
}
