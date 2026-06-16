using DesktopPet.App.Overlay;
using DesktopPet.App.Tray;
using WpfApplication = System.Windows.Application;

namespace DesktopPet.App.Shell;

public sealed class DesktopPetApplication : IDisposable
{
    private readonly WpfApplication _application;
    private readonly PetOverlayWindow _overlayWindow;
    private readonly PetTrayController _trayController;

    public DesktopPetApplication(WpfApplication application)
    {
        _application = application;

        _overlayWindow = new PetOverlayWindow();
        _trayController = new PetTrayController(_overlayWindow, _application.Shutdown);

        _application.MainWindow = _overlayWindow;
    }

    public void Start()
    {
        _overlayWindow.Show();
    }

    public void Dispose()
    {
        _trayController.Dispose();
    }
}
