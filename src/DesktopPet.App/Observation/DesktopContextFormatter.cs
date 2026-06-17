using System.Text;

namespace DesktopPet.App.Observation;

public static class DesktopContextFormatter
{
    private const int MaximumFieldLength = 240;
    private const int MaximumContextLength = 1200;

    public static string? Format(DesktopTurnContext? context)
    {
        if (context is null || string.IsNullOrWhiteSpace(context.ApplicationName))
        {
            return null;
        }

        var lines = new List<string>
        {
            $"Application: {Clean(context.ApplicationName)}"
        };

        Add(lines, "Activity", context.ActivityDescription);
        if (context.ActiveDuration is not null)
        {
            lines.Add($"Active for: {FormatDuration(context.ActiveDuration.Value)}");
        }

        Add(lines, "Visible interface", context.StructuralDescription);
        Add(lines, "Visual summary", context.VisualDescription);
        lines.Add($"Access used: {FormatCapabilities(context.Capabilities)}");

        var result = string.Join(Environment.NewLine, lines);
        return result.Length <= MaximumContextLength
            ? result
            : string.Concat(result.AsSpan(0, MaximumContextLength - 3), "...");
    }

    private static void Add(ICollection<string> lines, string label, string? value)
    {
        var cleaned = Clean(value);
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            lines.Add($"{label}: {cleaned}");
        }
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ReplaceLineEndings(" "))
        {
            if (!char.IsControl(character))
            {
                builder.Append(character);
            }

            if (builder.Length >= MaximumFieldLength)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1))
        {
            return "less than a minute";
        }

        if (duration < TimeSpan.FromHours(1))
        {
            return $"about {Math.Max(1, (int)Math.Round(duration.TotalMinutes))} minutes";
        }

        return $"about {Math.Max(1, (int)Math.Round(duration.TotalHours))} hours";
    }

    private static string FormatCapabilities(DesktopContextCapabilities capabilities)
    {
        var names = new List<string>();
        if (capabilities.HasFlag(DesktopContextCapabilities.Metadata))
        {
            names.Add("window metadata");
        }

        if (capabilities.HasFlag(DesktopContextCapabilities.Structure))
        {
            names.Add("visible interface labels");
        }

        if (capabilities.HasFlag(DesktopContextCapabilities.Visual))
        {
            names.Add("visual analysis");
        }

        return names.Count == 0 ? "none" : string.Join(", ", names);
    }
}
