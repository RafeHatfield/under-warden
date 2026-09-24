using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class HaelHintsData
{
    [JsonPropertyName("unlocked_hints")]
    public List<HaelHintRecord> UnlockedHints { get; set; } = new();

    [JsonPropertyName("branch_of_passage_unlock_marker")]
    public bool BranchOfPassageUnlockMarker { get; set; }

    public bool HasHint(string hintId) =>
        UnlockedHints.Any(h => h.Id == hintId);

    public HaelHintRecord? TryUnlock(string hintId, int unlockedRun, string hintTextRef)
    {
        if (HasHint(hintId)) return null;
        var record = new HaelHintRecord
        {
            Id = hintId,
            UnlockedRun = unlockedRun,
            HintTextRef = hintTextRef,
        };
        UnlockedHints.Add(record);
        return record;
    }
}

public sealed class HaelHintRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("unlocked_run")]
    public int UnlockedRun { get; set; }

    [JsonPropertyName("hint_text_ref")]
    public string HintTextRef { get; set; } = "";
}
