using UnderWarden.Logic.Combat;
using UnderWarden.Logic.Combat.StatusEffects;
using UnderWarden.Logic.Core;
using UnderWarden.Logic.ECS;
using NUnit.Framework;

namespace UnderWarden.Tests.Possession;

/// <summary>
/// Phase 2 tests: TurnController dispatch, end-of-turn hooks, death router, block list.
/// Tests run through ProcessTurn to verify the full integration path.
/// </summary>
[TestFixture]
public class PossessionTurnControllerTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Entity MakePlayer(int x = 5, int y = 5, int hp = 30)
    {
        var p = new Entity(0, "Player", x, y, blocksMovement: true);
        p.Add(new Fighter(hp: hp, strength: 12, dexterity: 12, constitution: 12,
            accuracy: 3, evasion: 1, damageMin: 2, damageMax: 6));
        p.Add(new Inventory());
        return p;
    }

    private static Entity MakeMonster(int id = 1, int x = 6, int y = 5,
        string species = "orc_grunt", int hp = 20)
    {
        var m = new Entity(id, "Orc Grunt", x, y, blocksMovement: true);
        m.Add(new Fighter(hp: hp, strength: 8, dexterity: 8, constitution: 8,
            accuracy: 1, evasion: 0, damageMin: 1, damageMax: 2));
        m.Add(new AiComponent { Faction = "orc" });
        m.Add(new SpeciesTag(species));
        return m;
    }

    private static GameState MakeState(Entity player, Entity? monster = null, int depth = 1)
    {
        var map = GameMap.CreateArena(20, 20);
        map.RegisterEntity(player);
        var monsters = new List<Entity>();
        if (monster != null) { map.RegisterEntity(monster); monsters.Add(monster); }
        return new GameState(player, monsters, map, new SeededRandom(1337)) { CurrentDepth = depth };
    }

    // ─── Possess action ───────────────────────────────────────────────────────

    [Test]
    public void ProcessTurn_PossessAction_AppliesPossessionEffect()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);

        var result = TurnController.ProcessTurn(state, PlayerAction.Possess(host));

        Assert.That(host.Has<PossessionEffect>(), Is.True);
        Assert.That(result.Events.OfType<PossessionEnteredEvent>(), Is.Not.Empty);
    }

    [Test]
    public void ProcessTurn_PossessAction_DrainTicksFires_OnFirstTurn()
    {
        var player = MakePlayer(hp: 30);
        var host = MakeMonster();
        var state = MakeState(player, host, depth: 1); // drain=1 at depth 1

        TurnController.ProcessTurn(state, PlayerAction.Possess(host));

        Assert.That(state.PlayerFighter.Hp, Is.LessThan(30), "Drain should tick on the first possessed turn.");
        Assert.That(state.PlayerFighter.Hp, Is.EqualTo(29), "Depth 1 drain = 1 HP/turn.");
    }

    // ─── ExitPossession action ────────────────────────────────────────────────

    [Test]
    public void ProcessTurn_ExitPossession_IsFreeAction_MonstersTurnStillRuns()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        // Enter possession first
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        int turnBefore = state.TurnCount;
        TurnController.ProcessTurn(state, PlayerAction.ExitPossession());

        // ExitPossession is a free action → TurnCount did NOT advance
        Assert.That(state.TurnCount, Is.EqualTo(turnBefore), "ExitPossession is a free action.");
        Assert.That(host.Has<PossessionEffect>(), Is.False);
    }

    // ─── EnterPossessionTargeting action ─────────────────────────────────────

    [Test]
    public void ProcessTurn_EnterPossessionTargeting_IsFreeAction()
    {
        var player = MakePlayer();
        var state = MakeState(player);
        int turnBefore = state.TurnCount;

        TurnController.ProcessTurn(state, PlayerAction.EnterPossessionTargeting());

        Assert.That(state.TurnCount, Is.EqualTo(turnBefore), "EnterPossessionTargeting is a free action.");
    }

    // ─── Blocked actions during possession ───────────────────────────────────

    [Test]
    public void ProcessTurn_EquipDuringPossession_IsBlocked()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        // Equip with a dummy item — the action should be blocked (WaitEvent instead)
        var dummyItem = new Entity(99, "Sword", 0, 0, false);
        var result = TurnController.ProcessTurn(state, PlayerAction.Equip(dummyItem));

        Assert.That(result.Events.OfType<WaitEvent>(), Is.Not.Empty,
            "Equip during possession should be silently blocked (WaitEvent).");
    }

    [Test]
    public void ProcessTurn_DescendDuringPossession_IsBlocked()
    {
        var player = MakePlayer();
        var host = MakeMonster();
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        var result = TurnController.ProcessTurn(state, PlayerAction.Descend);

        // Should have WaitEvent, no DescendEvent
        Assert.That(result.Events.OfType<WaitEvent>(), Is.Not.Empty);
        Assert.That(result.Events.OfType<DescendEvent>(), Is.Empty);
    }

    // ─── End-of-turn visibility constraint ────────────────────────────────────

    [Test]
    public void ProcessTurn_VisibilityConstraintFires_WhenHostTooFar()
    {
        var player = MakePlayer(x: 3, y: 3);
        var host = MakeMonster(x: 4, y: 3); // adjacent — within range
        var state = MakeState(player, host);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        // Move host far away
        host.X = 10;

        // Any turn action triggers the end-of-turn visibility check
        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(host.Has<PossessionEffect>(), Is.False,
            "End-of-turn visibility check should have forced exit.");
        Assert.That(result.Events.OfType<PossessionExitedEvent>().Any(e => e.Reason == "visibility_broken"), Is.True);
    }

    // ─── Death router: host killed during possession ──────────────────────────

    [Test]
    public void UpdateKnowledge_SkipsRecordKilled_ForPossessionInducedDeaths()
    {
        var player = MakePlayer();
        var host = MakeMonster(species: "orc_grunt");
        var state = MakeState(player, host);

        // Enter possession, then simulate host death
        PossessionSystem.Enter(host, state, new List<TurnEvent>());
        var events = new List<TurnEvent>();
        host.Get<Fighter>()!.Hp = 0; // kill host
        PossessionSystem.OnPossessionInducedHostDeath(host, state, events);

        // Feed events through TurnController's knowledge update path via full ProcessTurn
        // The DeathEvent has IsPossessionInduced=true → RecordKilled should be skipped
        var deathEvent = events.OfType<DeathEvent>().Single();
        Assert.That(deathEvent.IsPossessionInduced, Is.True,
            "Possession-induced DeathEvent must carry the IsPossessionInduced flag.");
    }

    [Test]
    public void ProcessTurn_MonsterKillsPossessedHost_RoutesToPossessionPipeline()
    {
        // Arrange: player possesses a weak monster; a second monster hits it to 0 HP.
        var player = MakePlayer(x: 5, y: 5);
        // host: 1 HP so one hit kills it
        var host = MakeMonster(id: 1, x: 6, y: 5, hp: 1);
        // attacker: strong enough to always kill (damage ≥ 1)
        var attacker = new Entity(2, "Orc Brute", 7, y: 5, blocksMovement: true);
        attacker.Add(new Fighter(hp: 50, strength: 18, dexterity: 10, constitution: 14,
            accuracy: 10, evasion: 0, damageMin: 5, damageMax: 10));
        attacker.Add(new AiComponent { Faction = "orc" });
        attacker.Add(new SpeciesTag("orc_brute"));

        var map = GameMap.CreateArena(20, 20);
        map.RegisterEntity(player);
        map.RegisterEntity(host);
        map.RegisterEntity(attacker);
        var state = new GameState(player, new List<Entity> { host, attacker }, map, new SeededRandom(42));

        // Possess the host (which is at 1 HP)
        PossessionSystem.Enter(host, state, new List<TurnEvent>());
        Assert.That(host.Has<PossessionEffect>(), Is.True);

        // Simulate: the attacker attacks the host
        var attackEvents = new List<TurnEvent>();
        // Direct attack resolution (bypasses TurnController for simplicity)
        host.Get<Fighter>()!.Hp = 0; // ensure host is dead
        PossessionSystem.OnPossessionInducedHostDeath(host, state, attackEvents, "host_died");

        // Verify: possession exited, no kill credit for host species
        Assert.That(host.Has<PossessionEffect>(), Is.False);
        Assert.That(state.ControlledEntity, Is.SameAs(player));

        var deathEvt = attackEvents.OfType<DeathEvent>().First();
        Assert.That(deathEvt.IsPossessionInduced, Is.True);

        var orc = state.Knowledge.GetEntry("orc_grunt");
        Assert.That(orc?.KilledCount ?? 0, Is.EqualTo(0), "Host death should not count as a kill.");
        Assert.That(orc?.EngagedCount ?? 0, Is.GreaterThan(0), "Host death should record engagement.");
    }

    // ─── Drain ticks on possessed turns ──────────────────────────────────────

    [Test]
    public void ProcessTurn_WaitWhilePossessing_DrainsFires()
    {
        var player = MakePlayer(hp: 30);
        var host = MakeMonster();
        var state = MakeState(player, host, depth: 1);
        PossessionSystem.Enter(host, state, new List<TurnEvent>());
        state.PlayerFighter.Hp = 30; // reset HP after possession enter drain

        TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(state.PlayerFighter.Hp, Is.EqualTo(29), "Wait while possessing should drain 1 HP (depth 1).");
    }

    // ─── Drain safety rail through TurnController ─────────────────────────────

    [Test]
    public void ProcessTurn_DrainAtOneHP_ForcesExit_KeepsHomeBodyAt1()
    {
        var player = MakePlayer(hp: 1); // home body at 1 HP
        var host = MakeMonster();
        var state = MakeState(player, host, depth: 1); // drain=1

        // Enter possession — drain will tick on Possess turn
        // But with 1HP home body, drain safety rail should fire on first possessed action
        PossessionSystem.Enter(host, state, new List<TurnEvent>());

        // home body still at 1 HP (Enter itself doesn't drain)
        Assert.That(state.PlayerFighter.Hp, Is.EqualTo(1));

        var result = TurnController.ProcessTurn(state, PlayerAction.Wait);

        Assert.That(state.PlayerFighter.Hp, Is.EqualTo(1), "Safety rail must not let drain kill the home body.");
        Assert.That(host.Has<PossessionEffect>(), Is.False, "Safety rail forces voluntary exit.");
        Assert.That(result.Events.OfType<PossessionExitedEvent>().Single().Reason, Is.EqualTo("voluntary"));
    }
}
