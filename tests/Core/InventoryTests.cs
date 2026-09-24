using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

[TestFixture]
public class InventoryTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Entity MakeConsumable(int id, string name, int healAmount = 40, int stackSize = 1)
    {
        var e = new Entity(id, name);
        e.Add(new Consumable(healAmount) { StackSize = stackSize });
        return e;
    }

    private static Entity MakeEquippable(int id, string name)
    {
        // Non-consumable item — use Equippable as a stand-in for any equippable.
        // Inventory stacking only applies to Consumable components, so any entity
        // without Consumable will never stack regardless of name.
        var e = new Entity(id, name);
        e.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 1, DamageMax = 4 });
        return e;
    }

    /// <summary>
    /// Minimal GameState: player only (no monsters), arena map.
    /// </summary>
    private static GameState CreateMinimalState()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(8, 8);
        var player = new Entity(0, "Player", 3, 3, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);
        return new GameState(player, new List<Entity>(), map, rng);
    }

    // ─── Inventory.Add — basic behaviour ────────────────────────────────────

    [Test]
    public void Add_ItemToEmptyInventory_ReturnsTrue()
    {
        var inv = new Inventory();
        var potion = MakeConsumable(1, "Healing Potion");

        bool result = inv.Add(potion);

        Assert.That(result, Is.True);
        Assert.That(inv.Count, Is.EqualTo(1));
    }

    [Test]
    public void Add_BeyondCapacity_ReturnsFalse_ItemNotAdded()
    {
        var inv = new Inventory();

        // Fill to capacity with non-stackable equippables (unique names → no stacking)
        for (int i = 0; i < Inventory.Capacity; i++)
            inv.Add(MakeEquippable(i, $"Dagger #{i}"));

        Assert.That(inv.IsFull, Is.True, "Precondition: inventory is full");

        var overflow = MakeEquippable(999, "Extra Dagger");
        bool result = inv.Add(overflow);

        Assert.That(result, Is.False);
        Assert.That(inv.Count, Is.EqualTo(Inventory.Capacity));
    }

    [Test]
    public void Add_SameNameConsumable_StacksInsteadOfAdding()
    {
        var inv = new Inventory();
        var potion1 = MakeConsumable(1, "Healing Potion");
        var potion2 = MakeConsumable(2, "Healing Potion");

        inv.Add(potion1);
        bool result = inv.Add(potion2);

        Assert.That(result, Is.True, "Stacking should succeed");
        Assert.That(inv.Count, Is.EqualTo(1), "Only one slot used");
        Assert.That(inv.Items[0].Require<Consumable>().StackSize, Is.EqualTo(2),
            "StackSize should be 2 after adding a second potion");
    }

    [Test]
    public void Add_DifferentConsumables_DoNotStack()
    {
        var inv = new Inventory();
        var healPotion = MakeConsumable(1, "Healing Potion");
        var manaPotion = MakeConsumable(2, "Mana Potion", healAmount: 0);

        inv.Add(healPotion);
        inv.Add(manaPotion);

        Assert.That(inv.Count, Is.EqualTo(2), "Different consumables occupy separate slots");
    }

    [Test]
    public void Add_EquippableItems_DoNotStack()
    {
        var inv = new Inventory();
        var dagger1 = MakeEquippable(1, "Dagger");
        var dagger2 = MakeEquippable(2, "Dagger");

        inv.Add(dagger1);
        inv.Add(dagger2);

        Assert.That(inv.Count, Is.EqualTo(2),
            "Equippables with the same name do not stack — each occupies its own slot");
    }

    // ─── IsFull ─────────────────────────────────────────────────────────────

    [Test]
    public void IsFull_WhenAtCapacity_ReturnsTrue()
    {
        var inv = new Inventory();
        for (int i = 0; i < Inventory.Capacity; i++)
            inv.Add(MakeEquippable(i, $"Item #{i}"));

        Assert.That(inv.IsFull, Is.True);
    }

    [Test]
    public void IsFull_BelowCapacity_ReturnsFalse()
    {
        var inv = new Inventory();
        inv.Add(MakeConsumable(1, "Healing Potion"));

        Assert.That(inv.IsFull, Is.False);
    }

    // ─── TryHeal — stack-aware consumption ──────────────────────────────────

    [Test]
    public void TryHeal_StackedPotion_DecrementsStackSize()
    {
        // Stacked potion with 3 charges — after healing, StackSize should be 2, still in inventory.
        var state = CreateMinimalState();
        var inventory = state.Player.Add(new Inventory());

        var potion = MakeConsumable(99, "Healing Potion", healAmount: 40, stackSize: 3);
        inventory.Add(potion);

        // Damage player so Heal actually heals something
        state.PlayerFighter.TakeDamage(20);

        TurnController.ProcessTurn(state, PlayerAction.UseItem());

        Assert.That(inventory.Count, Is.EqualTo(1), "Potion slot should still exist");
        Assert.That(inventory.Items[0].Require<Consumable>().StackSize, Is.EqualTo(2),
            "StackSize should have decremented from 3 to 2");
    }

    [Test]
    public void TryHeal_LastPotion_RemovesFromInventory()
    {
        // StackSize = 1 — after healing the stack is exhausted and the entity is removed.
        var state = CreateMinimalState();
        var inventory = state.Player.Add(new Inventory());

        var potion = MakeConsumable(99, "Healing Potion", healAmount: 40, stackSize: 1);
        inventory.Add(potion);

        state.PlayerFighter.TakeDamage(20);

        TurnController.ProcessTurn(state, PlayerAction.UseItem());

        Assert.That(inventory.Count, Is.EqualTo(0), "Potion should be removed when last charge is used");
    }
}
