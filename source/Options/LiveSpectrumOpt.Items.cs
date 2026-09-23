namespace Resonalyze.Options
{
    public partial class LiveSpectrumOpt
    {
        private sealed record Choice<T>(T Value, string Label)
        {
            public override string ToString() => Label;
        }
    }
}
