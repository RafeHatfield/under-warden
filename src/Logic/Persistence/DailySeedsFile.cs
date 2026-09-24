using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence;

/// <summary>
/// Sibling file for daily-seed challenge records. Kept separate from the main
/// persistence file because daily seeds are global (not per-character) and have
/// a different lifecycle. See spec §7.
/// </summary>
public sealed class DailySeedsFile
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("saved_at")]
    public DateTimeOffset SavedAt { get; set; }

    // Keyed by date string "YYYY-MM-DD".
    [JsonPropertyName("records")]
    public Dictionary<string, DailySeedRecord> Records { get; set; } = new();

    public DailySeedRecord GetOrCreate(string dateKey)
    {
        if (!Records.TryGetValue(dateKey, out var record))
        {
            record = new DailySeedRecord();
            Records[dateKey] = record;
        }
        return record;
    }

    /// <summary>
    /// Record a completed daily-seed run. Updates best score/floor, increments run count,
    /// and captures the first-completion timestamp.
    /// </summary>
    public void RecordRun(string dateKey, string seed, int score, int floor)
    {
        var rec = GetOrCreate(dateKey);
        rec.Seed = seed;
        rec.RunsCompleted++;
        if (score > rec.BestScore) rec.BestScore = score;
        if (floor > rec.BestFloor) rec.BestFloor = floor;
        rec.FirstRunCompletedAt ??= DateTimeOffset.UtcNow;
    }
}

public sealed class DailySeedRecord
{
    [JsonPropertyName("seed")]
    public string Seed { get; set; } = "";

    [JsonPropertyName("best_score")]
    public int BestScore { get; set; }

    [JsonPropertyName("best_floor")]
    public int BestFloor { get; set; }

    [JsonPropertyName("runs_completed")]
    public int RunsCompleted { get; set; }

    [JsonPropertyName("first_run_completed_at")]
    public DateTimeOffset? FirstRunCompletedAt { get; set; }

    // Reserved for future cloud leaderboard hook. Off in v1.
    [JsonPropertyName("leaderboard_synced")]
    public bool LeaderboardSynced { get; set; }
}
