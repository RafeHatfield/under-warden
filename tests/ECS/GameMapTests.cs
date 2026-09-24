using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.ECS;

[TestFixture]
public class GameMapTests
{
    [Test]
    public void CreateArena_WallsOnEdges()
    {
        var map = GameMap.CreateArena(10, 10);

        // Edges are walls
        Assert.That(map.IsWalkable(0, 0), Is.False);
        Assert.That(map.IsWalkable(9, 9), Is.False);
        Assert.That(map.IsWalkable(5, 0), Is.False);
        Assert.That(map.IsWalkable(0, 5), Is.False);

        // Interior is walkable
        Assert.That(map.IsWalkable(1, 1), Is.True);
        Assert.That(map.IsWalkable(5, 5), Is.True);
    }

    [Test]
    public void IsBlocked_ByEntity()
    {
        var map = GameMap.CreateArena(10, 10);
        var entity = new Entity(1, "Orc", 5, 5, blocksMovement: true);
        map.RegisterEntity(entity);

        Assert.That(map.IsBlocked(5, 5), Is.True);
        Assert.That(map.IsBlocked(5, 4), Is.False);
    }

    [Test]
    public void MoveToward_DirectPath()
    {
        var map = GameMap.CreateArena(10, 10);
        var mover = new Entity(1, "Player", 2, 5);
        map.RegisterEntity(mover);

        bool moved = map.MoveToward(mover, 8, 5);

        Assert.That(moved, Is.True);
        Assert.That(mover.X, Is.EqualTo(3));
        Assert.That(mover.Y, Is.EqualTo(5));
    }

    [Test]
    public void MoveToward_Diagonal()
    {
        var map = GameMap.CreateArena(10, 10);
        var mover = new Entity(1, "Player", 3, 3);

        bool moved = map.MoveToward(mover, 6, 6);

        Assert.That(moved, Is.True);
        Assert.That(mover.X, Is.EqualTo(4));
        Assert.That(mover.Y, Is.EqualTo(4));
    }

    [Test]
    public void MoveToward_BlockedByEntity_TriesAlternative()
    {
        var map = GameMap.CreateArena(10, 10);
        var blocker = new Entity(1, "Orc", 4, 5, blocksMovement: true);
        var mover = new Entity(2, "Player", 3, 5);
        map.RegisterEntity(blocker);
        map.RegisterEntity(mover);

        bool moved = map.MoveToward(mover, 5, 5);

        // Can't go right (blocked), should try vertical
        Assert.That(moved, Is.False); // straight line, no vertical component
    }

    [Test]
    public void MoveToward_AlreadyAtTarget()
    {
        var map = GameMap.CreateArena(10, 10);
        var mover = new Entity(1, "Player", 5, 5);

        bool moved = map.MoveToward(mover, 5, 5);

        Assert.That(moved, Is.False);
        Assert.That(mover.X, Is.EqualTo(5));
    }

    // --- Prop cell tests ---

    [Test]
    public void IsWalkable_ReturnsFalse_WhenPropCellMarked()
    {
        var map = GameMap.CreateArena(10, 10);
        // (5, 5) is interior — walkable before marking
        Assert.That(map.IsWalkable(5, 5), Is.True, "precondition: cell should be walkable before prop placement");

        map.MarkPropCell(5, 5);

        Assert.That(map.IsWalkable(5, 5), Is.False);
    }

    [Test]
    public void IsWallTile_ReturnsFalse_ForPropCell()
    {
        var map = GameMap.CreateArena(10, 10);
        // Mark an interior floor tile as a prop — it is NOT a wall tile
        map.MarkPropCell(5, 5);

        // IsWallTile checks the tile array only, not prop cells
        Assert.That(map.IsWallTile(5, 5), Is.False);
        // Confirm actual wall tiles still return true
        Assert.That(map.IsWallTile(0, 0), Is.True);
    }

    [Test]
    public void IsPropCell_ReturnsTrue_AfterMarking()
    {
        var map = GameMap.CreateArena(10, 10);
        Assert.That(map.IsPropCell(3, 4), Is.False, "precondition: not yet a prop cell");

        map.MarkPropCell(3, 4);

        Assert.That(map.IsPropCell(3, 4), Is.True);
        // Neighbouring cell unaffected
        Assert.That(map.IsPropCell(3, 5), Is.False);
    }
}
