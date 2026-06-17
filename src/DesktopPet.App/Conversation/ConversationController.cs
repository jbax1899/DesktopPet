using DesktopPet.App.Cloud;
using DesktopPet.App.Errors;
using DesktopPet.App.Overlay;
using DesktopPet.App.Settings;
using DesktopPet.App.Voice;
using System.Diagnostics;

namespace DesktopPet.App.Conversation;

public sealed class ConversationController : IDisposable
{
    private static readonly TimeSpan TranscriptHoldAfterSpeech = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ErrorMoodDuration = TimeSpan.FromSeconds(2.5);

    private readonly ConversationOverlayWindow _overlayWindow;
    private readonly IChatService _chatService;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly Func<ProfileSettings> _profileSettingsProvider;
    private readonly StreamingPcmAudioPlayer _audioPlayer;
    private readonly ICharacterStateController _characterStateController;
    private readonly CharacterErrorMessageStore _errorMessageStore;
    private readonly SemaphoreSlim _playbackGate = new(1, 1);

    private int _newestSubmittedTurnId;
    private int _activeTranscriptTurnId;
    private CancellationTokenSource? _currentPlaybackCancellation;
    private Task _currentPlaybackTask = Task.CompletedTask;
    private bool _isDisposed;

    public ConversationController(
        ConversationOverlayWindow overlayWindow,
        IChatService chatService,
        IVoiceSynthesisService voiceSynthesisService,
        Func<ProfileSettings> profileSettingsProvider,
        StreamingPcmAudioPlayer audioPlayer,
        ICharacterStateController characterStateController,
        CharacterErrorMessageStore errorMessageStore)
    {
        _overlayWindow = overlayWindow;
        _chatService = chatService;
        _voiceSynthesisService = voiceSynthesisService;
        _profileSettingsProvider = profileSettingsProvider;
        _audioPlayer = audioPlayer;
        _characterStateController = characterStateController;
        _errorMessageStore = errorMessageStore;

        _overlayWindow.MessageSubmitted += OnMessageSubmitted;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _overlayWindow.MessageSubmitted -= OnMessageSubmitted;
        _currentPlaybackCancellation?.Cancel();
        _currentPlaybackCancellation?.Dispose();
        _playbackGate.Dispose();
        _isDisposed = true;
    }

    private async void OnMessageSubmitted(object? sender, string message)
    {
        await SubmitAsync(message);
    }

    private async Task SubmitAsync(string message)
    {
        var turnId = Interlocked.Increment(ref _newestSubmittedTurnId);
        _overlayWindow.SetRequestPending(isPending: true);

        using var thinking = _characterStateController.BeginMood(PetMood.Thinking);
        try
        {
            VoiceSynthesisResult? audio = null;
            try
            {
                var reply = await _chatService.ReplyAsync(
                    new ChatRequest(message, _profileSettingsProvider()),
                    CancellationToken.None);
                audio = await _voiceSynthesisService.SynthesizeAsync(new VoiceSynthesisRequest(reply.Text), CancellationToken.None);

                if (turnId != Volatile.Read(ref _newestSubmittedTurnId))
                {
                    return;
                }

                await ReplaceCurrentSpeechAsync(turnId, reply.Text, audio);
                audio = null;
            }
            finally
            {
                if (audio is not null)
                {
                    await audio.DisposeAsync();
                }
            }
        }
        catch (Exception ex)
        {
            if (turnId == Volatile.Read(ref _newestSubmittedTurnId))
            {
                ShowError(ex, PetErrorCode.ChatFailed);
                _characterStateController.ShowTemporaryMood(PetMood.Alarmed, ErrorMoodDuration);
            }
        }
        finally
        {
            _overlayWindow.SetRequestPending(isPending: false);
        }
    }

    private async Task ReplaceCurrentSpeechAsync(int turnId, string transcript, VoiceSynthesisResult audio)
    {
        await _playbackGate.WaitAsync();
        var playbackStarted = false;

        try
        {
            if (turnId != Volatile.Read(ref _newestSubmittedTurnId))
            {
                return;
            }

            _currentPlaybackCancellation?.Cancel();
            await IgnoreCancellationAsync(_currentPlaybackTask);

            if (turnId != Volatile.Read(ref _newestSubmittedTurnId))
            {
                return;
            }

            _activeTranscriptTurnId = turnId;
            _overlayWindow.ShowTranscript(transcript);

            _currentPlaybackCancellation?.Dispose();
            _currentPlaybackCancellation = new CancellationTokenSource();
            _currentPlaybackTask = PlaySpeechAsync(turnId, audio, _currentPlaybackCancellation.Token);
            playbackStarted = true;
        }
        finally
        {
            if (!playbackStarted)
            {
                await audio.DisposeAsync();
            }

            _playbackGate.Release();
        }
    }

    private async Task PlaySpeechAsync(int turnId, VoiceSynthesisResult audio, CancellationToken cancellationToken)
    {
        await using (audio)
        {
            try
            {
                using (var speaking = _characterStateController.BeginSpeaking())
                {
                    await _audioPlayer.PlayAsync(
                        audio.AudioStream,
                        audio.AudioFormat,
                        audio.SampleRate,
                        audio.BitsPerSample,
                        audio.Channels,
                        cancellationToken,
                        speaking.SetMouthOpen);
                }

                await Task.Delay(TranscriptHoldAfterSpeech, cancellationToken);
                if (turnId == Volatile.Read(ref _activeTranscriptTurnId))
                {
                    _overlayWindow.HideTranscript();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (turnId == Volatile.Read(ref _activeTranscriptTurnId))
                {
                    ShowError(ex, PetErrorCode.PlaybackFailed);
                    _characterStateController.ShowTemporaryMood(PetMood.Alarmed, ErrorMoodDuration);
                }
            }
        }
    }

    private void ShowError(Exception exception, PetErrorCode fallbackCode)
    {
        var error = PetError.FromException(exception, fallbackCode);
        Debug.WriteLine($"DesktopPet error ({error.Code}): {error.TechnicalMessage}");
        _overlayWindow.ShowError(_errorMessageStore.GetMessage(error.Code));
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
