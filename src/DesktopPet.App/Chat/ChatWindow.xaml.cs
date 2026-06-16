using DesktopPet.App.Cloud;
using DesktopPet.App.Voice;
using System.Windows;
using System.Windows.Controls;

namespace DesktopPet.App.Chat;

public partial class ChatWindow : Window
{
    private readonly IPetChatService _chatService;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly TempFileAudioPlayer _audioPlayer;

    private bool _isBusy;

    public ChatWindow(
        IPetChatService chatService,
        IVoiceSynthesisService voiceSynthesisService,
        TempFileAudioPlayer audioPlayer)
    {
        _chatService = chatService;
        _voiceSynthesisService = voiceSynthesisService;
        _audioPlayer = audioPlayer;

        InitializeComponent();
        UpdateButtonState();
    }

    private async void OnSendClicked(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        await RunOperationAsync("Sending...", async cancellationToken =>
        {
            var reply = await _chatService.ReplyAsync(
                new PetChatRequest(UserMessageTextBox.Text),
                cancellationToken);

            ReplyTextBox.Text = reply.Text;
            StatusTextBlock.Text = "Done.";
        });
    }

    private async void OnSpeakReplyClicked(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        await RunOperationAsync("Speaking...", async cancellationToken =>
        {
            var audio = await _voiceSynthesisService.SynthesizeAsync(
                new VoiceSynthesisRequest(ReplyTextBox.Text),
                cancellationToken);

            await _audioPlayer.PlayAsync(audio.AudioBytes, audio.AudioFormat, cancellationToken);
            StatusTextBlock.Text = "Done.";
        });
    }

    private async Task RunOperationAsync(string status, Func<CancellationToken, Task> operation)
    {
        // Keep async button work in one place so failures show in the window.
        _isBusy = true;
        StatusTextBlock.Text = status;
        UpdateButtonState();

        try
        {
            await operation(CancellationToken.None);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Failed: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
            UpdateButtonState();
        }
    }

    private void UpdateButtonState()
    {
        SendButton.IsEnabled = !_isBusy;
        SpeakReplyButton.IsEnabled = !_isBusy && !string.IsNullOrWhiteSpace(ReplyTextBox.Text);
    }

    private void OnReplyTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateButtonState();
    }
}
