using System.Numerics;
using System.Reflection;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

/// <summary>
/// Auto Tune from an AI import runs with no wizard on screen, so what it fits
/// has to be exactly what the wizard would have fitted for the same handoff:
/// the curves are compared point for point against the wizard's own render,
/// for a gated impulse response and for a spatial average alike. The option
/// mapping and the window clamp are pinned beside them.
/// </summary>
public sealed class EqAutoTuneHeadlessTests
{
    private const int SampleRate = 48_000;

    private static readonly PhaseAnalysisSettings GateTemplate = new(
        PhaseWindowMode.Fixed,
        PhaseAnalysisSettings.DefaultFdwCycles,
        PhaseDetrendMode.Off,
        ManualDetrendMilliseconds: 0.0,
        GateOffsetMs: 0.0,
        LeftMs: 2.0,
        PlateauMs: 12.0,
        RightMs: 5.0,
        Unwrap: false,
        SmoothingInverseOctaves: 0.0);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void TheGatedSource_IsTheWizardsOwnCurve(int smoothing)
    {
        VirtualDspEqHandoffRequest request = Build(BuildChannel(), smoothing: smoothing);

        IReadOnlyList<SignalPoint> headless =
            EqAutoTuneHeadless.SourceCurve(request.Source, smoothing, appliedBank: null);
        IReadOnlyList<SignalPoint> wizard = WizardSourceCurve(request);

        AssertSameCurve(wizard, headless);
    }

    [Fact]
    public void TheSpatialAverageSource_IsTheWizardsOwnCurve_OffsetIncluded()
    {
        VirtualDspEqHandoffRequest request = Build(
            BuildChannel(), spatialAverage: Capture(), spatialAverageOffsetDb: -73.5);

        IReadOnlyList<SignalPoint> headless =
            EqAutoTuneHeadless.SourceCurve(request.Source, 0, appliedBank: null);
        IReadOnlyList<SignalPoint> wizard = WizardSourceCurve(request);

        Assert.NotEmpty(headless);
        AssertSameCurve(wizard, headless);
    }

    [Fact]
    public void Prepare_KeepsTheAllPassBands_AndFitsAroundWhatTheyDoThroughTheWindow()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqBands =
        [
            new PeqBand(200, 1.0, -3),
            new PeqBand(400, 0.7, 0, PeqBandType.AllPassSecondOrder)
        ];
        VirtualDspEqHandoffRequest request = Build(channel);

        EqHeadlessTuneInputs inputs = EqAutoTuneHeadless.Prepare(
            request, TargetCurveSpec.FromPreset(TargetPreset.Flat), EqAutoTunePolicy.Default, null, null,
            allowShelves: false, cutsOnly: true);

        PeqBand kept = Assert.Single(inputs.KeptAllPass);
        Assert.Equal(400, kept.FrequencyHz);
        // The bank's budget is the processor's slot count less the kept band.
        Assert.Equal(EqualizationCurve.MaxBandCount - 1, inputs.Options.MaxBands);
        // The source the fit corrects is the response WITH the all-pass in the
        // chain — through a window that is not the same curve.
        IReadOnlyList<SignalPoint> withoutAllPass =
            EqAutoTuneHeadless.SourceCurve(request.Source, 0, appliedBank: null);
        IReadOnlyList<SignalPoint> withAllPass = EqAutoTuneHeadless.SourceCurve(
            request.Source, 0, new EqualizationCurve(inputs.KeptAllPass, preampDb: 0));
        AssertSameCurve(withAllPass, inputs.Source);
        Assert.NotEqual(
            withoutAllPass.Select(point => point.Y), withAllPass.Select(point => point.Y));
        // Target on the source's frequencies, at the handoff's level.
        Assert.Equal(inputs.Source.Select(point => point.X), inputs.Target.Select(point => point.X));
        Assert.All(inputs.Target, point => Assert.Equal(-41, point.Y, 9));
    }

    [Fact]
    public void Prepare_MapsTheOptionsAsTheWizardDoes()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqPreampDb = -2.5;
        VirtualDspEqHandoffRequest request = Build(
            channel, processorProfile: DspProcessorCatalog.Preset("helix-dsp-ultra-s")!.ToProfile());
        TargetCurveSpec target = TargetCurveSpec.FromPreset(TargetPreset.Flat);

        // Cuts only: the auto preamp may use the field's whole range, the net
        // gain is capped at 0 dB.
        EqAutoTuner.Options cuts = EqAutoTuneHeadless.Prepare(
            request, target, EqAutoTunePolicy.Default, null, null, allowShelves: false, cutsOnly: true).Options;
        Assert.True(cuts.CutsOnlyMode);
        Assert.Equal(-EqAutoTuneHeadless.PreampRangeDb, cuts.PreampMinDb);
        Assert.Equal(EqAutoTuneHeadless.PreampRangeDb, cuts.PreampMaxDb);
        Assert.Equal(0, cuts.TotalGainMaxDb);
        Assert.False(cuts.AllowShelves);
        // The window is the channel's passband, as the handoff set the wizard's
        // From/To; the rate is the processor's.
        Assert.Equal(80, cuts.MinFrequencyHz);
        Assert.Equal(500, cuts.MaxFrequencyHz);
        Assert.Equal(96_000, cuts.SampleRateHz);
        Assert.Equal(EqAutoTuneHeadless.BandGainMinDb, cuts.BandGainMinDb);
        Assert.Equal(EqAutoTuneHeadless.BandGainMaxDb, cuts.BandGainMaxDb);
        Assert.Equal(PeqSlotControl.MinimumQ, cuts.QMin);
        Assert.Equal(EqAutoTuneHeadless.MaxQ, cuts.QMax);

        // Boosts allowed: the preamp is the user's and stays where the bank had
        // it; the reply's window and shelves are honoured.
        EqAutoTuner.Options boosts = EqAutoTuneHeadless.Prepare(
            request, target, EqAutoTunePolicy.Default, 100, 3_000, allowShelves: true, cutsOnly: false).Options;
        Assert.False(boosts.CutsOnlyMode);
        Assert.Equal(-2.5, boosts.PreampMinDb);
        Assert.Equal(-2.5, boosts.PreampMaxDb);
        Assert.Equal(double.PositiveInfinity, boosts.TotalGainMaxDb);
        Assert.True(boosts.AllowShelves);
        Assert.Equal(100, boosts.MinFrequencyHz);
        Assert.Equal(3_000, boosts.MaxFrequencyHz);
    }

    [Fact]
    public void Prepare_TakesTheWindowAsStated_AndOnlyHoldsItToTheFieldsRange()
    {
        VirtualDspEqHandoffRequest request = Build(BuildChannel());
        TargetCurveSpec target = TargetCurveSpec.FromPreset(TargetPreset.Flat);

        // Inverted edges are NOT reordered: the review confirmed a lower and an
        // upper edge, and a run that swapped them would fit a window nobody
        // ticked. The pair reads as unusable instead.
        EqHeadlessTuneInputs inverted = EqAutoTuneHeadless.Prepare(
            request, target, EqAutoTunePolicy.Default, 5_000, 300, allowShelves: false, cutsOnly: true);
        Assert.Equal(5_000, inverted.MinHz);
        Assert.Equal(300, inverted.MaxHz);
        Assert.False(EqAutoTuneHeadless.IsUsableWindow(inverted.MinHz, inverted.MaxHz));
        Assert.True(EqAutoTuneHeadless.IsUsableWindow(300, 5_000));
        Assert.False(EqAutoTuneHeadless.IsUsableWindow(300, 300.5));

        EqHeadlessTuneInputs wide = EqAutoTuneHeadless.Prepare(
            request, target, EqAutoTunePolicy.Default, 5, 40_000, allowShelves: false, cutsOnly: true);
        Assert.Equal(EqAutoTuneHeadless.WindowMinHz, wide.MinHz);
        Assert.Equal(EqAutoTuneHeadless.WindowMaxHz, wide.MaxHz);
    }

    [Fact]
    public void Prepare_FitsWithTheWizardsCurrentSettings_UnlessTheReplyStatesItsOwn()
    {
        // The wizard's controls as the user left them — Max Filters 8, Max Q 3.5,
        // cuts down to -10 dB, boosts allowed, shelves on — reach the tuner the
        // way CreateAutoTuneOptions would hand them; the reply overrides only
        // what it states.
        VirtualDspEqHandoffRequest request = Build(BuildChannel());
        TargetCurveSpec target = TargetCurveSpec.FromPreset(TargetPreset.Flat);
        var policy = new EqAutoTunePolicy(8, -10, 4, 3.5, CutsOnly: false, AllowShelves: true);

        EqAutoTuner.Options asWizard = EqAutoTuneHeadless.Prepare(
            request, target, policy, null, null, allowShelves: null, cutsOnly: null).Options;
        Assert.Equal(8, asWizard.MaxBands);
        Assert.Equal(-10, asWizard.BandGainMinDb);
        Assert.Equal(4, asWizard.BandGainMaxDb);
        Assert.Equal(3.5, asWizard.QMax);
        Assert.False(asWizard.CutsOnlyMode);
        Assert.True(asWizard.AllowShelves);

        EqAutoTuner.Options overridden = EqAutoTuneHeadless.Prepare(
            request, target, policy, null, null, allowShelves: false, cutsOnly: true).Options;
        Assert.True(overridden.CutsOnlyMode);
        Assert.False(overridden.AllowShelves);
        Assert.Equal(8, overridden.MaxBands);
        Assert.Equal(3.5, overridden.QMax);

        // The opening values, for a host with no wizard to ask.
        Assert.Equal(EqualizationCurve.MaxBandCount, EqAutoTunePolicy.Default.MaxBands);
        Assert.True(EqAutoTunePolicy.Default.CutsOnly);
        Assert.False(EqAutoTunePolicy.Default.AllowShelves);
    }

    [Fact]
    public void Prepare_RefusesWhenTheKeptAllPassBandsFillMaxFilters_AsTheWizardDoes()
    {
        // Max Filters 4 and four all-pass bands kept: the wizard says "no room"
        // and does not run; a fit that placed one band anyway would hand back
        // five filters under a limit of four.
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqBands =
        [
            new PeqBand(200, 0.7, 0, PeqBandType.AllPassSecondOrder),
            new PeqBand(300, 0.7, 0, PeqBandType.AllPassSecondOrder),
            new PeqBand(400, 0.7, 0, PeqBandType.AllPassFirstOrder),
            new PeqBand(500, 0.7, 0, PeqBandType.AllPassSecondOrder)
        ];
        VirtualDspEqHandoffRequest request = Build(channel);
        var four = new EqAutoTunePolicy(4, -15, 6, 6, CutsOnly: true, AllowShelves: false);
        var five = four with { MaxBands = 5 };
        TargetCurveSpec target = TargetCurveSpec.FromPreset(TargetPreset.Flat);

        Assert.Equal(0, EqAutoTuneHeadless.RoomUnderMaxFilters(request, four));
        Assert.Throws<InvalidOperationException>(() =>
            EqAutoTuneHeadless.Prepare(request, target, four, null, null, null, null));

        Assert.Equal(1, EqAutoTuneHeadless.RoomUnderMaxFilters(request, five));
        EqHeadlessTuneInputs inputs = EqAutoTuneHeadless.Prepare(request, target, five, null, null, null, null);
        Assert.Equal(1, inputs.Options.MaxBands);
        EqualizationCurve fitted = EqAutoTuneHeadless.Fit(inputs);
        Assert.True(fitted.Bands.Count <= 5);
        Assert.Equal(4, fitted.Bands.Count(band => band.Type.IsAllPass()));
    }

    [Fact]
    public void Fit_ReturnsTheKeptAllPassBandsWithTheFittedOnes()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqBands = [new PeqBand(400, 0.7, 0, PeqBandType.AllPassSecondOrder)];
        VirtualDspEqHandoffRequest request = Build(channel);
        EqHeadlessTuneInputs inputs = EqAutoTuneHeadless.Prepare(
            request, TargetCurveSpec.FromPreset(TargetPreset.Flat), EqAutoTunePolicy.Default, null, null,
            allowShelves: false, cutsOnly: true);

        EqualizationCurve fitted = EqAutoTuneHeadless.Fit(inputs);

        Assert.Contains(fitted.Bands, band => band.Type.IsAllPass() && band.FrequencyHz == 400);
        Assert.True(fitted.Bands.Count <= EqualizationCurve.MaxBandCount);
        Assert.All(fitted.Bands.Where(band => !band.Type.IsAllPass()), band => Assert.True(band.GainDb <= 0));
    }

    // The wizard, handed the same request, asked for the Source curve it draws.
    private static IReadOnlyList<SignalPoint> WizardSourceCurve(VirtualDspEqHandoffRequest request)
    {
        using var panel = new EqWizardPanel();
        panel.BeginVirtualDspHandoff(request);
        object curve = typeof(EqWizardPanel)
            .GetMethod("ComputeSourceCurve", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(panel, [])!;
        var points = (IReadOnlyList<OxyPlot.DataPoint>)curve.GetType().GetProperty("Points")!.GetValue(curve)!;
        return points.Select(point => new SignalPoint(point.X, point.Y)).ToList();
    }

    private static void AssertSameCurve(IReadOnlyList<SignalPoint> expected, IReadOnlyList<SignalPoint> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].X, actual[index].X, 9);
            if (double.IsNaN(expected[index].Y))
            {
                Assert.True(double.IsNaN(actual[index].Y), $"point {index} at {expected[index].X} Hz");
            }
            else
            {
                Assert.Equal(expected[index].Y, actual[index].Y, 9);
            }
        }
    }

    private static VirtualDspEqHandoffRequest Build(
        VirtualCrossoverChannel channel,
        int smoothing = 0,
        LiveCaptureDocument? spatialAverage = null,
        double spatialAverageOffsetDb = 0,
        DspProcessorProfile? processorProfile = null) =>
        VirtualDspEqHandoff.Build(
            channel,
            channel.ActiveRight,
            withChain: true,
            processorProfile ?? DspProcessorProfile.Custom(SampleRate, PeqQConvention.Rbj),
            GateTemplate,
            pinnedGateOffsetMs: null,
            renderAnchorIndex: 480,
            phaseContext: null,
            targetLevelDb: -41,
            targetLevelMinDb: -120,
            targetLevelMaxDb: 60,
            smoothingInverseOctaves: smoothing,
            calibration: null,
            calibrationName: null,
            SpatialAverageCalibration.Own,
            projectGeneration: 1,
            spatialAverage,
            spatialAverageOffsetDb);

    private static LiveCaptureDocument Capture() => new()
    {
        SavedAtUtc = DateTimeOffset.UnixEpoch,
        Title = "l tw mmm",
        CurveDb = Enumerable.Range(0, 1_024)
            .Select(index => -20.0 + 3 * Math.Sin(index / 40.0))
            .ToArray(),
        GridStartHz = 20,
        GridStopHz = 20_000,
        Recipe = new LiveCaptureRecipe
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            SampleRateHz = SampleRate
        }
    };

    // The handoff tests' channel: a decaying wavelet at 10 ms through a full chain.
    private static VirtualCrossoverChannel BuildChannel()
    {
        var impulseResponse = new Complex[4_096];
        for (int i = 0; i < 64; i++)
        {
            impulseResponse[480 + i] =
                Math.Exp(-i / 12.0) * Math.Cos(2 * Math.PI * i / 16.0);
        }

        var channel = new VirtualCrossoverChannel("A");
        channel.SampleRate = SampleRate;
        channel.TransferImpulseResponse = impulseResponse;
        channel.TransferPeakIndex = 480;
        channel.Settings.GainDb = -3;
        channel.Settings.DelayMs = 1.25;
        channel.Settings.CrossoverKind = CrossoverKind.BandPass;
        channel.Settings.HighPassEdge = channel.Settings.HighPassEdge with { FrequencyHz = 80 };
        channel.Settings.LowPassEdge = channel.Settings.LowPassEdge with { FrequencyHz = 500 };
        return channel;
    }
}
