using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class MaryaFragmentsData
{
    [JsonPropertyName("unlocked")]
    public List<MaryaFragmentRecord> Unlocked { get; set; } = new();

    public bool HasUnlocked(string fragmentId) =>
        Unlocked.Any(r => r.Id == fragmentId);

    public MaryaFragmentRecord? TryUnlock(string fragmentId, int unlockedRun, string place, string fragmentTextRef)
    {
        if (HasUnlocked(fragmentId)) return null;
        var record = new MaryaFragmentRecord
        {
            Id = fragmentId,
            UnlockedRun = unlockedRun,
            UnlockedAt = DateTimeOffset.UtcNow,
            Place = place,
            FragmentTextRef = fragmentTextRef,
        };
        Unlocked.Add(record);
        return record;
    }
}

public sealed class MaryaFragmentRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("unlocked_run")]
    public int UnlockedRun { get; set; }

    [JsonPropertyName("unlocked_at")]
    public DateTimeOffset UnlockedAt { get; set; }

    [JsonPropertyName("place")]
    public string Place { get; set; } = "";

    [JsonPropertyName("fragment_text_ref")]
    public string FragmentTextRef { get; set; } = "";
}
