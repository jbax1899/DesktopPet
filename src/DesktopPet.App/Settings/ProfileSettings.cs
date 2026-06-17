namespace DesktopPet.App.Settings;

public sealed record ProfileSettings(
    string? UserName,
    string? Nickname)
{
    public static ProfileSettings Default { get; } = new(null, null);
}
