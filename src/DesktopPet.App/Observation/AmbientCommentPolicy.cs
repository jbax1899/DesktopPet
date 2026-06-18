namespace DesktopPet.App.Observation;

public enum AmbientDecisionReason
{
    Eligible,
    ObservationPaused,
    AmbientDisabled,
    PermissionRemoved,
    UserRequestActive,
    SpeechActive,
    UserRecentlyTyping,
    CooldownActive,
    DuplicateTopic,
    GeneratorChoseSilence,
    GenerationFailed,
    BelowThreshold
}

public sealed record AmbientCommentCandidate(
    DesktopObservationChange Change,
    bool PermissionStillAllowed);

public sealed record AmbientPolicyDecision(bool MaySpeak, AmbientDecisionReason Reason);

public interface IAmbientCommentPolicy
{
    AmbientPolicyDecision Evaluate(AmbientCommentCandidate candidate, DateTimeOffset now, VisionObservation? visionObservation = null);
    void RecordSpoken(AmbientCommentCandidate candidate, DateTimeOffset spokenAt);
    DateTimeOffset? GetLastSpokenAt();
}

internal sealed class AmbientCommentPolicy : IAmbientCommentPolicy
{
    private static readonly TimeSpan RecentTypingWindow = TimeSpan.FromSeconds(8);

    private readonly IObservationPermissionService _permissionService;
    private readonly IAmbientActivityState _activityState;
    private readonly object _sync = new();
    private readonly List<DateTimeOffset> _spokenAt = [];
    private readonly Dictionary<string, DateTimeOffset> _topics = new(StringComparer.Ordinal);

    public AmbientCommentPolicy(
        IObservationPermissionService permissionService,
        IAmbientActivityState activityState)
    {
        _permissionService = permissionService;
        _activityState = activityState;
    }

    public AmbientPolicyDecision Evaluate(AmbientCommentCandidate candidate, DateTimeOffset now, VisionObservation? visionObservation = null)
    {
        var settings = _permissionService.Current;
        if (!settings.ObservationEnabled) return Reject(AmbientDecisionReason.ObservationPaused);
        if (!settings.AmbientCommentsEnabled) return Reject(AmbientDecisionReason.AmbientDisabled);
        if (!candidate.PermissionStillAllowed) return Reject(AmbientDecisionReason.PermissionRemoved);
        if (_activityState.IsUserRequestActive) return Reject(AmbientDecisionReason.UserRequestActive);
        if (_activityState.IsSpeechActive) return Reject(AmbientDecisionReason.SpeechActive);
        if (now - _activityState.LastUserInputAt < RecentTypingWindow) return Reject(AmbientDecisionReason.UserRecentlyTyping);

        lock (_sync)
        {
            Prune(now, settings);
            var cooldown = TimeSpan.FromMinutes(settings.CooldownMinutes);

            if (_spokenAt.Count > 0 && now - _spokenAt[^1] < cooldown)
            {
                return Reject(AmbientDecisionReason.CooldownActive);
            }

            if (visionObservation is null)
            {
                var duplicateWindow = TimeSpan.FromMinutes(settings.DuplicateWindowMinutes);
                if (_topics.TryGetValue(candidate.Change.TopicKey, out var lastTopic)
                    && now - lastTopic < duplicateWindow)
                {
                    return Reject(AmbientDecisionReason.DuplicateTopic);
                }
            }

            if (visionObservation is not null)
            {
                var interestScore = CalculateInterestScore(visionObservation);
                var threshold = CalculateEffectiveThreshold(settings.VisionSensitivity);
                if (interestScore < threshold)
                {
                    return Reject(AmbientDecisionReason.BelowThreshold);
                }
            }
        }

        return new AmbientPolicyDecision(true, AmbientDecisionReason.Eligible);
    }

    public void RecordSpoken(AmbientCommentCandidate candidate, DateTimeOffset spokenAt)
    {
        lock (_sync)
        {
            var settings = _permissionService.Current;
            Prune(spokenAt, settings);
            _spokenAt.Add(spokenAt);
            _topics[candidate.Change.TopicKey] = spokenAt;
        }
    }

    public DateTimeOffset? GetLastSpokenAt()
    {
        lock (_sync)
        {
            return _spokenAt.Count > 0 ? _spokenAt[^1] : null;
        }
    }

    public static double CalculateInterestScore(VisionObservation observation)
    {
        return (observation.Novelty * 0.375)
            + (observation.Relevance * 0.375)
            + ((1.0 - observation.Sensitivity) * 0.125)
            + ((1.0 - observation.InterruptionCost) * 0.125);
    }

    private static double CalculateEffectiveThreshold(VisionSensitivity sensitivity)
    {
        return sensitivity switch
        {
            VisionSensitivity.Low => 0.7,
            VisionSensitivity.High => 0.3,
            _ => 0.5
        };
    }

    private void Prune(DateTimeOffset now, ObservationSettings settings)
    {
        var spokenCutoff = now - TimeSpan.FromHours(1);
        _spokenAt.RemoveAll(item => item < spokenCutoff);

        var topicCutoff = now - TimeSpan.FromMinutes(settings.DuplicateWindowMinutes);
        foreach (var key in _topics.Where(pair => pair.Value < topicCutoff).Select(pair => pair.Key).ToArray())
        {
            _topics.Remove(key);
        }
    }

    private static AmbientPolicyDecision Reject(AmbientDecisionReason reason) => new(false, reason);
}
