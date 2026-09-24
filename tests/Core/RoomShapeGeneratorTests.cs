using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for RoomShapeGenerator covering all shape carvers, shape selection,
/// connectivity invariants, bounding-box containment, and determinism.
/// </summary>
[TestFixture]
public class RoomShapeGeneratorTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static GameMap MakeMap(int w, int h) => new(w, h, allWalls: true);

    private static int CountFloor(GameMap map, Room room)
    {
        int count = 0;
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                if (map.GetTileKind(x, y) == TileKind.Floor)
                    count++;
        return count;
    }

    private static HashSet<(int, int)> GetFloorTiles(GameMap map, Room room)
    {
        var set = new HashSet<(int, int)>();
        for (int x = room.X; x < room.X + room.Width; x++)
            for (int y = room.Y; y < room.Y + room.Height; y++)
                if (map.GetTileKind(x, y) == TileKind.Floor)
                    set.Add((x, y));
        return set;
    }

    /// <summary>
    /// BFS flood fill from (startX, startY) across Floor tiles within the room's bounding box.
    /// Returns count of reachable floor tiles.
    /// </summary>
    private static int FloodFillFloor(GameMap map, Room room, int startX, int startY)
    {
        if (map.GetTileKind(startX, startY) != TileKind.Floor) return 0;

        var visited = new HashSet<(int, int)>();
        var queue = new Queue<(int, int)>();
        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (!room.Contains(nx, ny)) continue;
                if (visited.Contains((nx, ny))) continue;
                if (map.GetTileKind(nx, ny) != TileKind.Floor) continue;
                visited.Add((nx, ny));
                queue.Enqueue((nx, ny));
            }
        }
        return visited.Count;
    }

    // ─── TASK-P1-001: RoomShape enum and Room.Shape property ───────────────────

    [Test]
    public void Room_DefaultShape_IsRectangle()
    {
        var room = new Room(1, 1, 5, 5);
        Assert.That(room.Shape, Is.EqualTo(RoomShape.Rectangle));
    }

    [Test]
    public void Room_WithShapeInit_OverridesDefault()
    {
        var room = new Room(1, 1, 10, 10) { Shape = RoomShape.Cave };
        Assert.That(room.Shape, Is.EqualTo(RoomShape.Cave));
    }

    [Test]
    public void Room_WithRecord_PreservesShape()
    {
        var room = new Room(1, 1, 5, 5);
        var caveRoom = room with { Shape = RoomShape.Cave };
        Assert.That(caveRoom.Shape, Is.EqualTo(RoomShape.Cave));
        Assert.That(room.Shape, Is.EqualTo(RoomShape.Rectangle)); // original unchanged
    }

    // ─── TASK-P1-002: SelectShape ────────────────────────────────────────────────

    [Test]
    public void SelectShape_TinyRoom_AlwaysRectangle()
    {
        var rng = new SeededRandom(1337);
        // 3×3 — only Rectangle is eligible (Union needs 5×5, others need 7×7)
        for (int i = 0; i < 100; i++)
        {
            var shape = RoomShapeGenerator.SelectShape(3, 3, rng);
            Assert.That(shape, Is.EqualTo(RoomShape.Rectangle),
                "3×3 room must always get Rectangle");
        }
    }

    [Test]
    public void SelectShape_Below5x5_OnlyRectangle()
    {
        var rng = new SeededRandom(42);
        for (int i = 0; i < 200; i++)
        {
            var shape = RoomShapeGenerator.SelectShape(4, 4, rng);
            Assert.That(shape, Is.EqualTo(RoomShape.Rectangle),
                "4×4 room cannot get Union (needs 5×5) or larger shapes");
        }
    }

    [Test]
    public void SelectShape_LargeRoom_ProducesAllShapes()
    {
        // 15×15 is large enough for all shapes.
        // Run 2000 samples — each shape should appear at least once (very conservative check).
        var rng = new SeededRandom(1337);
        var seen = new HashSet<RoomShape>();
        for (int i = 0; i < 2000; i++)
            seen.Add(RoomShapeGenerator.SelectShape(15, 15, rng));

        Assert.That(seen, Contains.Item(RoomShape.Rectangle));
        Assert.That(seen, Contains.Item(RoomShape.Union));
        Assert.That(seen, Contains.Item(RoomShape.Cave));
        Assert.That(seen, Contains.Item(RoomShape.Circle));
        Assert.That(seen, Contains.Item(RoomShape.Alcove));
        Assert.That(seen, Contains.Item(RoomShape.CorridorRoom));
    }

    [Test]
    public void SelectShape_LargeRoom_DistributionWithinTolerance()
    {
        // 2000 samples, each shape within ±15% absolute of expected proportion
        // Expected for 15×15 (all eligible): Rectangle 30%, Union 30%, Cave 15%, Circle 8%, Alcove 10%, CorridorRoom 7%
        var rng = new SeededRandom(1337);
        const int Samples = 2000;
        var counts = new Dictionary<RoomShape, int>();
        foreach (RoomShape s in Enum.GetValues<RoomShape>())
            counts[s] = 0;

        for (int i = 0; i < Samples; i++)
            counts[RoomShapeGenerator.SelectShape(15, 15, rng)]++;

        // Each shape should be within ±15% absolute of its expected weight
        var expected = new Dictionary<RoomShape, double>
        {
            [RoomShape.Rectangle]    = 0.30,
            [RoomShape.Union]        = 0.30,
            [RoomShape.Cave]         = 0.15,
            [RoomShape.Circle]       = 0.08,
            [RoomShape.Alcove]       = 0.10,
            [RoomShape.CorridorRoom] = 0.07,
        };

        const double Tolerance = 0.15;
        foreach (var (shape, expectedFraction) in expected)
        {
            double actualFraction = (double)counts[shape] / Samples;
            Assert.That(actualFraction, Is.InRange(
                expectedFraction - Tolerance,
                expectedFraction + Tolerance),
                $"{shape}: expected ~{expectedFraction:P0}, got {actualFraction:P1}");
        }
    }

    [Test]
    public void SelectShape_CorridorRoom_RequiresOneAxisAtLeast8()
    {
        // 5×5 room cannot be a CorridorRoom (needs 8×3 or 3×8)
        var rng = new SeededRandom(1337);
        for (int i = 0; i < 500; i++)
        {
            var shape = RoomShapeGenerator.SelectShape(5, 5, rng);
            Assert.That(shape, Is.Not.EqualTo(RoomShape.CorridorRoom),
                "5×5 should not produce CorridorRoom (needs one dim >= 8)");
        }
    }

    // ─── TASK-P1-003: Rectangle carver ──────────────────────────────────────────

    [Test]
    public void CarveRectangle_AllTilesInBoundingBoxAreFloor()
    {
        var map = MakeMap(20, 20);
        var room = new Room(3, 3, 8, 6);
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.Rectangle, new SeededRandom(1337));

        int floor = CountFloor(map, room);
        Assert.That(floor, Is.EqualTo(room.Width * room.Height),
            "Rectangle carving must set every tile in bounding box to Floor");
    }

    [Test]
    public void CarveRectangle_NoTilesOutsideBoundingBox()
    {
        var map = MakeMap(20, 20);
        var room = new Room(3, 3, 5, 5);
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.Rectangle, new SeededRandom(1337));

        // Check corners outside bounding box remain wall
        Assert.That(map.GetTileKind(2, 2), Is.EqualTo(TileKind.Wall));
        Assert.That(map.GetTileKind(9, 9), Is.EqualTo(TileKind.Wall));
    }

    // ─── TASK-P1-004: Union carver ──────────────────────────────────────────────

    [Test]
    public void CarveUnion_CenterAlwaysFloor()
    {
        var rng = new SeededRandom(1337);
        for (int seed = 0; seed < 20; seed++)
        {
            var map = MakeMap(20, 20);
            var room = new Room(1, 1, 12, 12);
            RoomShapeGenerator.CarveRoom(map, room, RoomShape.Union, new SeededRandom(seed));
            Assert.That(map.GetTileKind(room.CenterX, room.CenterY), Is.EqualTo(TileKind.Floor),
                $"Union room center must be floor (seed {seed})");
        }
    }

    [Test]
    public void CarveUnion_AllFloorTilesConnected()
    {
        for (int seed = 0; seed < 15; seed++)
        {
            var map = MakeMap(25, 25);
            var room = new Room(1, 1, 14, 14);
            RoomShapeGenerator.CarveRoom(map, room, RoomShape.Union, new SeededRandom(seed));

            int totalFloor = CountFloor(map, room);
            if (totalFloor == 0) continue; // pathological — shouldn't happen, but skip

            int reachable = FloodFillFloor(map, room, room.CenterX, room.CenterY);
            Assert.That(reachable, Is.EqualTo(totalFloor),
                $"Union room: all floor tiles must be connected (seed {seed})");
        }
    }

    [Test]
    public void CarveUnion_NoTilesOutsideBoundingBox()
    {
        var map = MakeMap(30, 30);
        var room = new Room(5, 5, 10, 10);
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.Union, new SeededRandom(1337));

        for (int x = 0; x < 30; x++)
            for (int y = 0; y < 30; y++)
            {
                if (!room.Contains(x, y))
                    Assert.That(map.GetTileKind(x, y), Is.EqualTo(TileKind.Wall),
                        $"Tile ({x},{y}) is outside bounding box but was carved");
            }
    }

    // ─── TASK-P1-005: Cave carver ───────────────────────────────────────────────

    [Test]
    public void CarveCave_CenterAlwaysFloor()
    {
        for (int seed = 0; seed < 30; seed++)
        {
            var map = MakeMap(30, 30);
            var room = new Room(2, 2, 14, 14);
            RoomShapeGenerator.CarveRoom(map, room, RoomShape.Cave, new SeededRandom(seed));
            Assert.That(map.GetTileKind(room.CenterX, room.CenterY), Is.EqualTo(TileKind.Floor),
                $"Cave room center must always be floor (seed {seed})");
        }
    }

    [Test]
    public void CarveCave_AllFloorTilesConnected()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var map = MakeMap(30, 30);
            var room = new Room(2, 2, 14, 14);
            RoomShapeGenerator.CarveRoom(map, room, RoomShape.Cave, new SeededRandom(seed));

            int totalFloor = CountFloor(map, room);
            int reachable = FloodFillFloor(map, room, room.CenterX, room.CenterY);
            Assert.That(reachable, Is.EqualTo(totalFloor),
                $"Cave: all floor tiles must be reachable from center (seed {seed})");
        }
    }

    [Test]
    public void CarveCave_SmallRoom_FallsBackToRectangle()
    {
        // 6×6 is below minimum 7×7 — should carve full rectangle
        var map = MakeMap(20, 20);
        var room = new Room(3, 3, 6, 6);
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.Cave, new SeededRandom(1337));
        Assert.That(CountFloor(map, room), Is.EqualTo(36),
            "Cave below minimum size should fall back to full rectangle (6×6=36)");
    }

    [Test]
    public void CarveCave_NoTilesOutsideBoundingBox()
    {
        var map = MakeMap(30, 30);
        var room = new Room(5, 5, 10, 10);
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.Cave, new SeededRandom(1337));

        for (int x = 0; x < 30; x++)
            for (int y = 0; y < 30; y++)
                if (!room.Contains(x, y))
                    Assert.That(map.GetTileKind(x, y), Is.EqualTo(TileKind.Wall),
                        $"Cave carved outside bounding box at ({x},{y})");
    }

    // ─── TASK-P1-006: Circle carver ─────────────────────────────────────────────

    [Test]
    public void CarveCircle_AtLeast50PercentOfBoundingBoxCarved()
    {
        var map = MakeMap(30, 30);
        var room = new Room(3, 3, 15, 15);
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.Circle, new SeededRandom(1337));

        int floor = CountFloor(map, room);
        int bboxArea = room.Width * room.Height;
        Assert.That(floor, Is.GreaterThanOrEqualTo(bboxArea / 2),
            $"Circle must carve >= 50% of bounding box (carved {floor}/{bboxArea})");
    }

    [Test]
    public void CarveCircle_CenterAlwaysFloor()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var map = MakeMap(25, 25);
            var room = new Room(2, 2, 12, 12);
            RoomShapeGenerator.CarveRoom(map, room, RoomShape.Circle, new SeededRandom(seed));
            Assert.That(map.GetTileKind(room.CenterX, room.CenterY), Is.EqualTo(TileKind.Floor),
                $"Circle center must be floor (seed {seed})");
        }
    }

    [Test]
    public void CarveCircle_SmallRoom_FallsBackToRectangle()
    {
        var map = MakeMap(20, 20);
        var room = new Room(3, 3, 6, 6);
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.Circle, new SeededRandom(1337));
        Assert.That(CountFloor(map, room), Is.EqualTo(36),
            "Circle below 7×7 minimum should fall back to rectangle");
    }

    [Test]
    public void CarveCircle_NoTilesOutsideBoundingBox()
    {
        var map = MakeMap(30, 30);
        var room = new Room(5, 5, 12, 12);
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.Circle, new SeededRandom(1337));

        for (int x = 0; x < 30; x++)
            for (int y = 0; y < 30; y++)
                if (!room.Contains(x, y))
                    Assert.That(map.GetTileKind(x, y), Is.EqualTo(TileKind.Wall),
                        $"Circle carved outside bounding box at ({x},{y})");
    }

    // ─── TASK-P1-007: Alcove carver ─────────────────────────────────────────────

    [Test]
    public void CarveAlcove_NoTilesOutsideBoundingBox()
    {
        // Run multiple seeds to catch alcoves that might extend out of bounds
        for (int seed = 0; seed < 30; seed++)
        {
            var map = MakeMap(30, 30);
            var room = new Room(5, 5, 12, 12);
            RoomShapeGenerator.CarveRoom(map, room, RoomShape.Alcove, new SeededRandom(seed));

            for (int x = 0; x < 30; x++)
                for (int y = 0; y < 30; y++)
                    if (!room.Contains(x, y))
                        Assert.That(map.GetTileKind(x, y), Is.EqualTo(TileKind.Wall),
                            $"Alcove (seed {seed}) carved outside bounding box at ({x},{y})");
        }
    }

    [Test]
    public void CarveAlcove_CenterAlwaysFloor()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var map = MakeMap(25, 25);
            var room = new Room(2, 2, 10, 10);
            RoomShapeGenerator.CarveRoom(map, room, RoomShape.Alcove, new SeededRandom(seed));
            Assert.That(map.GetTileKind(room.CenterX, room.CenterY), Is.EqualTo(TileKind.Floor),
                $"Alcove center must be floor (seed {seed})");
        }
    }

    [Test]
    public void CarveAlcove_AllFloorTilesConnected()
    {
        for (int seed = 0; seed < 15; seed++)
        {
            var map = MakeMap(25, 25);
            var room = new Room(2, 2, 12, 12);
            RoomShapeGenerator.CarveRoom(map, room, RoomShape.Alcove, new SeededRandom(seed));

            int totalFloor = CountFloor(map, room);
            int reachable = FloodFillFloor(map, room, room.CenterX, room.CenterY);
            Assert.That(reachable, Is.EqualTo(totalFloor),
                $"Alcove (seed {seed}): all floor tiles must be connected");
        }
    }

    [Test]
    public void CarveAlcove_SmallRoom_FallsBackToRectangle()
    {
        var map = MakeMap(20, 20);
        var room = new Room(2, 2, 7, 7);
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.Alcove, new SeededRandom(1337));
        Assert.That(CountFloor(map, room), Is.EqualTo(49),
            "Alcove below 8×8 should fall back to full rectangle");
    }

    // ─── TASK-P1-008: CorridorRoom carver ───────────────────────────────────────

    [Test]
    public void CarveCorridorRoom_HorizontalRoom_LongDimensionFullyCarved()
    {
        var map = MakeMap(25, 20);
        var room = new Room(1, 5, 15, 5); // 15 wide, 5 tall — clearly horizontal
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.CorridorRoom, new SeededRandom(1337));

        // Every column (x) in the bounding box should have at least one floor tile
        for (int x = room.X; x < room.X + room.Width; x++)
        {
            bool hasFloor = false;
            for (int y = room.Y; y < room.Y + room.Height; y++)
                if (map.GetTileKind(x, y) == TileKind.Floor) { hasFloor = true; break; }
            Assert.That(hasFloor, Is.True,
                $"Horizontal corridor-room must have floor in every column (missing at x={x})");
        }
    }

    [Test]
    public void CarveCorridorRoom_VerticalRoom_LongDimensionFullyCarved()
    {
        var map = MakeMap(20, 25);
        var room = new Room(5, 1, 5, 15); // 5 wide, 15 tall — clearly vertical
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.CorridorRoom, new SeededRandom(1337));

        // Every row (y) in the bounding box should have at least one floor tile
        for (int y = room.Y; y < room.Y + room.Height; y++)
        {
            bool hasFloor = false;
            for (int x = room.X; x < room.X + room.Width; x++)
                if (map.GetTileKind(x, y) == TileKind.Floor) { hasFloor = true; break; }
            Assert.That(hasFloor, Is.True,
                $"Vertical corridor-room must have floor in every row (missing at y={y})");
        }
    }

    [Test]
    public void CarveCorridorRoom_Width2to3()
    {
        // Run a horizontal corridor room several times and verify the carved width
        for (int seed = 0; seed < 10; seed++)
        {
            var map = MakeMap(25, 15);
            var room = new Room(1, 1, 14, 5); // horizontal
            RoomShapeGenerator.CarveRoom(map, room, RoomShape.CorridorRoom, new SeededRandom(seed));

            // Count how many distinct y-rows have floor tiles
            int rowsWithFloor = 0;
            for (int y = room.Y; y < room.Y + room.Height; y++)
            {
                bool hasFloor = false;
                for (int x = room.X; x < room.X + room.Width; x++)
                    if (map.GetTileKind(x, y) == TileKind.Floor) { hasFloor = true; break; }
                if (hasFloor) rowsWithFloor++;
            }
            Assert.That(rowsWithFloor, Is.InRange(2, 3),
                $"Corridor-room should be 2-3 tiles wide (seed {seed}, got {rowsWithFloor} rows)");
        }
    }

    [Test]
    public void CarveCorridorRoom_SmallRoom_FallsBackToRectangle()
    {
        // 7×5 — neither dimension is >= 8 for the short side, 7 < 8
        // Actually: canH = 7>=8 false, canV = 5>=8 false → fallback
        var map = MakeMap(20, 20);
        var room = new Room(2, 2, 7, 5);
        RoomShapeGenerator.CarveRoom(map, room, RoomShape.CorridorRoom, new SeededRandom(1337));
        Assert.That(CountFloor(map, room), Is.EqualTo(35),
            "CorridorRoom with no eligible axis should fall back to rectangle");
    }

    [Test]
    public void CarveCorridorRoom_CenterAlwaysFloor()
    {
        for (int seed = 0; seed < 15; seed++)
        {
            var map = MakeMap(25, 20);
            var room = new Room(1, 1, 14, 5);
            RoomShapeGenerator.CarveRoom(map, room, RoomShape.CorridorRoom, new SeededRandom(seed));
            Assert.That(map.GetTileKind(room.CenterX, room.CenterY), Is.EqualTo(TileKind.Floor),
                $"CorridorRoom center must be floor (seed {seed})");
        }
    }

    // ─── Determinism ────────────────────────────────────────────────────────────

    [TestCase(RoomShape.Rectangle)]
    [TestCase(RoomShape.Union)]
    [TestCase(RoomShape.Cave)]
    [TestCase(RoomShape.Circle)]
    [TestCase(RoomShape.Alcove)]
    [TestCase(RoomShape.CorridorRoom)]
    public void CarveRoom_Deterministic_SameSeedProducesIdenticalTiles(RoomShape shape)
    {
        var room = new Room(2, 2, 12, 12);

        var map1 = MakeMap(20, 20);
        RoomShapeGenerator.CarveRoom(map1, room, shape, new SeededRandom(1337));

        var map2 = MakeMap(20, 20);
        RoomShapeGenerator.CarveRoom(map2, room, shape, new SeededRandom(1337));

        var tiles1 = GetFloorTiles(map1, room);
        var tiles2 = GetFloorTiles(map2, room);

        Assert.That(tiles1.SetEquals(tiles2),
            $"{shape}: same seed must produce identical carved tile set");
    }

    // ─── Center invariant (applies to all shapes) ────────────────────────────────

    [TestCase(RoomShape.Rectangle)]
    [TestCase(RoomShape.Union)]
    [TestCase(RoomShape.Cave)]
    [TestCase(RoomShape.Circle)]
    [TestCase(RoomShape.Alcove)]
    [TestCase(RoomShape.CorridorRoom)]
    public void CarveRoom_CenterTileAlwaysFloor_AcrossSeeds(RoomShape shape)
    {
        var room = new Room(2, 2, 12, 12);
        for (int seed = 0; seed < 20; seed++)
        {
            var map = MakeMap(25, 25);
            RoomShapeGenerator.CarveRoom(map, room, shape, new SeededRandom(seed));
            Assert.That(map.GetTileKind(room.CenterX, room.CenterY), Is.EqualTo(TileKind.Floor),
                $"{shape} seed {seed}: center ({room.CenterX},{room.CenterY}) must be Floor");
        }
    }

    // ─── Full-map connectivity test ──────────────────────────────────────────────

    [Test]
    public void MapGenerator_Generate_AllRoomCentersReachableFromPlayerSpawn()
    {
        int[] seeds = [1337, 42, 7, 999, 2026];
        foreach (int seed in seeds)
        {
            var rng = new SeededRandom(seed);
            // Small enough for a fast test, large enough to get multiple rooms
            var result = MapGenerator.Generate(60, 40, 30, 8, 14, rng);
            var map = result.Map;

            // BFS from player spawn
            var visited = new HashSet<(int, int)>();
            var queue = new Queue<(int, int)>();
            var start = result.PlayerSpawn;
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                {
                    if (visited.Contains((nx, ny))) continue;
                    // Treat Door as passable for connectivity — opening it grants access.
                    var nk = map.GetTileKind(nx, ny);
                    if (!map.IsWalkable(nx, ny) && nk != TileKind.Door) continue;
                    visited.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }

            foreach (var room in result.Rooms)
            {
                Assert.That(visited.Contains((room.CenterX, room.CenterY)), Is.True,
                    $"Seed {seed}: room center ({room.CenterX},{room.CenterY}) must be reachable from player spawn");
            }
        }
    }

    [Test]
    public void MapGenerator_Generate_PlayerSpawnIsWalkable()
    {
        int[] seeds = [1337, 42, 7, 999, 12345];
        foreach (int seed in seeds)
        {
            var rng = new SeededRandom(seed);
            var result = MapGenerator.Generate(60, 40, 30, 8, 14, rng);
            Assert.That(result.Map.IsWalkable(result.PlayerSpawn.X, result.PlayerSpawn.Y), Is.True,
                $"Seed {seed}: player spawn must be on a walkable tile");
        }
    }

    [Test]
    public void MapGenerator_Generate_ShapesRecordedOnRooms()
    {
        // After wiring, rooms should have the Shape property set (at least one non-Rectangle
        // should appear over enough seeds / room counts given the large rooms)
        var rng = new SeededRandom(1337);
        var result = MapGenerator.Generate(120, 80, 150, 12, 18, rng);

        // At minimum, all rooms have a valid RoomShape enum value
        foreach (var room in result.Rooms)
        {
            Assert.That(Enum.IsDefined(room.Shape), Is.True,
                $"Room at ({room.X},{room.Y}) has invalid Shape value {(int)room.Shape}");
        }

        // With 12-18 tile rooms and the shape distribution, we expect non-Rectangle shapes
        // over 150 placement attempts. Verify at least one room has a non-Rectangle shape.
        bool hasNonRect = result.Rooms.Any(r => r.Shape != RoomShape.Rectangle);
        Assert.That(hasNonRect, Is.True,
            "Large dungeon with many placement attempts should produce at least one non-Rectangle room");
    }
}
