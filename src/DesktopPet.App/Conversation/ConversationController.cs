using DesktopPet.App.Cloud;
using DesktopPet.App.Errors;
using DesktopPet.App.Memory;
using DesktopPet.App.Overlay;
using DesktopPet.App.Observation;
using DesktopPet.App.Settings;
using DesktopPet.App.Voice;
using System.Diagnostics;
using System.IO;

namespace DesktopPet.App.Conversation;

public sealed class ConversationController : IDisposable
{
    private const int ObservationHistoryCount = 5;

    private static readonly TimeSpan TranscriptHoldAfterSpeech = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ErrorMoodDuration = TimeSpan.FromSeconds(2.5);

    private readonly ConversationOverlayWindow _overlayWindow;
    private readonly IChatService _chatService;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly IChatHistoryStore _chatHistoryStore;
    private readonly ChatAudioStore _chatAudioStore;
    private readonly Func<ProfileSettings> _profileSettingsProvider;
    private readonly StreamingMp3AudioPlayer _audioPlayer;
    private readonly ICharacterStateController _characterStateController;
    private readonly CharacterErrorMessageStore _errorMessageStore;
    private readonly IMemoryStore _memoryStore;
    private readonly IDesktopContextProvider _desktopContextProvider;
    private readonly IAmbientActivityState _ambientActivityState;
    private readonly ObservationStore _observationStore;
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
        IChatHistoryStore chatHistoryStore,
        ChatAudioStore chatAudioStore,
        Func<ProfileSettings> profileSettingsProvider,
        StreamingMp3AudioPlayer audioPlayer,
        ICharacterStateController characterStateController,
        CharacterErrorMessageStore errorMessageStore,
        IMemoryStore memoryStore,
        IDesktopContextProvider desktopContextProvider,
        IAmbientActivityState ambientActivityState,
        ObservationStore observationStore)
    {
        _overlayWindow = overlayWindow;
        _chatService = chatService;
        _voiceSynthesisService = voiceSynthesisService;
        _chatHistoryStore = chatHistoryStore;
        _chatAudioStore = chatAudioStore;
        _profileSettingsProvider = profileSettingsProvider;
        _audioPlayer = audioPlayer;
        _characterStateController = characterStateController;
        _errorMessageStore = errorMessageStore;
        _memoryStore = memoryStore;
        _desktopContextProvider = desktopContextProvider;
        _ambientActivityState = ambientActivityState;
        _observationStore = observationStore;

        _overlayWindow.MessageSubmitted += OnMessageSubmitted;
        _overlayWindow.UserInputActivity += OnUserInputActivity;
    }

    public async Task ReplayCachedSpeechAsync(ChatHistoryMessage message)
    {
        if (message.Role != ChatHistoryRole.Bot
            || string.IsNullOrWhiteSpace(message.AudioFileName)
            || !_chatAudioStore.Exists(message.AudioFileName))
        {
            return;
        }

        var turnId = Interlocked.Increment(ref _newestSubmittedTurnId);
        try
        {
            var audio = new VoiceSynthesisResult(
                _chatAudioStore.OpenRead(message.AudioFileName),
                ChatAudioStore.AudioFormat);

            await ReplaceCurrentSpeechAsync(turnId, message.Text, audio, botMessageId: null);
        }
        catch (Exception ex)
        {
            ShowError(ex, PetErrorCode.PlaybackFailed);
            _characterStateController.ShowTemporaryMood(PetMood.Alarmed, ErrorMoodDuration);
            throw;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _overlayWindow.MessageSubmitted -= OnMessageSubmitted;
        _overlayWindow.UserInputActivity -= OnUserInputActivity;
        _currentPlaybackCancellation?.Cancel();
        _currentPlaybackCancellation?.Dispose();
        _playbackGate.Dispose();
        _isDisposed = true;
    }

    private async void OnMessageSubmitted(object? sender, string message)
    {
        await SubmitAsync(message);
    }

    private void OnUserInputActivity(object? sender, EventArgs e)
    {
        _ambientActivityState.RecordUserInput();
    }

    private async Task SubmitAsync(string message)
    {
        _ambientActivityState.SetUserRequestActive(true);
        var turnId = Interlocked.Increment(ref _newestSubmittedTurnId);
        TryAddHistoryMessage(ChatHistoryRole.User, message);
        _overlayWindow.SetRequestPending(isPending: true);

        using var thinking = _characterStateController.BeginMood(PetMood.Thinking);
        try
        {
            VoiceSynthesisResult? audio = null;
            try
            {
                var desktopContext = await _desktopContextProvider.GetCurrentContextAsync(CancellationToken.None);
                _overlayWindow.ShowDesktopContext(DesktopContextFormatter.Format(desktopContext.Context));
                var reply = await _chatService.ReplyAsync(
                    new ChatRequest(
                        message,
                        _profileSettingsProvider(),
                        BuildMemoriesContext(),
                        desktopContext.Context,
                        GetRecentObservations()),
                    CancellationToken.None);
                var botMessage = TryAddHistoryMessage(ChatHistoryRole.Bot, reply.Text);
                audio = await _voiceSynthesisService.SynthesizeAsync(new VoiceSynthesisRequest(reply.Text), CancellationToken.None);

                if (turnId != Volatile.Read(ref _newestSubmittedTurnId))
                {
                    return;
                }

                await ReplaceCurrentSpeechAsync(turnId, reply.Text, audio, botMessage?.Id);
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
            _ambientActivityState.SetUserRequestActive(false);
        }
    }

    private async Task ReplaceCurrentSpeechAsync(int turnId, string transcript, VoiceSynthesisResult audio, string? botMessageId)
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
            _currentPlaybackTask = PlaySpeechAsync(turnId, audio, botMessageId, _currentPlaybackCancellation.Token);
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

    private async Task PlaySpeechAsync(
        int turnId,
        VoiceSynthesisResult audio,
        string? botMessageId,
        CancellationToken cancellationToken)
    {
        FileStream? cacheStream = null;
        string? audioFileName = null;
        var audioCacheSaved = false;

        await using (audio)
        {
            try
            {
                var playbackStream = audio.AudioStream;
                if (!string.IsNullOrWhiteSpace(botMessageId))
                {
                    try
                    {
                        audioFileName = _chatAudioStore.CreateAudioFileName(botMessageId);
                        cacheStream = _chatAudioStore.CreateAudioFile(audioFileName);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"DesktopPet audio cache error: {ex.Message}");
                        audioFileName = null;
                    }
                }

                using (var speaking = _characterStateController.BeginSpeaking())
                {
                    _ambientActivityState.SetSpeechActive(true);
                    try
                    {
                        await _audioPlayer.PlayAsync(
                            playbackStream,
                            audio.AudioFormat,
                            cancellationToken,
                            speaking.SetMouthOpen,
                            cacheStream);
                    }
                    finally
                    {
                        _ambientActivityState.SetSpeechActive(false);
                    }
                }

                if (SaveCachedAudio(cacheStream, audioFileName, botMessageId))
                {
                    cacheStream = null;
                    audioCacheSaved = true;
                }

                await HideTranscriptAfterHoldAsync(turnId, cancellationToken);
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
                    await HideTranscriptAfterHoldAsync(turnId, cancellationToken);
                }
            }
            finally
            {
                cacheStream?.Dispose();
                if (!audioCacheSaved)
                {
                    _chatAudioStore.Delete(audioFileName);
                }
            }
        }
    }

    private async Task HideTranscriptAfterHoldAsync(int turnId, CancellationToken cancellationToken)
    {
        await Task.Delay(TranscriptHoldAfterSpeech, cancellationToken);
        if (turnId == Volatile.Read(ref _activeTranscriptTurnId))
        {
            _overlayWindow.HideTranscript();
        }
    }

    private ChatHistoryMessage? TryAddHistoryMessage(ChatHistoryRole role, string text)
    {
        try
        {
            return _chatHistoryStore.Add(role, text);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DesktopPet chat history error: {ex.Message}");
            return null;
        }
    }

    private bool SaveCachedAudio(
        FileStream? cacheStream,
        string? audioFileName,
        string? botMessageId)
    {
        if (cacheStream is null
            || string.IsNullOrWhiteSpace(audioFileName)
            || string.IsNullOrWhiteSpace(botMessageId))
        {
            return false;
        }

        try
        {
            cacheStream.Dispose();
            _chatHistoryStore.SetAudioFileName(botMessageId, audioFileName);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DesktopPet audio cache finalize error: {ex.Message}");
            return false;
        }
    }

    private void ShowError(Exception exception, PetErrorCode fallbackCode)
    {
        var error = PetError.FromException(exception, fallbackCode);
        Debug.WriteLine($"DesktopPet error ({error.Code}, {exception.GetType().Name}).");
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

    private string? BuildMemoriesContext()
    {
        try
        {
            var memories = _memoryStore.List();
            if (memories.Count == 0)
            {
                return null;
            }

            return string.Join("\n", memories.Select(m => m.Text));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DesktopPet memory load error: {ex.Message}");
            return null;
        }
    }

    private IReadOnlyList<ObservationRecord> GetRecentObservations()
    {
        try
        {
            return _observationStore.List()
                .OrderByDescending(r => r.CapturedAt)
                .Take(ObservationHistoryCount)
                .ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DesktopPet observation history load error: {ex.Message}");
            return [];
        }
    }
}
