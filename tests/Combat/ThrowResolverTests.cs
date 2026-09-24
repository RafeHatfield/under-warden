using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Combat;

/// <summary>
/// Unit tests for ThrowResolver. Covers all three resolution paths (weapon, potion, junk),
/// Bresenham path calculation, invisibility break, momentum reset, and edge cases.
/// Tests use 20×20 all-walkable maps so path calculations are unconstrained unless walls are added.
/// </summary>
[TestFixture]
public class ThrowResolverTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static GameState CreateState(int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = new GameMap(20, 20); // all walkable by default
        var player = new Entity(1, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 2, damageMax: 4));
        player.Add(new Inventory());
        player.Add(new Equipment());
        map.RegisterEntity(player);
        return new GameState(player, new List<Entity>(), map, rng, turnLimit: 100);
    }

    private static Entity CreateMonster(int id, string name, int x, int y, int hp = 20)
    {
        var monster = new Entity(id, name, x, y, blocksMovement: true);
        monster.Add(new Fighter(hp: hp, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 3, evasion: 1, damageMin: 2, damageMax: 4));
        monster.Add(new AiComponent { AiType = "basic", Faction = "enemy" });
        return monster;
    }

    private static Entity CreateWeapon(int id, string name = "Sword", int dmgMin = 4, int dmgMax = 6)
    {
        var weapon = new Entity(id, name);
        weapon.Add(new Equippable(EquipmentSlot.MainHand)
        {
            DamageMin = dmgMin,
            DamageMax = dmgMax,
        });
        return weapon;
    }

    /// <summary>Create a throwable potion (has SpellEffect with ThrowSpellId set).</summary>
    private static Entity CreateThrowablePotion(int id, string name = "Weakness Potion")
    {
        var potion = new Entity(id, name);
        potion.Add(new Consumable(healAmount: 0, isPotion: true) { StackSize = 1 });
        potion.Add(new SpellEffect
        {
            SpellId      = "drink_weakness",
            ThrowSpellId = "throw_weakness",
            Targeting    = TargetingMode.SingleTarget,
            Duration     = 10,
            Range        = 10,
        });
        return potion;
    }

    /// <summary>Add a monster to the state at the given position.</summary>
    private static void AddMonster(GameState state, Entity monster)
    {
        state.Monsters.Add(monster);
        state.Map.RegisterEntity(monster);
    }

    /// <summary>Add an item to the player's inventory.</summary>
    private static void GiveItem(GameState state, Entity item)
    {
        state.PlayerInventory!.Add(item);
    }

    // ─── Weapon path tests ────────────────────────────────────────────────────

    [Test]
    public void ThrowWeapon_HitsMonster_DealsDamageMinusTwo()
    {
        // Weapon 4d6 → throw deals roll - 2 (min 1).
        // With seed 1337 and RollDamage(4,6), we check the throw damage is <= 4 and >= 1.
        var state = CreateState();
        var monster = CreateMonster(2, "Orc", 8, 5, hp: 50);
        AddMonster(state, monster);

        var weapon = CreateWeapon(3, "Sword", dmgMin: 4, dmgMax: 6);
        GiveItem(state, weapon);

        var events = ThrowResolver.Resolve(state.Player, weapon, 8, 5, state);

        var throwEvt = events.OfType<ThrowEvent>().Single();
        Assert.That(throwEvt.Hit, Is.True, "Should hit monster at target tile");
        Assert.That(throwEvt.ResultType, Is.EqualTo(ThrowResultType.WeaponHit));

        // Damage must be weapon roll (4–6) minus 2 = 2–4, and never below 1
        Assert.That(throwEvt.Damage, Is.GreaterThanOrEqualTo(1));
        Assert.That(throwEvt.Damage, Is.LessThanOrEqualTo(4)); // max: 6 - 2 = 4

        // Monster took damage
        var monsterFighter = monster.Require<Fighter>();
        Assert.That(monsterFighter.Hp, Is.LessThan(50));
    }

    [Test]
    public void ThrowWeapon_MissesEmpty_LandsOnGround()
    {
        var state = CreateState();
        // No monster at target tile (9, 5)
        var weapon = CreateWeapon(3);
        GiveItem(state, weapon);

        var events = ThrowResolver.Resolve(state.Player, weapon, 9, 5, state);

        var throwEvt = events.OfType<ThrowEvent>().Single();
        Assert.That(throwEvt.Hit, Is.False);
        Assert.That(throwEvt.ResultType, Is.EqualTo(ThrowResultType.WeaponMiss));
        Assert.That(throwEvt.ItemLandsOnGround, Is.True);

        // Weapon must be on the floor
        Assert.That(state.FloorItems, Has.Member(weapon));
        // Weapon removed from inventory
        Assert.That(state.PlayerInventory!.Items, Does.Not.Contain(weapon));
    }

    [Test]
    public void ThrowWeapon_KillsMonster_EmitsDeathEvent()
    {
        var state = CreateState();
        // Monster with 1 HP dies to any throw damage
        var monster = CreateMonster(2, "Rat", 8, 5, hp: 1);
        AddMonster(state, monster);

        var weapon = CreateWeapon(3, "Sword", dmgMin: 5, dmgMax: 5); // always deals 3 after -2
        GiveItem(state, weapon);

        var events = ThrowResolver.Resolve(state.Player, weapon, 8, 5, state);

        Assert.That(events.OfType<DeathEvent>().Any(), Is.True, "DeathEvent must be emitted");
        var deathEvt = events.OfType<DeathEvent>().Single();
        Assert.That(deathEvt.ActorId, Is.EqualTo(monster.Id));
        Assert.That(deathEvt.KillerId, Is.EqualTo(state.Player.Id));

        var throwEvt = events.OfType<ThrowEvent>().Single();
        Assert.That(throwEvt.TargetKilled, Is.True);
        // Weapon lands on ground even after kill
        Assert.That(throwEvt.ItemLandsOnGround, Is.True);
    }

    [Test]
    public void ThrowWeapon_Equipped_AutoUnequips()
    {
        var state = CreateState();
        var weapon = CreateWeapon(3, "Sword");
        // Equip the weapon
        var equipment = state.Player.Get<Equipment>()!;
        equipment.MainHand = weapon;
        // Also in inventory (throws remove from inventory after auto-unequip)
        GiveItem(state, weapon);

        var events = ThrowResolver.Resolve(state.Player, weapon, 9, 5, state);

        // Equipment slot cleared
        Assert.That(equipment.MainHand, Is.Null);

        // UnequipEvent emitted
        var unequipEvt = events.OfType<UnequipEvent>().Single();
        Assert.That(unequipEvt.ItemId, Is.EqualTo(weapon.Id));
        Assert.That(unequipEvt.Slot, Is.EqualTo(EquipmentSlot.MainHand));

        // Weapon lands on ground
        Assert.That(state.FloorItems, Has.Member(weapon));
    }

    // ─── Potion path tests ────────────────────────────────────────────────────

    [Test]
    public void ThrowPotion_HitsMonster_AppliesEffect()
    {
        var state = CreateState();
        var monster = CreateMonster(2, "Orc", 8, 5, hp: 20);
        AddMonster(state, monster);

        var potion = CreateThrowablePotion(3);
        GiveItem(state, potion);

        var events = ThrowResolver.Resolve(state.Player, potion, 8, 5, state);

        var throwEvt = events.OfType<ThrowEvent>().Single();
        Assert.That(throwEvt.Hit, Is.True);
        Assert.That(throwEvt.ResultType, Is.EqualTo(ThrowResultType.PotionShatter));
        Assert.That(throwEvt.ItemLandsOnGround, Is.False, "Potions are consumed, not retrievable");

        // Weakness effect applied to monster
        Assert.That(monster.Has<WeaknessEffect>(), Is.True, "Monster should have WeaknessEffect");

        // Potion consumed
        Assert.That(state.PlayerInventory!.Items, Does.Not.Contain(potion));
    }

    [Test]
    public void ThrowPotion_MissesEmpty_Consumed()
    {
        var state = CreateState();
        // No monster at target tile
        var potion = CreateThrowablePotion(3);
        GiveItem(state, potion);

        var events = ThrowResolver.Resolve(state.Player, potion, 9, 5, state);

        var throwEvt = events.OfType<ThrowEvent>().Single();
        Assert.That(throwEvt.Hit, Is.False);
        Assert.That(throwEvt.ResultType, Is.EqualTo(ThrowResultType.PotionShatter));
        Assert.That(throwEvt.ItemLandsOnGround, Is.False);

        // Potion consumed even on miss
        Assert.That(state.PlayerInventory!.Items, Does.Not.Contain(potion));
        // Not on the floor
        Assert.That(state.FloorItems, Does.Not.Contain(potion));
    }

    [Test]
    public void ThrowPotion_Stacked_DecrementsStack()
    {
        var state = CreateState();
        var potion = CreateThrowablePotion(3, "Weakness Potion");
        potion.Get<Consumable>()!.StackSize = 3;
        GiveItem(state, potion);

        var events = ThrowResolver.Resolve(state.Player, potion, 9, 5, state);

        // Stack decremented — potion entity still in inventory with stack=2
        Assert.That(state.PlayerInventory!.Items, Has.Member(potion));
        Assert.That(potion.Get<Consumable>()!.StackSize, Is.EqualTo(2));
    }

    // ─── Junk path tests ──────────────────────────────────────────────────────

    [Test]
    public void ThrowJunk_Ring_LandsOnGround()
    {
        var state = CreateState();
        // Ring: has Equippable but IsWeapon=false, no SpellEffect
        var ring = new Entity(3, "Ring of Protection");
        ring.Add(new Equippable(EquipmentSlot.LeftRing) { ArmorClassBonus = 2 });
        GiveItem(state, ring);

        var events = ThrowResolver.Resolve(state.Player, ring, 9, 5, state);

        var throwEvt = events.OfType<ThrowEvent>().Single();
        Assert.That(throwEvt.Hit, Is.False);
        Assert.That(throwEvt.ResultType, Is.EqualTo(ThrowResultType.JunkLand));
        Assert.That(throwEvt.ItemLandsOnGround, Is.True);
        Assert.That(throwEvt.Damage, Is.EqualTo(0));

        Assert.That(state.FloorItems, Has.Member(ring));
        Assert.That(state.PlayerInventory!.Items, Does.Not.Contain(ring));
    }

    [Test]
    public void ThrowJunk_Scroll_LandsOnGround()
    {
        var state = CreateState();
        // Scroll: has SpellEffect but NO ThrowSpellId → treated as junk
        var scroll = new Entity(3, "Scroll of Light");
        scroll.Add(new Consumable(healAmount: 0, isPotion: false) { StackSize = 1 });
        scroll.Add(new SpellEffect { SpellId = "light", Targeting = TargetingMode.Self });
        GiveItem(state, scroll);

        var events = ThrowResolver.Resolve(state.Player, scroll, 9, 5, state);

        var throwEvt = events.OfType<ThrowEvent>().Single();
        Assert.That(throwEvt.ResultType, Is.EqualTo(ThrowResultType.JunkLand));
        Assert.That(throwEvt.ItemLandsOnGround, Is.True);
        Assert.That(state.FloorItems, Has.Member(scroll));
    }

    [Test]
    public void ThrowArmor_LandsOnGround()
    {
        var state = CreateState();
        var armor = new Entity(3, "Leather Armor");
        armor.Add(new Equippable(EquipmentSlot.Chest) { ArmorClassBonus = 3 });
        GiveItem(state, armor);

        var events = ThrowResolver.Resolve(state.Player, armor, 9, 5, state);

        var throwEvt = events.OfType<ThrowEvent>().Single();
        Assert.That(throwEvt.ResultType, Is.EqualTo(ThrowResultType.JunkLand));
        Assert.That(state.FloorItems, Has.Member(armor));
    }

    // ─── Cross-cutting invariants ──────────────────────────────────────────────

    [Test]
    public void Throw_BreaksInvisibility()
    {
        var state = CreateState();
        // Make player invisible
        state.Player.Add(new InvisibilityEffect { RemainingTurns = 20 });

        var weapon = CreateWeapon(3);
        GiveItem(state, weapon);

        var events = ThrowResolver.Resolve(state.Player, weapon, 9, 5, state);

        // Invisibility removed
        Assert.That(state.Player.Has<InvisibilityEffect>(), Is.False);

        // StatusExpiredEvent emitted
        var expiredEvt = events.OfType<StatusExpiredEvent>().SingleOrDefault();
        Assert.That(expiredEvt, Is.Not.Null);
        Assert.That(expiredEvt!.EffectName, Is.EqualTo("invisibility"));
        Assert.That(expiredEvt.Reason, Is.EqualTo("threw_item"));
    }

    [Test]
    public void Throw_ResetsMomentum()
    {
        var state = CreateState();
        var tracker = new SpeedBonusTracker(baseRatio: 0.25);
        // Build momentum by performing a fake roll (private _attackCounter is incremented by RollForBonusAttack)
        // We verify ResetMomentum was called by checking AttackCounter drops back to 0.
        // RollForBonusAttack increments the internal counter — we use a dummy RNG to drive it.
        var dummyRng = new SeededRandom(999);
        tracker.RollForBonusAttack(dummyRng); // increments _attackCounter to 1
        state.Player.Add(tracker);
        int counterBefore = tracker.AttackCounter;
        Assert.That(counterBefore, Is.EqualTo(1), "Counter should be 1 after one roll");

        var weapon = CreateWeapon(3);
        GiveItem(state, weapon);

        ThrowResolver.Resolve(state.Player, weapon, 9, 5, state);

        Assert.That(tracker.AttackCounter, Is.EqualTo(0), "Momentum should be reset (counter=0) after throw");
    }

    // ─── Bresenham / path tests ────────────────────────────────────────────────

    [Test]
    public void Throw_PathStopsAtWall()
    {
        // Build a map with a wall column at x=7
        var rng = new SeededRandom(1337);
        var map = new GameMap(20, 20); // default: all walkable
        // Block a column of tiles at x=7 using SetTile(Wall) which sets walkable=false
        for (int y = 0; y < 20; y++)
            map.SetTile(7, y, TileKind.Wall);

        var player = new Entity(1, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 50, damageMin: 2, damageMax: 4));
        player.Add(new Inventory());
        player.Add(new Equipment());
        map.RegisterEntity(player);

        var state = new GameState(player, new List<Entity>(), map, rng, turnLimit: 100);

        // Monster at x=9 (behind the wall at x=7)
        var monster = CreateMonster(2, "Orc", 9, 5, hp: 20);
        AddMonster(state, monster);

        var weapon = CreateWeapon(3, dmgMin: 4, dmgMax: 6);
        GiveItem(state, weapon);

        // Throw at x=9 (behind wall)
        var events = ThrowResolver.Resolve(player, weapon, 9, 5, state);

        var throwEvt = events.OfType<ThrowEvent>().Single();
        // Should stop at the wall tile (x=7) before the monster
        Assert.That(throwEvt.LandX, Is.EqualTo(7));
        Assert.That(throwEvt.Hit, Is.False, "Should not hit monster behind the wall");
    }

    [Test]
    public void Throw_MaxRange10()
    {
        var state = CreateState();
        // Target 15 tiles east of player (5+15=20, which is out of bounds, so use 18)
        // Player at (5,5), target at (18,5) — 13 tiles away, exceeds max range of 10
        var weapon = CreateWeapon(3);
        GiveItem(state, weapon);

        var events = ThrowResolver.Resolve(state.Player, weapon, 18, 5, state);

        var throwEvt = events.OfType<ThrowEvent>().Single();
        // Weapon should land at most 10 tiles from player (5+10=15)
        int landDist = Math.Abs(throwEvt.LandX - 5); // horizontal distance
        Assert.That(landDist, Is.LessThanOrEqualTo(10));
    }

    [Test]
    public void BresenhamPath_Straight_Horizontal()
    {
        var map = new GameMap(20, 20);
        var path = ThrowResolver.CalculatePath(5, 5, 10, 5, map, 10);

        // Horizontal line from (5,5) to (10,5) — should be exactly the 5 tiles to the right
        Assert.That(path.Count, Is.EqualTo(5));
        for (int i = 0; i < path.Count; i++)
        {
            Assert.That(path[i].X, Is.EqualTo(6 + i));
            Assert.That(path[i].Y, Is.EqualTo(5));
        }
    }

    [Test]
    public void BresenhamPath_Straight_Vertical()
    {
        var map = new GameMap(20, 20);
        var path = ThrowResolver.CalculatePath(5, 5, 5, 10, map, 10);

        Assert.That(path.Count, Is.EqualTo(5));
        for (int i = 0; i < path.Count; i++)
        {
            Assert.That(path[i].X, Is.EqualTo(5));
            Assert.That(path[i].Y, Is.EqualTo(6 + i));
        }
    }

    [Test]
    public void BresenhamPath_Diagonal()
    {
        var map = new GameMap(20, 20);
        // 45-degree diagonal from (0,0) to (4,4)
        var path = ThrowResolver.CalculatePath(0, 0, 4, 4, map, 10);

        Assert.That(path.Count, Is.EqualTo(4));
        for (int i = 0; i < path.Count; i++)
        {
            Assert.That(path[i].X, Is.EqualTo(i + 1));
            Assert.That(path[i].Y, Is.EqualTo(i + 1));
        }
    }
}
