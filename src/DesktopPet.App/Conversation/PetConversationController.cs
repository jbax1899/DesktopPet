using DesktopPet.App.Cloud;
using DesktopPet.App.Overlay;
using DesktopPet.App.Settings;
using DesktopPet.App.Voice;

namespace DesktopPet.App.Conversation;

public sealed class PetConversationController : IDisposable
{
    private static readonly TimeSpan TranscriptHoldAfterSpeech = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ErrorMoodDuration = TimeSpan.FromSeconds(2.5);

    private readonly ConversationOverlayWindow _overlayWindow;
    private readonly IPetChatService _chatService;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly Func<PetProfileSettings> _profileSettingsProvider;
    private readonly TempFileAudioPlayer _audioPlayer;
    private readonly IPetPerformanceController _performanceController;
    private readonly SemaphoreSlim _playbackGate = new(1, 1);

    private int _newestSubmittedTurnId;
    private int _activeTranscriptTurnId;
    private CancellationTokenSource? _currentPlaybackCancellation;
    private Task _currentPlaybackTask = Task.CompletedTask;
    private bool _isDisposed;

    public PetConversationController(
        ConversationOverlayWindow overlayWindow,
        IPetChatService chatService,
        IVoiceSynthesisService voiceSynthesisService,
        Func<PetProfileSettings> profileSettingsProvider,
        TempFileAudioPlayer audioPlayer,
        IPetPerformanceController performanceController)
    {
        _overlayWindow = overlayWindow;
        _chatService = chatService;
        _voiceSynthesisService = voiceSynthesisService;
        _profileSettingsProvider = profileSettingsProvider;
        _audioPlayer = audioPlayer;
        _performanceController = performanceController;

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

        using var thinking = _performanceController.BeginMood(PetMood.Thinking);
        try
        {
            var reply = await _chatService.ReplyAsync(
                new PetChatRequest(message, _profileSettingsProvider()),
                CancellationToken.None);
            var audio = await _voiceSynthesisService.SynthesizeAsync(new VoiceSynthesisRequest(reply.Text), CancellationToken.None);

            if (turnId != Volatile.Read(ref _newestSubmittedTurnId))
            {
                return;
            }

            await ReplaceCurrentSpeechAsync(turnId, reply.Text, audio);
        }
        catch (Exception ex)
        {
            if (turnId == Volatile.Read(ref _newestSubmittedTurnId))
            {
                _overlayWindow.ShowError($"Chat failed: {ex.Message}");
                _performanceController.ShowTemporaryMood(PetMood.Alarmed, ErrorMoodDuration);
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
        }
        finally
        {
            _playbackGate.Release();
        }
    }

    private async Task PlaySpeechAsync(int turnId, VoiceSynthesisResult audio, CancellationToken cancellationToken)
    {
        try
        {
            using (var speaking = _performanceController.BeginSpeaking())
            {
                await _audioPlayer.PlayAsync(
                    audio.AudioBytes,
                    audio.AudioFormat,
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
                _overlayWindow.ShowError($"Playback failed: {ex.Message}");
                _performanceController.ShowTemporaryMood(PetMood.Alarmed, ErrorMoodDuration);
            }
        }
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
