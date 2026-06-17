namespace DesktopPet.App.Settings;

public sealed record PetProfileSettings(
    string? UserName,
    string? PetNickname,
    string? PersonalityTone)
{
    public static PetProfileSettings Default { get; } = new(null, null, null);

    public static IReadOnlyList<string> PersonalityTones { get; } =
    [
        "Playful",
        "Calm",
        "Encouraging",
        "Dry",
        "Gentle"
    ];
}
