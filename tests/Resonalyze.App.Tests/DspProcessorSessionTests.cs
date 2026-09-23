using System.Globalization;
using Resonalyze.Dsp;

namespace Resonalyze.App.Tests;

public sealed class DspProcessorSessionTests
{
    private const int MeasurementRate = 48_000;

    private static DspProcessorPreset Helix => DspProcessorCatalog.Preset("helix-dsp-ultra-s")!;

    private static DspProcessorPreset Panacea => DspProcessorCatalog.Preset("amp-panacea-v1-v2")!;

    private static DspProcessorSession Open(
        bool followsMeasurements,
        int measurementRateHz = MeasurementRate,
        bool? phaseControl = null,
        bool? firFilters = null,
        DspProcessorProfile? profile = null) =>
        new(
            profile ?? DspProcessorProfile.Custom(measurementRateHz > 0 ? measurementRateHz : 48_000, PeqQConvention.Rbj),
            followsMeasurements,
            measurementRateHz,
            phaseControl,
            firFilters);

    [Fact]
    public void LookingAtAPreset_DoesNotForgetAStatedRate()
    {
        DspProcessorSession choice = Open(followsMeasurements: true);
        choice.SelectSampleRate(96_000);
        choice.SelectModel(DspProcessorCatalog.Preset("helix-next-v-eight-dsp-ultimate"));
        choice.SelectModel(null);

        Assert.False(choice.FollowsMeasurements);
        Assert.Equal(96_000, choice.Profile.SampleRateHz);
    }

    [Fact]
    public void LookingAtAPreset_DoesNotForgetTheFollowChoice()
    {
        DspProcessorSession choice = Open(followsMeasurements: false);
        choice.SelectSampleRate(null);
        choice.SelectModel(Panacea);
        choice.SelectModel(null);

        Assert.True(choice.FollowsMeasurements);
        Assert.Equal(MeasurementRate, choice.Profile.SampleRateHz);
    }

    [Fact]
    public void ANamedModel_NeverFollows_AndFixesItsRateAndConvention()
    {
        DspProcessorSession choice = Open(followsMeasurements: true);
        choice.SelectModel(Helix);
        choice.SelectSampleRate(44_100);
        choice.SelectQConvention(PeqQConvention.Symmetric);

        Assert.False(choice.CustomFields);
        Assert.False(choice.FollowsMeasurements);
        Assert.Equal(Helix.ToProfile(), choice.Profile);
    }

    [Fact]
    public void WithoutAMeasurement_FollowingStillResolvesToAUsableRate()
    {
        DspProcessorSession choice = Open(followsMeasurements: true, measurementRateHz: 0);

        Assert.True(choice.FollowsMeasurements);
        Assert.True(choice.Profile.SampleRateHz > 0);
    }

    [Fact]
    public void AnUnlistedRateJoinsTheList_SoOpeningCannotRoundTheProjectsRate()
    {
        DspProcessorSession choice = Open(
            followsMeasurements: false, measurementRateHz: 50_000, profile: DspProcessorProfile.Custom(22_050, PeqQConvention.Rbj));

        Assert.Contains(22_050, choice.SampleRates);
        Assert.Contains(50_000, choice.SampleRates);
        Assert.Equal(22_050, choice.SampleRate);
        Assert.Equal(22_050, choice.Profile.SampleRateHz);
    }

    [Fact]
    public void NamingAnotherModel_LetsTheCatalogAnswerThePhaseQuestionAgain()
    {
        // A stored yes belongs to its device; carried over it would keep rotations on hardware without the control.
        DspProcessorSession choice = Open(followsMeasurements: false, phaseControl: true);
        Assert.True(choice.PhaseControl);

        choice.SelectModel(Panacea);
        Assert.False(choice.PhaseControl);

        choice.SelectModel(Helix);
        Assert.True(choice.PhaseControl);
    }

    [Fact]
    public void TheStoredPhaseAnswer_SurvivesWhileTheModelDoes()
    {
        DspProcessorSession choice = Open(followsMeasurements: false, phaseControl: true);
        choice.SelectSampleRate(96_000);
        Assert.True(choice.PhaseControl);

        Assert.False(Open(followsMeasurements: false, phaseControl: false).PhaseControl);
    }

    [Fact]
    public void APhaseAnswerGivenForThisModel_IsNotUndoneByLookingAtTheFields()
    {
        DspProcessorSession choice = Open(followsMeasurements: false);
        choice.SelectModel(Helix);
        Assert.True(choice.PhaseControl);

        choice.SetPhaseControl(false);
        choice.SelectSampleRate(96_000);

        Assert.False(choice.PhaseControl);
    }

    [Fact]
    public void CustomRemembersItsOwnPhaseAnswer()
    {
        DspProcessorSession choice = Open(followsMeasurements: false);
        choice.SetPhaseControl(true);
        choice.SelectModel(Panacea);
        choice.SelectModel(null);

        Assert.True(choice.PhaseControl);
    }

    [Fact]
    public void TheFirTick_IsOffUntilGiven_AndNotTakenAwayByNamingAModel()
    {
        // The catalog's false means "not known", and an untick detaches every kernel.
        DspProcessorSession choice = Open(followsMeasurements: false);
        Assert.False(choice.FirFilters);

        choice.SetFirFilters(true);
        choice.SelectModel(Helix);
        Assert.True(choice.FirFilters);
        choice.SelectModel(Panacea);
        Assert.True(choice.FirFilters);
        choice.SelectModel(null);
        Assert.True(choice.FirFilters);
    }

    [Fact]
    public void TheStoredFirAnswer_SurvivesTheModelList_AndOnlyTheUserUnticksIt()
    {
        DspProcessorSession choice = Open(followsMeasurements: false, firFilters: true);
        choice.SelectModel(Panacea);
        Assert.True(choice.FirFilters);

        choice.SetFirFilters(false);
        choice.SelectModel(Helix);
        Assert.False(choice.FirFilters);
    }

    [Fact]
    public void TheStatusNamesTheRatesTheConventionAndWhatTheBlocksGain()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        DspProcessorSession choice = Open(followsMeasurements: true, measurementRateHz: 44_100);
        choice.SetPhaseControl(true);
        choice.SetFirFilters(true);

        string status = DspProcessorStatus.Text(choice);

        Assert.StartsWith("Filters are designed at 44.1 kHz; the measurements stay at 44.1 kHz", status);
        Assert.Contains("Q is stated as the RBJ cookbook defines it", status);
        Assert.Contains("follows the project's measurements", status);
        Assert.Contains("Each block gets a Phase field", status);
        Assert.Contains("A kernel is convolved at 44.1 kHz", status);

        choice.SelectModel(Panacea);
        string panacea = DspProcessorStatus.Text(choice);
        Assert.Contains("up to 22.1 kHz", panacea);
        Assert.Contains("Tuning sheets restate Q as", panacea);
        Assert.DoesNotContain("follows", panacea);
        Assert.StartsWith(
            "Filters are designed at 48 kHz. The project has no measurement yet",
            DspProcessorStatus.Text(Open(followsMeasurements: true, measurementRateHz: 0)));
    }
}
