namespace Resonalyze;

/// <summary>A docked settings panel that says when the user changed a setting: the host applies on that instead of
/// watching the panel's controls, so code moving a field never applies.</summary>
internal interface IUserEditedSettings
{
    event Action? UserChanged;
}
