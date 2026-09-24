using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

[TestFixture]
public class DropItemTests
{
    /// <summary>
    /// Minimal state: player only, no monsters, open arena map.
    /// Player starts at (3,3) with a potion in inventory.
    /// </summary>
    private static (GameState state, Entity potion) CreateStateWithPotion(int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 3, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var inventory = player.Add(new Inventory());

        var potion = new Entity(99, "Healing Potion");
        potion.Add(new Consumable(healAmount: 40));
        inventory.Add(potion);

        var state = new GameState(player, new List<Entity>(), map, rng);
        return (state, potion);
    }

    [Test]
    public void DropItem_RemovesFromInventory()
    {
        var (state, potion) = CreateStateWithPotion();
        var inventory = state.Player.Require<Inventory>();
        Assert.That(inventory.Count, Is.EqualTo(1), "Precondition: potion is in inventory");

        TurnController.ProcessTurn(state, PlayerAction.Drop(potion));

        Assert.That(inventory.Count, Is.EqualTo(0), "Inventory should be empty after drop");
        Assert.That(inventory.FindFirst(i => i.Id == potion.Id), Is.Null, "Potion should not be found in inventory");
    }

    [Test]
    public void DropItem_AppearsOnFloorAtPlayerPosition()
    {
        var (state, potion) = CreateStateWithPotion();
        int playerX = state.Player.X;
        int playerY = state.Player.Y;

        TurnController.ProcessTurn(state, PlayerAction.Drop(potion));

        Assert.That(state.FloorItems, Has.Count.EqualTo(1), "One item should be on the floor");
        var floorItem = state.FloorItems[0];
        Assert.That(floorItem.Id, Is.EqualTo(potion.Id), "The dropped item should be on the floor");
        Assert.That(floorItem.X, Is.EqualTo(playerX), "Item X should match player X at time of drop");
        Assert.That(floorItem.Y, Is.EqualTo(playerY), "Item Y should match player Y at time of drop");
    }

    [Test]
    public void DropItem_EmitsDropEvent()
    {
        var (state, potion) = CreateStateWithPotion();

        var result = TurnController.ProcessTurn(state, PlayerAction.Drop(potion));

        var dropEvents = result.Events.OfType<DropEvent>().ToList();
        Assert.That(dropEvents, Has.Count.EqualTo(1), "Should emit exactly one DropEvent");
        Assert.That(dropEvents[0].ActorId, Is.EqualTo(state.Player.Id), "ActorId is the player");
        Assert.That(dropEvents[0].ItemId, Is.EqualTo(potion.Id), "ItemId matches the dropped item");
        Assert.That(dropEvents[0].ItemName, Is.EqualTo(potion.Name), "ItemName matches the dropped item");
    }

    [Test]
    public void DropItem_ItemRegisteredInMap()
    {
        var (state, potion) = CreateStateWithPotion();

        TurnController.ProcessTurn(state, PlayerAction.Drop(potion));

        // Map.Entities should include the dropped item (it was unregistered during pickup,
        // and must be re-registered on drop so the presentation layer can render it)
        var allEntities = state.Map.Entities.ToList();
        Assert.That(allEntities.Any(e => e.Id == potion.Id), Is.True,
            "Dropped item must be registered in the map so it can be rendered on the floor");
    }

    [Test]
    public void DropItem_CanBePickedUpAgainByWalkingOver()
    {
        // Player at (3,3), potion dropped there. Player walks to (4,3) then back to (3,3).
        var (state, potion) = CreateStateWithPotion();
        var player = state.Player;

        // Drop the potion at (3,3)
        TurnController.ProcessTurn(state, PlayerAction.Drop(potion));
        Assert.That(state.FloorItems, Has.Count.EqualTo(1), "Precondition: potion on floor");
        Assert.That(player.Get<Inventory>()?.Count, Is.EqualTo(0), "Precondition: inventory empty");

        // Walk away — player moves to (4,3)
        TurnController.ProcessTurn(state, PlayerAction.MoveTo(4, 3));
        Assert.That(player.X, Is.EqualTo(4), "Player should have moved to x=4");

        // Walk back over the potion — triggers auto-pickup
        TurnController.ProcessTurn(state, PlayerAction.MoveTo(3, 3));
        Assert.That(player.X, Is.EqualTo(3), "Player should have moved back to x=3");

        // Potion should be back in inventory
        var inventory = player.Get<Inventory>();
        Assert.That(inventory, Is.Not.Null, "Player should have an inventory");
        Assert.That(inventory!.Count, Is.EqualTo(1), "Potion should be back in inventory after walking over it");
        Assert.That(state.FloorItems, Is.Empty, "FloorItems should be empty after pickup");
    }
}
