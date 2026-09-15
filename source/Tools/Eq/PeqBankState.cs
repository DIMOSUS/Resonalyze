using Resonalyze.Dsp;

namespace Resonalyze;

/// <summary>Bank snapshot; slot order is part of the state (exports number filters by it), so reordering is undoable.</summary>
internal sealed class PeqBankState : IEquatable<PeqBankState>
{
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
