namespace UnderWarden.Logic.ECS;

/// <summary>
/// A floor trap that triggers when a creature walks onto the tile.
/// Placed by EntityPlacer.PlaceFloorFeatures; resolved by TurnController.HandleFloorTrapEntry.
///
/// Detection model (PoC-canonical):
///   - IsDetectable: whether passive detection roll is attempted at all
///   - PassiveDetectChance: probability (0-1) of detecting the trap on entry
///   - IsDetected: set true when detected; auto-avoids on re-entry
///   - IsSpent: set true after triggering — one-shot, safe to walk over afterward
///
/// NOTE: IsDisarmed is intentionally omitted — disarm is deferred out of scope this plan.
/// </summary>
public sealed class FloorTrapComponent : IComponent
{
    public Entity? Owner { get; set; }

    /// <summary>
    /// PoC-canonical trap type identifiers:
    /// "spike_trap" | "web_trap" | "alarm_plate" | "root_trap" |
    /// "teleport_trap" | "gas_trap" | "fire_trap" | "hole_trap" | "acid_trap"
    /// </summary>
    public string TrapType { get; init; } = "";

    /// <summary>True once the trap has fired — one-shot. Safe to walk over afterward.</summary>
    public bool IsSpent { get; set; }

    /// <summary>True once detected by passive detection roll. Auto-avoids on future re-entry.</summary>
    public bool IsDetected { get; set; }

    /// <summary>Whether this trap can be passively detected at all.</summary>
    public bool IsDetectable { get; init; } = true;

    /// <summary>Probability (0.0–1.0) of passive detection on entry. Context-placed traps use 0.05–0.08.</summary>
    public double PassiveDetectChance { get; init; } = 0.10;

    /// <summary>Actions to resolve when this trap triggers.</summary>
    public TrapPayloadComponent Payload { get; init; } = new();

    /// <summary>Tile ID to render when the trap is visible (detected). Hidden traps render as floor.</summary>
    public int VisibleTileId { get; init; }

    /// <summary>
    /// Presentation-layer color modulate [r, g, b, a]. Null = no modulation (default tile color).
    /// Used to visually distinguish traps sharing a tile ID (e.g., root_trap vs web_trap on tile 430).
    /// The logic layer stores this but never reads it — only Presentation uses it.
    /// </summary>
    public float[]? TileModulate { get; init; }
}
