using System.Collections.Generic;

namespace UnderWarden.Logic.ECS;

/// <summary>
/// Run-scoped tally of the player's unprovoked cross-faction kills, keyed by victim faction
/// (plan_end_game TASK-003). Lives on the player entity so its lifecycle matches the run:
/// created fresh per run (with the default player) and carried across floors via
/// PlayerCarryForward, so it accumulates over the whole descent and resets on a new run.
///
/// Read at run end by two consumers: the orc faction-reputation Hostile transition (this-run orc
/// count) and the flush into UnderWarden.cumulative_unprovoked_kills (the cross-run excess metric).
/// Not persisted directly — loss-on-crash is acceptable for a run-scoped counter (persistence §5).
/// </summary>
public sealed class RunAggressionTally : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>Unprovoked kills this run, keyed by victim faction id (e.g. "orc", "undead").</summary>
    public Dictionary<string, int> UnprovokedKillsByFaction { get; } = new();

    public void AddUnprovokedKill(string factionId)
    {
        UnprovokedKillsByFaction[factionId] =
            (UnprovokedKillsByFaction.TryGetValue(factionId, out var prev) ? prev : 0) + 1;
    }

    public int UnprovokedKillsFor(string factionId) =>
        UnprovokedKillsByFaction.TryGetValue(factionId, out var c) ? c : 0;

    public int Total()
    {
        int sum = 0;
        foreach (var v in UnprovokedKillsByFaction.Values) sum += v;
        return sum;
    }
}
