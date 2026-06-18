using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace DesktopPet.App.Observation;

public enum AmbientDecisionReason
{
    Eligible,
    ObservationPaused,
    AmbientDisabled,
    PermissionRemoved,
    StaleObservation,
    ActiveApplicationChanged,
    UserRequestActive,
    SpeechActive,
    UserRecentlyTyping,
    FullScreenApplication,
    CooldownActive,
    HourlyLimitReached,
    DuplicateTopic,
    GeneratorChoseSilence,
    GenerationFailed,
    BelowThreshold
}

public sealed record AmbientCommentCandidate(
    DesktopObservationChange Change,
    bool IsStillCurrent,
    bool PermissionStillAllowed);

public sealed record AmbientPolicyDecision(bool MaySpeak, AmbientDecisionReason Reason);

public interface IAmbientCommentPolicy
{
    AmbientPolicyDecision Evaluate(AmbientCommentCandidate candidate, DateTimeOffset now, VisionObservation? visionObservation = null);
    void RecordSpoken(AmbientCommentCandidate candidate, DateTimeOffset spokenAt);
    DateTimeOffset? GetLastSpokenAt();
}

internal sealed partial class AmbientCommentPolicy : IAmbientCommentPolicy
{
    private static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RecentTypingWindow = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(30);
    private const int MaximumObservationsPerHour = 8;

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
        if (now - candidate.Change.Observation.ObservedAt > MaximumObservationAge) return Reject(AmbientDecisionReason.StaleObservation);
        if (!candidate.IsStillCurrent) return Reject(AmbientDecisionReason.ActiveApplicationChanged);
        if (_activityState.IsUserRequestActive) return Reject(AmbientDecisionReason.UserRequestActive);
        if (_activityState.IsSpeechActive) return Reject(AmbientDecisionReason.SpeechActive);
        if (now - _activityState.LastUserInputAt < RecentTypingWindow
            || GetSystemIdleDuration() < TimeSpan.FromSeconds(3)) return Reject(AmbientDecisionReason.UserRecentlyTyping);
        if (IsForegroundFullScreen()) return Reject(AmbientDecisionReason.FullScreenApplication);

        lock (_sync)
        {
            Prune(now);
            var cooldown = GetCooldown(settings.CommentaryLevel);

            if (_spokenAt.Count > 0 && now - _spokenAt[^1] < cooldown)
            {
                return Reject(AmbientDecisionReason.CooldownActive);
            }

            if (_spokenAt.Count >= MaximumObservationsPerHour)
            {
                return Reject(AmbientDecisionReason.HourlyLimitReached);
            }

            if (_topics.TryGetValue(candidate.Change.TopicKey, out var lastTopic)
                && now - lastTopic < DuplicateWindow)
            {
                return Reject(AmbientDecisionReason.DuplicateTopic);
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
            Prune(spokenAt);
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

    private static double CalculateInterestScore(VisionObservation observation)
    {
        return (observation.Novelty * 0.3)
            + (observation.Relevance * 0.3)
            + (observation.Confidence * 0.2)
            + ((1.0 - observation.Sensitivity) * 0.1)
            + ((1.0 - observation.InterruptionCost) * 0.1);
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

    private static TimeSpan GetCooldown(CommentaryLevel level)
    {
        return level switch
        {
            CommentaryLevel.Quiet => TimeSpan.FromMinutes(30),
            CommentaryLevel.Talkative => TimeSpan.FromMinutes(7),
            _ => TimeSpan.FromMinutes(15)
        };
    }

    private void Prune(DateTimeOffset now)
    {
        _spokenAt.RemoveAll(item => now - item >= TimeSpan.FromHours(1));
        foreach (var key in _topics.Where(pair => now - pair.Value >= DuplicateWindow).Select(pair => pair.Key).ToArray())
        {
            _topics.Remove(key);
        }
    }

    private static AmbientPolicyDecision Reject(AmbientDecisionReason reason) => new(false, reason);

    private static bool IsForegroundFullScreen()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == nint.Zero || !NativeMethods.GetWindowRect(window, out var rect)) return false;
        var screen = Forms.Screen.FromHandle(window);
        return rect.Left <= screen.Bounds.Left
            && rect.Top <= screen.Bounds.Top
            && rect.Right >= screen.Bounds.Right
            && rect.Bottom >= screen.Bounds.Bottom;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial nint GetForegroundWindow();

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetWindowRect(nint windowHandle, out NativeRect bounds);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetLastInputInfo(ref LastInputInfo info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    private static TimeSpan GetSystemIdleDuration()
    {
        var info = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };
        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return TimeSpan.MaxValue;
        }

        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }
}
