using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class HaelData
{
    private const int RequiredHintCount = 4;

    [JsonPropertyName("met")]
    public bool Met { get; set; }

    // "neutral" | "trusted" | "allied"
    [JsonPropertyName("relationship")]
    public string Relationship { get; set; } = "neutral";

    [JsonPropertyName("hints_unlocked")]
    public List<string> HintsUnlocked { get; set; } = new();

    // Computed — not stored. See spec §6.6: one source of truth for the gate.
    [JsonIgnore]
    public bool BranchOfPassageUnlocked =>
        Relationship == "allied" && HintsUnlocked.Count >= RequiredHintCount;

    public bool UnlockHint(string hintId)
    {
        if (HintsUnlocked.Contains(hintId)) return false;
        HintsUnlocked.Add(hintId);
        return true;
    }
}
