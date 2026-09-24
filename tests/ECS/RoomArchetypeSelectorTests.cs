using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

/// <summary>
/// Tests for RoomArchetypeSelector.
/// Verifies hard rules, shape-based exclusions, depth filtering, and determinism.
/// </summary>
[TestFixture]
public class RoomArchetypeSelectorTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Build a small GameMap with all tiles walkable within the room bounds.
    /// This simulates a carved rectangle — every tile in [room.X, room.X+w) x [room.Y, room.Y+h) is Floor.
    /// </summary>
    private static (Room Room, GameMap Map) MakeWalkableRoom(
        int width, int height,
        RoomShape shape = RoomShape.Rectangle)
    {
        var map = new GameMap(width + 4, height + 4, allWalls: true);
        var room = new Room(2, 2, width, height) { Shape = shape };

        // Carve every tile in the bounding box as Floor
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                map.SetTile(x, y, TileKind.Floor);

        return (room, map);
    }

    /// <summary>
    /// Run the selector N times and return all distinct archetypes observed.
    /// Using different seeds ensures the weight distribution is exercised.
    /// </summary>
    private static HashSet<RoomArchetype> CollectArchetypes(
        Room room, GameMap map, int depth,
        int roomIndex, int totalRooms,
        int iterations = 200)
    {
        var seen = new HashSet<RoomArchetype>();
        for (int i = 0; i < iterations; i++)
        {
            var result = RoomArchetypeSelector.Select(room, map, depth, roomIndex, totalRooms, new SeededRandom(i));
            seen.Add(result);
        }
        return seen;
    }

    // -------------------------------------------------------------------------
    // Hard rule: spawn room (index 0) is always Generic
    // -------------------------------------------------------------------------

    [Test]
    public void Select_SpawnRoom_AlwaysGeneric_RegardlessOfSize()
    {
        // A large rectangle at depth 3 (many archetypes eligible) — index 0 must still be Generic
        var (room, map) = MakeWalkableRoom(15, 15, RoomShape.Rectangle);

        for (int seed = 0; seed < 50; seed++)
        {
            var result = RoomArchetypeSelector.Select(room, map, depth: 3, roomIndex: 0, totalRooms: 10, new SeededRandom(seed));
            Assert.That(result, Is.EqualTo(RoomArchetype.Generic),
                $"Seed {seed}: spawn room (index 0) must always be Generic");
        }
    }

    // -------------------------------------------------------------------------
    // Hard rule: last room (index == totalRooms - 1) is always Generic
    // -------------------------------------------------------------------------

    [Test]
    public void Select_LastRoom_AlwaysGeneric()
    {
        var (room, map) = MakeWalkableRoom(15, 15, RoomShape.Rectangle);

        for (int seed = 0; seed < 50; seed++)
        {
            var result = RoomArchetypeSelector.Select(room, map, depth: 3, roomIndex: 9, totalRooms: 10, new SeededRandom(seed));
            Assert.That(result, Is.EqualTo(RoomArchetype.Generic),
                $"Seed {seed}: last room must always be Generic");
        }
    }

    // -------------------------------------------------------------------------
    // Shape: Cave-shaped rooms never get Library / Armory / Kitchen / ThroneRoom / Prison
    // -------------------------------------------------------------------------

    [Test]
    public void Select_CaveRoom_NeverGetsStructuredArchetypes()
    {
        // Large cave room eligible for most archetypes on depth, but Cave exclusions must hold
        var (room, map) = MakeWalkableRoom(12, 12, RoomShape.Cave);

        // Use middle room index so neither hard rule fires
        var seen = CollectArchetypes(room, map, depth: 3, roomIndex: 5, totalRooms: 20, iterations: 300);

        Assert.That(seen, Does.Not.Contain(RoomArchetype.Library),    "Cave room must not get Library");
        Assert.That(seen, Does.Not.Contain(RoomArchetype.Armory),     "Cave room must not get Armory");
        Assert.That(seen, Does.Not.Contain(RoomArchetype.Kitchen),    "Cave room must not get Kitchen");
        Assert.That(seen, Does.Not.Contain(RoomArchetype.ThroneRoom), "Cave room must not get ThroneRoom");
        Assert.That(seen, Does.Not.Contain(RoomArchetype.Prison),     "Cave room must not get Prison");
    }

    // -------------------------------------------------------------------------
    // Shape: CorridorRoom only gets Generic or Sewer
    // -------------------------------------------------------------------------

    [Test]
    public void Select_CorridorRoom_OnlyGenericOrSewer()
    {
        var (room, map) = MakeWalkableRoom(14, 4, RoomShape.CorridorRoom);

        var seen = CollectArchetypes(room, map, depth: 2, roomIndex: 5, totalRooms: 20, iterations: 300);

        foreach (var archetype in seen)
        {
            Assert.That(
                archetype == RoomArchetype.Generic || archetype == RoomArchetype.Sewer,
                Is.True,
                $"CorridorRoom produced {archetype} — only Generic or Sewer are allowed");
        }
    }

    // -------------------------------------------------------------------------
    // MinWalkable: room with walkable area < 25 never gets Library
    // -------------------------------------------------------------------------

    [Test]
    public void Select_SmallRoom_NeverGetsLibrary()
    {
        // 4x5 = 20 walkable tiles < 25 required for Library
        var (room, map) = MakeWalkableRoom(4, 5, RoomShape.Rectangle);

        var seen = CollectArchetypes(room, map, depth: 1, roomIndex: 3, totalRooms: 20, iterations: 200);

        Assert.That(seen, Does.Not.Contain(RoomArchetype.Library),
            "Room with walkable area < 25 must not get Library");
    }

    // -------------------------------------------------------------------------
    // Depth: room at depth < 3 never gets ThroneRoom
    // -------------------------------------------------------------------------

    [Test]
    public void Select_ShallowDepth_NeverGetsThroneRoom()
    {
        // Large room that would qualify on size but not depth for ThroneRoom (requires depth >= 3)
        var (room, map) = MakeWalkableRoom(12, 12, RoomShape.Rectangle);

        for (int depth = 1; depth <= 2; depth++)
        {
            var seen = CollectArchetypes(room, map, depth, roomIndex: 3, totalRooms: 20, iterations: 200);
            Assert.That(seen, Does.Not.Contain(RoomArchetype.ThroneRoom),
                $"Depth {depth} room must not get ThroneRoom (requires depth >= 3)");
        }
    }

    // -------------------------------------------------------------------------
    // Determinism: same seed always produces same archetype
    // -------------------------------------------------------------------------

    [Test]
    public void Select_SameSeed_ProducesSameArchetype()
    {
        var (room, map) = MakeWalkableRoom(10, 10, RoomShape.Rectangle);

        for (int seed = 0; seed < 20; seed++)
        {
            var a = RoomArchetypeSelector.Select(room, map, depth: 2, roomIndex: 3, totalRooms: 20, new SeededRandom(seed));
            var b = RoomArchetypeSelector.Select(room, map, depth: 2, roomIndex: 3, totalRooms: 20, new SeededRandom(seed));
            Assert.That(a, Is.EqualTo(b),
                $"Seed {seed}: same inputs must always produce the same archetype");
        }
    }

    // -------------------------------------------------------------------------
    // Failsafe: Generic is always reachable (even tiny rooms)
    // -------------------------------------------------------------------------

    [Test]
    public void Select_Generic_AlwaysReachable_AsFallback()
    {
        // 3x3 = 9 walkable tiles — only Generic qualifies (MinWalkable = 9)
        var (room, map) = MakeWalkableRoom(3, 3, RoomShape.Rectangle);

        for (int seed = 0; seed < 30; seed++)
        {
            var result = RoomArchetypeSelector.Select(room, map, depth: 1, roomIndex: 3, totalRooms: 20, new SeededRandom(seed));
            Assert.That(result, Is.EqualTo(RoomArchetype.Generic),
                $"3x3 room (walkable=9) should always resolve to Generic");
        }
    }

    // -------------------------------------------------------------------------
    // MapGenerator integration: first and last room in generated map are Generic
    // -------------------------------------------------------------------------

    [Test]
    public void MapGenerator_FirstRoom_AlwaysGeneric()
    {
        var result = MapGenerator.Generate(60, 40, 10, 5, 10, new SeededRandom(1337), depth: 2);
        Assert.That(result.Rooms[0].Archetype, Is.EqualTo(RoomArchetype.Generic),
            "First room (player spawn) must always be Generic");
    }

    [Test]
    public void MapGenerator_LastRoom_AlwaysGeneric()
    {
        var result = MapGenerator.Generate(60, 40, 10, 5, 10, new SeededRandom(1337), depth: 2);
        Assert.That(result.Rooms[^1].Archetype, Is.EqualTo(RoomArchetype.Generic),
            "Last room (stair exit) must always be Generic");
    }

    // -------------------------------------------------------------------------
    // MapGenerator integration: middle rooms can receive non-Generic archetypes
    // -------------------------------------------------------------------------

    [Test]
    public void MapGenerator_MiddleRooms_CanReceiveNonGenericArchetypes()
    {
        // Run multiple seeds and collect archetypes from middle rooms — at least one
        // non-Generic archetype should appear across 10 different maps.
        var seen = new HashSet<RoomArchetype>();

        for (int seed = 0; seed < 10; seed++)
        {
            var result = MapGenerator.Generate(80, 60, 15, 8, 15, new SeededRandom(seed), depth: 3);
            for (int i = 1; i < result.Rooms.Count - 1; i++)
                seen.Add(result.Rooms[i].Archetype);
        }

        Assert.That(seen.Count, Is.GreaterThan(1),
            "Middle rooms should produce more than just Generic across multiple seeds/depths");
    }
}
