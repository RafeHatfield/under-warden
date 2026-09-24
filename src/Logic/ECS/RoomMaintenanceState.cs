namespace UnderWarden.Logic.ECS;

/// <summary>
/// How well a room has been maintained. Assigned during dungeon generation based on
/// depth and RNG. Affects prop density, scatter overlays, and optional-prop jitter.
/// Deeper floors skew toward worse states — rooms haven't been tended in longer.
/// </summary>
public enum RoomMaintenanceState
{
    WellMaintained, // Full density, no scatter. Rare — depth 1-2 only.
    Normal,         // Default. Current placement behavior, no modifiers.
    Neglected,      // 80% density, props may jitter 1 tile.
    Abandoned,      // 60% density, jitter, 1-2 scatter overlays (rubble/cobweb).
    Ruined,         // 40% density, 2-4 scatter overlays, required props may be removed.
}
