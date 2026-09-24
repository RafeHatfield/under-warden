using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

[TestFixture]
public class EquipItemTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Entity MakeWeapon(int id, string name, EquipmentSlot slot = EquipmentSlot.MainHand)
    {
        var e = new Entity(id, name);
        e.Add(new Equippable(slot) { DamageMin = 1, DamageMax = 6 });
        return e;
    }

    private static Entity MakeArmor(int id, string name, EquipmentSlot slot = EquipmentSlot.Chest)
    {
        var e = new Entity(id, name);
        e.Add(new Equippable(slot) { ArmorClassBonus = 2 });
        return e;
    }

    private static GameState CreateStateWithPlayer(int playerHp = 54)
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(8, 8);
        var player = new Entity(0, "Player", 3, 3, blocksMovement: true);
        player.Add(new Fighter(hp: playerHp, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);
        return new GameState(player, new List<Entity>(), map, rng);
    }

    // ─── Equip — happy path ─────────────────────────────────────────────────

    [Test]
    public void Equip_WeaponFromInventory_PlacesInMainHandSlot()
    {
        var state = CreateStateWithPlayer();
        var sword = MakeWeapon(1, "Sword");
        state.Player.GetOrAdd<Inventory>().Add(sword);

        TurnController.ProcessTurn(state, PlayerAction.Equip(sword));

        var equipment = state.Player.Get<Equipment>();
        Assert.That(equipment?.MainHand, Is.SameAs(sword));
    }

    [Test]
    public void Equip_WeaponFromInventory_RemovesFromInventory()
    {
        var state = CreateStateWithPlayer();
        var sword = MakeWeapon(1, "Sword");
        state.Player.GetOrAdd<Inventory>().Add(sword);

        TurnController.ProcessTurn(state, PlayerAction.Equip(sword));

        Assert.That(state.PlayerInventory?.Count, Is.EqualTo(0));
    }

    [Test]
    public void Equip_EmitsEquipEvent()
    {
        var state = CreateStateWithPlayer();
        var sword = MakeWeapon(1, "Sword");
        state.Player.GetOrAdd<Inventory>().Add(sword);

        var result = TurnController.ProcessTurn(state, PlayerAction.Equip(sword));

        Assert.That(result.Events.OfType<EquipEvent>().Count(), Is.EqualTo(1));
        var evt = result.Events.OfType<EquipEvent>().First();
        Assert.That(evt.ItemId,   Is.EqualTo(sword.Id));
        Assert.That(evt.ItemName, Is.EqualTo(sword.Name));
        Assert.That(evt.Slot,     Is.EqualTo(EquipmentSlot.MainHand));
    }

    [Test]
    public void Equip_ArmorIntoChestSlot_PlacesCorrectly()
    {
        var state = CreateStateWithPlayer();
        var armor = MakeArmor(2, "Leather Armor");
        state.Player.GetOrAdd<Inventory>().Add(armor);

        TurnController.ProcessTurn(state, PlayerAction.Equip(armor));

        var equipment = state.Player.Get<Equipment>();
        Assert.That(equipment?.Chest, Is.SameAs(armor));
    }

    // ─── Equip — displacing existing item ──────────────────────────────────

    [Test]
    public void Equip_OccupiedSlot_DisplacedItemReturnedToInventory()
    {
        var state = CreateStateWithPlayer();
        var dagger = MakeWeapon(1, "Dagger");
        var sword  = MakeWeapon(2, "Sword");

        // Start with dagger equipped.
        state.Player.GetOrAdd<Equipment>().SetSlot(EquipmentSlot.MainHand, dagger);
        // Sword is in inventory.
        state.Player.GetOrAdd<Inventory>().Add(sword);

        TurnController.ProcessTurn(state, PlayerAction.Equip(sword));

        var equipment = state.Player.Get<Equipment>();
        var inventory = state.PlayerInventory;
        Assert.That(equipment?.MainHand, Is.SameAs(sword));
        Assert.That(inventory?.Items, Has.Member(dagger));
    }

    [Test]
    public void Equip_OccupiedSlot_EquipEventCarriesDisplacedInfo()
    {
        var state = CreateStateWithPlayer();
        var dagger = MakeWeapon(1, "Dagger");
        var sword  = MakeWeapon(2, "Sword");
        state.Player.GetOrAdd<Equipment>().SetSlot(EquipmentSlot.MainHand, dagger);
        state.Player.GetOrAdd<Inventory>().Add(sword);

        var result = TurnController.ProcessTurn(state, PlayerAction.Equip(sword));

        var evt = result.Events.OfType<EquipEvent>().First();
        Assert.That(evt.DisplacedItemId,   Is.EqualTo(dagger.Id));
        Assert.That(evt.DisplacedItemName, Is.EqualTo(dagger.Name));
    }

    // ─── Equip — guard conditions ───────────────────────────────────────────

    [Test]
    public void Equip_ItemNotInInventory_NoEquipEvent()
    {
        var state = CreateStateWithPlayer();
        var sword = MakeWeapon(1, "Sword");
        // Sword is NOT added to inventory.

        var result = TurnController.ProcessTurn(state, PlayerAction.Equip(sword));

        Assert.That(result.Events.OfType<EquipEvent>(), Is.Empty);
        Assert.That(state.Player.Get<Equipment>()?.MainHand, Is.Null);
    }

    [Test]
    public void Equip_ItemWithNoEquippableComponent_NoEquipEvent()
    {
        var state = CreateStateWithPlayer();
        // A plain entity with no Equippable component.
        var weirdItem = new Entity(99, "Weird Item");
        state.Player.GetOrAdd<Inventory>().Add(weirdItem);

        var result = TurnController.ProcessTurn(state, PlayerAction.Equip(weirdItem));

        Assert.That(result.Events.OfType<EquipEvent>(), Is.Empty);
    }

    // ─── Unequip — happy path ───────────────────────────────────────────────

    [Test]
    public void Unequip_OccupiedSlot_ClearsSlot()
    {
        var state = CreateStateWithPlayer();
        var sword = MakeWeapon(1, "Sword");
        state.Player.GetOrAdd<Equipment>().SetSlot(EquipmentSlot.MainHand, sword);

        TurnController.ProcessTurn(state, PlayerAction.Unequip(EquipmentSlot.MainHand));

        Assert.That(state.Player.Get<Equipment>()?.MainHand, Is.Null);
    }

    [Test]
    public void Unequip_OccupiedSlot_ItemAddedToInventory()
    {
        var state = CreateStateWithPlayer();
        var sword = MakeWeapon(1, "Sword");
        state.Player.GetOrAdd<Equipment>().SetSlot(EquipmentSlot.MainHand, sword);

        TurnController.ProcessTurn(state, PlayerAction.Unequip(EquipmentSlot.MainHand));

        Assert.That(state.PlayerInventory?.Items, Has.Member(sword));
    }

    [Test]
    public void Unequip_EmitsUnequipEvent()
    {
        var state = CreateStateWithPlayer();
        var sword = MakeWeapon(1, "Sword");
        state.Player.GetOrAdd<Equipment>().SetSlot(EquipmentSlot.MainHand, sword);

        var result = TurnController.ProcessTurn(state, PlayerAction.Unequip(EquipmentSlot.MainHand));

        Assert.That(result.Events.OfType<UnequipEvent>().Count(), Is.EqualTo(1));
        var evt = result.Events.OfType<UnequipEvent>().First();
        Assert.That(evt.ItemId,   Is.EqualTo(sword.Id));
        Assert.That(evt.Slot,     Is.EqualTo(EquipmentSlot.MainHand));
    }

    // ─── Unequip — guard conditions ─────────────────────────────────────────

    [Test]
    public void Unequip_EmptySlot_NoUnequipEvent()
    {
        var state = CreateStateWithPlayer();
        // MainHand is empty.

        var result = TurnController.ProcessTurn(state, PlayerAction.Unequip(EquipmentSlot.MainHand));

        Assert.That(result.Events.OfType<UnequipEvent>(), Is.Empty);
    }

    [Test]
    public void Unequip_InventoryFull_BlockedNoUnequipEvent()
    {
        var state = CreateStateWithPlayer();
        var sword = MakeWeapon(1, "Sword");
        state.Player.GetOrAdd<Equipment>().SetSlot(EquipmentSlot.MainHand, sword);

        // Fill inventory to capacity with distinct non-stackable items.
        var inv = state.Player.GetOrAdd<Inventory>();
        for (int i = 2; i < Inventory.Capacity + 2; i++)
            inv.Add(MakeWeapon(i, $"Sword {i}", EquipmentSlot.MainHand));

        Assert.That(inv.IsFull, Is.True);

        var result = TurnController.ProcessTurn(state, PlayerAction.Unequip(EquipmentSlot.MainHand));

        Assert.That(result.Events.OfType<UnequipEvent>(), Is.Empty);
        Assert.That(state.Player.Get<Equipment>()?.MainHand, Is.SameAs(sword));
    }

    // ─── New slots (LeftRing, RightRing, Neck) ─────────────────────────────

    [Test]
    public void Equip_ItemIntoLeftRingSlot_Works()
    {
        var state = CreateStateWithPlayer();
        var ring = new Entity(10, "Ring of Protection");
        ring.Add(new Equippable(EquipmentSlot.LeftRing) { ArmorClassBonus = 1 });
        state.Player.GetOrAdd<Inventory>().Add(ring);

        TurnController.ProcessTurn(state, PlayerAction.Equip(ring));

        Assert.That(state.Player.Get<Equipment>()?.LeftRing, Is.SameAs(ring));
    }

    [Test]
    public void Equip_ItemIntoNeckSlot_Works()
    {
        var state = CreateStateWithPlayer();
        var amulet = new Entity(11, "Amulet of Health");
        amulet.Add(new Equippable(EquipmentSlot.Neck) { ArmorClassBonus = 1 });
        state.Player.GetOrAdd<Inventory>().Add(amulet);

        TurnController.ProcessTurn(state, PlayerAction.Equip(amulet));

        Assert.That(state.Player.Get<Equipment>()?.Neck, Is.SameAs(amulet));
    }

    [Test]
    public void Unequip_RightRingSlot_ItemReturnsToInventory()
    {
        var state = CreateStateWithPlayer();
        var ring = new Entity(12, "Ring of Might");
        ring.Add(new Equippable(EquipmentSlot.RightRing) { ArmorClassBonus = 1 });
        state.Player.GetOrAdd<Equipment>().SetSlot(EquipmentSlot.RightRing, ring);

        TurnController.ProcessTurn(state, PlayerAction.Unequip(EquipmentSlot.RightRing));

        Assert.That(state.Player.Get<Equipment>()?.RightRing, Is.Null);
        Assert.That(state.PlayerInventory?.Items, Has.Member(ring));
    }
}
