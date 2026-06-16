using DesktopPet.App.Shell;
using System.Windows;

namespace DesktopPet.App;

public partial class App
{
    private DesktopPetApplication? _desktopPetApplication;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _desktopPetApplication = new DesktopPetApplication(this);
        _desktopPetApplication.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _desktopPetApplication?.Dispose();
        base.OnExit(e);
    }
}
