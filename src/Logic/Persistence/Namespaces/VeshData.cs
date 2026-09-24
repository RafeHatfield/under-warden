using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class VeshData
{
    [JsonPropertyName("met")]
    public bool Met { get; set; }

    [JsonPropertyName("jobs_completed")]
    public int JobsCompleted { get; set; }

    [JsonPropertyName("spirit_received")]
    public bool SpiritReceived { get; set; }

    [JsonPropertyName("spirit_story_heard")]
    public bool SpiritStoryHeard { get; set; }

    public void RecordMet()               => Met = true;
    public void RecordJobCompleted()      => JobsCompleted++;
    public void RecordSpiritReceived()    => SpiritReceived = true;
    public void RecordSpiritStoryHeard()  => SpiritStoryHeard = true;
}
