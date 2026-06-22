namespace DesktopPet.App.Cloud;

public interface IVoiceSynthesisService
{
    Task<VoiceSynthesisResult> SynthesizeAsync(VoiceSynthesisRequest request, CancellationToken cancellationToken);
}
