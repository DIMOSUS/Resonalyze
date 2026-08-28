namespace Resonalyze.App.Tests;

/// <summary>
/// An array belongs to the device it was configured on, not just to the backend.
/// </summary>
/// <remarks>
/// Two interfaces with eight inputs each present the same channel NUMBERS. Swapping
/// one for the other therefore leaves every configured position reachable, every
/// calibration attached and every note intact, while every microphone points at a
/// different physical input — and the measurement succeeds, with curves that look
/// entirely ordinary. The reachability guard cannot catch it: it asks whether the
/// input exists, and it does.
/// </remarks>
public sealed class ArrayDeviceIdentityTests
{
    private static MeasurementSettingsFile.SweepMeasurementSettings Configured(
        string? driverName,
        string? configuredOn) =>
        new()
        {
            AudioBackend = AudioBackend.Asio,
            AsioDriverName = driverName,
            AsioInputChannelOffset = 0,
            AsioLoopbackInputChannelOffset = 1,
            AsioArrayDeviceId = configuredOn,
            AsioArrayMicrophones =
            [
                new ArrayMicrophoneDefinition { ChannelOffset = 2 },
                new ArrayMicrophoneDefinition { ChannelOffset = 3 }
            ]
        };

    private static IReadOnlyList<int> Channels(
        MeasurementSettingsFile.SweepMeasurementSettings settings) =>
        settings.BuildConfiguration().Audio.AsioArrayInputChannelOffsets ?? [];

    [Fact]
    public void TheArrayIsRecordedOnTheDeviceItWasConfiguredOn()
    {
        Assert.Equal([2, 3], Channels(Configured("Interface A", "Interface A")));
    }

    [Fact]
    public void ADifferentDeviceRecordsNoArray()
    {
        // Fail-closed, and it has to be: the alternative is a measurement that
        // succeeds while its positions describe inputs the user never chose.
        Assert.Empty(Channels(Configured("Interface B", "Interface A")));
    }

    [Fact]
    public void AnArrayConfiguredBeforeTheStampExistedIsStillRecorded()
    {
        // Nothing to compare is not a mismatch. Refusing what cannot be checked
        // would throw away a working setup in the name of protecting it.
        Assert.Equal([2, 3], Channels(Configured("Interface A", configuredOn: null)));
    }

    [Fact]
    public void TheVerdictDoesNotDependOnTheDeviceBeingPluggedInNow()
    {
        // The comparison is against the device the settings NAME, not the one they
        // resolve to: an interface that is unplugged right now still owns its array,
        // the same permissiveness the reachability check keeps for the same reason.
        Assert.Equal(
            [2, 3],
            Channels(Configured("Interface Nobody Has Installed", "Interface Nobody Has Installed")));
    }

    [Fact]
    public void CarryingSettingsForwardCarriesTheDeviceToo()
    {
        // A capture rebuilds the calibration section from the previous settings. If
        // the positions travelled without their stamp, a stale array would arrive
        // looking freshly configured for whatever device is selected now.
        MeasurementSettingsFile.SweepMeasurementSettings previous =
            Configured("Interface A", "Interface A");
        var current = new MeasurementSettingsFile.SweepMeasurementSettings
        {
            AudioBackend = AudioBackend.Asio,
            AsioDriverName = "Interface B",
            AsioInputChannelOffset = 0,
            AsioLoopbackInputChannelOffset = 1
        };

        current.CopyCalibrationFrom(previous);

        Assert.Equal("Interface A", current.AsioArrayDeviceId);
        Assert.Empty(Channels(current));
    }
}
