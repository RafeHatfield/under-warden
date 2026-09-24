using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

[TestFixture]
public class PlayerCarryForwardTests
{
    private static Entity BuildPlayer(int currentHp = 30, int maxHp = 54)
    {
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        // Create fighter with the given base max HP
        player.Add(new Fighter(hp: maxHp, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 2, evasion: 1, damageMin: 1, damageMax: 4));
        // Damage down to currentHp
        int damage = maxHp - currentHp;
        if (damage > 0)
            player.Require<Fighter>().TakeDamage(damage);
        return player;
    }

    // ─── HP carry-forward ────────────────────────────────────────────────────

    [Test]
    public void Apply_PreservesCurrentHp_NotMaxHp()
    {
        var player = BuildPlayer(currentHp: 30, maxHp: 54);
        Assert.That(player.Require<Fighter>().Hp, Is.EqualTo(30), "Precondition: player is wounded");

        var newPlayer = PlayerCarryForward.Apply(player);

        Assert.That(newPlayer.Require<Fighter>().Hp, Is.EqualTo(30),
            "Current HP should carry forward (wounds persist between floors)");
    }

    [Test]
    public void Apply_PreservesBaseMaxHp()
    {
        var player = BuildPlayer(currentHp: 54, maxHp: 54);

        var newPlayer = PlayerCarryForward.Apply(player);

        Assert.That(newPlayer.Require<Fighter>().BaseMaxHp, Is.EqualTo(54));
    }

    // ─── Entity ID + position reset ─────────────────────────────────────────

    [Test]
    public void Apply_NewPlayer_IsAlwaysId0()
    {
        // Even if the source entity somehow had a different ID, carry-forward always produces ID 0.
        // In practice the player is always 0, but the guarantee should hold.
        var player = BuildPlayer();

        var newPlayer = PlayerCarryForward.Apply(player);

        Assert.That(newPlayer.Id, Is.EqualTo(0), "Player entity is always ID 0");
    }

    [Test]
    public void Apply_PositionResetToOrigin()
    {
        var player = BuildPlayer();
        player.X = 15;
        player.Y = 22;

        var newPlayer = PlayerCarryForward.Apply(player);

        // Position is reset to (0,0) — DungeonFloorBuilder overrides this to the spawn point
        Assert.That(newPlayer.X, Is.EqualTo(0));
        Assert.That(newPlayer.Y, Is.EqualTo(0));
    }

    // ─── Equipment carry-forward ─────────────────────────────────────────────

    [Test]
    public void Apply_EquipmentPresent_IsCarriedForward()
    {
        var player = BuildPlayer();
        var equipment = player.Add(new Equipment());
        var sword = new Entity(50, "Short Sword");
        sword.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = 3, DamageMax = 6, ToHitBonus = 1 });
        equipment.MainHand = sword;

        var newPlayer = PlayerCarryForward.Apply(player);

        var newEquipment = newPlayer.Get<Equipment>();
        Assert.That(newEquipment, Is.Not.Null, "Equipment component should be present");
        Assert.That(newEquipment!.MainHand, Is.SameAs(sword), "Equipped sword should be the same entity");
    }

    [Test]
    public void Apply_NoEquipment_NoEquipmentComponent()
    {
        var player = BuildPlayer(); // no equipment added
        Assert.That(player.Get<Equipment>(), Is.Null, "Precondition: no equipment");

        var newPlayer = PlayerCarryForward.Apply(player);

        Assert.That(newPlayer.Get<Equipment>(), Is.Null, "No equipment component should be added");
    }

    // ─── Inventory carry-forward ─────────────────────────────────────────────

    [Test]
    public void Apply_InventoryPresent_IsCarriedForward()
    {
        var player = BuildPlayer();
        var inventory = player.Add(new Inventory());
        var potion = new Entity(99, "Healing Potion");
        potion.Add(new Consumable(healAmount: 40));
        inventory.Add(potion);

        var newPlayer = PlayerCarryForward.Apply(player);

        var newInventory = newPlayer.Get<Inventory>();
        Assert.That(newInventory, Is.Not.Null, "Inventory component should be present");
        Assert.That(newInventory!.Count, Is.EqualTo(1), "One item should be in the inventory");
        Assert.That(newInventory.Items[0], Is.SameAs(potion), "Potion should be the same entity");
    }

    [Test]
    public void Apply_NoInventory_NoInventoryComponent()
    {
        var player = BuildPlayer();
        Assert.That(player.Get<Inventory>(), Is.Null, "Precondition: no inventory");

        var newPlayer = PlayerCarryForward.Apply(player);

        Assert.That(newPlayer.Get<Inventory>(), Is.Null, "No inventory component should be added");
    }

    [Test]
    public void Apply_MultipleItems_AllCarriedForward()
    {
        var player = BuildPlayer();
        var inventory = player.Add(new Inventory());
        for (int i = 0; i < 5; i++)
        {
            var item = new Entity(100 + i, $"Potion {i}");
            item.Add(new Consumable(healAmount: 20));
            inventory.Add(item);
        }

        var newPlayer = PlayerCarryForward.Apply(player);

        Assert.That(newPlayer.Require<Inventory>().Count, Is.EqualTo(5));
    }

    // ─── All 8 equipment slots survive floor transition ──────────────────────

    [Test]
    public void Apply_AllEightEquipmentSlots_SurviveFloorTransition()
    {
        // Regression guard for DEBT-002: LeftRing, RightRing, Neck were previously dropped.
        var player = BuildPlayer();
        var equipment = player.Add(new Equipment());

        var mainHand  = new Entity(10, "Main Sword");  mainHand.Add(new Equippable(EquipmentSlot.MainHand));
        var offHand   = new Entity(11, "Shield");      offHand.Add(new Equippable(EquipmentSlot.OffHand));
        var head      = new Entity(12, "Helm");        head.Add(new Equippable(EquipmentSlot.Head));
        var chest     = new Entity(13, "Breastplate");  chest.Add(new Equippable(EquipmentSlot.Chest));
        var feet      = new Entity(14, "Boots");       feet.Add(new Equippable(EquipmentSlot.Feet));
        var leftRing  = new Entity(15, "Left Ring");   leftRing.Add(new Equippable(EquipmentSlot.LeftRing));
        var rightRing = new Entity(16, "Right Ring");  rightRing.Add(new Equippable(EquipmentSlot.RightRing));
        var neck      = new Entity(17, "Amulet");      neck.Add(new Equippable(EquipmentSlot.Neck));

        equipment.MainHand  = mainHand;
        equipment.OffHand   = offHand;
        equipment.Head      = head;
        equipment.Chest     = chest;
        equipment.Feet      = feet;
        equipment.LeftRing  = leftRing;
        equipment.RightRing = rightRing;
        equipment.Neck      = neck;

        var newPlayer = PlayerCarryForward.Apply(player);
        var newEquipment = newPlayer.Require<Equipment>();

        Assert.That(newEquipment.MainHand,  Is.SameAs(mainHand),  "MainHand should survive floor transition");
        Assert.That(newEquipment.OffHand,   Is.SameAs(offHand),   "OffHand should survive floor transition");
        Assert.That(newEquipment.Head,      Is.SameAs(head),      "Head should survive floor transition");
        Assert.That(newEquipment.Chest,     Is.SameAs(chest),     "Chest should survive floor transition");
        Assert.That(newEquipment.Feet,      Is.SameAs(feet),      "Feet should survive floor transition");
        Assert.That(newEquipment.LeftRing,  Is.SameAs(leftRing),  "LeftRing should survive floor transition");
        Assert.That(newEquipment.RightRing, Is.SameAs(rightRing), "RightRing should survive floor transition");
        Assert.That(newEquipment.Neck,      Is.SameAs(neck),      "Neck should survive floor transition");
    }

    // ─── SpeedBonusTracker NOT carried forward ───────────────────────────────

    [Test]
    public void Apply_SpeedBonusTracker_NotCarriedForward()
    {
        var player = BuildPlayer();
        // Even if the player has a SpeedBonusTracker, it should NOT carry over
        // (momentum resets between floors — this is intentional per design spec)
        player.Add(new SpeedBonusTracker(baseRatio: 0.3));

        var newPlayer = PlayerCarryForward.Apply(player);

        Assert.That(newPlayer.Get<SpeedBonusTracker>(), Is.Null,
            "SpeedBonusTracker should NOT carry forward — momentum resets between floors");
    }
}
