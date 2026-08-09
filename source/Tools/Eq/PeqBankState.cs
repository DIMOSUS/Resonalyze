using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>
/// A snapshot of the EQ Wizard filter bank: the bands in slot order plus the
/// preamp. The order is part of the state, not a display detail — an exported
/// profile numbers its filters by it — so the same bands in a different order
/// are a different state, and re-ordering is undoable like any other edit.
/// </summary>
internal sealed class PeqBankState : IEquatable<PeqBankState>
{
    /// <summary>An empty bank with a neutral preamp: what "Reset filters" leaves.</summary>
    public static readonly PeqBankState Empty = new(Array.Empty<PeqBand>(), 0);

    private readonly PeqBand[] bands;

    public PeqBankState(IEnumerable<PeqBand> bands, double preampDb)
    {
        ArgumentNullException.ThrowIfNull(bands);

        this.bands = bands.ToArray();
        PreampDb = preampDb;
    }

    public IReadOnlyList<PeqBand> Bands => bands;

    public double PreampDb { get; }

    public bool Equals(PeqBankState? other) =>
        other != null &&
        PreampDb.Equals(other.PreampDb) &&
        bands.AsSpan().SequenceEqual(other.bands.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as PeqBankState);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(PreampDb);
        foreach (PeqBand band in bands)
        {
            hash.Add(band);
        }

        return hash.ToHashCode();
    }
}
