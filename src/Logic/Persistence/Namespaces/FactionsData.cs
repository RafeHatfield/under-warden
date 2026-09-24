using System.Text.Json.Serialization;

namespace UnderWarden.Logic.Persistence.Namespaces;

public sealed class FactionsData
{
    public const string OrcFactionId = "orc";
    public const int HostileThreshold = 3;      // unprovoked kills in a run to trigger Hostile
    public const int DecayThreshold = 5;        // runs with no negative action to decay Hostile→Neutral

    // v1 populates "orc" only. Additional factions are default-adds — missing keys default
    // to a fresh Neutral FactionStateEntry. See spec §6.3 for extension story.
    [JsonPropertyName("factions")]
    public Dictionary<string, FactionStateEntry> Factions { get; set; } = new()
    {
        ["orc"] = new FactionStateEntry()
    };

    public FactionStateEntry GetOrCreate(string factionId)
    {
        if (!Factions.TryGetValue(factionId, out var entry))
        {
            entry = new FactionStateEntry();
            Factions[factionId] = entry;
        }
        return entry;
    }

    public string GetState(string factionId) => GetOrCreate(factionId).State;

    /// <summary>
    /// Set faction to Hostile and reset the decay counter.
    /// Called at run end when unprovoked kill threshold was crossed.
    /// </summary>
    public void ApplyNegativeAction(string factionId)
    {
        var entry = GetOrCreate(factionId);
        entry.State = "hostile";
        entry.RunsSinceNegativeAction = 0;
    }

    /// <summary>
    /// Set faction to Allied. Called when Borrek arc reaches Allied state.
    /// </summary>
    public void ApplyAllied(string factionId)
    {
        var entry = GetOrCreate(factionId);
        entry.State = "allied";
        entry.RunsSinceNegativeAction = 0;
    }

    /// <summary>
    /// Called at run end when no negative action fired this run.
    /// Increments the decay counter; if it reaches DecayThreshold and state is Hostile,
    /// decays to Neutral (spec §6.3, OQ-3 resolution C).
    /// Returns true if state changed (caller should MarkDirty).
    /// </summary>
    public bool OnRunEndNoNegativeAction(string factionId, int threshold = DecayThreshold)
    {
        var entry = GetOrCreate(factionId);
        if (entry.State == "allied") return false; // allied never decays
        entry.RunsSinceNegativeAction++;
        if (entry.State == "hostile" && entry.RunsSinceNegativeAction >= threshold)
        {
            entry.State = "neutral";
            return true;
        }
        return false;
    }
}

public sealed class FactionStateEntry
{
    // "hostile" | "neutral" | "allied"
    [JsonPropertyName("state")]
    public string State { get; set; } = "neutral";

    // Cross-run decay counter for Hostile → Neutral soft-decay (per spec §6.3, OQ-3).
    [JsonPropertyName("runs_since_negative_action")]
    public int RunsSinceNegativeAction { get; set; }
}
