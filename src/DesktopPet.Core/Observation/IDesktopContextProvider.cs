namespace DesktopPet.App.Observation;

public interface IDesktopContextProvider
{
    Task<DesktopContextResult> GetCurrentContextAsync(CancellationToken cancellationToken);
}
