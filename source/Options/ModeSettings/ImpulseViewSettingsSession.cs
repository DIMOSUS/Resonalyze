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

    /// <summary>Null for a stored value the list does not hold: nothing is picked and Apply writes the first.</summary>
    public ImpulseAmplitudeScale? AmplitudeScale { get; set; }

    public ImpulseTimeUnit? TimeUnit { get; set; }

    public ImpulseTimeOrigin? TimeOrigin { get; set; }

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
        AmplitudeScale = Enum.IsDefined(options.AmplitudeScale) ? options.AmplitudeScale : null;
        TimeUnit = Enum.IsDefined(options.TimeUnit) ? options.TimeUnit : null;
        TimeOrigin = Enum.IsDefined(options.TimeOrigin) ? options.TimeOrigin : null;
        Invert = options.Invert;
        NormalizeStepToImpulsePeak = options.NormalizeStepToImpulsePeak;
        ShowImpulse = options.ShowImpulse;
        ShowEnvelope = options.ShowEnvelope;
        ShowStep = options.ShowStep;
        ShowAutocorrelation = options.ShowAutocorrelation;
    }

    /// <summary>The centres a new rate realizes; the nearest to the one shown stays selected.</summary>
    public void Follow(int sampleRate)
    {
        if (sampleRate != SampleRate)
        {
            SampleRate = sampleRate;
            ListCentres(CentreHz ?? 1_000.0);
        }
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
        options.AmplitudeScale = AmplitudeScale ?? ImpulseAmplitudeScale.Linear;
        options.TimeUnit = TimeUnit ?? ImpulseTimeUnit.Milliseconds;
        options.TimeOrigin = TimeOrigin ?? ImpulseTimeOrigin.RecordStart;
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
