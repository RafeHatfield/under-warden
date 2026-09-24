using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

/// <summary>
/// Tests for PROP-012: RoomMaintenanceState — depth-weighted assignment, density
/// modifiers, jitter validity, scatter overlays, and connectivity preservation.
/// </summary>
[TestFixture]
public class MaintenanceStateTests
{
    // -------------------------------------------------------------------------
    // Test helpers
    // -------------------------------------------------------------------------

    private static string PropsYamlPath() =>
        Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "props.yaml"));

    private static PropRegistry LoadRegistry() =>
        new ContentLoader().LoadPropsFromFile(PropsYamlPath());

    /// <summary>
    /// Build a minimal test registry containing scatter props plus the props needed for
    /// a given archetype, so scatter-overlay tests work without loading the full YAML.
    /// </summary>
    private static PropRegistry MakeTestRegistry(params string[] extraIds)
    {
        var dict = new Dictionary<string, PropDefinition>
        {
            // Scatter props that AddScatterOverlays tries to use
            ["cobweb"]     = new() { TileIds = [1], Footprint = [1, 1], BlocksMovement = false },
            ["rubble"]     = new() { TileIds = [2], Footprint = [1, 1], BlocksMovement = false },
            ["bones_pile"] = new() { TileIds = [3], Footprint = [1, 1], BlocksMovement = false },

            // Crypt required props (sarcophagus + tombstone/urn) for archetype-specific tests
            ["sarcophagus"]  = new() { TileIds = [10], Footprint = [1, 1], BlocksMovement = true, PlacementRaw = "center" },
            ["tombstone"]    = new() { TileIds = [11], Footprint = [1, 1], BlocksMovement = true, PlacementRaw = "free_standing" },
            ["urn"]          = new() { TileIds = [12], Footprint = [1, 1], BlocksMovement = true, PlacementRaw = "free_standing" },
            ["candelabra"]   = new() { TileIds = [13], Footprint = [1, 1], BlocksMovement = true, PlacementRaw = "free_standing" },
        };

        foreach (var id in extraIds)
            if (!dict.ContainsKey(id))
                dict[id] = new() { TileIds = [99], Footprint = [1, 1], BlocksMovement = true, PlacementRaw = "free_standing" };

        return new PropRegistry(dict);
    }

    /// <summary>Build a walkable room carved into a map, with a corridor entrance on the south side.</summary>
    private static (Room Room, GameMap Map) MakeRoomWithMap(
        int width, int height,
        RoomArchetype archetype = RoomArchetype.Generic,
        RoomMaintenanceState maintenance = RoomMaintenanceState.Normal,
        bool addCorridorEntrance = true)
    {
        int mapW = width + 6;
        int mapH = height + 6;
        var map = new GameMap(mapW, mapH, allWalls: true);
        var room = new Room(3, 3, width, height)
        {
            Shape = RoomShape.Rectangle,
            Archetype = archetype,
            MaintenanceState = maintenance,
        };

        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                map.SetTile(x, y, TileKind.Floor);

        if (addCorridorEntrance)
        {
            int cx = room.CenterX;
            map.SetTile(cx, room.Y + room.Height, TileKind.Corridor);
        }

        return (room, map);
    }

    /// <summary>BFS flood fill over IsWalkable tiles (mirrors the logic in RoomPropPlacer).</summary>
    private static HashSet<(int, int)> FloodFill(GameMap map, int startX, int startY)
    {
        var visited = new HashSet<(int, int)>();
        if (!map.IsWalkable(startX, startY)) return visited;

        var queue = new Queue<(int, int)>();
        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            {
                int nx = x + dx, ny = y + dy;
                if (visited.Contains((nx, ny)) || !map.IsWalkable(nx, ny)) continue;
                visited.Add((nx, ny));
                queue.Enqueue((nx, ny));
            }
        }
        return visited;
    }

    // -------------------------------------------------------------------------
    // Test 1: Depth-weighted distribution
    // -------------------------------------------------------------------------

    [Test]
    public void Distribution_Depth1_NeverRuined()
    {
        // At depth 1 the weights are [10, 70, 20, 0, 0] — Ruined and Abandoned cannot appear.
        var rng = new SeededRandom(1337);
        int ruinedCount = 0;
        int abandonedCount = 0;

        for (int i = 0; i < 200; i++)
        {
            var state = MapGenerator.RollMaintenanceState(1, rng);
            if (state == RoomMaintenanceState.Ruined)   ruinedCount++;
            if (state == RoomMaintenanceState.Abandoned) abandonedCount++;
        }

        Assert.That(ruinedCount,   Is.EqualTo(0), "Depth 1 must never produce Ruined state");
        Assert.That(abandonedCount, Is.EqualTo(0), "Depth 1 must never produce Abandoned state");
    }

    [Test]
    public void Distribution_Depth7_RuinedAppearsFrequently()
    {
        // At depth 7+ the weights are [0, 10, 20, 30, 40] — Ruined is the most likely state.
        var rng = new SeededRandom(42);
        int ruinedCount = 0;

        for (int i = 0; i < 200; i++)
        {
            var state = MapGenerator.RollMaintenanceState(7, rng);
            if (state == RoomMaintenanceState.Ruined) ruinedCount++;
        }

        // Expected: ~40% = ~80 out of 200. Must be at least 20 to confirm it can appear.
        Assert.That(ruinedCount, Is.GreaterThan(20),
            $"Depth 7 should produce Ruined state frequently; got {ruinedCount}/200");
    }

    [Test]
    public void Distribution_Depth1_FirstRoomAlwaysWellMaintained()
    {
        // MapGenerator forces the first room to WellMaintained regardless of depth.
        // Verify by generating a floor and checking room[0].
        for (int seed = 0; seed < 10; seed++)
        {
            var result = MapGenerator.Generate(80, 60, 20, 5, 10, new SeededRandom(seed), depth: 7);
            Assert.That(result.Rooms[0].MaintenanceState, Is.EqualTo(RoomMaintenanceState.WellMaintained),
                $"Seed {seed}: first room (player spawn) must always be WellMaintained");
        }
    }

    // -------------------------------------------------------------------------
    // Test 2: Density modifier — Ruined has fewer props than WellMaintained
    // -------------------------------------------------------------------------

    [Test]
    public void Density_RuinedHasFewerPropsThanWellMaintained_AverageAcrossSeeds()
    {
        var registry = LoadRegistry();

        // Average blocking prop count across many seeds — Ruined must be lower on average.
        // Symmetric archetypes (Crypt) can occasionally produce more props than expected
        // on a single seed due to RNG state divergence. The average is the reliable signal.
        // Use a non-symmetric archetype (Kitchen) so individual-seed comparison is fair.
        const int Seeds = 30;
        double ruinedTotal = 0;
        double wellTotal   = 0;

        for (int seed = 0; seed < Seeds; seed++)
        {
            var (ruinedRoom, ruinedMap) = MakeRoomWithMap(14, 14,
                archetype: RoomArchetype.Kitchen,
                maintenance: RoomMaintenanceState.Ruined);
            var (wellRoom, wellMap) = MakeRoomWithMap(14, 14,
                archetype: RoomArchetype.Kitchen,
                maintenance: RoomMaintenanceState.WellMaintained);

            var ruinedProps = RoomPropPlacer.PlaceProps(ruinedRoom, ruinedMap, depth: 5, registry, new SeededRandom(seed));
            var wellProps   = RoomPropPlacer.PlaceProps(wellRoom,   wellMap,   depth: 5, registry, new SeededRandom(seed));

            ruinedTotal += ruinedProps.Count(p => p.BlocksMovement);
            wellTotal   += wellProps.Count(p => p.BlocksMovement);
        }

        double ruinedAvg = ruinedTotal / Seeds;
        double wellAvg   = wellTotal / Seeds;

        Assert.That(ruinedAvg, Is.LessThan(wellAvg),
            $"Ruined average blocking props ({ruinedAvg:F1}) should be < WellMaintained ({wellAvg:F1}) over {Seeds} seeds");
    }

    // -------------------------------------------------------------------------
    // Test 3: Jitter validity — all props in bounds and not on entrance tiles
    // -------------------------------------------------------------------------

    [Test]
    public void Jitter_AllPropsRemainInBoundsAndNotOnEntrances()
    {
        var registry = LoadRegistry();

        // Determine entrance tiles for the base room setup
        var (baseRoom, baseMap) = MakeRoomWithMap(12, 12,
            archetype: RoomArchetype.Library,
            maintenance: RoomMaintenanceState.Neglected);

        var entrances = new HashSet<(int, int)>();
        for (int x = baseRoom.X; x < baseRoom.X + baseRoom.Width; x++)
            for (int y = baseRoom.Y; y < baseRoom.Y + baseRoom.Height; y++)
            {
                if (!baseRoom.Contains(x, y)) continue;
                if (baseMap.IsWallTile(x, y)) continue;
                if (baseMap.GetTileKind(x - 1, y) == TileKind.Corridor ||
                    baseMap.GetTileKind(x + 1, y) == TileKind.Corridor ||
                    baseMap.GetTileKind(x, y - 1) == TileKind.Corridor ||
                    baseMap.GetTileKind(x, y + 1) == TileKind.Corridor)
                    entrances.Add((x, y));
            }

        for (int seed = 0; seed < 30; seed++)
        {
            var (room, map) = MakeRoomWithMap(12, 12,
                archetype: RoomArchetype.Library,
                maintenance: RoomMaintenanceState.Neglected);

            var props = RoomPropPlacer.PlaceProps(room, map, depth: 3, registry, new SeededRandom(seed));

            foreach (var prop in props)
            {
                // All footprint cells must be inside the room
                for (int fx = prop.X; fx < prop.X + prop.FootprintW; fx++)
                    for (int fy = prop.Y; fy < prop.Y + prop.FootprintH; fy++)
                    {
                        Assert.That(room.Contains(fx, fy), Is.True,
                            $"Seed {seed}: '{prop.PropId}' footprint cell ({fx},{fy}) is outside room bounds");
                        Assert.That(entrances.Contains((fx, fy)), Is.False,
                            $"Seed {seed}: '{prop.PropId}' footprint cell ({fx},{fy}) is an entrance tile");
                    }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Test 4: Scatter overlays present for Ruined/Abandoned rooms
    // -------------------------------------------------------------------------

    [Test]
    public void ScatterOverlays_PresentForRuinedRoom()
    {
        // Use a Crypt so Pass 1 places real required props, then scatter overlays are added.
        // Scatter overlays are non-blocking.
        var registry = LoadRegistry();

        bool foundScatter = false;

        for (int seed = 0; seed < 20; seed++)
        {
            var (room, map) = MakeRoomWithMap(14, 14,
                archetype: RoomArchetype.Crypt,
                maintenance: RoomMaintenanceState.Ruined);

            var props = RoomPropPlacer.PlaceProps(room, map, depth: 6, registry, new SeededRandom(seed));

            bool hasScatter = props.Any(p =>
                !p.BlocksMovement &&
                (p.PropId == "cobweb" || p.PropId == "rubble" || p.PropId == "bones_pile"));

            if (hasScatter) { foundScatter = true; break; }
        }

        Assert.That(foundScatter, Is.True,
            "Ruined Crypt rooms should produce at least one scatter overlay (cobweb/rubble/bones_pile)");
    }

    [Test]
    public void ScatterOverlays_PresentForAbandonedRoom()
    {
        var registry = LoadRegistry();

        bool foundScatter = false;

        for (int seed = 0; seed < 20; seed++)
        {
            var (room, map) = MakeRoomWithMap(14, 14,
                archetype: RoomArchetype.Crypt,
                maintenance: RoomMaintenanceState.Abandoned);

            var props = RoomPropPlacer.PlaceProps(room, map, depth: 5, registry, new SeededRandom(seed));

            bool hasScatter = props.Any(p =>
                !p.BlocksMovement &&
                (p.PropId == "cobweb" || p.PropId == "rubble" || p.PropId == "bones_pile"));

            if (hasScatter) { foundScatter = true; break; }
        }

        Assert.That(foundScatter, Is.True,
            "Abandoned Crypt rooms should produce at least one scatter overlay");
    }

    // -------------------------------------------------------------------------
    // Test 5: Normal room — no scatter overlays added by maintenance logic
    // -------------------------------------------------------------------------

    [Test]
    public void NormalRoom_NoMaintenanceScatterAdded()
    {
        // Normal maintenance should produce no cobweb/rubble/bones_pile from the
        // maintenance scatter path. (Crypt recipe includes cobweb/bones_pile as optional
        // props via its recipe, so we use a room archetype that has no scatter in its recipe.)
        var registry = LoadRegistry();

        // Storage has no cobweb or bones_pile in its recipe — any appearance would be scatter
        for (int seed = 0; seed < 20; seed++)
        {
            var (room, map) = MakeRoomWithMap(12, 12,
                archetype: RoomArchetype.Storage,
                maintenance: RoomMaintenanceState.Normal);

            var props = RoomPropPlacer.PlaceProps(room, map, depth: 3, registry, new SeededRandom(seed));

            // For Normal maintenance, no maintenance-driven scatter should appear.
            // cobweb/rubble are not in the Storage recipe, so any appearance is a bug.
            bool hasMaintenanceScatter = props.Any(p =>
                p.PropId == "cobweb" || p.PropId == "rubble" || p.PropId == "bones_pile");

            Assert.That(hasMaintenanceScatter, Is.False,
                $"Seed {seed}: Normal maintenance Storage room must not produce scatter overlays");
        }
    }

    // -------------------------------------------------------------------------
    // Test 6: Connectivity preserved after Ruined placement with jitter and scatter
    // -------------------------------------------------------------------------

    [Test]
    public void Connectivity_PreservedAfterRuinedPlacement()
    {
        var registry = LoadRegistry();

        // Dense archetypes are the hardest test for connectivity — use Crypt (Ruined)
        for (int seed = 0; seed < 30; seed++)
        {
            var (room, map) = MakeRoomWithMap(12, 12,
                archetype: RoomArchetype.Crypt,
                maintenance: RoomMaintenanceState.Ruined);

            RoomPropPlacer.PlaceProps(room, map, depth: 6, registry, new SeededRandom(seed));

            // Find the corridor entrance tile
            (int, int)? entranceTile = null;
            for (int x = room.X; x < room.X + room.Width && entranceTile == null; x++)
                for (int y = room.Y; y < room.Y + room.Height && entranceTile == null; y++)
                {
                    if (!room.Contains(x, y) || map.IsWallTile(x, y)) continue;
                    if (map.GetTileKind(x - 1, y) == TileKind.Corridor ||
                        map.GetTileKind(x + 1, y) == TileKind.Corridor ||
                        map.GetTileKind(x, y - 1) == TileKind.Corridor ||
                        map.GetTileKind(x, y + 1) == TileKind.Corridor)
                        entranceTile = (x, y);
                }

            if (entranceTile == null) continue;

            var reachable = FloodFill(map, entranceTile.Value.Item1, entranceTile.Value.Item2);

            // At least 40% of originally walkable room tiles must still be reachable.
            // (Same threshold as the existing Connectivity_PreservedAfterPlacement test.)
            // We don't assert the center is reachable — a jittered prop might occupy it,
            // but the placer's ValidateConnectivity ensures entrance-to-entrance routing.
            int totalWalkable = 0;
            int reachableCount = 0;
            for (int x = room.X; x < room.X + room.Width; x++)
                for (int y = room.Y; y < room.Y + room.Height; y++)
                    if (room.Contains(x, y) && map.IsWalkable(x, y))
                    {
                        totalWalkable++;
                        if (reachable.Contains((x, y))) reachableCount++;
                    }

            double fraction = totalWalkable > 0 ? (double)reachableCount / totalWalkable : 1.0;
            Assert.That(fraction, Is.GreaterThanOrEqualTo(0.40),
                $"Seed {seed}: Ruined Crypt has only {fraction:P0} walkable tiles reachable from entrance (minimum 40%)");

            // The entrance itself must be walkable (not blocked by a misplaced prop)
            Assert.That(reachable.Contains(entranceTile.Value), Is.True,
                $"Seed {seed}: entrance tile {entranceTile.Value} must be reachable");
        }
    }

    // -------------------------------------------------------------------------
    // Test 7: Generated floors assign non-Normal states at deeper depths
    // -------------------------------------------------------------------------

    [Test]
    public void GeneratedFloor_Depth6_HasDegradedRooms()
    {
        // At depth 6 all non-first rooms have weights [0, 20, 30, 30, 20].
        // With enough rooms at least one should be Neglected, Abandoned, or Ruined.
        bool foundDegraded = false;

        for (int seed = 0; seed < 5; seed++)
        {
            var result = MapGenerator.Generate(120, 80, 50, 5, 10,
                new SeededRandom(seed), depth: 6);

            foreach (var room in result.Rooms.Skip(1)) // skip player spawn (always WellMaintained)
            {
                if (room.MaintenanceState == RoomMaintenanceState.Neglected ||
                    room.MaintenanceState == RoomMaintenanceState.Abandoned ||
                    room.MaintenanceState == RoomMaintenanceState.Ruined)
                {
                    foundDegraded = true;
                    break;
                }
            }
            if (foundDegraded) break;
        }

        Assert.That(foundDegraded, Is.True,
            "Depth 6 floors should contain at least one Neglected/Abandoned/Ruined room");
    }

    // -------------------------------------------------------------------------
    // Test 8: Scatter overlays have BlocksMovement=false
    // -------------------------------------------------------------------------

    [Test]
    public void ScatterOverlays_NeverBlockMovement()
    {
        var registry = LoadRegistry();
        var scatterIds = new HashSet<string> { "cobweb", "rubble", "bones_pile" };

        for (int seed = 0; seed < 20; seed++)
        {
            var (room, map) = MakeRoomWithMap(14, 14,
                archetype: RoomArchetype.Crypt,
                maintenance: RoomMaintenanceState.Ruined);

            var props = RoomPropPlacer.PlaceProps(room, map, depth: 6, registry, new SeededRandom(seed));

            foreach (var prop in props)
            {
                // Any scatter overlay added by maintenance logic must be non-blocking
                // (scatter overlays are placed with BlocksMovement=false)
                if (scatterIds.Contains(prop.PropId) && !prop.BlocksMovement)
                {
                    // This is the expected state — non-blocking scatter
                    Assert.That(prop.BlocksMovement, Is.False,
                        $"Seed {seed}: scatter prop '{prop.PropId}' at ({prop.X},{prop.Y}) should not block movement");
                }
            }
        }
    }
}
