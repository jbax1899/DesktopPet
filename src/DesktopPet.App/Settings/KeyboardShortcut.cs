using System.Windows.Input;
using System.Text.Json.Serialization;

namespace DesktopPet.App.Settings;

public sealed record KeyboardShortcut(
    string Key,
    bool Control,
    bool Shift,
    bool Alt,
    bool Windows)
{
    public static KeyboardShortcut DefaultChatShortcut { get; } = new("Space", false, true, false, false);

    public static KeyboardShortcut DefaultPushToTalkShortcut { get; } = new("Space", true, false, false, false);

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Control)
            {
                parts.Add("Ctrl");
            }

            if (Shift)
            {
                parts.Add("Shift");
            }

            if (Alt)
            {
                parts.Add("Alt");
            }

            if (Windows)
            {
                parts.Add("Win");
            }

            parts.Add(GetDisplayKey());
            return string.Join("+", parts);
        }
    }

    public bool TryGetWpfKey(out Key key)
    {
        return Enum.TryParse(Key, ignoreCase: true, out key)
            && key != System.Windows.Input.Key.None
            && !IsModifierKey(key);
    }

    public static KeyboardShortcut FromWpfInput(Key key, ModifierKeys modifiers)
    {
        return new KeyboardShortcut(
            key.ToString(),
            modifiers.HasFlag(ModifierKeys.Control),
            modifiers.HasFlag(ModifierKeys.Shift),
            modifiers.HasFlag(ModifierKeys.Alt),
            modifiers.HasFlag(ModifierKeys.Windows));
    }

    public static bool IsModifierKey(Key key)
    {
        return key is System.Windows.Input.Key.LeftCtrl
            or System.Windows.Input.Key.RightCtrl
            or System.Windows.Input.Key.LeftShift
            or System.Windows.Input.Key.RightShift
            or System.Windows.Input.Key.LeftAlt
            or System.Windows.Input.Key.RightAlt
            or System.Windows.Input.Key.LWin
            or System.Windows.Input.Key.RWin;
    }

    public bool IsValid()
    {
        return (Control || Shift || Alt || Windows) && TryGetWpfKey(out _);
    }

    private string GetDisplayKey()
    {
        return TryGetWpfKey(out var key) && key == System.Windows.Input.Key.Space
            ? "Space"
            : Key;
    }
}
