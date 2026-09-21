namespace Resonalyze.Options;

/// <summary>The protective high-pass the session holds, as the measurement divides it out.</summary>
internal static class RecordHighPass
{
    public static ProtectiveHighPassKind SelectedKind(RecordSettingsSession session) =>
        session.HighPassKind.SelectedItem is ProtectiveHighPassKind kind ? kind : ProtectiveHighPassKind.Off;

    /// <summary>Corner and slope mean nothing while the filter is off.</summary>
    public static bool IsEditable(RecordSettingsSession session) =>
        SelectedKind(session) != ProtectiveHighPassKind.Off;

    public static ProtectiveHighPassConfiguration Read(RecordSettingsSession session) =>
        ProtectiveHighPassConfiguration.Normalize(
            new ProtectiveHighPassConfiguration(
                SelectedKind(session),
                (double)session.HighPassFrequency.Value,
                session.HighPassSlope.SelectedItem is int slope ? slope : 24));

    public static string KindLabel(ProtectiveHighPassKind kind) => kind switch
    {
        ProtectiveHighPassKind.LinkwitzRiley => "Linkwitz-Riley",
        _ => kind.ToString()
    };

    public static string SlopeLabel(int slope) => $"{slope} dB/oct";
}
