using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using NUnit.Framework;

namespace UnderWarden.Tests.Logic;

[TestFixture]
public class PathfinderTests
{
    // -----------------------------------------------------------------------
    // AStar — basic cases
    // -----------------------------------------------------------------------

    [Test]
    public void AStar_SameTile_ReturnsEmptyList()
    {
        var map = GameMap.CreateArena(10, 10);

        var path = Pathfinder.AStar(map, 5, 5, 5, 5);

        Assert.That(path, Is.Not.Null);
        Assert.That(path, Is.Empty);
    }

    [Test]
    public void AStar_AdjacentTile_ReturnsSingleStep()
    {
        var map = GameMap.CreateArena(10, 10);

        var path = Pathfinder.AStar(map, 3, 3, 4, 3);

        Assert.That(path, Is.Not.Null);
        Assert.That(path!.Count, Is.EqualTo(1));
        Assert.That(path[0], Is.EqualTo((4, 3)));
    }

    [Test]
    public void AStar_DirectPath_ReturnsCorrectLength()
    {
        // 10x10 arena, interior 1..8. Path from (1,1) to (8,8) is 7 diagonal steps.
        var map = GameMap.CreateArena(10, 10);

        var path = Pathfinder.AStar(map, 1, 1, 8, 8);

        Assert.That(path, Is.Not.Null);
        // Diagonal path: max(|8-1|,|8-1|) = 7 steps
        Assert.That(path!.Count, Is.EqualTo(7));
        Assert.That(path[^1], Is.EqualTo((8, 8)));
    }

    // -----------------------------------------------------------------------
    // AStar — wall/obstacle cases
    // -----------------------------------------------------------------------

    [Test]
    public void AStar_PathAroundWall_FindsDetour()
    {
        // 10x10 arena with a vertical wall at x=5, y=1..7 (leaving y=8 open as a gap)
        var map = GameMap.CreateArena(10, 10);
        for (int y = 1; y <= 7; y++)
            map.SetTile(5, y, TileKind.Wall);

        // Path from left of wall to right of wall — must go around through y=8
        var path = Pathfinder.AStar(map, 3, 3, 7, 3);

        Assert.That(path, Is.Not.Null, "Should find a path around the wall");
        Assert.That(path!.Count, Is.GreaterThan(4), "Detour is longer than direct path");
        Assert.That(path[^1], Is.EqualTo((7, 3)));
        // Verify path never passes through the wall
        foreach (var step in path)
            Assert.That(map.IsWalkable(step.X, step.Y), Is.True,
                $"Path step ({step.X},{step.Y}) should be walkable");
    }

    [Test]
    public void AStar_BlockedCompletely_ReturnsNull()
    {
        // Surround destination on all 8 sides so it's completely unreachable
        // (since corner-cutting is allowed, diagonal neighbours must also be walled)
        var map = GameMap.CreateArena(10, 10);
        map.SetTile(4, 5, TileKind.Wall);
        map.SetTile(6, 5, TileKind.Wall);
        map.SetTile(5, 4, TileKind.Wall);
        map.SetTile(5, 6, TileKind.Wall);
        map.SetTile(4, 4, TileKind.Wall);
        map.SetTile(6, 4, TileKind.Wall);
        map.SetTile(4, 6, TileKind.Wall);
        map.SetTile(6, 6, TileKind.Wall);

        var path = Pathfinder.AStar(map, 1, 1, 5, 5);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void AStar_DiagonalAllowed_ThroughCornerWall()
    {
        // Place walls at (4,3) and (3,4) — adjacent to the diagonal step (3,3)->(4,4).
        // Corner-cutting is NOT blocked: the game's MoveToward only checks destination
        // walkability, not cardinal neighbours. AStar must match that behaviour so
        // auto-explore can plan paths the player can actually execute.
        var map = GameMap.CreateArena(10, 10);
        map.SetTile(4, 3, TileKind.Wall);
        map.SetTile(3, 4, TileKind.Wall);

        var path = Pathfinder.AStar(map, 3, 3, 4, 4);

        Assert.That(path, Is.Not.Null, "Diagonal through a corner wall must be reachable");
        Assert.That(path!.Count, Is.EqualTo(1), "Destination is one diagonal step away");
        Assert.That(path[0], Is.EqualTo((4, 4)));
    }

    // -----------------------------------------------------------------------
    // AStar — entity blocking cases
    // -----------------------------------------------------------------------

    [Test]
    public void AStar_DestinationOccupied_StillFindsPath()
    {
        var map = GameMap.CreateArena(10, 10);
        // Place a blocking entity at the destination
        var blocker = new Entity(1, "Monster", 5, 5, blocksMovement: true);
        map.RegisterEntity(blocker);

        // Path should still succeed — destination entity is ignored
        var path = Pathfinder.AStar(map, 1, 1, 5, 5);

        Assert.That(path, Is.Not.Null, "Should reach an occupied destination tile");
        Assert.That(path![^1], Is.EqualTo((5, 5)));
    }

    [Test]
    public void AStar_ExcludesMovingEntity_DoesNotBlockSelf()
    {
        var map = GameMap.CreateArena(10, 10);
        // Moving entity sits at a mid-path tile (3,3). With a narrow corridor this would
        // block the path unless the entity correctly excludes itself.
        var mover = new Entity(1, "Player", 3, 3, blocksMovement: true);
        map.RegisterEntity(mover);

        // Path starts at the mover's position — it must not block itself
        var path = Pathfinder.AStar(map, 3, 3, 7, 3, movingEntity: mover);

        Assert.That(path, Is.Not.Null);
        Assert.That(path![^1], Is.EqualTo((7, 3)));
    }

    [Test]
    public void AStar_MidPathEntityBlocks_WhenNotExcluded()
    {
        // Narrow corridor: only one walkable row, with a blocking entity in the way.
        // Path cannot go through the blocker if it's not excluded or at the destination.
        var map = new GameMap(10, 5, allWalls: true);
        // Carve a single walkable row: y=2, x=1..8
        for (int x = 1; x <= 8; x++)
            map.SetTile(x, 2, TileKind.Floor);

        var blocker = new Entity(99, "Wall-Monster", 4, 2, blocksMovement: true);
        map.RegisterEntity(blocker);

        // Without exclusion and blocker is not the destination — path should be null
        var path = Pathfinder.AStar(map, 1, 2, 8, 2);

        Assert.That(path, Is.Null, "Blocker mid-corridor with no exclusion should block path");
    }

    // -----------------------------------------------------------------------
    // DijkstraMap
    // -----------------------------------------------------------------------

    [Test]
    public void DijkstraMap_OpenRoom_DistancesCorrect()
    {
        // 11x11 arena, interior 1..9. Source at center (5,5).
        var map = GameMap.CreateArena(11, 11);

        var dist = Pathfinder.DijkstraMap(map, 5, 5);

        // Source tile is 0
        Assert.That(dist[5, 5], Is.EqualTo(0));

        // Cardinal neighbors at distance 1
        Assert.That(dist[6, 5], Is.EqualTo(1));
        Assert.That(dist[4, 5], Is.EqualTo(1));
        Assert.That(dist[5, 6], Is.EqualTo(1));
        Assert.That(dist[5, 4], Is.EqualTo(1));

        // Diagonal neighbors at distance 1 (8-directional BFS uses step-count, not g-cost)
        Assert.That(dist[6, 6], Is.EqualTo(1));
        Assert.That(dist[4, 4], Is.EqualTo(1));
    }

    [Test]
    public void DijkstraMap_WallTile_ReturnsMaxValue()
    {
        var map = GameMap.CreateArena(10, 10);
        // Wall tiles on the edges should be unreachable from interior
        var dist = Pathfinder.DijkstraMap(map, 5, 5);

        Assert.That(dist[0, 0], Is.EqualTo(int.MaxValue));
        Assert.That(dist[9, 9], Is.EqualTo(int.MaxValue));
        Assert.That(dist[5, 0], Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void DijkstraMap_UnreachableIsland_ReturnsMaxValue()
    {
        // Create an island of floor tiles surrounded by walls (unreachable from outside)
        var map = new GameMap(15, 15, allWalls: true);
        // Outer walkable area
        for (int x = 1; x <= 6; x++)
            for (int y = 1; y <= 13; y++)
                map.SetTile(x, y, TileKind.Floor);
        // Island at x=10..12, y=5..9 — separated by wall columns 7..9
        for (int x = 10; x <= 12; x++)
            for (int y = 5; y <= 9; y++)
                map.SetTile(x, y, TileKind.Floor);

        var dist = Pathfinder.DijkstraMap(map, 3, 7);

        // Outer area is reachable
        Assert.That(dist[1, 1], Is.Not.EqualTo(int.MaxValue));
        // Island is not reachable
        Assert.That(dist[11, 7], Is.EqualTo(int.MaxValue));
    }

    // -----------------------------------------------------------------------
    // NearestWhere
    // -----------------------------------------------------------------------

    [Test]
    public void NearestWhere_FindsClosestMatch()
    {
        var map = GameMap.CreateArena(11, 11);
        var dist = Pathfinder.DijkstraMap(map, 5, 5);

        // Predicate: tiles in the top-left interior corner (1,1)..(2,2)
        // Closest should be (4,4) which is at BFS distance 1 diagonally... actually
        // the predicate targets a specific corner (1,1) so nearest matching tile is (1,1).
        var result = Pathfinder.NearestWhere(dist, map.Width, map.Height,
            (x, y) => x <= 2 && y <= 2);

        Assert.That(result, Is.Not.Null);
        // (2,2) is closer to (5,5) than (1,1) — distance 3 vs 4
        Assert.That(result!.Value, Is.EqualTo((2, 2)));
    }

    [Test]
    public void NearestWhere_NoMatch_ReturnsNull()
    {
        var map = GameMap.CreateArena(10, 10);
        var dist = Pathfinder.DijkstraMap(map, 5, 5);

        // Predicate that never matches
        var result = Pathfinder.NearestWhere(dist, map.Width, map.Height,
            (x, y) => false);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void NearestWhere_UnreachableTilesIgnored()
    {
        var map = GameMap.CreateArena(10, 10);
        var dist = Pathfinder.DijkstraMap(map, 5, 5);

        // Wall tiles at the edges have MaxValue distance — predicate matches them,
        // but NearestWhere should skip them as unreachable.
        var result = Pathfinder.NearestWhere(dist, map.Width, map.Height,
            (x, y) => x == 0 || y == 0 || x == 9 || y == 9);

        Assert.That(result, Is.Null, "Edge wall tiles should be unreachable and thus skipped");
    }
}
