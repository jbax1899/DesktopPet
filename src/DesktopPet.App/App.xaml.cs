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

        try
        {
            _desktopPetApplication = new DesktopPetApplication(this);
            _desktopPetApplication.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DesktopPet startup failed: {ex}");
            System.Windows.MessageBox.Show(
                "Desktop Pet could not initialize its local data. Close any other running copies and try again.",
                "Desktop Pet",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _desktopPetApplication?.Dispose();
        base.OnExit(e);
    }
}
