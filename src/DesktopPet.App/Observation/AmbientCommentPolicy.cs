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
    DoNotDisturb,
    CooldownActive,
    HourlyLimitReached,
    DuplicateTopic
}

public sealed record AmbientCommentCandidate(
    DesktopObservationChange Change,
    bool IsStillCurrent,
    bool PermissionStillAllowed);

public sealed record AmbientPolicyDecision(bool MaySpeak, AmbientDecisionReason Reason);

public interface IAmbientCommentPolicy
{
    AmbientPolicyDecision Evaluate(AmbientCommentCandidate candidate, DateTimeOffset now);
    void RecordSpoken(AmbientCommentCandidate candidate, DateTimeOffset spokenAt);
}

internal sealed partial class AmbientCommentPolicy : IAmbientCommentPolicy
{
    private static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RecentTypingWindow = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(30);

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

    public AmbientPolicyDecision Evaluate(AmbientCommentCandidate candidate, DateTimeOffset now)
    {
        var settings = _permissionService.Current;
        if (!settings.ObservationEnabled) return Reject(AmbientDecisionReason.ObservationPaused);
        if (!settings.AmbientCommentsEnabled) return Reject(AmbientDecisionReason.AmbientDisabled);
        if (!candidate.PermissionStillAllowed) return Reject(AmbientDecisionReason.PermissionRemoved);
        if (now - candidate.Change.Observation.ObservedAt > MaximumObservationAge) return Reject(AmbientDecisionReason.StaleObservation);
        if (!candidate.IsStillCurrent) return Reject(AmbientDecisionReason.ActiveApplicationChanged);
        if (_activityState.IsUserRequestActive) return Reject(AmbientDecisionReason.UserRequestActive);
        if (_activityState.IsSpeechActive) return Reject(AmbientDecisionReason.SpeechActive);
        if (now - _activityState.LastUserInputAt < RecentTypingWindow) return Reject(AmbientDecisionReason.UserRecentlyTyping);
        if (IsForegroundFullScreen()) return Reject(AmbientDecisionReason.FullScreenApplication);
        if (settings.DoNotDisturb) return Reject(AmbientDecisionReason.DoNotDisturb);

        lock (_sync)
        {
            Prune(now);
            var limits = GetLimits(settings.CommentaryLevel);
            if (_spokenAt.Count > 0 && now - _spokenAt[^1] < limits.Cooldown) return Reject(AmbientDecisionReason.CooldownActive);
            if (_spokenAt.Count >= limits.HourlyLimit) return Reject(AmbientDecisionReason.HourlyLimitReached);
            if (_topics.TryGetValue(candidate.Change.TopicKey, out var lastTopic)
                && now - lastTopic < DuplicateWindow) return Reject(AmbientDecisionReason.DuplicateTopic);
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

    private void Prune(DateTimeOffset now)
    {
        _spokenAt.RemoveAll(item => now - item >= TimeSpan.FromHours(1));
        foreach (var key in _topics.Where(pair => now - pair.Value >= DuplicateWindow).Select(pair => pair.Key).ToArray())
        {
            _topics.Remove(key);
        }
    }

    private static (TimeSpan Cooldown, int HourlyLimit) GetLimits(CommentaryLevel level) => level switch
    {
        CommentaryLevel.Quiet => (TimeSpan.FromMinutes(30), 1),
        CommentaryLevel.Talkative => (TimeSpan.FromMinutes(7), 4),
        _ => (TimeSpan.FromMinutes(15), 2)
    };

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
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
