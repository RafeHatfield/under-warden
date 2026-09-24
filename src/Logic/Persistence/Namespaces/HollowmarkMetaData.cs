using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class HollowmarkMetaData
{
    // Keys are floor numbers as strings (e.g. "1", "2"). Values are unlock tier levels (1..N).
    [JsonPropertyName("floor_unlock_levels")]
    public Dictionary<string, int> FloorUnlockLevels { get; set; } = new();

    // Stable line IDs — tracked as a set to prevent re-firing across all runs.
    // See spec §6.11: ID-not-in-set lookup; patch-safe vs integer index.
    [JsonPropertyName("between_runs_lines_fired")]
    public HashSet<string> BetweenRunsLinesFired { get; set; } = new();

    public int GetFloorUnlockLevel(int floor) =>
        FloorUnlockLevels.TryGetValue(floor.ToString(), out var level) ? level : 1;

    public void AdvanceFloorUnlockLevel(int floor, int maxLevel)
    {
        var key = floor.ToString();
        var current = FloorUnlockLevels.GetValueOrDefault(key, 1);
        if (current < maxLevel) FloorUnlockLevels[key] = current + 1;
    }

    public bool HasFiredBetweenRunsLine(string lineId) =>
        BetweenRunsLinesFired.Contains(lineId);

    public void RecordBetweenRunsLineFired(string lineId) =>
        BetweenRunsLinesFired.Add(lineId);
}
