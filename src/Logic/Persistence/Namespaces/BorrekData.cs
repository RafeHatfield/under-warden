using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class BorrekData
{
    // "wary" | "curious" | "allied"
    [JsonPropertyName("arc_state")]
    public string ArcState { get; set; } = "wary";

    [JsonPropertyName("orc_positive_actions")]
    public int OrcPositiveActions { get; set; }

    [JsonPropertyName("knife_received")]
    public bool KnifeReceived { get; set; }

    [JsonPropertyName("daughter_bloodline_news_delivered")]
    public bool DaughterBloodlineNewsDelivered { get; set; }

    // Thresholds from spec §6.4 / v3 §M-prime.
    private const int CuriousThreshold = 1;
    private const int AlliedThreshold  = 3;

    /// <summary>
    /// Record one orc-positive action and advance the arc state if thresholds are crossed.
    /// Returns true if the arc state changed (caller should MarkDirty and flush).
    /// </summary>
    public bool RecordPositiveAction()
    {
        OrcPositiveActions++;
        var previous = ArcState;
        ArcState = OrcPositiveActions >= AlliedThreshold ? "allied"
                 : OrcPositiveActions >= CuriousThreshold ? "curious"
                 : "wary";
        return ArcState != previous;
    }

    public void RecordKnifeReceived()         => KnifeReceived = true;
    public void RecordDaughterNewsDelivered() => DaughterBloodlineNewsDelivered = true;
}
