using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

/// <summary>
/// Tests for DoorPlacer — chokepoint detection and door placement at corridor-room boundaries.
/// </summary>
[TestFixture]
public class DoorPlacerTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private const int W = 30;
    private const int H = 20;

    /// <summary>
    /// Build a map with one room on the left and a corridor running east from it.
    /// The room occupies columns 2-6, rows 5-9. The corridor starts at column 7.
    ///
    ///  Layout (schematic):
    ///    WWWWWWWWWWWWWWWW
    ///    WW[room]WCCCCCW
    ///    WWWWWWWWWWWWWWWW
    ///
    /// The tile at room.right, corridor.y is a Corridor tile adjacent to a Floor tile —
    /// exactly what DoorPlacer looks for.
    /// </summary>
    private static (GameMap map, Room room, int corridorStartX, int corridorY) MakeRoomWithEastCorridor()
    {
        var map = new GameMap(W, H, allWalls: true);
        var room = new Room(2, 5, 5, 5); // x=[2..6] y=[5..9]

        // Carve room
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                map.SetTile(x, y, TileKind.Floor);

        // Carve horizontal corridor east of the room at room center row
        int corridorY = room.CenterY; // y=7
        int corridorStartX = room.X + room.Width; // x=7
        for (int x = corridorStartX; x < W - 1; x++)
            map.SetTile(x, corridorY, TileKind.Corridor);

        return (map, room, corridorStartX, corridorY);
    }

    /// <summary>
    /// Flood fill walkable tiles from a starting position.
    /// Treats closed Door tiles as passable for connectivity checks (opening a door = reachable).
    /// </summary>
    private static HashSet<(int X, int Y)> FloodFill(GameMap map, int startX, int startY)
    {
        var visited = new HashSet<(int, int)>();
        var kind = map.GetTileKind(startX, startY);
        if (!map.IsWalkable(startX, startY) && kind != TileKind.Door) return visited;

        var queue = new Queue<(int, int)>();
        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (visited.Contains((nx, ny))) continue;
                var nKind = map.GetTileKind(nx, ny);
                if (map.IsWalkable(nx, ny) || nKind == TileKind.Door)
                {
                    visited.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }
        }

        return visited;
    }

    // -------------------------------------------------------------------------
    // Core placement tests
    // -------------------------------------------------------------------------

    [Test]
    public void PlaceDoors_SingleCorridorMeetsRoom_PlacesDoorAtBoundary()
    {
        var (map, room, corridorStartX, corridorY) = MakeRoomWithEastCorridor();

        // The first corridor tile (corridorStartX, corridorY) is adjacent to a Floor tile
        // and has walls N+S (since corridor is one tile wide). It should get a door.
        var doors = DoorPlacer.PlaceDoors(map);

        Assert.That(doors.Count, Is.GreaterThanOrEqualTo(1),
            "Expected at least one door at the corridor-room boundary");

        // The door should be at or very near the corridor start (room boundary)
        bool hasBoundaryDoor = doors.Any(d =>
            d.X == corridorStartX && d.Y == corridorY);

        Assert.That(hasBoundaryDoor, Is.True,
            $"Expected a door at ({corridorStartX},{corridorY}) — the first corridor tile adjacent to the room floor");
    }

    [Test]
    public void PlaceDoors_DoorTiles_AreClosedDoors()
    {
        var (map, _, _, _) = MakeRoomWithEastCorridor();
        var doors = DoorPlacer.PlaceDoors(map);

        foreach (var (dx, dy) in doors)
        {
            Assert.That(map.GetTileKind(dx, dy), Is.EqualTo(TileKind.Door),
                $"Tile at ({dx},{dy}) should be TileKind.Door after PlaceDoors");
            Assert.That(map.IsWalkable(dx, dy), Is.False,
                $"Closed door at ({dx},{dy}) should block movement until opened");
        }
    }

    [Test]
    public void PlaceDoors_TwoCorridorsFromDifferentSides_TwoDoorsPlaced()
    {
        var map = new GameMap(W, H, allWalls: true);
        // Central room at x=[10..14] y=[5..9]
        var room = new Room(10, 5, 5, 5);
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                map.SetTile(x, y, TileKind.Floor);

        // West corridor entering at center row
        int corridorY = room.CenterY;
        for (int x = 1; x < room.X; x++)
            map.SetTile(x, corridorY, TileKind.Corridor);

        // East corridor exiting at center row
        for (int x = room.X + room.Width; x < W - 1; x++)
            map.SetTile(x, corridorY, TileKind.Corridor);

        var doors = DoorPlacer.PlaceDoors(map);

        Assert.That(doors.Count, Is.GreaterThanOrEqualTo(2),
            "Expected doors on both the west and east corridor-room boundaries");
    }

    [Test]
    public void PlaceDoors_CorridorNotAdjacentToFloor_NoDoorPlaced()
    {
        var map = new GameMap(W, H, allWalls: true);

        // Corridor running through the middle with no adjacent room floor tiles
        for (int x = 2; x < W - 2; x++)
            map.SetTile(x, 10, TileKind.Corridor);

        var doors = DoorPlacer.PlaceDoors(map);

        // No floor adjacent — no chokepoint doors should be placed on this corridor
        Assert.That(doors.Count, Is.EqualTo(0),
            "Corridor with no adjacent floor tiles should not receive doors");
    }

    [Test]
    public void PlaceDoors_DoorPositions_MatchSetTileResults()
    {
        var (map, _, _, _) = MakeRoomWithEastCorridor();
        var doors = DoorPlacer.PlaceDoors(map);

        // Every position in doors list must match a Door tile on the map
        foreach (var (dx, dy) in doors)
        {
            Assert.That(map.GetTileKind(dx, dy), Is.EqualTo(TileKind.Door),
                $"DoorPositions contains ({dx},{dy}) but map tile is {map.GetTileKind(dx, dy)}");
        }

        // Conversely, count Door tiles on map and verify it matches doors.Count
        int doorTileCount = 0;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (map.GetTileKind(x, y) == TileKind.Door)
                    doorTileCount++;

        Assert.That(doorTileCount, Is.EqualTo(doors.Count),
            "Map door tile count must match DoorPositions list length");
    }

    [Test]
    public void PlaceDoors_DoesNotPlaceDoorOnStairTile()
    {
        // Generate a real map and verify no door overlaps a stair tile
        var rng = new SeededRandom(1337);
        var result = MapGenerator.Generate(60, 40, 10, 5, 10, rng);

        foreach (var (dx, dy) in result.DoorPositions)
        {
            var kind = result.Map.GetTileKind(dx, dy);
            Assert.That(kind, Is.Not.EqualTo(TileKind.StairDown),
                $"Door placed at ({dx},{dy}) which is a StairDown tile");
            Assert.That(kind, Is.Not.EqualTo(TileKind.StairUp),
                $"Door placed at ({dx},{dy}) which is a StairUp tile");
        }
    }

    [Test]
    public void PlaceDoors_GeneratedMap_DoorPositions_Exposed()
    {
        // GeneratedMap.DoorPositions should be populated after generation
        var rng = new SeededRandom(42);
        var result = MapGenerator.Generate(60, 40, 10, 5, 10, rng);

        // DoorPositions may be empty (very few rooms, no valid chokepoints),
        // but the property must exist and not throw
        Assert.That(result.DoorPositions, Is.Not.Null);

        // Consistency: each position in DoorPositions is a Door on the map
        foreach (var (dx, dy) in result.DoorPositions)
        {
            Assert.That(result.Map.GetTileKind(dx, dy), Is.EqualTo(TileKind.Door),
                $"DoorPositions lists ({dx},{dy}) but it is not a Door tile");
        }
    }

    [Test]
    public void PlaceDoors_RoomConnectivity_PreservedAfterDoorPlacement()
    {
        // After doors are placed, all room centers should still be reachable
        // (Doors are walkable, so this should always hold)
        var rng = new SeededRandom(7777);
        var result = MapGenerator.Generate(80, 50, 15, 5, 10, rng);

        var reachable = FloodFill(result.Map, result.PlayerSpawn.X, result.PlayerSpawn.Y);

        foreach (var room in result.Rooms)
        {
            Assert.That(reachable.Contains((room.CenterX, room.CenterY)), Is.True,
                $"Room center ({room.CenterX},{room.CenterY}) not reachable after door placement");
        }
    }
}
