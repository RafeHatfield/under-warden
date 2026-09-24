using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class UnshrivenGeasData
{
    [JsonPropertyName("marker_pushed")]
    public bool MarkerPushed { get; set; }

    [JsonPropertyName("marker_pushed_run")]
    public int? MarkerPushedRun { get; set; }

    [JsonPropertyName("marker_pushed_at")]
    public DateTimeOffset? MarkerPushedAt { get; set; }

    public void PushMarker(int currentRun)
    {
        if (MarkerPushed) return;
        MarkerPushed = true;
        MarkerPushedRun = currentRun;
        MarkerPushedAt = DateTimeOffset.UtcNow;
    }
}
