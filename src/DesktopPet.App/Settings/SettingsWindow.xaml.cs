using DesktopPet.App.Cloud;
using System.Windows;
using System.Windows.Input;

namespace DesktopPet.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly ElevenLabsSettingsStore _elevenLabsSettingsStore;
    private readonly UiSettingsStore _uiSettingsStore;
    private readonly ProfileSettingsStore _profileSettingsStore;
    private readonly Func<UiSettings, string?> _applyUiSettings;
    private readonly Func<string?> _getHotkeyWarning;
    private KeyboardShortcut _selectedChatShortcut = KeyboardShortcut.DefaultChatShortcut;
    private bool _isRecordingShortcut;

    public SettingsWindow(
        ElevenLabsSettingsStore elevenLabsSettingsStore,
        UiSettingsStore uiSettingsStore,
        ProfileSettingsStore profileSettingsStore,
        Func<UiSettings, string?> applyUiSettings,
        Func<string?> getHotkeyWarning)
    {
        _elevenLabsSettingsStore = elevenLabsSettingsStore;
        _uiSettingsStore = uiSettingsStore;
        _profileSettingsStore = profileSettingsStore;
        _applyUiSettings = applyUiSettings;
        _getHotkeyWarning = getHotkeyWarning;

        InitializeComponent();
        LoadSettings();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _elevenLabsSettingsStore.Save(new ElevenLabsSettings(
                ToNullIfWhiteSpace(ElevenLabsApiKeyTextBox.Text),
                ToNullIfWhiteSpace(ElevenLabsAgentIdTextBox.Text),
                ToNullIfWhiteSpace(ElevenLabsVoiceIdTextBox.Text)));

            _profileSettingsStore.Save(new ProfileSettings(
                ToNullIfWhiteSpace(UserNameTextBox.Text),
                ToNullIfWhiteSpace(NicknameTextBox.Text),
                GetSelectedPersonalityTone()));

            var currentUiSettings = _uiSettingsStore.Load();
            var uiSettings = currentUiSettings with
            {
                ChatShortcut = _selectedChatShortcut
            };
            _uiSettingsStore.Save(uiSettings);

            var hotkeyWarning = _applyUiSettings(uiSettings);
            StatusTextBlock.Text = hotkeyWarning is null
                ? "Saved."
                : $"Saved, but {hotkeyWarning}";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Save failed: {ex.Message}";
        }
    }

    private void LoadSettings()
    {
        var settings = _elevenLabsSettingsStore.Load();
        ElevenLabsApiKeyTextBox.Text = settings.ElevenLabsApiKey ?? string.Empty;
        ElevenLabsAgentIdTextBox.Text = settings.ElevenLabsAgentId ?? string.Empty;
        ElevenLabsVoiceIdTextBox.Text = settings.ElevenLabsVoiceId ?? string.Empty;

        var profileSettings = _profileSettingsStore.Load();
        UserNameTextBox.Text = profileSettings.UserName ?? string.Empty;
        NicknameTextBox.Text = profileSettings.Nickname ?? string.Empty;
        SelectPersonalityTone(profileSettings.PersonalityTone);

        _selectedChatShortcut = _uiSettingsStore.Load().ChatShortcut;
        UpdateShortcutButton();

        var hotkeyWarning = _getHotkeyWarning();
        if (!string.IsNullOrWhiteSpace(hotkeyWarning))
        {
            StatusTextBlock.Text = hotkeyWarning;
        }
    }

    private static string? ToNullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private string? GetSelectedPersonalityTone()
    {
        var selectedTone = PersonalityToneComboBox.SelectedItem as string;
        return string.IsNullOrWhiteSpace(selectedTone) ? null : selectedTone;
    }

    private void SelectPersonalityTone(string? tone)
    {
        PersonalityToneComboBox.ItemsSource = ProfileSettings.PersonalityTones;

        if (string.IsNullOrWhiteSpace(tone)
            || !ProfileSettings.PersonalityTones.Contains(tone))
        {
            PersonalityToneComboBox.SelectedIndex = -1;
            return;
        }

        PersonalityToneComboBox.SelectedItem = tone;
    }

    private void OnRecordShortcutClicked(object sender, RoutedEventArgs e)
    {
        _isRecordingShortcut = true;
        ChatShortcutButton.Content = "Press shortcut...";
        StatusTextBlock.Text = "Press a key with Ctrl, Alt, Shift, or Win. Esc cancels.";
        ChatShortcutButton.Focus();
    }

    private void OnResetShortcutClicked(object sender, RoutedEventArgs e)
    {
        _isRecordingShortcut = false;
        _selectedChatShortcut = KeyboardShortcut.DefaultChatShortcut;
        UpdateShortcutButton();
        StatusTextBlock.Text = "Shortcut reset. Save to apply.";
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRecordingShortcut)
        {
            return;
        }

        e.Handled = true;

        var key = GetRealKey(e);
        if (key == Key.Escape)
        {
            _isRecordingShortcut = false;
            UpdateShortcutButton();
            StatusTextBlock.Text = "Shortcut recording cancelled.";
            return;
        }

        if (KeyboardShortcut.IsModifierKey(key))
        {
            StatusTextBlock.Text = "Press a non-modifier key too.";
            return;
        }

        var shortcut = KeyboardShortcut.FromWpfInput(key, Keyboard.Modifiers);
        if (!shortcut.IsValid())
        {
            StatusTextBlock.Text = "Shortcut must include Ctrl, Alt, Shift, or Win.";
            return;
        }

        _selectedChatShortcut = shortcut;
        _isRecordingShortcut = false;
        UpdateShortcutButton();
        StatusTextBlock.Text = "Shortcut captured. Save to apply.";
    }

    private void UpdateShortcutButton()
    {
        ChatShortcutButton.Content = _selectedChatShortcut.DisplayText;
    }

    private static Key GetRealKey(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.System)
        {
            return e.SystemKey;
        }

        return e.Key == Key.ImeProcessed
            ? e.ImeProcessedKey
            : e.Key;
    }
}
