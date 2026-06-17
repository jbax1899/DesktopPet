using System.IO;

namespace DesktopPet.App.Observation;

public interface IObservationPermissionService
{
    ObservationSettings Current { get; }

    void Save(ObservationSettings settings);

    ApplicationObservationRule? FindRule(string executablePath);

    bool IsAllowed(string executablePath, DesktopContextCapabilities capability);
}

public sealed class ObservationPermissionService : IObservationPermissionService
{
    private readonly ObservationSettingsStore _store;

    public ObservationPermissionService(ObservationSettingsStore store)
    {
        _store = store;
    }

    public ObservationSettings Current => _store.Load();

    public void Save(ObservationSettings settings)
    {
        _store.Save(settings);
    }

    public ApplicationObservationRule? FindRule(string executablePath)
    {
        var normalizedPath = ObservationApplicationIdentity.NormalizePath(executablePath);
        return Current.ApplicationRules.FirstOrDefault(rule =>
            string.Equals(rule.ExecutablePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsAllowed(string executablePath, DesktopContextCapabilities capability)
    {
        var settings = Current;
        if (!settings.ObservationEnabled)
        {
            return false;
        }

        var rule = FindRule(executablePath);
        if (rule is null || rule.IsDenied)
        {
            return false;
        }

        return capability switch
        {
            DesktopContextCapabilities.Metadata => rule.AllowMetadata,
            DesktopContextCapabilities.Structure => rule.AllowStructure,
            DesktopContextCapabilities.Visual => rule.AllowVisual,
            _ => false
        };
    }
}

public static class ObservationApplicationIdentity
{
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception) when (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return path.Trim();
        }
    }
}
