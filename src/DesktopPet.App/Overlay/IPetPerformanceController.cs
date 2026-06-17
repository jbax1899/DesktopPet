namespace DesktopPet.App.Overlay;

public interface IPetPerformanceController
{
    IPetSpeakingScope BeginSpeaking();
}

public interface IPetSpeakingScope : IDisposable
{
    void SetMouthOpen(double openness);
}
