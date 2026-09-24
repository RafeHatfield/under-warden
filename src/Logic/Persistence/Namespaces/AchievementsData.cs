using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class AchievementsData
{
    [JsonPropertyName("unlocked")]
    public List<AchievementRecord> Unlocked { get; set; } = new();

    public bool HasUnlocked(string achievementId) =>
        Unlocked.Any(a => a.Id == achievementId);

    public AchievementRecord? TryUnlock(string achievementId)
    {
        if (HasUnlocked(achievementId)) return null;
        var record = new AchievementRecord
        {
            Id = achievementId,
            UnlockedAt = DateTimeOffset.UtcNow,
        };
        Unlocked.Add(record);
        return record;
    }
}

public sealed class AchievementRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("unlocked_at")]
    public DateTimeOffset UnlockedAt { get; set; }
}
