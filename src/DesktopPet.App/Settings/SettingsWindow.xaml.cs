using DesktopPet.App.Cloud;
using DesktopPet.App.Errors;
using System.Windows;
using System.Windows.Input;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace DesktopPet.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly ElevenLabsSettingsStore _elevenLabsSettingsStore;
    private readonly UiSettingsStore _uiSettingsStore;
    private readonly ProfileSettingsStore _profileSettingsStore;
    private readonly CharacterErrorMessageStore _errorMessageStore;
    private readonly Func<UiSettings, PetError?> _applyUiSettings;
    private readonly Func<PetError?> _getHotkeyWarning;
    private readonly Action _showObservationSettings;
    private KeyboardShortcut _selectedChatShortcut = KeyboardShortcut.DefaultChatShortcut;
    private bool _isRecordingShortcut;

    public SettingsWindow(
        ElevenLabsSettingsStore elevenLabsSettingsStore,
        UiSettingsStore uiSettingsStore,
        ProfileSettingsStore profileSettingsStore,
        CharacterErrorMessageStore errorMessageStore,
        Func<UiSettings, PetError?> applyUiSettings,
        Func<PetError?> getHotkeyWarning,
        Action showObservationSettings)
    {
        _elevenLabsSettingsStore = elevenLabsSettingsStore;
        _uiSettingsStore = uiSettingsStore;
        _profileSettingsStore = profileSettingsStore;
        _errorMessageStore = errorMessageStore;
        _applyUiSettings = applyUiSettings;
        _getHotkeyWarning = getHotkeyWarning;
        _showObservationSettings = showObservationSettings;

        InitializeComponent();
        LoadSettings();
    }

    private void OnScreenContextClicked(object sender, RoutedEventArgs e)
    {
        _showObservationSettings();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var pronunciationDictionaries = BuildPronunciationDictionaries();

            _elevenLabsSettingsStore.Save(new ElevenLabsSettings(
                ToNullIfWhiteSpace(ElevenLabsApiKeyTextBox.Text),
                ToNullIfWhiteSpace(ElevenLabsAgentIdTextBox.Text),
                ToNullIfWhiteSpace(ElevenLabsVoiceIdTextBox.Text),
                pronunciationDictionaries));

            _profileSettingsStore.Save(new ProfileSettings(
                ToNullIfWhiteSpace(UserNameTextBox.Text),
                ToNullIfWhiteSpace(NicknameTextBox.Text)));

            var currentUiSettings = _uiSettingsStore.Load();
            var uiSettings = currentUiSettings with
            {
                ChatShortcut = _selectedChatShortcut
            };
            _uiSettingsStore.Save(uiSettings);

            var hotkeyWarning = _applyUiSettings(uiSettings);
            StatusTextBlock.Text = hotkeyWarning is null
                ? "Saved."
                : $"Saved, but {_errorMessageStore.GetMessage(hotkeyWarning.Code)}";
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
        LoadPronunciationDictionaries(settings.PronunciationDictionaries);

        var profileSettings = _profileSettingsStore.Load();
        UserNameTextBox.Text = profileSettings.UserName ?? string.Empty;
        NicknameTextBox.Text = profileSettings.Nickname ?? string.Empty;

        _selectedChatShortcut = _uiSettingsStore.Load().ChatShortcut;
        UpdateShortcutButton();

        var hotkeyWarning = _getHotkeyWarning();
        if (hotkeyWarning is not null)
        {
            StatusTextBlock.Text = _errorMessageStore.GetMessage(hotkeyWarning.Code);
        }
    }

    private static string? ToNullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private IReadOnlyList<ElevenLabsPronunciationDictionaryLocator> BuildPronunciationDictionaries()
    {
        var rows = new[]
        {
            ReadPronunciationDictionaryRow(1, PronunciationName1TextBox, PronunciationDictionaryId1TextBox, PronunciationVersionId1TextBox),
            ReadPronunciationDictionaryRow(2, PronunciationName2TextBox, PronunciationDictionaryId2TextBox, PronunciationVersionId2TextBox),
            ReadPronunciationDictionaryRow(3, PronunciationName3TextBox, PronunciationDictionaryId3TextBox, PronunciationVersionId3TextBox)
        };

        return rows
            .Where(row => row is not null)
            .Cast<ElevenLabsPronunciationDictionaryLocator>()
            .ToArray();
    }

    private static ElevenLabsPronunciationDictionaryLocator? ReadPronunciationDictionaryRow(
        int rowNumber,
        WpfTextBox nameTextBox,
        WpfTextBox dictionaryIdTextBox,
        WpfTextBox versionIdTextBox)
    {
        var displayName = ToNullIfWhiteSpace(nameTextBox.Text);
        var dictionaryId = ToNullIfWhiteSpace(dictionaryIdTextBox.Text);
        var versionId = ToNullIfWhiteSpace(versionIdTextBox.Text);

        if (dictionaryId is null && versionId is null)
        {
            if (displayName is not null)
            {
                throw new InvalidOperationException($"Dictionary {rowNumber} needs a dictionary ID and version ID.");
            }

            return null;
        }

        if (dictionaryId is null || versionId is null)
        {
            throw new InvalidOperationException($"Dictionary {rowNumber} needs both dictionary ID and version ID.");
        }

        return new ElevenLabsPronunciationDictionaryLocator(displayName, dictionaryId, versionId);
    }

    private void LoadPronunciationDictionaries(
        IReadOnlyList<ElevenLabsPronunciationDictionaryLocator>? pronunciationDictionaries)
    {
        var dictionaries = pronunciationDictionaries ?? [];

        LoadPronunciationDictionaryRow(dictionaries.ElementAtOrDefault(0), PronunciationName1TextBox, PronunciationDictionaryId1TextBox, PronunciationVersionId1TextBox);
        LoadPronunciationDictionaryRow(dictionaries.ElementAtOrDefault(1), PronunciationName2TextBox, PronunciationDictionaryId2TextBox, PronunciationVersionId2TextBox);
        LoadPronunciationDictionaryRow(dictionaries.ElementAtOrDefault(2), PronunciationName3TextBox, PronunciationDictionaryId3TextBox, PronunciationVersionId3TextBox);
    }

    private static void LoadPronunciationDictionaryRow(
        ElevenLabsPronunciationDictionaryLocator? locator,
        WpfTextBox nameTextBox,
        WpfTextBox dictionaryIdTextBox,
        WpfTextBox versionIdTextBox)
    {
        nameTextBox.Text = locator?.DisplayName ?? string.Empty;
        dictionaryIdTextBox.Text = locator?.PronunciationDictionaryId ?? string.Empty;
        versionIdTextBox.Text = locator?.VersionId ?? string.Empty;
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
