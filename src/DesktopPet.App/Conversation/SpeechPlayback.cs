using System.Diagnostics;
using System.IO;
using DesktopPet.App.Cloud;
using DesktopPet.App.Memory;
using DesktopPet.App.Overlay;
using DesktopPet.App.Observation;
using DesktopPet.App.Voice;

namespace DesktopPet.App.Conversation;

public sealed class SpeechPlayback
{
    private readonly StreamingMp3AudioPlayer _audioPlayer;
    private readonly ICharacterStateController _characterStateController;
    private readonly IAmbientActivityState _activityState;
    private readonly IChatHistoryStore _chatHistoryStore;
    private readonly ChatAudioStore _chatAudioStore;

    public SpeechPlayback(
        StreamingMp3AudioPlayer audioPlayer,
        ICharacterStateController characterStateController,
        IAmbientActivityState activityState,
        IChatHistoryStore chatHistoryStore,
        ChatAudioStore chatAudioStore)
    {
        _audioPlayer = audioPlayer;
        _characterStateController = characterStateController;
        _activityState = activityState;
        _chatHistoryStore = chatHistoryStore;
        _chatAudioStore = chatAudioStore;
    }

    public async Task PlayAsync(
        VoiceSynthesisResult audio,
        string? historyMessageId,
        CancellationToken cancellationToken)
    {
        FileStream? cacheStream = null;
        string? audioFileName = null;
        var audioCacheSaved = false;

        try
        {
            if (!string.IsNullOrWhiteSpace(historyMessageId))
            {
                try
                {
                    audioFileName = _chatAudioStore.CreateAudioFileName(historyMessageId);
                    cacheStream = _chatAudioStore.CreateAudioFile(audioFileName);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DesktopPet audio cache error: {ex.Message}");
                    audioFileName = null;
                }
            }

            using var speaking = _characterStateController.BeginSpeaking();
            _activityState.SetSpeechActive(true);
            try
            {
                await _audioPlayer.PlayAsync(
                    audio.AudioStream,
                    audio.AudioFormat,
                    cancellationToken,
                    speaking.SetMouthOpen,
                    cacheStream);
            }
            finally
            {
                _activityState.SetSpeechActive(false);
            }

            if (SaveCachedAudio(cacheStream, audioFileName, historyMessageId))
            {
                cacheStream = null;
                audioCacheSaved = true;
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

    private bool SaveCachedAudio(
        FileStream? cacheStream,
        string? audioFileName,
        string? historyMessageId)
    {
        if (cacheStream is null
            || string.IsNullOrWhiteSpace(audioFileName)
            || string.IsNullOrWhiteSpace(historyMessageId))
        {
            return false;
        }

        try
        {
            cacheStream.Dispose();
            _chatHistoryStore.SetAudioFileName(historyMessageId, audioFileName);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DesktopPet audio cache finalize error: {ex.Message}");
            return false;
        }
    }
}
