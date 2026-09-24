using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class RunCounterData
{
    [JsonPropertyName("total_runs")]
    public int TotalRuns { get; set; }

    [JsonPropertyName("first_run_started_at")]
    public DateTimeOffset? FirstRunStartedAt { get; set; }

    [JsonPropertyName("best_floor_reached")]
    public int BestFloorReached { get; set; }

    public void IncrementRunCount()
    {
        TotalRuns++;
        FirstRunStartedAt ??= DateTimeOffset.UtcNow;
    }

    public void UpdateBestFloor(int floor)
    {
        if (floor > BestFloorReached)
            BestFloorReached = floor;
    }
}
