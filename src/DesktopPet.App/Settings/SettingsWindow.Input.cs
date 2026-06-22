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
        StatusTextBlock.Text = "Press a key with Ctrl, Alt, Shift, or Win. Esc cancels.";
        ChatShortcutButton.Focus();
    }

    private void OnResetChatShortcutClicked(object sender, RoutedEventArgs e)
    {
        _isRecordingShortcut = false;
        _recordingTarget = ShortcutTarget.None;
        _selectedChatShortcut = KeyboardShortcut.DefaultChatShortcut;
        UpdateShortcutButtons();
        StatusTextBlock.Text = "Shortcut reset. Save to apply.";
    }

    private void OnRecordPushToTalkShortcutClicked(object sender, RoutedEventArgs e)
    {
        _recordingTarget = ShortcutTarget.PushToTalk;
        _isRecordingShortcut = true;
        PushToTalkShortcutButton.Content = "Press shortcut...";
        StatusTextBlock.Text = "Press a key with Ctrl, Alt, Shift, or Win. Esc cancels.";
        PushToTalkShortcutButton.Focus();
    }

    private void OnResetPushToTalkShortcutClicked(object sender, RoutedEventArgs e)
    {
        _isRecordingShortcut = false;
        _recordingTarget = ShortcutTarget.None;
        _selectedPushToTalkShortcut = KeyboardShortcut.DefaultPushToTalkShortcut;
        UpdateShortcutButtons();
        StatusTextBlock.Text = "Mic hotkey reset. Save to apply.";
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
        StatusTextBlock.Text = "Shortcut captured. Save to apply.";
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
