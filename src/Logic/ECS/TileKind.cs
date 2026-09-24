namespace UnderWarden.Logic.ECS;

/// <summary>
/// Semantic tile type for dungeon generation. Determines walkability and rendering hints.
/// Wall, Floor, and Corridor are the core generation types; others are placed by EntityPlacer.
/// </summary>
public enum TileKind
{
    Wall,
    Floor,
    Corridor,
    StairDown,
    StairUp,
    Door,         // Closed door — blocks movement and LOS until opened
    DoorOpen,     // Open door — walkable and LOS-transparent
    Trap,
    LockedDoor,   // Locked door — blocks movement until matching key is used; never passable by pathfinder
    SecretDoor,   // Hidden door — renders as wall until discovered; passive detection reveals it as Door
}
