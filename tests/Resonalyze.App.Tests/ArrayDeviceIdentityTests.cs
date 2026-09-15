namespace Resonalyze.App.Tests;

/// <summary>Two 8-input interfaces share channel numbers, so swapping them passes the reachability guard; the array is tied to its device.</summary>
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
        Assert.Empty(Channels(Configured("Interface B", "Interface A")));
    }

    [Fact]
    public void AnArrayConfiguredBeforeTheStampExistedIsStillRecorded()
    {
        Assert.Equal([2, 3], Channels(Configured("Interface A", configuredOn: null)));
    }

    [Fact]
    public void TheVerdictDoesNotDependOnTheDeviceBeingPluggedInNow()
    {
        // Compared against the device the settings name, so an unplugged interface keeps its array.
        Assert.Equal(
            [2, 3],
            Channels(Configured("Interface Nobody Has Installed", "Interface Nobody Has Installed")));
    }

    [Fact]
    public void CarryingSettingsForwardCarriesTheDeviceToo()
    {
        // Capture rebuilds calibration from previous settings; positions without their stamp would look freshly configured.
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
