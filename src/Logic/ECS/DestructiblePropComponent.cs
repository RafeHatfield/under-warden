namespace UnderWarden.Logic.ECS;

/// <summary>
/// A destructible/interactive prop: barrel, bookshelf, or bone pile.
/// Bump-to-interact: player bumps → entity resolves → loot drops + optional trap or rouse fires.
///
/// Resolved once (IsResolved=false → true). After resolution the entity stays on the map
/// displaying the broken/open sprite but does nothing on future bumps (free action, no turn cost).
///
/// Loot pre-resolution: LootEntityIds are pre-resolved at floor-gen time so the same seed
/// always produces the same items regardless of when the player decides to bump the prop.
/// </summary>
public sealed class DestructiblePropComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>"barrel" | "bookshelf" | "bone_pile"</summary>
    public string PropKind { get; init; } = "";

    /// <summary>True once broken/searched. Entity stays on map (broken sprite) but does nothing.</summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// Item entity IDs pre-resolved at floor-gen time.
    /// Dropped to player tile on resolve, then TryPickUpItemsAt runs.
    /// </summary>
    public List<int> LootEntityIds { get; init; } = new();

    /// <summary>
    /// Optional trap payload fired on resolve. Null = not trapped.
    /// Barrels can be trapped (fire_burst_small or spike_burst_small from YAML).
    /// Bookshelves are never trapped.
    /// </summary>
    public TrapPayloadComponent? TrapPayload { get; set; }

    /// <summary>
    /// Optional rouse action: a spawn_monster TrapAction built from rouse_* YAML fields at placement time.
    /// Null = no rouse. Used by bone_pile (35% rouse chance, min_depth=2).
    /// This is a standard TrapAction(Kind="spawn_monster") — no separate RousePayload type.
    /// </summary>
    public TrapAction? RouseAction { get; set; }

    /// <summary>Tile ID for the intact/closed prop sprite.</summary>
    public int ClosedTileId { get; init; }

    /// <summary>Tile ID for the broken/open prop sprite. May equal ClosedTileId (bookshelf: no visual change).</summary>
    public int OpenTileId { get; init; }
}
