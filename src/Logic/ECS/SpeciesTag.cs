namespace UnderWarden.Logic.ECS;

/// <summary>
/// Identifies a monster entity's species type — the YAML key it was spawned from (e.g. "orc", "zombie").
/// Added at spawn time by MonsterFactory. Used by MonsterKnowledgeSystem to key per-species tracking.
/// Intentionally a separate component so the knowledge system can be queried without coupling to YAML content.
/// </summary>
public sealed class SpeciesTag : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>The YAML monster type ID (e.g. "orc", "plague_zombie", "orc_chieftain").</summary>
    public string TypeId { get; }

    public SpeciesTag(string typeId)
    {
        TypeId = typeId;
    }
}
