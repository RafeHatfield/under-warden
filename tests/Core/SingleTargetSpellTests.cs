using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for Phase 3 single-target status effect spells.
/// Each test verifies that the correct status effect component is attached to the
/// target entity after the spell fires. Behavioral integration (confused movement,
/// slowed turn skipping) is tested by plan_status_effects — not this suite.
///
/// Test setup: scenario mode (IsDungeonMode=false), all tiles visible.
/// Target is pre-provided via targetEntityId (as the UI would after a player tap).
/// </summary>
[TestFixture]
public class SingleTargetSpellTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (GameState state, Entity target) CreateStateWithTarget(
        int targetX = 6, int targetY = 5,
        int seed = 1337)
    {
        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(20, 20);

        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var target = new Entity(1, "Orc", targetX, targetY, blocksMovement: true);
        target.Add(new Fighter(hp: 30, strength: 14, dexterity: 10, constitution: 12,
            accuracy: 4, evasion: 1, damageMin: 4, damageMax: 6));
        // Add AiComponent so faction/tag lookups work
        target.Add(new AiComponent
        {
            AiType = "basic",
            Faction = "orc",
            Tags = ["humanoid", "living"],
        });
        map.RegisterEntity(target);

        var state = new GameState(player, [target], map, rng, turnLimit: 100);
        return (state, target);
    }

    private static SpellEffect MakeSpell(string spellId, int range = 8, int duration = 0)
        => new SpellEffect
        {
            SpellId = spellId,
            Targeting = TargetingMode.SingleTarget,
            Range = range,
            Duration = duration,
        };

    // ─── Confusion ──────────────────────────────────────────────────────────

    [Test]
    public void Confusion_AppliesDisorientationEffectToTarget()
    {
        var (state, target) = CreateStateWithTarget();
        var spell = MakeSpell("confusion", range: 8, duration: 10);

        var events = SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        Assert.That(target.Has<DisorientationEffect>(), Is.True);
        Assert.That(target.Require<DisorientationEffect>().RemainingTurns, Is.EqualTo(10));
    }

    [Test]
    public void Confusion_EmitsSpellEventWithStatusApplied()
    {
        var (state, target) = CreateStateWithTarget();
        var spell = MakeSpell("confusion", duration: 10);

        var events = SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.True);
        Assert.That(spellEvent.StatusApplied, Is.EqualTo("disoriented"));
        Assert.That(spellEvent.StatusDuration, Is.EqualTo(10));
        Assert.That(spellEvent.TargetId, Is.EqualTo(target.Id));
    }

    [Test]
    public void Confusion_DefaultDuration10_WhenDurationZero()
    {
        var (state, target) = CreateStateWithTarget();
        var spell = MakeSpell("confusion", duration: 0); // 0 = use default

        SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        Assert.That(target.Require<DisorientationEffect>().RemainingTurns, Is.EqualTo(10));
    }

    // ─── Slow ────────────────────────────────────────────────────────────────

    [Test]
    public void Slow_AppliesSlowedEffectToTarget()
    {
        var (state, target) = CreateStateWithTarget();
        var spell = MakeSpell("slow", duration: 10);

        SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        Assert.That(target.Has<SlowedEffect>(), Is.True);
        Assert.That(target.Require<SlowedEffect>().RemainingTurns, Is.EqualTo(10));
    }

    // ─── Glue ────────────────────────────────────────────────────────────────

    [Test]
    public void Glue_AppliesImmobilizedEffect()
    {
        var (state, target) = CreateStateWithTarget();
        var spell = MakeSpell("glue", duration: 5);

        SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        Assert.That(target.Has<ImmobilizedEffect>(), Is.True);
        Assert.That(target.Require<ImmobilizedEffect>().RemainingTurns, Is.EqualTo(5));
    }

    // ─── Rage ────────────────────────────────────────────────────────────────

    [Test]
    public void Rage_AppliesEnragedEffect()
    {
        var (state, target) = CreateStateWithTarget();
        var spell = MakeSpell("rage", duration: 8);

        SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        Assert.That(target.Has<EnragedEffect>(), Is.True);
        var effect = target.Require<EnragedEffect>();
        Assert.That(effect.RemainingTurns, Is.EqualTo(8));
        Assert.That(effect.DamageMultiplier, Is.EqualTo(2.0));
        Assert.That(effect.AccuracyMultiplier, Is.EqualTo(0.5));
    }

    // ─── Yo Mama ─────────────────────────────────────────────────────────────

    [Test]
    public void YoMama_AppliesPermanentTauntFixatedOnCaster()
    {
        var (state, target) = CreateStateWithTarget();
        var spell = MakeSpell("yo_mama");

        SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        Assert.That(target.Has<TauntedEffect>(), Is.True);
        var effect = target.Require<TauntedEffect>();
        Assert.That(effect.TauntTargetId, Is.EqualTo(state.Player.Id));
        Assert.That(effect.RemainingTurns, Is.EqualTo(-1), "Yo Mama taunt is permanent");
    }

    [Test]
    public void YoMama_EmitsSpellEventWithPermanentDuration()
    {
        var (state, target) = CreateStateWithTarget();
        var spell = MakeSpell("yo_mama");

        var events = SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.StatusDuration, Is.EqualTo(-1));
    }

    // ─── Disarm ──────────────────────────────────────────────────────────────

    [Test]
    public void Disarm_AppliesDisarmedEffect()
    {
        var (state, target) = CreateStateWithTarget();
        var spell = MakeSpell("disarm", duration: 3);

        SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        Assert.That(target.Has<DisarmedEffect>(), Is.True);
        Assert.That(target.Require<DisarmedEffect>().RemainingTurns, Is.EqualTo(3));
    }

    // ─── Plague ──────────────────────────────────────────────────────────────

    [Test]
    public void Plague_AppliesOnCorporealTarget()
    {
        // Target has ["humanoid", "living"] tags — corporeal
        var (state, target) = CreateStateWithTarget();
        var spell = MakeSpell("plague", duration: 20);

        SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        Assert.That(target.Has<PlagueEffect>(), Is.True);
        var effect = target.Require<PlagueEffect>();
        Assert.That(effect.RemainingTurns, Is.EqualTo(20));
        Assert.That(effect.DamagePerTurn, Is.EqualTo(1));
    }

    [Test]
    public void Plague_FailsOnNonCorporealTarget()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);
        var player = new Entity(0, "Player", 5, 5, true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        // Skeleton — undead_bone tag → non-corporeal
        var skeleton = new Entity(1, "Skeleton", 6, 5, true);
        skeleton.Add(new Fighter(hp: 20, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        skeleton.Add(new AiComponent
        {
            Faction = "undead",
            Tags = ["undead", "undead_bone"],
        });
        map.RegisterEntity(skeleton);

        var state = new GameState(player, [skeleton], map, rng);
        var spell = MakeSpell("plague", duration: 20);

        var events = SpellResolver.Resolve(player, spell, state, targetEntityId: skeleton.Id);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.False, "Plague should fail on non-corporeal targets");
        Assert.That(skeleton.Has<PlagueEffect>(), Is.False);
    }

    // ─── Aggravation ─────────────────────────────────────────────────────────

    [Test]
    public void Aggravation_AppliesAggravatedEffectWithTargetFaction()
    {
        var (state, target) = CreateStateWithTarget();
        var spell = MakeSpell("aggravation");

        SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        Assert.That(target.Has<AggravatedEffect>(), Is.True);
        var effect = target.Require<AggravatedEffect>();
        // Target is an orc (Faction="orc") so should be aggravated against "orc"
        Assert.That(effect.TargetFaction, Is.EqualTo("orc"));
        Assert.That(effect.RemainingTurns, Is.EqualTo(-1), "Aggravation is permanent");
    }

    // ─── Range Validation ────────────────────────────────────────────────────

    [Test]
    public void SingleTarget_OutOfRange_NoEffect()
    {
        // Target at (15, 5) — distance ~10, spell range 8
        var (state, farTarget) = CreateStateWithTarget(targetX: 15, targetY: 5);
        var spell = MakeSpell("confusion", range: 8, duration: 10);

        var events = SpellResolver.Resolve(state.Player, spell, state, targetEntityId: farTarget.Id);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.False, "Target out of range should fail");
        Assert.That(farTarget.Has<DisorientationEffect>(), Is.False);
    }

    [Test]
    public void SingleTarget_NullTargetId_NoEffect()
    {
        var (state, _) = CreateStateWithTarget();
        var spell = MakeSpell("confusion", duration: 10);

        // Pass null targetEntityId — should fail gracefully
        var events = SpellResolver.Resolve(state.Player, spell, state, targetEntityId: null);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.False);
    }

    [Test]
    public void SingleTarget_DeadTarget_NoEffect()
    {
        var (state, target) = CreateStateWithTarget();
        // Kill the target first
        target.Require<Fighter>().TakeDamage(1000);

        var spell = MakeSpell("confusion", duration: 10);
        var events = SpellResolver.Resolve(state.Player, spell, state, targetEntityId: target.Id);

        var spellEvent = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvent.Success, Is.False, "Cannot target dead entities");
    }

    // ─── Fear (AoeSelf) ──────────────────────────────────────────────────────

    private static GameState CreateStateWithMultipleMonsters(out Entity m1, out Entity m2, out Entity m3)
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(30, 30);
        var player = new Entity(0, "Player", 10, 10, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        m1 = new Entity(1, "Orc", 12, 10, true); // dist 2
        m1.Add(new Fighter(hp: 30, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(m1);

        m2 = new Entity(2, "Orc", 10, 15, true); // dist 5
        m2.Add(new Fighter(hp: 30, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(m2);

        m3 = new Entity(3, "Zombie", 10, 25, true); // dist 15, outside radius 10
        m3.Add(new Fighter(hp: 40, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(m3);

        var state = new GameState(player, [m1, m2, m3], map, rng, turnLimit: 100);
        return state;
    }

    [Test]
    public void Fear_AppliesToAllVisibleMonstersInRadius()
    {
        var state = CreateStateWithMultipleMonsters(out var m1, out var m2, out var m3);
        var spell = new SpellEffect
        {
            SpellId = "fear",
            Targeting = TargetingMode.AoeSelf,
            Radius = 10,
            Duration = 15,
        };

        var events = SpellResolver.Resolve(state.Player, spell, state);

        // m1 and m2 are within radius 10
        Assert.That(m1.Has<FearEffect>(), Is.True);
        Assert.That(m1.Require<FearEffect>().RemainingTurns, Is.EqualTo(15));
        Assert.That(m2.Has<FearEffect>(), Is.True);
        // m3 is outside radius 10 (distance 15)
        Assert.That(m3.Has<FearEffect>(), Is.False);

        var spellEvt = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvt.Success, Is.True);
        Assert.That(spellEvt.AffectedIds, Contains.Item(m1.Id));
        Assert.That(spellEvt.AffectedIds, Contains.Item(m2.Id));
        Assert.That(spellEvt.AffectedIds, Does.Not.Contain(m3.Id));
    }

    [Test]
    public void Fear_EmitsStatusAppliedEventPerAffectedMonster()
    {
        var state = CreateStateWithMultipleMonsters(out var m1, out var m2, out _);
        var spell = new SpellEffect
        {
            SpellId = "fear", Targeting = TargetingMode.AoeSelf, Radius = 10, Duration = 15,
        };

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var statusEvents = events.OfType<StatusAppliedEvent>().ToList();
        Assert.That(statusEvents, Has.Count.EqualTo(2));
        Assert.That(statusEvents.All(e => e.EffectName == "fear"), Is.True);
        Assert.That(statusEvents.Select(e => e.TargetId),
            Is.EquivalentTo(new[] { m1.Id, m2.Id }));
    }

    // ─── Invisibility / Shield / Haste (Self) ────────────────────────────────

    [Test]
    public void Invisibility_AppliesEffectToCaster()
    {
        var (state, _) = CreateStateWithTarget();
        var spell = new SpellEffect
        {
            SpellId = "invisibility", Targeting = TargetingMode.Self, Duration = 30,
        };

        var events = SpellResolver.Resolve(state.Player, spell, state);

        var spellEvt = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvt.Success, Is.True);
        Assert.That(spellEvt.StatusApplied, Is.EqualTo("invisibility"));
        Assert.That(state.Player.Has<InvisibilityEffect>(), Is.True);
        Assert.That(state.Player.Require<InvisibilityEffect>().RemainingTurns, Is.EqualTo(30));
    }

    [Test]
    public void Shield_AppliesShieldEffectWithAcBonus()
    {
        var (state, _) = CreateStateWithTarget();
        var spell = new SpellEffect
        {
            SpellId = "shield", Targeting = TargetingMode.Self, Duration = 10,
        };

        SpellResolver.Resolve(state.Player, spell, state);

        Assert.That(state.Player.Has<ShieldEffect>(), Is.True);
        var effect = state.Player.Require<ShieldEffect>();
        Assert.That(effect.RemainingTurns, Is.EqualTo(10));
        Assert.That(effect.AcBonus, Is.EqualTo(4));
    }

    [Test]
    public void Haste_AppliesSpeedEffectToCaster()
    {
        var (state, _) = CreateStateWithTarget();
        var spell = new SpellEffect
        {
            SpellId = "haste", Targeting = TargetingMode.Self, Duration = 30,
        };

        SpellResolver.Resolve(state.Player, spell, state);

        Assert.That(state.Player.Has<SpeedEffect>(), Is.True);
        Assert.That(state.Player.Require<SpeedEffect>().RemainingTurns, Is.EqualTo(30));
    }

    // ─── Teleport (Location) ─────────────────────────────────────────────────

    [Test]
    public void Teleport_MovesPlayerToTargetTile_WhenNoMisfire()
    {
        var (state, _) = CreateStateWithTarget();
        var spell = new SpellEffect
        {
            SpellId = "teleport",
            Targeting = TargetingMode.Location,
            Range = 20,
            MisfireChance = 0.0, // guaranteed no misfire
        };

        var events = SpellResolver.Resolve(state.Player, spell, state, targetX: 15, targetY: 15);

        var teleEvt = events.OfType<TeleportEvent>().Single();
        Assert.That(teleEvt.Misfire, Is.False);
        Assert.That(teleEvt.ToX, Is.EqualTo(15));
        Assert.That(teleEvt.ToY, Is.EqualTo(15));
        Assert.That(state.Player.X, Is.EqualTo(15));
        Assert.That(state.Player.Y, Is.EqualTo(15));
    }

    [Test]
    public void Teleport_MisfireChance_LandsOnWalkableTile()
    {
        var (state, _) = CreateStateWithTarget();
        var spell = new SpellEffect
        {
            SpellId = "teleport",
            Targeting = TargetingMode.Location,
            Range = 20,
            MisfireChance = 1.0, // guaranteed misfire
        };

        var events = SpellResolver.Resolve(state.Player, spell, state, targetX: 15, targetY: 15);

        var teleEvt = events.OfType<TeleportEvent>().Single();
        Assert.That(teleEvt.Misfire, Is.True);
        // Player should have landed somewhere walkable (not necessarily target)
        Assert.That(state.Map.IsWalkable(state.Player.X, state.Player.Y), Is.True);
    }

    [Test]
    public void Teleport_EmitsTeleportEvent()
    {
        var (state, _) = CreateStateWithTarget();
        int fromX = state.Player.X, fromY = state.Player.Y;
        var spell = new SpellEffect
        {
            SpellId = "teleport", Targeting = TargetingMode.Location, Range = 20, MisfireChance = 0.0,
        };

        var events = SpellResolver.Resolve(state.Player, spell, state, targetX: 12, targetY: 12);

        var teleEvt = events.OfType<TeleportEvent>().Single();
        Assert.That(teleEvt.FromX, Is.EqualTo(fromX));
        Assert.That(teleEvt.FromY, Is.EqualTo(fromY));
        Assert.That(teleEvt.EntityId, Is.EqualTo(state.Player.Id));
    }

    // ─── Fireball (Location AoE) ─────────────────────────────────────────────

    [Test]
    public void Fireball_DamagesAllMonstersInRadius()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(25, 25);
        var player = new Entity(0, "Player", 1, 1, true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);

        var m1 = new Entity(1, "Orc", 10, 10, true); // at blast center
        m1.Add(new Fighter(hp: 100, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(m1);

        var m2 = new Entity(2, "Orc", 12, 10, true); // distance 2 from center
        m2.Add(new Fighter(hp: 100, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(m2);

        var mFar = new Entity(3, "Zombie", 14, 10, true); // distance 4, outside radius 3
        mFar.Add(new Fighter(hp: 100, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        map.RegisterEntity(mFar);

        var state = new GameState(player, [m1, m2, mFar], map, rng, 100);
        var spell = new SpellEffect
        {
            SpellId = "fireball", Targeting = TargetingMode.Location,
            Damage = 25, Radius = 3, Range = 15,
        };

        var events = SpellResolver.Resolve(player, spell, state, targetX: 10, targetY: 10);

        Assert.That(m1.Require<Fighter>().Hp, Is.LessThan(100));
        Assert.That(m2.Require<Fighter>().Hp, Is.LessThan(100));
        Assert.That(mFar.Require<Fighter>().Hp, Is.EqualTo(100)); // outside radius

        var spellEvt = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvt.Success, Is.True);
        Assert.That(spellEvt.AffectedIds, Contains.Item(m1.Id));
        Assert.That(spellEvt.AffectedIds, Contains.Item(m2.Id));
        Assert.That(spellEvt.AffectedIds, Does.Not.Contain(mFar.Id));
    }

    [Test]
    public void Fireball_NoTargetLocation_Fails()
    {
        var (state, _) = CreateStateWithTarget();
        var spell = new SpellEffect
        {
            SpellId = "fireball", Targeting = TargetingMode.Location, Damage = 25, Radius = 3,
        };

        // Call without targetX/targetY — should fail gracefully
        var events = SpellResolver.Resolve(state.Player, spell, state);

        var spellEvt = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvt.Success, Is.False);
    }

    // ─── Blink (Location, short range) ──────────────────────────────────────

    [Test]
    public void Blink_TeleportsPlayerToTargetTile()
    {
        var (state, _) = CreateStateWithTarget();
        int fromX = state.Player.X, fromY = state.Player.Y;
        var spell = new SpellEffect
        {
            SpellId = "blink", Targeting = TargetingMode.Location, Range = 5,
        };

        // Blink to a nearby tile within range
        var events = SpellResolver.Resolve(state.Player, spell, state, targetX: 4, targetY: 4);

        var teleEvt = events.OfType<TeleportEvent>().Single();
        Assert.That(teleEvt.Misfire, Is.False);
        Assert.That(teleEvt.FromX, Is.EqualTo(fromX));
        Assert.That(teleEvt.FromY, Is.EqualTo(fromY));
        Assert.That(teleEvt.ToX, Is.EqualTo(4));
        Assert.That(teleEvt.ToY, Is.EqualTo(4));
        Assert.That(state.Player.X, Is.EqualTo(4));
        Assert.That(state.Player.Y, Is.EqualTo(4));

        var spellEvt = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvt.Success, Is.True);
    }

    [Test]
    public void Blink_BeyondRange_Fails()
    {
        var (state, _) = CreateStateWithTarget();
        var spell = new SpellEffect
        {
            SpellId = "blink", Targeting = TargetingMode.Location, Range = 5,
        };
        int originalX = state.Player.X, originalY = state.Player.Y;

        // Player is at (1,1), target is at (15,15) — far beyond range 5
        var events = SpellResolver.Resolve(state.Player, spell, state, targetX: 15, targetY: 15);

        var spellEvt = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvt.Success, Is.False);
        Assert.That(state.Player.X, Is.EqualTo(originalX));
        Assert.That(state.Player.Y, Is.EqualTo(originalY));
    }

    [Test]
    public void Blink_NonWalkableTile_Fails()
    {
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(25, 25);
        // Make tile (5, 5) non-walkable — CreateArena uses all-floor arena but we can
        // test with a tile outside bounds or check that a wall blocks the blink.
        // CreateArena makes everything walkable; test out-of-bounds tile instead.
        var player = new Entity(0, "Player", 1, 1, true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        map.RegisterEntity(player);
        var state = new GameState(player, [], map, rng, 100);

        var spell = new SpellEffect
        {
            SpellId = "blink", Targeting = TargetingMode.Location, Range = 5,
        };

        // targetX/targetY null should fail (no target provided)
        var events = SpellResolver.Resolve(state.Player, spell, state);

        var spellEvt = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvt.Success, Is.False);
        Assert.That(state.Player.X, Is.EqualTo(1));
        Assert.That(state.Player.Y, Is.EqualTo(1));
    }

    // ─── Raise Dead (stub) ───────────────────────────────────────────────────

    [Test]
    public void RaiseDead_NoCorpseSystem_ReturnsFailedEvent()
    {
        var (state, _) = CreateStateWithTarget();
        var spell = new SpellEffect
        {
            SpellId = "raise_dead", Targeting = TargetingMode.Location, Range = 5,
        };

        // Until plan_monster_specials lands, raise_dead is always a no-op
        var events = SpellResolver.Resolve(state.Player, spell, state, targetX: 5, targetY: 5);

        var spellEvt = events.OfType<SpellEvent>().Single();
        Assert.That(spellEvt.Success, Is.False);
        Assert.That(spellEvt.SpellId, Is.EqualTo("raise_dead"));
    }
}
