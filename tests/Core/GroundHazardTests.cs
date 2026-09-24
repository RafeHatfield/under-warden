using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for the ground hazard system: creation, damage decay, entity targeting,
/// and integration via TurnController (fireball + dragon fart leave hazards).
/// </summary>
[TestFixture]
public class GroundHazardTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static GameState CreateState(int seed = 1337)
    {
        var rng    = new SeededRandom(seed);
        var map    = GameMap.CreateArena(20, 20);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 54, strength: 14, dexterity: 14, constitution: 14,
            accuracy: 3, evasion: 0, damageMin: 1, damageMax: 4));
        player.Add(new Inventory());
        map.RegisterEntity(player);
        return new GameState(player, new List<Entity>(), map, rng);
    }

    private static Entity AddMonster(GameState state, int id, int x, int y, int hp = 50)
    {
        var m = new Entity(id, "Orc", x, y, blocksMovement: true);
        m.Add(new Fighter(hp: hp, strength: 10, dexterity: 10, constitution: 10,
            accuracy: 2, evasion: 0, damageMin: 2, damageMax: 4));
        // Prevent AI movement so monster stays on hazard tile during TickEnvironment
        m.GetOrAdd<SleepEffect>().RemainingTurns = 100;
        state.Monsters.Add(m);
        state.Map.RegisterEntity(m);
        return m;
    }

    private static Entity MakeFireballScroll()
    {
        var scroll = new Entity(99, "Scroll of Fireball", 0, 0);
        scroll.Add(new SpellEffect { SpellId = "fireball", Damage = 25, Radius = 3 });
        scroll.Add(new Consumable { StackSize = 1 });
        scroll.Add(new ItemTag("scroll_of_fireball"));
        return scroll;
    }

    private static Entity MakeDragonFartScroll()
    {
        var scroll = new Entity(98, "Scroll of Dragon Fart", 0, 0);
        scroll.Add(new SpellEffect { SpellId = "dragon_fart", Duration = 3, Range = 8 });
        scroll.Add(new Consumable { StackSize = 1 });
        scroll.Add(new ItemTag("scroll_of_dragon_fart"));
        return scroll;
    }

    // ─── GroundHazardManager unit tests ─────────────────────────────────────

    [Test]
    public void AddHazard_CreatesHazardAtPosition()
    {
        var manager = new GroundHazardManager();
        manager.AddHazard(HazardType.Fire, 3, 4, baseDamage: 3, maxDuration: 3);

        Assert.That(manager.Hazards.ContainsKey((3, 4)));
        var h = manager.Hazards[(3, 4)];
        Assert.That(h.Type, Is.EqualTo(HazardType.Fire));
        Assert.That(h.BaseDamage, Is.EqualTo(3));
        Assert.That(h.MaxDuration, Is.EqualTo(3));
        Assert.That(h.RemainingTurns, Is.EqualTo(3));
    }

    [Test]
    public void AddHazard_ReplacesExistingAtSameTile()
    {
        var manager = new GroundHazardManager();
        manager.AddHazard(HazardType.Fire, 3, 4, baseDamage: 3, maxDuration: 3);
        // Age the first hazard
        manager.Hazards[(3, 4)].RemainingTurns = 1;
        // Add a new one at the same tile — should reset to full duration
        manager.AddHazard(HazardType.Fire, 3, 4, baseDamage: 3, maxDuration: 3);

        Assert.That(manager.Hazards[(3, 4)].RemainingTurns, Is.EqualTo(3));
        Assert.That(manager.Hazards.Count, Is.EqualTo(1)); // no stacking
    }

    [Test]
    public void DamageDecay_FireHazard_3Turns()
    {
        // Fireball: base=3, duration=3 → 3 / 2 / 1 on turns 1/2/3
        var h = new GroundHazard(HazardType.Fire, 0, 0, baseDamage: 3, maxDuration: 3);
        Assert.That(h.CurrentDamage, Is.EqualTo(3)); // turn 1: 3*3/3 = 3
        h.RemainingTurns = 2;
        Assert.That(h.CurrentDamage, Is.EqualTo(2)); // turn 2: 3*2/3 = 2
        h.RemainingTurns = 1;
        Assert.That(h.CurrentDamage, Is.EqualTo(1)); // turn 3: 3*1/3 = 1
        h.RemainingTurns = 0;
        Assert.That(h.CurrentDamage, Is.EqualTo(0)); // expired
    }

    [Test]
    public void DamageDecay_PoisonGasHazard_5Turns()
    {
        // Dragon fart: base=6, duration=5 → 6/4/3/2/1
        var h = new GroundHazard(HazardType.PoisonGas, 0, 0, baseDamage: 6, maxDuration: 5);
        Assert.That(h.CurrentDamage, Is.EqualTo(6)); // 6*5/5
        h.RemainingTurns = 4;
        Assert.That(h.CurrentDamage, Is.EqualTo(4)); // 6*4/5 = 4.8 → 4
        h.RemainingTurns = 3;
        Assert.That(h.CurrentDamage, Is.EqualTo(3)); // 6*3/5 = 3.6 → 3
        h.RemainingTurns = 2;
        Assert.That(h.CurrentDamage, Is.EqualTo(2)); // 6*2/5 = 2.4 → 2
        h.RemainingTurns = 1;
        Assert.That(h.CurrentDamage, Is.EqualTo(1)); // 6*1/5 = 1.2 → 1
    }

    [Test]
    public void RemoveExpired_PurgesZeroRemainingTurns()
    {
        var manager = new GroundHazardManager();
        manager.AddHazard(HazardType.Fire, 1, 1, 3, 3);
        manager.AddHazard(HazardType.Fire, 2, 2, 3, 3);
        manager.Hazards[(1, 1)].RemainingTurns = 0;

        manager.RemoveExpired();

        Assert.That(manager.Hazards.ContainsKey((1, 1)), Is.False);
        Assert.That(manager.Hazards.ContainsKey((2, 2)), Is.True);
    }

    // ─── TickEnvironment integration tests ──────────────────────────────────

    [Test]
    public void TickEnvironment_DamagesMonsterOnHazardTile()
    {
        var state   = CreateState();
        var monster = AddMonster(state, 1, 8, 8, hp: 20);
        // justPlaced:false — simulates a hazard that was placed a previous turn
        state.GroundHazards.AddHazard(HazardType.Fire, 8, 8, baseDamage: 3, maxDuration: 3, justPlaced: false);

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        // Should have a DotDamageEvent for the monster
        var dots = result.Events.OfType<DotDamageEvent>().ToList();
        Assert.That(dots.Count, Is.EqualTo(1));
        Assert.That(dots[0].EntityId, Is.EqualTo(monster.Id));
        Assert.That(dots[0].EffectName, Is.EqualTo("fire"));
        Assert.That(dots[0].Damage, Is.EqualTo(3)); // full first tick
    }

    [Test]
    public void TickEnvironment_DamagesPlayer_WhenPlayerOnHazardTile()
    {
        var state = CreateState();
        // Player is at (5,5) — place hazard there (already aged, ready to tick)
        state.GroundHazards.AddHazard(HazardType.Fire, 5, 5, baseDamage: 3, maxDuration: 3, justPlaced: false);

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var dots = result.Events.OfType<DotDamageEvent>()
            .Where(d => d.EntityId == state.Player.Id).ToList();
        Assert.That(dots.Count, Is.EqualTo(1));
        Assert.That(dots[0].Damage, Is.EqualTo(3));
    }

    [Test]
    public void TickEnvironment_HazardAges_AndExpires()
    {
        var state = CreateState();
        state.GroundHazards.AddHazard(HazardType.Fire, 8, 8, baseDamage: 3, maxDuration: 3, justPlaced: false);

        // 3 turns — hazard should be gone after the third
        TurnController.ProcessTurn(state, PlayerAction.Wait); // RemainingTurns → 2
        TurnController.ProcessTurn(state, PlayerAction.Wait); // → 1
        TurnController.ProcessTurn(state, PlayerAction.Wait); // → 0 → expired

        Assert.That(state.GroundHazards.Hazards.Count, Is.EqualTo(0));
    }

    [Test]
    public void TickEnvironment_JustPlaced_SkipsFirstTick()
    {
        // Hazard placed this turn (e.g. by fireball) should not deal damage until next turn.
        var state   = CreateState();
        var monster = AddMonster(state, 1, 8, 8, hp: 20);
        state.GroundHazards.AddHazard(HazardType.Fire, 8, 8, baseDamage: 3, maxDuration: 3); // justPlaced=true

        // Same turn as placement — no damage
        var r1 = TurnController.ProcessTurn(state, PlayerAction.Wait);
        Assert.That(r1.Events.OfType<DotDamageEvent>().Count(), Is.EqualTo(0));

        // Next turn — first real tick, full damage
        var r2 = TurnController.ProcessTurn(state, PlayerAction.Wait);
        var dots = r2.Events.OfType<DotDamageEvent>().ToList();
        Assert.That(dots.Count, Is.EqualTo(1));
        Assert.That(dots[0].Damage, Is.EqualTo(3));
    }

    [Test]
    public void TickEnvironment_KillsMonster_AndEmitsDeathEvent()
    {
        var state   = CreateState();
        var monster = AddMonster(state, 1, 8, 8, hp: 2); // low HP — one tick kills it
        state.GroundHazards.AddHazard(HazardType.Fire, 8, 8, baseDamage: 3, maxDuration: 3, justPlaced: false);

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(result.Events.OfType<DeathEvent>().Any(d => d.ActorId == monster.Id));
        Assert.That(monster.Require<Fighter>().IsAlive, Is.False);
    }

    [Test]
    public void TickEnvironment_DoesNotDamage_EntitiesNotOnHazardTile()
    {
        var state   = CreateState();
        var monster = AddMonster(state, 1, 10, 10, hp: 20); // far from hazard
        state.GroundHazards.AddHazard(HazardType.Fire, 8, 8, baseDamage: 3, maxDuration: 3, justPlaced: false);

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        var dots = result.Events.OfType<DotDamageEvent>().ToList();
        Assert.That(dots.Count, Is.EqualTo(0));
    }

    [Test]
    public void TickEnvironment_DamageDecaysEachTurn()
    {
        var state   = CreateState();
        var monster = AddMonster(state, 1, 8, 8, hp: 100);
        state.GroundHazards.AddHazard(HazardType.Fire, 8, 8, baseDamage: 3, maxDuration: 3, justPlaced: false);

        // Turn 1: damage = 3
        var r1 = TurnController.ProcessTurn(state, PlayerAction.Wait);
        var d1 = r1.Events.OfType<DotDamageEvent>().First(d => d.EntityId == monster.Id).Damage;

        // Turn 2: damage = 2
        var r2 = TurnController.ProcessTurn(state, PlayerAction.Wait);
        var d2 = r2.Events.OfType<DotDamageEvent>().First(d => d.EntityId == monster.Id).Damage;

        // Turn 3: damage = 1
        var r3 = TurnController.ProcessTurn(state, PlayerAction.Wait);
        var d3 = r3.Events.OfType<DotDamageEvent>().First(d => d.EntityId == monster.Id).Damage;

        Assert.That(d1, Is.EqualTo(3));
        Assert.That(d2, Is.EqualTo(2));
        Assert.That(d3, Is.EqualTo(1));
    }

    // ─── Spell integration: fireball creates fire hazards ───────────────────

    [Test]
    public void Fireball_CreatesFireHazards_OnBlastTiles()
    {
        var state  = CreateState();
        var scroll = MakeFireballScroll();
        state.PlayerInventory!.Add(scroll);

        TurnController.ProcessTurn(state,
            PlayerAction.CastSpell(scroll, targetX: 10, targetY: 10));

        // Should have fire hazards in a radius-3 Chebyshev area around (10,10)
        Assert.That(state.GroundHazards.Hazards.Count, Is.GreaterThan(0));
        Assert.That(state.GroundHazards.Hazards.Values.All(h => h.Type == HazardType.Fire));
        // Target tile must be a hazard
        Assert.That(state.GroundHazards.Hazards.ContainsKey((10, 10)));
        // Duration = 3, base damage = 3
        var centre = state.GroundHazards.Hazards[(10, 10)];
        Assert.That(centre.MaxDuration, Is.EqualTo(3));
        Assert.That(centre.BaseDamage,  Is.EqualTo(3));
    }

    [Test]
    public void DragonFart_CreatesPoisonGasHazards_OnConeTiles()
    {
        var state  = CreateState();
        var scroll = MakeDragonFartScroll();
        state.PlayerInventory!.Add(scroll);

        TurnController.ProcessTurn(state,
            PlayerAction.CastSpell(scroll, targetX: 10, targetY: 5));

        Assert.That(state.GroundHazards.Hazards.Count, Is.GreaterThan(0));
        Assert.That(state.GroundHazards.Hazards.Values.All(h => h.Type == HazardType.PoisonGas));
        // All poison gas hazards should have duration=5, base=6
        var first = state.GroundHazards.Hazards.Values.First();
        Assert.That(first.MaxDuration, Is.EqualTo(5));
        Assert.That(first.BaseDamage,  Is.EqualTo(6));
    }
}
