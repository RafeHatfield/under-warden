using UnderWarden.Logic.AI;
using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Core;

/// <summary>
/// Tests for Orc Chieftain Rally lifecycle: rally ends when chieftain takes attack damage,
/// fear cleanse at rally time, and DOT/non-chieftain damage does not break rally.
///
/// PoC reference: orc_chieftain_ai.py lines 108-202; fighter.py lines 593-611.
/// </summary>
[TestFixture]
public class ChieftainRallyLifecycleTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static Entity MakeChieftain(int id = 1, int x = 10, int y = 5)
    {
        var e = new Entity(id, "Orc Chieftain", x, y, blocksMovement: true);
        e.Add(new Fighter(hp: 35, strength: 14, dexterity: 10, constitution: 14,
            accuracy: 4, evasion: 2, damageMin: 4, damageMax: 8));
        e.Add(new AiComponent { AiType = "orc_chieftain", Faction = "orc" });
        e.Add(new AlertedState { LastKnownPlayerX = 5, LastKnownPlayerY = 5, TurnsUntilDeaggro = 20 });
        e.Add(new OrcChieftainComponent { RallyMinAllies = 2 });
        return e;
    }

    private static Entity MakeOrcAlly(int id, int x, int y)
    {
        var e = new Entity(id, "Orc Grunt", x, y, blocksMovement: true);
        e.Add(new Fighter(hp: 20, strength: 12, dexterity: 10, constitution: 12,
            accuracy: 3, evasion: 1, damageMin: 2, damageMax: 5));
        e.Add(new AiComponent { AiType = "basic", Faction = "orc" });
        e.Add(new AlertedState { LastKnownPlayerX = 5, LastKnownPlayerY = 5, TurnsUntilDeaggro = 20 });
        return e;
    }

    private static (GameState state, Entity player, Entity chieftain, List<Entity> allies)
        CreateArenaWithRally(int seed = 1337)
    {
        var chieftain = MakeChieftain(id: 1);
        var ally1 = MakeOrcAlly(id: 2, x: 8, y: 5);
        var ally2 = MakeOrcAlly(id: 3, x: 9, y: 5);
        var allMonsters = new List<Entity> { chieftain, ally1, ally2 };

        var rng = new SeededRandom(seed);
        var map = GameMap.CreateArena(20, 20);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 3, damageMax: 5));
        foreach (var m in allMonsters) map.RegisterEntity(m);
        map.RegisterEntity(player);

        var state = new GameState(player, allMonsters, map, rng, turnLimit: 200);

        // Fire rally by running one chieftain turn.
        OrcChieftainAI.Decide(chieftain, state);

        return (state, player, chieftain, new List<Entity> { ally1, ally2 });
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Test]
    public void Rally_EndsOnChieftainAttackDamage()
    {
        // When the chieftain takes attack damage > 0, RallyEffect is removed from all carriers.
        // Uses CreateArenaWithRally (chieftain at distance 5 from player) then verifies
        // that the interrupt hook fires correctly via OnAttackDamageTaken's internal logic.
        //
        // We trigger the interrupt by directly applying a RallyEffect with ChieftainId set,
        // then calling ProcessTurn with the player attacking the chieftain — verified by
        // that the chieftain is adjacent.
        var chieftain = MakeChieftain(id: 1, x: 10, y: 5); // far from player during rally
        var ally1 = MakeOrcAlly(id: 2, x: 8, y: 5);
        var ally2 = MakeOrcAlly(id: 3, x: 9, y: 5);
        var allMonsters = new List<Entity> { chieftain, ally1, ally2 };

        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 14, dexterity: 20, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 5, damageMax: 8));
        foreach (var m in allMonsters) map.RegisterEntity(m);
        map.RegisterEntity(player);
        var state = new GameState(player, allMonsters, map, rng, turnLimit: 200);

        // Fire rally at distance 5 (chieftain and allies in range, player out of panic zone).
        OrcChieftainAI.Decide(chieftain, state);
        Assert.That(chieftain.Has<RallyEffect>(), Is.True, "Chieftain must have rally");
        Assert.That(ally1.Has<RallyEffect>(), Is.True, "Ally 1 must have rally");
        Assert.That(ally2.Has<RallyEffect>(), Is.True, "Ally 2 must have rally");

        // Move chieftain adjacent to player for the attack test.
        map.UnregisterEntity(chieftain);
        chieftain.X = 6; chieftain.Y = 5;
        map.RegisterEntity(chieftain);

        // Player attacks chieftain — must hit and deal damage to trigger interrupt.
        // With dexterity=20 (DexMod=+5) vs chieftain AC=10: d20+5>=10 → always hits.
        TurnController.ProcessTurn(state, PlayerAction.Attack(chieftain));

        // Rally should be broken.
        Assert.That(chieftain.Has<RallyEffect>(), Is.False,
            "Chieftain RallyEffect should end after taking attack damage");
        Assert.That(ally1.Has<RallyEffect>(), Is.False,
            "Ally 1 RallyEffect should end when chieftain is hit");
        Assert.That(ally2.Has<RallyEffect>(), Is.False,
            "Ally 2 RallyEffect should end when chieftain is hit");
    }

    [Test]
    public void Rally_DoesNotEndOnDotDamage()
    {
        // DOT damage (e.g. BurningEffect tick) should NOT break rally.
        var (state, player, chieftain, allies) = CreateArenaWithRally();

        Assert.That(chieftain.Has<RallyEffect>(), Is.True, "Rally must be active before test");

        // Apply burning to chieftain and process a turn.
        StatusEffectProcessor.ApplyEffect<BurningEffect>(chieftain, 3);
        var events = new List<TurnEvent>();
        // DOT fires in ProcessTurnStart — this is NOT attack damage.
        StatusEffectProcessor.ProcessTurnStart(chieftain, events, turnCount: 1, state: state);

        // Rally should still be active after DOT tick.
        Assert.That(chieftain.Has<RallyEffect>(), Is.True,
            "RallyEffect should NOT end from DOT damage (burning tick is not attack damage)");
        Assert.That(allies[0].Has<RallyEffect>(), Is.True, "Ally rally intact after DOT");
        Assert.That(allies[1].Has<RallyEffect>(), Is.True, "Ally rally intact after DOT");
    }

    [Test]
    public void Rally_CleansesFearFromAllies()
    {
        // At rally time, FearEffect is removed from any rallied ally.
        var chieftain = MakeChieftain(id: 1);
        var ally1 = MakeOrcAlly(id: 2, x: 8, y: 5);
        var ally2 = MakeOrcAlly(id: 3, x: 9, y: 5);

        // Pre-apply fear to ally1.
        ally1.Add(new FearEffect { RemainingTurns = 5 });
        Assert.That(ally1.Has<FearEffect>(), Is.True, "Ally1 should have FearEffect before rally");

        var allMonsters = new List<Entity> { chieftain, ally1, ally2 };
        var rng = new SeededRandom(1337);
        var map = GameMap.CreateArena(20, 20);
        var player = new Entity(0, "Player", 5, 5, blocksMovement: true);
        player.Add(new Fighter(hp: 100, strength: 12, dexterity: 14, constitution: 12,
            accuracy: 10, evasion: 0, damageMin: 3, damageMax: 5));
        foreach (var m in allMonsters) map.RegisterEntity(m);
        map.RegisterEntity(player);
        var state = new GameState(player, allMonsters, map, rng, turnLimit: 200);

        // Rally fires.
        OrcChieftainAI.Decide(chieftain, state);

        // RallyEffect applied, FearEffect should be cleansed.
        Assert.That(ally1.Has<RallyEffect>(), Is.True, "Ally1 should have RallyEffect");
        Assert.That(ally1.Has<FearEffect>(), Is.False,
            "FearEffect should be cleansed from ally when rally is applied");
        // Non-feared ally unaffected.
        Assert.That(ally2.Has<FearEffect>(), Is.False, "Ally2 had no fear, still no fear");
    }

    [Test]
    public void Rally_DoesNotRefireAfterEnding()
    {
        // After rally ends (chieftain hit), RallyCried stays true — no second rally.
        // Uses CreateArenaWithRally to reliably fire the initial rally (chieftain far from player).
        var (state, player, chieftain, allies) = CreateArenaWithRally();

        // Verify rally fired.
        Assert.That(chieftain.Has<RallyEffect>(), Is.True, "Rally must fire initially");

        // Manually remove the rally effect as if the chieftain was hit (simulating the interrupt
        // without needing the player to actually attack across a large map).
        chieftain.Remove<RallyEffect>();
        foreach (var ally in allies)
            ally.Remove<RallyEffect>();

        // Chieftain decides again — RallyCried=true, so it should NOT re-rally.
        OrcChieftainAI.Decide(chieftain, state);
        Assert.That(chieftain.Has<RallyEffect>(), Is.False,
            "Rally should not re-fire after ending — RallyCried stays true regardless of ally count");
    }

    [Test]
    public void Rally_DoesNotEndOnAllyDamage()
    {
        // Damaging a rallied orc ally (not the chieftain) should not break the rally.
        var (state, player, chieftain, allies) = CreateArenaWithRally();

        Assert.That(allies[0].Has<RallyEffect>(), Is.True, "Ally should be rallied");

        // Deal damage to the ally directly via TakeDamage (not attack path).
        // The interrupt hook in OnAttackDamageTaken only fires for the chieftain.
        allies[0].Require<Fighter>().TakeDamage(3);
        var events = new List<TurnEvent>();
        StatusEffectProcessor.OnDamageTaken(allies[0], events);

        // Rally on chieftain and other allies should be unaffected.
        Assert.That(chieftain.Has<RallyEffect>(), Is.True,
            "Chieftain rally should persist when an ally takes damage");
        Assert.That(allies[1].Has<RallyEffect>(), Is.True,
            "Other ally rally should persist when a different ally takes damage");
    }
}
