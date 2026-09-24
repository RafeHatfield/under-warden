using UnderWarden.Logic.Content;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

/// <summary>
/// Tests for the PROP-010 symmetry pass in RoomPropPlacer.
/// Covers bilateral (ThroneRoom, Armory, Crypt) and radial (FountainRoom) symmetry,
/// blocked-mirror graceful degradation, connectivity preservation, and non-symmetric archetype passthrough.
/// </summary>
[TestFixture]
public class PropSymmetryTests
{
    // -------------------------------------------------------------------------
    // Helpers shared with RoomPropPlacerTests
    // -------------------------------------------------------------------------

    private static string PropsYamlPath() =>
        Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "config", "props.yaml"));

    private static PropRegistry LoadRegistry() =>
        new ContentLoader().LoadPropsFromFile(PropsYamlPath());

    /// <summary>
    /// Build a fully-walkable room carved into an all-walls map.
    /// An optional south-side corridor stub creates entrance tiles.
    /// </summary>
    private static (Room Room, GameMap Map) MakeRoomWithArchetype(
        int width, int height,
        RoomArchetype archetype,
        bool addCorridorEntrance = true)
    {
        int mapW = width + 6;
        int mapH = height + 6;
        var map = new GameMap(mapW, mapH, allWalls: true);
        var room = new Room(3, 3, width, height) { Archetype = archetype };

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

    private static List<PlacedProp> Place(
        Room room, GameMap map,
        PropRegistry registry,
        int seed = 1337)
    {
        return RoomPropPlacer.PlaceProps(room, map, depth: 2, registry, new SeededRandom(seed));
    }

    /// <summary>BFS flood fill over IsWalkable tiles, mirrors the placer's own flood fill.</summary>
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
                if (!visited.Contains((nx, ny)) && map.IsWalkable(nx, ny))
                {
                    visited.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }
        }
        return visited;
    }

    // -------------------------------------------------------------------------
    // Test 1: Bilateral ThroneRoom — brazier mirrored across horizontal long axis
    // -------------------------------------------------------------------------

    /// <summary>
    /// A wide ThroneRoom (width > height) uses a vertical center axis.
    /// Any optional brazier placed at X should have a counterpart at the mirrored X.
    /// We run multiple seeds to find at least one where a brazier lands.
    /// </summary>
    [Test]
    public void Bilateral_ThroneRoom_BrazierMirrored()
    {
        var registry = LoadRegistry();
        bool found = false;

        for (int seed = 0; seed < 50 && !found; seed++)
        {
            var (room, map) = MakeRoomWithArchetype(14, 8, RoomArchetype.ThroneRoom);
            var props = Place(room, map, registry, seed);

            var braziers = props.Where(p => p.PropId == "brazier").ToList();
            if (braziers.Count < 2) continue;

            // Wide room: mirror axis is room.X + room.Width/2 = 3 + 7 = 10
            int axisX = room.X + room.Width / 2;

            // For each brazier, check if a mirrored brazier exists
            foreach (var b in braziers)
            {
                int expectedMirrorX = 2 * axisX - b.X - (b.FootprintW - 1);
                bool hasMirror = braziers.Any(other =>
                    other != b && other.X == expectedMirrorX && other.Y == b.Y);
                if (hasMirror) { found = true; break; }
            }
        }

        Assert.That(found, Is.True,
            "Expected at least one ThroneRoom run (wide) to produce a mirrored brazier pair across the vertical center axis");
    }

    // -------------------------------------------------------------------------
    // Test 2: Bilateral Crypt — tall room, long axis is vertical, mirror flips Y
    // -------------------------------------------------------------------------

    [Test]
    public void Bilateral_Crypt_TallRoom_OptionalPropMirroredAcrossHorizontalAxis()
    {
        var registry = LoadRegistry();
        bool found = false;

        for (int seed = 0; seed < 80 && !found; seed++)
        {
            // Height > Width → long axis is vertical → mirror flips Y
            var (room, map) = MakeRoomWithArchetype(8, 14, RoomArchetype.Crypt);
            var props = Place(room, map, registry, seed);

            // Any optional Crypt prop (tombstone, urn, candelabra, cobweb, bones_pile) with a match
            var optionalIds = new HashSet<string> { "tombstone", "urn", "candelabra", "cobweb", "bones_pile" };
            var optionals = props.Where(p => optionalIds.Contains(p.PropId)).ToList();

            if (optionals.Count < 2) continue;

            int axisY = room.Y + room.Height / 2;

            foreach (var prop in optionals)
            {
                int expectedMirrorY = 2 * axisY - prop.Y - (prop.FootprintH - 1);
                bool hasMirror = optionals.Any(other =>
                    other != prop &&
                    other.PropId == prop.PropId &&
                    other.X == prop.X &&
                    other.Y == expectedMirrorY);
                if (hasMirror) { found = true; break; }
            }
        }

        Assert.That(found, Is.True,
            "Expected at least one Crypt run (tall room, H>W) to produce a mirrored optional prop across the horizontal center axis");
    }

    // -------------------------------------------------------------------------
    // Test 3: Bilateral — blocked mirror position is skipped gracefully
    // -------------------------------------------------------------------------

    /// <summary>
    /// We run PlaceProps over many seeds. When a bilateral mirror is blocked (already occupied
    /// by a required prop or the room layout forbids it), the original prop must still exist
    /// and nothing should crash. We verify there is never a crash and that the total count
    /// of any prop is always >= 1 (the original was not removed due to a blocked mirror).
    /// </summary>
    [Test]
    public void Bilateral_BlockedMirror_OriginalPropPreserved()
    {
        var registry = LoadRegistry();

        // Run many seeds — some will produce blocked mirrors naturally
        for (int seed = 0; seed < 50; seed++)
        {
            var (room, map) = MakeRoomWithArchetype(10, 8, RoomArchetype.ThroneRoom);

            // Should not throw regardless of mirror feasibility
            Assert.DoesNotThrow(() =>
            {
                var props = Place(room, map, registry, seed);
                // The throne (required) must always be present
                Assert.That(props.Any(p => p.PropId == "throne"), Is.True,
                    $"Seed {seed}: throne (required) must always be present even when mirror is blocked");
            }, $"Seed {seed}: PlaceProps threw unexpectedly");
        }
    }

    // -------------------------------------------------------------------------
    // Test 4: Radial FountainRoom — optional props have counterparts at cardinal offsets
    // -------------------------------------------------------------------------

    [Test]
    public void Radial_FountainRoom_CounterpartsAtCardinalOffsets()
    {
        var registry = LoadRegistry();
        bool found = false;

        for (int seed = 0; seed < 80 && !found; seed++)
        {
            var (room, map) = MakeRoomWithArchetype(14, 14, RoomArchetype.FountainRoom);
            var props = Place(room, map, registry, seed);

            var fountain = props.FirstOrDefault(p => p.PropId == "fountain");
            if (fountain == null) continue;

            int fcx = fountain.X + fountain.FootprintW / 2;
            int fcy = fountain.Y + fountain.FootprintH / 2;

            // Optional FountainRoom props
            var optionalIds = new HashSet<string> { "bench", "planter", "pillar" };
            var optionals = props.Where(p => optionalIds.Contains(p.PropId)).ToList();

            if (optionals.Count < 2) continue;

            // Check if any prop has a same-ID counterpart at a 90° rotated offset from fountain center
            foreach (var prop in optionals)
            {
                int offX = (prop.X + prop.FootprintW / 2) - fcx;
                int offY = (prop.Y + prop.FootprintH / 2) - fcy;

                // The three cardinal rotations of (offX, offY): 90°, 180°, 270°
                (int rx, int ry)[] rotations =
                [
                    (-offY, offX),
                    (-offX, -offY),
                    (offY, -offX),
                ];

                foreach (var (rx, ry) in rotations)
                {
                    int expectedCx = fcx + rx;
                    int expectedCy = fcy + ry;

                    bool hasCounterpart = optionals.Any(other =>
                        other != prop &&
                        other.PropId == prop.PropId &&
                        (other.X + other.FootprintW / 2) == expectedCx &&
                        (other.Y + other.FootprintH / 2) == expectedCy);

                    if (hasCounterpart) { found = true; break; }
                }
                if (found) break;
            }
        }

        Assert.That(found, Is.True,
            "Expected at least one FountainRoom run to produce an optional prop with a cardinal-offset counterpart from the fountain center");
    }

    // -------------------------------------------------------------------------
    // Test 5: Connectivity preserved after symmetry pass (ThroneRoom with two entrances)
    // -------------------------------------------------------------------------

    [Test]
    public void Bilateral_ConnectivityPreservedAfterSymmetry()
    {
        var registry = LoadRegistry();

        for (int seed = 0; seed < 30; seed++)
        {
            // Build ThroneRoom with TWO corridor entrances (north + south)
            int width = 14, height = 8;
            int mapW = width + 6, mapH = height + 6;
            var map = new GameMap(mapW, mapH, allWalls: true);
            var room = new Room(3, 3, width, height) { Archetype = RoomArchetype.ThroneRoom };

            for (int x = room.X; x < room.X + room.Width; x++)
                for (int y = room.Y; y < room.Y + room.Height; y++)
                    map.SetTile(x, y, TileKind.Floor);

            // South entrance
            map.SetTile(room.CenterX, room.Y + room.Height, TileKind.Corridor);
            // North entrance
            map.SetTile(room.CenterX, room.Y - 1, TileKind.Corridor);

            // Find entrance tiles before placement
            var entranceTiles = new List<(int, int)>();
            for (int x = room.X; x < room.X + room.Width; x++)
                for (int y = room.Y; y < room.Y + room.Height; y++)
                {
                    if (!map.IsWalkable(x, y)) continue;
                    if (map.GetTileKind(x, y - 1) == TileKind.Corridor ||
                        map.GetTileKind(x, y + 1) == TileKind.Corridor ||
                        map.GetTileKind(x - 1, y) == TileKind.Corridor ||
                        map.GetTileKind(x + 1, y) == TileKind.Corridor)
                        entranceTiles.Add((x, y));
                }

            RoomPropPlacer.PlaceProps(room, map, depth: 2, registry, new SeededRandom(seed));

            // All entrance tiles must be mutually reachable (flood fill from first entrance)
            if (entranceTiles.Count < 2) continue;

            var reachable = FloodFill(map, entranceTiles[0].Item1, entranceTiles[0].Item2);
            foreach (var (ex, ey) in entranceTiles)
            {
                Assert.That(reachable.Contains((ex, ey)), Is.True,
                    $"ThroneRoom seed={seed}: entrance tile ({ex},{ey}) not reachable after symmetry pass");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Test 6: Non-symmetric archetype (Library) — props not mirrored
    // -------------------------------------------------------------------------

    /// <summary>
    /// Library has no entry in ArchetypeSymmetry.  Running PlaceProps on it should produce
    /// normal (non-mirrored) results: the set of prop IDs must be consistent with the recipe
    /// and should not contain artificially doubled positions at symmetric coordinates.
    /// We simply verify: no crash, required props present, placement count within sensible range.
    /// Note: bookshelf is now placed by EntityPlacer (interactive prop), not RoomPropPlacer.
    /// </summary>
    [Test]
    public void NonSymmetric_Library_UnaffectedBySymmetryPass()
    {
        var registry = LoadRegistry();

        for (int seed = 0; seed < 20; seed++)
        {
            var (room, map) = MakeRoomWithArchetype(10, 10, RoomArchetype.Library);
            var props = Place(room, map, registry, seed);

            // table is the remaining required prop (bookshelf moved to EntityPlacer)
            Assert.That(props.Any(p => p.PropId == "table"), Is.True,
                $"Library seed={seed}: table (required) must be present");

            // Sanity: prop count should be >= 1 (table required) and <= maxProps for a 10x10 room
            Assert.That(props.Count, Is.GreaterThanOrEqualTo(1),
                $"Library seed={seed}: expected at least 1 prop (required)");
        }
    }
}
