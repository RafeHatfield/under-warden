namespace UnderWarden.Logic.ECS;

/// <summary>
/// Attached to a raised undead entity to record its corpse lineage.
/// When a raised entity dies, TurnController checks for this tag to create
/// a SPENT corpse (instead of a fresh one) — it has already been raised once.
/// </summary>
public sealed class RaisedFromCorpseTag : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>
    /// The CorpseId of the corpse this entity was raised from.
    /// Format: "corpse_{x}_{y}_{turn}". Links back to the original death location.
    /// </summary>
    public string CorpseId { get; set; } = "";
}
