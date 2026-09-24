using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for SpellResolver — each Phase 1 spell has at minimum a success and a failure path.
/// GameState is scenario-mode (IsDungeonMode=false) so all tiles are visible by default,
/// which simplifies target detection in tests.
/// </summary>
[TestFixture]
public class SpellResolverTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static GameState CreateState(
        int playerX = 5, int playerY = 5,
        int mapSize = 20,
        int seed = 1337,
        int turnLimit = 100)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(mapSize, mapSize);

        var player = new Entity(0, "Player", playerX, playerY, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        return new GameState(player, new List<Entity>(), map, rng, turnLimit);
    }

    private static Entity AddMonster(GameState state, int id, int x, int y, int hp = 20)
    {
        var m = new Entity(id, "Orc", x, y, blocksMovement: true);
        m.Add(new Fighter(hp: hp, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        state.Monsters.Add(m);
        state.Map.RegisterEntity(m);
        return m;
    }

    private static SpellEffect MakeSpell(string spellId,
        TargetingMode targeting = TargetingMode.Self,
        int damage = 0, int radius = 0, int range = 0, int duration = 0)
        => new SpellEffect
        {
            SpellId = spellId,
            Targeting = targeting,
            Damage = damage,
            Radius = radius,
            Range = range,
            Duration = duration,
        };

    // ─── Lightning ───────────────────────────────────────────────────────────

    [Test]
    public void Lightning_HitsNearestVisibleMonster_DealsDamage()
    {
        var state = CreateState();
        var monster = AddMonster(state, 1, x: 6, y: 5, hp: 100);
        var spell = MakeSpell("lightning", TargetingMode.AutoClosest, damage: 40, range: 5);

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.True);
        Assert.That(spellEvent.TargetId, Is.EqualTo(monster.Id));
        Assert.That(spellEvent.Damage, Is.GreaterThan(0));
        Assert.That(monster.Require<Fighter>().Hp, Is.LessThan(100));
    }

    [Test]
    public void Lightning_KillsMonster_EmitsDeathEvent()
    {
        var state = CreateState();
        var monster = AddMonster(state, 1, x: 6, y: 5, hp: 1);
        var spell = MakeSpell("lightning", TargetingMode.AutoClosest, damage: 40, range: 5);

        var events = SpellResolver.Resolve(state.Player, spell, state);

        Assert.That(events.OfType<DeathEvent>().Any(), Is.True);
        Assert.That(monster.Require<Fighter>().IsAlive, Is.False);
    }

    [Test]
    public void Lightning_NoVisibleMonsters_ReturnsFailed()
    {
        var state = CreateState();
        // No monsters added
        var spell = MakeSpell("lightning", TargetingMode.AutoClosest, damage: 40, range: 5);

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.False);
    }

    [Test]
    public void Lightning_MonsterOutOfRange_NotHit()
    {
        // Player at (5,5), monster at (12,5) — distance ~7, range=5
        var state = CreateState(playerX: 5, playerY: 5);
        AddMonster(state, 1, x: 12, y: 5, hp: 100);
        var spell = MakeSpell("lightning", TargetingMode.AutoClosest, damage: 40, range: 5);

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.False);
    }

    // ─── Earthquake ──────────────────────────────────────────────────────────

    [Test]
    public void Earthquake_DamagesAllVisibleMonstersInRadius()
    {
        var state = CreateState();
        var m1 = AddMonster(state, 1, x: 5, y: 6, hp: 100); // distance 1
        var m2 = AddMonster(state, 2, x: 5, y: 7, hp: 100); // distance 2
        var spell = MakeSpell("earthquake", TargetingMode.AoeSelf, damage: 20, radius: 3);

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.True);
        Assert.That(spellEvent.AffectedIds, Does.Contain(m1.Id));
        Assert.That(spellEvent.AffectedIds, Does.Contain(m2.Id));
        Assert.That(m1.Require<Fighter>().Hp, Is.LessThan(100));
        Assert.That(m2.Require<Fighter>().Hp, Is.LessThan(100));
    }

    [Test]
    public void Earthquake_DoesNotDamageCaster()
    {
        var state = CreateState();
        AddMonster(state, 1, x: 5, y: 6, hp: 100);
        int playerHpBefore = state.Player.Require<Fighter>().Hp;
        var spell = MakeSpell("earthquake", TargetingMode.AoeSelf, damage: 20, radius: 3);

        SpellResolver.Resolve(state.Player, spell, state);

        Assert.That(state.Player.Require<Fighter>().Hp, Is.EqualTo(playerHpBefore),
            "Earthquake must not damage the caster");
    }

    [Test]
    public void Earthquake_MonsterOutOfRadius_NotHit()
    {
        // Player at (5,5), monster at (5, 11) — Chebyshev distance 6, radius 3
        var state = CreateState();
        var monster = AddMonster(state, 1, x: 5, y: 11, hp: 100);
        var spell = MakeSpell("earthquake", TargetingMode.AoeSelf, damage: 20, radius: 3);

        SpellResolver.Resolve(state.Player, spell, state);

        Assert.That(monster.Require<Fighter>().Hp, Is.EqualTo(100));
    }

    [Test]
    public void Earthquake_KillsMonsters_EmitsDeathEvents()
    {
        var state = CreateState();
        AddMonster(state, 1, x: 5, y: 6, hp: 1);
        AddMonster(state, 2, x: 5, y: 7, hp: 1);
        var spell = MakeSpell("earthquake", TargetingMode.AoeSelf, damage: 20, radius: 3);

        var events = SpellResolver.Resolve(state.Player, spell, state);

        Assert.That(events.OfType<DeathEvent>().Count(), Is.EqualTo(2));
    }

    // ─── Magic Mapping ───────────────────────────────────────────────────────

    [Test]
    public void MagicMapping_EmitsMapRevealEventWithTypeFull()
    {
        var state = CreateState();
        var spell = MakeSpell("magic_mapping");

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var revealEvent = events.OfType<MapRevealEvent>().Single();
        Assert.That(revealEvent.RevealType, Is.EqualTo("full"));
    }

    // ─── Light ───────────────────────────────────────────────────────────────

    [Test]
    public void Light_EmitsMapRevealEventWithTypeFov()
    {
        var state = CreateState();
        var spell = MakeSpell("light");

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var revealEvent = events.OfType<MapRevealEvent>().Single();
        Assert.That(revealEvent.RevealType, Is.EqualTo("fov"));
    }

    // ─── Detect Monsters ─────────────────────────────────────────────────────

    [Test]
    public void DetectMonsters_EmitsEventWithAllMonsterPositions()
    {
        var state = CreateState();
        var m1 = AddMonster(state, 1, x: 3, y: 4);
        var m2 = AddMonster(state, 2, x: 8, y: 9);
        var spell = MakeSpell("detect_monsters", duration: 20);

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var detectEvent = events.OfType<DetectMonstersEvent>().Single();
        Assert.That(detectEvent.Duration, Is.EqualTo(20));
        Assert.That(detectEvent.MonsterPositions.Any(p => p.MonsterId == m1.Id), Is.True);
        Assert.That(detectEvent.MonsterPositions.Any(p => p.MonsterId == m2.Id), Is.True);
    }

    [Test]
    public void DetectMonsters_DefaultDuration20_WhenSpellDurationIsZero()
    {
        var state = CreateState();
        AddMonster(state, 1, x: 3, y: 4);
        // duration=0 means use default (20)
        var spell = MakeSpell("detect_monsters", duration: 0);

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var detectEvent = events.OfType<DetectMonstersEvent>().Single();
        Assert.That(detectEvent.Duration, Is.EqualTo(20));
    }

    // ─── Enhance Weapon ──────────────────────────────────────────────────────

    [Test]
    public void EnhanceWeapon_IncreasesEquippedWeaponDamage()
    {
        var state = CreateState();
        var weapon = new Entity(10, "Dagger");
        var equippable = new Equippable(EquipmentSlot.MainHand) { DamageMin = 1, DamageMax = 4 };
        weapon.Add(equippable);

        var equipment = new Equipment();
        equipment.SetSlot(EquipmentSlot.MainHand, weapon);
        state.Player.Add(equipment);

        var spell = MakeSpell("enhance_weapon");
        SpellResolver.Resolve(state.Player, spell, state);

        Assert.That(equippable.DamageMin, Is.EqualTo(2), "DamageMin should increase by 1");
        Assert.That(equippable.DamageMax, Is.EqualTo(6), "DamageMax should increase by 2");
    }

    [Test]
    public void EnhanceWeapon_NoWeaponEquipped_ReturnsFailedEvent()
    {
        var state = CreateState();
        // No equipment added — no weapon
        var spell = MakeSpell("enhance_weapon");

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.False);
    }

    // ─── Enhance Armor ───────────────────────────────────────────────────────

    [Test]
    public void EnhanceArmor_IncreasesEquippedArmorAc()
    {
        var state = CreateState();
        var armorEntity = new Entity(11, "Leather Armor");
        var equippable = new Equippable(EquipmentSlot.Chest) { ArmorClassBonus = 2 };
        armorEntity.Add(equippable);

        var equipment = new Equipment();
        equipment.SetSlot(EquipmentSlot.Chest, armorEntity);
        state.Player.Add(equipment);

        var spell = MakeSpell("enhance_armor");
        SpellResolver.Resolve(state.Player, spell, state);

        Assert.That(equippable.ArmorClassBonus, Is.EqualTo(3));
    }

    [Test]
    public void EnhanceArmor_NoArmorEquipped_ReturnsFailedEvent()
    {
        var state = CreateState();
        var spell = MakeSpell("enhance_armor");

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.False);
    }

    // ─── Unknown Spell ───────────────────────────────────────────────────────

    [Test]
    public void UnknownSpellId_ReturnsFailedSpellEvent()
    {
        var state = CreateState();
        var spell = MakeSpell("this_spell_does_not_exist");

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.False);
    }
}
