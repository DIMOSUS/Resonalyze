using System.Reflection;
using System.Runtime.CompilerServices;
using Resonalyze.Dsp;
using Resonalyze.Options;

namespace Resonalyze.App.Tests;

public sealed class LiveSpectrumControllerTests
{
    [Fact]
    public void MissingEffectiveSplCalibration_NormalizesSilentToPeriodicPink()
    {
        var options = new LiveSpectrumOptions
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.Silent
        };

        bool changed = LiveSpectrumController.NormalizeSignalType(
            options,
            MagnitudeScale.Relative);

        Assert.True(changed);
        Assert.Equal(NoiseColor.PinkPeriodic, options.NoiseColor);
    }

    [Fact]
    public void NormalizeSignalType_KeepsSilentWhileSplIsEffective()
    {
        var options = new LiveSpectrumOptions { NoiseColor = NoiseColor.Silent };

        bool changed = LiveSpectrumController.NormalizeSignalType(
            options,
            MagnitudeScale.SoundPressureLevel);

        Assert.False(changed);
        Assert.Equal(NoiseColor.Silent, options.NoiseColor);
    }

    [Fact]
    public void NormalizeSignalType_LeavesANonSilentSignalUntouched()
    {
        var options = new LiveSpectrumOptions { NoiseColor = NoiseColor.Pink };

        bool changed = LiveSpectrumController.NormalizeSignalType(
            options,
            MagnitudeScale.Relative);

        Assert.False(changed);
        Assert.Equal(NoiseColor.Pink, options.NoiseColor);
    }

    [Fact]
    public void RestoredEffectiveSpl_NormalizesPeriodicPinkBackToSilent()
    {
        // The symmetric half of the fallback: after a Silent→pink calibration-loss the
        // stored signal is periodic pink while the requested scale is still SPL. When SPL
        // becomes effective again, periodic pink is invalid there (it is the transfer
        // reference, pointless in the reference-free RTA), so it must swap back to Silent
        // — never left playing an excitation the SPL panel cannot even display.
        var options = new LiveSpectrumOptions
        {
            MagnitudeScale = MagnitudeScale.SoundPressureLevel,
            NoiseColor = NoiseColor.PinkPeriodic
        };

        bool changed = LiveSpectrumController.NormalizeSignalType(
            options,
            MagnitudeScale.SoundPressureLevel);

        Assert.True(changed);
        Assert.Equal(NoiseColor.Silent, options.NoiseColor);
    }

    [Fact]
    public void NormalizeSignalType_KeepsPeriodicPinkOnTheRelativeScale()
    {
        var options = new LiveSpectrumOptions { NoiseColor = NoiseColor.PinkPeriodic };

        bool changed = LiveSpectrumController.NormalizeSignalType(
            options,
            MagnitudeScale.Relative);

        Assert.False(changed);
        Assert.Equal(NoiseColor.PinkPeriodic, options.NoiseColor);
    }

    [Fact]
    public void CalibrationInvalidatedOutsideLiveSpectrum_DropsPeakHoldBeforeRestore()
    {
        var controller = (LiveSpectrumController)RuntimeHelpers.GetUninitializedObject(
            typeof(LiveSpectrumController));
        FieldInfo peakHoldField = typeof(LiveSpectrumController).GetField(
            "peakHoldPoints",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        peakHoldField.SetValue(
            controller,
            new List<SignalPoint> { new(1000.0, 85.0) });

        // PersistCalibration invokes this even when another mode owns the plot.
        controller.InvalidateCalibration();

        Assert.Null(peakHoldField.GetValue(controller));
    }
}
