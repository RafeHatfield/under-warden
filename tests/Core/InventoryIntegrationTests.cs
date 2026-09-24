using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Integration tests for the inventory/item flow: drop, pickup, use, stacking.
/// These exercise the full input → TurnController → GameState chain with no Godot dependencies.
/// Corresponds to Task 8 of the HUD/Inventory milestone.
/// </summary>
[TestFixture]
public class InventoryIntegrationTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal state: player only, no monsters, open arena.
    /// Player starts at (3,3). Returned inventory is already attached to the player.
    /// </summary>
    private static (GameState state, Inventory inventory) CreatePlayerOnlyState(int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(12, 12);

        var player = new Entity(0, "Player", 3, 3, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var inventory = player.Add(new Inventory());
        var state = new GameState(player, new List<Entity>(), map, rng);
        return (state, inventory);
    }

    private static Entity MakePotion(int id, int healAmount = 40, int stackSize = 1)
    {
        var e = new Entity(id, "Healing Potion");
        e.Add(new Consumable(healAmount) { StackSize = stackSize });
        return e;
    }

    private static Entity MakeDagger(int id, string nameSuffix = "")
    {
        var e = new Entity(id, $"Dagger{nameSuffix}");
        e.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 1, DamageMax = 4 });
        return e;
    }

    // ─── Test 1: UseItem_Potion_ConsumesAndHeals ─────────────────────────────

    [Test]
    public void UseItem_Potion_ConsumesAndHeals()
    {
        var (state, inventory) = CreatePlayerOnlyState();

        var potion = MakePotion(id: 99);
        inventory.Add(potion);

        // Damage the player so there is room to heal
        state.PlayerFighter.TakeDamage(30);
        int hpBefore = state.PlayerFighter.Hp;

        Assert.That(inventory.Count, Is.EqualTo(1), "Precondition: potion in inventory");

        var result = TurnController.ProcessTurn(state, PlayerAction.UseItem(potion));

        // Potion is consumed (StackSize was 1 → item removed)
        Assert.That(inventory.Count, Is.EqualTo(0), "Potion should be removed after use");

        // HP increased
        Assert.That(state.PlayerFighter.Hp, Is.GreaterThan(hpBefore), "Player should have been healed");

        // HealEvent was emitted with the correct item id
        var healEvents = result.Events.OfType<HealEvent>().ToList();
        Assert.That(healEvents, Has.Count.EqualTo(1), "Should emit exactly one HealEvent");
        Assert.That(healEvents[0].ItemId, Is.EqualTo(99), "HealEvent.ItemId should match the potion");
        Assert.That(healEvents[0].AmountHealed, Is.GreaterThan(0), "Healed amount should be positive");
    }

    // ─── Test 2: DropItem_ThenWalkOver_PicksUpAgain ──────────────────────────

    [Test]
    public void DropItem_ThenWalkOver_PicksUpAgain()
    {
        // Player at (3,3). Drop the potion there, walk to (4,3), walk back to (3,3) → auto-pickup.
        var (state, inventory) = CreatePlayerOnlyState();
        var potion = MakePotion(id: 77);
        inventory.Add(potion);

        // Drop at (3,3)
        TurnController.ProcessTurn(state, PlayerAction.Drop(potion));

        Assert.That(inventory.Count, Is.EqualTo(0), "Precondition: inventory empty after drop");
        Assert.That(state.FloorItems, Has.Count.EqualTo(1), "Precondition: potion on floor");

        // Walk away to (4,3)
        TurnController.ProcessTurn(state, PlayerAction.MoveTo(4, 3));
        Assert.That(state.Player.X, Is.EqualTo(4), "Player should be at x=4");

        // Walk back onto the dropped item at (3,3) → auto-pickup
        TurnController.ProcessTurn(state, PlayerAction.MoveTo(3, 3));
        Assert.That(state.Player.X, Is.EqualTo(3), "Player should be back at x=3");

        Assert.That(inventory.Count, Is.EqualTo(1), "Potion should be back in inventory");
        Assert.That(state.FloorItems, Is.Empty, "Floor should be empty after pickup");
    }

    // ─── Test 3: InventoryFull_PickupIgnored ─────────────────────────────────

    [Test]
    public void InventoryFull_PickupIgnored()
    {
        // Fill inventory to capacity with 25 uniquely-named equippables (non-stackable).
        // Place a potion on the floor at (4,3) — the tile adjacent to the player.
        // When the player walks there the pickup attempt should fail and the item stays on the floor.
        var (state, inventory) = CreatePlayerOnlyState();

        for (int i = 0; i < Inventory.Capacity; i++)
            inventory.Add(MakeDagger(id: i + 1, nameSuffix: $" #{i + 1}"));

        Assert.That(inventory.IsFull, Is.True, "Precondition: inventory is full");
        Assert.That(inventory.Count, Is.EqualTo(Inventory.Capacity));

        // Place a potion on the floor at (4,3) — the tile the player will step onto
        var floorPotion = MakePotion(id: 999);
        floorPotion.X = 4;
        floorPotion.Y = 3;
        state.FloorItems.Add(floorPotion);
        state.Map.RegisterEntity(floorPotion);

        // Player walks onto (4,3)
        TurnController.ProcessTurn(state, PlayerAction.MoveTo(4, 3));
        Assert.That(state.Player.X, Is.EqualTo(4), "Player should have moved to x=4");

        // Inventory still full — potion stays on floor
        Assert.That(inventory.Count, Is.EqualTo(Inventory.Capacity), "Inventory count should be unchanged");
        Assert.That(state.FloorItems, Has.Count.EqualTo(1), "Potion should still be on the floor");
        Assert.That(state.FloorItems[0].Id, Is.EqualTo(999), "The same potion should remain on the floor");
    }

    // ─── Test 4: StackedPotions_UseReducesCount ───────────────────────────────

    [Test]
    public void StackedPotions_UseReducesCount()
    {
        // Player has a stacked potion with StackSize=2.
        // Using it should decrement StackSize to 1 — the item slot remains in inventory.
        var (state, inventory) = CreatePlayerOnlyState();

        var stackedPotion = MakePotion(id: 55, stackSize: 2);
        inventory.Add(stackedPotion);

        // Damage player so heal has an effect
        state.PlayerFighter.TakeDamage(30);

        Assert.That(inventory.Count, Is.EqualTo(1), "Precondition: one slot in inventory");
        Assert.That(inventory.Items[0].Require<Consumable>().StackSize, Is.EqualTo(2),
            "Precondition: StackSize is 2");

        TurnController.ProcessTurn(state, PlayerAction.UseItem(stackedPotion));

        // Item slot is still present — stack not exhausted
        Assert.That(inventory.Count, Is.EqualTo(1), "Inventory slot should still exist (stack not empty)");
        Assert.That(inventory.Items[0].Require<Consumable>().StackSize, Is.EqualTo(1),
            "StackSize should have decremented from 2 to 1");

        // Sanity: player was healed
        Assert.That(state.PlayerFighter.Hp, Is.GreaterThan(state.PlayerFighter.MaxHp - 30),
            "Player should have been healed");
    }
}
