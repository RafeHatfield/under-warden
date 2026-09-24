using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

/// <summary>
/// Tests for TileKind enum and GameMap extensions added in Phase 1 of the dungeon generation milestone.
/// Verifies: SetTile keeps _walkable in sync, GetTileKind round-trips, allWalls constructor,
/// and existing CreateArena + default constructor behaviour is unchanged.
/// </summary>
[TestFixture]
public class TileKindGameMapTests
{
    // --- SetTile walkability sync ---

    [Test]
    public void SetTile_Floor_MakesTileWalkable()
    {
        var map = new GameMap(10, 10, allWalls: true);
        Assert.That(map.IsWalkable(3, 3), Is.False, "Pre-condition: allWalls map is non-walkable");

        map.SetTile(3, 3, TileKind.Floor);

        Assert.That(map.IsWalkable(3, 3), Is.True);
    }

    [Test]
    public void SetTile_Wall_MakesTileNonWalkable()
    {
        var map = new GameMap(10, 10); // default: all walkable
        Assert.That(map.IsWalkable(3, 3), Is.True, "Pre-condition: default map is walkable");

        map.SetTile(3, 3, TileKind.Wall);

        Assert.That(map.IsWalkable(3, 3), Is.False);
    }

    [Test]
    public void SetTile_Corridor_MakesTileWalkable()
    {
        var map = new GameMap(10, 10, allWalls: true);
        map.SetTile(5, 5, TileKind.Corridor);
        Assert.That(map.IsWalkable(5, 5), Is.True);
    }

    [Test]
    public void SetTile_StairDown_MakesTileWalkable()
    {
        var map = new GameMap(10, 10, allWalls: true);
        map.SetTile(5, 5, TileKind.StairDown);
        Assert.That(map.IsWalkable(5, 5), Is.True);
    }

    [Test]
    public void SetTile_StairUp_MakesTileWalkable()
    {
        var map = new GameMap(10, 10, allWalls: true);
        map.SetTile(5, 5, TileKind.StairUp);
        Assert.That(map.IsWalkable(5, 5), Is.True);
    }

    [Test]
    public void SetTile_Door_MakesTileNonWalkable()
    {
        var map = new GameMap(10, 10, allWalls: true);
        map.SetTile(5, 5, TileKind.Door);
        Assert.That(map.IsWalkable(5, 5), Is.False, "Closed door blocks movement");
    }

    [Test]
    public void OpenDoor_MakesTileWalkable()
    {
        var map = new GameMap(10, 10, allWalls: true);
        map.SetTile(5, 5, TileKind.Door);
        bool opened = map.OpenDoor(5, 5);
        Assert.That(opened, Is.True);
        Assert.That(map.IsWalkable(5, 5), Is.True);
        Assert.That(map.GetTileKind(5, 5), Is.EqualTo(TileKind.DoorOpen));
    }

    [Test]
    public void SetTile_Trap_MakesTileNonWalkable()
    {
        var map = new GameMap(10, 10); // default walkable
        map.SetTile(5, 5, TileKind.Trap);
        Assert.That(map.IsWalkable(5, 5), Is.False);
    }

    // --- GetTileKind round-trips ---

    [Test]
    public void GetTileKind_ReturnsWhatWasSet()
    {
        var map = new GameMap(10, 10, allWalls: true);

        map.SetTile(2, 3, TileKind.Floor);
        Assert.That(map.GetTileKind(2, 3), Is.EqualTo(TileKind.Floor));

        map.SetTile(4, 5, TileKind.Corridor);
        Assert.That(map.GetTileKind(4, 5), Is.EqualTo(TileKind.Corridor));

        map.SetTile(6, 7, TileKind.StairDown);
        Assert.That(map.GetTileKind(6, 7), Is.EqualTo(TileKind.StairDown));
    }

    [Test]
    public void GetTileKind_OutOfBounds_ReturnsWall()
    {
        var map = new GameMap(10, 10);
        Assert.That(map.GetTileKind(-1, 0), Is.EqualTo(TileKind.Wall));
        Assert.That(map.GetTileKind(0, -1), Is.EqualTo(TileKind.Wall));
        Assert.That(map.GetTileKind(10, 5), Is.EqualTo(TileKind.Wall));
    }

    // --- AllWalls constructor ---

    [Test]
    public void AllWallsConstructor_AllTilesNonWalkable()
    {
        var map = new GameMap(8, 8, allWalls: true);
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                Assert.That(map.IsWalkable(x, y), Is.False,
                    $"Expected non-walkable at ({x},{y})");
    }

    [Test]
    public void AllWallsConstructor_AllTileKindsAreWall()
    {
        var map = new GameMap(8, 8, allWalls: true);
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                Assert.That(map.GetTileKind(x, y), Is.EqualTo(TileKind.Wall),
                    $"Expected Wall at ({x},{y})");
    }

    // --- Existing behavior unchanged ---

    [Test]
    public void DefaultConstructor_AllTilesWalkable()
    {
        var map = new GameMap(8, 8);
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                Assert.That(map.IsWalkable(x, y), Is.True,
                    $"Expected walkable at ({x},{y})");
    }

    [Test]
    public void CreateArena_WallsOnEdges_Unchanged()
    {
        var map = GameMap.CreateArena(10, 10);
        // Edges are walls (matches existing test)
        Assert.That(map.IsWalkable(0, 0), Is.False);
        Assert.That(map.IsWalkable(9, 9), Is.False);
        // Interior is walkable
        Assert.That(map.IsWalkable(5, 5), Is.True);
    }

    [Test]
    public void SetTile_OutOfBounds_DoesNotThrow()
    {
        var map = new GameMap(10, 10);
        Assert.DoesNotThrow(() => map.SetTile(-1, 0, TileKind.Floor));
        Assert.DoesNotThrow(() => map.SetTile(0, 10, TileKind.Floor));
    }
}
