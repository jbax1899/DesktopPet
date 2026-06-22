using System.Net.Http;
using DesktopPet.App.Cloud;
using DesktopPet.App.Settings;

namespace DesktopPet.App.Shell;

public sealed class CloudServiceFactory
{
    public IChatService ChatService { get; }
    public IVoiceSynthesisService VoiceSynthesisService { get; }
    public OpenRouterModelsService OpenRouterModelsService { get; }
    public CreditInfoService CreditInfoService { get; }
    public ElevenLabsPronunciationService PronunciationService { get; }

    public CloudServiceFactory(
        HttpClient httpClient,
        SettingsHub settings)
    {
        ChatService = new ElevenLabsAgentChatService(
            httpClient,
            settings.ElevenLabs.Load,
            () => settings.Ui.Load().GetEffectiveChatHistoryContext());
        VoiceSynthesisService = new ElevenLabsVoiceSynthesisService(httpClient, settings.ElevenLabs.Load);
        OpenRouterModelsService = new OpenRouterModelsService(httpClient, settings.OpenRouter.Load);
        CreditInfoService = new CreditInfoService(httpClient, settings.ElevenLabs.Load, settings.OpenRouter.Load);
        PronunciationService = new ElevenLabsPronunciationService(httpClient);
    }
}
