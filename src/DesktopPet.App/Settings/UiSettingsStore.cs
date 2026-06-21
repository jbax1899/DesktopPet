using System.IO;
using System.Text.Json;
using DesktopPet.App.Storage;

namespace DesktopPet.App.Settings;

public sealed class UiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly JsonFileStore<UiSettings> _settingsFile;

    public UiSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPet",
            "ui-settings.json"))
    {
    }

    internal UiSettingsStore(string settingsFilePath)
    {
        _settingsFile = new JsonFileStore<UiSettings>(
            settingsFilePath,
            json => JsonSerializer.Deserialize<UiSettings>(json, JsonOptions)
                ?? throw new JsonException("UI settings are empty."),
            settings => JsonSerializer.Serialize(settings, JsonOptions));
    }

    public UiSettings Load()
    {
        var settings = _settingsFile.Load(UiSettings.Default);
        return settings with
        {
            ChatShortcut = settings.ChatShortcut?.IsValid() == true
                ? settings.ChatShortcut
                : KeyboardShortcut.DefaultChatShortcut,
            PushToTalkShortcut = settings.PushToTalkShortcut?.IsValid() == true
                ? settings.PushToTalkShortcut
                : KeyboardShortcut.DefaultPushToTalkShortcut,
            ChatHistoryContext = settings.GetEffectiveChatHistoryContext()
        };
    }

    public void Save(UiSettings settings)
    {
        var normalizedSettings = settings with
        {
            ChatHistoryContext = settings.GetEffectiveChatHistoryContext()
        };
        _settingsFile.Save(normalizedSettings);
    }
}
