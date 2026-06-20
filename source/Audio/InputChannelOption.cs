namespace Resonalyze;

public sealed record InputChannelOption(int? Offset, string Name)
{
    public override string ToString() => Name;
}
