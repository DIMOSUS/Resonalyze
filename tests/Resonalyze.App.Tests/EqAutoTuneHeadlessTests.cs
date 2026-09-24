using System.Numerics;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

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

        // The crossover stays out of the target here: the subject is the all-pass band, not the skirts.
        EqHeadlessTuneInputs inputs = EqAutoTuneHeadless.Prepare(
            request,
            TargetCurveSpec.FromPreset(TargetPreset.Flat),
            EqAutoTunePolicy.Default with { CrossoverInTarget = false },
            null,
            null,
            allowShelves: false,
            boosts: EqAutoTuneBoosts.Off);

        PeqBand kept = Assert.Single(inputs.Kept);
        Assert.Equal(400, kept.FrequencyHz);
        Assert.Equal(EqualizationCurve.MaxBandCount - 1, inputs.Options.MaxBands);
        // The fit corrects the response WITH the all-pass in the chain; through a window that is a different curve.
        IReadOnlyList<SignalPoint> withoutAllPass =
            EqAutoTuneHeadless.SourceCurve(request.Source, 0, appliedBank: null);
        IReadOnlyList<SignalPoint> withAllPass = EqAutoTuneHeadless.SourceCurve(
            request.Source, 0, new EqualizationCurve(inputs.Kept, preampDb: 0));
        AssertSameCurve(withAllPass, inputs.Source);
        Assert.NotEqual(
            withoutAllPass.Select(point => point.Y), withAllPass.Select(point => point.Y));
        Assert.Equal(inputs.Source.Select(point => point.X), inputs.Target.Select(point => point.X));
        Assert.All(inputs.Target, point => Assert.Equal(-41, point.Y, 9));
    }

    [Fact]
    public void Prepare_WithTheCrossoverInTheTarget_FollowsTheSkirtsAndRefusesToLiftThem()
    {
        // The channel is a band-pass at 80 Hz and 500 Hz, and the wizard's default is to follow its slopes.
        VirtualDspEqHandoffRequest request = Build(BuildChannel());
        TargetCurveSpec flat = TargetCurveSpec.FromPreset(TargetPreset.Flat);

        EqHeadlessTuneInputs inputs = EqAutoTuneHeadless.Prepare(
            request, flat, EqAutoTunePolicy.Default, null, null, allowShelves: null, boosts: null);

        Assert.True(inputs.Options.MinFrequencyHz < 80, $"From is {inputs.Options.MinFrequencyHz}.");
        Assert.True(inputs.Options.MaxFrequencyHz > 500, $"To is {inputs.Options.MaxFrequencyHz}.");
        Assert.NotEmpty(inputs.Options.NoBoostBands);
        // Half a decibel of tolerance: a 2.6-octave passband's own skirts leave its middle a shade below 0 dB.
        double passband = TargetAt(inputs.Target, 200);
        Assert.Equal(-41, passband, 0.6);
        Assert.True(TargetAt(inputs.Target, 45) < passband - 10, "the low skirt should pull the target down.");
        Assert.True(TargetAt(inputs.Target, 900) < passband - 10, "the high skirt should pull the target down.");

        // A window the reply states is the caller's; only the handoff's own passband is widened.
        EqHeadlessTuneInputs stated = EqAutoTuneHeadless.Prepare(
            request, flat, EqAutoTunePolicy.Default, 100, 400, allowShelves: null, boosts: null);

        Assert.Equal(100, stated.Options.MinFrequencyHz);
        Assert.Equal(400, stated.Options.MaxFrequencyHz);
    }

    private static double TargetAt(IReadOnlyList<SignalPoint> curve, double hz) =>
        curve.OrderBy(point => Math.Abs(Math.Log(point.X / hz))).First().Y;

    [Fact]
    public void Prepare_MapsTheOptionsAsTheWizardDoes()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqPreampDb = -2.5;
        VirtualDspEqHandoffRequest request = Build(
            channel, processorProfile: DspProcessorCatalog.Preset("helix-dsp-ultra-s")!.ToProfile());
        TargetCurveSpec target = TargetCurveSpec.FromPreset(TargetPreset.Flat);

        // Window and target as they stand without the crossover; its own test covers the shaped goal.
        EqAutoTunePolicy plain = EqAutoTunePolicy.Default with { CrossoverInTarget = false };
        EqAutoTuner.Options cuts = EqAutoTuneHeadless.Prepare(
            request, target, plain, null, null, allowShelves: false, boosts: EqAutoTuneBoosts.Off).Options;
        Assert.Equal(EqAutoTuneBoosts.Off, cuts.Boosts);
        Assert.Equal((double)EqWizardLimits.Preamp.Minimum, cuts.PreampMinDb);
        Assert.Equal((double)EqWizardLimits.Preamp.Maximum, cuts.PreampMaxDb);
        Assert.Equal(0, cuts.TotalGainMaxDb);
        Assert.False(cuts.AllowShelves);
        Assert.Equal(80, cuts.MinFrequencyHz);
        Assert.Equal(500, cuts.MaxFrequencyHz);
        Assert.Equal(96_000, cuts.SampleRateHz);
        Assert.Equal(EqAutoTuneHeadless.BandGainMinDb, cuts.BandGainMinDb);
        Assert.Equal(EqAutoTuneHeadless.BandGainMaxDb, cuts.BandGainMaxDb);
        Assert.Equal((double)EqWizardLimits.BandQ.Minimum, cuts.QMin);
        Assert.Equal(EqAutoTuneHeadless.MaxQ, cuts.QMax);

        EqAutoTuner.Options boosts = EqAutoTuneHeadless.Prepare(
            request, target, plain, 100, 3_000, allowShelves: true, boosts: EqAutoTuneBoosts.Allowed).Options;
        Assert.Equal(EqAutoTuneBoosts.Allowed, boosts.Boosts);
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

        // Inverted edges are not reordered: a swap would fit a window nobody ticked.
        EqHeadlessTuneInputs inverted = EqAutoTuneHeadless.Prepare(
            request, target, EqAutoTunePolicy.Default, 5_000, 300, allowShelves: false, boosts: EqAutoTuneBoosts.Off);
        Assert.Equal(5_000, inverted.MinHz);
        Assert.Equal(300, inverted.MaxHz);
        Assert.False(EqAutoTuneHeadless.IsUsableWindow(inverted.MinHz, inverted.MaxHz));
        Assert.True(EqAutoTuneHeadless.IsUsableWindow(300, 5_000));
        Assert.False(EqAutoTuneHeadless.IsUsableWindow(300, 300.5));

        EqHeadlessTuneInputs wide = EqAutoTuneHeadless.Prepare(
            request, target, EqAutoTunePolicy.Default, 5, 40_000, allowShelves: false, boosts: EqAutoTuneBoosts.Off);
        Assert.Equal(EqAutoTuneHeadless.WindowMinHz, wide.MinHz);
        Assert.Equal(EqAutoTuneHeadless.WindowMaxHz, wide.MaxHz);
    }

    [Fact]
    public void Prepare_FitsWithTheWizardsCurrentSettings_UnlessTheReplyStatesItsOwn()
    {
        VirtualDspEqHandoffRequest request = Build(BuildChannel());
        TargetCurveSpec target = TargetCurveSpec.FromPreset(TargetPreset.Flat);
        var policy = new EqAutoTunePolicy(
            8, -10, 4, 3.5, EqAutoTuneBoosts.Allowed, AllowShelves: true, CrossoverInTarget: false);

        EqAutoTuner.Options asWizard = EqAutoTuneHeadless.Prepare(
            request, target, policy, null, null, allowShelves: null, boosts: null).Options;
        Assert.Equal(8, asWizard.MaxBands);
        Assert.Equal(-10, asWizard.BandGainMinDb);
        Assert.Equal(4, asWizard.BandGainMaxDb);
        Assert.Equal(3.5, asWizard.QMax);
        Assert.Equal(EqAutoTuneBoosts.Allowed, asWizard.Boosts);
        Assert.True(asWizard.AllowShelves);

        EqAutoTuner.Options overridden = EqAutoTuneHeadless.Prepare(
            request, target, policy, null, null, allowShelves: false, boosts: EqAutoTuneBoosts.Off).Options;
        Assert.Equal(EqAutoTuneBoosts.Off, overridden.Boosts);
        Assert.False(overridden.AllowShelves);
        Assert.Equal(8, overridden.MaxBands);
        Assert.Equal(3.5, overridden.QMax);

        Assert.Equal(EqualizationCurve.MaxBandCount, EqAutoTunePolicy.Default.MaxBands);
        Assert.Equal(EqAutoTuneBoosts.RefillOwnCuts, EqAutoTunePolicy.Default.Boosts);
        Assert.False(EqAutoTunePolicy.Default.AllowShelves);
    }

    [Fact]
    public void Prepare_RefusesWhenTheKeptAllPassBandsFillMaxFilters_AsTheWizardDoes()
    {
        // Four kept all-pass bands under Max Filters 4: the wizard does not run, so neither may this.
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqBands =
        [
            new PeqBand(200, 0.7, 0, PeqBandType.AllPassSecondOrder),
            new PeqBand(300, 0.7, 0, PeqBandType.AllPassSecondOrder),
            new PeqBand(400, 0.7, 0, PeqBandType.AllPassFirstOrder),
            new PeqBand(500, 0.7, 0, PeqBandType.AllPassSecondOrder)
        ];
        VirtualDspEqHandoffRequest request = Build(channel);
        var four = new EqAutoTunePolicy(
            4, -15, 6, 6, EqAutoTuneBoosts.Off, AllowShelves: false, CrossoverInTarget: false);
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
    public void AWindowWithNothingMeasuredInIt_IsARefusalTheCallerSkipsOn_AndFitRefusesItOutright()
    {
        // The tuner answers an empty window with an empty bank and Auto Tune applies what it returns, so this run
        // must not happen. Prepare does not throw: the import skips this channel and goes on to the next.
        VirtualDspEqHandoffRequest request = Build(BuildChannel(), spatialAverage: Capture(gridStopHz: 1_000));
        TargetCurveSpec target = TargetCurveSpec.FromPreset(TargetPreset.Flat);

        EqHeadlessTuneInputs empty = EqAutoTuneHeadless.Prepare(
            request, target, EqAutoTunePolicy.Default, 5_000, 8_000,
            allowShelves: false, boosts: EqAutoTuneBoosts.Off);

        string refusal = Assert.IsType<string>(EqAutoTuneHeadless.NoMeasuredDataRefusal(empty));
        Assert.Contains("5000 Hz - 8000 Hz", refusal);
        Assert.Contains("no measured point", refusal);
        // The backstop for a caller that does not ask: an empty bank must never reach a channel.
        Assert.Throws<InvalidOperationException>(() => EqAutoTuneHeadless.Fit(empty));

        // Inside what the capture covers there is something to fit, and nothing refuses it.
        EqHeadlessTuneInputs fits = EqAutoTuneHeadless.Prepare(
            request, target, EqAutoTunePolicy.Default, 100, 400,
            allowShelves: false, boosts: EqAutoTuneBoosts.Off);

        Assert.Null(EqAutoTuneHeadless.NoMeasuredDataRefusal(fits));
        Assert.NotEmpty(EqAutoTuneHeadless.Fit(fits).Bands);
    }

    [Fact]
    public void Fit_ReturnsTheKeptAllPassBandsWithTheFittedOnes()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        channel.Settings.PeqBands = [new PeqBand(400, 0.7, 0, PeqBandType.AllPassSecondOrder)];
        VirtualDspEqHandoffRequest request = Build(channel);
        EqHeadlessTuneInputs inputs = EqAutoTuneHeadless.Prepare(
            request, TargetCurveSpec.FromPreset(TargetPreset.Flat), EqAutoTunePolicy.Default, null, null,
            allowShelves: false, boosts: EqAutoTuneBoosts.Off);

        EqualizationCurve fitted = EqAutoTuneHeadless.Fit(inputs);

        Assert.Contains(fitted.Bands, band => band.Type.IsAllPass() && band.FrequencyHz == 400);
        Assert.True(fitted.Bands.Count <= EqualizationCurve.MaxBandCount);
        // Low to high, as the button leaves it.
        Assert.Equal(fitted.Bands.OrderBy(band => band.FrequencyHz), fitted.Bands);
        Assert.All(fitted.Bands.Where(band => !band.Type.IsAllPass()), band => Assert.True(band.GainDb <= 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prepare_KeepsALockedBell_AndFitsTheSourceThroughIt_AsTheButtonDoes(bool spatialAverage)
    {
        VirtualCrossoverChannel channel = BuildChannel();
        var locked = new PeqBand(250, 1.5, -4, PeqBandType.Peaking, Locked: true);
        channel.Settings.PeqBands = [new PeqBand(200, 1.0, -3), locked];
        VirtualDspEqHandoffRequest request = spatialAverage
            ? Build(channel, spatialAverage: Capture())
            : Build(channel);

        EqHeadlessTuneInputs inputs = EqAutoTuneHeadless.Prepare(
            request,
            TargetCurveSpec.FromPreset(TargetPreset.Flat),
            EqAutoTunePolicy.Default with { CrossoverInTarget = false },
            null,
            null,
            allowShelves: false,
            boosts: EqAutoTuneBoosts.Off);

        Assert.Equal(locked, Assert.Single(inputs.Kept));
        Assert.Equal(EqualizationCurve.MaxBandCount - 1, inputs.Options.MaxBands);
        IReadOnlyList<SignalPoint> throughTheBell = EqAutoTuneHeadless.SourceCurve(
            request.Source, 0, new EqualizationCurve([locked], preampDb: 0));
        AssertSameCurve(throughTheBell, inputs.Source);
        Assert.NotEqual(
            EqAutoTuneHeadless.SourceCurve(request.Source, 0, appliedBank: null).Select(point => point.Y),
            throughTheBell.Select(point => point.Y));

        var session = new EqWizardSession();
        session.BeginHandoff(request);
        AssertSameCurve(
            EqWizardFit.FitSource(session, session.SourceCurve!, EqWizardFit.LockedBands(session))
                .Select(point => new SignalPoint(point.X, point.Y))
                .ToList(),
            inputs.Source);
    }

    [Fact]
    public void Fit_ReturnsALockedBandAsItWas()
    {
        VirtualCrossoverChannel channel = BuildChannel();
        var locked = new PeqBand(250, 1.5, -4, PeqBandType.Peaking, Locked: true);
        channel.Settings.PeqBands = [locked];
        EqHeadlessTuneInputs inputs = EqAutoTuneHeadless.Prepare(
            Build(channel), TargetCurveSpec.FromPreset(TargetPreset.Flat), EqAutoTunePolicy.Default, null, null,
            allowShelves: false, boosts: EqAutoTuneBoosts.Off);

        EqualizationCurve fitted = EqAutoTuneHeadless.Fit(inputs);

        Assert.Equal(locked, Assert.Single(fitted.Bands, band => band.Locked));
        Assert.Equal(fitted.Bands.OrderBy(band => band.FrequencyHz), fitted.Bands);
    }

    private static IReadOnlyList<SignalPoint> WizardSourceCurve(VirtualDspEqHandoffRequest request)
    {
        var session = new EqWizardSession();
        session.BeginHandoff(request);
        return session.SourceCurve!.Points.Select(point => new SignalPoint(point.X, point.Y)).ToList();
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

    private static LiveCaptureDocument Capture(double gridStopHz = 20_000) => new()
    {
        SavedAtUtc = DateTimeOffset.UnixEpoch,
        Title = "l tw mmm",
        CurveDb = Enumerable.Range(0, 1_024)
            .Select(index => -20.0 + 3 * Math.Sin(index / 40.0))
            .ToArray(),
        GridStartHz = 20,
        GridStopHz = gridStopHz,
        Recipe = new LiveCaptureRecipe
        {
            AnalysisMode = LiveAnalysisMode.Mmm,
            SampleRateHz = SampleRate
        }
    };

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
