namespace DesktopPet.App.Overlay;

public interface ICharacterStateController
{
    ISpeakingScope BeginSpeaking();
    IDisposable BeginMood(PetMood mood);
    void ShowTemporaryMood(PetMood mood, TimeSpan duration);
    void MarkActivity();
}

public interface ISpeakingScope : IDisposable
{
    void SetMouthOpen(double openness);
}
