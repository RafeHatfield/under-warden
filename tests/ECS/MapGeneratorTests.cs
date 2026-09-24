using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

/// <summary>
/// Tests for MapGenerator (Phase 3 of the dungeon generation milestone).
/// Covers connectivity, determinism, constraints, and stair placement.
/// </summary>
[TestFixture]
public class MapGeneratorTests
{
    private const int Width = 60;
    private const int Height = 40;
    private const int MaxRooms = 10;
    private const int MinSize = 5;
    private const int MaxSize = 10;

    private static GeneratedMap MakeMap(int seed = 1337) =>
        MapGenerator.Generate(Width, Height, MaxRooms, MinSize, MaxSize, new SeededRandom(seed));

    // --- Basic sanity ---

    [Test]
    public void Generate_ReturnsResult_WithAtLeastOneRoom()
    {
        var result = MakeMap();
        Assert.That(result.Rooms.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Generate_PlayerSpawn_IsWalkable()
    {
        var result = MakeMap();
        Assert.That(result.Map.IsWalkable(result.PlayerSpawn.X, result.PlayerSpawn.Y), Is.True);
    }

    [Test]
    public void Generate_StairDown_IsWalkable()
    {
        var result = MakeMap();
        Assert.That(result.StairDownPos, Is.Not.Null);
        Assert.That(result.Map.IsWalkable(result.StairDownPos!.Value.X, result.StairDownPos.Value.Y), Is.True);
    }

    [Test]
    public void Generate_StairDown_NotInPlayerRoom()
    {
        // This can only hold when there are at least 2 rooms
        var result = MakeMap();
        if (result.Rooms.Count < 2) Assert.Ignore("Only 1 room placed — stair must share room");

        var stair = result.StairDownPos!.Value;
        Assert.That(result.PlayerRoom.Contains(stair.X, stair.Y), Is.False,
            "Stair down should be in last room, not player room");
    }

    [Test]
    public void Generate_PlayerRoom_IsFirstRoom()
    {
        var result = MakeMap();
        Assert.That(result.PlayerRoom, Is.EqualTo(result.Rooms[0]));
    }

    [Test]
    public void Generate_PlayerSpawn_IsCenterOfPlayerRoom()
    {
        var result = MakeMap();
        Assert.That(result.PlayerSpawn.X, Is.EqualTo(result.PlayerRoom.CenterX));
        Assert.That(result.PlayerSpawn.Y, Is.EqualTo(result.PlayerRoom.CenterY));
    }

    // --- Room constraints ---

    [Test]
    public void Generate_MaxRooms_Respected()
    {
        var result = MakeMap();
        Assert.That(result.Rooms.Count, Is.LessThanOrEqualTo(MaxRooms));
    }

    [Test]
    public void Generate_RoomSizes_WithinBounds()
    {
        var result = MakeMap();
        foreach (var room in result.Rooms)
        {
            Assert.That(room.Width, Is.GreaterThanOrEqualTo(MinSize));
            Assert.That(room.Width, Is.LessThanOrEqualTo(MaxSize));
            Assert.That(room.Height, Is.GreaterThanOrEqualTo(MinSize));
            Assert.That(room.Height, Is.LessThanOrEqualTo(MaxSize));
        }
    }

    [Test]
    public void Generate_NoRoomOverlaps()
    {
        var result = MakeMap();
        for (int i = 0; i < result.Rooms.Count; i++)
        for (int j = i + 1; j < result.Rooms.Count; j++)
        {
            // Rooms must not overlap (padding is fine — Intersects includes padding)
            // We check strict tile overlap by checking centers are in different rooms
            var a = result.Rooms[i];
            var b = result.Rooms[j];

            // Strict overlap: tile ranges overlap
            bool strictOverlap = a.X < b.X + b.Width && a.X + a.Width > b.X
                               && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
            Assert.That(strictOverlap, Is.False,
                $"Room {i} ({a.X},{a.Y} {a.Width}x{a.Height}) overlaps room {j} ({b.X},{b.Y} {b.Width}x{b.Height})");
        }
    }

    // --- Connectivity ---

    [Test]
    public void Generate_AllWalkableTiles_Reachable_FromPlayerSpawn()
    {
        var result = MakeMap();
        var reachable = FloodFill(result.Map, result.PlayerSpawn.X, result.PlayerSpawn.Y);

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            if (result.Map.IsWalkable(x, y))
                Assert.That(reachable.Contains((x, y)), Is.True,
                    $"Walkable tile ({x},{y}) is not reachable from player spawn");
        }
    }

    // --- Corridor tracking ---

    [Test]
    public void Generate_CorridorsRecorded_WhenMultipleRooms()
    {
        var result = MakeMap();
        if (result.Rooms.Count < 2) Assert.Ignore("Only 1 room — no corridors expected");

        Assert.That(result.Corridors, Is.Not.Empty,
            "Multiple rooms placed but no corridor segments recorded");
    }

    // --- Determinism ---

    [Test]
    public void Generate_SameSeed_ProducesIdenticalMaps()
    {
        var a = MakeMap(seed: 42);
        var b = MakeMap(seed: 42);

        Assert.That(a.Rooms.Count, Is.EqualTo(b.Rooms.Count));
        Assert.That(a.PlayerSpawn, Is.EqualTo(b.PlayerSpawn));
        Assert.That(a.StairDownPos, Is.EqualTo(b.StairDownPos));

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            Assert.That(a.Map.GetTileKind(x, y), Is.EqualTo(b.Map.GetTileKind(x, y)),
                $"Tile ({x},{y}) differs between runs with same seed");
    }

    [Test]
    public void Generate_DifferentSeeds_ProduceDifferentMaps()
    {
        var a = MakeMap(seed: 1);
        var b = MakeMap(seed: 9999);

        // It's astronomically unlikely that two different seeds produce the exact same layout
        bool anyDifference = a.PlayerSpawn != b.PlayerSpawn
            || a.StairDownPos != b.StairDownPos
            || a.Rooms.Count != b.Rooms.Count;

        Assert.That(anyDifference, Is.True,
            "Different seeds produced identical maps — PRNG may not be seeded correctly");
    }

    // --- StairRules ---

    [Test]
    public void Generate_StairsDown_False_NoStairDownPosition()
    {
        var stairs = new UnderWarden.Logic.Balance.StairRules { Down = false, Up = false };
        var result = MapGenerator.Generate(Width, Height, MaxRooms, MinSize, MaxSize,
            new SeededRandom(1337), stairs);
        Assert.That(result.StairDownPos, Is.Null);
    }

    [Test]
    public void Generate_StairDown_DefaultPlacedInLastRoom()
    {
        var result = MakeMap();
        if (result.Rooms.Count < 2) Assert.Ignore("Only 1 room");

        var lastRoom = result.Rooms[^1];
        var stair = result.StairDownPos!.Value;
        Assert.That(lastRoom.Contains(stair.X, stair.Y), Is.True,
            "Stair down should be at center of last room placed");
    }

    // --- AllWalls constructor used by generator ---

    [Test]
    public void AllWallsMap_AllNonWalkable_BeforeCarving()
    {
        var map = new UnderWarden.Logic.ECS.GameMap(Width, Height, allWalls: true);
        Assert.That(map.IsWalkable(5, 5), Is.False);
    }

    // --- Flood-fill helper ---

    private static HashSet<(int X, int Y)> FloodFill(GameMap map, int startX, int startY)
    {
        var visited = new HashSet<(int, int)>();
        var queue = new Queue<(int, int)>();
        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));

        int[] dxs = [-1, 0, 1, 0, -1, -1, 1, 1];
        int[] dys = [0, -1, 0, 1, -1, 1, -1, 1];

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            for (int i = 0; i < 8; i++)
            {
                int nx = x + dxs[i];
                int ny = y + dys[i];
                // Treat Door as passable — opening it grants access.
                bool passable = map.IsWalkable(nx, ny) || map.GetTileKind(nx, ny) == TileKind.Door;
                if (passable && visited.Add((nx, ny)))
                    queue.Enqueue((nx, ny));
            }
        }

        return visited;
    }
}
