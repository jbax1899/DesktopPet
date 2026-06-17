namespace DesktopPet.App.Observation;

public interface IDesktopContextProvider
{
    Task<DesktopContextResult> GetCurrentContextAsync(CancellationToken cancellationToken);
}

public sealed class NoDesktopContextProvider : IDesktopContextProvider
{
    public Task<DesktopContextResult> GetCurrentContextAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(DesktopContextResult.NoContext(DesktopContextCollectionStatus.Disabled));
    }
}
