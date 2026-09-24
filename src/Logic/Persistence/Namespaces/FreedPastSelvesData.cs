using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class FreedPastSelvesData
{
    [JsonPropertyName("records")]
    public List<FreedPastSelfRecord> Records { get; set; } = new();

    public FreedPastSelfRecord AddRecord(int freedPastSashaId, int freedRun, int freedFloor)
    {
        var record = new FreedPastSelfRecord
        {
            FreedPastSashaId = freedPastSashaId,
            FreedRun = freedRun,
            FreedAt = DateTimeOffset.UtcNow,
            FreedFloor = freedFloor,
        };
        Records.Add(record);
        return record;
    }
}

public sealed class FreedPastSelfRecord
{
    [JsonPropertyName("freed_past_sasha_id")]
    public int FreedPastSashaId { get; set; }

    [JsonPropertyName("freed_run")]
    public int FreedRun { get; set; }

    [JsonPropertyName("freed_at")]
    public DateTimeOffset FreedAt { get; set; }

    [JsonPropertyName("freed_floor")]
    public int FreedFloor { get; set; }
}
