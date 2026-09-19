using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// The wizard's filter bank and its undo history. A band's index IS its filter number and export position, so order is
/// part of the state. Every band is held as its strip shows it (<see cref="EqWizardLimits.Normalize"/>): an edit is
/// typed, a structural change (add, remove, reorder, retype, a whole new bank) is one undo step of its own.
/// </summary>
internal sealed class EqWizardBank
{
    // Narrow and mid-band: a deliberate correction to drag into place, not a wide bell colouring half the spectrum.
    private const double AddedBandFrequencyHz = 1000;
    private const double AddedBandQ = 5;

    // Target curve's default shelf corners, with the steepest monotonic knee.
    private const double AddedLowShelfFrequencyHz = 100;
    private const double AddedHighShelfFrequencyHz = 5000;
    private const double AddedShelfQ = 0.7;

    // Q is the first-order band's sentinel too: the order has no Q, but project-file validators require a positive one.
    private const double AddedAllPassFrequencyHz = 2000;
    private const double AddedAllPassQ = 1.0;

    private const double DefaultBandQ = 1.0;

    // ISO 266 1/3-octave centres, 16 Hz..20 kHz: 32 values match the maximum bank; used for whole-bank spreads.
    private static readonly double[] IsoThirdOctaveCentersHz =
    {
        16, 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500,
        630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000,
        10000, 12500, 16000, 20000
    };

    private readonly List<PeqBand> bands = [];
    private readonly PeqBankHistory history = new();
    private readonly Action changed;
    private NumericFieldRange gain;
    private PeqBankState committed = PeqBankState.Empty;

    /// <param name="changed">Told after every change the settings file keeps.</param>
    public EqWizardBank(NumericFieldRange gain, Action changed)
    {
        this.gain = gain;
        this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    public IReadOnlyList<PeqBand> Bands => bands;

    public double PreampDb { get; private set; }

    public NumericFieldRange GainRange => gain;

    public bool IsEmpty => bands.Count == 0 && PreampDb == 0;

    public bool CanUndo => history.CanUndo;

    public bool CanRedo => history.CanRedo;

    public PeqBankState State => new(bands, PreampDb);

    /// <summary>Every band is a filter: an unwanted one is removed, not parked.</summary>
    public EqualizationCurve Curve => new(bands, PreampDb);

    /// <summary>A value typed into a strip; it becomes an undo step when <see cref="Commit"/> lands it.</summary>
    public void Edit(int index, PeqBand band) => bands[index] = EqWizardLimits.Normalize(band, gain);

    /// <inheritdoc cref="Edit"/>
    public void EditPreamp(double preampDb) => PreampDb = (double)EqWizardLimits.Preamp.Clamp(preampDb);

    /// <summary>Moves every band's gain field; a gain left outside is clamped as a pending edit.</summary>
    /// <returns>Whether a gain was clamped.</returns>
    public bool SetGainRange(NumericFieldRange range)
    {
        gain = range;
        bool clamped = false;
        for (int index = 0; index < bands.Count; index++)
        {
            double contained = (double)range.Contain((decimal)bands[index].GainDb);
            if (contained != bands[index].GainDb)
            {
                bands[index] = bands[index] with { GainDb = contained };
                clamped = true;
            }
        }

        return clamped;
    }

    /// <summary>Lands the pending edit as one undo step. Also called before every structural change, so a half-made edit does not ride along with it.</summary>
    /// <returns>False when nothing changed since the last step.</returns>
    public bool Commit()
    {
        PeqBankState current = State;
        if (current.Equals(committed))
        {
            return false;
        }

        history.Push(committed);
        committed = current;
        changed();
        return true;
    }

    /// <summary>Creates or trims the whole bank, spreading new bands over the ISO third-octave centres.</summary>
    public bool SetCount(int count)
    {
        count = Math.Clamp(count, 0, EqWizardLimits.MaxBands);
        if (count == bands.Count)
        {
            return false;
        }

        Commit();
        while (bands.Count > count)
        {
            bands.RemoveAt(bands.Count - 1);
        }

        while (bands.Count < count)
        {
            bands.Add(EqWizardLimits.Normalize(DefaultBand(bands.Count), gain));
        }

        Commit();
        return true;
    }

    /// <returns>The new band's index, or -1 when the bank is full.</returns>
    public int Add(PeqBandType type)
    {
        if (bands.Count >= EqWizardLimits.MaxBands)
        {
            return -1;
        }

        Commit();
        bands.Add(EqWizardLimits.Normalize(NewBand(type), gain));
        Commit();
        return bands.Count - 1;
    }

    /// <summary>Keeps frequency, Q and gain: a bell and a shelf at the same corner are what a tuner compares.</summary>
    public bool SetType(int index, PeqBandType type)
    {
        if (bands[index].Type == type)
        {
            return false;
        }

        Commit();
        bands[index] = bands[index] with { Type = type };
        Commit();
        return true;
    }

    /// <summary>Part of a drag, which re-orders live and ends with <see cref="Commit"/>.</summary>
    public void Move(int from, int to)
    {
        PeqBand band = bands[from];
        bands.RemoveAt(from);
        bands.Insert(Math.Clamp(to, 0, bands.Count), band);
    }

    /// <inheritdoc cref="Move"/>
    public void Remove(int index) => bands.RemoveAt(index);

    /// <summary>A whole new bank (Auto Tune, an import, a handoff's seed) as one step; bands past the maximum are dropped.</summary>
    public void Replace(EqualizationCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        Commit();
        Write(curve.Bands.Take(EqWizardLimits.MaxBands), curve.PreampDb);
        Commit();
    }

    /// <summary>No filters, preamp 0 dB; one step, so undo brings them back.</summary>
    public void Clear()
    {
        Commit();
        Write([], 0);
        Commit();
    }

    public bool Undo()
    {
        Commit();
        if (!history.TryUndo(committed, out PeqBankState previous))
        {
            return false;
        }

        Restore(previous);
        return true;
    }

    public bool Redo()
    {
        Commit();
        if (!history.TryRedo(committed, out PeqBankState next))
        {
            return false;
        }

        Restore(next);
        return true;
    }

    /// <summary>A restored bank becomes the history's baseline: settings loaded from disk are not an edit to undo into.</summary>
    public void Load(IEnumerable<PeqBand> restored, double preampDb)
    {
        Write(restored, preampDb);
        changed();
        history.Clear();
        committed = State;
    }

    /// <summary>Old settings files carry only a count; they rebuild the ISO-centred spread.</summary>
    public static IEnumerable<PeqBand> DefaultBands(int count) =>
        Enumerable
            .Range(0, Math.Clamp(count, 0, EqWizardLimits.MaxBands))
            .Select(DefaultBand);

    private static PeqBand DefaultBand(int index) =>
        new(IsoThirdOctaveCentersHz[Math.Clamp(index, 0, IsoThirdOctaveCentersHz.Length - 1)], DefaultBandQ, 0);

    private static PeqBand NewBand(PeqBandType type) => type switch
    {
        PeqBandType.LowShelf =>
            new PeqBand(AddedLowShelfFrequencyHz, AddedShelfQ, 0, type),
        PeqBandType.HighShelf =>
            new PeqBand(AddedHighShelfFrequencyHz, AddedShelfQ, 0, type),
        PeqBandType.AllPassFirstOrder or PeqBandType.AllPassSecondOrder =>
            new PeqBand(AddedAllPassFrequencyHz, AddedAllPassQ, 0, type),
        _ => new PeqBand(AddedBandFrequencyHz, AddedBandQ, 0, type)
    };

    // What the strips hold after clamping becomes the baseline, or the next commit would record a phantom step and drop the redo trail.
    private void Restore(PeqBankState state)
    {
        Write(state.Bands, state.PreampDb);
        changed();
        committed = State;
    }

    // Held as the strips would hold it, so a hand-edited file loses a value, not the bank.
    private void Write(IEnumerable<PeqBand> newBands, double preampDb)
    {
        List<PeqBand> normalized = newBands.Select(band => EqWizardLimits.Normalize(band, gain)).ToList();
        bands.Clear();
        bands.AddRange(normalized);
        PreampDb = (double)EqWizardLimits.Preamp.Clamp(preampDb);
    }
}
