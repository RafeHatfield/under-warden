using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Integration tests for the throw system. Tests full TurnController.ProcessTurn
/// cycles with PlayerAction.ThrowItem to verify the complete pipeline:
/// action → ThrowResolver → events → monster state changes → floor items.
/// </summary>
[TestFixture]
public class ThrowSystemIntegrationTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static GameState CreateState(int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = new GameMap(20, 20);
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

    private static Entity CreateWeapon(int id, int dmgMin = 4, int dmgMax = 6)
    {
        var weapon = new Entity(id, "Sword");
        weapon.Add(new Equippable(EquipmentSlot.MainHand) { DamageMin = dmgMin, DamageMax = dmgMax });
        return weapon;
    }

    private static Entity CreateThrowPotion(int id)
    {
        var potion = new Entity(id, "Weakness Potion");
        potion.Add(new Consumable(healAmount: 0, isPotion: true) { StackSize = 1 });
        potion.Add(new SpellEffect
        {
            SpellId      = "drink_weakness",
            ThrowSpellId = "throw_weakness",
            Targeting    = TargetingMode.SingleTarget,
            Duration     = 10,
        });
        return potion;
    }

    private static Entity CreateRing(int id)
    {
        var ring = new Entity(id, "Ring of Protection");
        ring.Add(new Equippable(EquipmentSlot.LeftRing) { ArmorClassBonus = 2 });
        return ring;
    }

    private static void AddMonster(GameState state, Entity monster)
    {
        state.Monsters.Add(monster);
        state.Map.RegisterEntity(monster);
    }

    private static void GiveItem(GameState state, Entity item)
    {
        state.PlayerInventory!.Add(item);
    }

    // ─── Full turn cycle tests ─────────────────────────────────────────────────

    [Test]
    public void ThrowWeapon_FullTurnCycle_MonsterTakesDamage_WeaponOnFloor()
    {
        var state = CreateState();
        var monster = CreateMonster(2, "Orc", 8, 5, hp: 50);
        AddMonster(state, monster);

        var weapon = CreateWeapon(3);
        GiveItem(state, weapon);

        var result = TurnController.ProcessTurn(state, PlayerAction.ThrowItem(weapon, 8, 5));

        // ThrowEvent emitted
        var throwEvt = result.Events.OfType<ThrowEvent>().SingleOrDefault();
        Assert.That(throwEvt, Is.Not.Null, "ThrowEvent should be emitted");
        Assert.That(throwEvt!.Hit, Is.True);
        Assert.That(throwEvt.ResultType, Is.EqualTo(ThrowResultType.WeaponHit));

        // Monster took damage
        var monsterFighter = monster.Require<Fighter>();
        Assert.That(monsterFighter.Hp, Is.LessThan(50), "Monster should have taken damage");

        // Weapon is on the floor
        Assert.That(state.FloorItems, Has.Member(weapon), "Weapon should be floor item");

        // Weapon is no longer in inventory
        Assert.That(state.PlayerInventory!.Items, Does.Not.Contain(weapon));

        // Monster got their turn (they may have moved or attacked)
        Assert.That(result.TurnNumber, Is.EqualTo(1));
    }

    [Test]
    public void ThrowPotion_FullTurnCycle_EffectApplied_NotOnFloor()
    {
        var state = CreateState();
        var monster = CreateMonster(2, "Orc", 8, 5, hp: 30);
        AddMonster(state, monster);

        var potion = CreateThrowPotion(3);
        GiveItem(state, potion);

        var result = TurnController.ProcessTurn(state, PlayerAction.ThrowItem(potion, 8, 5));

        var throwEvt = result.Events.OfType<ThrowEvent>().SingleOrDefault();
        Assert.That(throwEvt, Is.Not.Null);
        Assert.That(throwEvt!.ResultType, Is.EqualTo(ThrowResultType.PotionShatter));
        Assert.That(throwEvt.Hit, Is.True);
        Assert.That(throwEvt.ItemLandsOnGround, Is.False);

        // Potion consumed — not in inventory, not on floor
        Assert.That(state.PlayerInventory!.Items, Does.Not.Contain(potion));
        Assert.That(state.FloorItems, Does.Not.Contain(potion));

        // Weakness effect applied to monster
        Assert.That(monster.Has<WeaknessEffect>(), Is.True, "Monster should have WeaknessEffect");
    }

    [Test]
    public void ThrowRing_FullTurnCycle_LandsOnFloor_NoEffect()
    {
        var state = CreateState();
        var monster = CreateMonster(2, "Orc", 8, 5, hp: 30);
        AddMonster(state, monster);

        var ring = CreateRing(3);
        GiveItem(state, ring);

        var result = TurnController.ProcessTurn(state, PlayerAction.ThrowItem(ring, 8, 5));

        var throwEvt = result.Events.OfType<ThrowEvent>().SingleOrDefault();
        Assert.That(throwEvt, Is.Not.Null);
        Assert.That(throwEvt!.ResultType, Is.EqualTo(ThrowResultType.JunkLand));
        Assert.That(throwEvt.Damage, Is.EqualTo(0));

        // Ring on floor
        Assert.That(state.FloorItems, Has.Member(ring));

        // Monster HP unchanged (ring does no damage)
        Assert.That(monster.Require<Fighter>().Hp, Is.EqualTo(30));
    }

    [Test]
    public void ThrowWeapon_KillsMonster_DeathEventEmitted_WeaponOnFloor()
    {
        var state = CreateState();
        // Monster with 1 HP — any throw damage kills it
        var monster = CreateMonster(2, "Rat", 8, 5, hp: 1);
        AddMonster(state, monster);

        // Weapon with guaranteed damage 5 → 5-2=3 throw damage > 1 HP
        var weapon = CreateWeapon(3, dmgMin: 5, dmgMax: 5);
        GiveItem(state, weapon);

        var result = TurnController.ProcessTurn(state, PlayerAction.ThrowItem(weapon, 8, 5));

        // DeathEvent emitted
        Assert.That(result.Events.OfType<DeathEvent>().Any(), Is.True);
        var deathEvt = result.Events.OfType<DeathEvent>().Single();
        Assert.That(deathEvt.ActorId, Is.EqualTo(monster.Id));

        // Monster dead
        Assert.That(monster.Require<Fighter>().IsAlive, Is.False);

        // Weapon on floor (at monster's position)
        Assert.That(state.FloorItems, Has.Member(weapon));
        Assert.That(weapon.X, Is.EqualTo(8));
        Assert.That(weapon.Y, Is.EqualTo(5));

        // ThrowEvent marks kill
        var throwEvt = result.Events.OfType<ThrowEvent>().Single();
        Assert.That(throwEvt.TargetKilled, Is.True);
    }

    [Test]
    public void ThrowItem_FullActionDispatch_TurnControllerRoutes()
    {
        // Verify PlayerAction.ThrowItem is accepted by TurnController without error.
        // Tests the full dispatch path: ActionKind.ThrowItem → ResolveThrowItem → ThrowResolver.
        var state = CreateState();
        var weapon = CreateWeapon(3);
        GiveItem(state, weapon);

        // Throw at an empty tile
        var result = TurnController.ProcessTurn(state, PlayerAction.ThrowItem(weapon, 9, 5));

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Events.OfType<ThrowEvent>().Any(), Is.True);
        Assert.That(result.TurnNumber, Is.EqualTo(1));
        // No exception = routing works
    }
}
