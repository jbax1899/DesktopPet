namespace DesktopPet.App.Overlay;

public interface IPetPerformanceController
{
    IPetSpeakingScope BeginSpeaking();
    IDisposable BeginMood(PetMood mood);
    void ShowTemporaryMood(PetMood mood, TimeSpan duration);
    void MarkActivity();
}

public interface IPetSpeakingScope : IDisposable
{
    void SetMouthOpen(double openness);
}
