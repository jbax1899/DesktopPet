namespace DesktopPet.App.Settings;

public sealed record ProfileSettings(
    string? UserName,
    string? Nickname,
    string? PersonalityTone)
{
    public static ProfileSettings Default { get; } = new(null, null, null);

    public static IReadOnlyList<string> PersonalityTones { get; } =
    [
        "Playful",
        "Calm",
        "Encouraging",
        "Dry",
        "Gentle"
    ];
}
