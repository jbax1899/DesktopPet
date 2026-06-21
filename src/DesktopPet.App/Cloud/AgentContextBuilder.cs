using DesktopPet.App.Memory;
using DesktopPet.App.Observation;
using DesktopPet.App.Settings;

namespace DesktopPet.App.Cloud;

public static class AgentContextBuilder
{
    public static AgentContextSnapshot Build(
        ChatRequest request,
        ChatHistoryContextSettings historySettings,
        DateTimeOffset? createdAt = null,
        TimeZoneInfo? timeZone = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        var localTimeZone = timeZone ?? TimeZoneInfo.Local;
        var values = new Dictionary<string, string>
        {
            ["temporal_context"] = TemporalContextFormatter.Format(now, localTimeZone)
        };

        if (!string.IsNullOrWhiteSpace(request.MemoriesContext))
        {
            values["memories_context"] = request.MemoriesContext;
        }

        var desktopContext = DesktopContextFormatter.Format(request.DesktopContext);
        if (!string.IsNullOrWhiteSpace(desktopContext))
        {
            values["desktop_context"] = desktopContext;
        }

        var observationHistory = ObservationHistoryFormatter.Format(request.ObservationHistory, now);
        if (!string.IsNullOrWhiteSpace(observationHistory))
        {
            values["desktop_observation_history"] = observationHistory;
        }

        if (!string.IsNullOrWhiteSpace(request.AudioObservationHistory))
        {
            values["audio_observation_history"] = request.AudioObservationHistory;
        }

        var normalizedHistorySettings = historySettings.Normalize();
        var conversationHistory = ConversationHistoryFormatter.Format(
            request.ConversationHistory,
            now,
            localTimeZone,
            normalizedHistorySettings.RegularMessageCount,
            normalizedHistorySettings.AmbientMessageCount);
        if (!string.IsNullOrWhiteSpace(conversationHistory))
        {
            values["conversation_history"] = conversationHistory;
        }

        var userName = request.ProfileSettings?.UserName?.Trim();
        var petName = request.ProfileSettings?.Nickname?.Trim();

        if (!string.IsNullOrWhiteSpace(userName))
        {
            values["user_name"] = userName;
        }

        if (!string.IsNullOrWhiteSpace(petName))
        {
            values["pet_name"] = petName;
        }

        return new AgentContextSnapshot(now, values);
    }
}
