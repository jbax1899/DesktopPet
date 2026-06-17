using DesktopPet.App.Cloud;
using DesktopPet.App.Conversation;
using DesktopPet.App.Overlay;
using DesktopPet.App.Voice;

namespace DesktopPet.App.Observation;

internal sealed class AmbientCommentCoordinator : IDisposable
{
    private readonly IDesktopObservationCoordinator _observationCoordinator;
    private readonly IObservationPermissionService _permissionService;
    private readonly IAmbientCommentPolicy _policy;
    private readonly IAmbientCommentGenerator _generator;
    private readonly IVoiceSynthesisService _voiceSynthesisService;
    private readonly StreamingMp3AudioPlayer _audioPlayer;
    private readonly ConversationOverlayWindow _overlayWindow;
    private readonly ICharacterStateController _characterStateController;
    private readonly IAmbientActivityState _activityState;
    private readonly AmbientDecisionStore _decisionStore;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _currentCancellation;
    private long _turnId;
    private bool _disposed;

    public AmbientCommentCoordinator(
        IDesktopObservationCoordinator observationCoordinator,
        IObservationPermissionService permissionService,
        IAmbientCommentPolicy policy,
        IAmbientCommentGenerator generator,
        IVoiceSynthesisService voiceSynthesisService,
        StreamingMp3AudioPlayer audioPlayer,
        ConversationOverlayWindow overlayWindow,
        ICharacterStateController characterStateController,
        IAmbientActivityState activityState,
        AmbientDecisionStore decisionStore)
    {
        _observationCoordinator = observationCoordinator;
        _permissionService = permissionService;
        _policy = policy;
        _generator = generator;
        _voiceSynthesisService = voiceSynthesisService;
        _audioPlayer = audioPlayer;
        _overlayWindow = overlayWindow;
        _characterStateController = characterStateController;
        _activityState = activityState;
        _decisionStore = decisionStore;

        _observationCoordinator.ChangeDetected += OnChangeDetected;
        _activityState.UserRequestStarted += OnUserRequestStarted;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _observationCoordinator.ChangeDetected -= OnChangeDetected;
        _activityState.UserRequestStarted -= OnUserRequestStarted;
        Interlocked.Increment(ref _turnId);
        _currentCancellation?.Cancel();
        _currentCancellation?.Dispose();
        _disposed = true;
    }

    private async void OnChangeDetected(object? sender, DesktopObservationChange change)
    {
        try
        {
            await HandleChangeAsync(change);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _decisionStore.Add(change, spoke: false, AmbientDecisionReason.GenerationFailed);
            System.Diagnostics.Debug.WriteLine($"Ambient comment failed ({ex.GetType().Name}).");
        }
    }

    private void OnUserRequestStarted(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _turnId);
        _currentCancellation?.Cancel();
    }

    private async Task HandleChangeAsync(DesktopObservationChange change)
    {
        if (_disposed)
        {
            return;
        }

        var turnId = Interlocked.Increment(ref _turnId);
        var candidate = CreateCandidate(change);
        var initialDecision = _policy.Evaluate(candidate, DateTimeOffset.UtcNow);
        if (!initialDecision.MaySpeak)
        {
            _decisionStore.Add(change, spoke: false, initialDecision.Reason);
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_disposed || turnId != Volatile.Read(ref _turnId))
            {
                return;
            }

            _currentCancellation?.Cancel();
            _currentCancellation?.Dispose();
            _currentCancellation = new CancellationTokenSource();
            var cancellationToken = _currentCancellation.Token;

            var comment = await _generator.GenerateAsync(change, cancellationToken);
            if (string.IsNullOrWhiteSpace(comment) || turnId != Volatile.Read(ref _turnId))
            {
                if (turnId == Volatile.Read(ref _turnId))
                {
                    _decisionStore.Add(change, spoke: false, AmbientDecisionReason.GeneratorChoseSilence);
                }
                return;
            }

            candidate = CreateCandidate(change);
            var finalDecision = _policy.Evaluate(candidate, DateTimeOffset.UtcNow);
            if (!finalDecision.MaySpeak)
            {
                _decisionStore.Add(change, spoke: false, finalDecision.Reason);
                return;
            }

            await using var audio = await _voiceSynthesisService.SynthesizeAsync(
                new VoiceSynthesisRequest(comment),
                cancellationToken);
            if (turnId != Volatile.Read(ref _turnId))
            {
                return;
            }

            _overlayWindow.ShowTranscript(comment);
            using var speaking = _characterStateController.BeginSpeaking();
            _activityState.SetSpeechActive(true);
            try
            {
                await _audioPlayer.PlayAsync(
                    audio.AudioStream,
                    audio.AudioFormat,
                    cancellationToken,
                    speaking.SetMouthOpen);
            }
            finally
            {
                _activityState.SetSpeechActive(false);
            }

            _policy.RecordSpoken(candidate, DateTimeOffset.UtcNow);
            _decisionStore.Add(change, spoke: true, AmbientDecisionReason.Eligible);
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            _overlayWindow.HideTranscript();
        }
        finally
        {
            _gate.Release();
        }
    }

    private AmbientCommentCandidate CreateCandidate(DesktopObservationChange change)
    {
        var current = _observationCoordinator.RecentObservations.LastOrDefault();
        var isCurrent = current is not null
            && string.Equals(current.ApplicationName, change.Observation.ApplicationName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.ActivityDescription, change.Observation.ActivityDescription, StringComparison.Ordinal);
        var permitted = _permissionService.Current.ApplicationRules.Any(rule =>
            !rule.IsDenied
            && rule.AllowMetadata
            && string.Equals(rule.DisplayName, change.Observation.ApplicationName, StringComparison.OrdinalIgnoreCase));

        return new AmbientCommentCandidate(change, isCurrent, permitted);
    }
}
