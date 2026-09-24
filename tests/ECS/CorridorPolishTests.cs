using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

/// <summary>
/// Corridor generation invariant tests.
///
/// Wide corridors, dead-end stubs, and alcoves were intentionally removed when the
/// dungeon layout switched to a nearest-room MST connection algorithm. Remaining tests
/// verify connectivity and corridor-width invariants under the current design.
/// </summary>
[TestFixture]
public class CorridorPolishTests
{
    private const int Width = 80;
    private const int Height = 60;
    private const int MaxRooms = 20;
    private const int MinSize = 5;
    private const int MaxSize = 10;

    private static GeneratedMap MakeMap(int seed) =>
        MapGenerator.Generate(Width, Height, MaxRooms, MinSize, MaxSize, new SeededRandom(seed));

    // -------------------------------------------------------------------------
    // Corridor width invariant
    // -------------------------------------------------------------------------

    [Test]
    public void CORR001_CorridorSegment_Width_IsAlwaysOne()
    {
        var map = MakeMap(1337);
        foreach (var seg in map.Corridors)
        {
            Assert.That(seg.Width, Is.EqualTo(1),
                $"Corridor segment has Width={seg.Width}, expected 1");
        }
    }

    // -------------------------------------------------------------------------
    // Connectivity invariants — all tests below verify that map generation
    // never disconnects rooms, regardless of other corridor features.
    // -------------------------------------------------------------------------

    [Test]
    public void CORR003_AllRooms_Reachable_AcrossSeeds()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var generated = MakeMap(seed);
            var reachable = FloodFill(generated.Map, generated.PlayerSpawn.X, generated.PlayerSpawn.Y);
            foreach (var room in generated.Rooms)
            {
                Assert.That(reachable.Contains((room.CenterX, room.CenterY)), Is.True,
                    $"Seed {seed}: room center ({room.CenterX},{room.CenterY}) unreachable");
            }
        }
    }

    [Test]
    public void CORR003_AllRooms_Reachable_StandardSeeds()
    {
        for (int seed = 1337; seed < 1337 + 20; seed++)
        {
            var generated = MakeMap(seed);
            var reachable = FloodFill(generated.Map, generated.PlayerSpawn.X, generated.PlayerSpawn.Y);
            foreach (var room in generated.Rooms)
            {
                Assert.That(reachable.Contains((room.CenterX, room.CenterY)), Is.True,
                    $"Seed {seed}: room at ({room.CenterX},{room.CenterY}) unreachable");
            }
        }
    }

    [Test]
    public void CORR002_AllRooms_Reachable_ExtendedSeeds()
    {
        for (int seed = 5000; seed < 5020; seed++)
        {
            var generated = MakeMap(seed);
            var reachable = FloodFill(generated.Map, generated.PlayerSpawn.X, generated.PlayerSpawn.Y);

            foreach (var room in generated.Rooms)
            {
                Assert.That(reachable.Contains((room.CenterX, room.CenterY)), Is.True,
                    $"Seed {seed}: room at ({room.CenterX},{room.CenterY}) unreachable");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static HashSet<(int X, int Y)> FloodFill(GameMap map, int startX, int startY)
    {
        var visited = new HashSet<(int, int)>();
        var startKind = map.GetTileKind(startX, startY);
        if (!map.IsWalkable(startX, startY) && startKind != TileKind.Door) return visited;

        var queue = new Queue<(int, int)>();
        queue.Enqueue((startX, startY));
        visited.Add((startX, startY));

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (visited.Contains((nx, ny))) continue;
                var nk = map.GetTileKind(nx, ny);
                // Treat Door as passable — opening it grants access.
                if (map.IsWalkable(nx, ny) || nk == TileKind.Door)
                {
                    visited.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }
        }

        return visited;
    }
}
