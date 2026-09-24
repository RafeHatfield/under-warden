using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for monster item-seeking, pickup, auto-equip, and drop-on-death.
/// All tests use a scenario-mode map (IsDungeonMode=false, RevealAll).
/// </summary>
[TestFixture]
public class MonsterItemSeekingTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Arena map 20x20, all walkable inside.
    /// Player at (1,1). Monster at (monsterX, monsterY).
    /// Player has very high damage (damageMin=damageMax=999) so death-tests are reliable.
    /// </summary>
    private static (GameState state, Entity monster) CreateState(
        int monsterX = 10, int monsterY = 10,
        bool monsterCanSeek = true, int inventorySize = 5,
        int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(20, 20);

        // High damage to guarantee kills in death-drop tests without needing a power helper.
        var player = new Entity(0, "Player", 1, 1, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 18, dexterity: 14, constitution: 12,
            accuracy: 99, evasion: 1, damageMin: 999, damageMax: 999));
        map.RegisterEntity(player);

        var monster = new Entity(1, "Goblin", monsterX, monsterY, blocksMovement: true);
        monster.Add(new Fighter(hp: 30, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 4, evasion: 1, damageMin: 2, damageMax: 4));
        monster.Add(new AiComponent { CanSeekItems = monsterCanSeek, SeekDistance = 8, InventorySize = inventorySize });
        monster.Add(new Inventory());
        monster.Add(new Equipment());
        map.RegisterEntity(monster);

        var state = new GameState(player, new List<Entity> { monster }, map, rng);
        return (state, monster);
    }

    /// <summary>Create a weapon item entity with an Equippable component.</summary>
    private static Entity MakeWeapon(int id, string name, int x, int y, GameState state)
    {
        var weapon = new Entity(id, name, x, y);
        weapon.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 3, DamageMax = 6 });
        state.FloorItems.Add(weapon);
        state.Map.RegisterEntity(weapon);
        return weapon;
    }

    /// <summary>Create a healing potion entity.</summary>
    private static Entity MakePotion(int id, string name, int x, int y, GameState state)
    {
        var potion = new Entity(id, name, x, y);
        potion.Add(new Consumable(healAmount: 20));
        state.FloorItems.Add(potion);
        state.Map.RegisterEntity(potion);
        return potion;
    }

    // ─── Item seeking ─────────────────────────────────────────────────────────

    [Test]
    public void Monster_SeeksItem_WhenItemIsCloserThanPlayer()
    {
        // Monster at (10,10), player at (1,1) — far away.
        // Item at (10,8) — 2 tiles above monster, much closer than player.
        var (state, monster) = CreateState(monsterX: 10, monsterY: 10);
        MakeWeapon(50, "Sword", 10, 8, state);

        int startX = monster.X, startY = monster.Y;

        // Wait — player does nothing, monster should move toward item
        TurnController.ProcessTurn(state, PlayerAction.Wait);

        // Monster should have moved toward the item (upward, y-1)
        Assert.That(monster.Y, Is.LessThan(startY),
            "Monster should move toward item that is closer than the player");
    }

    [Test]
    public void Monster_DoesNotSeekItem_WhenPlayerIsCloser()
    {
        // Player adjacent to monster at (10,10) → player at (9,10).
        // Item at (10,5) — 5 tiles away, farther than player (1 tile).
        var (state, monster) = CreateState(monsterX: 10, monsterY: 10);

        // Move player next to monster so player is closer than the item
        state.Player.X = 9;
        state.Player.Y = 10;
        MakeWeapon(50, "Sword", 10, 5, state);

        // Because player is adjacent, monster should attack instead of seeking item
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var monsterAttacks = result.Events.OfType<AttackEvent>().Where(e => e.ActorId == 1).ToList();
        Assert.That(monsterAttacks, Is.Not.Empty,
            "Monster should attack player when player is adjacent, not seek item");
    }

    [Test]
    public void Monster_DoesNotSeekItem_WhenCanSeekItemsFalse()
    {
        // Monster at (10,10) without CanSeekItems. Player at (1,1) — far away.
        // Item placed on the same tile as the monster (10,10) to verify that even
        // standing on the item, the monster without CanSeekItems won't pick it up.
        // In scenario mode the monster is always alerted and pursues the player.
        var (state, monster) = CreateState(monsterX: 10, monsterY: 10, monsterCanSeek: false);
        var weapon = new Entity(50, "Sword", 10, 10);
        weapon.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 3, DamageMax = 6 });
        state.FloorItems.Add(weapon);
        state.Map.RegisterEntity(weapon);

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        // Monster without CanSeekItems must NOT have picked up the weapon
        Assert.That(state.FloorItems, Has.Count.EqualTo(1),
            "Weapon should remain on floor — monster without CanSeekItems ignores floor items");
        var equipment = monster.Require<Equipment>();
        Assert.That(equipment.MainHand, Is.Null,
            "Monster without CanSeekItems should not auto-equip a weapon from the floor");
    }

    [Test]
    public void Monster_EmitsSeekItemMoveEvent_WhenApproachingItem()
    {
        var (state, monster) = CreateState(monsterX: 10, monsterY: 10);
        MakeWeapon(50, "Sword", 10, 8, state);

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        // Should emit a MoveEvent for the monster (SeekItem resolves as MoveTo in TurnController)
        var moveEvents = result.Events.OfType<MoveEvent>().Where(e => e.ActorId == 1).ToList();
        Assert.That(moveEvents, Is.Not.Empty,
            "Monster seeking an item should emit a MoveEvent");
    }

    // ─── Pickup ──────────────────────────────────────────────────────────────

    [Test]
    public void Monster_PicksUpWeapon_WhenOnSameTile()
    {
        // Monster and item share the same tile → should immediately PickUp
        var (state, monster) = CreateState(monsterX: 10, monsterY: 10);
        MakeWeapon(50, "Short Sword", 10, 10, state);

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var pickUpEvents = result.Events.OfType<PickUpEvent>().Where(e => e.ActorId == 1).ToList();
        Assert.That(pickUpEvents, Has.Count.EqualTo(1), "Monster should emit one PickUpEvent");
        Assert.That(pickUpEvents[0].ItemId, Is.EqualTo(50));

        Assert.That(state.FloorItems, Is.Empty, "Weapon should be removed from floor");
    }

    [Test]
    public void Monster_AutoEquipsWeapon_IntoEmptyMainHand()
    {
        var (state, monster) = CreateState(monsterX: 10, monsterY: 10);
        MakeWeapon(50, "Short Sword", 10, 10, state);

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        var equipment = monster.Require<Equipment>();
        Assert.That(equipment.MainHand, Is.Not.Null, "Weapon should be auto-equipped in MainHand");
        Assert.That(equipment.MainHand!.Id, Is.EqualTo(50));
        Assert.That(monster.Get<Inventory>()?.Count, Is.EqualTo(0),
            "Weapon should be in equipment slot, not inventory");
    }

    [Test]
    public void Monster_AddsConsumableToInventory_WhenPickedUp()
    {
        var (state, monster) = CreateState(monsterX: 10, monsterY: 10);
        MakePotion(50, "Healing Potion", 10, 10, state);

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        var inventory = monster.Require<Inventory>();
        Assert.That(inventory.Count, Is.EqualTo(1), "Potion should be in inventory");
        Assert.That(inventory.Items[0].Id, Is.EqualTo(50));
    }

    [Test]
    public void Monster_AddsWeaponToInventory_WhenMainHandOccupied()
    {
        var (state, monster) = CreateState(monsterX: 10, monsterY: 10);

        // Pre-equip a weapon
        var existingWeapon = new Entity(99, "Existing Sword", 0, 0);
        existingWeapon.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 2, DamageMax = 4 });
        monster.Require<Equipment>().SetSlot(EquipmentSlot.MainHand, existingWeapon);

        // Drop a second weapon on monster's tile
        MakeWeapon(50, "Better Sword", 10, 10, state);

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        var inventory = monster.Require<Inventory>();
        Assert.That(inventory.Count, Is.EqualTo(1),
            "Second weapon should go to inventory when MainHand is occupied");
        Assert.That(inventory.Items[0].Id, Is.EqualTo(50));
    }

    [Test]
    public void Monster_LeavesItemOnFloor_WhenInventoryFull()
    {
        // InventorySize=0 means the monster has no inventory capacity (only equipment)
        // Give it a full inventory by setting InventorySize=1 and pre-filling
        var (state, monster) = CreateState(monsterX: 10, monsterY: 10, inventorySize: 1);

        // Pre-fill inventory to capacity
        var existingPotion = new Entity(98, "Old Potion");
        existingPotion.Add(new Consumable(healAmount: 10));
        monster.Require<Inventory>().Add(existingPotion);

        // Pre-equip weapon so auto-equip slot is also taken
        var existingWeapon = new Entity(99, "Existing Sword", 0, 0);
        existingWeapon.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 2, DamageMax = 4 });
        monster.Require<Equipment>().SetSlot(EquipmentSlot.MainHand, existingWeapon);

        // A weapon on monster's tile — can't equip (slot full) and can't inventory (full)
        MakeWeapon(50, "Dropped Sword", 10, 10, state);

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(state.FloorItems.Any(i => i.Id == 50), Is.True,
            "Item should remain on floor when monster has no room for it");
    }

    // ─── Drop on death ────────────────────────────────────────────────────────

    [Test]
    public void MonsterDeath_DropsEquippedWeapon()
    {
        // Player at (1,1), monster at (2,1) — adjacent. Player deals 999 damage, guaranteed kill.
        var (state, monster) = CreateState(monsterX: 2, monsterY: 1);

        // Equip a weapon so it will drop on death
        var weapon = new Entity(50, "Iron Sword", 0, 0);
        weapon.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 3, DamageMax = 6 });
        monster.Require<Equipment>().SetSlot(EquipmentSlot.MainHand, weapon);

        // Keep attacking until monster dies (999 damage guarantees kill on first hit)
        TurnResult? lastResult = null;
        for (int i = 0; i < 10; i++)
        {
            if (!monster.Require<Fighter>().IsAlive) break;
            lastResult = TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
        }

        Assert.That(monster.Require<Fighter>().IsAlive, Is.False, "Precondition: monster should be dead");

        // Weapon should be on the floor at the monster's position
        var droppedWeapon = state.FloorItems.FirstOrDefault(i => i.Id == 50);
        Assert.That(droppedWeapon, Is.Not.Null, "Equipped weapon should drop on monster death");
        Assert.That(droppedWeapon!.X, Is.EqualTo(monster.X), "Dropped item X should match monster X");
        Assert.That(droppedWeapon.Y, Is.EqualTo(monster.Y), "Dropped item Y should match monster Y");
    }

    [Test]
    public void MonsterDeath_DropsInventoryItems()
    {
        var (state, monster) = CreateState(monsterX: 2, monsterY: 1);

        // Add a potion to inventory
        var potion = new Entity(60, "Healing Potion");
        potion.Add(new Consumable(healAmount: 20));
        monster.Require<Inventory>().Add(potion);

        for (int i = 0; i < 10; i++)
        {
            if (!monster.Require<Fighter>().IsAlive) break;
            TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
        }

        Assert.That(monster.Require<Fighter>().IsAlive, Is.False);

        var droppedPotion = state.FloorItems.FirstOrDefault(i => i.Id == 60);
        Assert.That(droppedPotion, Is.Not.Null, "Inventory potion should drop on monster death");
    }

    [Test]
    public void MonsterDeath_EmitsDropEvents_ForEachItem()
    {
        var (state, monster) = CreateState(monsterX: 2, monsterY: 1);

        var weapon = new Entity(50, "Iron Sword", 0, 0);
        weapon.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 3, DamageMax = 6 });
        monster.Require<Equipment>().SetSlot(EquipmentSlot.MainHand, weapon);

        TurnResult? result = null;
        for (int i = 0; i < 10; i++)
        {
            if (!monster.Require<Fighter>().IsAlive) break;
            result = TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
        }

        Assert.That(result, Is.Not.Null);
        var dropEvents = result!.Events.OfType<DropEvent>().Where(e => e.ActorId == 1).ToList();
        Assert.That(dropEvents, Has.Count.EqualTo(1), "One DropEvent per dropped item");
        Assert.That(dropEvents[0].ItemId, Is.EqualTo(50));
    }

    [Test]
    public void MonsterDeath_NoItems_NoDropEvents()
    {
        // Monster with no equipment or inventory items — death should emit no DropEvents
        var (state, monster) = CreateState(monsterX: 2, monsterY: 1);

        TurnResult? result = null;
        for (int i = 0; i < 10; i++)
        {
            if (!monster.Require<Fighter>().IsAlive) break;
            result = TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
        }

        Assert.That(result, Is.Not.Null);
        var dropEvents = result!.Events.OfType<DropEvent>().Where(e => e.ActorId == 1).ToList();
        Assert.That(dropEvents, Is.Empty, "No DropEvents when monster has no items");
        Assert.That(state.FloorItems, Is.Empty, "No floor items added when monster had nothing");
    }

    [Test]
    public void DroppedItems_CanBePickedUpByPlayer()
    {
        // Monster at (2,1) adjacent to player at (1,1) — player kills it.
        // Monster had a weapon equipped. Player walks onto monster's tile to pick it up.
        var (state, monster) = CreateState(monsterX: 2, monsterY: 1);

        var weapon = new Entity(50, "Iron Sword", 0, 0);
        weapon.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 3, DamageMax = 6 });
        monster.Require<Equipment>().SetSlot(EquipmentSlot.MainHand, weapon);

        for (int i = 0; i < 10; i++)
        {
            if (!monster.Require<Fighter>().IsAlive) break;
            TurnController.ProcessTurn(state, PlayerAction.Attack(monster));
        }

        Assert.That(state.FloorItems.Count, Is.EqualTo(1), "Weapon should be on floor");

        // Player walks onto the monster's tile — auto-pickup
        var playerInventory = state.Player.GetOrAdd<Inventory>();
        TurnController.ProcessTurn(state, PlayerAction.MoveTo(monster.X, monster.Y));

        Assert.That(state.FloorItems, Is.Empty, "Floor should be empty after pickup");
        Assert.That(playerInventory.Count, Is.EqualTo(1), "Player should have the dropped weapon");
    }
}
