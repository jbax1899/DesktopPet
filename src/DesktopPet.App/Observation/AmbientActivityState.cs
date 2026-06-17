namespace DesktopPet.App.Observation;

public interface IAmbientActivityState
{
    event EventHandler? UserRequestStarted;

    bool IsUserRequestActive { get; }
    bool IsSpeechActive { get; }
    DateTimeOffset LastUserInputAt { get; }
    void SetUserRequestActive(bool active);
    void SetSpeechActive(bool active);
    void RecordUserInput();
}

public sealed class AmbientActivityState : IAmbientActivityState
{
    private int _userRequestCount;
    private int _speechCount;
    private long _lastUserInputTicks = DateTimeOffset.MinValue.UtcTicks;

    public event EventHandler? UserRequestStarted;

    public bool IsUserRequestActive => Volatile.Read(ref _userRequestCount) > 0;
    public bool IsSpeechActive => Volatile.Read(ref _speechCount) > 0;
    public DateTimeOffset LastUserInputAt => new(Interlocked.Read(ref _lastUserInputTicks), TimeSpan.Zero);

    public void SetUserRequestActive(bool active)
    {
        UpdateCount(ref _userRequestCount, active);
        if (active)
        {
            UserRequestStarted?.Invoke(this, EventArgs.Empty);
        }
    }
    public void SetSpeechActive(bool active) => UpdateCount(ref _speechCount, active);
    public void RecordUserInput() => Interlocked.Exchange(ref _lastUserInputTicks, DateTimeOffset.UtcNow.UtcTicks);

    private static void UpdateCount(ref int count, bool active)
    {
        if (active)
        {
            Interlocked.Increment(ref count);
        }
        else if (Interlocked.Decrement(ref count) < 0)
        {
            Interlocked.Exchange(ref count, 0);
        }
    }
}
