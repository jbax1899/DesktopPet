using System.IO;
using System.Net.Http;
using DesktopPet.App.Audio;
using DesktopPet.App.Cloud;
using DesktopPet.App.Memory;
using DesktopPet.App.Observation;
using DesktopPet.App.Settings;

namespace DesktopPet.App.Shell;

public sealed class ObservationServiceFactory
{
    public IObservationPermissionService ObservationPermissionService { get; }
    public IForegroundWindowCollector ForegroundWindowCollector { get; }
    public IUiAutomationContextCollector UiAutomationContextCollector { get; }
    public IWindowCaptureService WindowCaptureService { get; }
    public ObservationStore ObservationStore { get; }
    public IVisualContextAnalyzer VisualContextAnalyzer { get; }
    public IDesktopEnvironmentCaptureCoordinator EnvironmentCoordinator { get; }
    public IAmbientActivityState AmbientActivityState { get; }
    public IAmbientCommentPolicy AmbientCommentPolicy { get; }
    public IAmbientCommentGenerator AmbientCommentGenerator { get; }
    public AmbientDecisionStore AmbientDecisionStore { get; }
    public ForegroundDesktopContextProvider DesktopContextProvider { get; }
    public ImageCaptureCoordinator ImageCaptureCoordinator { get; }

    public ObservationServiceFactory(
        SettingsHub settings,
        HttpClient httpClient,
        IChatService chatService,
        IMemoryStore memoryStore,
        IChatHistoryStore chatHistoryStore,
        AudioObservationContextProvider audioObservationContextProvider)
    {
        ObservationPermissionService = new ObservationPermissionService(settings.Observation);
        ForegroundWindowCollector = new ForegroundWindowCollector(ObservationPermissionService);
        UiAutomationContextCollector = new UiAutomationContextCollector(ObservationPermissionService);
        WindowCaptureService = new WindowCaptureService(ObservationPermissionService);
        ObservationStore = new ObservationStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet"),
            () => ObservationPermissionService.Current.StoredObservationCount);
        VisualContextAnalyzer = new OpenRouterVisionAnalyzer(httpClient, settings.OpenRouter.Load, ObservationPermissionService, ObservationStore);
        EnvironmentCoordinator = new DesktopEnvironmentCaptureCoordinator(
            ForegroundWindowCollector,
            ObservationPermissionService,
            UiAutomationContextCollector);
        AmbientActivityState = new AmbientActivityState();
        AmbientCommentPolicy = new AmbientCommentPolicy(
            ObservationPermissionService,
            AmbientActivityState);
        ImageCaptureCoordinator = new ImageCaptureCoordinator(
            EnvironmentCoordinator,
            ObservationPermissionService,
            ForegroundWindowCollector,
            WindowCaptureService,
            VisualContextAnalyzer,
            ObservationStore,
            AmbientCommentPolicy);
        AmbientCommentGenerator = new ElevenLabsAmbientCommentGenerator(
            chatService,
            ObservationStore,
            chatHistoryStore,
            memoryStore,
            settings.Profile.Load,
            ObservationPermissionService,
            audioObservationContextProvider.GetCurrentContext);
        AmbientDecisionStore = new AmbientDecisionStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DesktopPet",
                "ambient-decisions.json"),
            () => ObservationPermissionService.Current.StoredAmbientDecisionCount);
        DesktopContextProvider = new ForegroundDesktopContextProvider(
            ForegroundWindowCollector,
            ObservationPermissionService,
            UiAutomationContextCollector,
            WindowCaptureService,
            VisualContextAnalyzer);
    }

    public void Dispose()
    {
        ImageCaptureCoordinator.Dispose();
        EnvironmentCoordinator.Dispose();
    }
}
