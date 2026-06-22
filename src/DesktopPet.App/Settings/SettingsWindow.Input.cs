using System.Windows;
using System.Windows.Input;

namespace DesktopPet.App.Settings;

public partial class SettingsWindow
{
    private void OnRecordChatShortcutClicked(object sender, RoutedEventArgs e)
    {
        _recordingTarget = ShortcutTarget.Chat;
        _isRecordingShortcut = true;
        ChatShortcutButton.Content = "Press shortcut...";
        ChatShortcutButton.Focus();
    }

    private void OnResetChatShortcutClicked(object sender, RoutedEventArgs e)
    {
        _isRecordingShortcut = false;
        _recordingTarget = ShortcutTarget.None;
        _selectedChatShortcut = KeyboardShortcut.DefaultChatShortcut;
        UpdateShortcutButtons();
        ApplyAllSettings();
    }

    private void OnRecordPushToTalkShortcutClicked(object sender, RoutedEventArgs e)
    {
        _recordingTarget = ShortcutTarget.PushToTalk;
        _isRecordingShortcut = true;
        PushToTalkShortcutButton.Content = "Press shortcut...";
        PushToTalkShortcutButton.Focus();
    }

    private void OnResetPushToTalkShortcutClicked(object sender, RoutedEventArgs e)
    {
        _isRecordingShortcut = false;
        _recordingTarget = ShortcutTarget.None;
        _selectedPushToTalkShortcut = KeyboardShortcut.DefaultPushToTalkShortcut;
        UpdateShortcutButtons();
        ApplyAllSettings();
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
            _recordingTarget = ShortcutTarget.None;
            UpdateShortcutButtons();
            return;
        }

        if (KeyboardShortcut.IsModifierKey(key))
        {
            return;
        }

        var shortcut = KeyboardShortcut.FromWpfInput(key, Keyboard.Modifiers);
        if (!shortcut.IsValid())
        {
            return;
        }

        switch (_recordingTarget)
        {
            case ShortcutTarget.Chat:
                _selectedChatShortcut = shortcut;
                break;
            case ShortcutTarget.PushToTalk:
                _selectedPushToTalkShortcut = shortcut;
                break;
        }

        _isRecordingShortcut = false;
        _recordingTarget = ShortcutTarget.None;
        UpdateShortcutButtons();
        ApplyAllSettings();
    }

    private void UpdateShortcutButtons()
    {
        ChatShortcutButton.Content = _selectedChatShortcut.DisplayText;
        PushToTalkShortcutButton.Content = _selectedPushToTalkShortcut.DisplayText;
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

    private enum ShortcutTarget
    {
        None,
        Chat,
        PushToTalk
    }
}
