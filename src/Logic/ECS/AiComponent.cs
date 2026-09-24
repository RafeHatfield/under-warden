namespace UnderWarden.Logic.ECS;

/// <summary>
/// Marks an entity as AI-controlled and specifies its behavior type.
/// Read by MonsterAI.Decide to dispatch to the correct AI implementation.
/// </summary>
public sealed class AiComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>
    /// AI behavior type. Matches MonsterDefinition.AiType from YAML.
    /// "basic" = standard chase/attack, "orc_chieftain" = controller, etc.
    /// </summary>
    public string AiType { get; set; } = "basic";

    /// <summary>
    /// Faction this entity belongs to (e.g., "orc", "undead", "neutral").
    /// Used by aggravation spell to make the monster aggro its own faction.
    /// </summary>
    public string Faction { get; set; } = "neutral";

    /// <summary>
    /// Tags describing this entity's nature (e.g., ["humanoid", "living"], ["undead", "zombie"]).
    /// Used by plague scroll (corporeal check) and other tag-dependent effects.
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Whether this monster can seek and pick up floor items.</summary>
    public bool CanSeekItems { get; set; }

    /// <summary>
    /// Whether this monster can attempt to use consumable items from its inventory.
    /// Defaults to false — matches PoC's can_use_potions = False. Enable per-monster
    /// in YAML once the scroll system lands and balance is validated.
    /// </summary>
    public bool CanUseItems { get; set; }

    /// <summary>Item detection range in tiles (Chebyshev distance).</summary>
    public int SeekDistance { get; set; } = 5;

    /// <summary>Maximum inventory capacity for picked-up items. 0 = no inventory.</summary>
    public int InventorySize { get; set; }
}
