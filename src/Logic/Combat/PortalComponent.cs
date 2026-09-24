using UnderWarden.Logic.ECS;

namespace UnderWarden.Logic.Combat;

/// <summary>
/// Marks an entity as one half of a portal pair.
///
/// Portal pairs consist of exactly two entities: an entrance (cyan) and an exit (yellow).
/// Each portal stores the entity ID of its linked partner. Traversal is bidirectional —
/// stepping onto either portal teleports to the other.
///
/// Only one active portal pair exists at a time per GameState. Using the Wand of Portals
/// again recycles the existing pair before placing the new one.
/// </summary>
public sealed class PortalComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>Which half of the pair this portal represents.</summary>
    public PortalType Type { get; set; }

    /// <summary>Entity ID of the linked portal on the other end. -1 if not yet linked.</summary>
    public int LinkedPortalId { get; set; } = -1;

    /// <summary>
    /// Transient flag: set to true immediately after an entity teleports through this portal.
    /// Cleared at the end of the turn. Prevents chain-teleporting when the entity arrives
    /// at the destination portal and would otherwise trigger again.
    /// </summary>
    public bool UsedThisTurn { get; set; }
}

public enum PortalType
{
    /// <summary>Cyan portal — the first placed. Teleports to exit.</summary>
    Entrance,
    /// <summary>Yellow portal — the second placed. Teleports to entrance.</summary>
    Exit,
}
