using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using UnderWarden.Logic.Map;
using NUnit.Framework;

namespace UnderWarden.Tests.Logic;

[TestFixture]
public class FovComputerTests
{
    // -----------------------------------------------------------------------
    // Helper: small open room (all-floor interior, wall border)
    // Uses CreateArena which gives walls on edges, floor inside.
    // -----------------------------------------------------------------------

    private static GameMap OpenRoom(int w = 15, int h = 15) => GameMap.CreateArena(w, h);

    // -----------------------------------------------------------------------
    // Player tile
    // -----------------------------------------------------------------------

    [Test]
    public void FovComputer_PlayerTile_AlwaysVisible()
    {
        var map = OpenRoom();
        FovComputer.Compute(map, 7, 7);

        Assert.That(map.IsVisible(7, 7), Is.True, "Player's own tile must always be visible.");
    }

    [Test]
    public void FovComputer_PlayerTile_AlwaysExplored()
    {
        var map = OpenRoom();
        FovComputer.Compute(map, 7, 7);

        Assert.That(map.IsExplored(7, 7), Is.True, "Player's own tile must be explored after compute.");
    }

    // -----------------------------------------------------------------------
    // Open room — adjacent tiles visible
    // -----------------------------------------------------------------------

    [Test]
    public void FovComputer_OpenRoom_AdjacentTilesVisible()
    {
        var map = OpenRoom();
        FovComputer.Compute(map, 5, 5);

        // All four orthogonal neighbours should be visible in an open room
        Assert.That(map.IsVisible(4, 5), Is.True, "Left tile should be visible.");
        Assert.That(map.IsVisible(6, 5), Is.True, "Right tile should be visible.");
        Assert.That(map.IsVisible(5, 4), Is.True, "Up tile should be visible.");
        Assert.That(map.IsVisible(5, 6), Is.True, "Down tile should be visible.");
    }

    [Test]
    public void FovComputer_OpenRoom_DiagonalTilesVisible()
    {
        var map = OpenRoom();
        FovComputer.Compute(map, 5, 5);

        Assert.That(map.IsVisible(4, 4), Is.True, "Diagonal tile should be visible in open room.");
        Assert.That(map.IsVisible(6, 6), Is.True, "Diagonal tile should be visible in open room.");
    }

    // -----------------------------------------------------------------------
    // Walls block line of sight
    // -----------------------------------------------------------------------

    [Test]
    public void FovComputer_Wall_BlocksLosBehindIt()
    {
        // Create a 15x15 all-walls map, carve a corridor with a wall in the middle
        var map = new GameMap(15, 15, allWalls: true);

        // Carve a horizontal strip of floor tiles at y=7
        for (int x = 1; x < 14; x++)
            map.SetTile(x, 7, TileKind.Floor);

        // Place player at (2, 7), a wall blocker at (6, 7), open past it
        // Leave (6,7) as Wall (it was never carved — allWalls)
        // Actually all floor tiles at x 1-5 and 7-13, leave x=6 as wall
        map.SetTile(6, 7, TileKind.Wall); // explicit wall blocker

        // Player at (2, 7) — should see (3,7), (4,7), (5,7) but NOT (7,7) because (6,7) is wall
        FovComputer.Compute(map, 2, 7, radius: 12);

        Assert.That(map.IsVisible(5, 7), Is.True, "Tile before the wall should be visible.");
        Assert.That(map.IsVisible(7, 7), Is.False, "Tile behind the wall should NOT be visible.");
    }

    [Test]
    public void FovComputer_Wall_IsItself_Visible_AtEdge()
    {
        // The wall tile that blocks LoS should still be visible (you can see a wall's face)
        var map = new GameMap(15, 15, allWalls: true);
        for (int x = 1; x < 14; x++)
            map.SetTile(x, 7, TileKind.Floor);
        map.SetTile(6, 7, TileKind.Wall); // blocker

        FovComputer.Compute(map, 2, 7, radius: 12);

        // The wall face facing the player should be visible
        Assert.That(map.IsVisible(6, 7), Is.True, "The blocking wall tile should be visible (you see its face).");
    }

    // -----------------------------------------------------------------------
    // Radius limits visibility
    // -----------------------------------------------------------------------

    [Test]
    public void FovComputer_Radius_LimitsVisibility()
    {
        // Open room, player in centre. Radius 2 should not light up tiles at distance 4.
        var map = OpenRoom(15, 15);
        int cx = 7, cy = 7;
        int radius = 2;

        FovComputer.Compute(map, cx, cy, radius);

        // Distance-1 tiles must be visible
        Assert.That(map.IsVisible(cx + 1, cy), Is.True, "Distance-1 tile should be visible.");

        // Distance-4 tile (well beyond radius 2) must not be visible
        // (4 > 2, so it must be dark)
        Assert.That(map.IsVisible(cx + 4, cy), Is.False, "Distance-4 tile should NOT be visible at radius=2.");
    }

    [Test]
    public void FovComputer_Radius_AllowsTilesWithinRadius()
    {
        var map = OpenRoom(15, 15);
        FovComputer.Compute(map, 7, 7, radius: 5);

        // Tiles at distance ~5 orthogonally should be visible
        Assert.That(map.IsVisible(7 + 5, 7), Is.True, "Distance-5 tile should be visible at radius=5.");
    }

    // -----------------------------------------------------------------------
    // Explored state persists across recomputes
    // -----------------------------------------------------------------------

    [Test]
    public void FovComputer_ExploredPersistsAfterClearAllVisible()
    {
        var map = OpenRoom(15, 15);

        // First compute: player at (5, 7) — sees tiles around (5,7)
        FovComputer.Compute(map, 5, 7, radius: 3);
        bool wasExplored = map.IsExplored(5, 7);
        Assert.That(wasExplored, Is.True, "Tile at (5,7) should be explored after first compute.");

        // Second compute: player moves to (12, 7) — (5,7) should no longer be visible
        FovComputer.Compute(map, 12, 7, radius: 3);

        Assert.That(map.IsVisible(5, 7), Is.False, "Tile at (5,7) should not be visible after player moved away.");
        Assert.That(map.IsExplored(5, 7), Is.True, "Tile at (5,7) should remain explored even when not visible.");
    }

    [Test]
    public void FovComputer_ClearAllVisible_DoesNotClearExplored()
    {
        var map = OpenRoom();
        FovComputer.Compute(map, 5, 5);
        Assert.That(map.IsExplored(5, 5), Is.True);

        // Manually call ClearAllVisible (as Compute does at the start of each call)
        map.ClearAllVisible();

        Assert.That(map.IsExplored(5, 5), Is.True, "Explored must survive ClearAllVisible.");
        Assert.That(map.IsVisible(5, 5), Is.False, "Visible should be cleared.");
    }

    // -----------------------------------------------------------------------
    // RevealAll — used by scenarios (no fog of war)
    // -----------------------------------------------------------------------

    [Test]
    public void FovComputer_ScenarioRevealAll_AllTilesVisible()
    {
        var map = GameMap.CreateArena(10, 10);
        map.RevealAll();

        // Every tile — walls and floors — should be visible and explored
        for (int x = 0; x < 10; x++)
        for (int y = 0; y < 10; y++)
        {
            Assert.That(map.IsVisible(x, y), Is.True, $"Tile ({x},{y}) should be visible after RevealAll.");
            Assert.That(map.IsExplored(x, y), Is.True, $"Tile ({x},{y}) should be explored after RevealAll.");
        }
    }

    [Test]
    public void FovComputer_ScenarioRevealAll_AllWallsMap()
    {
        // RevealAll should work on a pure-wall map too (used for test harness setups)
        var map = new GameMap(8, 8, allWalls: true);
        map.RevealAll();

        Assert.That(map.IsVisible(0, 0), Is.True);
        Assert.That(map.IsVisible(7, 7), Is.True);
    }

    // -----------------------------------------------------------------------
    // Dungeon mode: RecomputeFov guard
    // -----------------------------------------------------------------------

    [Test]
    public void GameState_RecomputeFov_NoOpWhenNotDungeonMode()
    {
        // Scenario mode (IsDungeonMode defaults to false) — RecomputeFov should be a no-op.
        // Map starts unexplored (default GameMap constructor, no RevealAll).
        var map = new GameMap(10, 10); // all floor, all unexplored
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        map.RegisterEntity(player);
        var rng = new SeededRandom(42);
        var state = new GameState(player, new List<Entity>(), map, rng)
        {
            // IsDungeonMode defaults to false (init property)
        };

        state.RecomputeFov(); // should be a no-op

        // No tiles should have become visible because IsDungeonMode=false skips compute
        Assert.That(map.IsVisible(5, 5), Is.False, "RecomputeFov should be no-op in scenario mode.");
    }

    [Test]
    public void GameState_RecomputeFov_ComputesWhenDungeonMode()
    {
        // Dungeon mode: RecomputeFov should run FovComputer.
        var map = new GameMap(15, 15, allWalls: true);
        // Carve a small room around the player
        for (int x = 3; x < 12; x++)
        for (int y = 3; y < 12; y++)
            map.SetTile(x, y, TileKind.Floor);

        var player = new Entity(0, "Player", 7, 7, blocksMovement: true);
        map.RegisterEntity(player);
        var rng = new SeededRandom(42);
        var state = new GameState(player, new List<Entity>(), map, rng)
        {
            IsDungeonMode = true,
        };

        state.RecomputeFov(radius: 4);

        // Player's own tile and adjacent tiles should be visible
        Assert.That(map.IsVisible(7, 7), Is.True, "Player tile should be visible.");
        Assert.That(map.IsVisible(7, 8), Is.True, "Adjacent tile should be visible.");
    }

    // -----------------------------------------------------------------------
    // Out-of-bounds safety
    // -----------------------------------------------------------------------

    [Test]
    public void FovComputer_OutOfBounds_IsNotVisible()
    {
        var map = OpenRoom(10, 10);
        FovComputer.Compute(map, 5, 5);

        // Out-of-bounds queries should return false safely
        Assert.That(map.IsVisible(-1, 5), Is.False);
        Assert.That(map.IsVisible(5, -1), Is.False);
        Assert.That(map.IsVisible(100, 100), Is.False);
    }

    [Test]
    public void FovComputer_PlayerAtEdge_DoesNotCrash()
    {
        // Player at tile (1,1) in a 10x10 arena — FOV should not throw even with edges nearby
        var map = OpenRoom(10, 10);
        Assert.DoesNotThrow(() => FovComputer.Compute(map, 1, 1, radius: 8));
        Assert.That(map.IsVisible(1, 1), Is.True);
    }
}
