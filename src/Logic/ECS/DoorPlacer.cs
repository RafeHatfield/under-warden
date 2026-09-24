namespace UnderWarden.Logic.ECS;

/// <summary>
/// Scans the generated map for corridor-room chokepoints and places TileKind.Door tiles.
/// Doors are purely visual and walkable — no open/closed state in this implementation.
/// Called after all corridors are carved and connectivity is verified, before prop placement.
///
/// A chokepoint is a Corridor tile that:
///   1. Is adjacent to at least one Floor tile (it borders a room)
///   2. Has walls on both sides of one axis (N+S or E+W), forming a 1-tile-wide passage
///
/// A minimum spacing of 2 tiles between doors prevents double-doors in adjacent chokepoints.
/// </summary>
public static class DoorPlacer
{
    /// <summary>
    /// Place doors at all valid corridor-room chokepoints.
    /// Returns the list of door positions placed.
    /// </summary>
    public static IReadOnlyList<(int X, int Y)> PlaceDoors(GameMap map)
    {
        var doors = new List<(int X, int Y)>();
        var doorSet = new HashSet<(int, int)>();

        for (int x = 1; x < map.Width - 1; x++)
        for (int y = 1; y < map.Height - 1; y++)
        {
            if (map.GetTileKind(x, y) != TileKind.Corridor) continue;

            // Must be adjacent to at least one Floor tile (this tile borders a room)
            bool adjacentToFloor =
                map.GetTileKind(x - 1, y) == TileKind.Floor ||
                map.GetTileKind(x + 1, y) == TileKind.Floor ||
                map.GetTileKind(x, y - 1) == TileKind.Floor ||
                map.GetTileKind(x, y + 1) == TileKind.Floor;
            if (!adjacentToFloor) continue;

            // Validate chokepoint: walls on N+S (horizontal passage) or E+W (vertical passage)
            bool wallN = !map.InBounds(x, y - 1) || map.GetTileKind(x, y - 1) == TileKind.Wall;
            bool wallS = !map.InBounds(x, y + 1) || map.GetTileKind(x, y + 1) == TileKind.Wall;
            bool wallE = !map.InBounds(x + 1, y) || map.GetTileKind(x + 1, y) == TileKind.Wall;
            bool wallW = !map.InBounds(x - 1, y) || map.GetTileKind(x - 1, y) == TileKind.Wall;

            bool horizontalPassage = wallN && wallS; // walls N+S = passage runs E-W
            bool verticalPassage   = wallE && wallW; // walls E+W = passage runs N-S

            if (!horizontalPassage && !verticalPassage) continue;

            // Minimum distance: skip if a door was already placed within 2 tiles
            // This prevents back-to-back doors in short chokepoints
            bool tooClose = false;
            for (int dx = -2; dx <= 2 && !tooClose; dx++)
            for (int dy = -2; dy <= 2 && !tooClose; dy++)
                if (doorSet.Contains((x + dx, y + dy))) tooClose = true;
            if (tooClose) continue;

            map.SetTile(x, y, TileKind.Door);
            doors.Add((x, y));
            doorSet.Add((x, y));
        }

        return doors;
    }
}
