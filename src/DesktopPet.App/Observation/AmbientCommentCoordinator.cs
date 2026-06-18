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
    private readonly ObservationStore _observationStore;
    private readonly IWindowCaptureService? _windowCaptureService;
    private readonly IVisualContextAnalyzer? _visualAnalyzer;
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
        AmbientDecisionStore decisionStore,
        ObservationStore observationStore,
        IWindowCaptureService? windowCaptureService = null,
        IVisualContextAnalyzer? visualAnalyzer = null)
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
        _observationStore = observationStore;
        _windowCaptureService = windowCaptureService;
        _visualAnalyzer = visualAnalyzer;

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
            System.Diagnostics.Debug.WriteLine($"Ambient comment failed ({ex.GetType().Name}): {ex.Message}");
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
        System.Diagnostics.Debug.WriteLine($"Ambient: Change detected for {change.Observation.ApplicationName} ({change.Type}). Turn={turnId}.");

        VisionObservation? visionObservation = null;
        if (_visualAnalyzer is not null && _visualAnalyzer.IsAvailable
            && _windowCaptureService is not null
            && _permissionService.IsAllowed(change.Observation.ExecutablePath, DesktopContextCapabilities.Visual))
        {
            System.Diagnostics.Debug.WriteLine($"Ambient: Attempting vision analysis for {change.Observation.ExecutablePath}.");
            visionObservation = await CaptureAndAnalyzeAsync(change, turnId, CancellationToken.None);
            if (visionObservation is not null)
            {
                System.Diagnostics.Debug.WriteLine($"Ambient: Vision analysis complete. Summary={visionObservation.Summary}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Ambient: Vision analysis returned null.");
            }
        }
        else
        {
            var reasons = new List<string>();
            if (_visualAnalyzer is null) reasons.Add("no visual analyzer");
            else if (!_visualAnalyzer.IsAvailable) reasons.Add("visual analyzer not available");
            if (_windowCaptureService is null) reasons.Add("no capture service");
            if (!_permissionService.IsAllowed(change.Observation.ExecutablePath, DesktopContextCapabilities.Visual))
                reasons.Add("visual permission not granted");
            System.Diagnostics.Debug.WriteLine($"Ambient: Skipping vision analysis. {string.Join(", ", reasons)}.");
        }

        var initialDecision = _policy.Evaluate(candidate, DateTimeOffset.UtcNow, visionObservation);
        if (!initialDecision.MaySpeak)
        {
            System.Diagnostics.Debug.WriteLine($"Ambient: Policy rejected. Reason={initialDecision.Reason}.");
            RecordObservation(change, visionObservation, initialDecision.Reason, spoke: false);
            return;
        }

        System.Diagnostics.Debug.WriteLine("Ambient: Policy approved. Waiting for gate.");
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

            var comment = await _generator.GenerateAsync(change, visionObservation, cancellationToken);
            if (string.IsNullOrWhiteSpace(comment) || turnId != Volatile.Read(ref _turnId))
            {
                if (turnId == Volatile.Read(ref _turnId))
                {
                    RecordObservation(change, visionObservation, AmbientDecisionReason.GeneratorChoseSilence, spoke: false);
                }
                return;
            }

            candidate = CreateCandidate(change);
            var finalDecision = _policy.Evaluate(candidate, DateTimeOffset.UtcNow, visionObservation);
            if (!finalDecision.MaySpeak)
            {
                RecordObservation(change, visionObservation, finalDecision.Reason, spoke: false);
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
            RecordObservation(change, visionObservation, AmbientDecisionReason.Eligible, spoke: true);
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            _overlayWindow.HideTranscript();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<VisionObservation?> CaptureAndAnalyzeAsync(
        DesktopObservationChange change,
        long turnId,
        CancellationToken cancellationToken)
    {
        if (_windowCaptureService is null || _visualAnalyzer is null)
        {
            return null;
        }

        try
        {
            var foregroundCollector = new ForegroundWindowCollector(_permissionService);
            var foregroundSnapshot = foregroundCollector.CollectPermittedMetadata();
            if (foregroundSnapshot is null)
            {
                return null;
            }

            if (!string.Equals(foregroundSnapshot.ExecutablePath, change.Observation.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var capture = await _windowCaptureService.CaptureAsync(
                foregroundSnapshot.WindowHandle,
                foregroundSnapshot.ExecutablePath,
                foregroundSnapshot.IsVisible,
                foregroundSnapshot.IsMinimized,
                cancellationToken);

            using (capture.Image)
            {
                if (capture.Status != DesktopContextCollectionStatus.Available || capture.Image is null)
                {
                    return null;
                }

                var lastSpokeAt = _policy.GetLastSpokenAt();
                var recentObservations = _observationCoordinator.RecentObservations;
                return await (_visualAnalyzer as OpenRouterVisionAnalyzer)?.AnalyzeDetailedAsync(
                    capture.Image,
                    new VisualAnalysisRequest(change.Observation.ApplicationName, change.Observation.ActivityDescription),
                    recentObservations,
                    lastSpokeAt,
                    cancellationToken)!;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Vision analysis failed ({ex.GetType().Name}): {ex.Message}");
            return null;
        }
    }

    private void RecordObservation(
        DesktopObservationChange change,
        VisionObservation? visionObservation,
        AmbientDecisionReason reason,
        bool spoke)
    {
        _decisionStore.Add(change, spoke, reason);

        if (visionObservation is not null)
        {
            var interestScore = CalculateInterestScore(visionObservation);
            var record = new ObservationRecord(
                Id: Guid.NewGuid().ToString("N"),
                CapturedAt: DateTimeOffset.UtcNow,
                Application: change.Observation.ApplicationName,
                WindowTitle: change.Observation.ActivityDescription,
                Provider: "openrouter",
                Model: "",
                Analysis: visionObservation,
                InterestScore: interestScore,
                Outcome: MapOutcome(reason, spoke),
                SpokenAt: spoke ? DateTimeOffset.UtcNow : null);
            _observationStore.Add(record);
        }
    }

    private static double CalculateInterestScore(VisionObservation observation)
    {
        return (observation.Novelty * 0.3)
            + (observation.Relevance * 0.3)
            + (observation.Confidence * 0.2)
            + ((1.0 - observation.Sensitivity) * 0.1)
            + ((1.0 - observation.InterruptionCost) * 0.1);
    }

    private static ObservationOutcome MapOutcome(AmbientDecisionReason reason, bool spoke)
    {
        if (spoke) return ObservationOutcome.Spoken;
        return reason switch
        {
            AmbientDecisionReason.CooldownActive => ObservationOutcome.Cooldown,
            AmbientDecisionReason.HourlyLimitReached => ObservationOutcome.Cooldown,
            AmbientDecisionReason.DuplicateTopic => ObservationOutcome.Duplicate,
            AmbientDecisionReason.UserRequestActive => ObservationOutcome.UserBusy,
            AmbientDecisionReason.SpeechActive => ObservationOutcome.UserBusy,
            AmbientDecisionReason.UserRecentlyTyping => ObservationOutcome.UserBusy,
            AmbientDecisionReason.StaleObservation => ObservationOutcome.Stale,
            AmbientDecisionReason.ActiveApplicationChanged => ObservationOutcome.Stale,
            AmbientDecisionReason.PermissionRemoved => ObservationOutcome.Sensitive,
            _ => ObservationOutcome.BelowThreshold
        };
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
