using Resonalyze.Dsp;

namespace Resonalyze.Options
{
    public partial class LiveSpectrumOpt
    {
        private sealed class SequenceLengthOption
        {
            private readonly int sampleRateHz;

            public SequenceLengthOption(int length, int sampleRateHz)
            {
                Length = length;
                this.sampleRateHz = sampleRateHz;
            }

            public int Length { get; }

            public override string ToString() =>
                LiveSpectrumSettingsChoices.SequenceLengthLabel(Length, sampleRateHz);
        }

        private sealed class CoherenceLimitOption
        {
            public CoherenceLimitOption(int percent)
            {
                Percent = percent;
            }

            public int Percent { get; }

            public override string ToString() => LiveSpectrumSettingsChoices.PercentLabel(Percent);
        }

        private sealed class WindowOption
        {
            public WindowOption(WindowType windowType, string displayName)
            {
                WindowType = windowType;
                DisplayName = displayName;
            }

            public WindowType WindowType { get; }

            public string DisplayName { get; }

            public override string ToString() => DisplayName;
        }

        private sealed class AveragingOption
        {
            public AveragingOption(AveragingSpeed speed, string displayName)
            {
                Speed = speed;
                DisplayName = displayName;
            }

            public AveragingSpeed Speed { get; }

            public string DisplayName { get; }

            public override string ToString() => DisplayName;
        }

        private sealed class OverlapOption
        {
            public OverlapOption(int percent)
            {
                Percent = percent;
            }

            public int Percent { get; }

            public override string ToString() => LiveSpectrumSettingsChoices.PercentLabel(Percent);
        }

        private sealed class NoiseColorOption
        {
            public NoiseColorOption(NoiseColor noiseColor, string displayName)
            {
                NoiseColor = noiseColor;
                DisplayName = displayName;
            }

            public NoiseColor NoiseColor { get; }

            public string DisplayName { get; }

            public override string ToString() => DisplayName;
        }
    }
}
