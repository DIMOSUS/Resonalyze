using Resonalyze.Dsp;

namespace Resonalyze.Options;

/// <summary>The Impulse and Autocorrelation settings panels' state, each field as its control shows it. The band centre
/// is kept while the band is off. See docs/tech/mode-settings.md#code-map.</summary>
internal sealed class ImpulseViewSettingsSession
{
    public int Length { get; set; }

    public decimal EnvelopeSmoothingMs { get; set; }

    public double BandOctaves { get; private set; }

    public IReadOnlyList<double> Centres { get; private set; } = [];

    public int CentreIndex { get; set; } = -1;

    public bool BandActive => BandOctaves > 0.0;

    public ImpulseAmplitudeScale AmplitudeScale { get; set; }

    public ImpulseTimeUnit TimeUnit { get; set; }

    public ImpulseTimeOrigin TimeOrigin { get; set; }

    public bool Invert { get; set; }

    public bool NormalizeStepToImpulsePeak { get; set; }

    public bool ShowImpulse { get; set; }

    public bool ShowEnvelope { get; set; }

    public bool ShowStep { get; set; }

    public bool ShowAutocorrelation { get; set; }

    public int SampleRate { get; private set; }

    public void Load(ImpulseResponseOptions options, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(options);
        SampleRate = sampleRate;
        Length = (int)ModeSettingsLimits.ImpulseLength.Clamp(options.Length);
        EnvelopeSmoothingMs = ModeSettingsLimits.EnvelopeSmoothingMs.Clamp(options.EnvelopeSmoothingMs);
        BandOctaves = ImpulseBandCentres.NearestWidth(options.BandFilterOctaves);
        ListCentres(options.BandCenterHz);
        AmplitudeScale = options.AmplitudeScale;
        TimeUnit = options.TimeUnit;
        TimeOrigin = options.TimeOrigin;
        Invert = options.Invert;
        NormalizeStepToImpulsePeak = options.NormalizeStepToImpulsePeak;
        ShowImpulse = options.ShowImpulse;
        ShowEnvelope = options.ShowEnvelope;
        ShowStep = options.ShowStep;
        ShowAutocorrelation = options.ShowAutocorrelation;
    }

    /// <summary>Keeps the nearest centre when the width changes.</summary>
    public void SetBandOctaves(double octaves)
    {
        BandOctaves = octaves;
        ListCentres(CentreHz ?? 1_000.0);
    }

    public double? CentreHz => CentreIndex >= 0 && CentreIndex < Centres.Count ? Centres[CentreIndex] : null;

    public void WriteTo(ImpulseResponseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Length = Length;
        options.EnvelopeSmoothingMs = (double)EnvelopeSmoothingMs;
        options.BandFilterOctaves = BandOctaves;
        options.BandCenterHz = CentreHz ?? options.BandCenterHz;
        options.AmplitudeScale = AmplitudeScale;
        options.TimeUnit = TimeUnit;
        options.TimeOrigin = TimeOrigin;
        options.Invert = Invert;
        options.NormalizeStepToImpulsePeak = NormalizeStepToImpulsePeak;
        options.ShowImpulse = ShowImpulse;
        options.ShowEnvelope = ShowEnvelope;
        options.ShowStep = ShowStep;
    }

    public void WriteAutocorrelation(ImpulseResponseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ShowAutocorrelation = ShowAutocorrelation;
    }

    private void ListCentres(double preferredHz)
    {
        Centres = ImpulseBandCentres.For(BandOctaves, SampleRate);
        CentreIndex = Centres.ToList().IndexOf(ImpulseBandCentres.Nearest(Centres, preferredHz));
    }
}
